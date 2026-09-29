using ManaUI;
using ManaVoxel.Code.Tools;

// ManaVoxel has a global `using Silk.NET.Input`, which also defines Button.
using Button = ManaUI.Button;

namespace ManaVoxel.Code.UI.ManaUi
{
    /// <summary>Voxel toolbar details and physics debug controls.</summary>
    public static class VoxelEditorPanelsManaView
    {
        /// <summary>Voxel counters, without renderer performance diagnostics.</summary>
        public static UiElement BuildStats(UiTheme theme, VoxelEditorViewModel viewModel)
        {
            return SessionStats(theme, viewModel);
        }

        /// <summary>The active node and what the pointer is over.</summary>
        public static UiElement BuildPlacement(UiTheme theme, VoxelEditorViewModel viewModel)
        {
            return EditorTab(theme, viewModel);
        }

        /// <summary>The same without a scroller of its own, for the Voxel Tools window, which scrolls as a whole.</summary>
        public static UiElement BuildNodeAndPlacement(UiTheme theme, VoxelEditorViewModel viewModel)
        {
            return NodeAndPlacement(theme, viewModel);
        }

        /// <summary>Explosion and reassembly tuning, and the bake actions.</summary>
        public static UiElement BuildPhysics(UiTheme theme, VoxelEditorViewModel viewModel)
        {
            return PhysicsTab(theme, viewModel);
        }

        /// <summary>
        /// The session counters. A table in ImGui; here a column of rows, which is the same thing without
        /// a table's borders competing with the panel's own.
        /// </summary>
        private static UiElement SessionStats(UiTheme theme, VoxelEditorViewModel viewModel)
        {
            var rows = new StackPanel().Vertical(2.0f);

            foreach (var (label, read) in new (string, Func<int>)[]
            {
                ("Voxels", () => viewModel.VoxelCount),
                ("Voxel Nodes", () => viewModel.VoxelLayers.Count),
                ("Active Node Voxels", () => viewModel.ActiveVoxelCount),
                ("Created This Session", () => viewModel.CreatedThisSessionVoxelCount),
                ("Pending Voxels", () => viewModel.PendingVoxelCount),
                ("Duplicate Skips", () => viewModel.DuplicateVoxelRejectCount),
                ("Physics Baked Voxels", () => viewModel.BakedPhysicsVoxelCount)
            })
            {
                rows.Children.Add(Counter(theme, label, read));
            }

            return new Expander().Header("Session Stats").Content(rows);
        }

        private static UiElement Counter(UiTheme theme, string label, Func<int> read)
        {
            return new Grid().Columns("*, Auto", theme.Spacing).Rows("Auto").Children(
                new TextBlock().Text(label).Foreground(theme.TextMuted).Ellipsis().GridColumn(0),
                new TextBlock()
                    .BindTextPolled(() => read().ToString("N0"))
                    .FontFamily(theme.FontFamilyMono)
                    .GridColumn(1));
        }

        private static UiElement EditorTab(UiTheme theme, VoxelEditorViewModel viewModel) =>
            NodeAndPlacement(theme, viewModel).Padding(8.0f).Scrollable();

        private static StackPanel NodeAndPlacement(UiTheme theme, VoxelEditorViewModel viewModel)
        {
            // Rebuilt when the set of layers changes, since a combo's options are fixed once built and a
            // node added or removed has to show up.
            var activeNode = new DynamicContent()
                .Showing<string>(
                    () => string.Join('', viewModel.VoxelLayerNames),
                    _ => ActiveNodePicker(viewModel))
                .Empty(new TextBlock().Text("None").Foreground(theme.TextMuted));

            return new StackPanel().Vertical(theme.Spacing).Children(
                Section(theme, "Active Voxel Node"),
                activeNode,

                Section(theme, "Placement"),
                Placement(theme, viewModel),
                BuildStats(theme, viewModel));
        }

