using Mana.Code;
using Mana.Code.Materials;
using Mana.Code.Shapes;
using ManaGeoTools.Code.GeoTiff;

namespace ManaGeoTools.Code.Planet
{
    /// <summary>
    /// Near tier of the two-tier planet ocean: one camera-following square of sea surface, dense
    /// enough to carry real waves, that follows the camera down from orbit (see
    /// PlanetNode.UpdateNearOcean). The far tier -- PlanetOceanMesh's whole-globe shell -- stays flat
    /// and fades out underneath this patch's footprint so the two never double-blend.
    ///
    /// The vertex COUNT is fixed and the SPAN varies: descending shrinks the footprint, which raises
    /// detail without ever changing topology, reallocating differently-sized buffers, or introducing a
    /// second density that could crack against the first. That is also why this is a single patch
    /// rather than an ocean quadtree mirroring the terrain's -- adjacent quadtree levels would meet at
    /// T-junctions, and on a surface whose vertices are displaced every frame those junctions crack
    /// open visibly rather than staying hidden the way they do on static terrain.
    ///
    /// Span is expressed as an ANGLE, not a distance. A patch that has to work from orbit down to sea
    /// level covers a ~1000:1 range, and an angle is the only framing where both ends stay meaningful
    /// on a sphere (a metre span would either vanish from orbit or wrap past the horizon up close).
    /// Geometry is a gnomonic tangent-plane grid around the center direction, NOT a cube-sphere patch:
    /// any parameterization describes the same sea-level sphere, and a tangent grid has no face
    /// boundary or polar case to straddle. It carries no skirt, since it fades to fully transparent
    /// well inside its own rim.
    /// </summary>
    public sealed class PlanetNearOceanMesh : ManaProceduralMesh
    {
        // Vertices across one edge. 257 keeps the whole patch at ~66k vertices -- comparable to a
        // single up-res'd terrain patch, and a fixed cost regardless of altitude since the span (not
        // the count) is what varies.
        public const int Resolution = 257;

        // The wave field repeats every this many meters (see near_ocean.wgsl's wrap_cell). Large
        // enough that the repeat never fits on screen at any altitude this patch is active at, small
        // enough that the tracked wave origin stays a precise f32 after reduction.
        public const float WaveTileMeters = 16384f;

        // Ceiling on the half-span angle (~20 degrees). Gnomonic projection stretches as tan(angle), so
        // this is where the edge-vs-center scale difference (~1.14x, and mostly inside the faded rim)
        // is still small enough to leave waves looking round rather than smeared.
        public const float MaxHalfSpanRadians = 0.35f;

        // Fraction of the half-span at which the cross-fade to the far shell starts and finishes.
        // Both are inside the inscribed circle of the square grid, so the fade always completes on
        // geometry that exists -- the corners beyond it are skipped outright rather than drawn at zero
        // alpha.
        private const float EdgeFadeInnerFraction = 0.60f;
        private const float EdgeFadeOuterFraction = 0.95f;

        public float Radius { get; }
        public float MetersPerUnit { get; }
        public GeoTiffLibrary? HeightLibrary { get; private set; }

        /// <summary>Unit direction from the planet center to the patch center, in planet-local space.</summary>
        public Vector3 CenterDirection { get; private set; } = Vector3.UnitY;

        /// <summary>Patch tangent basis in planet-local space, built so cross(East, North) == CenterDirection.</summary>
        public Vector3 East { get; private set; } = Vector3.UnitX;
        public Vector3 North { get; private set; } = Vector3.UnitZ;

        /// <summary>Half the patch's angular width, as seen from the planet center.</summary>
        public float HalfSpanRadians { get; private set; }

        public float PlanetRadiusMeters => Radius * MetersPerUnit;

        /// <summary>Half-span as surface arc distance -- the span in the units waves are authored in.</summary>
        public float HalfSpanMeters => HalfSpanRadians * PlanetRadiusMeters;

        /// <summary>
        /// Where this patch's (east, north) wave-plane origin currently sits, in meters, reduced modulo
        /// WaveTileMeters. Tracked cumulatively across re-centers rather than recomputed from the
        /// center's position -- see RecenterTo.
        /// </summary>
        public Vector2 WaveOriginMeters { get; private set; }

        /// <summary>
        /// Ground distance between adjacent vertices. This is the quantity that actually bounds how
        /// small a wave the patch can carry, and it grows with the span -- near_ocean.wgsl sizes waves
        /// against it so they stay both visible and resolvable at every altitude.
        /// </summary>
        public float VertexSpacingMeters => HalfSpanMeters * 2f / (Resolution - 1);

        public float EdgeFadeInnerRadiusUnits => HalfSpanRadians * EdgeFadeInnerFraction * Radius;

