namespace Mana.Code
{
    public class ManaDynamicGeometryContainer
    {
        public List<ManaGeometryVertex> DisconnectedPoints = new List<ManaGeometryVertex>();
        public List<ManaGeometryVertex> Verts = new List<ManaGeometryVertex>();
        public List<ManaGeometryFace> Faces = new List<ManaGeometryFace>();
        public List<ManaGeometryQuad> Quads = new List<ManaGeometryQuad>();
        public List<ManaGeometryEdge> Edges = new List<ManaGeometryEdge>();

        public ManaVertexBufferObject CreateVertexBufferObject(ManaContext context)
        {
            var positions = Positions.ToArray();
            var normals = Normals.ToArray();
            var colors = Colors.ToArray();
            var textureCoordinates0 = TexCoords0.ToArray();
            var textureCoordinates1 = TexCoords1.ToArray();
            var indexes = Indexes.ToArray();

            var vertexBufferObject = new ManaVertexBufferObject(context, indexes, positions, normals, colors, textureCoordinates0, textureCoordinates1);
            vertexBufferObject.ConstructVBO();
            return vertexBufferObject;
        }

        public ManaGeometryVertex AddPoint(Vector3 point, Vector3 normal)
        {
            ManaGeometryVertex v = new ManaGeometryVertex();
            v.Position = point;
            v.Normal = normal;
            v.Color = new Vector4(1, 1, 1, 1);
            return AddVertex(v);
        }

        public ManaGeometryVertex AddVertex(ManaGeometryVertex vertex)
        {
            vertex.Index = Verts.Count;
            vertex.Geom = this;
            Verts.Add(vertex);
            DisconnectedPoints.Add(vertex);
            return vertex;
        }

        public ManaGeometryVertex[] AddVertexRange(params ManaGeometryVertex[] vertexes)
        {
            foreach (var vertex in vertexes)
            {
                vertex.Index = Verts.Count;
                vertex.Geom = this;
                Verts.Add(vertex);
                DisconnectedPoints.Add(vertex);
            }

            return vertexes;
        }

        public void AddTriangle(float height, float width, Matrix4x4 rotation, Matrix4x4 scale, Vector4 color)
        {
            float halfWidth = width * 0.5f;

            Vector3 a = new Vector3(-halfWidth, -height * 0.5f, halfWidth);
            Vector3 b = new Vector3(+halfWidth, -height * 0.5f, halfWidth);
            Vector3 c = new Vector3(0.0f, height * 0.5f, 0.0f);

            var va = Vector3.Transform(a, rotation);
            var vb = Vector3.Transform(b, rotation);
            var vc = Vector3.Transform(c, rotation);

            var verts = AddVertexRange
            (
                new ManaGeometryVertex()
                {
                    Position = va,
                    Normal = new Vector3(0, 0, 1).TransformNormal(rotation),
                    Color = color,
                    TexCoord0 = new Vector2(0, 0)
                },

                new ManaGeometryVertex()
                {
                    Position = vb,
                    Normal = new Vector3(0, 0, 1).TransformNormal(rotation),
                    Color = color,
                    TexCoord0 = new Vector2(1, 0)
                },

                new ManaGeometryVertex()
                {
                    Position = vc,
                    Normal = new Vector3(0, 0, 1).TransformNormal(rotation),
                    Color = color,
                    TexCoord0 = new Vector2(1, 1)
                }
            );

            CreateFacesFromPoints(verts.ToList());
        }

        public ManaGeometryQuad[] AddQuad(Vector3 quadSize, Matrix4x4 rotation, Matrix4x4 scale, Vector4 color)
        {
            var cR = Vector3.Transform(quadSize * 0.5f, scale);

            var va = Vector3.Transform(new Vector3(-cR.X, -cR.Y, -cR.Z), rotation);
            var vb = Vector3.Transform(new Vector3(cR.X, -cR.Y, -cR.Z), rotation);
            var vc = Vector3.Transform(new Vector3(cR.X, cR.Y, -cR.Z), rotation);
            var vd = Vector3.Transform(new Vector3(-cR.X, cR.Y, -cR.Z), rotation);

            var verts = AddVertexRange
            (
                new ManaGeometryVertex()
                {
                    Position = va,
                    Normal = new Vector3(0, 0, 1).TransformNormal(rotation),
                    Color = color,
                    TexCoord0 = new Vector2(0, 0)
                },

                new ManaGeometryVertex()
                {
                    Position = vb,
                    Normal = new Vector3(0, 0, 1).TransformNormal(rotation),
                    Color = color,
                    TexCoord0 = new Vector2(1, 0)
                },

                new ManaGeometryVertex()
                {
                    Position = vc,
                    Normal = new Vector3(0, 0, 1).TransformNormal(rotation),
                    Color = color,
                    TexCoord0 = new Vector2(1, 1)
                },

                new ManaGeometryVertex()
                {
                    Position = vd,
                    Normal = new Vector3(0, 0, 1).TransformNormal(rotation),
                    Color = color,
                    TexCoord0 = new Vector2(0, 1)
                }
            );

            return CreateQuadsFromPoints(verts.ToList());
        }


        public void CreateFaceFromDisconnectedPoints()
        {
            CreateFacesFromPoints(DisconnectedPoints);
        }

