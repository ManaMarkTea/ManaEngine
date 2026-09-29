using Mana;
using Mana.Code;
using Mana.Code.GameModes;
using Mana.Code.Levels;
using Mana.Code.Nodes;
using ManaUI;

namespace ManaTests;

/// <summary>
/// Click-to-select resolution: a raw ray pick almost never lands on the node that should actually get
/// selected -- an imported FlightHelmet is one <see cref="ManaLevelSource"/> on its root and none on the
/// Lens/Metal/etc. children a click actually hits, so the picked node has to be walked up to the whole
/// authored object first. What makes this worth a suite of its own rather than folding into the pick
/// tests: a second click, while that object is already the selection, needs to invert the walk and drill
/// straight to the specific part instead -- two behaviours from one function, verified against a small
/// synthetic hierarchy rather than only by clicking a real window.
/// </summary>
public static class SceneSelectionDrillDownChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        var world = new ManaWorld();
        var context = new ManaContext(null!, null!, world) { DeferGpuUploads = true };

        static ManaSceneNode Node(ManaContext context, string name) =>
            new(context, System.Numerics.Matrix4x4.Identity) { Name = name };

        // One authored object (FlightHelmet-shaped: a root the importer tagged, two untagged mesh-part
        // children) and a second, separate authored object to prove switching targets resets the walk.
        var helmetRoot = Node(context, "FlightHelmet");
        helmetRoot.LevelSource = ManaLevelSource.ForMesh("FlightHelmet.gltf");
        var lens = Node(context, "Lens");
        var metal = Node(context, "Metal");
        helmetRoot.AddChild(lens);
        helmetRoot.AddChild(metal);

        var crateRoot = Node(context, "Crate");
        crateRoot.LevelSource = ManaLevelSource.ForMesh("crate.glb");

        // A coin: a group that carries the behaviour, with a model child that is what gets clicked. The coin is the
        // object, so its settings have to be reachable from a click on its model.
        var coin = Node(context, "Coin");
        coin.LevelSource = ManaLevelSource.Group;
        coin.Components.Add(new ManaPickupMotionComponent(coin));
        var coinModel = Node(context, "Coin Model");
        coinModel.LevelSource = ManaLevelSource.ForMesh("coin.glb");
        coin.AddChild(coinModel);
        Check("clicking a model inside a group that carries behaviour selects the group, where the behaviour is",
            ReferenceEquals(VoxelEditorGameMode.ResolveEditorSelectableNode(coinModel, null), coin));
        Check("and a further click drills to the model itself",
            ReferenceEquals(VoxelEditorGameMode.ResolveEditorSelectableNode(coinModel, coin), coinModel));

        // An ordinary group (a section, a category) carries nothing, so a click still picks the thing in it.
        var category = Node(context, "Pickups");
        category.LevelSource = ManaLevelSource.Group;
        var crate = Node(context, "Container");
        crate.LevelSource = ManaLevelSource.ForMesh("container.glb");
        category.AddChild(crate);
        Check("but a group with no behaviour does not swallow what is inside it",
            ReferenceEquals(VoxelEditorGameMode.ResolveEditorSelectableNode(crate, null), crate));

        // A grid map's tiles are built by the editor from its cells; clicking one with the map selected
        // must keep the map selected, or its tool windows close.
        var map = Node(context, "Dungeon");
        map.LevelSource = ManaLevelSource.GridMap;
        var tile = Node(context, "corridor (0, 0, 0)");
        tile.IsEditorTransient = true;
        var tilePart = Node(context, "corridor");
        tilePart.IsEditorTransient = true;
        map.AddChild(tile);
        tile.AddChild(tilePart);
        Check("clicking a grid map tile selects the map",
            ReferenceEquals(VoxelEditorGameMode.ResolveEditorSelectableNode(tilePart, null), map));
        Check("and clicking one again with the map selected keeps the map, rather than drilling into the tile",
            ReferenceEquals(VoxelEditorGameMode.ResolveEditorSelectableNode(tilePart, map), map));

        Check("clicking any part with nothing selected resolves up to the whole object",
            ReferenceEquals(VoxelEditorGameMode.ResolveEditorSelectableNode(lens, null), helmetRoot),
            "expected the FlightHelmet root, not the Lens child");

        Check("clicking a different part with the whole object already selected drills to that part",
            ReferenceEquals(VoxelEditorGameMode.ResolveEditorSelectableNode(metal, helmetRoot), metal),
            "expected the Metal child, not a re-selected root");

        Check("clicking a sibling part while already drilled into a different part keeps drilling, not bouncing back to the root",
            ReferenceEquals(VoxelEditorGameMode.ResolveEditorSelectableNode(lens, metal), lens),
            "expected the Lens child directly, since Metal's own authored root is still the same FlightHelmet");

        Check("clicking a genuinely different object always selects its whole object first, even with a part of another object selected",
            ReferenceEquals(VoxelEditorGameMode.ResolveEditorSelectableNode(crateRoot, lens), crateRoot),
            "expected the Crate root, not drilled-into behaviour carried over from the unrelated FlightHelmet selection");

        Check("clicking empty space (no pick) clears rather than reselecting",
            VoxelEditorGameMode.ResolveEditorSelectableNode(null, helmetRoot) == null,
            "expected null");

        var untagged = Node(context, "Untagged");
        Check("an untagged node with no LevelSource anywhere up its chain selects itself",
            ReferenceEquals(VoxelEditorGameMode.ResolveEditorSelectableNode(untagged, null), untagged),
            "expected the node itself as a fallback, since FindAuthoredRoot found nothing to walk up to");
    }
}
