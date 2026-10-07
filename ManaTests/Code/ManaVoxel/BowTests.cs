using Mana;
using Mana.Code;
using Mana.Code.Nodes;
using Mana.Code.Shapes;
using ManaUI;
using ManaVoxel.Code.Gameplay.Course;

namespace ManaTests;

/// <summary>The side-on bow: draw, loose, fly, hit an enemy, and clean up when play stops.</summary>
public static class BowChecks
{
    private const float FrameSeconds = 1.0f / 60.0f;

    private static void Step(ManaWorld world, int frames)
    {
        for (var frame = 0; frame < frames; frame++)
        {
            ManaTime.GlobalTime.DeltaTime = TimeSpan.FromSeconds(FrameSeconds);
            world.GameState.Tick(FrameSeconds);
            foreach (var node in world.DefaultScene.Children)
            {
                node.UpdateTransform();
            }
        }
    }

    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        Check("a segment through a box crosses it", EnemyComponent.SegmentCrossesBox(-5, 0, 5, 0, 0, 0, 1, 1));
        Check("one passing above it does not", !EnemyComponent.SegmentCrossesBox(-5, 3, 5, 3, 0, 0, 1, 1));
        Check("one that stops short does not", !EnemyComponent.SegmentCrossesBox(-5, 0, -2, 0, 0, 0, 1, 1));

        var world = new ManaWorld();
        var context = new ManaContext(null!, null!, world) { DeferGpuUploads = true };
        world.DefaultScene = new ManaScene(context) { Name = "Default Scene" };
        world.Scenes.Add(world.DefaultScene);
        var player = new ManaPawnNode(context, Matrix4x4.Identity) { SkinningEnabled = false };
        world.DefaultScene.Children.Add(player);
        world.GameState.Player = player;

        var enemyNode = new ManaSceneNode(context, Matrix4x4.CreateTranslation(8.0f, 0.6f, 0.0f));
        var enemy = new EnemyComponent(enemyNode) { Travel = Vector3.Zero };
        enemyNode.Components.Add(enemy);
        enemyNode.Components.Add(new MeshRendererComponent(enemyNode, new ManaCube(context, Vector4.One, Vector3.One)));
        world.DefaultScene.Children.Add(enemyNode);

        var holder = new ManaSceneNode(context, Matrix4x4.Identity);
        var bow = new BowComponent(holder);
        holder.Components.Add(bow);
        world.DefaultScene.Children.Add(holder);

        world.BeginPlay();
        Step(world, 2);
        Check("play adds the bow to the player", player.Children.Count == 1);

        bow.HoldFire = true;
        Step(world, 10);
        Check("holding the key draws", bow.IsDrawing && bow.DrawPower > 0.0f && bow.DrawPower < 1.0f, $"{bow.DrawPower}");
        Step(world, 40);
        Check("a long hold is a full draw", bow.DrawPower >= 1.0f);

        bow.HoldFire = false;
        Step(world, 1);
        Check("letting go looses an arrow", !bow.IsDrawing && bow.ArrowsInFlight == 1 && holder.Children.Count == 1);

        Step(world, 60);
        Check("the arrow defeats an enemy in its path", enemy.IsDefeated && world.GameState.EnemiesDefeated == 1);
        Check("and is used up", bow.ArrowsInFlight == 0 && holder.Children.Count == 0);

        bow.HoldFire = true;
        Step(world, 2);
        bow.HoldFire = false;
        Step(world, 1);
        Check("a quick tap still shoots", bow.ArrowsInFlight == 1);

        world.EndPlay();
        Check("stopping play removes the bow and its arrows", player.Children.Count == 0 && holder.Children.Count == 0);
    }
}
