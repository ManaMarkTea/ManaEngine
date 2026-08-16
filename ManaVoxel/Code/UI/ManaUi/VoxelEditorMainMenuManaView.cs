using Mana;
using Mana.Code;
using Mana.Code.GameModes;
using Mana.Code.UI.ManaUi;
using ManaUI;

namespace ManaVoxel.Code.UI.ManaUi
{
    /// <summary>
    /// The editor's main menu, in ManaUI. Built once against the same
    /// <see cref="VoxelEditorViewModel"/> the ImGui menu drove, rather than re-declared every frame.
    /// <para>
    /// Every tick and enabled state is a lambda over the view model, so the menu reflects the editor
    /// without anything having to tell it. That is the one thing an immediate-mode menu gets for free and
    /// a retained one has to arrange, and it is why the rows here read almost the same as the originals.
    /// </para>
    /// </summary>
    public static class VoxelEditorMainMenuManaView
    {
        /// <summary>
        /// Builds the bar. <paramref name="host"/> is nullable so the menu can be built and driven without
        /// a game mode: the rows that need one are the ImGui-era font and theme settings, which are the
        /// rows due to disappear with ImGui, while everything else answers to the view model alone.
        /// </summary>
        public static MenuBar Build(VoxelEditorViewModel viewModel, BaseGameMode? host = null)
        {
            return new MenuBar().AddRange(BuildMenus(viewModel, host));
        }

        /// <summary>
        /// The menus alone, for a bar that carries other things too -- the workspace's own chrome sits
        /// beside these rather than in a second bar.
        /// </summary>
        public static MenuItem[] BuildMenus(VoxelEditorViewModel viewModel, BaseGameMode? host = null)
        {
            return new[]
            {
                File(viewModel),
                Character(viewModel),
                View(viewModel, host)
            };
        }

        private static MenuItem File(VoxelEditorViewModel viewModel)
        {
            return new MenuItem("File").WithItems(
                new MenuItem("Bake Voxels To Physics", () => viewModel.Commands.BakeToPhysics())
                    .EnabledWhen(() => !viewModel.HasBakedVoxels),

                new MenuItem("Lock Voxels For Editing", () => viewModel.Commands.LockBakedVoxelsForEditing())
                    .EnabledWhen(() => viewModel.HasBakedVoxels));
        }

        private static MenuItem Character(VoxelEditorViewModel viewModel)
        {
            var menu = new MenuItem("Character").WithItems(
                new MenuItem("Spawn Character", () => viewModel.Commands.SpawnCharacter()),

                new MenuItem("Next Character", () => viewModel.Commands.CycleControlledCharacter(1))
                    .WithShortcut("Num +")
                    .EnabledWhen(() => viewModel.PawnController?.PossessedPawn != null),

                new MenuItem("Previous Character", () => viewModel.Commands.CycleControlledCharacter(-1))
                    .WithShortcut("Num -")
                    .EnabledWhen(() => viewModel.PawnController?.PossessedPawn != null),

                new MenuItem("Remove All Characters", () => viewModel.Commands.RemoveAllCharacters())
                    .EnabledWhen(() => viewModel.PawnController is { Pawns.Count: > 0 }),

                MenuItem.Label(CharacterSummary(viewModel))
                    .ShortcutFrom(() => ControllingStatus(viewModel)),

                MenuItem.Separator(),

                new MenuItem("Spawn Platform Course", () => viewModel.Commands.SpawnPlatformCourse()),

                new MenuItem("Remove Platform Course", () => viewModel.Commands.RemovePlatformCourse())
                    .EnabledWhen(() => viewModel.HasPlatformCourse),

                new MenuItem(
                        "Physics Capsule (new spawns)",
                        () => viewModel.CharacterPhysicsEnabled = !viewModel.CharacterPhysicsEnabled)
                    .Checkable(() => viewModel.CharacterPhysicsEnabled)
                    .WithTooltip(
                        "On: the character gets a Bepu capsule, falls under gravity, collides with "
                        + "platforms and can jump with Space.\nOff: the old flat-ground walker."),

                MenuItem.Separator(),

                new MenuItem(
                        "Click To Move",
                        () => SetIfControllerPresent(viewModel, c => c.ClickToMoveEnabled = !c.ClickToMoveEnabled))
                    .Checkable(() => viewModel.PawnController?.ClickToMoveEnabled ?? false)
                    .EnabledWhen(() => viewModel.PawnController != null),

                new MenuItem(
                        "WASD Drive",
                        () => SetIfControllerPresent(viewModel, c => c.KeyboardDriveEnabled = !c.KeyboardDriveEnabled))
                    .Checkable(() => viewModel.PawnController?.KeyboardDriveEnabled ?? false)
                    .EnabledWhen(() => viewModel.PawnController != null)
                    .WithTooltip(
                        "While a character is possessed, WASD drives it instead of the camera.\n"
                        + "Q/E and mouse orbit still move the camera. Esc releases."),

                new MenuItem("Release Character", () => viewModel.PawnController?.Possess(null))
                    .WithShortcut("Esc")
                    .EnabledWhen(() => viewModel.PawnController?.PossessedPawn != null),

                MenuItem.Separator(),

                CameraModeMenu(viewModel));

            return menu;
        }

