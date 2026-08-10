using Mana.Code;
using Mana.Code.Materials;
using Mana.Code.ManaShaders;
using Mana.Code.Shapes;
using ManaGeoTools.Code.Geo;
using ManaGeoTools.Code.Planet;

namespace ManaGeoTools.Code.Region
{
    /// <summary>
    /// A flat XZ terrain tiling for one RegionNode, built in a local east-north-up tangent plane centered
    /// on the region's lat/long at 1 scene unit == 1 real meter -- the cartesian counterpart to
    /// PlanetPatchMesh, which puts the same Terrarium elevation data on a (heavily shrunk) sphere.
    ///
    /// Axes: +X is east, +Y is up, +Z is SOUTH. That ordering is what makes the basis right-handed with
    /// Y up (east x up == south), and it puts north at -Z, i.e. straight ahead of a default-oriented
    /// camera looking down -Z.
    ///
    /// The surface is a HEXAGONAL tiling rather than a quad grid (see RegionHexGrid), and each hex is a
    /// plateau at its own sampled elevation joined to its neighbours by a rim of wall quads -- a boardgame
    /// / Civ read rather than a smooth heightfield. StepStrength dials continuously between the two: at 1
    /// every hex is dead flat and the walls carry the whole elevation change, at 0 the hex corners settle
    /// onto the shared average and the surface becomes continuous again, merely hex-FACETED. Nothing about
    /// the underlying data changes with it, only how the same samples are drawn.
    ///
    /// Elevation is stored relative to GroundElevationMeters (the real elevation at the region's exact
    /// center) rather than absolute, so the node's own origin sits on the ground instead of the mesh
    /// floating hundreds of meters up whenever the region happens to be inland. Vertex color still uses
    /// ABSOLUTE elevation, so the biome ramp matches what the planet shows for the same place.
    /// </summary>
    public sealed class RegionTerrainMesh : ManaProceduralMesh
    {
        /// <summary>
        /// Hex radius as a multiple of the source sample spacing, when the caller doesn't name one. At 1.0
        /// a hex covers about 2.6 source samples, which is small enough that nothing in the elevation data
        /// is smoothed away but large enough that the tiling reads as hexagons from a normal viewing
        /// distance rather than as noise.
        /// </summary>
        public const float DefaultHexRadiusInSamples = 1.0f;

        /// <summary>
        /// Ceiling on hex count, applied by growing the hexes rather than by cropping the region. A hex
        /// costs roughly 13 vertices and 18 triangles against a quad grid cell's 1 and 2, so an
        /// unconstrained small radius over the largest allowed 64km box would run to millions of both.
        /// </summary>
        public const int MaxHexCells = 120_000;

        // Below this the walls are shorter than the noise in the source data, so they cost geometry
        // without being visible -- the surface is just hex-faceted at that point, and is built as bare
        // top faces with no rim at all.
        private const float MinStepStrengthForWalls = 0.01f;

        public LatLong Center { get; }
        public float HalfExtentMeters { get; }
        public int Resolution { get; }
        public float GroundElevationMeters { get; }
        public float MinElevationMeters { get; private set; }
        public float MaxElevationMeters { get; private set; }

        /// <summary>Center-to-corner size of one hex, in meters. Call RebuildGeometry after changing it.</summary>
        public float HexRadiusMeters { get; set; }

        /// <summary>
        /// 0 == continuous hex-faceted surface, 1 == fully stepped plateaus with vertical walls between
        /// them. Call RebuildGeometry after changing it.
        /// </summary>
        public float StepStrength { get; set; } = 1.0f;

        /// <summary>How many hexes the last build actually emitted -- what the UI reports in place of a vertex count.</summary>
        public int HexCellCount { get; private set; }

        /// <summary>The hex radius the last build actually used, which is larger than HexRadiusMeters when MaxHexCells bit.</summary>
        public float EffectiveHexRadiusMeters { get; private set; }

        private readonly RegionHeightField _heightField;

        public RegionTerrainMesh(
            ManaContext context,
            LatLong center,
            float halfExtentMeters,
            int resolution,
            RegionHeightField heightField,
            float groundElevationMeters,
            float? hexRadiusMeters = null,
            float stepStrength = 1.0f)
            : base(context)
        {
            Center = center;
            HalfExtentMeters = halfExtentMeters;
            Resolution = Math.Max(2, resolution);
            GroundElevationMeters = groundElevationMeters;
            _heightField = heightField;
            HexRadiusMeters = hexRadiusMeters ?? (DefaultHexRadiusInSamples * (2f * halfExtentMeters) / (Resolution - 1));
            StepStrength = Math.Clamp(stepStrength, 0f, 1f);
            DefaultVertexColor = Colors.White;

            GenerateData();
        }

