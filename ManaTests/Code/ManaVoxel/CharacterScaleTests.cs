using Mana;
using Mana.Code;
using Mana.Code.Nodes;
using Mana.Code.Shapes;
using ManaUI;
using ManaVoxel.Code.Services;

namespace ManaTests;

/// <summary>
/// The player in Play is the same size as the model placed at 1x, and moves at a pace that suits that size.
/// <para>
/// Spawning used to rescale every character to two units tall, so a mini character (about 0.75 native) came
/// out nearly three times bigger in Play than it looked in the editor.
/// </para>
/// </summary>
public static class CharacterScaleChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        static bool Near(float a, float b, float tolerance = 0.02f) => MathF.Abs(a - b) <= tolerance;

        var world = new ManaWorld();
        var context = new ManaContext(null!, null!, world) { DeferGpuUploads = true };
        world.Context = context;
        world.DefaultScene = new ManaScene(context) { Name = "Default Scene" };
        world.Scenes.Add(world.DefaultScene);

        static ManaPawnNode PawnOfHeight(ManaContext context, float height, float scale = 1.0f)
        {
            var pawn = new ManaPawnNode(context, Matrix4x4.Identity);
            var body = new ManaSceneNode(context, Matrix4x4.Identity) { Name = "Body" };
            body.Components.Add(new MeshRendererComponent(body, new ManaCube(context, Vector4.One, new Vector3(0.5f, height, 0.5f))));
            pawn.AddChild(body);
            pawn.Scale = new Vector3(scale);
            pawn.UpdateTransform();
            return pawn;
        }

        // ---------------------------------------------------------------- the scaling itself
        var reference = PawnOfHeight(context, ManaPawnNode.ReferenceHeight);
        reference.ScaleLocomotionToHeight();
        Check("a pawn as tall as the height the speeds were tuned for keeps them",
            Near(reference.WalkSpeed, ManaPawnNode.DefaultWalkSpeed)
            && Near(reference.RunSpeed, ManaPawnNode.DefaultRunSpeed)
            && Near(reference.JumpHeight, ManaPawnNode.DefaultJumpHeight),
            $"{reference.WalkSpeed} / {reference.RunSpeed} / {reference.JumpHeight}");

        var small = PawnOfHeight(context, 1.0f);
        small.ScaleLocomotionToHeight();
        Check("half the height is half the speed and half the jump",
            Near(small.WalkSpeed, ManaPawnNode.DefaultWalkSpeed * 0.5f)
            && Near(small.RunSpeed, ManaPawnNode.DefaultRunSpeed * 0.5f)
            && Near(small.JumpHeight, ManaPawnNode.DefaultJumpHeight * 0.5f),
            $"{small.WalkSpeed} / {small.RunSpeed} / {small.JumpHeight}");

        var tall = PawnOfHeight(context, 2.7f);
        tall.ScaleLocomotionToHeight();
        Check("a taller one is faster",
            tall.WalkSpeed > ManaPawnNode.DefaultWalkSpeed && Near(tall.WalkSpeed, ManaPawnNode.DefaultWalkSpeed * 1.35f),
            $"{tall.WalkSpeed}");

        var scaledUp = PawnOfHeight(context, 1.0f, scale: 2.0f);
        scaledUp.ScaleLocomotionToHeight();
        Check("the pawn's own scale counts, not just its model's",
            Near(scaledUp.WalkSpeed, ManaPawnNode.DefaultWalkSpeed), $"{scaledUp.WalkSpeed}");

        small.ScaleLocomotionToHeight();
        Check("applying it twice does not compound", Near(small.WalkSpeed, ManaPawnNode.DefaultWalkSpeed * 0.5f), $"{small.WalkSpeed}");

        // ---------------------------------------------------------------- real characters
        var service = new CharacterSpawnService();
        if (service.Catalog.Count == 0)
        {
            Check("no character packs on this machine, so spawning is not checked", true);
            return;
        }

        var checkedAny = false;
        foreach (var asset in service.Catalog.GroupBy(a => a.PackName).Select(g => g.First()))
        {
            var pawn = service.Spawn(asset, world, Vector3.Zero, null, usePhysics: false);
            if (pawn == null)
            {
                Check($"{asset} spawns", false, service.LastError ?? "");
                continue;
            }

            checkedAny = true;
            pawn.TryGetBindPoseHeight(out var nativeHeight);
            Check($"{asset} is spawned at the model's own size, not resized",
                Near(pawn.Scale.X, 1.0f) && Near(pawn.Scale.Y, 1.0f) && Near(pawn.Scale.Z, 1.0f),
                $"scale {pawn.Scale}, native height {nativeHeight:0.###}");
            // Relative, not absolute: the height is measured after the animation pose has updated, a fraction
            // of a percent away from the bind-pose height the speeds were scaled from.
            var expectedWalk = ManaPawnNode.DefaultWalkSpeed * nativeHeight / ManaPawnNode.ReferenceHeight;
            Check($"{asset} moves at a pace to suit its height",
                MathF.Abs(pawn.WalkSpeed - expectedWalk) <= expectedWalk * 0.02f,
                $"walk {pawn.WalkSpeed:0.##} for {nativeHeight:0.##} tall");
        }

        Check("at least one character was spawned", checkedAny);

        // The mini-dungeon hero is under a metre tall; a level built for two units asks for him at two.
        var heroIndex = service.IndexOf("kenney_mini-dungeon", "character-human");
        if (heroIndex >= 0)
        {
            var hero = service.Spawn(service.Catalog[heroIndex], world, Vector3.Zero, null, usePhysics: false, targetHeight: ManaPawnNode.ReferenceHeight);
            hero!.TryGetBindPoseHeight(out var heroLocalHeight);
            var heroWorldHeight = heroLocalHeight * hero.Scale.Y;
            Check("a character asked for a height is scaled to it", Near(heroWorldHeight, ManaPawnNode.ReferenceHeight),
                $"{heroWorldHeight:0.###} tall, scale {hero.Scale.Y:0.###}, native {heroLocalHeight:0.###}");
            Check("and then moves and jumps like the reference character",
                Near(hero.WalkSpeed, ManaPawnNode.DefaultWalkSpeed) && Near(hero.JumpHeight, ManaPawnNode.DefaultJumpHeight),
                $"walk {hero.WalkSpeed:0.##}, jump {hero.JumpHeight:0.##}");
        }

        // Play with a start that names no character still gets the start's height: a metre unless it says.
        var start = new ManaPlayerStartNode(context, Vector3.Zero);
        Check("a new player start asks for a one-metre character", Near(start.CharacterHeight, ManaPlayerStartNode.DefaultCharacterHeight));
        foreach (var (label, from) in new[] { ("a start that names no character", start), ("no start at all", (ManaPlayerStartNode?)null) })
        {
            var player = service.SpawnFor(from, world, Vector3.Zero, null, usePhysics: false);
            player!.TryGetBindPoseHeight(out var local);
            Check($"{label} plays at a metre, whichever character comes up", Near(local * player.Scale.Y, 1.0f),
                $"{player.Name}: {local * player.Scale.Y:0.###} tall");
        }

        start.CharacterHeight = 0.0f;
        var ownSize = service.SpawnFor(start, world, Vector3.Zero, null, usePhysics: false);
        Check("and a height of 0 keeps the model's own size", ownSize != null && Near(ownSize.Scale.Y, 1.0f), $"{ownSize?.Scale}");
    }
}
