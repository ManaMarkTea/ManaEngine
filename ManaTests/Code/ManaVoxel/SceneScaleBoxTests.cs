using Mana;
using Mana.Code;
using Mana.Code.Components;
using Mana.Code.GameModes;
using Mana.Code.Nodes;
using Mana.Code.Shapes;
using ManaUI;

namespace ManaTests;

/// <summary>The Scale tool's box: which handles a flat sprite and a solid get, and the box itself, in the node's own frame.</summary>
public static class SceneScaleBoxChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        static bool Near(Vector3 a, Vector3 b) => Vector3.Distance(a, b) < 0.001f;

        var flat = SceneScaleBox.Handles(new Vector3(-1, 0, 0), new Vector3(1, 2, 0));
        Check("a flat box has four edges and four corners, none along its flat axis",
            flat.Count == 8 && flat.All(h => h.Z == 0), string.Join(" ", flat));
        Check("its sides are edges, grabbed along their length",
            SceneScaleBox.TryEdge(new SceneScaleBoxHandle(1, 0, 0), new Vector3(-1, 0, 0), new Vector3(1, 2, 0), out var start, out var end)
            && Near(start, new Vector3(1, 0, 0)) && Near(end, new Vector3(1, 2, 0)), $"{start} {end}");
        Check("a corner is not an edge",
            !SceneScaleBox.TryEdge(new SceneScaleBoxHandle(1, 1, 0), new Vector3(-1, 0, 0), new Vector3(1, 2, 0), out _, out _));

        var solid = SceneScaleBox.Handles(-Vector3.One, Vector3.One);
        Check("a solid box has six faces and eight corners", solid.Count == 14, string.Join(" ", solid));
        Check("whose faces are dots", !SceneScaleBox.TryEdge(new SceneScaleBoxHandle(0, 1, 0), -Vector3.One, Vector3.One, out _, out _));

        Check("handles by name for scripts",
            SceneScaleBoxHandle.Parse("-y") == new SceneScaleBoxHandle(0, -1, 0)
            && SceneScaleBoxHandle.Parse("+X-y") == new SceneScaleBoxHandle(1, -1, 0)
            && SceneScaleBoxHandle.Parse("y") == null && SceneScaleBoxHandle.Parse("") == null);

        // The box ignores the node's own scale and turn (they are what the handles change) but not a child's offset.
        var world = new ManaWorld();
        var context = new ManaContext(null!, null!, world) { DeferGpuUploads = true };
        world.Context = context;
        var node = new ManaSceneNode(context, Matrix4x4.Identity)
        {
            Position = new Vector3(10, 0, 0), Rotation = new Vector3(0, 0.7f, 0), Scale = new Vector3(3, 2, 1)
        };
        node.UpdateTransform();
        var child = new ManaSceneNode(context, Matrix4x4.Identity) { Position = new Vector3(0, 0.5f, 0) };
        child.Components.Add(new MeshRendererComponent(child, new ManaCube(context, Vector4.One, new Vector3(2, 1, 0.5f))));
        node.AddChild(child);
        child.UpdateTransform();
        var found = SceneScaleBox.TryLocalBounds(node, out var min, out var max);
        Check("the box is in the node's own frame",
            found && Near(min, new Vector3(-1, 0, -0.25f)) && Near(max, new Vector3(1, 1, 0.25f)), $"{min} {max}");
    }
}
