using Mana;
using Mana.Code;
using Mana.Code.GameModes;
using Mana.Code.UI.ManaUi;
using ManaUI;

namespace ManaVoxel.Code.UI.ManaUi
{
    /// <summary>
    /// The editor's main menu, in ManaUI. Built once against the same
    /// <see cref="VoxelEditorViewModel"/> the editor drives, rather than re-declared every frame.
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
        /// a game mode: every row here answers to the view model alone.
        /// </summary>
        public static MenuBar Build(VoxelEditorViewModel viewModel, BaseGameMode? host = null, Action? openPreferences = null, Action? openSearch = null)
        {
            return new MenuBar().AddRange(BuildMenus(viewModel, host, openPreferences, openSearch));
        }

        /// <summary>
        /// The menus alone, for a bar that carries other things too -- the workspace's own chrome sits
        /// beside these rather than in a second bar.
        /// </summary>
        public static MenuItem[] BuildMenus(VoxelEditorViewModel viewModel, BaseGameMode? host = null, Action? openPreferences = null, Action? openSearch = null)
        {
            return new[]
            {
                File(viewModel),
                Edit(viewModel, openPreferences),
                Play(viewModel),
                Character(viewModel),
                View(viewModel, host, openSearch)
            };
        }

        /// <param name="openPreferences">Opens the preferences panel. Null where there is no workspace to open
        /// one in (a bare menu under test), in which case the row is left out rather than shown dead.</param>
        private static MenuItem Edit(VoxelEditorViewModel viewModel, Action? openPreferences)
        {
            var items = new List<MenuItem>
            {
                new MenuItem("Undo", () => viewModel.History.Undo())
                    .WithShortcut("Ctrl+Z")
                    .EnabledWhen(() => viewModel.History.CanUndo),
                new MenuItem("Redo", () => viewModel.History.Redo())
                    .WithShortcut("Ctrl+Y")
                    .EnabledWhen(() => viewModel.History.CanRedo),

                MenuItem.Separator(),

                // Not commands -- the two viewport keys that are otherwise learnable only by being told.
                // A menu is where someone looks for "what can I press", so the keys that belong to no
                // button at all are the ones that most need saying somewhere.
                MenuItem.Label("Frame selected").ShortcutFrom(() => "F"),
                MenuItem.Label("Clear selection").ShortcutFrom(() => "Esc"),

                MenuItem.Separator(),

                new MenuItem("Snap to Grid", () => viewModel.Snap.Enabled = !viewModel.Snap.Enabled)
                    .Checkable(() => viewModel.Snap.Enabled)
                    .WithTooltip("Snaps placement and the move, rotate and scale gizmos. Sizes are in Preferences.")
            };

            if (openPreferences != null)
            {
                items.Add(new MenuItem("Preferences...", openPreferences));
            }

            return new MenuItem("Edit").WithItems(items.ToArray());
        }

        private static MenuItem File(VoxelEditorViewModel viewModel)
        {
            var levels = viewModel.Levels;

            return new MenuItem("File").WithItems(
                // Which level this menu is about, before the commands that act on it. It used to ride in
                // Save Level's shortcut column, which is the column a reader scans for keys -- and now that
                // Save actually has one, that column had to be given back to it.
                MenuItem.Label(string.Empty).ShortcutFrom(() => levels.CurrentLevelLabel),

                MenuItem.Separator(),

                new MenuItem("New Level", levels.New),

                // Saves under whatever name the toolbar's box holds, which is also how Save As works.
                // There is no file dialog to open here, and inventing a modal one for a name that is
                // already on screen and editable would be a second way to say the same thing.
                new MenuItem("Save Level", levels.Save)
                    .WithShortcut("Ctrl+S"),

                new MenuItem("Reload Level", levels.Reload)
                    .EnabledWhen(() => levels.CurrentPath != null),

                MenuItem.Separator(),

                new MenuItem("Place Player Start Here", levels.PlacePlayerStart)
                    .ShortcutFrom(() => levels.HasPlayerStart ? "moves the existing one" : string.Empty),

                MenuItem.Separator(),

                new MenuItem("Bake Voxels To Physics", () => viewModel.Commands.BakeToPhysics())
                    .EnabledWhen(() => !viewModel.HasBakedVoxels),

                new MenuItem("Lock Voxels For Editing", () => viewModel.Commands.LockBakedVoxelsForEditing())
                    .EnabledWhen(() => viewModel.HasBakedVoxels));
        }

