using ImGuiNET;
using Mana;
using Mana.Code.GameModes;

namespace ManaVoxel.Code.UI
{
    public class VoxelEditorMainMenuView
    {
        public void Draw(VoxelEditorViewModel viewModel, BaseGameMode uiHost)
        {
            if (!ImGui.BeginMainMenuBar())
            {
                return;
            }

            if (ImGui.BeginMenu("File"))
            {
                if (ImGui.MenuItem("Bake Voxels To Physics", string.Empty, false, !viewModel.HasBakedVoxels))
                {
                    viewModel.Commands.BakeToPhysics();
                }

                if (ImGui.MenuItem("Lock Voxels For Editing", string.Empty, false, viewModel.HasBakedVoxels))
                {
                    viewModel.Commands.LockBakedVoxelsForEditing();
                }

                ImGui.Separator();
                if (ImGui.MenuItem("Show ImGui Demo", string.Empty, viewModel.ShowImGuiDemoWindow))
                {
                    viewModel.ShowImGuiDemoWindow = !viewModel.ShowImGuiDemoWindow;
                }

                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("Character"))
            {
                DrawCharacterMenu(viewModel);
                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("View"))
            {
                if (ImGui.MenuItem("Show Lightning Ball", string.Empty, viewModel.ShowLightningBall))
                {
                    viewModel.ShowLightningBall = !viewModel.ShowLightningBall;
                }

                if (ImGui.MenuItem("Show Liquid Ball", string.Empty, viewModel.ShowLiquidBall))
                {
                    viewModel.ShowLiquidBall = !viewModel.ShowLiquidBall;
                }

                if (ImGui.MenuItem("Show Water", string.Empty, viewModel.ShowWater))
                {
                    viewModel.ShowWater = !viewModel.ShowWater;
                }

                if (ImGui.MenuItem("Show Wireframe", string.Empty, viewModel.ShowWireframe))
                {
                    viewModel.ShowWireframe = !viewModel.ShowWireframe;
                }

                if (ImGui.MenuItem("Show Vertex Normals", string.Empty, viewModel.ShowVertexNormals))
                {
                    viewModel.ShowVertexNormals = !viewModel.ShowVertexNormals;
                }

                // Logarithmic: useful lengths span a voxel-scale scene and a kilometre-scale region.
                ImGui.BeginDisabled(!viewModel.ShowVertexNormals);
                ImGui.SetNextItemWidth(160.0f);
                ImGui.DragFloat(
                    "Normal Length",
                    ref viewModel.VertexNormalLength,
                    0.05f,
                    0.01f,
                    1000.0f,
                    "%.2f",
                    ImGuiSliderFlags.Logarithmic);
                ImGui.EndDisabled();

                if (ImGui.MenuItem("Shade By Normal", string.Empty, viewModel.ShowNormals))
                {
                    viewModel.ShowNormals = !viewModel.ShowNormals;
                }

                // Stays enabled while loading so the toggle can be switched back off mid-load; the sync
                // pass just won't attach the node when it eventually arrives. Disabled only when the
                // (non-repo) KML file isn't on this machine at all.
                if (ImGui.MenuItem(
                        "Show Airspace (KML)",
                        viewModel.KmlAirspaceLoading ? "Loading..." : string.Empty,
                        viewModel.ShowKmlAirspace,
                        viewModel.KmlAirspaceAvailable))
                {
                    viewModel.ShowKmlAirspace = !viewModel.ShowKmlAirspace;
                }

                if (ImGui.MenuItem("Search Location...", string.Empty, viewModel.ShowSearchWindow))
                {
                    viewModel.ShowSearchWindow = !viewModel.ShowSearchWindow;
                }

                ImGui.Separator();

                if (ImGui.MenuItem("Light Theme", string.Empty, viewModel.UseLightTheme))
                {
                    viewModel.UseLightTheme = true;
                    uiHost.ApplyLightImGuiTheme();
                }

                if (ImGui.MenuItem("Dark Theme", string.Empty, !viewModel.UseLightTheme))
                {
                    viewModel.UseLightTheme = false;
                    uiHost.ApplyDarkImGuiTheme();
                }

                if (ImGui.BeginMenu("UI Density"))
                {
                    if (ImGui.MenuItem("Comfortable", string.Empty, ManaUiDensity.CurrentPreset == ManaUiDensityPreset.Comfortable))
                    {
                        ManaUiDensity.SetPreset(ManaUiDensityPreset.Comfortable);
                    }

                    if (ImGui.MenuItem("Compact", string.Empty, ManaUiDensity.CurrentPreset == ManaUiDensityPreset.Compact))
                    {
                        ManaUiDensity.SetPreset(ManaUiDensityPreset.Compact);
                    }

                    ImGui.EndMenu();
                }

                if (ImGui.MenuItem("Emoji Fallback (Experimental)", string.Empty, uiHost.EmojiFallbackEnabled))
                {
                    uiHost.SetEmojiFallbackEnabled(!uiHost.EmojiFallbackEnabled);
                }

                if (ImGui.MenuItem("Reset Layout"))
                {
                    uiHost.ResetImGuiLayoutToDefaults();
                }

                ImGui.Separator();
                DrawFontMenu(uiHost);

                ImGui.Separator();
                if (ImGui.MenuItem("About", string.Empty, viewModel.ShowAboutWindow))
                {
                    viewModel.ShowAboutWindow = !viewModel.ShowAboutWindow;
                }

                ImGui.EndMenu();
            }

            ImGui.EndMainMenuBar();
        }

