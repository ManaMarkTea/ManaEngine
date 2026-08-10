using Silk.NET.Assimp;
using Silk.NET.OpenGL;
using Mana.Code.Graphics;
using ManaCore.Graphics;
using System.Numerics;

namespace Mana.Code
{
    public class ManaDynamicGeometryContainer
    {
        internal readonly static Matrix4x4[] CubeFaceRotations = new Matrix4x4[]
        {
            Matrix4x4.CreateRotationY(0 * 90.0f.ToRadians(), Vector3.Zero),
            Matrix4x4.CreateRotationY(1 * 90.0f.ToRadians(), Vector3.Zero),
            Matrix4x4.CreateRotationY(2 * 90.0f.ToRadians(), Vector3.Zero),
            Matrix4x4.CreateRotationY(3 * 90.0f.ToRadians(), Vector3.Zero),
            Matrix4x4.CreateRotationX(1 * 90.0f.ToRadians(), Vector3.Zero),
            Matrix4x4.CreateRotationX(-1 * 90.0f.ToRadians(), Vector3.Zero),
        };

        internal readonly static Matrix4x4[] CubeFaceScales = new Matrix4x4[]
        {
                 Matrix4x4.CreateRotationY(0 * 90.0f.ToRadians(), Vector3.Zero),
                 Matrix4x4.CreateRotationY(1 * 90.0f.ToRadians(), Vector3.Zero),
                 Matrix4x4.CreateRotationY(0 * 90.0f.ToRadians(), Vector3.Zero),
                 Matrix4x4.CreateRotationY(1 * 90.0f.ToRadians(), Vector3.Zero),
                 Matrix4x4.CreateRotationX(1 * 90.0f.ToRadians(), Vector3.Zero),
                 Matrix4x4.CreateRotationX(1 * 90.0f.ToRadians(), Vector3.Zero),
        };

        // HashSet, not List: CreateFacesFromQuad (the hot path for any grid-built mesh, e.g.
        // PlanetPatchMesh -- one call per quad) used to remove consumed vertices via
        // List.RemoveAll(predicate), an O(N) scan of the WHOLE remaining list on every single call.
        // For a 160x160 vertex patch (~25600 points, ~25281 quads) that's on the order of hundreds of
        // millions of comparisons for one mesh rebuild -- easily seconds of wall-clock time, which is
        // bad enough on its own but became a whole-app freeze once introduced by an on-demand up-res:
        // GenerateData() runs under PlanetNode._rebuildLock, which the main thread also needs every
        // frame. ManaGeometryVertex has no custom Equals/GetHashCode (confirmed), so it already used
        // plain reference equality everywhere this was compared -- a HashSet with default reference
        // equality behaves identically, just with O(1) average Add/Remove instead of O(N).
        public HashSet<ManaGeometryVertex> DisconnectedPoints = new HashSet<ManaGeometryVertex>();
        public List<ManaGeometryVertex> Verts = new List<ManaGeometryVertex>();
        public List<ManaGeometryFace> Faces = new List<ManaGeometryFace>();
        public List<ManaGeometryQuad> Quads = new List<ManaGeometryQuad>();
        public List<ManaGeometryEdge> Edges = new List<ManaGeometryEdge>();

        public ManaVertexBufferObject CreateVertexBufferObject(ManaContext context, BufferUsageARB usageHint = BufferUsageARB.StaticDraw)
        {
            if (context.DeferGpuUploads)
            {
                return null!;
            }
            var meshData = CreateCpuData();
            var gpuMesh = context.RequireOpenGlDevice().CreateMesh(context, meshData, usageHint);
            if (gpuMesh is ManaOpenGlMesh openGlMesh)
            {
                return openGlMesh.VertexBufferObject;
            }

            throw new InvalidOperationException($"OpenGL mesh factory returned unsupported mesh type '{gpuMesh.GetType().Name}'.");
        }