        /// <summary>
        /// This is the ManaUI-side fix for what was otherwise the only way to leave a spring-arm camera
        /// mode: the ImGui Character menu's "Camera" submenu. C still cycles modes and Esc still releases
        /// the pawn (ManaPawnController.UpdateDriveInput reads those keys directly), but without this menu
        /// there was no way to see which mode was active, no way to jump straight to Free, and no way to
        /// discover either shortcut at all once ImGui's own menu bar stopped being the one on screen.
        /// </summary>
        private static MenuItem CameraModeMenu(VoxelEditorViewModel viewModel)
        {
            return new MenuItem("Camera").WithItems(
                CameraModeItem(viewModel, ManaCameraMode.Free, "Free Camera",
                    "Editor fly camera. WASD/QE, right-drag to look."),
                CameraModeItem(viewModel, ManaCameraMode.Follow, "Follow (Spring Arm)",
                    "Boom behind the character that swings around as it moves."),
                CameraModeItem(viewModel, ManaCameraMode.Orbit, "Orbit (Spring Arm)",
                    "Same boom, but it stays where you leave it."),

                MenuItem.Separator(),
                MenuItem.Label("C cycles modes"),
                MenuItem.Label("Right-drag orbits, scroll zooms the boom"),
                MenuItem.Label(string.Empty).ShortcutFrom(() => SpringArmStatus(viewModel)));
        }

        private static MenuItem CameraModeItem(VoxelEditorViewModel viewModel, ManaCameraMode mode, string label, string tooltip)
        {
            // The spring-arm modes need something to hang off, so they stay greyed out until a character
            // is possessed rather than silently doing nothing -- same rule the ImGui version used.
            return new MenuItem(label, () => viewModel.PawnController?.SetCameraMode(mode))
                .Checkable(() => viewModel.PawnController?.CameraMode == mode)
                .EnabledWhen(() => mode == ManaCameraMode.Free || viewModel.PawnController?.PossessedPawn != null)
                .WithTooltip(tooltip);
        }

        private static void SetIfControllerPresent(VoxelEditorViewModel viewModel, Action<ManaPawnController> apply)
        {
            if (viewModel.PawnController is { } controller)
            {
                apply(controller);
            }
        }

        private static string SpringArmStatus(VoxelEditorViewModel viewModel)
        {
            var springArm = viewModel.PawnController?.SpringArm;
            if (springArm == null)
            {
                return string.Empty;
            }

            return $"Arm {springArm.CurrentArmLength:0.0} / {springArm.ArmLength:0.0}"
                + (springArm.IsProbeShortened ? " (pulled in)" : string.Empty);
        }

        private static string CharacterSummary(VoxelEditorViewModel viewModel)
        {
            var packs = viewModel.CharacterCatalog.Select(asset => asset.PackName).Distinct().Count();
            return $"{viewModel.CharacterCatalog.Count} characters across {packs} pack(s)";
        }