        public override ManaMaterial GenerateMaterial()
        {
            // Same reasoning as PlanetPatchMesh.GenerateMaterial: the base ManaProceduralMesh material
            // leaves Shader null, which only the OpenGL-only finalize step backfills, so under WebGPU the
            // Inspector would show no PBR channels at all.
            var material = new ManaMaterial(Context) { Name = "Region Terrain Material" };
            material.Shader = new ManaPbrShader(Context);
            // Faceted by default -- with a hex tiling this is what makes each plateau read as one solid
            // tile instead of being smeared across its neighbours. Toggleable per-material in the Inspector.
            material.FlatShaded = true;
            material.Albedo.Value = DefaultVertexColor;
            material.EnsureShaderMetadataDefaults();
            return material;
        }

        /// <summary>
        /// How far the sphere's surface falls away from the flat tangent plane at a point this far
        /// horizontally from the region's center. Ignorable for a 1km region (13cm) but not for a large
        /// one -- at the corners of the largest allowed 64km box it reaches 80m, which is enough to bow a
        /// lake surface visibly and to put ridgelines at the wrong height relative to the middle.
        ///
        /// Computed in double: the exact form needs R squared (4e13), which blows straight past float's
        /// 24-bit mantissa and would leave meters of noise in a sub-100m answer.
        /// </summary>
        private static float CurvatureDropMeters(float eastMeters, float southMeters)
        {
            var radius = (double)PlanetLoader.EarthRadiusMeters;
            var horizontalSquared = ((double)eastMeters * eastMeters) + ((double)southMeters * southMeters);
            return (float)(radius - Math.Sqrt(Math.Max(0.0, (radius * radius) - horizontalSquared)));
        }

        // -1 means "not water", which is what PlanetColors expects for the land ramp.
        private static float WaterDepthAt(GeoWaterLookup? lookup, float longitude, float latitude)
        {
            if (lookup == null || !lookup.IsWater(longitude, latitude))
            {
                return -1f;
            }

            return lookup.GetWaterDepth01(longitude, latitude);
        }

        /// <summary>Running total for one hex corner, so its shared height is the mean of every hex touching it.</summary>
        private struct CornerHeight
        {
            public float Sum;
            public int Count;
        }

        /// <summary>
        /// Grows the requested radius until the lattice fits inside MaxHexCells. Cell count scales as
        /// 1/radius^2, so the ratio of the two counts gives the needed factor in one step rather than by
        /// iterating.
        /// </summary>
        private float ResolveHexRadius()
        {
            var requested = MathF.Max(HexRadiusMeters, 0.05f);
            var probe = new RegionHexGrid(HalfExtentMeters, requested);
            var cells = (long)probe.Columns * probe.Rows;
            if (cells <= MaxHexCells)
            {
                return requested;
            }

            return requested * MathF.Sqrt((float)cells / MaxHexCells);
        }

