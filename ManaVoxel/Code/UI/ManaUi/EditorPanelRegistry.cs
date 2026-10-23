using Mana;
using Mana.Code.UI.ManaUi;
using ManaCore.Graphics;
using ManaUI;
using ManaVoxel.Code.Assets;

namespace ManaVoxel.Code.UI.ManaUi;

// Registration order is also the order used when merging panels into saved layouts.
internal static class EditorPanelRegistry
{
    internal sealed record BuildContext(
        UiTheme Theme, ManaUiShowcaseViewModel Vm, Func<string>? LiveStat,
        VoxelEditorViewModel? Editor, ManaWorld? World, Func<float> ProjectFolderWidth,
        Action<ManaScene>? SaveScene, Func<ManaScene, bool>? SceneHasChanges, EditorAssetServices Assets);

    internal sealed record Definition(string Id, string Title, string? MenuGroup, int MenuOrder,
        bool OpensByDefault, Func<BuildContext, UiElement?> Build);

    internal static IReadOnlyList<Definition> Panels { get; } = Array.AsReadOnly<Definition>(
    [
        new("level", "Level", "Editor", 0, false,
            context => context.Editor != null ? LevelManaView.Build(context.Theme, context.Editor.Levels).Scrollable().Configure(viewer => viewer.Padding = new Thickness(0.0f, 4.0f, 0.0f, 4.0f)) : null),
        new("tools", "Toolbar", "Editor", 3, true,
            context => context.Editor != null ? VoxelEditorToolbarManaView.Build(context.Theme, context.Editor, context.World) : null),
        new("import", "Import", "Assets", 0, true,
            context => context.Editor != null ? MeshImportManaView.Build(context.Theme, context.Editor, context.Assets.Library, context.Assets.Thumbnails) : null),
        new("project", "Project", "Assets", 1, true,
            context => context.Editor != null && context.World != null ? ProjectManaView.Build(context.Theme, context.World, context.Editor, context.ProjectFolderWidth) : null),
        new("scenes", "Scenes", "Assets", 2, true,
            context => context.Editor != null ? ScenesManaView.Build(context.Theme, context.Editor.Levels) : null),
        new("blurtest", "Blur Test", "Debugging", 4, false,
            context => BackdropTestManaView.Build(context.Theme).Scrollable().Configure(viewer => viewer.Padding = new Thickness(0.0f, 4.0f, 0.0f, 4.0f))),
        new("outliner", "Outliner", "Editor", 1, true,
            context => context.World != null ? SceneOutlinerManaView.Build(context.Theme, context.World, context.World.Selection, context.SaveScene, context.SceneHasChanges, context.Editor?.History) : null),
        new("inspector", "Inspector", "Editor", 2, true,
            context => context.World != null ? SceneInspectorManaView.Build(
                        context.Theme,
                        context.World.Selection,
                        context.World.Context,
                        history: context.Editor?.History) : null),
        new(GridMapToolManaView.PanelId, "Grid Map", null, 0, false,
            context => context.World != null ? GridMapToolManaView.Build(context.Theme, context.World, context.Editor?.History) : null),
        new(TerrainToolManaView.PanelId, "Terrain", null, 0, false,
            context => context.World != null ? TerrainToolManaView.Build(context.Theme, context.World) : null),
        new(MeasureToolManaView.PanelId, "Measure", null, 0, false,
            context => context.Editor != null ? MeasureToolManaView.Build(context.Theme, context.Editor) : null),
        new(EditorWorkspace.VoxelToolsPanelId, "Voxel Tools", null, 0, false,
            context => context.Editor != null ? VoxelToolPaletteManaView.BuildWindow(context.Theme, context.Editor) : null),
        new(GridMapWfcManaView.PanelId, "Generator", null, 0, false,
            context => context.World != null ? GridMapWfcManaView.Build(context.Theme, context.World, context.Editor?.History) : null),
        new(SpriteAssetManaView.PanelId, "Sprite Asset", null, 0, false,
            context => SpriteAssetManaView.Build(context.Theme)),
        new(PrefabChangesManaView.PanelId, "Review Changes", null, 0, false,
            context => context.World != null ? PrefabChangesManaView.Build(context.Theme, context.World) : null),
        new(UiDesignerManaView.PanelId, "UI Designer", "Assets", 3, false,
            context => UiDesignerManaView.Build(context.Theme)),
        new("livescan", "Live Scan", "Other", 0, false,
            context => BuildLiveScan(context)),
        new("showcase", "ManaUI", "Debugging", 3, false,
            context => ManaUiShowcaseView.BuildContent(context.Theme, context.Vm, context.LiveStat).Scrollable().Configure(viewer => viewer.Padding = new Thickness(0.0f, 4.0f, 0.0f, 4.0f))),
        new("about", "Diagnostics", "Debugging", 2, false,
            context => new DynamicContent()
                    .Showing<ManaGraphicsDiagnosticsSnapshot>(
                        () => ManaUiDiagnostics.Snapshot,
                        captured => AboutManaView.Build(context.Theme, captured, ManaUiDiagnostics.BackendName, context.World?.Context.Audio))
                    .Empty(AboutManaView.Build(context.Theme, null, ManaUiDiagnostics.BackendName, context.World?.Context.Audio))),
        new("worldperf", "World Perf", "Debugging", 0, false,
            context => context.World != null ? WorldPerfManaView.Build(context.Theme, context.World).Scrollable().Configure(viewer => viewer.Padding = new Thickness(0.0f, 4.0f, 0.0f, 4.0f)) : null),
        new(RenderTargetsManaView.PanelId, "Render Targets", "Debugging", 5, false,
            context => RenderTargetsManaView.Build(context.Theme)),
        new("debug", "Debug", "Debugging", 1, false,
            context => context.Editor != null ? DebugManaView.Build(context.Theme, context.Editor) : null),
        new("preferences", "Preferences", "Settings", 0, false,
            context => context.Editor != null ? PreferencesManaView.Build(context.Theme, context.Editor) : null),
    ]);

    private static readonly IReadOnlyDictionary<string, Definition> ById =
        Panels.ToDictionary(panel => panel.Id, StringComparer.Ordinal);

    internal static IReadOnlyList<string> Ids { get; } = Array.AsReadOnly(Panels.Select(panel => panel.Id).ToArray());
    internal static IReadOnlyDictionary<string, string> Titles { get; } =
        new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(
            Panels.ToDictionary(panel => panel.Id, panel => panel.Title, StringComparer.Ordinal));

    internal static bool OpensByDefault(string id) => ById.TryGetValue(id, out var panel) && panel.OpensByDefault;

    internal static DockItem? Build(string id, BuildContext context)
    {
        if (!ById.TryGetValue(id, out var panel) || panel.Build(context) is not { } content)
        {
            return null;
        }
        return new DockItem(panel.Title, content) { Id = panel.Id };
    }

    private static UiElement? BuildLiveScan(BuildContext context)
    {
#if !MANA_BROWSER
        return context.World != null && context.Editor != null
            ? LiveScanManaView.Build(context.Theme, context.World, context.Editor) : null;
#else
        return null;
#endif
    }
}
