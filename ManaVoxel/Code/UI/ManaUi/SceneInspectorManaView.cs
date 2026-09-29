using Mana;
using Mana.Code.Components;
using Mana.Code.Nodes;
using Mana.Code.UI;
using ManaUI;
using ManaVoxel.Code.UI.ManaUi.Inspectors;

using Button = ManaUI.Button;

namespace ManaVoxel.Code.UI.ManaUi
{
    /// <summary>
    /// The scene inspector, over the live scene graph and sharing the editor's
    /// <see cref="SceneSelectionState"/> with the ImGui inspector and both outliners.
    /// <para>
    /// This is only the frame: it follows the selection and asks an
    /// <see cref="InspectorWidgetFactory"/> what to show for whatever is selected. Everything that knows
    /// about a particular kind of thing -- a transform, a material, a PBR shader's channels -- is a widget
    /// under <c>Inspectors/</c>, so adding support for a new component is a new file and a registration
    /// rather than another branch in here.
    /// </para>
    /// <para>
    /// Widgets write straight to the node. The scene graph is not a view model and never will be, so the
    /// editors are bound with polled reads and direct writes rather than through
    /// <c>INotifyPropertyChanged</c> -- the same escape hatch the outliner uses. That also means a value
    /// changed by anything else, a gizmo or an animation, shows up here without being told.
    /// </para>
    /// </summary>
    public static class SceneInspectorManaView
    {
        public static UiElement Build(
            UiTheme theme,
            SceneSelectionState selection,
            ManaContext? context = null,
            InspectorWidgetFactory? widgets = null,
            EditorUndoHistory? history = null)
        {
            var inspector = new InspectorContext(theme, context, widgets, selection, history);
            var target = new SelectionTarget(selection);

            // The tree is retained, so the panel cannot simply re-run when the selection changes -- it has
            // to notice. DynamicContent polls the selection and rebuilds only on an actual change, which is
            // what keeps a half-typed field from being thrown away every frame.
            var content = new DynamicContent()
                .Showing<SelectionTarget.Snapshot>(
                    target.Current,
                    snapshot => BuildSelection(snapshot, inspector))
                .Empty(EmptyState(theme));

            return new Grid().Rows("*").Columns("*").Padding(8.0f).Children(
                content.Scrollable().Configure(viewer => viewer.MinHeight = 120.0f));
        }

        private static UiElement EmptyState(UiTheme theme)
        {
            return new TextBlock()
                .Text("Nothing selected.")
                .Foreground(theme.TextMuted)
                .Margin(4.0f);
        }

        private static UiElement BuildSelection(SelectionTarget.Snapshot snapshot, InspectorContext inspector)
        {
            if (snapshot.Scene != null)
            {
                var sections = (StackPanel)BuildSubject(snapshot.Scene, inspector);
                if (inspector.Engine?.World is { } world)
                {
                    sections.Children.Add(new Expander().Header("Scene Settings")
                        .Section(inspector.Theme.Spacing,
                            new TextBlock().Text("Lighting settings are shared by all scenes.")
                                .Foreground(inspector.Theme.TextMuted).Wrap(),
                            IrradianceSettingsManaView.Build(inspector.Theme, world.IrradianceSettings)));
                }
                return sections;
            }

            return BuildNode(snapshot, inspector);
        }

        private static UiElement BuildSubject(object subject, InspectorContext inspector)
        {
            var sections = new StackPanel().Vertical(inspector.Theme.Spacing);

            foreach (var section in inspector.BuildAll(subject))
            {
                sections.Children.Add(section);
            }

            return sections;
        }

