using Mana.Code.Materials;
using Mana.Code.Nodes;
using ManaCore.Diagnostics;

namespace Mana.Code.Models
{
    /// <summary>
    /// A game's models and prefabs, each read once (<see cref="ManaModel"/>) and placed as shared copies; a copy's
    /// "Owner ..." parts can be painted in a player's colour.
    /// </summary>
    public sealed class ManaModelLibrary(ManaContext context, Func<string, ManaContext, ManaScene, ManaSceneNode>? loader = null)
    {
        private readonly Dictionary<string, ManaModel> _models = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<(ManaMaterial, Vector4), ManaMaterial> _painted = new();

        /// <summary>The model, loaded the first time it is asked for -- or null when it cannot be (logged once).</summary>
        public ManaModel? Model(string asset)
        {
            if (!_models.TryGetValue(asset, out var model))
            {
                model = ManaModel.Load(asset, context, loader);
                _models[asset] = model;
                if (!model.HasGeometry)
                {
                    ManaLog.WriteDiagnostic($"Model '{asset}' did not load: {model.Error}");
                }
            }

            if (model.HasGeometry)
            {
                return model;
            }
            return null;
        }

        /// <summary>A copy of the model placed by <paramref name="placement"/>, or null when it cannot be loaded.</summary>
        public ManaSceneNode? Place(string asset, Matrix4x4 placement, string? name = null) =>
            Model(asset)?.Instantiate(context, name ?? Path.GetFileNameWithoutExtension(asset), placement);

        /// <summary>Draws a placed copy's "Owner ..." parts in <paramref name="color"/> (each material painted once per colour).</summary>
        public void Paint(ManaSceneNode placed, Vector4 color)
        {
            foreach (var part in ManaModel.MeshNodes(placed).Where(node => ManaModel.IsOwnerPart(node.Name)))
            {
                part.MeshRenderer.Material = Painted(part.MeshRenderer.Material, color);
            }
        }

        /// <summary>A copy of <paramref name="source"/> in <paramref name="color"/>: the colour multiplies the mesh's own.</summary>
        public ManaMaterial Painted(ManaMaterial source, Vector4 color)
        {
            if (!_painted.TryGetValue((source, color), out var material))
            {
                material = (ManaMaterial)source.Clone();
                material.Albedo.Value = color;
                _painted[(source, color)] = material;
            }
            return material;
        }
    }
}