        private static string ControllingStatus(VoxelEditorViewModel viewModel)
        {
            var controller = viewModel.PawnController;
            if (controller?.PossessedPawn == null)
            {
                return controller == null ? string.Empty : "Click a character to control it";
            }

            return $"Controlling: {controller.PossessedPawn.Name} (WASD, Shift run, Space jump)";
        }

        private static MenuItem View(VoxelEditorViewModel viewModel, BaseGameMode? host)
        {
            // Logarithmic: useful lengths span a voxel-scale scene and a kilometre-scale region.
            var normalLength = new SliderBox
            {
                Minimum = 0.01f,
                Maximum = 1000.0f,
                Step = 0.05f,
                DecimalPlaces = 2,
                Width = 200.0f
            };

            normalLength.Value = viewModel.VertexNormalLength;
            normalLength.ValueChanged += (_, value) =>
            {
                if (!normalLength.IsApplyingBinding)
                {
                    viewModel.VertexNormalLength = value;
                }
            };

            normalLength.BindPolled(s => s.Value, () => viewModel.VertexNormalLength);
            normalLength.BindPolled(s => s.IsEnabled, () => viewModel.ShowVertexNormals);

            return new MenuItem("View").WithItems(
                Toggle("Show Lightning Ball", () => viewModel.ShowLightningBall, v => viewModel.ShowLightningBall = v),
                Toggle("Show Liquid Ball", () => viewModel.ShowLiquidBall, v => viewModel.ShowLiquidBall = v),
                Toggle("Show Water", () => viewModel.ShowWater, v => viewModel.ShowWater = v),
                Toggle("Show Wireframe", () => viewModel.ShowWireframe, v => viewModel.ShowWireframe = v),
                Toggle("Show Vertex Normals", () => viewModel.ShowVertexNormals, v => viewModel.ShowVertexNormals = v),

                new MenuItem("Normal Length") { Content = normalLength },

                Toggle("Shade By Normal", () => viewModel.ShowNormals, v => viewModel.ShowNormals = v),

                // Stays enabled while loading so the toggle can be switched back off mid-load; the sync
                // pass just will not attach the node when it eventually arrives. Disabled only when the
                // (non-repo) KML file is not on this machine at all.
                new MenuItem("Show Airspace (KML)", () => viewModel.ShowKmlAirspace = !viewModel.ShowKmlAirspace)
                    .Checkable(() => viewModel.ShowKmlAirspace)
                    .EnabledWhen(() => viewModel.KmlAirspaceAvailable)
                    .ShortcutFrom(() => viewModel.KmlAirspaceLoading ? "Loading..." : string.Empty),

                new MenuItem("Search Location...", () => viewModel.ShowSearchWindow = !viewModel.ShowSearchWindow)
                    .Checkable(() => viewModel.ShowSearchWindow),

                MenuItem.Separator(),

                new MenuItem("Light Theme", () =>
                    {
                        viewModel.UseLightTheme = true;
                        host?.ApplyLightImGuiTheme();
                    })
                    .Checkable(() => viewModel.UseLightTheme),

                new MenuItem("Dark Theme", () =>
                    {
                        viewModel.UseLightTheme = false;
                        host?.ApplyDarkImGuiTheme();
                    })
                    .Checkable(() => !viewModel.UseLightTheme),

                new MenuItem("UI Density").WithItems(
                    new MenuItem("Comfortable", () => ManaUiDensity.SetPreset(ManaUiDensityPreset.Comfortable))
                        .Checkable(() => ManaUiDensity.CurrentPreset == ManaUiDensityPreset.Comfortable),
                    new MenuItem("Compact", () => ManaUiDensity.SetPreset(ManaUiDensityPreset.Compact))
                        .Checkable(() => ManaUiDensity.CurrentPreset == ManaUiDensityPreset.Compact)),

                new MenuItem("Emoji Fallback (Experimental)",
                        () => host?.SetEmojiFallbackEnabled(!host.EmojiFallbackEnabled))
                    .Checkable(() => host?.EmojiFallbackEnabled ?? false)
                    .EnabledWhen(() => host != null),

                new MenuItem("Reset Layout", () => host?.ResetImGuiLayoutToDefaults())
                    .EnabledWhen(() => host != null),

                MenuItem.Separator(),

                Font(host),

                MenuItem.Separator(),

                new MenuItem("About", () => viewModel.ShowAboutWindow = !viewModel.ShowAboutWindow)
                    .Checkable(() => viewModel.ShowAboutWindow));
        }