        public ManaMeshCpuData CreateCpuData()
        {
            return new ManaMeshCpuData
            {
                Positions = Positions.ToArray(),
                Normals = Normals.ToArray(),
                Colors = Colors.ToArray(),
                TextureCoordinates0 = TexCoords0.ToArray(),
                TextureCoordinates1 = TexCoords1.ToArray(),
                Indexes = Indexes.ToArray()
            };
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

        public ManaGeometryQuad[] AddQuad(Vector3 positionOffset, Vector3 quadSize, Matrix4x4 rotation, Matrix4x4 scale, Vector4 color)
        {
            var cR = Vector3.Transform(quadSize * 0.5f, scale);
            
            var va = Vector3.Transform(new Vector3(-cR.X, -cR.Y, -cR.Z), rotation) + positionOffset;
            var vb = Vector3.Transform(new Vector3(cR.X, -cR.Y, -cR.Z), rotation) + positionOffset;
            var vc = Vector3.Transform(new Vector3(cR.X, cR.Y, -cR.Z), rotation) + positionOffset;
            var vd = Vector3.Transform(new Vector3(-cR.X, cR.Y, -cR.Z), rotation) + positionOffset;
            var normal = new Vector3(0, 0, 1).TransformNormal(rotation);
            var center = (va + vb + vc + vd) * 0.25f;
            var outward = center - positionOffset;
            if (Vector3.Dot(outward, normal) < 0.0f)
            {
                normal = -normal;
            }
            normal = Vector3.Normalize(normal);
            

            var verts = AddVertexRange
            (
                new ManaGeometryVertex()
                {
                    Position = va,
                    Normal = normal,
                    Color = color,
                    TexCoord0 = new Vector2(0, 0)
                },

                new ManaGeometryVertex()
                {
                    Position = vb,
                    Normal = normal,
                    Color = color,
                    TexCoord0 = new Vector2(1, 0)
                },

                new ManaGeometryVertex()
                {
                    Position = vc,
                    Normal = normal,
                    Color = color,
                    TexCoord0 = new Vector2(1, 1)
                },

                new ManaGeometryVertex()
                {
                    Position = vd,
                    Normal = normal,
                    Color = color,
                    TexCoord0 = new Vector2(0, 1)
                }
            );

            return CreateQuadsFromPoints(verts.ToList());
        }

        public void AddCube(Vector3 position, Vector3 cubeSize, Vector4 color)
        {
            for (int i = 0; i < 6; i++)
            {
                var rotation = CubeFaceRotations[i];
                var scaleRotation = CubeFaceScales[i];
                AddQuad(position, cubeSize, rotation, scaleRotation, color);
            }
        }

        /// <summary>
        /// Adds a cube with an arbitrary body orientation (for physics-driven cubes).
        /// </summary>
        public void AddOrientedCube(Vector3 position, Quaternion orientation, Vector3 cubeSize, Vector4 color)
        {
            var bodyMatrix = Matrix4x4.CreateFromQuaternion(orientation);
            for (int i = 0; i < 6; i++)
            {
                // Face rotation first, then body orientation
                var combinedRotation = CubeFaceRotations[i] * bodyMatrix;
                AddQuad(position, cubeSize, combinedRotation, CubeFaceScales[i], color);
            }
        }


        public void CreateFaceFromDisconnectedPoints()
        {
            CreateFacesFromPoints(DisconnectedPoints.ToList());
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
                face.ComputeNormal();

                Faces.Add(face);
                consumedPoints.Add(verts[indexA]);
                consumedPoints.Add(verts[indexB]);
                consumedPoints.Add(verts[indexC]);
            }

            verts.RemoveRange(0, consumedPoints.Count);
            foreach (var point in consumedPoints)
            {
                DisconnectedPoints.Remove(point);
            }
        }

        public void CreateQuadFromDisconnectedPoints(List<ManaGeometryVertex> verts)
        {
            CreateQuadsFromPoints(DisconnectedPoints.ToList());
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
                face.ComputeNormal();

                ManaGeometryFace face2 = new ManaGeometryFace();
                face2.Geom = this;
                face2.A = verts[indexA];
                face2.B = verts[indexC];
                face2.C = verts[indexD];
                face2.ComputeNormal();

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
            foreach (var point in consumedPoints)
            {
                DisconnectedPoints.Remove(point);
            }
            return outputQuads.ToArray();
        }

        // The triangle counterpart to CreateFacesFromQuad, for meshes whose cells aren't quads (e.g.
        // RegionTerrainMesh's hex fans). CreateFacesFromPoints can do this, but only via a List
        // allocated per triangle, which is a lot of garbage on a mesh built one triangle at a time.
        public void CreateFacesFromTriangle(ManaGeometryVertex a, ManaGeometryVertex b, ManaGeometryVertex c)
        {
            ManaGeometryFace face = new ManaGeometryFace();
            face.Geom = this;
            face.A = a;
            face.B = b;
            face.C = c;
            face.ComputeNormal();
            Faces.Add(face);

            DisconnectedPoints.Remove(a);
            DisconnectedPoints.Remove(b);
            DisconnectedPoints.Remove(c);
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
            face1.ComputeNormal();
            Faces.Add(face1);

            ManaGeometryFace face2 = new ManaGeometryFace();
            face2.Geom = this;
            face2.A = a;
            face2.B = c;
            face2.C = d;
            face2.ComputeNormal();
            Faces.Add(face2);

            DisconnectedPoints.Remove(a);
            DisconnectedPoints.Remove(b);
            DisconnectedPoints.Remove(c);
            DisconnectedPoints.Remove(d);
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
                face.ComputeNormal();

                Faces.Add(face);
            }
        }

        internal void Clear()
        {
            Verts.Clear();
            Indexes.Clear();
            Edges.Clear();
            Faces.Clear();
            Colors.Clear();
            Quads.Clear();
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
