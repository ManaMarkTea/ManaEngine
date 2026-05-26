using ImGuiNET;
using Mana;
using ManaVoxel.Code.Tools;

namespace ManaVoxel.Code.UI
{
    public sealed class VoxelPlacementDepthOverlayView
    {
        private const float CubeHalfSize = 0.5f;
        private const float ScenePadding = 40.0f;

        private static readonly int[,] CubeEdges =
        {
            { 0, 1 }, { 1, 2 }, { 2, 3 }, { 3, 0 },
            { 4, 5 }, { 5, 6 }, { 6, 7 }, { 7, 4 },
            { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 }
        };

        public void Draw(CameraComponent camera, Vector2 sceneMin, Vector2 sceneMax, VoxelEditorViewModel viewModel)
        {
            if (!viewModel.HasHoverVoxel)
            {
                return;
            }

            var center = viewModel.HoverVoxelPosition;
            if (!TryProject(camera, center, out var screenCenter, out _))
            {
                return;
            }

            if (!IsNearScene(screenCenter, sceneMin, sceneMax, ScenePadding))
            {
                return;
            }

            var drawList = ImGui.GetForegroundDrawList();
            drawList.PushClipRect(sceneMin, sceneMax, true);

            DrawGroundProjection(drawList, camera, center);
            DrawDropRuler(drawList, camera, center);
            DrawCubeGhost(drawList, camera, center);
            DrawBadge(drawList, screenCenter, sceneMin, sceneMax, viewModel);

            drawList.PopClipRect();
        }

        private static void DrawCubeGhost(ImDrawListPtr drawList, CameraComponent camera, Vector3 center)
        {
            Span<Vector3> corners = stackalloc Vector3[8]
            {
                center + new Vector3(-CubeHalfSize, -CubeHalfSize, -CubeHalfSize),
                center + new Vector3( CubeHalfSize, -CubeHalfSize, -CubeHalfSize),
                center + new Vector3( CubeHalfSize,  CubeHalfSize, -CubeHalfSize),
                center + new Vector3(-CubeHalfSize,  CubeHalfSize, -CubeHalfSize),
                center + new Vector3(-CubeHalfSize, -CubeHalfSize,  CubeHalfSize),
                center + new Vector3( CubeHalfSize, -CubeHalfSize,  CubeHalfSize),
                center + new Vector3( CubeHalfSize,  CubeHalfSize,  CubeHalfSize),
                center + new Vector3(-CubeHalfSize,  CubeHalfSize,  CubeHalfSize)
            };

            Span<Vector2> projected = stackalloc Vector2[8];
            Span<float> depths = stackalloc float[8];
            for (var i = 0; i < corners.Length; i++)
            {
                if (!TryProject(camera, corners[i], out projected[i], out depths[i]))
                {
                    return;
                }
            }

            var edgeColor = Color(0.10f, 0.63f, 0.93f, 0.92f);
            var backEdgeColor = Color(0.10f, 0.36f, 0.62f, 0.36f);
            var glowColor = Color(0.66f, 0.91f, 1.0f, 0.30f);

            for (var edgeIndex = 0; edgeIndex < CubeEdges.GetLength(0); edgeIndex++)
            {
                var a = CubeEdges[edgeIndex, 0];
                var b = CubeEdges[edgeIndex, 1];
                var depthAlpha = depths[a] <= depths[b] ? edgeColor : backEdgeColor;

                drawList.AddLine(projected[a] + Vector2.One, projected[b] + Vector2.One, Color(0.02f, 0.06f, 0.10f, 0.46f), 4.0f);
                drawList.AddLine(projected[a], projected[b], glowColor, 4.5f);
                drawList.AddLine(projected[a], projected[b], depthAlpha, 2.0f);
            }

            if (TryProject(camera, center, out var screenCenter, out _))
            {
                drawList.AddCircleFilled(screenCenter, 3.6f, Color(1.0f, 0.97f, 0.52f, 0.95f), 16);
                drawList.AddCircle(screenCenter, 6.0f, Color(0.05f, 0.08f, 0.11f, 0.40f), 16, 1.5f);
            }
        }

        private static void DrawDropRuler(ImDrawListPtr drawList, CameraComponent camera, Vector3 center)
        {
            var ground = new Vector3(center.X, 0.0f, center.Z);
            if (MathF.Abs(center.Y - ground.Y) < 0.05f)
            {
                return;
            }

            if (!TryProject(camera, center, out var top, out _) ||
                !TryProject(camera, ground, out var bottom, out _))
            {
                return;
            }

            DrawDashedLine(drawList, top, bottom, Color(0.98f, 0.72f, 0.20f, 0.82f), 2.0f, 8.0f, 5.0f);
            drawList.AddCircleFilled(bottom, 4.0f, Color(0.98f, 0.72f, 0.20f, 0.75f), 14);

            var heightText = $"Y {center.Y:0.0}";
            var textSize = ImGui.CalcTextSize(heightText);
            var textPos = Vector2.Lerp(top, bottom, 0.5f) + new Vector2(8.0f, -textSize.Y * 0.5f);
            drawList.AddRectFilled(textPos - new Vector2(5.0f, 3.0f), textPos + textSize + new Vector2(5.0f, 3.0f), Color(0.05f, 0.08f, 0.11f, 0.62f), 4.0f);
            drawList.AddText(textPos, Color(1.0f, 0.91f, 0.62f, 0.96f), heightText);
        }

