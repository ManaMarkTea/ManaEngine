namespace Mana
{
    public class ManaQuad : ManaProceduralMesh
    {
        public Vector3 CubeSize { get; set; }

        public ManaQuad(ManaContext context, Vector4 color, Vector3 size) : base(context)
        {
            CubeSize = size;
            DefaultVertexColor = color;
            GenerateData();
        }

        protected override void GenerateData()
        {
            Vector3 cR = CubeSize;
            MinSize.X = MinSize.Y = MinSize.Z = float.MaxValue;
            MaxSize.X = MaxSize.Y = MaxSize.Z = float.MinValue;
            MinSize = -cR;
            MaxSize = cR;

            var va = new Vector3(-cR.X, -cR.Y, -cR.Z) * 0.5f;
            var vb = new Vector3(cR.X, -cR.Y, -cR.Z) * 0.5f;
            var vc = new Vector3(cR.X, cR.Y, -cR.Z) * 0.5f;
            var vd = new Vector3(-cR.X, cR.Y, -cR.Z) * 0.5f;

            var vBa= vb - va;
            var vCa = vc - va;
            var vNormal = Vector3.Cross(vBa, vCa).Normalized();

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
