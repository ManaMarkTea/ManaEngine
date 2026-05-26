using ImGuiNET;
using System.Numerics;

namespace Mana.Code.UI
{
    public class ViewportCameraGizmoView
    {
        private const float GizmoSize = 166.0f;
        private const float GizmoMargin = 18.0f;
        private const float TripodSize = 94.0f;

        public bool Draw(CameraComponent camera, Vector2 sceneMin, Vector2 sceneMax)
        {
            Vector2 sceneSize = sceneMax - sceneMin;
            if (sceneSize.X < GizmoSize + GizmoMargin * 2.0f ||
                sceneSize.Y < GizmoSize + GizmoMargin * 2.0f)
            {
                return false;
            }

            Vector2 windowPos = new(sceneMax.X - GizmoSize - GizmoMargin, sceneMin.Y + GizmoMargin);
            Vector2 windowSize = new(GizmoSize, GizmoSize);
            var flags = ImGuiWindowFlags.NoDecoration |
                        ImGuiWindowFlags.NoDocking |
                        ImGuiWindowFlags.NoMove |
                        ImGuiWindowFlags.NoSavedSettings |
                        ImGuiWindowFlags.NoScrollbar |
                        ImGuiWindowFlags.NoScrollWithMouse |
                        ImGuiWindowFlags.NoFocusOnAppearing |
                        ImGuiWindowFlags.NoNav |
                        ImGuiWindowFlags.NoBackground;

            ImGui.SetNextWindowPos(windowPos, ImGuiCond.Always);
            ImGui.SetNextWindowSize(windowSize, ImGuiCond.Always);
            ImGui.SetNextWindowBgAlpha(0.0f);

            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
            ImGui.Begin("Viewport Camera Gizmo", flags);
            ImGui.PopStyleVar();

            var drawList = ImGui.GetWindowDrawList();
            Vector2 origin = ImGui.GetWindowPos();

            DrawBackdrop(drawList, origin, windowSize);

            if (DrawArrowButton(drawList, "##viewport-gizmo-back", origin + new Vector2(83.0f, 27.0f), new Vector2(34.0f, 24.0f), GizmoDirection.Up, "Back"))
            {
                camera.SetViewPreset(CameraViewPreset.Back);
            }

            if (DrawArrowButton(drawList, "##viewport-gizmo-left", origin + new Vector2(32.0f, 79.0f), new Vector2(24.0f, 34.0f), GizmoDirection.Left, "Left"))
            {
                camera.SetViewPreset(CameraViewPreset.Left);
            }

            Vector2 faceMin = origin + new Vector2(56.0f, 54.0f);
            Vector2 faceMax = origin + new Vector2(110.0f, 100.0f);
            if (DrawFaceButton(drawList, "##viewport-gizmo-top", faceMin, faceMax, "TOP"))
            {
                camera.SetViewPreset(CameraViewPreset.Top);
            }

            if (DrawArrowButton(drawList, "##viewport-gizmo-right", origin + new Vector2(134.0f, 79.0f), new Vector2(24.0f, 34.0f), GizmoDirection.Right, "Right"))
            {
                camera.SetViewPreset(CameraViewPreset.Right);
            }

            if (DrawArrowButton(drawList, "##viewport-gizmo-front", origin + new Vector2(83.0f, 118.0f), new Vector2(34.0f, 24.0f), GizmoDirection.Down, "Front"))
            {
                camera.SetViewPreset(CameraViewPreset.Front);
            }

            if (DrawPillButton(drawList, "##viewport-gizmo-bottom", origin + new Vector2(30.0f, 139.0f), new Vector2(64.0f, 22.0f), "BOTTOM", "Bottom"))
            {
                camera.SetViewPreset(CameraViewPreset.Bottom);
            }

            string projectionLabel = camera.OrthoGraphic ? "ORTHO" : "PERSP";
            if (DrawPillButton(drawList, "##viewport-gizmo-projection", origin + new Vector2(101.0f, 139.0f), new Vector2(48.0f, 22.0f), projectionLabel, "Toggle projection"))
            {
                camera.SetProjectionMode(!camera.OrthoGraphic);
            }

            bool capturesMouse = ImGui.IsWindowHovered() || ImGui.IsAnyItemActive();
            ImGui.End();
            DrawAxisTripod(camera, sceneMin, sceneMax);
            return capturesMouse;
        }