        public float EdgeFadeOuterRadiusUnits => HalfSpanRadians * EdgeFadeOuterFraction * Radius;

        public PlanetNearOceanMesh(
            ManaContext context,
            float radius,
            float metersPerUnit,
            Vector3 centerDirection,
            float halfSpanRadians,
            GeoTiffLibrary? heightLibrary)
            : base(context)
        {
            Radius = radius;
            MetersPerUnit = MathF.Max(metersPerUnit, 1e-6f);
            HalfSpanRadians = ClampHalfSpan(halfSpanRadians);
            HeightLibrary = heightLibrary;
            DefaultVertexColor = Colors.White;

            SetCenterDirection(centerDirection);
            GenerateData();
        }

        /// <summary>
        /// Moves the patch to a new center/span and rebuilds. Safe to call from any thread -- see
        /// SetCpuData's doc comment on ManaMesh.
        ///
        /// The wave origin is ADVANCED by the new center's displacement in the OLD tangent frame,
        /// rather than recomputed from the new center's latitude/longitude. That distinction is the
        /// whole reason the wave field doesn't jump every time the patch re-centers: there is no
        /// globally consistent flat (east, north) map of a sphere, so any position-derived origin
        /// would slide as the center moved. Integrating the small per-step displacement instead keeps
        /// the field continuous, with only an O(step^2 / planetRadius) error per step and a harmless
        /// holonomy drift if the camera flies a large closed loop. Reducing modulo WaveTileMeters is
        /// then exact, because the shader's lattice wraps on that same tile.
        /// </summary>
        public void RecenterTo(Vector3 centerDirection, float halfSpanRadians, GeoTiffLibrary? heightLibrary)
        {
            var newDirection = Vector3.Normalize(centerDirection);

            // Project the move into the OLD frame (that's the frame the current origin is expressed
            // in) using the chord, which matches the arc to well within a wavelength at these steps.
            var delta = newDirection - CenterDirection;
            var stepEastMeters = Vector3.Dot(delta, East) * PlanetRadiusMeters;
            var stepNorthMeters = Vector3.Dot(delta, North) * PlanetRadiusMeters;

            WaveOriginMeters = ReduceToWaveTile(WaveOriginMeters + new Vector2(stepEastMeters, stepNorthMeters));

            HalfSpanRadians = ClampHalfSpan(halfSpanRadians);
            HeightLibrary = heightLibrary;
            SetCenterDirection(newDirection);
            GenerateData();
        }

        private static float ClampHalfSpan(float halfSpanRadians)
        {
            return Math.Clamp(halfSpanRadians, 1e-6f, MaxHalfSpanRadians);
        }

        private static Vector2 ReduceToWaveTile(Vector2 value)
        {
            return new Vector2(
                value.X - (MathF.Floor(value.X / WaveTileMeters) * WaveTileMeters),
                value.Y - (MathF.Floor(value.Y / WaveTileMeters) * WaveTileMeters));
        }

        private void SetCenterDirection(Vector3 centerDirection)
        {
            var direction = centerDirection.LengthSquared() > 0f
                ? Vector3.Normalize(centerDirection)
                : Vector3.UnitY;

            // Any tangent basis works (the wave field is isotropic); it only has to be orthonormal and
            // right-handed about the outward direction so near_ocean.wgsl's cross(d_east, d_north)
            // comes out pointing away from the planet. The helper swap avoids a degenerate cross
            // product when the direction is near the axis being crossed against.
            var helper = MathF.Abs(direction.Y) > 0.9f ? Vector3.UnitX : Vector3.UnitY;
            var east = Vector3.Normalize(Vector3.Cross(helper, direction));
            var north = Vector3.Cross(direction, east);

            CenterDirection = direction;
            East = east;
            North = north;
        }

        public override ManaMaterial GenerateMaterial()
        {
            // Same reasoning as PlanetOceanMesh/PlanetPatchMesh: the base ManaProceduralMesh material
            // leaves Shader null, which only gets backfilled by the OpenGL-only finalize step.
            var material = new ManaMaterial(Context) { Name = "Planet Near Ocean Material" };
            material.Shader = new ManaNearOceanShader(Context);
            material.EnsureShaderMetadataDefaults();
            return material;
        }

