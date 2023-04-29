using Mana.Code;
using Mana.Code.Shapes;
using Silk.NET.Assimp;
using Silk.NET.Core.Native;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mana
{
    public class ManaQuad: ManaProceduralMesh
    {
        public Vector3 CubeSize { get; set; }

        public ManaQuad(ManaWorld world, Vector4 color, Vector3 size) : base(world)
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

            List<ManaMaterial> newMaterials = new List<ManaMaterial>();
            var foundMaterial = new ManaMaterial(World);
            if (foundMaterial != null)
            {
                newMaterials.Add(foundMaterial);
            }

            
            MinSize = -cR;
            MaxSize = cR;

            ManaDynamicGeometryContainer container = new ManaDynamicGeometryContainer();

            var va = new Vector3(-cR.X, -cR.Y, -cR.Z) * 0.5f;
            var vb = new Vector3(cR.X, -cR.Y, -cR.Z) * 0.5f;
            var vc = new Vector3(cR.X, cR.Y, -cR.Z) * 0.5f;
            var vd = new Vector3(-cR.X, cR.Y, -cR.Z) * 0.5f;

            ManaGeometryVertex[] verts = new ManaGeometryVertex[4];
            verts[0] = container.AddPoint(va);
            verts[1] = container.AddPoint(vb);
            verts[2] = container.AddPoint(vc);
            verts[3] = container.AddPoint(vd);

            foreach (var vert in verts)
            {
                vert.Color = this.DefaultVertexColor;
            }

            container.CreateFacesFromQuad(verts);

            var positions = container.Positions.ToArray();
            var normals = container.Normals.ToArray();
            var colors = container.Colors.ToArray();
            var textureCoordinates0 = container.TexCoords0.ToArray();
            var textureCoordinates1 = container.TexCoords0.ToArray();
            var indexes = container.Indexes.ToArray();

            Materials = newMaterials.ToArray();

            MeshBufferObject = new MetaVertexBufferObject(indexes, positions, normals, colors, textureCoordinates0, textureCoordinates1);
            MeshBufferObject.ConstructVBO();
        }

    }
}