        public void CreateFacesFromPoints(List<ManaGeometryVertex> verts)
        {
            List<ManaGeometryVertex> consumedPoints = new List<ManaGeometryVertex>();
            for (int i = 0; i < verts.Count; i += 3)
            {
                int indexA = i + 0;
                int indexB = i + 1;
                int indexC = i + 2;

                if (indexC >= verts.Count)
                {
                    break;
                }

                ManaGeometryFace face = new ManaGeometryFace();
                face.Geom = this;
                face.A = verts[indexA];
                face.B = verts[indexB];
                face.C = verts[indexC];
                face.AssociateNeighbors();

                Faces.Add(face);
                consumedPoints.Add(verts[indexA]);
                consumedPoints.Add(verts[indexB]);
                consumedPoints.Add(verts[indexC]);
            }

            verts.RemoveRange(0, consumedPoints.Count);
            DisconnectedPoints.RemoveAll(consumedPoints.Contains);
        }

        public void CreateQuadFromDisconnectedPoints(List<ManaGeometryVertex> verts)
        {
            CreateQuadsFromPoints(DisconnectedPoints);
        }

        public ManaGeometryQuad[] CreateQuadsFromPoints(List<ManaGeometryVertex> verts)
        {
            List<ManaGeometryQuad> outputQuads = new List<ManaGeometryQuad>();

            List<ManaGeometryVertex> consumedPoints = new List<ManaGeometryVertex>();
            for (int i = 0; i < verts.Count; i += 4)
            {
                int indexA = i + 0;
                int indexB = i + 1;
                int indexC = i + 2;
                int indexD = i + 3;

                if (indexD >= verts.Count)
                {
                    break;
                }

                ManaGeometryFace face = new ManaGeometryFace();
                face.Geom = this;
                face.A = verts[indexA];
                face.B = verts[indexB];
                face.C = verts[indexC];
                face.AssociateNeighbors();

                ManaGeometryFace face2 = new ManaGeometryFace();
                face2.Geom = this;
                face2.A = verts[indexA];
                face2.B = verts[indexC];
                face2.C = verts[indexD];
                face2.AssociateNeighbors();

                Faces.Add(face);
                Faces.Add(face2);

                ManaGeometryQuad quad = new ManaGeometryQuad()
                {
                    A = verts[indexA],
                    B = verts[indexB],
                    C = verts[indexC],
                    D = verts[indexD],
                    Face1 = face,
                    Face2 = face2,
                    Normal = (face.Normal + face2.Normal) * 0.5f
                };

                Quads.Add(quad);
                outputQuads.Add(quad);
                consumedPoints.Add(verts[indexA]);
                consumedPoints.Add(verts[indexB]);
                consumedPoints.Add(verts[indexC]);
                consumedPoints.Add(verts[indexD]);
            }

            verts.RemoveRange(0, consumedPoints.Count);
            DisconnectedPoints.RemoveAll(consumedPoints.Contains);
            return outputQuads.ToArray();
        }

        public void CreateFacesFromQuad(ManaGeometryVertex[] verts)
        {
            CreateFacesFromQuad(verts[0], verts[1], verts[2], verts[3]);
        }

        public void CreateFacesFromQuad(ManaGeometryVertex a, ManaGeometryVertex b, ManaGeometryVertex c, ManaGeometryVertex d)
        {
            ManaGeometryFace face1 = new ManaGeometryFace();
            face1.Geom = this;
            face1.A = a;
            face1.B = b;
            face1.C = c;
            face1.AssociateNeighbors();
            Faces.Add(face1);

            ManaGeometryFace face2 = new ManaGeometryFace();
            face2.Geom = this;
            face2.A = a;
            face2.B = c;
            face2.C = d;
            face2.AssociateNeighbors();
            Faces.Add(face2);

            DisconnectedPoints.RemoveAll((item) => { return item == a || item == b || item == c || item == d; });
        }

        internal void AddFaces(List<uint> indexes)
        {
            for (int i = 0; i < indexes.Count; i += 3)
            {
                int indexA = (int)(indexes[i + 0]);
                int indexB = (int)(indexes[i + 1]);
                int indexC = (int)(indexes[i + 2]);

                if (indexC >= Verts.Count)
                {
                    break;
                }

                ManaGeometryFace face = new ManaGeometryFace();
                face.Geom = this;
                face.A = Verts[indexA];
                face.B = Verts[indexB];
                face.C = Verts[indexC];
                face.AssociateNeighbors();

                Faces.Add(face);
            }
        }



        public List<uint> Indexes
        {
            get
            {
                List<uint> output = new List<uint>();

                foreach (var face in Faces)
                {
                    output.Add((uint)face.A.Index);
                    output.Add((uint)face.B.Index);
                    output.Add((uint)face.C.Index);
                }

                return output;
            }
        }

        public List<Vector3> Positions { get { return Verts.Select((item) => item.Position).ToList(); } }
        public List<Vector3> Normals { get { return Verts.Select((item) => item.Normal).ToList(); } }
        public List<Vector4> Colors { get { return Verts.Select((item) => item.Color).ToList(); } }
        public List<Vector2> TexCoords0 { get { return Verts.Select((item) => item.TexCoord0).ToList(); } }
        public List<Vector2> TexCoords1 { get { return Verts.Select((item) => item.TexCoord1).ToList(); } }

    }
}