        private static void DrawBackdrop(ImDrawListPtr drawList, Vector2 origin, Vector2 size)
        {
            Vector2 max = origin + size;
            drawList.AddRectFilled(origin + new Vector2(2.0f, 3.0f), max + new Vector2(2.0f, 3.0f), Color(0.06f, 0.10f, 0.16f, 0.14f), 10.0f);
            drawList.AddRectFilled(origin, max, Color(0.91f, 0.95f, 0.99f, 0.84f), 10.0f);
            drawList.AddRect(origin, max, Color(0.43f, 0.58f, 0.72f, 0.42f), 10.0f);
        }

        private static void DrawAxisTripod(CameraComponent camera, Vector2 sceneMin, Vector2 sceneMax)
        {
            Vector2 sceneSize = sceneMax - sceneMin;
            if (sceneSize.X < TripodSize + GizmoMargin * 2.0f ||
                sceneSize.Y < TripodSize + GizmoMargin * 2.0f)
            {
                return;
            }

            Vector2 windowPos = new(sceneMin.X + GizmoMargin, sceneMax.Y - TripodSize - GizmoMargin);
            Vector2 windowSize = new(TripodSize, TripodSize);
            var flags = ImGuiWindowFlags.NoDecoration |
                        ImGuiWindowFlags.NoDocking |
                        ImGuiWindowFlags.NoMove |
                        ImGuiWindowFlags.NoSavedSettings |
                        ImGuiWindowFlags.NoScrollbar |
                        ImGuiWindowFlags.NoScrollWithMouse |
                        ImGuiWindowFlags.NoFocusOnAppearing |
                        ImGuiWindowFlags.NoNav |
                        ImGuiWindowFlags.NoBackground |
                        ImGuiWindowFlags.NoInputs;

            ImGui.SetNextWindowPos(windowPos, ImGuiCond.Always);
            ImGui.SetNextWindowSize(windowSize, ImGuiCond.Always);
            ImGui.SetNextWindowBgAlpha(0.0f);

            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
            ImGui.Begin("Viewport Axis Tripod", flags);
            ImGui.PopStyleVar();

            var drawList = ImGui.GetWindowDrawList();
            Vector2 origin = ImGui.GetWindowPos();
            Vector2 center = origin + new Vector2(38.0f, 58.0f);
            Vector2 labelMin = origin + new Vector2(4.0f, 4.0f);
            Vector2 labelMax = origin + windowSize - new Vector2(4.0f, 4.0f);

            drawList.AddCircleFilled(center + Vector2.One, 4.2f, Color(0.05f, 0.09f, 0.13f, 0.18f), 12);
            drawList.AddCircleFilled(center, 3.4f, Color(0.20f, 0.29f, 0.38f, 0.94f), 12);

            AxisLine[] axes =
            {
                ProjectAxis("X", Vector3.UnitX, camera, Color(0.88f, 0.20f, 0.25f, 1.0f)),
                ProjectAxis("Y", Vector3.UnitY, camera, Color(0.20f, 0.70f, 0.34f, 1.0f)),
                ProjectAxis("Z", Vector3.UnitZ, camera, Color(0.23f, 0.44f, 0.92f, 1.0f))
            };

            Array.Sort(axes, static (a, b) => a.Depth.CompareTo(b.Depth));
            foreach (var axis in axes)
            {
                DrawAxisLine(drawList, center, axis, labelMin, labelMax);
            }

            ImGui.End();
        }

