using Mana.Code;
using Mana.Code.Materials;
using Mana.Code.ManaShaders;
using Mana.Code.Shapes;
using ManaGeoTools.Code.Planet;

namespace ManaGeoTools.Code.Region
{
    /// <summary>
    /// A flat XZ terrain grid for one RegionNode, built in a local east-north-up tangent plane centered
    /// on the region's lat/long at 1 scene unit == 1 real meter -- the cartesian counterpart to
    /// PlanetPatchMesh, which puts the same Terrarium elevation data on a (heavily shrunk) sphere.
    ///
    /// Axes: +X is east, +Y is up, +Z is SOUTH. That ordering is what makes the basis right-handed with
    /// Y up (east x up == south), and it puts north at -Z, i.e. straight ahead of a default-oriented
    /// camera looking down -Z.
    ///
    /// Elevation is stored relative to GroundElevationMeters (the real elevation at the region's exact
    /// center) rather than absolute, so the node's own origin sits on the ground instead of the mesh
    /// floating hundreds of meters up whenever the region happens to be inland. Vertex color still uses
    /// ABSOLUTE elevation, so the biome ramp matches what the planet shows for the same place.
    /// </summary>
    public sealed class RegionTerrainMesh : ManaProceduralMesh
    {
        public LatLong Center { get; }
        public float HalfExtentMeters { get; }
        public int Resolution { get; }
        public float GroundElevationMeters { get; }
        public float MinElevationMeters { get; private set; }
        public float MaxElevationMeters { get; private set; }

        private readonly RegionHeightField _heightField;

        public RegionTerrainMesh(
            ManaContext context,
            LatLong center,
            float halfExtentMeters,
            int resolution,
            RegionHeightField heightField,
            float groundElevationMeters)
            : base(context)
        {
            Center = center;
            HalfExtentMeters = halfExtentMeters;
            Resolution = Math.Max(2, resolution);
            GroundElevationMeters = groundElevationMeters;
            _heightField = heightField;
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
            // Faceted by default -- reads as terrain rather than a smooth putting green at these
            // sample spacings. Toggleable per-material in the Inspector.
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

        protected override void GenerateData()
        {
            base.GenerateData();

            var resolution = Resolution;
            var metersPerDegreeLongitude = RegionExtents.MetersPerDegreeLongitude(Center.Latitude);
            var spacingMeters = (2f * HalfExtentMeters) / (resolution - 1);

            // Sampled up front, as its own pass, because per-vertex normals below are a central
            // difference over the four neighbours -- same reason GeoTiffTerrainMesh does it this way
            // (ManaGeometryVertex.Normal has to be supplied when the vertex is added; there's no
            // generate-normals post-pass in this engine).
            // Two height arrays, deliberately: `elevations` is true elevation above sea level (what the
            // biome ramp and the reported min/max want), while `surfaceY` is the local vertical coordinate
            // after the datum and Earth's curvature are taken out. Normals are computed from surfaceY so
            // they match the geometry that actually gets built.
            var elevations = new float[resolution, resolution];
            var surfaceY = new float[resolution, resolution];
            var latitudes = new float[resolution];
            var longitudes = new float[resolution];
            var eastOffsets = new float[resolution];
            var southOffsets = new float[resolution];
            var minElevation = float.MaxValue;
            var maxElevation = float.MinValue;

            for (var z = 0; z < resolution; z++)
            {
                southOffsets[z] = float.Lerp(-HalfExtentMeters, HalfExtentMeters, (float)z / (resolution - 1));
                latitudes[z] = Center.Latitude - (southOffsets[z] / RegionExtents.MetersPerDegreeLatitude);
            }

            for (var x = 0; x < resolution; x++)
            {
                eastOffsets[x] = float.Lerp(-HalfExtentMeters, HalfExtentMeters, (float)x / (resolution - 1));
                longitudes[x] = Center.Longitude + (eastOffsets[x] / metersPerDegreeLongitude);
            }

            for (var z = 0; z < resolution; z++)
            {
                for (var x = 0; x < resolution; x++)
                {
                    var elevation = _heightField.SampleMeters(longitudes[x], latitudes[z]);
                    elevations[x, z] = elevation;
                    surfaceY[x, z] = elevation - GroundElevationMeters - CurvatureDropMeters(eastOffsets[x], southOffsets[z]);
                    minElevation = MathF.Min(minElevation, elevation);
                    maxElevation = MathF.Max(maxElevation, elevation);
                }
            }

            MinElevationMeters = minElevation;
            MaxElevationMeters = maxElevation;

            var verts = new ManaGeometryVertex[resolution, resolution];
            for (var z = 0; z < resolution; z++)
            {
                for (var x = 0; x < resolution; x++)
                {
                    // Central difference in real meters: for a heightfield y = f(x,z) the surface normal
                    // is proportional to (-df/dx, 1, -df/dz); scaling through by 2*spacing clears the
                    // divisions out. Same shape as GeoTiffTerrainMesh's, except that one could hardcode
                    // a spacing of 1 because its grid was in raw pixel units rather than meters.
                    var left = surfaceY[Math.Max(x - 1, 0), z];
                    var right = surfaceY[Math.Min(x + 1, resolution - 1), z];
                    var north = surfaceY[x, Math.Max(z - 1, 0)];
                    var south = surfaceY[x, Math.Min(z + 1, resolution - 1)];
                    var normal = Vector3.Normalize(new Vector3(left - right, 2f * spacingMeters, north - south));

                    var position = new Vector3(eastOffsets[x], surfaceY[x, z], southOffsets[z]);

                    var vert = DynamicGeometryContainer.AddPoint(position, normal);
                    vert.TexCoord0 = new Vector2((float)x / (resolution - 1), (float)z / (resolution - 1));
                    vert.Color = PlanetColors.GetBiomeColor(elevations[x, z], latitudes[z]);
                    verts[x, z] = vert;
                }
            }

            for (var x = 0; x < resolution - 1; x++)
            {
                for (var z = 0; z < resolution - 1; z++)
                {
                    DynamicGeometryContainer.CreateFacesFromQuad(
                        verts[x, z],
                        verts[x + 1, z],
                        verts[x + 1, z + 1],
                        verts[x, z + 1]);
                }
            }

            // Bounds come from surfaceY, not raw elevation: the curvature drop pulls the corners down by
            // as much as 80m at the largest allowed region, which the raw range wouldn't account for.
            var minSurfaceY = float.MaxValue;
            var maxSurfaceY = float.MinValue;
            foreach (var y in surfaceY)
            {
                minSurfaceY = MathF.Min(minSurfaceY, y);
                maxSurfaceY = MathF.Max(maxSurfaceY, y);
            }

            MinSize = new Vector3(-HalfExtentMeters, minSurfaceY, -HalfExtentMeters);
            MaxSize = new Vector3(HalfExtentMeters, maxSurfaceY, HalfExtentMeters);

            // Explicit rather than relying on GetOrCreateCpuData()'s lazy build, for the same reason
            // GeoTiffTerrainMesh does it: the lazy path returns the cached CpuData immediately once set,
            // so a later regenerate would otherwise have no visible effect.
            SetCpuData(DynamicGeometryContainer.CreateCpuData());
        }
    }
}