        private static UiElement BuildNode(SelectionTarget.Snapshot snapshot, InspectorContext inspector)
        {
            var node = snapshot.Node!;
            var sections = new StackPanel().Vertical(inspector.Theme.Spacing * 2.0f);
            sections.Children.Add(ProjectManaView.BuildInspector(inspector.Theme, node));
            sections.Children.Add(InspectorCard(
                "NODE PROPERTIES", BuildSubject(node, inspector), inspector.Theme));
            sections.Children.Add(BuildNodeActions(node, inspector));

            var showing = ComponentsToShow(node, snapshot.Component, out var hidden);

            foreach (var component in showing)
            {
                AddComponent(sections, component, inspector, ReferenceEquals(component, snapshot.Component));
            }

            // Only ever non-zero on a node carrying an unreasonable number of them. Said out loud so the
            // list is understood to be partial rather than complete.
            if (hidden > 0)
            {
                sections.Children.Add(new TextBlock()
                    .Text($"{hidden} more components on this node are not shown.")
                    .Wrap()
                    .Foreground(inspector.Theme.TextMuted)
                    .Margin(4.0f));
            }

            return sections;
        }

        private static UiElement BuildNodeActions(ManaSceneNode node, InspectorContext inspector)
        {
            var theme = inspector.Theme;
            var add = new ComboBox
            {
                Placeholder = "Add component"
            }.WithTooltip("Attach a component to this node.");
            add.SetItems(SceneComponentCatalog.Definitions, definition => definition.Label);
            add.SelectionChanged += (_, index) =>
            {
                if (index < 0 || index >= add.Items.Count)
                {
                    return;
                }

                if (add.Items[index] is SceneComponentDefinition definition)
                {
                    SceneComponentCatalog.AddToNode(node, definition, inspector.Selection, inspector.History);
                }

                add.SelectedIndex = -1;
            };

            var delete = new Button()
                .Label("🗑️")
                .Variant(ButtonVariant.Danger)
                .Enabled(inspector.Engine != null && SceneEditorOperations.CanDeleteNode(inspector.Engine.World, node))
                .WithTooltip("Delete Node — delete this node and its children from the scene.")
                .OnClick(() =>
                {
                    if (inspector.Engine != null)
                    {
                        SceneEditorOperations.DeleteNode(inspector.Engine.World, inspector.Selection, node);
                    }
                });
            delete.BindPolled(
                button => button.IsEnabled,
                () => inspector.Engine != null && SceneEditorOperations.CanDeleteNode(inspector.Engine.World, node));

            return new Grid().Columns("Auto, *, Auto", theme.Spacing).Rows("Auto").Children(
                new TextBlock()
                    .BindTextPolled(() => $"{node.Components.Count} component{(node.Components.Count == 1 ? "" : "s")}")
                    .Foreground(theme.TextMuted)
                    .CenterV()
                    .GridColumn(0),
                add.GridColumn(1),
                delete.GridColumn(2));
        }

        /// <summary>
        /// The selection as one object, so a change to either half rebuilds the panel.
        /// <para>
        /// <see cref="DynamicContent"/> compares its source by reference, deliberately: two models that
        /// compare equal are still different things to inspect. That means the source has to be a single
        /// object that stays the same instance while the selection stands still and becomes a new one when
        /// any part of it moves. Tracking only the node -- which is what this did at first -- meant picking
        /// a different component on the same node changed nothing at all, and which sections you got
        /// depended on whatever happened to be selected the last time the node itself changed.
        /// </para>
        /// </summary>
        private sealed class SelectionTarget
        {
            private readonly SceneSelectionState _selection;
            private Snapshot? _current;

            public SelectionTarget(SceneSelectionState selection)
            {
                _selection = selection;
            }

            public sealed record Snapshot(ManaScene? Scene, ManaSceneNode? Node, ManaComponent? Component, int Revision);

            public Snapshot? Current()
            {
                var node = _selection.SelectedNode;
                var component = _selection.SelectedComponent;
                var scene = node == null && component == null ? _selection.SelectedScene : null;
                if (scene == null && node == null)
                {
                    _current = null;
                    return null;
                }

                // A new instance only when something actually moved. Allocating one per poll would
                // rebuild the panel every frame and throw away half-typed fields with it.
                if (_current == null
                    || !ReferenceEquals(_current.Scene, scene)
                    || !ReferenceEquals(_current.Node, node)
                    || !ReferenceEquals(_current.Component, component)
                    || _current.Revision != _selection.SceneGraphRevision)
                {
                    _current = new Snapshot(scene, node, component, _selection.SceneGraphRevision);
                }

                return _current;
            }
        }

