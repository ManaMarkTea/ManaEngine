using Mana;
using Mana.Code.GameModes;
using ManaEditor.Code.Levels;
using ManaUI;
using Button = ManaUI.Button;

namespace ManaEditor.Code.UI.ManaUi.Inspectors
{
    /// <summary>
    /// A level's game modes, shown when its scene is selected: add one, remove one, and set each one's settings --
    /// so two levels can run the same game differently. Changes are the level's like any other edit: the scene reads
    /// as unsaved until it is saved.
    /// </summary>
    public sealed class GameModesWidget : InspectorWidget<ManaScene>
    {
        // Replaced on every add or remove: the list below rebuilds when it changes (DynamicContent compares by reference).
        private sealed class Version
        {
        }

        protected override UiElement Build(ManaScene scene, InspectorContext context)
        {
            var theme = context.Theme;
            var version = new Version();
            var settings = new ReflectedPropertiesWidget { Title = _ => "Settings" };

            var list = new DynamicContent().Showing<Version>(() => version, _ =>
            {
                var rows = new StackPanel().Vertical(theme.Spacing);
                if (scene.GameModes.Count == 0)
                {
                    rows.Children.Add(new TextBlock()
                        .Text("None: playing this level spawns the character at the Player Start.")
                        .Foreground(theme.TextMuted)
                        .Wrap());
                }

                foreach (var mode in scene.GameModes.ToList())
                {
                    var definition = ManaLevelGameModes.Find(mode.Type);
                    var header = new DockPanel().FillLast();
                    header.Children.Add(new Button().Label("Remove").OnClick(() =>
                    {
                        scene.GameModes.Remove(mode);
                        version = new Version();
                    }).DockTo(Dock.Right));
                    header.Children.Add(new TextBlock().Text(ManaLevelGameModes.DisplayName(mode)).FontSize(theme.FontSizeHeading).CenterV());
                    rows.Children.Add(header);
                    if (definition != null)
                    {
                        rows.Children.Add(new TextBlock().Text(definition.Description).Foreground(theme.TextMuted).Wrap());
                    }

                    if (settings.CanInspect(mode))
                    {
                        rows.Children.Add(settings.Build(mode, context));
                    }
                }

                // Only modes the level does not run yet; one of each is plenty.
                var addable = ManaLevelGameModes.Available.Where(d => scene.GameModes.All(m => m.Type != d.Type)).ToList();
                if (addable.Count > 0)
                {
                    var choice = 0;
                    var picker = new ComboBox().Options(addable, d => d.DisplayName).Selected(0).OnSelected(index => choice = index);
                    var add = new DockPanel().FillLast();
                    add.Children.Add(new Button().Label("Add").OnClick(() =>
                    {
                        if (choice >= 0 && choice < addable.Count && ManaLevelGameModes.Create(addable[choice].Type) is { } created)
                        {
                            scene.GameModes.Add(created);
                            version = new Version();
                        }
                    }).DockTo(Dock.Right).Margin(theme.Spacing, 0.0f, 0.0f, 0.0f));
                    add.Children.Add(picker);
                    rows.Children.Add(InspectorControls.Row(context, "Add game mode", add,
                        "A game mode is the level's rules, run when it is played. Several can run at once."));
                }

                return rows;
            });

            return new Expander().Header("Game modes").Content(list);
        }
    }
}