        protected override void GenerateData()
        {
            base.GenerateData();

            var planetRadiusMeters = PlanetRadiusMeters;
            // Gnomonic tangent-plane extent: a grid coordinate of `u` sits at arc angle atan(u), so
            // reaching a half-span ANGLE means going out to tan(angle) on the plane.
            var halfSpanTangent = MathF.Tan(HalfSpanRadians);
            var fadeInnerMeters = HalfSpanMeters * EdgeFadeInnerFraction;
            var fadeOuterMeters = HalfSpanMeters * EdgeFadeOuterFraction;

            var positions = new Vector3[Resolution, Resolution];
            var normals = new Vector3[Resolution, Resolution];
            var waveCoords = new Vector2[Resolution, Resolution];
            var colors = new Vector4[Resolution, Resolution];
            var isOcean = new bool[Resolution, Resolution];
            var isInsideFade = new bool[Resolution, Resolution];

            // Same safety argument as PlanetPatchMesh.GenerateData's phase 1: each iteration writes only
            // its own (x, y) slots, and HeightLibrary has no concurrent writer because every caller
            // holds PlanetNode's _rebuildLock across this whole call.
            Parallel.For(0, Resolution, y =>
            {
                var northTangent = ((((float)y / (Resolution - 1)) * 2f) - 1f) * halfSpanTangent;
                // Arc distance, not tangent-plane distance -- waves are authored in real surface
                // meters, and past a few degrees the two diverge enough to visibly stretch the wave
                // pattern toward the patch edges.
                var northMeters = MathF.Atan(northTangent) * planetRadiusMeters;

                for (var x = 0; x < Resolution; x++)
                {
                    var eastTangent = ((((float)x / (Resolution - 1)) * 2f) - 1f) * halfSpanTangent;
                    var eastMeters = MathF.Atan(eastTangent) * planetRadiusMeters;

                    var direction = Vector3.Normalize(
                        CenterDirection + (East * eastTangent) + (North * northTangent));

                    var rawHeightMeters = OceanSurface.SampleHeightMeters(HeightLibrary, direction);
                    isOcean[x, y] = OceanSurface.IsOcean(rawHeightMeters, direction);

                    var radialMeters = MathF.Sqrt((eastMeters * eastMeters) + (northMeters * northMeters));
                    isInsideFade[x, y] = radialMeters <= fadeOuterMeters;
                    var edgeFade = 1f - SmoothStep(fadeInnerMeters, fadeOuterMeters, radialMeters);

                    positions[x, y] = direction * Radius;
                    normals[x, y] = direction;
                    waveCoords[x, y] = WaveOriginMeters + new Vector2(eastMeters, northMeters);
                    // R carries the edge fade and A the depth factor; near_ocean.wgsl owns both sides
                    // of this layout, so G/B are simply unused.
                    colors[x, y] = new Vector4(edgeFade, 1f, 1f, OceanSurface.ComputeDepthFactor(rawHeightMeters));
                }
            });

            // Sequential, like PlanetPatchMesh's phase 2: AddPoint appends to a shared list and assigns
            // sequential indices.
            var verts = new ManaGeometryVertex[Resolution, Resolution];
            for (var y = 0; y < Resolution; y++)
            {
                for (var x = 0; x < Resolution; x++)
                {
                    var vert = DynamicGeometryContainer.AddPoint(positions[x, y], normals[x, y]);
                    vert.TexCoord0 = waveCoords[x, y];
                    vert.Color = colors[x, y];
                    verts[x, y] = vert;
                }
            }

            // A quad survives only if it is both inside the fade's outer radius (the square's corners
            // are past it and would draw nothing but fully transparent fill) and touching real ocean.
            // The ocean test matches PlanetOceanMesh's: one wet corner is enough, so the patch always
            // reaches the coastline rather than stopping a quad short of it.
            for (var y = 0; y < Resolution - 1; y++)
            {
                for (var x = 0; x < Resolution - 1; x++)
                {
                    var insideFade = isInsideFade[x, y] || isInsideFade[x + 1, y] || isInsideFade[x + 1, y + 1] || isInsideFade[x, y + 1];
                    if (!insideFade)
                    {
                        continue;
                    }

                    if (isOcean[x, y] || isOcean[x + 1, y] || isOcean[x + 1, y + 1] || isOcean[x, y + 1])
                    {
                        DynamicGeometryContainer.CreateFacesFromQuad(verts[x, y], verts[x + 1, y], verts[x + 1, y + 1], verts[x, y + 1]);
                    }
                }
            }

            MinSize = new Vector3(-Radius, -Radius, -Radius);
            MaxSize = new Vector3(Radius, Radius, Radius);

            SetCpuData(DynamicGeometryContainer.CreateCpuData());
        }

        private static float SmoothStep(float edge0, float edge1, float value)
        {
            if (edge1 <= edge0)
            {
                return value < edge0 ? 0f : 1f;
            }

            var t = Math.Clamp((value - edge0) / (edge1 - edge0), 0f, 1f);
            return t * t * (3f - (2f * t));
        }
    }
}