        /// <summary>
        /// Which of a node's components to show.
        /// <para>
        /// All of them, because a node is the thing being inspected and hiding half of it is surprising --
        /// clicking a mesh in the scene selects its renderer, and narrowing to that made everything else on
        /// the node disappear, which is how a pickup's settings became impossible to reach.
        /// </para>
        /// <para>
        /// A selected component still comes first, so picking one in the outliner brings it to the top
        /// rather than leaving it to be hunted for. The cap is for the pathological case the old rule was
        /// really written for: a planet's shared root holds one component per terrain patch, and building
        /// a section for each of several hundred is not an inspector.
        /// </para>
        /// </summary>
        private static List<ManaComponent> ComponentsToShow(
            ManaSceneNode node,
            ManaComponent? selected,
            out int hidden)
        {
            var ordered = new List<ManaComponent>(node.Components.Count);

            if ((selected is not ManaCollisionComponent || selected is ManaBoxColliderComponent)
                && selected != null && node.Components.Contains(selected))
            {
                ordered.Add(selected);
            }

            foreach (var component in node.Components)
            {
                if (component is ManaCollisionComponent and not ManaBoxColliderComponent)
                {
                    continue;
                }

                if (!ReferenceEquals(component, selected))
                {
                    ordered.Add(component);
                }
            }

            if (ordered.Count <= MaxComponentSections)
            {
                hidden = 0;
                return ordered;
            }

            hidden = ordered.Count - MaxComponentSections;
            return ordered.Take(MaxComponentSections).ToList();
        }

        /// <summary>How many component sections are worth building before the panel stops being useful.</summary>
        private const int MaxComponentSections = 12;

        private static void AddComponent(
            StackPanel sections,
            ManaComponent component,
            InspectorContext inspector,
            bool selected)
        {
            var built = inspector.BuildAll(component);
            var content = new StackPanel().Vertical(inspector.Theme.Spacing);

            // Nothing claimed it, and reflection found nothing either -- so it is a component with no
            // editable state at all. Said out loud rather than skipped: one that silently vanishes from
            // the panel looks like a bug in the selection rather than a gap in the widget set.
            if (built.Count == 0)
            {
                built.Add(InspectorControls.ReadOnly(inspector.Theme, "No inspector for this component yet."));

            }

            var primaryPromoted = false;
            foreach (var section in built)
            {
                content.Children.Add(PromotePrimaryContent(section, ref primaryPromoted));
            }

            var actions = new StackPanel().Horizontal(inspector.Theme.Spacing);

            // A name for telling apart two components of one type on a node. Empty means "unnamed"; the class name
            // stays the fallback, and is shown as the hint so the box says what it is for.
            var nameBox = new TextBox()
                .Value(component.Label ?? string.Empty)
                .Hint("name")
                .Width(120.0f);
            nameBox.OnCommitted(text =>
            {
                if (!nameBox.IsApplyingBinding)
                {
                    // Through Rename, so anything on the node that points at this component keeps pointing at it.
                    ManaComponentReference.Rename(component, text);
                }
            });
            nameBox.BindPolled(b => b.Text, () => component.Label ?? string.Empty);
            nameBox.WithTooltip("A name for this component, to tell it from another of the same type on this node.");
            actions.Children.Add(nameBox);

            if (selected)
                actions.Children.Add(new TextBlock().Text("✅").CenterV().WithTooltip("Selected"));
            actions.Children.Add(OpenSourceButton(component, inspector.Theme));
            actions.Children.Add(RemoveComponentButton(component, inspector));

            var group = new Expander
            {
                Header = ComponentDisplayName.For(component.GetType()),
                HeaderForeground = selected ? inspector.Theme.Accent : inspector.Theme.TextMuted,
                HeaderBackground = inspector.Theme.SurfaceRaised,
                HeaderFontSize = inspector.Theme.FontSizeHeading,
                HeaderAccessory = actions,
                ContentPadding = new Thickness(0.0f, 8.0f, 0.0f, 4.0f),
                Content = content
            }.WithTooltip(ComponentInheritance.Describe(component.GetType()));

            // The name follows the box: renaming a component retitles its section without rebuilding the panel.
            group.BindPolled(g => g.Header, () => ComponentInheritance.Title(component));
            sections.Children.Add(InspectorFrame(group, inspector.Theme, selected));
        }