        private static bool DrawFaceButton(ImDrawListPtr drawList, string id, Vector2 min, Vector2 max, string label)
        {
            ImGui.SetCursorScreenPos(min);
            bool clicked = ImGui.InvisibleButton(id, max - min);
            bool hovered = ImGui.IsItemHovered();

            uint fill = hovered ? Color(0.68f, 0.84f, 0.97f, 1.0f) : Color(0.78f, 0.90f, 0.98f, 1.0f);
            uint border = Color(0.28f, 0.43f, 0.58f, 0.95f);
            uint accent = Color(0.40f, 0.57f, 0.70f, 0.55f);

            drawList.AddRectFilled(min + new Vector2(2.0f, 3.0f), max + new Vector2(2.0f, 3.0f), Color(0.09f, 0.16f, 0.22f, 0.16f), 5.0f);
            drawList.AddRectFilled(min, max, fill, 5.0f);
            drawList.AddRect(min, max, border, 5.0f);
            drawList.AddLine(min + new Vector2(9.0f, 0.0f), min + new Vector2(0.0f, 9.0f), accent, 1.0f);
            drawList.AddLine(new Vector2(max.X - 9.0f, min.Y), new Vector2(max.X, min.Y + 9.0f), accent, 1.0f);
            drawList.AddLine(new Vector2(min.X, max.Y - 9.0f), new Vector2(min.X + 9.0f, max.Y), accent, 1.0f);
            drawList.AddLine(max - new Vector2(9.0f, 0.0f), max - new Vector2(0.0f, 9.0f), accent, 1.0f);
            DrawCenteredText(drawList, min, max, label, Color(0.18f, 0.31f, 0.42f, 1.0f));
            DrawTooltip("Top");

            return clicked;
        }

        private static bool DrawArrowButton(
            ImDrawListPtr drawList,
            string id,
            Vector2 center,
            Vector2 size,
            GizmoDirection direction,
            string tooltip)
        {
            Vector2 min = center - size * 0.5f;
            ImGui.SetCursorScreenPos(min);
            bool clicked = ImGui.InvisibleButton(id, size);
            bool hovered = ImGui.IsItemHovered();

            GetTrianglePoints(center, size, direction, out Vector2 p1, out Vector2 p2, out Vector2 p3);
            uint fill = hovered ? Color(0.68f, 0.84f, 0.97f, 1.0f) : Color(0.86f, 0.94f, 1.0f, 0.96f);
            uint border = Color(0.31f, 0.48f, 0.62f, 0.92f);
            drawList.AddTriangleFilled(p1, p2, p3, fill);
            drawList.AddTriangle(p1, p2, p3, border, 1.2f);
            DrawTooltip(tooltip);

            return clicked;
        }

        private static bool DrawPillButton(ImDrawListPtr drawList, string id, Vector2 min, Vector2 size, string label, string tooltip)
        {
            ImGui.SetCursorScreenPos(min);
            bool clicked = ImGui.InvisibleButton(id, size);
            bool hovered = ImGui.IsItemHovered();
            Vector2 max = min + size;

            uint fill = hovered ? Color(0.68f, 0.84f, 0.97f, 1.0f) : Color(0.86f, 0.94f, 1.0f, 0.98f);
            uint border = Color(0.31f, 0.48f, 0.62f, 0.88f);
            drawList.AddRectFilled(min, max, fill, size.Y * 0.5f);
            drawList.AddRect(min, max, border, size.Y * 0.5f);
            DrawCenteredText(drawList, min, max, label, Color(0.18f, 0.31f, 0.42f, 1.0f));
            DrawTooltip(tooltip);

            return clicked;
        }

        private static void GetTrianglePoints(
            Vector2 center,
            Vector2 size,
            GizmoDirection direction,
            out Vector2 p1,
            out Vector2 p2,
            out Vector2 p3)
        {
            Vector2 half = size * 0.5f;
            switch (direction)
            {
                case GizmoDirection.Left:
                    p1 = new Vector2(center.X - half.X, center.Y);
                    p2 = new Vector2(center.X + half.X, center.Y - half.Y);
                    p3 = new Vector2(center.X + half.X, center.Y + half.Y);
                    break;
                case GizmoDirection.Right:
                    p1 = new Vector2(center.X + half.X, center.Y);
                    p2 = new Vector2(center.X - half.X, center.Y - half.Y);
                    p3 = new Vector2(center.X - half.X, center.Y + half.Y);
                    break;
                case GizmoDirection.Down:
                    p1 = new Vector2(center.X, center.Y + half.Y);
                    p2 = new Vector2(center.X - half.X, center.Y - half.Y);
                    p3 = new Vector2(center.X + half.X, center.Y - half.Y);
                    break;
                default:
                    p1 = new Vector2(center.X, center.Y - half.Y);
                    p2 = new Vector2(center.X - half.X, center.Y + half.Y);
                    p3 = new Vector2(center.X + half.X, center.Y + half.Y);
                    break;
            }
        }