        private static UiElement ActiveNodePicker(VoxelEditorViewModel viewModel)
        {
            var names = viewModel.VoxelLayerNames;
            if (names.Length == 0)
            {
                return new TextBlock().Text("None");
            }

            var combo = new ComboBox().Options(names);
            combo.SelectedIndex = Math.Clamp(viewModel.ActiveVoxelLayerIndex, 0, names.Length - 1);

            combo.SelectionChanged += (_, _) =>
            {
                if (!combo.IsApplyingBinding)
                {
                    viewModel.ActiveVoxelLayerIndex = combo.SelectedIndex;
                }
            };

            combo.BindPolled(
                c => c.SelectedIndex,
                () => Math.Clamp(viewModel.ActiveVoxelLayerIndex, 0, Math.Max(0, viewModel.VoxelLayerNames.Length - 1)));

            return combo;
        }

        /// <summary>
        /// What the pointer is over. Every row polls, because this changes with the mouse rather than with
        /// anything that could announce itself.
        /// </summary>
        private static UiElement Placement(UiTheme theme, VoxelEditorViewModel viewModel)
        {
            var rows = new StackPanel().Vertical(2.0f);

            rows.Children.Add(Readout(theme, "Cell", () =>
            {
                if (!viewModel.HasHoverVoxel)
                {
                    return "-";
                }

                var cell = VoxelRasterizer.ToCell(viewModel.HoverVoxelPosition);
                return $"{cell.X}, {cell.Y}, {cell.Z}";
            }));

            rows.Children.Add(Readout(theme, "Center", () => viewModel.HasHoverVoxel
                ? Format(viewModel.HoverVoxelPosition)
                : "-"));

            rows.Children.Add(Readout(theme, "Delta From Last", () => viewModel.HasHoverDelta
                ? Format(viewModel.HoverVoxelPosition - viewModel.LastCommittedVoxelPosition!.Value)
                : "-"));

            var empty = new TextBlock()
                .Text("No hovered voxel")
                .Foreground(theme.TextMuted);

            empty.BindPolled(t => t.Visibility, () => viewModel.HasHoverVoxel ? Visibility.Collapsed : Visibility.Visible);
            rows.BindPolled(r => r.Visibility, () => viewModel.HasHoverVoxel ? Visibility.Visible : Visibility.Collapsed);

            return new Grid().Rows("Auto").Columns("*").Children(rows, empty);
        }

        private static string Format(Vector3 value) => $"{value.X:0.##}, {value.Y:0.##}, {value.Z:0.##}";

        private static UiElement Readout(UiTheme theme, string label, Func<string> read)
        {
            return new Grid().Columns("112, *", theme.Spacing).Rows("Auto").Children(
                new TextBlock().Text(label).Foreground(theme.TextMuted).Ellipsis().GridColumn(0),
                new TextBlock().BindTextPolled(read).FontFamily(theme.FontFamilyMono).Ellipsis().GridColumn(1));
        }

