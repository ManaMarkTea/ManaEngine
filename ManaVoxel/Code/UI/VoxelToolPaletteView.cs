using ImGuiNET;
using ManaVoxel.Code.Tools;

namespace ManaVoxel.Code.UI
{
    public sealed class VoxelToolPaletteView
    {
        public const string WindowTitle = "Voxel Tools";

        public void Draw(VoxelEditorViewModel viewModel)
        {
            ImGui.Begin(WindowTitle, ImGuiWindowFlags.NoCollapse);

            DrawToolButtons(viewModel);

            ImGui.Separator();
            DrawPlaneButtons(viewModel);

            ImGui.Separator();
            ImGui.ColorEdit4("Paint", ref viewModel.PaintVoxelColor);

            var selectedTool = VoxelToolCatalog.GetToolKind(viewModel.SelectedToolIndex);
            if (selectedTool is VoxelToolKind.Stroke or VoxelToolKind.Line or VoxelToolKind.Spline)
            {
                ImGui.SliderInt("Radius", ref viewModel.StrokeRadiusVoxels, 0, 12);
            }

            if (selectedTool == VoxelToolKind.Text)
            {
                ImGui.InputText("Text", ref viewModel.ToolText, 128);
                ImGui.SliderInt("Scale", ref viewModel.TextScale, 1, 8);
                ImGui.SliderInt("Depth", ref viewModel.TextDepth, 1, 16);
            }

            ImGui.End();
        }

        private static void DrawToolButtons(VoxelEditorViewModel viewModel)
        {
            DrawToolButton(viewModel, VoxelToolKind.Stroke, "B", "Brush / Stroke");
            DrawToolButton(viewModel, VoxelToolKind.Line, "L", "Line");
            DrawToolButton(viewModel, VoxelToolKind.Circle, "O", "Circle");
            DrawToolButton(viewModel, VoxelToolKind.Sphere, "S", "Sphere");
            DrawToolButton(viewModel, VoxelToolKind.Spline, "~", "Spline");
            DrawToolButton(viewModel, VoxelToolKind.Text, "T", "Text");
        }

        private static void DrawToolButton(
            VoxelEditorViewModel viewModel,
            VoxelToolKind toolKind,
            string label,
            string tooltip)
        {
            const float buttonSize = 42.0f;
            var toolIndex = VoxelToolCatalog.GetToolIndex(toolKind);
            var selected = viewModel.SelectedToolIndex == toolIndex;

            if (selected)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.26f, 0.55f, 0.90f, 1.0f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.32f, 0.62f, 0.98f, 1.0f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.20f, 0.46f, 0.78f, 1.0f));
            }

            if (ImGui.Button(label, new Vector2(buttonSize, buttonSize)))
            {
                viewModel.SelectedToolIndex = toolIndex;
            }

            if (selected)
            {
                ImGui.PopStyleColor(3);
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(tooltip);
            }
        }

        private static void DrawPlaneButtons(VoxelEditorViewModel viewModel)
        {
            ImGui.TextUnformatted("Plane");

            for (var i = 0; i < viewModel.WorkPlaneOptions.Length; i++)
            {
                if (i > 0)
                {
                    ImGui.SameLine();
                }

                DrawPlaneButton(viewModel, i);
            }
        }

        private static void DrawPlaneButton(VoxelEditorViewModel viewModel, int planeIndex)
        {
            var selected = viewModel.SelectedWorkPlaneIndex == planeIndex;
            if (selected)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.26f, 0.55f, 0.90f, 1.0f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.32f, 0.62f, 0.98f, 1.0f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.20f, 0.46f, 0.78f, 1.0f));
            }

            if (ImGui.SmallButton(viewModel.WorkPlaneOptions[planeIndex]))
            {
                viewModel.SelectedWorkPlaneIndex = planeIndex;
            }

            if (selected)
            {
                ImGui.PopStyleColor(3);
            }
        }
    }
}
