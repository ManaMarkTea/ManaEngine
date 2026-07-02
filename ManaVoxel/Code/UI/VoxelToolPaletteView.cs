using ImGuiNET;
using ManaVoxel.Code.Tools;
using Mana;

namespace ManaVoxel.Code.UI
{
    public sealed class VoxelToolPaletteView
    {
        public const string WindowTitle = "Voxel Tools";
        private const float ToolButtonSize = 42.0f;
        private const int ToolButtonColumns = 3;

        public void Draw(VoxelEditorViewModel viewModel)
        {
            ImGui.Begin(WindowTitle, ImGuiWindowFlags.NoCollapse);

            DrawSelectToolButton(viewModel);
            ImGui.Separator();
            DrawToolButtons(viewModel);

            ImGui.Separator();
            DrawPlaneButtons(viewModel);

            ImGui.Separator();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("Paint Color");
            ImGuiExt.DrawColorValue(ref viewModel.PaintVoxelColor);

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

        private static void DrawSelectToolButton(VoxelEditorViewModel viewModel)
        {
            var selected = viewModel.IsSelectToolActive;

            if (ImGui.InvisibleButton("##voxel-tool-select", new Vector2(ToolButtonSize, ToolButtonSize)))
            {
                viewModel.IsSelectToolActive = true;
            }

            var hovered = ImGui.IsItemHovered();
            var active = ImGui.IsItemActive();
            var min = ImGui.GetItemRectMin();
            var max = ImGui.GetItemRectMax();
            DrawToolButtonFrame(ImGui.GetWindowDrawList(), min, max, selected, hovered, active);
            DrawSelectIcon(ImGui.GetWindowDrawList(), (min + max) * 0.5f, selected);

            if (hovered)
            {
                ImGui.SetTooltip("Select (click objects in the viewport instead of placing voxels)");
            }
        }

        private static void DrawSelectIcon(ImDrawListPtr drawList, Vector2 center, bool selected)
        {
            var iconColor = selected
                ? Color(0.96f, 0.99f, 1.0f, 1.0f)
                : Color(0.14f, 0.26f, 0.38f, 1.0f);
            var accentColor = selected
                ? Color(0.85f, 0.94f, 1.0f, 0.82f)
                : Color(0.24f, 0.53f, 0.84f, 0.88f);

            var origin = center + new Vector2(-8.0f, -10.0f);
            var tip = origin;
            var baseA = origin + new Vector2(0.0f, 17.0f);
            var baseB = origin + new Vector2(7.0f, 10.0f);
            var tailA = origin + new Vector2(4.5f, 9.5f);
            var tailTip = origin + new Vector2(11.0f, 19.0f);
            var tailB = origin + new Vector2(8.5f, 13.0f);

            drawList.AddTriangleFilled(tip, baseA, baseB, WithAlpha(accentColor, 0.28f));
            drawList.AddTriangle(tip, baseA, baseB, iconColor, 1.4f);
            drawList.AddTriangleFilled(tailA, tailTip, tailB, WithAlpha(accentColor, 0.28f));
            drawList.AddTriangle(tailA, tailTip, tailB, iconColor, 1.4f);
        }

        private static void DrawToolButtons(VoxelEditorViewModel viewModel)
        {
            for (var i = 0; i < VoxelToolCatalog.ToolKinds.Length; i++)
            {
                if (i > 0 && i % ToolButtonColumns != 0)
                {
                    ImGui.SameLine();
                }

                var toolKind = VoxelToolCatalog.ToolKinds[i];
                DrawToolButton(viewModel, toolKind, GetToolTooltip(toolKind));
            }
        }

        private static void DrawToolButton(
            VoxelEditorViewModel viewModel,
            VoxelToolKind toolKind,
            string tooltip)
        {
            var toolIndex = VoxelToolCatalog.GetToolIndex(toolKind);
            var selected = !viewModel.IsSelectToolActive && viewModel.SelectedToolIndex == toolIndex;

            if (ImGui.InvisibleButton($"##voxel-tool-{toolKind}", new Vector2(ToolButtonSize, ToolButtonSize)))
            {
                viewModel.SelectedToolIndex = toolIndex;
                viewModel.IsSelectToolActive = false;
            }

            var hovered = ImGui.IsItemHovered();
            var active = ImGui.IsItemActive();
            var min = ImGui.GetItemRectMin();
            var max = ImGui.GetItemRectMax();
            DrawToolButtonFrame(ImGui.GetWindowDrawList(), min, max, selected, hovered, active);
            DrawToolIcon(ImGui.GetWindowDrawList(), toolKind, min, max, selected);

            if (hovered)
            {
                ImGui.SetTooltip(tooltip);
            }
        }

        private static void DrawToolButtonFrame(
            ImDrawListPtr drawList,
            Vector2 min,
            Vector2 max,
            bool selected,
            bool hovered,
            bool active)
        {
            var style = ImGui.GetStyle();
            var fill = ToColor(style.Colors[(int)ImGuiCol.Button]);
            if (hovered)
            {
                fill = ToColor(style.Colors[(int)ImGuiCol.ButtonHovered]);
            }

            if (active)
            {
                fill = ToColor(style.Colors[(int)ImGuiCol.ButtonActive]);
            }

            if (selected)
            {
                fill = Color(0.26f, 0.55f, 0.90f, 1.0f);
            }

            var border = selected
                ? Color(0.13f, 0.36f, 0.62f, 1.0f)
                : Color(0.36f, 0.48f, 0.60f, hovered ? 0.78f : 0.44f);

            drawList.AddRectFilled(min, max, fill, 5.0f);
            drawList.AddRect(min, max, border, 5.0f);

            if (selected)
            {
                drawList.AddRectFilled(
                    new Vector2(min.X + 9.0f, max.Y - 5.0f),
                    new Vector2(max.X - 9.0f, max.Y - 3.0f),
                    Color(0.85f, 0.94f, 1.0f, 0.95f),
                    1.0f);
            }
        }

        private static void DrawToolIcon(
            ImDrawListPtr drawList,
            VoxelToolKind toolKind,
            Vector2 min,
            Vector2 max,
            bool selected)
        {
            var iconColor = selected
                ? Color(0.96f, 0.99f, 1.0f, 1.0f)
                : Color(0.14f, 0.26f, 0.38f, 1.0f);
            var accentColor = selected
                ? Color(0.85f, 0.94f, 1.0f, 0.82f)
                : Color(0.24f, 0.53f, 0.84f, 0.88f);
            var center = (min + max) * 0.5f;

            switch (toolKind)
            {
                case VoxelToolKind.Stroke:
                    DrawBrushIcon(drawList, center, iconColor, accentColor);
                    break;
                case VoxelToolKind.Line:
                    DrawLineIcon(drawList, center, iconColor, accentColor);
                    break;
                case VoxelToolKind.Circle:
                    DrawCircleIcon(drawList, center, iconColor, accentColor);
                    break;
                case VoxelToolKind.Sphere:
                    DrawSphereIcon(drawList, center, iconColor, accentColor);
                    break;
                case VoxelToolKind.Spline:
                    DrawSplineIcon(drawList, center, iconColor, accentColor);
                    break;
                case VoxelToolKind.Text:
                    DrawTextBlockIcon(drawList, center, iconColor, accentColor);
                    break;
            }
        }

        private static void DrawBrushIcon(ImDrawListPtr drawList, Vector2 center, uint iconColor, uint accentColor)
        {
            var handleStart = center + new Vector2(0.0f, -1.0f);
            var handleEnd = center + new Vector2(10.0f, -11.0f);
            var ferruleA = center + new Vector2(-7.0f, 2.0f);
            var ferruleB = center + new Vector2(-2.0f, -3.0f);
            var ferruleC = center + new Vector2(3.0f, 2.0f);
            var ferruleD = center + new Vector2(-2.0f, 7.0f);
            var tip = center + new Vector2(-10.0f, 10.0f);

            drawList.AddLine(handleStart, handleEnd, iconColor, 4.0f);
            drawList.AddQuadFilled(ferruleA, ferruleB, ferruleC, ferruleD, accentColor);
            drawList.AddQuad(ferruleA, ferruleB, ferruleC, ferruleD, iconColor, 1.1f);
            drawList.AddTriangleFilled(tip, ferruleA + new Vector2(1.5f, 2.0f), ferruleD + new Vector2(2.0f, -1.5f), iconColor);
            drawList.AddCircleFilled(tip + new Vector2(-1.0f, 1.0f), 2.2f, accentColor, 10);
        }

        private static void DrawLineIcon(ImDrawListPtr drawList, Vector2 center, uint iconColor, uint accentColor)
        {
            var start = center + new Vector2(-11.0f, 8.0f);
            var end = center + new Vector2(11.0f, -8.0f);

            drawList.AddLine(start, end, accentColor, 4.0f);
            drawList.AddLine(start, end, iconColor, 2.0f);
            drawList.AddCircleFilled(start, 3.3f, iconColor, 12);
            drawList.AddCircleFilled(end, 3.3f, iconColor, 12);
        }

        private static void DrawCircleIcon(ImDrawListPtr drawList, Vector2 center, uint iconColor, uint accentColor)
        {
            drawList.AddCircleFilled(center, 10.0f, WithAlpha(accentColor, 0.22f), 28);
            drawList.AddCircle(center, 10.0f, iconColor, 28, 2.4f);
            drawList.AddCircleFilled(center, 2.2f, accentColor, 10);
        }

        private static void DrawSphereIcon(ImDrawListPtr drawList, Vector2 center, uint iconColor, uint accentColor)
        {
            drawList.AddCircleFilled(center, 10.5f, WithAlpha(accentColor, 0.28f), 28);
            drawList.AddCircle(center, 10.5f, iconColor, 28, 2.2f);
            DrawEllipse(drawList, center, new Vector2(10.0f, 4.2f), WithAlpha(iconColor, 0.68f), 1.4f, 28);
            DrawEllipse(drawList, center, new Vector2(4.3f, 10.0f), WithAlpha(iconColor, 0.68f), 1.4f, 28);
            drawList.AddCircleFilled(center + new Vector2(-3.7f, -4.2f), 2.2f, WithAlpha(iconColor, 0.72f), 10);
        }

        private static void DrawSplineIcon(ImDrawListPtr drawList, Vector2 center, uint iconColor, uint accentColor)
        {
            var p0 = center + new Vector2(-12.0f, 6.0f);
            var p1 = center + new Vector2(-8.0f, -11.0f);
            var p2 = center + new Vector2(7.0f, 10.0f);
            var p3 = center + new Vector2(12.0f, -6.0f);

            DrawCubicBezier(drawList, p0, p1, p2, p3, accentColor, 5.0f, 18);
            DrawCubicBezier(drawList, p0, p1, p2, p3, iconColor, 2.2f, 18);
            drawList.AddCircleFilled(p0, 3.0f, iconColor, 12);
            drawList.AddCircleFilled(p3, 3.0f, iconColor, 12);
        }

        private static void DrawTextBlockIcon(ImDrawListPtr drawList, Vector2 center, uint iconColor, uint accentColor)
        {
            var min = center + new Vector2(-10.0f, -10.0f);
            var max = center + new Vector2(10.0f, 10.0f);

            drawList.AddRect(min, max, iconColor, 2.5f);
            drawList.AddLine(min + new Vector2(5.0f, 5.0f), max - new Vector2(4.0f, 15.0f), iconColor, 1.6f);
            drawList.AddLine(min + new Vector2(5.0f, 10.0f), max - new Vector2(2.0f, 10.0f), iconColor, 1.6f);
            drawList.AddLine(min + new Vector2(5.0f, 15.0f), max - new Vector2(7.0f, 5.0f), iconColor, 1.6f);
            drawList.AddLine(center + new Vector2(-5.0f, -6.5f), center + new Vector2(-5.0f, 6.5f), accentColor, 2.2f);
        }

        private static void DrawEllipse(
            ImDrawListPtr drawList,
            Vector2 center,
            Vector2 radius,
            uint color,
            float thickness,
            int segments)
        {
            var previous = center + new Vector2(radius.X, 0.0f);
            for (var i = 1; i <= segments; i++)
            {
                var angle = i / (float)segments * MathF.PI * 2.0f;
                var current = center + new Vector2(MathF.Cos(angle) * radius.X, MathF.Sin(angle) * radius.Y);
                drawList.AddLine(previous, current, color, thickness);
                previous = current;
            }
        }

        private static void DrawCubicBezier(
            ImDrawListPtr drawList,
            Vector2 p0,
            Vector2 p1,
            Vector2 p2,
            Vector2 p3,
            uint color,
            float thickness,
            int segments)
        {
            var previous = p0;
            for (var i = 1; i <= segments; i++)
            {
                var t = i / (float)segments;
                var inverseT = 1.0f - t;
                var current =
                    p0 * (inverseT * inverseT * inverseT) +
                    p1 * (3.0f * inverseT * inverseT * t) +
                    p2 * (3.0f * inverseT * t * t) +
                    p3 * (t * t * t);

                drawList.AddLine(previous, current, color, thickness);
                previous = current;
            }
        }

        private static string GetToolTooltip(VoxelToolKind toolKind)
        {
            return toolKind switch
            {
                VoxelToolKind.Stroke => "Brush / Stroke",
                VoxelToolKind.Line => "Line",
                VoxelToolKind.Circle => "Circle",
                VoxelToolKind.Sphere => "Sphere",
                VoxelToolKind.Spline => "Spline",
                VoxelToolKind.Text => "Text",
                _ => toolKind.ToString()
            };
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

        private static uint ToColor(Vector4 value)
        {
            return ImGui.ColorConvertFloat4ToU32(value);
        }

        private static uint WithAlpha(uint color, float alphaScale)
        {
            var value = ImGui.ColorConvertU32ToFloat4(color);
            value.W *= alphaScale;
            return ImGui.ColorConvertFloat4ToU32(value);
        }

        private static uint Color(float r, float g, float b, float a)
        {
            return ImGui.ColorConvertFloat4ToU32(new Vector4(r, g, b, a));
        }
    }
}