        private static void DrawFontMenu(BaseGameMode uiHost)
        {
            if (!ImGui.BeginMenu("Font"))
            {
                return;
            }

            var availableFontPresets = uiHost.GetAvailableImGuiFontPresets();

            if (ImGui.BeginMenu("Monospace Size"))
            {
                if (ImGui.MenuItem("Small", string.Empty, uiHost.CurrentMonoSizeTier == BaseGameMode.ImGuiMonoSizeTier.Small))
                {
                    uiHost.SetImGuiMonoSizeTier(BaseGameMode.ImGuiMonoSizeTier.Small);
                }

                if (ImGui.MenuItem("Regular", string.Empty, uiHost.CurrentMonoSizeTier == BaseGameMode.ImGuiMonoSizeTier.Regular))
                {
                    uiHost.SetImGuiMonoSizeTier(BaseGameMode.ImGuiMonoSizeTier.Regular);
                }

                ImGui.EndMenu();
            }

            ImGui.Separator();

            DrawFontPresetMenuItem(uiHost, availableFontPresets, BaseGameMode.ImGuiFontPreset.SegoeUI, "Segoe UI");
            DrawFontPresetMenuItem(uiHost, availableFontPresets, BaseGameMode.ImGuiFontPreset.SegoeSemibold, "Segoe UI Semibold");
            DrawFontPresetMenuItem(uiHost, availableFontPresets, BaseGameMode.ImGuiFontPreset.Bahnschrift, "Bahnschrift");
            DrawFontPresetMenuItem(uiHost, availableFontPresets, BaseGameMode.ImGuiFontPreset.BarlowCondensed, "Barlow Condensed");
            DrawFontPresetMenuItem(uiHost, availableFontPresets, BaseGameMode.ImGuiFontPreset.Roboto, "Roboto");
            DrawFontPresetMenuItem(uiHost, availableFontPresets, BaseGameMode.ImGuiFontPreset.RobotoSemiCondensed, "Roboto Semi Condensed");
            DrawFontPresetMenuItem(uiHost, availableFontPresets, BaseGameMode.ImGuiFontPreset.RobotoCondensed, "Roboto Condensed");
            DrawFontPresetMenuItem(uiHost, availableFontPresets, BaseGameMode.ImGuiFontPreset.Calibri, "Calibri");
            DrawFontPresetMenuItem(uiHost, availableFontPresets, BaseGameMode.ImGuiFontPreset.Candara, "Candara");
            DrawFontPresetMenuItem(uiHost, availableFontPresets, BaseGameMode.ImGuiFontPreset.Corbel, "Corbel");
            DrawFontPresetMenuItem(uiHost, availableFontPresets, BaseGameMode.ImGuiFontPreset.Verdana, "Verdana");
            DrawFontPresetMenuItem(uiHost, availableFontPresets, BaseGameMode.ImGuiFontPreset.Tahoma, "Tahoma");
            DrawFontPresetMenuItem(uiHost, availableFontPresets, BaseGameMode.ImGuiFontPreset.Trebuchet, "Trebuchet");
            DrawFontPresetMenuItem(uiHost, availableFontPresets, BaseGameMode.ImGuiFontPreset.SegoeUIMono, "Segoe UI Mono");
            DrawFontPresetMenuItem(uiHost, availableFontPresets, BaseGameMode.ImGuiFontPreset.Consolas, "Consolas (Mono)");
            DrawFontPresetMenuItem(uiHost, availableFontPresets, BaseGameMode.ImGuiFontPreset.CascadiaMono, "Cascadia Mono");
            DrawFontPresetMenuItem(uiHost, availableFontPresets, BaseGameMode.ImGuiFontPreset.JetBrainsMono, "JetBrains Mono");
            DrawFontPresetMenuItem(uiHost, availableFontPresets, BaseGameMode.ImGuiFontPreset.FiraCode, "Fira Code");
            DrawFontPresetMenuItem(uiHost, availableFontPresets, BaseGameMode.ImGuiFontPreset.SourceCodePro, "Source Code Pro");
            DrawFontPresetMenuItem(uiHost, availableFontPresets, BaseGameMode.ImGuiFontPreset.CourierNew, "Courier New (Mono)");

            if (ImGui.MenuItem("Default", string.Empty, uiHost.CurrentImGuiFontPreset == BaseGameMode.ImGuiFontPreset.Default))
            {
                uiHost.RequestImGuiFontPreset(BaseGameMode.ImGuiFontPreset.Default);
            }

            ImGui.EndMenu();
        }

