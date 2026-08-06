using Mana.Code;
using Mana.Code.Materials;
using Mana.Code.Shapes;

namespace ManaGeoTools.Code.Planet
{
    /// <summary>
    /// One combined mesh for every country border ring passed in -- not one mesh per country, since
    /// nothing here needs per-country selection the way KML placemarks benefit from individual
    /// toggling, and a single mesh means a single draw call for the whole border layer.
    ///
    /// Each ring edge becomes a short, thin, square-cross-section oriented box via
    /// ManaDynamicGeometryContainer.AddOrientedCube -- the same primitive ManaWireframeCage already
    /// uses for axis-aligned box edges, just with an arbitrary per-segment orientation instead. A
    /// square (not flat) cross-section is deliberate: each segment's own "roll" around its long axis
    /// is computed independently (there's no shared-vertex ribbon connecting segments), and a square
    /// cross-section makes that roll invisible instead of needing to keep it consistent between
    /// neighboring segments.
    /// </summary>
    public sealed class CountryBorderMesh : ManaProceduralMesh
    {
        public float Radius { get; }
        public float Thickness { get; }
        public float MinSegmentDegrees { get; }
        public IReadOnlyList<IReadOnlyList<(double Longitude, double Latitude)>> Rings { get; }

        public CountryBorderMesh(
            ManaContext context,
            IReadOnlyList<IReadOnlyList<(double Longitude, double Latitude)>> rings,
            float radius,
            float thickness,
            float minSegmentDegrees)
            : base(context)
        {
            Rings = rings;
            Radius = radius;
            Thickness = thickness;
            MinSegmentDegrees = minSegmentDegrees;
            DefaultVertexColor = new Vector4(1.0f, 0.75f, 0.3f, 1.0f); // warm amber/gold

            GenerateData();
        }

        public override ManaMaterial GenerateMaterial()
        {
            var material = new ManaMaterial(Context) { Name = "Country Border Material" };
            material.Shader = new ManaPbrShader(Context);
            material.Albedo.Value = DefaultVertexColor;
            // Emissive so borders read on the planet's unlit/night side too, not just where the
            // scene's lights happen to be hitting.
            material.Emission.Value = DefaultVertexColor;
            material.EmissionStrength = 2.5;
            material.EnsureShaderMetadataDefaults();
            return material;
        }

        protected override void GenerateData()
        {
            base.GenerateData();

            var cosThreshold = MathF.Cos(MinSegmentDegrees * MathF.PI / 180.0f);

            foreach (var ring in Rings)
            {
                var decimated = DecimateRing(ring, cosThreshold);
                if (decimated.Count < 2)
                {
                    continue;
                }

                for (var i = 0; i < decimated.Count; i++)
                {
                    var a = decimated[i];
                    var b = decimated[(i + 1) % decimated.Count]; // wrap to close the loop
                    AddSegment(a, b);
                }
            }

            MinSize = new Vector3(-Radius, -Radius, -Radius);
            MaxSize = new Vector3(Radius, Radius, Radius);

            SetCpuData(DynamicGeometryContainer.CreateCpuData());
        }

        private Vector3 ToSpherePosition((double Longitude, double Latitude) point)
        {
            var latLong = LatLong.FromLongitudeAndLatitude((float)point.Longitude, (float)point.Latitude);
            return latLong.ToUnitSphere() * Radius;
        }

        private void AddSegment((double Longitude, double Latitude) a, (double Longitude, double Latitude) b)
        {
            var posA = ToSpherePosition(a);
            var posB = ToSpherePosition(b);
            var delta = posB - posA;
            var length = delta.Length();
            if (length < 0.0001f)
            {
                return;
            }

            var direction = delta / length;
            var midpoint = (posA + posB) * 0.5f;
            var orientation = RotationFromXAxis(direction);
            var size = new Vector3(length, Thickness, Thickness);
            DynamicGeometryContainer.AddOrientedCube(midpoint, orientation, size, DefaultVertexColor);
        }

        // The rotation that maps local +X onto `direction` -- ManaDynamicGeometryContainer's cube
        // helpers treat local X as the long axis before any rotation is applied (confirmed from
        // ManaWireframeCage's unrotated AddCube calls, e.g. new Vector3(c.X * 2, t, t) for an
        // X-parallel edge). No existing helper in this codebase does a generic "rotation between two
        // vectors" construction.
        private static Quaternion RotationFromXAxis(Vector3 direction)
        {
            const float parallelThreshold = 0.9999f;
            var dot = Vector3.Dot(Vector3.UnitX, direction);

            if (dot > parallelThreshold)
            {
                return Quaternion.Identity;
            }

            if (dot < -parallelThreshold)
            {
                return Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI);
            }

            var axis = Vector3.Normalize(Vector3.Cross(Vector3.UnitX, direction));
            var angle = MathF.Acos(Math.Clamp(dot, -1.0f, 1.0f));
            return Quaternion.CreateFromAxisAngle(axis, angle);
        }

        // Keeps a point only once it's more than the threshold's great-circle angle away from the
        // last kept point -- at 50m resolution this dataset is ~100K raw points, most of it far more
        // coastline/border detail than reads as anything but noise from a whole-planet view. Always
        // keeps the first point; the loop in GenerateData wraps back to it to close the ring, so the
        // last kept point doesn't need special-casing here.
        private List<(double Longitude, double Latitude)> DecimateRing(IReadOnlyList<(double Longitude, double Latitude)> ring, float cosThreshold)
        {
            if (ring.Count <= 3)
            {
                return new List<(double, double)>(ring);
            }

            var result = new List<(double, double)> { ring[0] };
            var lastKept = LatLong.FromLongitudeAndLatitude((float)ring[0].Longitude, (float)ring[0].Latitude).ToUnitSphere();

            for (var i = 1; i < ring.Count; i++)
            {
                var point = ring[i];
                var v = LatLong.FromLongitudeAndLatitude((float)point.Longitude, (float)point.Latitude).ToUnitSphere();
                if (Vector3.Dot(lastKept, v) < cosThreshold)
                {
                    result.Add(point);
                    lastKept = v;
                }
            }

            return result;
        }
    }
}
