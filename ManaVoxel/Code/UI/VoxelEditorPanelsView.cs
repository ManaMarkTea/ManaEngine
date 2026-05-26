using ImGuiNET;
using ManaVoxel.Code.Tools;

namespace ManaVoxel.Code.UI
{
    public class VoxelEditorPanelsView
    {
        private TimeSpan[]? _fpsData;
        private float[] _recentFpsData = Array.Empty<float>();
        private float _fpsGraphCeiling = 60.0f;

        private const float FpsHistorySeconds = 5.0f;
        private static readonly float[] FpsGraphSteps = { 30.0f, 60.0f, 90.0f, 120.0f, 144.0f, 165.0f, 240.0f, 360.0f, 480.0f, 720.0f, 1000.0f };

        public void Draw(VoxelEditorViewModel viewModel, ref Vector3 rayOrigin, ref Vector3 rayDirection)
        {
            viewModel.ApplyRuntimeSettings();

            ImGui.Text(ManaTime.GlobalTime.FramesPerSecondAvg.ToString(" 0.00") + " fps");
            ImGui.SameLine();
            ImGui.Text(ManaTime.GlobalTime.DeltaTimeAvg.TotalMilliseconds.ToString(" 0") + " ms");
            DrawRecentFpsHistory();

            ImGui.Text($"Mode: {viewModel.ModeText}");
            ImGui.SameLine();
            ImGui.Text($"State: {viewModel.ReassemblyText}");
            ImGui.SameLine();
            ImGui.Text($"Baked Bodies: {viewModel.BakedPhysicsVoxelCount}");
            ImGui.Separator();

            if (ImGui.BeginTabBar("ManaTabs"))
            {
                DrawEditorTab(viewModel);
                DrawPhysicsTab(viewModel);
                DrawDebugTab(viewModel, ref rayOrigin, ref rayDirection);
                ImGui.EndTabBar();
            }
        }

        private static void DrawEditorTab(VoxelEditorViewModel viewModel)
        {
            if (!ImGui.BeginTabItem("Editor"))
            {
                return;
            }

            ImGui.SeparatorText("Session");
            DrawSessionTable(viewModel);

            ImGui.Spacing();
            DrawPlacementReadout(viewModel);

            ImGui.Spacing();
            DrawVoxelNodeControls(viewModel);

            ImGui.Spacing();
            ImGui.SeparatorText("Import");
            ImGui.InputFloat3("Import Pos Offset", ref viewModel.ImportPositionOffset);
            ImGui.InputFloat3("Import Rot (deg)", ref viewModel.ImportRotationDegrees);
            ImGui.InputFloat3("Import Scale", ref viewModel.ImportScale);

            ImGui.EndTabItem();
        }

        private static void DrawSessionTable(VoxelEditorViewModel viewModel)
        {
            const ImGuiTableFlags flags =
                ImGuiTableFlags.Borders |
                ImGuiTableFlags.RowBg |
                ImGuiTableFlags.SizingStretchProp;

            if (!ImGui.BeginTable("SessionStats", 2, flags))
            {
                return;
            }

            ImGui.TableSetupColumn("Metric", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Count", ImGuiTableColumnFlags.WidthFixed, 96.0f);
            ImGui.TableHeadersRow();

            DrawSessionTableRow("Voxels", viewModel.VoxelCount);
            DrawSessionTableRow("Voxel Nodes", viewModel.VoxelLayers.Count);
            DrawSessionTableRow("Active Node Voxels", viewModel.ActiveVoxelCount);
            DrawSessionTableRow("Created This Session", viewModel.CreatedThisSessionVoxelCount);
            DrawSessionTableRow("Pending Voxels", viewModel.PendingVoxelCount);
            DrawSessionTableRow("Duplicate Skips", viewModel.DuplicateVoxelRejectCount);
            DrawSessionTableRow("Physics Baked Voxels", viewModel.BakedPhysicsVoxelCount);

            ImGui.EndTable();
        }

        private static void DrawVoxelNodeControls(VoxelEditorViewModel viewModel)
        {
            ImGui.SeparatorText("Voxel Nodes");
            if (viewModel.VoxelLayers.Count == 0)
            {
                ImGui.TextUnformatted("None");
                return;
            }

            var layerNames = viewModel.VoxelLayerNames;
            viewModel.ActiveVoxelLayerIndex = Math.Clamp(
                viewModel.ActiveVoxelLayerIndex,
                0,
                layerNames.Length - 1);
            ImGui.Combo("Active Node", ref viewModel.ActiveVoxelLayerIndex, layerNames, layerNames.Length);

            var activeLayer = viewModel.ActiveVoxelLayer;
            if (activeLayer == null)
            {
                return;
            }

            ImGui.TextUnformatted($"Source: {activeLayer.Source}");

            var nodePosition = activeLayer.Node.Position;
            if (ImGui.InputFloat3("Node Pos", ref nodePosition))
            {
                activeLayer.Node.Position = nodePosition;
            }
        }

        private static void DrawPlacementReadout(VoxelEditorViewModel viewModel)
        {
            ImGui.SeparatorText("Placement");
            if (!viewModel.HasHoverVoxel)
            {
                ImGui.TextUnformatted("No hovered voxel");
                return;
            }

            var cell = VoxelRasterizer.ToCell(viewModel.HoverVoxelPosition);
            ImGui.TextUnformatted($"Cell: {cell.X}, {cell.Y}, {cell.Z}");
            ImGui.TextUnformatted($"Center: {FormatVector(viewModel.HoverVoxelPosition)}");
            ImGui.TextUnformatted($"Camera Distance: {viewModel.HoverCameraDistance:0.0}");

            if (viewModel.HasHoverDelta)
            {
                ImGui.TextUnformatted($"Delta From Last: {FormatVector(viewModel.HoverDeltaFromLast)}");
            }
        }

        private static void DrawSessionTableRow(string label, int value)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(label);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(value.ToString());
        }

