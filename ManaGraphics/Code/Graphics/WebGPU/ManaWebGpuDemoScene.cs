using System.Numerics;
using ManaCore.Graphics;

namespace ManaGraphics.Code.Graphics;

internal static class ManaWebGpuDemoScene
{
        internal static ManaRenderScene Create()
        {
            var meshData = CreateDemoMeshData();
            var worldTransform = CreateNormalizedWorldTransform(meshData);

            return new ManaRenderScene
            {
                Camera = new ManaCameraData(
                    Matrix4x4.CreateLookAt(new Vector3(0.0f, 0.55f, 4.25f), new Vector3(0.0f, 0.1f, 0.0f), Vector3.UnitY),
                    Matrix4x4.CreatePerspectiveFieldOfView((55.0f * MathF.PI / 180.0f), 16.0f / 9.0f, 0.05f, 100.0f),
                    new Vector3(0.0f, 0.55f, 4.25f),
                    0.05f,
                    100.0f),
                Meshes = new[]
                {
                    new ManaRenderMeshInstance
                    {
                        Name = "WebGPU Core Demo Cube",
                        MeshData = meshData,
                        WorldTransform = worldTransform
                    }
                }
            };
        }

        private static ManaMeshCpuData CreateDemoMeshData()
        {
            var positions = new List<Vector3>();
            var normals = new List<Vector3>();
            var colors = new List<Vector4>();
            var texCoords = new List<Vector2>();
            var indexes = new List<uint>();

            AddCubeFace(positions, normals, colors, texCoords, indexes, new Vector3(0, 0, 1), new Vector3(1, 0, 0), new Vector3(0, 1, 0), new Vector4(0.12f, 0.95f, 1.0f, 1.0f));
            AddCubeFace(positions, normals, colors, texCoords, indexes, new Vector3(0, 0, -1), new Vector3(-1, 0, 0), new Vector3(0, 1, 0), new Vector4(0.25f, 0.55f, 1.0f, 1.0f));
            AddCubeFace(positions, normals, colors, texCoords, indexes, new Vector3(1, 0, 0), new Vector3(0, 0, -1), new Vector3(0, 1, 0), new Vector4(0.95f, 0.35f, 0.22f, 1.0f));
            AddCubeFace(positions, normals, colors, texCoords, indexes, new Vector3(-1, 0, 0), new Vector3(0, 0, 1), new Vector3(0, 1, 0), new Vector4(0.95f, 0.8f, 0.22f, 1.0f));
            AddCubeFace(positions, normals, colors, texCoords, indexes, new Vector3(0, 1, 0), new Vector3(1, 0, 0), new Vector3(0, 0, -1), new Vector4(0.65f, 1.0f, 0.3f, 1.0f));
            AddCubeFace(positions, normals, colors, texCoords, indexes, new Vector3(0, -1, 0), new Vector3(1, 0, 0), new Vector3(0, 0, 1), new Vector4(0.9f, 0.45f, 1.0f, 1.0f));

            return new ManaMeshCpuData
            {
                Positions = positions.ToArray(),
                Normals = normals.ToArray(),
                Colors = colors.ToArray(),
                TextureCoordinates0 = texCoords.ToArray(),
                TextureCoordinates1 = Array.Empty<Vector2>(),
                Indexes = indexes.ToArray(),
                AlbedoFactor = Vector4.One,
                AlbedoTexturePath = null
            };
        }

        private static void AddCubeFace(
            List<Vector3> positions,
            List<Vector3> normals,
            List<Vector4> colors,
            List<Vector2> texCoords,
            List<uint> indexes,
            Vector3 normal,
            Vector3 tangent,
            Vector3 bitangent,
            Vector4 color)
        {
            var start = (uint)positions.Count;
            var center = normal * 0.5f;
            var halfTangent = tangent * 0.5f;
            var halfBitangent = bitangent * 0.5f;

            positions.Add(center - halfTangent - halfBitangent);
            positions.Add(center + halfTangent - halfBitangent);
            positions.Add(center + halfTangent + halfBitangent);
            positions.Add(center - halfTangent + halfBitangent);

            for (var i = 0; i < 4; i++)
            {
                normals.Add(normal);
                colors.Add(color);
            }

            texCoords.Add(new Vector2(0, 1));
            texCoords.Add(new Vector2(1, 1));
            texCoords.Add(new Vector2(1, 0));
            texCoords.Add(new Vector2(0, 0));

            indexes.Add(start);
            indexes.Add(start + 1);
            indexes.Add(start + 2);
            indexes.Add(start);
            indexes.Add(start + 2);
            indexes.Add(start + 3);
        }

        private static Matrix4x4 CreateNormalizedWorldTransform(ManaMeshCpuData meshData)
        {
            if (meshData.Positions.Length == 0)
            {
                return Matrix4x4.Identity;
            }

            var min = meshData.Positions[0];
            var max = meshData.Positions[0];
            foreach (var position in meshData.Positions)
            {
                min = Vector3.Min(min, position);
                max = Vector3.Max(max, position);
            }

            var center = (min + max) * 0.5f;
            var extents = max - min;
            var maxExtent = MathF.Max(extents.X, MathF.Max(extents.Y, extents.Z));
            var scale = maxExtent <= 0.0001f ? 1.0f : 2.0f / maxExtent;
            return Matrix4x4.CreateTranslation(-center) * Matrix4x4.CreateScale(scale);
        }

}
