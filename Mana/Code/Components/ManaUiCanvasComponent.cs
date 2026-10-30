using Mana.Code.Content;
using Mana.Code.Nodes;
using ManaUI;

namespace Mana.Code.Components;

/// <summary>A screen-space ManaUI layout on a level or prefab node, shown by the play host.</summary>
public sealed class ManaUiCanvasComponent(ManaSceneNode owner) : ManaComponent(owner)
{
    [AssetKind(UiMarkup.Extension)]
    public ManaAssetRef Layout { get; set; } = ManaAssetRef.None;

    /// <summary>The game mode type supplying the layout's named values and actions; empty for a static layout.</summary>
    public string BindingSource { get; set; } = string.Empty;
    public bool Visible { get; set; } = true;

    public UiMarkupResult Build(UiTheme theme, UiMarkupData? data = null)
    {
        var file = ManaContentAssets.Resolve(Layout, UiMarkup.Extension);
        if (file == null)
        {
            var missing = new UiMarkupResult();
            missing.Problems.Add($"UI layout not found: {Layout.AssetPath}");
            return missing;
        }
        try
        {
            return UiMarkup.Load(File.ReadAllText(file), theme, data);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            var failed = new UiMarkupResult();
            failed.Problems.Add(exception.Message);
            return failed;
        }
    }
}
