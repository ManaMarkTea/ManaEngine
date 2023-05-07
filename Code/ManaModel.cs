using Mana.Code;
using MetaRenderer.Code;
using Silk.NET.Assimp;
using Silk.NET.OpenGL;
using static System.Formats.Asn1.AsnWriter;
using AssimpMesh = Silk.NET.Assimp.Mesh;
using AssimpTexture = Silk.NET.Assimp.Texture;

namespace Mana
{
    public class ManaModel : ManaObject, IDisposable
    {
        public ManaContext Context;

        public ManaModel(ManaContext context) : base (context)
        {
            this.Context = context;
        }

        private List<ManaTexture> _texturesLoaded = new List<ManaTexture>();
        public string Directory { get; protected set; } = string.Empty;
        public List<ManaMesh> Meshes { get; protected set; } = new List<ManaMesh>();

        public unsafe void LoadFromGltf(AssimpMesh* mesh)
        {
            Meshes.Add(ProcessMesh(mesh));
        }

        private unsafe ManaMesh ProcessMeshMana(AssimpMesh* mesh)
        {
            // data to fill
            List<ManaVertex> vertices = new List<ManaVertex>();
            List<uint> indices = new List<uint>();
            List<ManaTexture> textures = new List<ManaTexture>();

            // walk through each of the mesh's vertices
            for (uint i = 0; i < mesh->MNumVertices; i++)
            {

            }

            return new ManaMesh(Context);
        }
                
        private unsafe ManaMesh ProcessMesh(AssimpMesh* mesh)
        {
            // data to fill
            List<ManaVertex> vertices = new List<ManaVertex>();
            List<uint> indices = new List<uint>();
            List<ManaTexture> textures = new List<ManaTexture>();

            // walk through each of the mesh's vertices
            for (uint i = 0; i < mesh->MNumVertices; i++)
            {
                ManaVertex vertex = new ManaVertex();
                vertex.BoneIds = new int[ManaVertex.MAX_BONE_INFLUENCE];
                vertex.Weights = new float[ManaVertex.MAX_BONE_INFLUENCE];
                Vector3 vector = new Vector3(); // we declare a placeholder vector since assimp uses its own vector class that doesn't directly convert to glm's vec3 class so we transfer the data to this placeholder glm::vec3 first.
                                                // positions
                vector.X = mesh->MVertices[i].X;
                vector.Y = mesh->MVertices[i].Y;
                vector.Z = mesh->MVertices[i].Z;
                vertex.Position = vector;
                // normals

                if (mesh->MNormals != null)
                {
                    vector.X = mesh->MNormals[i].X;
                    vector.Y = mesh->MNormals[i].Y;
                    vector.Z = mesh->MNormals[i].Z;
                    vertex.Normal = vector;
                }
                // texture coordinates
                if (mesh->MTextureCoords[0] != null) // does the mesh contain texture coordinates?
                {
                    Vector2 vec = new Vector2();
                    // a vertex can contain up to 8 different texture coordinates. We thus make the assumption that we won't 
                    // use models where a vertex can have multiple texture coordinates so we always take the first set (0).
                    vec.X = mesh->MTextureCoords[0][i].X;
                    vec.Y = mesh->MTextureCoords[0][i].Y;
                    vertex.TexCoord0 = vec;
                    // tangent
                    if (mesh->MTangents != null)
                    {
                        vector.X = mesh->MTangents[i].X;
                        vector.Y = mesh->MTangents[i].Y;
                        vector.Z = mesh->MTangents[i].Z;
                        vertex.Tangent = vector;
                    }
                    // bitangent
                    if (mesh->MBitangents != null)
                    {
                        vector.X = mesh->MBitangents[i].X;
                        vector.Y = mesh->MBitangents[i].Y;
                        vector.Z = mesh->MBitangents[i].Z;
                        vertex.Bitangent = vector;
                    }
                }
                else
                {
                    vertex.TexCoord0 = new Vector2(0.0f, 0.0f);
                }

                vertices.Add(vertex);
            }
            // now wak through each of the mesh's faces (a face is a mesh its triangle) and retrieve the corresponding vertex indices.
            for (uint i = 0; i < mesh->MNumFaces; i++)
            {
                Face face = mesh->MFaces[i];
                // retrieve all indices of the face and store them in the indices vector
                for (uint j = 0; j < face.MNumIndices; j++)
                {
                    indices.Add(face.MIndices[j]);
                }
            }
           

            return null;
        }        

        private float[] BuildVertices(List<ManaVertex> vertexCollection)
        {
            var vertices = new List<float>();

            foreach (var vertex in vertexCollection)
            {
                vertices.Add(vertex.Position.X);
                vertices.Add(vertex.Position.Y);
                vertices.Add(vertex.Position.Z);
                vertices.Add(vertex.TexCoord0.X);
                vertices.Add(vertex.TexCoord0.Y);
            }

            return vertices.ToArray();
        }

        private uint[] BuildIndices(List<uint> indices)
        {
            return indices.ToArray();
        }

        public void Dispose()
        {
            foreach (var mesh in Meshes)
            {
                mesh.Dispose();
            }

            _texturesLoaded = null;
        }
    }
}