        private static Button OpenSourceButton(ManaComponent component, UiTheme theme)
        {
            var source = ComponentSource.Find(component.GetType());
            var button = new Button()
                .Label("📝")
                .Variant(ButtonVariant.Subtle)
                .Height(MathF.Max(20.0f, theme.ControlHeight - 6.0f))
                .Enabled(source != null)
                .WithTooltip(source == null
                    ? "Open C# — local source or debug symbols are unavailable for this component."
                    : $"Open C# in VS Code: {source.Path}:{source.Line}");
            button.Padding = new Thickness(6.0f, 1.0f);
            button.OnClick(() =>
            {
                if (source != null && !ComponentSource.Open(source))
                {
                    button.WithTooltip("Could not open VS Code. Check that it is installed and the source file still exists.");
                }
            });
            return button;
        }

        private static Border InspectorCard(string label, UiElement content, UiTheme theme, bool selected = false,
            UiElement? accessory = null)
        {
            var heading = new TextBlock()
                .Text(label)
                .Foreground(selected ? theme.Accent : theme.TextMuted)
                .Margin(4.0f, 2.0f, 4.0f, 4.0f);

            var header = new Grid().Columns("*, Auto", theme.Spacing).Rows("Auto")
                .Children(heading.CenterV().GridColumn(0));
            if (accessory != null) header.Children.Add(accessory.GridColumn(1));

            return InspectorFrame(new StackPanel().Vertical(theme.Spacing).Children(header, content), theme, selected);
        }

        private static Border InspectorFrame(UiElement content, UiTheme theme, bool selected)
        {
            return new Border
            {
                Background = theme.SurfaceAlt,
                BorderColor = selected ? theme.Accent : theme.BorderStrong,
                BorderThickness = 1.0f,
                CornerRadius = theme.CornerRadius,
                Padding = new Thickness(6.0f),
                Child = content
            };
        }

        // Move the primary widget's properties under the card heading so the component name
        // occurs once. Secondary sections (materials, input settings, etc.) retain their headings.
        private static UiElement PromotePrimaryContent(UiElement section, ref bool promoted)
        {
            if (promoted) return section;
            if (section is Expander expander)
            {
                var content = expander.Content;
                if (content == null || expander.HeaderAccessory != null) return section;
                expander.Content = null;
                promoted = true;
                return content;
            }

            if (section is Panel panel)
            {
                for (var index = 0; index < panel.Children.Count && !promoted; index++)
                {
                    var child = panel.Children[index];
                    var replacement = PromotePrimaryContent(child, ref promoted);
                    if (!ReferenceEquals(child, replacement)) panel.Children[index] = replacement;
                }
            }

            return section;
        }

        private static Button RemoveComponentButton(ManaComponent component, InspectorContext inspector)
        {
            var button = new Button()
                .Label("🗑️")
                .Variant(ButtonVariant.Subtle)
                .Height(MathF.Max(20.0f, inspector.Theme.ControlHeight - 6.0f))
                .Enabled(SceneEditorOperations.CanRemoveComponent(component))
                .WithTooltip("Remove Component — remove this component from the node.")
                .OnClick(() =>
                {
                    if (inspector.History != null)
                        inspector.History.RemoveComponent(component, inspector.Selection);
                    else
                        SceneEditorOperations.RemoveComponent(inspector.Selection, component);
                });

            button.Padding = new Thickness(6.0f, 1.0f);
            button.BindPolled(
                b => b.IsEnabled,
                () => component.Owner.Components.Contains(component)
                    && SceneEditorOperations.CanRemoveComponent(component));

            return button;
        }
    }
}
