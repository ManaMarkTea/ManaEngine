using Mana.Code.UI.ManaUi;
using ManaUI;
using ManaVoxel.Code.UI.ManaUi;

namespace ManaTests;

/// <summary>
/// The overlays drawn over the scene: the camera gizmo, the axis tripod and the placement readout.
/// <para>
/// A CameraComponent subscribes to the window's resize event as it is built, so one cannot exist in a
/// headless suite. The parts worth checking do not need one: projecting a world point to screen and turning
/// a view matrix into three spokes are both matrix arithmetic, and they take matrices here rather than a
/// camera for exactly that reason.
/// </para>
/// </summary>
public static class ViewportOverlayChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        // A camera at the origin looking down -Z, which is what the engine's own look-at produces.
        var view = Matrix4x4.CreateLookAt(new Vector3(0.0f, 0.0f, 10.0f), Vector3.Zero, Vector3.UnitY);
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4.0f, 1.0f, 0.1f, 100.0f);
        var viewProjection = view * projection;
        var window = new Vector2(800.0f, 600.0f);

        // ---------------------------------------------------------------- projection
        Check("a point at the origin projects to the centre",
            VoxelPlacementDepthOverlay.TryProject(viewProjection, window, Vector3.Zero, out var centre, out _)
                && MathF.Abs(centre.X - 400.0f) < 0.5f && MathF.Abs(centre.Y - 300.0f) < 0.5f,
            centre.ToString());

        // Screen space grows downward and world space does not, so up in the world is up on screen only if
        // the Y flip is right -- and a flip the wrong way looks plausible until something moves.
        VoxelPlacementDepthOverlay.TryProject(viewProjection, window, Vector3.UnitY, out var above, out _);
        Check("and a point above it is higher on screen", above.Y < centre.Y, $"{above.Y:0} vs {centre.Y:0}");

        VoxelPlacementDepthOverlay.TryProject(viewProjection, window, Vector3.UnitX, out var beside, out _);
        Check("a point to the right is further right", beside.X > centre.X, $"{beside.X:0} vs {centre.X:0}");

        // Behind the camera w crosses zero and the divide gives a mirror image somewhere off screen. Drawing
        // that is worse than drawing nothing: the ghost comes out inside out rather than merely misplaced.
        Check("a point behind the camera is refused",
            !VoxelPlacementDepthOverlay.TryProject(
                viewProjection, window, new Vector3(0.0f, 0.0f, 20.0f), out _, out _),
            "refused");

        // Further away is deeper, which is what orders the wireframe's near and far edges.
        VoxelPlacementDepthOverlay.TryProject(viewProjection, window, Vector3.Zero, out _, out var nearDepth);
        VoxelPlacementDepthOverlay.TryProject(
            viewProjection, window, new Vector3(0.0f, 0.0f, -20.0f), out _, out var farDepth);

        Check("depth grows with distance", farDepth > nearDepth, $"{nearDepth:0.000} -> {farDepth:0.000}");

        // ---------------------------------------------------------------- the tripod
        var x = AxisTripod.Project("X", Vector3.UnitX, view, UiColor.White);
        var y = AxisTripod.Project("Y", Vector3.UnitY, view, UiColor.White);
        var z = AxisTripod.Project("Z", Vector3.UnitZ, view, UiColor.White);

        Check("world X points right on screen", x.Offset.X > 1.0f, x.Offset.ToString());
        Check("world Y points up on screen", y.Offset.Y < -1.0f, y.Offset.ToString());

        // Z comes toward this camera, so it projects to almost nothing across the screen -- and is nearest,
        // which is what puts it on top when the three are sorted.
        Check("world Z points at the camera",
            z.Depth > x.Depth && z.Depth > y.Depth,
            $"z {z.Depth:0.00}, x {x.Depth:0.00}, y {y.Depth:0.00}");

        // An axis pointing straight at the camera would otherwise collapse to a point and read as an axis
        // that had stopped existing.
        Check("and still has a visible spoke", z.Offset.Length() > 5.0f, $"{z.Offset.Length():0.0}px");

        // ---------------------------------------------------------------- the viewport rectangle
        // The viewport is not stored anywhere -- it is whatever the columns have not taken. Getting this
        // wrong puts the gizmo underneath a panel, where it is unreachable.
        var root = new UiRoot(fonts);

        var leftWidth = 200.0f;
        var rightWidth = 300.0f;

        var left = new Border().Fill(UiColor.White).Width(leftWidth);
        var right = new Border().Fill(UiColor.White).Width(rightWidth);

        var layer = new ViewportOverlayLayer(
            root.Theme, () => leftWidth, () => rightWidth, topOffset: 40.0f, () => null, null);

        // Stands in for the gizmo, which needs a camera and so cannot be built here. Same slot, same
        // placement -- what is being checked is where the layer puts a top-right overlay, not what it is.
        var marker = new Border().Fill(UiColor.White);
        layer.Add(marker, ViewportSlot.TopRight, new Vector2(ViewportCameraGizmoManaView.GizmoSize),
            ViewportCameraGizmoManaView.FitsIn);

        root.Content = new Canvas().Children(
            layer.Anchor(left: 0.0f, top: 0.0f, right: 0.0f, bottom: 0.0f),
            left.Anchor(left: 0.0f, top: 40.0f, bottom: 0.0f),
            right.Anchor(right: 0.0f, top: 40.0f, bottom: 0.0f));

        root.UpdateLayout(new Vector2(1200, 800));

        var viewport = layer.Viewport;
        Check("the viewport starts where the left column ends",
            MathF.Abs(viewport.X - 200.0f) < 0.5f, $"{viewport.X:0}");

        Check("and ends where the right one begins",
            MathF.Abs(viewport.Right - 900.0f) < 0.5f, $"{viewport.Right:0}");

        Check("below the menu band", MathF.Abs(viewport.Y - 40.0f) < 0.5f, $"{viewport.Y:0}");
        Check("to the bottom of the window", MathF.Abs(viewport.Bottom - 800.0f) < 0.5f, $"{viewport.Bottom:0}");

        // Where an overlay is actually put, not just what Viewport computes. The layer is arranged before
        // the columns are -- it has to be, so they draw over it -- so anything read from their bounds is a
        // frame stale and zero on the first frame. That put the gizmo at the top-right of the whole window,
        // underneath the right column, where it was simply never seen.
        Check("a top-right overlay lands inside the viewport",
            marker.Bounds.Right <= viewport.Right + 0.5f && marker.Bounds.X >= viewport.X - 0.5f,
            $"{marker.Bounds} in {viewport}");

        Check("clear of the right column",
            marker.Bounds.Right < right.Bounds.X, $"ends at {marker.Bounds.Right:0}, column at {right.Bounds.X:0}");

        // And it has to follow a resize, or it is correct exactly once.
        rightWidth = 500.0f;
        right.Width = rightWidth;
        root.UpdateLayout(new Vector2(1200, 800));

        Check("and follows the column being resized",
            MathF.Abs(marker.Bounds.Right - (700.0f - ViewportCameraGizmoManaView.GizmoMargin)) < 0.5f,
            $"ends at {marker.Bounds.Right:0}");

        rightWidth = 300.0f;
        right.Width = rightWidth;
        root.UpdateLayout(new Vector2(1200, 800));

        // A collapsed column gives its space back, which is what a workspace with one column emptied does.
        leftWidth = 0.0f;
        left.Visibility = Visibility.Collapsed;
        root.UpdateLayout(new Vector2(1200, 800));

        Check("a collapsed column gives its space back",
            MathF.Abs(layer.Viewport.X) < 0.5f, $"{layer.Viewport.X:0}");

        // Both gone is the whole window, not a negative rectangle.
        rightWidth = 0.0f;
        right.Visibility = Visibility.Collapsed;
        root.UpdateLayout(new Vector2(1200, 800));

        Check("and both gone is the whole window",
            layer.Viewport.Width > 1199.0f, $"{layer.Viewport.Width:0}px");

        // A window narrower than its own columns. UiRect clamps a backwards rectangle to zero width at its
        // left edge rather than swapping the edges, so the answer is no viewport -- not one overlapping the
        // panel that took the space, which would read as valid while the overlays drew under it.
        left.Visibility = Visibility.Visible;
        right.Visibility = Visibility.Visible;
        leftWidth = 200.0f;
        rightWidth = 300.0f;
        root.UpdateLayout(new Vector2(300, 800));

        Check("a viewport is never inside out", layer.Viewport.Width >= 0.0f, $"{layer.Viewport.Width:0}px");

        Check("and never lands inside a column",
            layer.Viewport.X >= left.Bounds.Right - 0.5f,
            $"starts at {layer.Viewport.X:0}, column ends at {left.Bounds.Right:0}");

        // ---------------------------------------------------------------- fitting
        Check("a gizmo needs room", !ViewportCameraGizmoManaView.FitsIn(new Vector2(120.0f, 800.0f)), "hidden");
        Check("and appears once there is", ViewportCameraGizmoManaView.FitsIn(new Vector2(700.0f, 800.0f)));

        // The tripod is smaller, so a viewport too narrow for the gizmo can still carry it.
        Check("the tripod fits in less",
            ViewportCameraGizmoManaView.TripodFitsIn(new Vector2(140.0f, 200.0f))
                && !ViewportCameraGizmoManaView.FitsIn(new Vector2(140.0f, 200.0f)),
            "tripod only");

        Check("the layer renders", root.Render().Instances.Length >= 0, "no throw");
    }
}
