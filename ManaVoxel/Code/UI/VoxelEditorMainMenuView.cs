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
