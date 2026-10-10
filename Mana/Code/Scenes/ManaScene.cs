using System.Diagnostics;
using Mana.Code;
using Mana.Code.Nodes;
using ManaCore.Graphics;

namespace Mana
{
    public class ManaScene : ManaObject
    {
        public List<ManaSceneNode> Children { get; set; }

        /// <summary>
        /// The level JSON this scene was opened from, or null for runtime/import scenes. Editor-only
        /// metadata: the level file itself remains just the authored nodes it contains.
        /// </summary>
        public string? LevelPath { get; set; }

        /// <summary>
        /// Editor-only snapshot of the level-backed scene content at the last open/save. Used for dirty
        /// indicators; it deliberately excludes camera pose so navigating the editor does not mark a level.
        /// </summary>
        public string? SavedLevelContentJson { get; set; }

        /// <summary>The sky block the level file was opened with (a ManaLevelSky), kept so saving writes it back.</summary>
        public object? AuthoredSky { get; set; }

        /// <summary>The level's game modes (its rules), with their settings; saved with the level and run on Play.</summary>
        public List<Mana.Code.GameModes.ManaLevelGameMode> GameModes { get; } = new();

        /// <summary>Where the level is published (the web build's level selector); saved with the level.</summary>
        public ManaScenePublishing Publishing { get; set; } = new();


        public ManaScene(ManaContext context) : base(context)
        {
            Children = new List<ManaSceneNode>();
        }

        public void Update()
        {
            foreach (var node in Children)
            {
                node.UpdateTransform();
            }
        }

        public void Draw()
        {
            foreach (var node in Children)
            {
                node.Draw();
            }
        }

        public void CollectRenderMeshInstances(List<ManaRenderMeshInstance> output)
        {
            foreach (var node in Children)
            {
                node.CollectRenderMeshInstances(output);
            }
        }

    }
}
