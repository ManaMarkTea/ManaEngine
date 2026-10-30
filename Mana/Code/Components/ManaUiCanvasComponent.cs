using Mana.Code.Content;
using Mana.Code.Nodes;
using ManaUI;

namespace Mana.Code.Components;

/// <summary>A screen-space ManaUI layout on a level or prefab node, shown by the play host.</summary>
public sealed class ManaUiCanvasComponent(ManaSceneNode owner) : ManaComponent(owner)
{
    [AssetKind(UiMarkup.Extension)]
    public ManaAssetRef Layout { get; set; } = ManaAssetRef.None;

    /// <summary>The game mode whose view model (<c>HudViewModel</c>) the layout binds to; empty for a static layout.</summary>
    public string BindingSource { get; set; } = string.Empty;
    public bool Visible { get; set; } = true;

    // Announced each world update: how the play host knows what is alive, without walking every node to find out.
    public override void UpdateComponent()
    {
        base.UpdateComponent();
        Context.World.NoteUiCanvas(this);
    }

    public UiMarkupResult Build(UiTheme theme, object? viewModel = null)
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
            return UiMarkup.Load(File.ReadAllText(file), theme, viewModel);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            var failed = new UiMarkupResult();
            failed.Problems.Add(exception.Message);
            return failed;
        }
    }
}