        /// <summary>
        /// Play. The keys are the point of this menu as much as the rows are -- F5 works whether or not the
        /// menu is ever opened, and this is the only place that says so.
        /// </summary>
        private static MenuItem Play(VoxelEditorViewModel viewModel)
        {
            var levels = viewModel.Levels;

            return new MenuItem("Play").WithItems(
                new MenuItem("Play", levels.TogglePlay)
                    .WithShortcut("F5")
                    .EnabledWhen(() => levels.CanPlay && !levels.IsPlaying)
                    .WithTooltip("Spawns the character at the Player Start and hides the editor."),

                new MenuItem("Play From Camera", levels.PlayFromCamera)
                    .WithShortcut("Shift+F5")
                    .EnabledWhen(() => levels.CanPlay && !levels.IsPlaying)
                    .WithTooltip("Starts the character in front of the camera instead of at the Player Start."),

                new MenuItem("Play In New Window", levels.PlayInNewWindow)
                    .WithShortcut("Ctrl+F5")
                    .EnabledWhen(() => levels.CanPlay)
                    .WithTooltip("Saves the level and launches it as a standalone game in its own window."),

                new MenuItem("Stop", levels.TogglePlay)
                    .WithShortcut("Esc")
                    .EnabledWhen(() => levels.IsPlaying),

                MenuItem.Separator(),

                MenuItem.Label(string.Empty).ShortcutFrom(() => levels.Status));
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
        /// mode. C still cycles modes and Esc still releases
        /// the pawn (ManaPawnController.UpdateDriveInput reads those keys directly), but without this menu
        /// there was no way to see which mode was active, no way to jump straight to Free, and no way to
        /// discover either shortcut at all without a menu row that says so.
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
            // is possessed rather than silently doing nothing.
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

        private static MenuItem View(VoxelEditorViewModel viewModel, BaseGameMode? host, Action? openSearch)
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

                new MenuItem("Search Area...", openSearch ?? (() => viewModel.ShowSearchWindow = true)),

                MenuItem.Separator(),

                // These set a view-model flag and nothing else now. The Appearance menu's own Theme
                // submenu is what actually swaps ManaUI's palette -- see ManaUiDockDemo's ChooseTheme.
                new MenuItem("Light Theme", () => viewModel.UseLightTheme = true)
                    .Checkable(() => viewModel.UseLightTheme),

                new MenuItem("Dark Theme", () => viewModel.UseLightTheme = false)
                    .Checkable(() => !viewModel.UseLightTheme),

                new MenuItem("UI Density").WithItems(
                    new MenuItem("Comfortable", () => ManaUiDensity.SetPreset(ManaUiDensityPreset.Comfortable))
                        .Checkable(() => ManaUiDensity.CurrentPreset == ManaUiDensityPreset.Comfortable),
                    new MenuItem("Compact", () => ManaUiDensity.SetPreset(ManaUiDensityPreset.Compact))
                        .Checkable(() => ManaUiDensity.CurrentPreset == ManaUiDensityPreset.Compact)),

                // Emoji fallback, Reset Layout and the font-preset list used to sit here. All three were
                // ImGui glyph-atlas and docking concerns; ManaUI has its own Font and Layout menus, and
                // resets its own dock layout from the Layout menu.

                MenuItem.Separator(),

                new MenuItem("About", () => viewModel.ShowAboutWindow = !viewModel.ShowAboutWindow)
                    .Checkable(() => viewModel.ShowAboutWindow));
        }

        /// <summary>A checkable row that flips the thing it reports. Most of this menu is these.</summary>
        private static MenuItem Toggle(string text, Func<bool> read, Action<bool> write)
        {
            return new MenuItem(text, () => write(!read())).Checkable(read);
        }
    }
}
