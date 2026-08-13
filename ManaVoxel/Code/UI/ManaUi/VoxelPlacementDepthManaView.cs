using Mana;
using ManaUI;
using ManaVoxel.Code.Tools;

namespace ManaVoxel.Code.UI.ManaUi
{
    /// <summary>
    /// Where the voxel under the cursor actually is: a wireframe ghost of the cell, its shadow on the ground,
    /// and a dashed ruler between the two.
    /// <para>
    /// A perspective view gives no reliable sense of depth for a single floating cube -- the whole reason
    /// this exists is that a voxel two metres up and a voxel ten metres back look identical. The ruler down
    /// to the ground plane is what resolves it.
    /// </para>
    /// </summary>
    public sealed class VoxelPlacementDepthOverlay : UiElement
    {
        private const float CubeHalfSize = 0.5f;

        /// <summary>How far outside the viewport the cell can be before the overlay gives up on it.</summary>
        private const float ScenePadding = 40.0f;

        private static readonly (int A, int B)[] CubeEdges =
        {
            (0, 1), (1, 2), (2, 3), (3, 0),
            (4, 5), (5, 6), (6, 7), (7, 4),
            (0, 4), (1, 5), (2, 6), (3, 7)
        };

        private static readonly Vector4 EdgeColor = new(0.10f, 0.63f, 0.93f, 0.92f);
        private static readonly Vector4 BackEdgeColor = new(0.10f, 0.36f, 0.62f, 0.36f);
        private static readonly Vector4 GlowColor = new(0.66f, 0.91f, 1.00f, 0.30f);
        private static readonly Vector4 RulerColor = new(0.98f, 0.72f, 0.20f, 0.82f);
        private static readonly Vector4 HubColor = new(1.00f, 0.97f, 0.52f, 0.95f);

        private readonly VoxelEditorViewModel _viewModel;
        private readonly Func<CameraComponent?> _camera;

        private readonly Vector2[] _projected = new Vector2[8];
        private readonly float[] _depths = new float[8];

        public VoxelPlacementDepthOverlay(VoxelEditorViewModel viewModel, Func<CameraComponent?> camera)
        {
            _viewModel = viewModel;
            _camera = camera;

            // A readout over the scene. Taking the pointer here would mean the cursor could not place the
            // very voxel this is drawing.
            IsHitTestVisible = false;
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize) => Vector2.Zero;

        protected override void OnRender(ManaUiDrawList drawList)
        {
            if (!_viewModel.HasHoverVoxel || _camera() is not { } camera || Root == null)
            {
                return;
            }

            var centre = _viewModel.HoverVoxelPosition;
            if (!TryProject(camera, centre, out var screenCentre, out _) || !IsNearScene(screenCentre))
            {
                return;
            }

            drawList.PushClip(Bounds);

            DrawGroundShadow(drawList, camera, centre);
            DrawRuler(drawList, camera, centre);
            DrawGhost(drawList, camera, centre, screenCentre);
            DrawBadge(drawList, screenCentre);

            drawList.PopClip();
        }

        /// <summary>
        /// The cell's wireframe. Edges are drawn dim when their far end is further away, which is the only
        /// thing distinguishing the back of the box from the front.
        /// </summary>
        private void DrawGhost(ManaUiDrawList drawList, CameraComponent camera, Vector3 centre, Vector2 screenCentre)
        {
            Span<Vector3> corners = stackalloc Vector3[8]
            {
                centre + new Vector3(-CubeHalfSize, -CubeHalfSize, -CubeHalfSize),
                centre + new Vector3( CubeHalfSize, -CubeHalfSize, -CubeHalfSize),
                centre + new Vector3( CubeHalfSize,  CubeHalfSize, -CubeHalfSize),
                centre + new Vector3(-CubeHalfSize,  CubeHalfSize, -CubeHalfSize),
                centre + new Vector3(-CubeHalfSize, -CubeHalfSize,  CubeHalfSize),
                centre + new Vector3( CubeHalfSize, -CubeHalfSize,  CubeHalfSize),
                centre + new Vector3( CubeHalfSize,  CubeHalfSize,  CubeHalfSize),
                centre + new Vector3(-CubeHalfSize,  CubeHalfSize,  CubeHalfSize)
            };

            for (var i = 0; i < corners.Length; i++)
            {
                // All or nothing: half a projected box behind the camera is worse than none of it.
                if (!TryProject(camera, corners[i], out _projected[i], out _depths[i]))
                {
                    return;
                }
            }

            foreach (var (a, b) in CubeEdges)
            {
                var color = _depths[a] <= _depths[b] ? EdgeColor : BackEdgeColor;

                drawList.DrawLine(_projected[a], _projected[b], GlowColor, 4.5f);
                drawList.DrawLine(_projected[a], _projected[b], color, 2.0f);
            }

            drawList.FillRect(
                new UiRect(screenCentre.X - 3.6f, screenCentre.Y - 3.6f, 7.2f, 7.2f),
                HubColor,
                new CornerRadius(3.6f));
        }