        private static void DrawCharacterMenu(VoxelEditorViewModel viewModel)
        {
            var controller = viewModel.PawnController;

            if (ImGui.MenuItem("Spawn Character"))
            {
                viewModel.Commands.SpawnCharacter();
            }

            if (ImGui.MenuItem("Next Character", "Num +", false, controller?.PossessedPawn != null))
            {
                viewModel.Commands.CycleControlledCharacter(1);
            }

            if (ImGui.MenuItem("Previous Character", "Num -", false, controller?.PossessedPawn != null))
            {
                viewModel.Commands.CycleControlledCharacter(-1);
            }

            if (ImGui.MenuItem("Remove All Characters", string.Empty, false, controller is { Pawns.Count: > 0 }))
            {
                viewModel.Commands.RemoveAllCharacters();
            }

            ImGui.TextDisabled($"{viewModel.CharacterCatalog.Count} characters across " +
                $"{viewModel.CharacterCatalog.Select(asset => asset.PackName).Distinct().Count()} pack(s)");

            ImGui.Separator();

            if (ImGui.MenuItem("Spawn Platform Course"))
            {
                viewModel.Commands.SpawnPlatformCourse();
            }

            if (ImGui.MenuItem("Remove Platform Course", string.Empty, false, viewModel.HasPlatformCourse))
            {
                viewModel.Commands.RemovePlatformCourse();
            }

            var physicsEnabled = viewModel.CharacterPhysicsEnabled;
            if (ImGui.MenuItem("Physics Capsule (new spawns)", string.Empty, physicsEnabled))
            {
                viewModel.CharacterPhysicsEnabled = !physicsEnabled;
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("On: the character gets a Bepu capsule, falls under gravity, collides with\nplatforms and can jump with Space. Off: the old flat-ground walker.");
            }

            if (controller == null)
            {
                return;
            }

            ImGui.Separator();

            var clickToMove = controller.ClickToMoveEnabled;
            if (ImGui.MenuItem("Click To Move", string.Empty, clickToMove))
            {
                controller.ClickToMoveEnabled = !clickToMove;
            }

            var keyboardDrive = controller.KeyboardDriveEnabled;
            if (ImGui.MenuItem("WASD Drive", string.Empty, keyboardDrive))
            {
                controller.KeyboardDriveEnabled = !keyboardDrive;
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("While a character is possessed, WASD drives it instead of the camera.\nQ/E and mouse orbit still move the camera. Esc releases.");
            }

            if (ImGui.MenuItem("Release Character", "Esc", false, controller.PossessedPawn != null))
            {
                controller.Possess(null);
            }

            ImGui.Separator();

            if (ImGui.BeginMenu("Camera"))
            {
                DrawCameraModeItem(controller, ManaCameraMode.Free, "Free Camera", "Editor fly camera. WASD/QE, right-drag to look.");
                DrawCameraModeItem(controller, ManaCameraMode.Follow, "Follow (Spring Arm)", "Boom behind the character that swings around as it moves.");
                DrawCameraModeItem(controller, ManaCameraMode.Orbit, "Orbit (Spring Arm)", "Same boom, but it stays where you leave it.");

                ImGui.Separator();
                ImGui.TextDisabled("C cycles modes");
                ImGui.TextDisabled("Right-drag orbits, scroll zooms the boom");

                if (controller.SpringArm is { } springArm)
                {
                    ImGui.Separator();
                    ImGui.TextDisabled($"Arm {springArm.CurrentArmLength:0.0} / {springArm.ArmLength:0.0}" +
                        (springArm.IsProbeShortened ? " (pulled in)" : string.Empty));
                }

                ImGui.EndMenu();
            }

            ImGui.Separator();
            ImGui.TextDisabled($"Characters: {controller.Pawns.Count}");
            ImGui.TextDisabled(controller.PossessedPawn == null
                ? "Click a character to control it"
                : $"Controlling: {controller.PossessedPawn.Name} (WASD, Shift run, Space jump)");

            if (!string.IsNullOrWhiteSpace(viewModel.CharacterSpawnError))
            {
                ImGui.Separator();
                ImGui.TextColored(new Vector4(0.95f, 0.45f, 0.35f, 1.0f), viewModel.CharacterSpawnError);
            }
        }

        private static void DrawCameraModeItem(ManaPawnController controller, ManaCameraMode mode, string label, string tooltip)
        {
            // The spring-arm modes need something to hang off, so they stay greyed out until a
            // character is possessed rather than silently doing nothing.
            var available = mode == ManaCameraMode.Free || controller.PossessedPawn != null;
            if (ImGui.MenuItem(label, string.Empty, controller.CameraMode == mode, available))
            {
                controller.SetCameraMode(mode);
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(available ? tooltip : $"{tooltip}\n(Possess a character first.)");
            }
        }

        private static void DrawFontPresetMenuItem(
            BaseGameMode uiHost,
            IReadOnlyCollection<BaseGameMode.ImGuiFontPreset> availableFontPresets,
            BaseGameMode.ImGuiFontPreset preset,
            string label)
        {
            if (!availableFontPresets.Contains(preset))
            {
                return;
            }

            if (ImGui.MenuItem(label, string.Empty, uiHost.CurrentImGuiFontPreset == preset))
            {
                uiHost.RequestImGuiFontPreset(preset);
            }
        }
    }
}
