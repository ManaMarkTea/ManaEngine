namespace Mana
{
    public enum ManaQuadPlanes
    {
        XZ,
        YX,
        ZY,
    }

    public class ManaQuad : ManaProceduralMesh
    {
        public Vector3 GridStart { get; set; }
        public Vector3 GridEnd { get; set; }

        ManaQuadPlanes Plane;
        float AxisStart;
        float AxisEnd;
        float ExtrudeStart;
        float ExtrudeEnd;

        public ManaQuad(ManaContext context, Vector4 color, Vector3 size) : base(context)
        {
            GridStart = -size * 0.5f;
            GridEnd = size * 0.5f;

            Plane = ManaQuadPlanes.YX;
            AxisStart = -size.X * 0.5f;
            AxisEnd = size.X * 0.5f;
            ExtrudeStart = -size.Y * 0.5f;
            ExtrudeEnd = size.Y * 0.5f;

            DefaultVertexColor = color;
            GenerateData();
        }

        //public ManaQuad(ManaContext context, Vector4 color, Vector3 start, Vector3 end) : base(context)
        //{
        //    GridStart = start;
        //    GridEnd = end;

        //    DefaultVertexColor = color;
        //    GenerateData();
        //}

        public ManaQuad(ManaContext context, Vector4 color, ManaQuadPlanes plane, float axisStart, float axisEnd, float extrudeStart, float extrudeEnd) : base(context)
        {
            Plane = plane;
            AxisStart = axisStart;
            AxisEnd = axisEnd;

            ExtrudeStart = extrudeStart;
            ExtrudeEnd = extrudeEnd;

            DefaultVertexColor = color;
            GenerateData();
        }

        public override ManaMaterial GenerateMaterial()
        {
            var material = new ManaMaterial(Context);
            material.Shader = new ManaGridShader(Context);
            return material;
        }

        protected override void GenerateData()
        {
            base.GenerateData();
            MinSize.X = MinSize.Y = MinSize.Z = float.MaxValue;
            MaxSize.X = MaxSize.Y = MaxSize.Z = float.MinValue;
            MinSize = GridStart;
            MaxSize = GridEnd;

            Vector3 axis = Vector3.Zero;
            Vector3 extrustion = Vector3.Zero;

            switch (Plane)
            {
                case ManaQuadPlanes.XZ: axis = Vector3.UnitX; extrustion = Vector3.UnitZ; break;
                case ManaQuadPlanes.YX: axis = Vector3.UnitY; extrustion = Vector3.UnitX; break;
                case ManaQuadPlanes.ZY: axis = Vector3.UnitZ; extrustion = Vector3.UnitY; break;
            }

            var va = axis * AxisStart + extrustion * ExtrudeStart;
            var vb = axis * AxisEnd + extrustion * ExtrudeStart;
            
            var vc = axis * AxisEnd + extrustion * ExtrudeEnd;
            var vd = axis * AxisStart + extrustion * ExtrudeEnd;
            
            
            /*
            var va = new Vector3(GridStart.X, GridStart.Y, GridStart.Z);
            var vb = new Vector3(GridEnd.X, GridStart.Y, GridStart.Z);
            var vc = new Vector3(GridEnd.X, GridEnd.Y, GridStart.Z);
            var vd = new Vector3(GridStart.X, GridEnd.Y, GridStart.Z);
            */

            var vBa= vb - va;
            var vCa = vc - va;
            var vDa = vd - va;            
            var vNormal = Vector3.Cross(vCa, vBa).Normalized();

            ManaGeometryVertex[] verts = new ManaGeometryVertex[4];
            verts[0] = DynamicGeometryContainer.AddPoint(va, vNormal);
            verts[1] = DynamicGeometryContainer.AddPoint(vb, vNormal);
            verts[2] = DynamicGeometryContainer.AddPoint(vc, vNormal);
            verts[3] = DynamicGeometryContainer.AddPoint(vd, vNormal);

            verts[0].TexCoord0 = new Vector2(0.0f, 0.0f);
            verts[1].TexCoord0 = new Vector2(1.0f, 0.0f);
            verts[2].TexCoord0 = new Vector2(1.0f, 1.0f);
            verts[3].TexCoord0 = new Vector2(0.0f, 1.0f);

            foreach (var vert in verts)
            {
                vert.Color = this.DefaultVertexColor;
            }

            DynamicGeometryContainer.CreateFacesFromQuad(verts);

            VertexBufferObject = DynamicGeometryContainer.CreateVertexBufferObject(Context);
        }

    }
}