        /// <summary>The drop from the cell to the ground plane, and how far that is.</summary>
        private void DrawRuler(ManaUiDrawList drawList, CameraComponent camera, Vector3 centre)
        {
            var ground = new Vector3(centre.X, 0.0f, centre.Z);
            if (MathF.Abs(centre.Y - ground.Y) < 0.05f)
            {
                return;
            }

            if (!TryProject(camera, centre, out var top, out _)
                || !TryProject(camera, ground, out var bottom, out _))
            {
                return;
            }

            DrawDashed(drawList, top, bottom, RulerColor, 2.0f, dash: 8.0f, gap: 5.0f);

            drawList.FillRect(
                new UiRect(bottom.X - 4.0f, bottom.Y - 4.0f, 8.0f, 8.0f),
                RulerColor,
                new CornerRadius(4.0f));

            Badge(drawList, Vector2.Lerp(top, bottom, 0.5f) + new Vector2(10.0f, 0.0f), $"Y {centre.Y:0.0}", centred: true);
        }

        /// <summary>The cell's footprint on the ground, so its position reads in plan as well as elevation.</summary>
        private void DrawGroundShadow(ManaUiDrawList drawList, CameraComponent camera, Vector3 centre)
        {
            // Just above zero, so it does not fight the ground plane for the same pixels.
            const float y = 0.01f;

            Span<Vector3> corners = stackalloc Vector3[4]
            {
                new(centre.X - CubeHalfSize, y, centre.Z - CubeHalfSize),
                new(centre.X + CubeHalfSize, y, centre.Z - CubeHalfSize),
                new(centre.X + CubeHalfSize, y, centre.Z + CubeHalfSize),
                new(centre.X - CubeHalfSize, y, centre.Z + CubeHalfSize)
            };

            Span<Vector2> projected = stackalloc Vector2[4];
            for (var i = 0; i < corners.Length; i++)
            {
                if (!TryProject(camera, corners[i], out projected[i], out _))
                {
                    return;
                }
            }

            // An outline rather than the ImGui version's filled quad: a filled convex quad is four triangles
            // and this pipeline draws boxes. The outline carries the footprint on its own.
            for (var i = 0; i < 4; i++)
            {
                drawList.DrawLine(projected[i], projected[(i + 1) % 4], RulerColor with { W = 0.52f }, 1.6f);
            }
        }

        private void DrawBadge(ManaUiDrawList drawList, Vector2 screenCentre)
        {
            var cell = VoxelRasterizer.ToCell(_viewModel.HoverVoxelPosition);
            var text = $"XYZ {cell.X}, {cell.Y}, {cell.Z}  {_viewModel.HoverCameraDistance:0.0}u";

            if (_viewModel.HasHoverDelta)
            {
                var delta = _viewModel.HoverDeltaFromLast;
                text += $"  d {delta.X:+0;-0;0},{delta.Y:+0;-0;0},{delta.Z:+0;-0;0}";
            }

            Badge(drawList, screenCentre + new Vector2(14.0f, 14.0f), text, centred: false);
        }