        /// <summary>
        /// The font presets the host reports it can actually load, rather than a hardcoded list -- the
        /// original named twenty and hid the ones that were missing, which is the same thing said twice.
        /// </summary>
        private static MenuItem Font(BaseGameMode? host)
        {
            var font = new MenuItem("Font").WithItems(
                new MenuItem("Monospace Size").WithItems(
                    new MenuItem("Small", () => host?.SetImGuiMonoSizeTier(BaseGameMode.ImGuiMonoSizeTier.Small))
                        .Checkable(() => host?.CurrentMonoSizeTier == BaseGameMode.ImGuiMonoSizeTier.Small),
                    new MenuItem("Regular", () => host?.SetImGuiMonoSizeTier(BaseGameMode.ImGuiMonoSizeTier.Regular))
                        .Checkable(() => host?.CurrentMonoSizeTier == BaseGameMode.ImGuiMonoSizeTier.Regular)),

                MenuItem.Separator());

            var available = host?.GetAvailableImGuiFontPresets();

            foreach (var (preset, label) in FontPresets)
            {
                if (available != null && !available.Contains(preset))
                {
                    continue;
                }

                var chosen = preset;
                font.Items.Add(new MenuItem(label, () => host?.RequestImGuiFontPreset(chosen))
                    .Checkable(() => host?.CurrentImGuiFontPreset == chosen));
            }

            font.Items.Add(new MenuItem("Default", () => host?.RequestImGuiFontPreset(BaseGameMode.ImGuiFontPreset.Default))
                .Checkable(() => host?.CurrentImGuiFontPreset == BaseGameMode.ImGuiFontPreset.Default));

            return font;
        }

        private static readonly (BaseGameMode.ImGuiFontPreset Preset, string Label)[] FontPresets =
        {
            (BaseGameMode.ImGuiFontPreset.SegoeUI, "Segoe UI"),
            (BaseGameMode.ImGuiFontPreset.SegoeSemibold, "Segoe UI Semibold"),
            (BaseGameMode.ImGuiFontPreset.Bahnschrift, "Bahnschrift"),
            (BaseGameMode.ImGuiFontPreset.BarlowCondensed, "Barlow Condensed"),
            (BaseGameMode.ImGuiFontPreset.Roboto, "Roboto"),
            (BaseGameMode.ImGuiFontPreset.RobotoSemiCondensed, "Roboto Semi Condensed"),
            (BaseGameMode.ImGuiFontPreset.RobotoCondensed, "Roboto Condensed"),
            (BaseGameMode.ImGuiFontPreset.Calibri, "Calibri"),
            (BaseGameMode.ImGuiFontPreset.Candara, "Candara"),
            (BaseGameMode.ImGuiFontPreset.Corbel, "Corbel"),
            (BaseGameMode.ImGuiFontPreset.Verdana, "Verdana"),
            (BaseGameMode.ImGuiFontPreset.Tahoma, "Tahoma"),
            (BaseGameMode.ImGuiFontPreset.Trebuchet, "Trebuchet"),
            (BaseGameMode.ImGuiFontPreset.SegoeUIMono, "Segoe UI Mono"),
            (BaseGameMode.ImGuiFontPreset.Consolas, "Consolas (Mono)"),
            (BaseGameMode.ImGuiFontPreset.CascadiaMono, "Cascadia Mono"),
            (BaseGameMode.ImGuiFontPreset.JetBrainsMono, "JetBrains Mono"),
            (BaseGameMode.ImGuiFontPreset.FiraCode, "Fira Code"),
            (BaseGameMode.ImGuiFontPreset.SourceCodePro, "Source Code Pro"),
            (BaseGameMode.ImGuiFontPreset.CourierNew, "Courier New (Mono)")
        };

        /// <summary>A checkable row that flips the thing it reports. Most of this menu is these.</summary>
        private static MenuItem Toggle(string text, Func<bool> read, Action<bool> write)
        {
            return new MenuItem(text, () => write(!read())).Checkable(read);
        }
    }
}