        private static void DrawCenteredText(ImDrawListPtr drawList, Vector2 min, Vector2 max, string text, uint color)
        {
            Vector2 textSize = ImGui.CalcTextSize(text);
            Vector2 position = min + (max - min - textSize) * 0.5f;
            drawList.AddText(position, color, text);
        }

        private static void DrawTooltip(string text)
        {
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(text);
            }
        }

        private static AxisLine ProjectAxis(string label, Vector3 axis, CameraComponent camera, uint color)
        {
            Vector3 viewAxis = Vector3.TransformNormal(axis, camera._ViewMatrix);
            float projectedLength = MathF.Sqrt(viewAxis.X * viewAxis.X + viewAxis.Y * viewAxis.Y);
            Vector2 direction = projectedLength > 0.0001f
                ? new Vector2(viewAxis.X, -viewAxis.Y) / projectedLength
                : new Vector2(0.0f, -1.0f);

            float length = 34.0f * MathF.Max(0.28f, projectedLength);
            return new AxisLine(label, direction * length, viewAxis.Z, color);
        }

        private static void DrawAxisLine(ImDrawListPtr drawList, Vector2 center, AxisLine axis, Vector2 labelMin, Vector2 labelMax)
        {
            Vector2 end = center + axis.Offset;
            Vector2 direction = axis.Offset.LengthSquared() > 0.0001f
                ? Vector2.Normalize(axis.Offset)
                : new Vector2(0.0f, -1.0f);
            Vector2 textSize = ImGui.CalcTextSize(axis.Label);
            Vector2 labelPadding = new(4.0f, 2.0f);
            Vector2 labelSize = textSize + labelPadding * 2.0f;
            Vector2 labelCenter = end + direction * 12.0f;
            Vector2 labelPos = labelCenter - labelSize * 0.5f;
            labelPos = Vector2.Clamp(labelPos, labelMin, labelMax - labelSize);
            Vector2 labelRectMin = labelPos;
            Vector2 labelRectMax = labelPos + labelSize;
            Vector2 textPos = labelPos + labelPadding;
            float alphaScale = axis.Depth < 0.0f ? 0.72f : 1.0f;
            uint shadow = Color(0.05f, 0.09f, 0.13f, 0.22f);
            uint labelFill = Color(0.93f, 0.97f, 1.0f, axis.Depth < 0.0f ? 0.72f : 0.88f);
            uint labelBorder = Color(0.26f, 0.38f, 0.49f, axis.Depth < 0.0f ? 0.30f : 0.44f);

            drawList.AddLine(center + Vector2.One, end + Vector2.One, shadow, 3.0f);
            drawList.AddLine(center, end, WithAlpha(axis.Color, alphaScale), 2.2f);
            drawList.AddCircleFilled(end, 4.0f, WithAlpha(axis.Color, alphaScale), 12);
            drawList.AddRectFilled(labelRectMin + Vector2.One, labelRectMax + Vector2.One, shadow, 4.0f);
            drawList.AddRectFilled(labelRectMin, labelRectMax, labelFill, 4.0f);
            drawList.AddRect(labelRectMin, labelRectMax, labelBorder, 4.0f);
            drawList.AddText(textPos, WithAlpha(axis.Color, alphaScale), axis.Label);
        }

        private static uint WithAlpha(uint color, float alphaScale)
        {
            Vector4 value = ImGui.ColorConvertU32ToFloat4(color);
            value.W *= alphaScale;
            return ImGui.ColorConvertFloat4ToU32(value);
        }

        private static uint Color(float r, float g, float b, float a)
        {
            return ImGui.ColorConvertFloat4ToU32(new Vector4(r, g, b, a));
        }

        private readonly struct AxisLine
        {
            public AxisLine(string label, Vector2 offset, float depth, uint color)
            {
                Label = label;
                Offset = offset;
                Depth = depth;
                Color = color;
            }

            public string Label { get; }
            public Vector2 Offset { get; }
            public float Depth { get; }
            public uint Color { get; }
        }

        private enum GizmoDirection
        {
            Up,
            Down,
            Left,
            Right
        }
    }
}