        private static UiElement PhysicsTab(UiTheme theme, VoxelEditorViewModel viewModel)
        {
            var tuning = new StackPanel().Vertical(theme.Spacing);

            foreach (var (label, read, write, minimum, maximum) in new (string, Func<float>, Action<float>, float, float)[]
            {
                ("Explosion Strength", () => viewModel.BakeExplosionStrength, v => viewModel.BakeExplosionStrength = v, 1.0f, 150.0f),
                ("Click Explosion Scale", () => viewModel.ClickExplosionStrengthScale, v => viewModel.ClickExplosionStrengthScale = v, 0.02f, 10.0f),
                ("Explosion Radius Padding", () => viewModel.BakeExplosionRadiusPadding, v => viewModel.BakeExplosionRadiusPadding = v, 0.0f, 6.0f),
                ("Reassemble Pull", () => viewModel.ReassemblePullGain, v => viewModel.ReassemblePullGain = v, 0.0f, 30.0f),
                ("Reassemble Damping", () => viewModel.ReassembleVelocityDamping, v => viewModel.ReassembleVelocityDamping = v, 0.0f, 0.99f),
                ("Reassemble Rotation Lerp", () => viewModel.ReassembleRotationLerp, v => viewModel.ReassembleRotationLerp = v, 0.0f, 1.0f),
                ("Settle Distance", () => viewModel.ReassembleSettleDistance, v => viewModel.ReassembleSettleDistance = v, 0.01f, 0.5f),
                ("Settle Velocity", () => viewModel.ReassembleSettleVelocity, v => viewModel.ReassembleSettleVelocity = v, 0.01f, 2.0f)
            })
            {
                tuning.Children.Add(Slider(theme, label, read, write, minimum, maximum));
            }

            var bake = new Button().Label("Bake All Voxel Nodes To Physics").StretchH()
                .OnClick(() => viewModel.Commands.BakeToPhysics());
            bake.BindPolled(b => b.IsEnabled, () => viewModel.HasEditableVoxels);

            var reassemble = new Button().Label("Reassemble All Baked Voxels").StretchH()
                .OnClick(() => viewModel.Commands.BeginReassembleBakedVoxels());

            var lockAll = new Button().Label("Lock All Voxels For Editing").StretchH()
                .OnClick(() => viewModel.Commands.LockBakedVoxelsForEditing());

            var explode = new Button().Label("Explode All Baked Voxels").StretchH()
                .OnClick(() => viewModel.Commands.ExplodeBakedVoxels());

            foreach (var button in new[] { reassemble, lockAll, explode })
            {
                button.BindPolled(b => b.IsEnabled, () => viewModel.HasBakedVoxels);
            }

            return new StackPanel().Vertical(theme.Spacing).Padding(8.0f).Children(
                Section(theme, "Tuning"),
                tuning,

                Section(theme, "Bake"),
                bake,

                Section(theme, "Lock / Overlap"),
                new CheckBox().Label("Preview Lock Snap")
                    .Checked(viewModel.ShowLockPreview)
                    .OnChanged(value => viewModel.ShowLockPreview = value),

                new TextBlock()
                    .BindTextPolled(() => viewModel.ShowLockPreview
                        ? $"Preview voxels {viewModel.GetLockPreviewVoxelCount():N0}, overlaps {viewModel.GetLockPreviewOverlapCount():N0}"
                        : "Preview off")
                    .Foreground(theme.TextMuted)
                    .FontSize(theme.FontSizeSmall),

                Section(theme, "Actions"),
                reassemble,
                lockAll,
                explode,

                new TextBlock()
                    .BindTextPolled(() => viewModel.IsReassembling ? "Reassembly: in progress" : "Reassembly: idle")
                    .Foreground(theme.TextMuted)
                    .FontSize(theme.FontSizeSmall)).Scrollable();
        }

        private static UiElement Slider(
            UiTheme theme,
            string label,
            Func<float> read,
            Action<float> write,
            float minimum,
            float maximum)
        {
            var slider = new SliderBox
            {
                Minimum = minimum,
                Maximum = maximum,
                Step = (maximum - minimum) / 200.0f,
                DecimalPlaces = 3
            };

            slider.Value = read();
            slider.ValueChanged += (_, value) =>
            {
                if (!slider.IsApplyingBinding)
                {
                    write(value);
                }
            };

            slider.BindPolled(s => s.Value, read);

            return new Grid().Columns("150, *", theme.Spacing).Rows("Auto").Children(
                new TextBlock().Text(label).Foreground(theme.TextMuted).Ellipsis().CenterV().GridColumn(0),
                slider.GridColumn(1));
        }

        /// <summary>A titled rule, which is what ImGui's SeparatorText draws.</summary>
        private static UiElement Section(UiTheme theme, string title)
        {
            return new Grid().Columns("Auto, *", theme.Spacing).Rows("Auto").Margin(0.0f, 6.0f, 0.0f, 0.0f).Children(
                new TextBlock()
                    .Text(title)
                    .Foreground(theme.TextMuted)
                    .FontSize(theme.FontSizeSmall)
                    .FontFamily(theme.FontFamilyMedium)
                    .GridColumn(0),

                new Border().Fill(theme.Divider).Height(1.0f).CenterV().GridColumn(1));
        }
    }
}