        protected override void GenerateData()
        {
            base.GenerateData();

            var radius = ResolveHexRadius();
            EffectiveHexRadiusMeters = radius;
            var grid = new RegionHexGrid(HalfExtentMeters, radius);
            var step = Math.Clamp(StepStrength, 0f, 1f);
            var buildWalls = step >= MinStepStrengthForWalls;
            var metersPerDegreeLongitude = RegionExtents.MetersPerDegreeLongitude(Center.Latitude);

            var longitudeSpan = HalfExtentMeters / metersPerDegreeLongitude;
            var latitudeSpan = HalfExtentMeters / RegionExtents.MetersPerDegreeLatitude;

            // Rasterized on the source grid's resolution rather than the hex lattice's, so a shoreline
            // lands where the polygon puts it regardless of how the terrain is currently tiled. Null when
            // the Natural Earth data isn't present -- colouring then falls back to elevation alone.
            var waterLookup = GeoWaterMask.Shared?.Rasterize(
                Center.Longitude - longitudeSpan, Center.Longitude + longitudeSpan,
                Center.Latitude - latitudeSpan, Center.Latitude + latitudeSpan,
                Resolution, Resolution);

            // Sampled up front, as its own pass, for two reasons: per-hex normals below are a central
            // difference over neighbouring cells, and every corner height is an average over the hexes
            // that meet there. Neither is knowable while the first hex is still being built.
            // Two height arrays, deliberately: `elevations` is true elevation above sea level (what the
            // biome ramp and the reported min/max want), while `surfaceY` is the local vertical coordinate
            // after the datum and Earth's curvature are taken out.
            var included = new bool[grid.Columns, grid.Rows];
            var elevations = new float[grid.Columns, grid.Rows];
            var surfaceY = new float[grid.Columns, grid.Rows];
            var colors = new Vector4[grid.Columns, grid.Rows];
            var minElevation = float.MaxValue;
            var maxElevation = float.MinValue;
            var cellCount = 0;

            for (var row = 0; row < grid.Rows; row++)
            {
                var south = grid.CellSouth(row);
                var latitude = Center.Latitude - (south / RegionExtents.MetersPerDegreeLatitude);

                for (var column = 0; column < grid.Columns; column++)
                {
                    var east = grid.CellEast(column, row);

                    // The lattice deliberately over-covers the square (see RegionHexGrid), so the hexes
                    // whose centers land outside it are dropped here. That leaves a jagged hexagonal
                    // border rather than a clean edge, which suits the tiling.
                    if (MathF.Abs(east) > HalfExtentMeters || MathF.Abs(south) > HalfExtentMeters)
                    {
                        continue;
                    }

                    var longitude = Center.Longitude + (east / metersPerDegreeLongitude);
                    var elevation = _heightField.SampleMeters(longitude, latitude);

                    included[column, row] = true;
                    elevations[column, row] = elevation;
                    surfaceY[column, row] = elevation - GroundElevationMeters - CurvatureDropMeters(east, south);
                    colors[column, row] = PlanetColors.GetBiomeColor(
                        elevation,
                        latitude,
                        WaterDepthAt(waterLookup, longitude, latitude));

                    minElevation = MathF.Min(minElevation, elevation);
                    maxElevation = MathF.Max(maxElevation, elevation);
                    cellCount++;
                }
            }

            HexCellCount = cellCount;
            MinElevationMeters = cellCount > 0 ? minElevation : 0f;
            MaxElevationMeters = cellCount > 0 ? maxElevation : 0f;

            // Every hex contributes its own plateau height to each of its six corners; the mean is the
            // height that corner would have on a continuous surface. It is what StepStrength interpolates
            // AWAY from, and -- because all three hexes at a corner agree on it exactly -- it is also what
            // keeps the walls watertight: each hex's rim drops to this shared value, so neighbouring rims
            // meet edge to edge no matter how different their plateaus are.
            var cornerHeights = new Dictionary<long, CornerHeight>(cellCount * 2);
            for (var row = 0; row < grid.Rows; row++)
            {
                for (var column = 0; column < grid.Columns; column++)
                {
                    if (!included[column, row])
                    {
                        continue;
                    }

                    var height = surfaceY[column, row];
                    for (var corner = 0; corner < RegionHexGrid.CornersPerHex; corner++)
                    {
                        var key = RegionHexGrid.CornerKey(column, row, corner);
                        cornerHeights.TryGetValue(key, out var accumulated);
                        accumulated.Sum += height;
                        accumulated.Count++;
                        cornerHeights[key] = accumulated;
                    }
                }
            }

            var minSurfaceY = float.MaxValue;
            var maxSurfaceY = float.MinValue;
            var topCorners = new ManaGeometryVertex[RegionHexGrid.CornersPerHex];
            var bottomCorners = new ManaGeometryVertex[RegionHexGrid.CornersPerHex];
            var uvScale = 1f / (2f * HalfExtentMeters);

            for (var row = 0; row < grid.Rows; row++)
            {
                for (var column = 0; column < grid.Columns; column++)
                {
                    if (!included[column, row])
                    {
                        continue;
                    }

                    var east = grid.CellEast(column, row);
                    var south = grid.CellSouth(row);
                    var height = surfaceY[column, row];
                    var color = colors[column, row];
                    var normal = CellNormal(grid, included, surfaceY, column, row, step);

                    var center = DynamicGeometryContainer.AddPoint(new Vector3(east, height, south), normal);
                    center.Color = color;
                    center.TexCoord0 = new Vector2((east + HalfExtentMeters) * uvScale, (south + HalfExtentMeters) * uvScale);

                    minSurfaceY = MathF.Min(minSurfaceY, height);
                    maxSurfaceY = MathF.Max(maxSurfaceY, height);

                    for (var corner = 0; corner < RegionHexGrid.CornersPerHex; corner++)
                    {
                        var cornerEast = grid.CornerEast(column, row, corner);
                        var cornerSouth = grid.CornerSouth(row, corner);
                        var shared = cornerHeights[RegionHexGrid.CornerKey(column, row, corner)];
                        var sharedHeight = shared.Sum / shared.Count;
                        var topHeight = float.Lerp(sharedHeight, height, step);

                        var top = DynamicGeometryContainer.AddPoint(new Vector3(cornerEast, topHeight, cornerSouth), normal);
                        top.Color = color;
                        top.TexCoord0 = new Vector2((cornerEast + HalfExtentMeters) * uvScale, (cornerSouth + HalfExtentMeters) * uvScale);
                        topCorners[corner] = top;

                        minSurfaceY = MathF.Min(minSurfaceY, topHeight);
                        maxSurfaceY = MathF.Max(maxSurfaceY, topHeight);

                        if (!buildWalls)
                        {
                            continue;
                        }

                        // Walls point outward from the hex center, so that with flat shading off they
                        // still read as sides rather than as more of the plateau. The rim bottom sits at
                        // the shared height even when that is ABOVE the plateau (a hex in a hollow), which
                        // simply turns the wall inside out and is still watertight.
                        var outward = Vector3.Normalize(new Vector3(cornerEast - east, 0f, cornerSouth - south));
                        var bottom = DynamicGeometryContainer.AddPoint(new Vector3(cornerEast, sharedHeight, cornerSouth), outward);
                        bottom.Color = color;
                        bottom.TexCoord0 = top.TexCoord0;
                        bottomCorners[corner] = bottom;

                        minSurfaceY = MathF.Min(minSurfaceY, sharedHeight);
                        maxSurfaceY = MathF.Max(maxSurfaceY, sharedHeight);
                    }

                    for (var corner = 0; corner < RegionHexGrid.CornersPerHex; corner++)
                    {
                        var next = (corner + 1) % RegionHexGrid.CornersPerHex;
                        DynamicGeometryContainer.CreateFacesFromTriangle(center, topCorners[corner], topCorners[next]);

                        if (buildWalls)
                        {
                            DynamicGeometryContainer.CreateFacesFromQuad(
                                topCorners[corner],
                                topCorners[next],
                                bottomCorners[next],
                                bottomCorners[corner]);
                        }
                    }
                }
            }

            if (cellCount == 0)
            {
                minSurfaceY = 0f;
                maxSurfaceY = 0f;
            }

            // Horizontal bounds allow for the corner overhang: a hex kept because its CENTER is inside the
            // box still reaches a radius past it. Vertical bounds come from surfaceY rather than raw
            // elevation, because the curvature drop pulls the far corners down by as much as 80m at the
            // largest allowed region, which the raw range wouldn't account for.
            var horizontalExtent = HalfExtentMeters + grid.Radius;
            MinSize = new Vector3(-horizontalExtent, minSurfaceY, -horizontalExtent);
            MaxSize = new Vector3(horizontalExtent, maxSurfaceY, horizontalExtent);

            // Explicit rather than relying on GetOrCreateCpuData()'s lazy build, for the same reason
            // GeoTiffTerrainMesh does it: the lazy path returns the cached CpuData immediately once set,
            // so a later regenerate would otherwise have no visible effect.
            SetCpuData(DynamicGeometryContainer.CreateCpuData());
        }

