namespace Mana.Code.Shapes
{
    /// <summary>One polyline: a sequence of points in the owning mesh's local space, plus whether the
    /// last point should connect back to the first (a closed loop, e.g. a country border ring) or not
    /// (an open line, e.g. a flight path).</summary>
    public readonly struct ManaLinePolyline
    {
        public IReadOnlyList<Vector3> Points { get; }
        public bool Closed { get; }

        public ManaLinePolyline(IReadOnlyList<Vector3> points, bool closed)
        {
            Points = points;
            Closed = closed;
        }
    }

    /// <summary>
    /// Generic thin-line-in-3D-space mesh: any number of polylines, each rendered as a chain of short,
    /// thin, square-cross-section oriented boxes (one per segment) via
    /// ManaDynamicGeometryContainer.AddOrientedCube -- the same primitive ManaWireframeCage already
    /// uses for axis-aligned box edges, just with an arbitrary per-segment orientation instead. A
    /// square (not flat) cross-section is deliberate: each segment's own "roll" around its long axis is
    /// computed independently (there's no shared-vertex ribbon connecting segments), and a square
    /// cross-section makes that roll invisible instead of needing to keep it consistent between
    /// neighboring segments.
    ///
    /// Originally written specifically for country border lines (see ManaGeoTools' CountryBorderLines,
    /// which decimates + projects lat/long boundary rings onto a sphere and hands the resulting points
    /// here) -- pulled out to engine level since nothing about the line-building itself is
    /// planet-specific. Any node that wants to draw lines (flight paths, debug gizmos, KmlLineString's
    /// still-unimplemented case, etc.) can build a list of ManaLinePolylines and use this directly.
    /// </summary>
    public class ManaLineMesh : ManaProceduralMesh
    {
        public IReadOnlyList<ManaLinePolyline> Lines { get; }
        public float Thickness { get; }
        public float EmissionStrength { get; }

        public ManaLineMesh(
            ManaContext context,
            IReadOnlyList<ManaLinePolyline> lines,
            float thickness,
            Vector4 color,
            float emissionStrength = 0f)
            : base(context)
        {
            Lines = lines;
            Thickness = thickness;
            EmissionStrength = emissionStrength;
            DefaultVertexColor = color;

            GenerateData();
        }

        public override ManaMaterial GenerateMaterial()
        {
            var material = new ManaMaterial(Context) { Name = $"{GetShapeDisplayName()} Material" };
            material.Shader = new ManaPbrShader(Context);
            material.Albedo.Value = DefaultVertexColor;
            if (EmissionStrength > 0f)
            {
                // Optional: lets lines read clearly regardless of scene lighting (e.g. country
                // borders on a planet's unlit/night side) without forcing every use of this mesh to
                // glow -- EmissionStrength defaults to 0, which leaves the material non-emissive.
                material.Emission.Value = DefaultVertexColor;
                material.EmissionStrength = EmissionStrength;
            }
            material.EnsureShaderMetadataDefaults();
            return material;
        }

        protected override void GenerateData()
        {
            base.GenerateData();

            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            var hasAnyPoint = false;

            foreach (var line in Lines)
            {
                var points = line.Points;
                if (points.Count < 2)
                {
                    continue;
                }

                var segmentCount = line.Closed ? points.Count : points.Count - 1;
                for (var i = 0; i < segmentCount; i++)
                {
                    var a = points[i];
                    var b = points[(i + 1) % points.Count];
                    AddSegment(a, b);
                }

                foreach (var point in points)
                {
                    min = Vector3.Min(min, point);
                    max = Vector3.Max(max, point);
                    hasAnyPoint = true;
                }
            }

            var margin = new Vector3(Thickness);
            MinSize = hasAnyPoint ? min - margin : Vector3.Zero;
            MaxSize = hasAnyPoint ? max + margin : Vector3.Zero;

            SetCpuData(DynamicGeometryContainer.CreateCpuData());
        }

        private void AddSegment(Vector3 a, Vector3 b)
        {
            var delta = b - a;
            var length = delta.Length();
            if (length < 0.0001f)
            {
                return;
            }

            var direction = delta / length;
            var midpoint = (a + b) * 0.5f;
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
    }
}
