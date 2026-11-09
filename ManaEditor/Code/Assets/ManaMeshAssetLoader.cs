using System.IO.Compression;
using Mana;
using Mana.Code.Components;
using Mana.Code.Nodes;
using Mana.Code.Shapes;
using ManaEditor.Code.Services;

namespace ManaEditor.Code.Assets
{
    /// <summary>Loads loose and packed glTF models, retaining reusable templates within one world.</summary>
    public sealed class ManaMeshAssetLoader
    {
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ManaWorld, ManaMeshAssetLoader> Loaders = new();
        private readonly ManaWorld world;
        private readonly Dictionary<string, ManaSceneNode?> _templates = new();

        private ManaMeshAssetLoader(ManaWorld world) { this.world = world; }

        public static ManaMeshAssetLoader For(ManaWorld world) => Loaders.GetValue(world, w => new ManaMeshAssetLoader(w));

        public ManaSceneNode Load(string assetReference, ManaScene? into = null)
        {
            var key = MeshTemplateKey(assetReference);
            if (key == null)
            {
                return ReadMeshAsset(assetReference, world, into);
            }
            var templates = _templates;
            ManaSceneNode? template;
            lock (templates)
            {
                templates.TryGetValue(key, out template);
            }
            if (template == null)
            {
                var holding = new ManaScene(world.Context);
                var read = ReadMeshAsset(assetReference, world, holding);
                holding.Children.Remove(read);
                if (!IsPlainModel(read))
                {
                    // Rigged or otherwise more than meshes: each one is read afresh, as before.
                    Adopt(world, read, into);
                    return read;
                }
                lock (templates)
                {
                    templates[key] = template = read;
                }
            }
            var copy = CopyModel(template, world.Context);
            Adopt(world, copy, into);
            return copy;
        }

        private static string? MeshTemplateKey(string assetReference)
        {
            try
            {
                var file = ZipAssetReferences.TryDecodeZip(assetReference, out var zipPath, out _) ? zipPath : ManaAssetPaths.Resolve(assetReference);
                return File.Exists(file) ? $"{assetReference}|{File.GetLastWriteTimeUtc(file).Ticks}" : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Only plain nodes and mesh renderers -- what <see cref="CopyModel"/> can copy faithfully.</summary>
        private static bool IsPlainModel(ManaSceneNode node) =>
            node.GetType() == typeof(ManaSceneNode)
            && node.Components.All(component => component.GetType() == typeof(MeshRendererComponent))
            && node.Children.All(IsPlainModel);

        // The meshes are shared; each renderer gets its own material, as a fresh read gave it, so editing one copy's
        // material in place (the emission and material script commands) leaves the others alone.
        private static ManaSceneNode CopyModel(ManaSceneNode template, ManaContext context)
        {
            var node = new ManaSceneNode(context, template.LocalTransform) { Name = template.Name };
            foreach (var renderer in template.Components.OfType<MeshRendererComponent>())
            {
                node.Components.Add(new MeshRendererComponent(node, renderer.Mesh) { Material = (Mana.Code.Materials.ManaMaterial)renderer.Material.Clone() });
            }
            foreach (var child in template.Children)
            {
                node.AddChild(CopyModel(child, context));
            }
            return node;
        }

        // Where a read puts a model: in the given scene, or in a scene of its own in the world (the caller moves it out).
        private static void Adopt(ManaWorld world, ManaSceneNode node, ManaScene? into)
        {
            lock (world.SceneLock)
            {
                if (into != null)
                {
                    into.Children.Add(node);
                }
                else
                {
                    var scene = new ManaScene(world.Context) { Name = node.Name };
                    scene.Children.Add(node);
                    world.Scenes.Add(scene);
                }
            }
        }

        private static ManaSceneNode ReadMeshAsset(string assetReference, ManaWorld world, ManaScene? into)
        {
            if (ZipAssetReferences.TryDecodeZip(assetReference, out var zipPath, out var entryPath))
            {
                using var archive = ZipFile.OpenRead(zipPath);
                var entry = ZipAssetReferences.FindZipEntry(archive, entryPath)
                    ?? throw new FileNotFoundException($"Entry '{entryPath}' is no longer inside '{zipPath}'.", zipPath);

                using var entryStream = entry.Open();
                using var memory = new MemoryStream();
                entryStream.CopyTo(memory);

                var sidecarResolver = ZipModelSidecars.CreateZipSidecarResolver(archive, entryPath);
                return world.LoadFromGltfMemory(memory.ToArray(), entryPath, sidecarResolver, into: into);
            }

            assetReference = ManaAssetPaths.Resolve(assetReference);

            if (!File.Exists(assetReference))
            {
                throw new FileNotFoundException($"Mesh asset '{assetReference}' is missing.", assetReference);
            }

            return world.LoadFromGltf(assetReference, into: into);
        }

    }
}
