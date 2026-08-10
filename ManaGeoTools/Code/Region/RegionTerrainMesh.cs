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
    /// The surface is a hexagonal tiling rather than a quad grid (see RegionHexGrid): each hex is a plateau
    /// at its own sampled elevation, joined to its neighbours by a rim of wall quads. StepStrength dials
    /// between that and a continuous, merely hex-faceted surface -- a drawing choice, not a data one.
    ///
    /// Elevation is stored relative to GroundElevationMeters (the real elevation at the region's exact
    /// center) rather than absolute, so the node's own origin sits on the ground instead of the mesh
    /// floating hundreds of meters up whenever the region happens to be inland. Vertex color still uses
    /// ABSOLUTE elevation, so the biome ramp matches what the planet shows for the same place.
    /// </summary>
    public sealed class RegionTerrainMesh : ManaProceduralMesh
    {
        /// <summary>Default hex radius in source sample spacings -- 1.0 covers ~2.6 samples, fine enough to lose nothing.</summary>
        public const float DefaultHexRadiusInSamples = 1.0f;

        // Ceiling on hex count, applied by growing the hexes rather than cropping the region: a hex costs
        // ~13 verts and 18 tris against a quad cell's 1 and 2, so a small radius on a big box runs away.
        public const int MaxHexCells = 120_000;

        // Below this the walls are shorter than the noise in the source data, so they're built as bare
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

        /// <summary>0 == continuous hex-faceted surface, 1 == stepped plateaus with walls. Call RebuildGeometry after changing it.</summary>
        public float StepStrength { get; set; } = 1.0f;

        /// <summary>Hexes the last build emitted.</summary>
        public int HexCellCount { get; private set; }

        /// <summary>Radius the last build used -- larger than HexRadiusMeters when MaxHexCells bit.</summary>
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
            // Infers sample spacing from Resolution, which is only right when Resolution wasn't clamped --
            // RegionNode passes the height field's real MetersPerSample instead.
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
            // Faceted so each plateau reads as one solid tile. Toggleable in the Inspector.
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

        /// <summary>Running total for one corner, so its shared height is the mean of the hexes touching it.</summary>
        private struct CornerHeight
        {
            public float Sum;
            public int Count;
        }

        // Grows the radius until the lattice fits MaxHexCells. Count scales as 1/radius^2, so the ratio of
        // the two counts gives the factor in one step rather than by iterating.
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

            // Rasterized on the source resolution, not the hex lattice's, so shorelines don't move when the
            // tiling is re-dialled. Null without the Natural Earth data -- colouring falls back to elevation.
            var waterLookup = GeoWaterMask.Shared?.Rasterize(
                Center.Longitude - longitudeSpan, Center.Longitude + longitudeSpan,
                Center.Latitude - latitudeSpan, Center.Latitude + latitudeSpan,
                Resolution, Resolution);

            // Its own pass because normals need neighbouring cells and corner heights need every hex that
            // meets there -- neither is knowable while the first hex is being built. Two height arrays:
            // `elevations` is above sea level (what the biome ramp wants), `surfaceY` is local vertical
            // with the datum and curvature taken out.
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

                    // The lattice over-covers the square, so cells centered outside it are dropped -- which
                    // leaves a jagged hexagonal border rather than a clean edge.
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

            // The mean plateau height at each corner: what that corner would sit at on a continuous
            // surface, so it's what StepStrength interpolates away from. Also what keeps the walls
            // watertight -- all three hexes at a corner agree on it, so their rims meet edge to edge.
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

                        // Outward normals so walls read as sides with flat shading off. The rim bottom sits
                        // at the shared height even when that's above the plateau (a hex in a hollow),
                        // which just turns the wall inside out -- still watertight.
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

            // Horizontal bounds allow for the corner overhang past a kept cell's center. Vertical bounds
            // use surfaceY, not raw elevation -- the curvature drop is up to 80m at the largest region.
            var horizontalExtent = HalfExtentMeters + grid.Radius;
            MinSize = new Vector3(-horizontalExtent, minSurfaceY, -horizontalExtent);
            MaxSize = new Vector3(horizontalExtent, maxSurfaceY, horizontalExtent);

            // Explicit rather than relying on GetOrCreateCpuData()'s lazy build, for the same reason
            // GeoTiffTerrainMesh does it: the lazy path returns the cached CpuData immediately once set,
            // so a later regenerate would otherwise have no visible effect.
            SetCpuData(DynamicGeometryContainer.CreateCpuData());
        }

        /// <summary>
        /// Top-face normal, lerped from the local gradient to straight up alongside the geometry -- a flat
        /// plateau would otherwise be shaded as though still sloped. The gradient is a central difference
        /// over neighbouring cells: east is the adjacent column (Width apart), south is two rows away
        /// (3 * Radius), which is the nearest cell sharing this one's column alignment.
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

        // Falls back to the cell's own height at the lattice edge, rather than reading a dropped cell's
        // uninitialised zero as a cliff.
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
