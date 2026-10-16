using System;
using System.Numerics;
using Mana.Code;
using Mana.Code.Shapes;

namespace Mana
{
    public class ManaWireframeCage : ManaProceduralMesh
    {
        public Vector3 BoxSize { get; set; }
        public float Thickness { get; set; }

        public ManaWireframeCage(ManaContext context, Vector4 color, Vector3 size, float thickness = 1f) : base(context)
        {
            BoxSize = size;
            Thickness = thickness;
            DefaultVertexColor = color;
            GenerateData();
        }

        protected override void GenerateData()
        {
            base.GenerateData();

            Vector3 c = BoxSize;
            MinSize = -c - new Vector3(Thickness);
            MaxSize = c + new Vector3(Thickness);

            float t = Thickness;

            // X-parallel edges (4 edges)
            DynamicGeometryContainer.AddCube(new Vector3(0, -c.Y, -c.Z), new Vector3(c.X * 2, t, t), DefaultVertexColor);
            DynamicGeometryContainer.AddCube(new Vector3(0, c.Y, -c.Z), new Vector3(c.X * 2, t, t), DefaultVertexColor);
            DynamicGeometryContainer.AddCube(new Vector3(0, -c.Y, c.Z), new Vector3(c.X * 2, t, t), DefaultVertexColor);
            DynamicGeometryContainer.AddCube(new Vector3(0, c.Y, c.Z), new Vector3(c.X * 2, t, t), DefaultVertexColor);

            // Y-parallel edges (4 edges)
            DynamicGeometryContainer.AddCube(new Vector3(-c.X, 0, -c.Z), new Vector3(t, c.Y * 2, t), DefaultVertexColor);
            DynamicGeometryContainer.AddCube(new Vector3(c.X, 0, -c.Z), new Vector3(t, c.Y * 2, t), DefaultVertexColor);
            DynamicGeometryContainer.AddCube(new Vector3(-c.X, 0, c.Z), new Vector3(t, c.Y * 2, t), DefaultVertexColor);
            DynamicGeometryContainer.AddCube(new Vector3(c.X, 0, c.Z), new Vector3(t, c.Y * 2, t), DefaultVertexColor);

            // Z-parallel edges (4 edges)
            DynamicGeometryContainer.AddCube(new Vector3(-c.X, -c.Y, 0), new Vector3(t, t, c.Z * 2), DefaultVertexColor);
            DynamicGeometryContainer.AddCube(new Vector3(c.X, -c.Y, 0), new Vector3(t, t, c.Z * 2), DefaultVertexColor);
            DynamicGeometryContainer.AddCube(new Vector3(-c.X, c.Y, 0), new Vector3(t, t, c.Z * 2), DefaultVertexColor);
            DynamicGeometryContainer.AddCube(new Vector3(c.X, c.Y, 0), new Vector3(t, t, c.Z * 2), DefaultVertexColor);

            SetCpuData(DynamicGeometryContainer.CreateCpuData());
        }
    }
}