        private static string FormatVector(Vector3 value)
        {
            return $"{value.X:0.0}, {value.Y:0.0}, {value.Z:0.0}";
        }

        private static void DrawPhysicsTab(VoxelEditorViewModel viewModel)
        {
            if (!ImGui.BeginTabItem("Physics"))
            {
                return;
            }

            ImGui.SeparatorText("Tuning");
            ImGui.SliderFloat("Explosion Strength", ref viewModel.BakeExplosionStrength, 1.0f, 150.0f);
            ImGui.SliderFloat("Click Explosion Scale", ref viewModel.ClickExplosionStrengthScale, 0.02f, 1.0f);
            ImGui.SliderFloat("Explosion Radius Padding", ref viewModel.BakeExplosionRadiusPadding, 0.0f, 6.0f);
            ImGui.SliderFloat("Reassemble Pull", ref viewModel.ReassemblePullGain, 0.0f, 30.0f);
            ImGui.SliderFloat("Reassemble Damping", ref viewModel.ReassembleVelocityDamping, 0.0f, 0.99f);
            ImGui.SliderFloat("Reassemble Rotation Lerp", ref viewModel.ReassembleRotationLerp, 0.0f, 1.0f);
            ImGui.SliderFloat("Settle Distance", ref viewModel.ReassembleSettleDistance, 0.01f, 0.5f);
            ImGui.SliderFloat("Settle Velocity", ref viewModel.ReassembleSettleVelocity, 0.01f, 2.0f);

            ImGui.Spacing();
            ImGui.SeparatorText("Bake");

            var canBake = viewModel.HasEditableVoxels;
            if (!canBake)
            {
                ImGui.BeginDisabled();
            }

            if (ImGui.Button("Bake All Voxel Nodes To Physics", new Vector2(ImGui.GetContentRegionAvail().X, 0)))
            {
                viewModel.BakeToPhysics();
            }

            if (!canBake)
            {
                ImGui.EndDisabled();
            }

            ImGui.Spacing();
            ImGui.SeparatorText("Lock / Overlap");

            var hasBakedVoxels = viewModel.HasBakedVoxels;
            if (!hasBakedVoxels)
            {
                ImGui.BeginDisabled();
            }

            ImGui.Checkbox("Preview Lock Snap", ref viewModel.ShowLockPreview);
            if (ImGui.Combo(
                "Lock Overlap Policy",
                ref viewModel.LockOverlapPolicyIndex,
                viewModel.LockOverlapPolicyOptions,
                viewModel.LockOverlapPolicyOptions.Length))
            {
                viewModel.ApplyRuntimeSettings();
            }

            if (viewModel.ShowLockPreview && hasBakedVoxels)
            {
                ImGui.Text($"Lock Preview Voxels: {viewModel.GetLockPreviewVoxelCount()}");
                ImGui.Text($"Lock Preview Overlaps: {viewModel.GetLockPreviewOverlapCount()}");
            }

            ImGui.Spacing();
            ImGui.SeparatorText("Actions");

            if (ImGui.Button("Reassemble All Baked Voxels", new Vector2(ImGui.GetContentRegionAvail().X, 0)))
            {
                viewModel.BeginReassembleBakedVoxels();
            }

            if (ImGui.Button("Lock All Voxels For Editing", new Vector2(ImGui.GetContentRegionAvail().X, 0)))
            {
                viewModel.LockBakedVoxelsForEditing();
            }

            if (ImGui.Button("Explode All Baked Voxels", new Vector2(ImGui.GetContentRegionAvail().X, 0)))
            {
                viewModel.ExplodeBakedVoxels();
            }

            if (hasBakedVoxels)
            {
                ImGui.Text(viewModel.IsReassembling ? "Reassembly: in progress" : "Reassembly: idle");
            }

            if (!hasBakedVoxels)
            {
                ImGui.EndDisabled();
            }

            ImGui.EndTabItem();
        }

