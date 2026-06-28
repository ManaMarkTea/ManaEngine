namespace Mana.Code.Shapes
{
    public class ManaTorus : ManaProceduralMesh
    {
        public float MajorRadius { get; set; }
        public float MinorRadius { get; set; }
        public int MajorSegments { get; set; } = 64;
        public int MinorSegments { get; set; } = 18;

        public float InnerRadius
        {
            get => MathF.Max(0.0f, MajorRadius - MinorRadius);
            set => SetInnerOuterRadii(value, OuterRadius);
        }

        public float OuterRadius
        {
            get => MajorRadius + MinorRadius;
            set => SetInnerOuterRadii(InnerRadius, value);
        }

        public ManaTorus(ManaContext context, Vector4 color, float majorRadius = 0.38f, float minorRadius = 0.14f) : base(context)
        {
            MajorRadius = majorRadius;
            MinorRadius = minorRadius;
            DefaultVertexColor = color;
            GenerateData();
        }

        public void SetInnerOuterRadii(float innerRadius, float outerRadius)
        {
            var inner = MathF.Max(0.001f, innerRadius);
            var outer = MathF.Max(inner + 0.001f, outerRadius);
            MajorRadius = (inner + outer) * 0.5f;
            MinorRadius = (outer - inner) * 0.5f;
        }

        protected override void GenerateData()
        {
            base.GenerateData();

            var container = DynamicGeometryContainer;
            var majorSegments = Math.Max(3, MajorSegments);
            var minorSegments = Math.Max(3, MinorSegments);

            for (int major = 0; major <= majorSegments; ++major)
            {
                var u = major / (float)majorSegments;
                var theta = u * MathF.Tau;
                var radial = new Vector3(MathF.Cos(theta), 0.0f, MathF.Sin(theta));

                for (int minor = 0; minor <= minorSegments; ++minor)
                {
                    var v = minor / (float)minorSegments;
                    var phi = v * MathF.Tau;
                    var tubeNormal = radial * MathF.Cos(phi) + Vector3.UnitY * MathF.Sin(phi);
                    var position = radial * (MajorRadius + MinorRadius * MathF.Cos(phi))
                        + Vector3.UnitY * (MinorRadius * MathF.Sin(phi));

                    var vertex = container.AddPoint(position, tubeNormal.Normalized());
                    vertex.Color = DefaultVertexColor;
                    vertex.TexCoord0 = new Vector2(u, v);
                    vertex.TexCoord1 = vertex.TexCoord0;
                }
            }

            var rowStride = minorSegments + 1;
            var indices = new List<uint>();
            for (int major = 0; major < majorSegments; ++major)
            {
                for (int minor = 0; minor < minorSegments; ++minor)
                {
                    var a = (uint)(major * rowStride + minor);
                    var b = (uint)((major + 1) * rowStride + minor);
                    var c = (uint)((major + 1) * rowStride + minor + 1);
                    var d = (uint)(major * rowStride + minor + 1);

                    indices.Add(a);
                    indices.Add(b);
                    indices.Add(c);
                    indices.Add(a);
                    indices.Add(c);
                    indices.Add(d);
                }
            }

            container.AddFaces(indices);
            SetCpuData(container.CreateCpuData());
            if (Context.OpenGl != null)
            {
                VertexBufferObject = container.CreateVertexBufferObject(Context);
            }
        }
    }
}