        private static void DrawGroundProjection(ImDrawListPtr drawList, CameraComponent camera, Vector3 center)
        {
            var y = 0.01f;
            Span<Vector3> corners = stackalloc Vector3[4]
            {
                new(center.X - CubeHalfSize, y, center.Z - CubeHalfSize),
                new(center.X + CubeHalfSize, y, center.Z - CubeHalfSize),
                new(center.X + CubeHalfSize, y, center.Z + CubeHalfSize),
                new(center.X - CubeHalfSize, y, center.Z + CubeHalfSize)
            };

            Span<Vector2> projected = stackalloc Vector2[4];
            for (var i = 0; i < corners.Length; i++)
            {
                if (!TryProject(camera, corners[i], out projected[i], out _))
                {
                    return;
                }
            }

            drawList.AddQuadFilled(projected[0], projected[1], projected[2], projected[3], Color(0.08f, 0.15f, 0.23f, 0.16f));
            drawList.AddQuad(projected[0], projected[1], projected[2], projected[3], Color(0.98f, 0.72f, 0.20f, 0.52f), 1.6f);
        }

        private static void DrawBadge(
            ImDrawListPtr drawList,
            Vector2 screenCenter,
            Vector2 sceneMin,
            Vector2 sceneMax,
            VoxelEditorViewModel viewModel)
        {
            var cell = VoxelRasterizer.ToCell(viewModel.HoverVoxelPosition);
            var text = $"XYZ {cell.X}, {cell.Y}, {cell.Z}  {viewModel.HoverCameraDistance:0.0}u";
            if (viewModel.HasHoverDelta)
            {
                var delta = viewModel.HoverDeltaFromLast;
                text += $"  d {delta.X:+0;-0;0},{delta.Y:+0;-0;0},{delta.Z:+0;-0;0}";
            }

            var textSize = ImGui.CalcTextSize(text);
            var padding = new Vector2(8.0f, 5.0f);
            var badgeSize = textSize + padding * 2.0f;
            var position = screenCenter + new Vector2(14.0f, 14.0f);
            position = Vector2.Clamp(position, sceneMin + new Vector2(8.0f), sceneMax - badgeSize - new Vector2(8.0f));

            drawList.AddRectFilled(position + new Vector2(1.5f, 2.0f), position + badgeSize + new Vector2(1.5f, 2.0f), Color(0.02f, 0.05f, 0.08f, 0.25f), 5.0f);
            drawList.AddRectFilled(position, position + badgeSize, Color(0.93f, 0.97f, 1.0f, 0.88f), 5.0f);
            drawList.AddRect(position, position + badgeSize, Color(0.30f, 0.51f, 0.68f, 0.52f), 5.0f);
            drawList.AddText(position + padding, Color(0.11f, 0.25f, 0.36f, 1.0f), text);
        }

        private static void DrawDashedLine(
            ImDrawListPtr drawList,
            Vector2 start,
            Vector2 end,
            uint color,
            float thickness,
            float dashLength,
            float gapLength)
        {
            var delta = end - start;
            var length = delta.Length();
            if (length <= 0.001f)
            {
                return;
            }

            var direction = delta / length;
            var distance = 0.0f;
            while (distance < length)
            {
                var segmentStart = start + direction * distance;
                var segmentEnd = start + direction * MathF.Min(distance + dashLength, length);
                drawList.AddLine(segmentStart, segmentEnd, color, thickness);
                distance += dashLength + gapLength;
            }
        }

        private static bool TryProject(CameraComponent camera, Vector3 worldPosition, out Vector2 screenPosition, out float depth)
        {
            var worldClip = Vector4.Transform(new Vector4(worldPosition, 1.0f), camera._ViewMatrix * camera._ProjectionMatrix);
            if (MathF.Abs(worldClip.W) <= 0.0001f)
            {
                screenPosition = Vector2.Zero;
                depth = 0.0f;
                return false;
            }

            var ndc = new Vector3(worldClip.X, worldClip.Y, worldClip.Z) / worldClip.W;
            var viewport = ImGui.GetMainViewport();
            var windowSize = camera.Context.Window.Size;

            screenPosition = new Vector2(
                viewport.Pos.X + (ndc.X * 0.5f + 0.5f) * windowSize.X,
                viewport.Pos.Y + (1.0f - (ndc.Y * 0.5f + 0.5f)) * windowSize.Y);
            depth = ndc.Z;
            return true;
        }

        private static bool IsNearScene(Vector2 point, Vector2 sceneMin, Vector2 sceneMax, float padding)
        {
            return point.X >= sceneMin.X - padding &&
                   point.Y >= sceneMin.Y - padding &&
                   point.X <= sceneMax.X + padding &&
                   point.Y <= sceneMax.Y + padding;
        }

        private static uint Color(float r, float g, float b, float a)
        {
            return ImGui.ColorConvertFloat4ToU32(new Vector4(r, g, b, a));
        }
    }
}