        private void DrawDebugTab(VoxelEditorViewModel viewModel, ref Vector3 rayOrigin, ref Vector3 rayDirection)
        {
            if (!ImGui.BeginTabItem("Debug"))
            {
                return;
            }

            ImGui.SeparatorText("Timing");
            if (_fpsData == null)
            {
                _fpsData = new TimeSpan[ManaTime.GlobalTime.History.Length];
            }

            float[] data = ManaTime.GlobalTime.OrderedHistory(ref _fpsData).Select(i => (float)i.TotalMilliseconds).ToArray();
            ImGui.PlotLines("FPS", ref data[0], data.Length, 0, "fps", 0, 100, new Vector2(150, 90));

            ImGui.Spacing();
            ImGui.SeparatorText("Ray");
            ImGui.InputFloat3("RAY", ref rayOrigin);
            ImGui.InputFloat3("DIR", ref rayDirection);

            ImGui.Spacing();
            ImGui.SeparatorText("Tools");
            ImGui.Checkbox("Show ImGui Demo Window", ref viewModel.ShowImGuiDemoWindow);

            ImGui.EndTabItem();
        }

        private void DrawRecentFpsHistory()
        {
            if (_fpsData == null)
            {
                _fpsData = new TimeSpan[ManaTime.GlobalTime.History.Length];
            }

            var history = ManaTime.GlobalTime.OrderedHistory(ref _fpsData);
            if (history.Length == 0)
            {
                return;
            }

            // Keep approximately the most recent N seconds of frame history.
            float avgFrameSeconds = MathF.Max(0.001f, (float)ManaTime.GlobalTime.DeltaTimeAvg.TotalSeconds);
            int desiredSamples = Math.Clamp((int)(FpsHistorySeconds / avgFrameSeconds), 60, history.Length);
            int startIndex = Math.Max(0, history.Length - desiredSamples);
            int sampleCount = history.Length - startIndex;
            if (sampleCount <= 1)
            {
                return;
            }

            if (_recentFpsData.Length != sampleCount)
            {
                _recentFpsData = new float[sampleCount];
            }

            for (int i = 0; i < sampleCount; i++)
            {
                var frame = history[startIndex + i];
                _recentFpsData[i] = frame.TotalSeconds > 0
                    ? 1.0f / (float)frame.TotalSeconds
                    : 0.0f;
            }

            float fpsMin = _recentFpsData.Min();
            float fpsMax = _recentFpsData.Max();
            float fpsAvg = _recentFpsData.Average();
            float fpsCurrent = _recentFpsData[^1];
            float fpsGraphMax = fpsMax;

            if (MathF.Abs(fpsGraphMax - fpsMin) < 1.0f)
            {
                fpsGraphMax = fpsMin + 1.0f;
            }

            float currentFpsGraphCeiling = NextFpsGraphStep(fpsGraphMax);
            _fpsGraphCeiling = MathF.Max(_fpsGraphCeiling, currentFpsGraphCeiling);

            if (ImGui.SmallButton("Reset FPS Range"))
            {
                _fpsGraphCeiling = currentFpsGraphCeiling;
            }

            ImGui.PlotLines(
                $"FPS History ({FpsHistorySeconds:0}s)",
                ref _recentFpsData[0],
                _recentFpsData.Length,
                0,
                string.Empty,
                0.0f,
                _fpsGraphCeiling,
                new Vector2(ImGui.GetContentRegionAvail().X, 70));

            DrawFpsStatsTable(fpsCurrent, fpsAvg, fpsMin, fpsMax, _fpsGraphCeiling);
        }

        private static void DrawFpsStatsTable(float current, float average, float minimum, float maximum, float graphCeiling)
        {
            const ImGuiTableFlags flags =
                ImGuiTableFlags.Borders |
                ImGuiTableFlags.RowBg |
                ImGuiTableFlags.SizingStretchSame;

            if (!ImGui.BeginTable("FpsStats", 5, flags))
            {
                return;
            }

            ImGui.TableSetupColumn("Current");
            ImGui.TableSetupColumn("Avg");
            ImGui.TableSetupColumn("Min");
            ImGui.TableSetupColumn("Max");
            ImGui.TableSetupColumn("Range");
            ImGui.TableHeadersRow();

            ImGui.TableNextRow();
            DrawFpsStatsCell($"{current:0.0}");
            DrawFpsStatsCell($"{average:0.0}");
            DrawFpsStatsCell($"{minimum:0.0}");
            DrawFpsStatsCell($"{maximum:0.0}");
            DrawFpsStatsCell($"0-{graphCeiling:0}");

            ImGui.EndTable();
        }

        private static void DrawFpsStatsCell(string value)
        {
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(value);
        }

        private static float NextFpsGraphStep(float value)
        {
            foreach (var step in FpsGraphSteps)
            {
                if (value <= step)
                {
                    return step;
                }
            }

            return FpsGraphSteps[^1];
        }
    }
}
