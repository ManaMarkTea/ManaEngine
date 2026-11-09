using Mana;
using Mana.Code;
using Mana.Code.Nodes;
using Mana.Code.Shapes;

namespace ManaEditor.Code.Services
{
    // Builds a small jumpable course out of boxes: a visual ManaCube plus a matching kinematic Bepu
    // box so the character capsule actually lands on it.
    //
    // Sized against the pawn defaults -- a 2-unit character jumping 3 units clears a 1-unit rise and a
    // ~1.5-unit gap comfortably, with room to spare for a mistimed jump.
    public class PlatformCourseService
    {
        private const float StepRise = 1.0f;
        private const float StepSpacing = 4.0f;
        private const float PlatformThickness = 0.5f;

        private static readonly Vector4 StepColor = new(0.42f, 0.60f, 0.86f, 1.0f);
        private static readonly Vector4 GapPlatformColor = new(0.92f, 0.62f, 0.28f, 1.0f);
        private static readonly Vector4 SummitColor = new(0.45f, 0.82f, 0.48f, 1.0f);

        private readonly List<ManaSceneNode> platforms = new();

        public IReadOnlyList<ManaSceneNode> Platforms => platforms;

        public void BuildCourse(ManaContext context, ManaScene scene, Vector3 origin)
        {
            var shapes = new ManaShapeFactory(context, scene);

            // Staircase: four ascending steps, each a full rise below the pawn's jump apex.
            for (var step = 0; step < 4; step++)
            {
                var centerY = (step * StepRise) + (PlatformThickness * 0.5f);
                AddPlatform(
                    context,
                    shapes,
                    origin + new Vector3(step * StepSpacing, centerY, 0.0f),
                    new Vector3(3.0f, PlatformThickness, 3.0f),
                    StepColor,
                    $"Platform Step {step + 1}");
            }

            // Gap run: same height, but spaced so each one needs an actual jump rather than a step up.
            var runHeight = (3 * StepRise) + (PlatformThickness * 0.5f);
            var runStartX = 4 * StepSpacing;
            for (var gap = 0; gap < 3; gap++)
            {
                AddPlatform(
                    context,
                    shapes,
                    origin + new Vector3(runStartX + (gap * 3.5f), runHeight, 0.0f),
                    new Vector3(2.0f, PlatformThickness, 3.0f),
                    GapPlatformColor,
                    $"Platform Gap {gap + 1}");
            }

            // Summit: one more hop up, as somewhere to aim for.
            AddPlatform(
                context,
                shapes,
                origin + new Vector3(runStartX + (3 * 3.5f) + 1.0f, runHeight + (StepRise * 1.5f), 0.0f),
                new Vector3(4.0f, PlatformThickness, 4.0f),
                SummitColor,
                "Platform Summit");
        }

        // size is the platform's full visual extent, matching Bepu's Box width/height/length.
        private void AddPlatform(
            ManaContext context,
            ManaShapeFactory shapes,
            Vector3 center,
            Vector3 size,
            Vector4 color,
            string name)
        {
            var node = shapes.AddCube(center, color, size);
            node.Name = name;

            context.Physics.AddBoxBody(node, size, Vector3.Zero);
            platforms.Add(node);
        }

        public void ClearCourse(ManaContext context)
        {
            lock (context.World.SceneLock)
            {
                foreach (var platform in platforms)
                {
                    foreach (var collision in platform.GetComponents<Mana.Code.Components.ManaCollisionComponent>())
                    {
                        context.Physics.RemoveBody(collision.Body);
                    }

                    platform.Parent?.RemoveChild(platform);
                    foreach (var scene in context.World.Scenes)
                    {
                        scene.Children.Remove(platform);
                    }
                }
            }

            platforms.Clear();
        }
    }
}