        private void Badge(ManaUiDrawList drawList, Vector2 at, string text, bool centred)
        {
            var root = Root;
            if (root == null)
            {
                return;
            }

            var theme = root.Theme;
            var font = root.Fonts.Get(theme.FontFamilyMono, theme.FontSizeSmall, root.Scale);

            var layout = new TextLayout { Font = font, Text = text };
            layout.Update();

            var padding = new Vector2(8.0f, 5.0f);
            var size = layout.Size + padding * 2.0f;
            var position = centred ? at - new Vector2(0.0f, size.Y * 0.5f) : at;

            // Held inside the viewport, so a cell near the edge does not put its own readout off screen.
            position = Vector2.Clamp(
                position,
                new Vector2(Bounds.X + 8.0f, Bounds.Y + 8.0f),
                new Vector2(
                    MathF.Max(Bounds.X + 8.0f, Bounds.Right - size.X - 8.0f),
                    MathF.Max(Bounds.Y + 8.0f, Bounds.Bottom - size.Y - 8.0f)));

            var box = new UiRect(position.X, position.Y, size.X, size.Y);

            drawList.DrawBox(
                box,
                theme.Frosted(theme.Surface with { W = 0.88f }),
                theme.Border,
                1.0f,
                new CornerRadius(5.0f),
                theme.BackdropBlur);
            layout.Render(drawList, box.Deflate(new Thickness(padding.X, padding.Y)), theme.Text);
        }

        /// <summary>
        /// A dashed line, as its dashes. Dashed rather than solid so it reads as a measurement rather than as
        /// another edge of the box.
        /// </summary>
        private static void DrawDashed(
            ManaUiDrawList drawList,
            Vector2 from,
            Vector2 to,
            Vector4 color,
            float thickness,
            float dash,
            float gap)
        {
            var delta = to - from;
            var length = delta.Length();
            if (length <= 0.001f)
            {
                return;
            }

            var direction = delta / length;

            for (var distance = 0.0f; distance < length; distance += dash + gap)
            {
                drawList.DrawLine(
                    from + direction * distance,
                    from + direction * MathF.Min(distance + dash, length),
                    color,
                    thickness);
            }
        }

        private bool TryProject(CameraComponent camera, Vector3 world, out Vector2 screen, out float depth)
        {
            var size = camera.Context.Window.Size;

            return TryProject(
                camera._ViewMatrix * camera._ProjectionMatrix,
                new Vector2(size.X, size.Y),
                world,
                out screen,
                out depth);
        }

        /// <summary>
        /// World to screen. Returns false behind the camera, where w has crossed zero and the divide gives a
        /// mirror image somewhere off screen -- which would draw a ghost that is not merely misplaced but
        /// inside out.
        /// <para>
        /// Takes matrices rather than a camera so it can be checked: a CameraComponent subscribes to the
        /// window's resize event as it is built, so one cannot exist without a window.
        /// </para>
        /// </summary>
        public static bool TryProject(
            Matrix4x4 viewProjection,
            Vector2 windowSize,
            Vector3 world,
            out Vector2 screen,
            out float depth)
        {
            var clip = Vector4.Transform(new Vector4(world, 1.0f), viewProjection);
            if (clip.W <= 0.0001f)
            {
                screen = Vector2.Zero;
                depth = 0.0f;
                return false;
            }

            var ndc = new Vector3(clip.X, clip.Y, clip.Z) / clip.W;

            // Window pixels, which is also what ManaUI's own coordinates are -- so no viewport offset is
            // needed here, unlike the ImGui version which drew into a separate foreground list.
            screen = new Vector2(
                (ndc.X * 0.5f + 0.5f) * windowSize.X,
                (1.0f - (ndc.Y * 0.5f + 0.5f)) * windowSize.Y);

            depth = ndc.Z;
            return true;
        }

        private bool IsNearScene(Vector2 point)
        {
            return point.X >= Bounds.X - ScenePadding
                && point.Y >= Bounds.Y - ScenePadding
                && point.X <= Bounds.Right + ScenePadding
                && point.Y <= Bounds.Bottom + ScenePadding;
        }
    }
}