        /// <summary>
        /// The normal for one hex's top face. At full step strength a plateau is exactly level, so this
        /// lerps from the local surface gradient to straight up alongside the geometry itself -- otherwise
        /// flat tiles would be shaded as though they were still sloped.
        ///
        /// The gradient is a central difference over neighbouring CELLS: east is the adjacent column
        /// (Width apart), south is two rows away, which is the nearest cell sharing this one's column
        /// alignment (3 * Radius apart). Same (-df/dx, 1, -df/dz) form the quad grid used, just on the
        /// hex lattice's spacings.
        /// </summary>
        private static Vector3 CellNormal(
            RegionHexGrid grid,
            bool[,] included,
            float[,] surfaceY,
            int column,
            int row,
            float step)
        {
            var west = SampleCell(included, surfaceY, column - 1, row, column, row);
            var east = SampleCell(included, surfaceY, column + 1, row, column, row);
            var north = SampleCell(included, surfaceY, column, row - 2, column, row);
            var south = SampleCell(included, surfaceY, column, row + 2, column, row);

            var surfaceNormal = Vector3.Normalize(new Vector3(
                west - east,
                2f * grid.Width,
                (north - south) * (grid.Width / (3f * grid.Radius))));

            return Vector3.Normalize(Vector3.Lerp(surfaceNormal, Vector3.UnitY, step));
        }

        // Falls back to the cell's own height at the lattice edge, which flattens the gradient there
        // rather than reading a dropped cell's uninitialised zero as a cliff.
        private static float SampleCell(bool[,] included, float[,] surfaceY, int column, int row, int fallbackColumn, int fallbackRow)
        {
            if (column < 0 || row < 0 || column >= included.GetLength(0) || row >= included.GetLength(1) || !included[column, row])
            {
                return surfaceY[fallbackColumn, fallbackRow];
            }

            return surfaceY[column, row];
        }
    }
}
