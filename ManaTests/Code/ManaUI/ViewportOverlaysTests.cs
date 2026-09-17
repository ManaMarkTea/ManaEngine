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

        // Foreshortening is honest, so an axis aimed at the viewer is a stub rather than a spoke. The tip
        // is what says which way it goes, filled toward and hollow away.
        Check("an axis aimed at the viewer is a stub", z.Offset.Length() < 2.0f, $"{z.Offset.Length():0.00}px");
        // Compared against an axis genuinely pointing away, not against X -- X lies exactly in the screen
        // plane here, which is the boundary between the two and belongs to neither.
        var away = AxisTripod.Project("-Z", -Vector3.UnitZ, view, UiColor.White);
        Check("and its tip says so", z.PointsAtCamera && !away.PointsAtCamera, "filled toward, hollow away");

        // ---------------------------------------------------------------- looking down an axis
        // The tripod used to lie here. Projecting an axis that points along the view direction gives almost
        // nothing, so its direction is decided by the rounding -- and the old code normalised that and
        // rescaled it to a floored quarter-length spoke, which pointed it somewhere definite and wrong.
        // Tilting a fiftieth of a radian swung world X from nine pixels down to nine pixels up.
        static Matrix4x4 LookingDownX(float tilt) => Matrix4x4.CreateLookAt(
            new Vector3(-10.0f, MathF.Sin(tilt) * 10.0f, 0.0f), Vector3.Zero, Vector3.UnitY);

        var tiltedDown = AxisTripod.Project("X", Vector3.UnitX, LookingDownX(-0.02f), UiColor.White);
        var level = AxisTripod.Project("X", Vector3.UnitX, LookingDownX(0.0f), UiColor.White);
        var tiltedUp = AxisTripod.Project("X", Vector3.UnitX, LookingDownX(0.02f), UiColor.White);

        Check("looking down an axis leaves it a stub",
            level.Offset.Length() < 2.0f, $"{level.Offset.Length():0.00}px");

        // The real defect: a tiny camera movement must produce a tiny change on screen. It swung 19px.
        Check("and a small tilt moves it a small amount",
            Vector2.Distance(tiltedDown.Offset, tiltedUp.Offset) < 3.0f,
            $"{Vector2.Distance(tiltedDown.Offset, tiltedUp.Offset):0.00}px apart");

        // Tilting the camera up must not send the axis the other way -- which is exactly how it read.
        Check("without flipping end to end",
            MathF.Abs(tiltedDown.Offset.Y) < 2.0f && MathF.Abs(tiltedUp.Offset.Y) < 2.0f,
            $"{tiltedDown.Offset.Y:+0.00;-0.00} -> {tiltedUp.Offset.Y:+0.00;-0.00}");

        // It is still pointing away from the viewer throughout, which is the thing that has not changed.
        Check("and still reading as pointing away",
            !tiltedDown.PointsAtCamera && !level.PointsAtCamera && !tiltedUp.PointsAtCamera,
            "away");

        // Foreshortening has to be proportional, or the tripod is not showing an angle at all: an axis
        // halfway between the screen plane and the view direction is about seven tenths of a full spoke.
        var oblique = AxisTripod.Project("X", Vector3.UnitX, LookingDownX(0.0f), UiColor.White);
        var across = AxisTripod.Project("Z", Vector3.UnitZ, LookingDownX(0.0f), UiColor.White);

        Check("an axis across the view is full length",
            across.Offset.Length() > 30.0f, $"{across.Offset.Length():0.0}px vs stub {oblique.Offset.Length():0.0}px");

        // ---------------------------------------------------------------- the viewport rectangle
        // The viewport is not stored anywhere -- it is whatever the columns have not taken. Getting this
        // wrong puts the gizmo underneath a panel, where it is unreachable.
        var root = new UiRoot(fonts);

        var leftWidth = 200.0f;
        var rightWidth = 300.0f;

        var left = new Border().Fill(UiColor.White).Width(leftWidth);
        var right = new Border().Fill(UiColor.White).Width(rightWidth);

        var layer = new ViewportOverlayLayer(
            root.Theme, () => leftWidth, () => rightWidth, () => 40.0f, () => 0.0f, () => null, null);

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
        // ---------------------------------------------------------------- the gizmo's icons
        // The cluster itself cannot be built here -- it needs a CameraComponent, which needs a window -- but
        // its artwork is static functions over a draw list, and the artwork is what was wrong with it: the
        // four direction arrows were UiIcons.Triangle, which steps a solid triangle out of rounded rows
        // whenever the font has no geometric-shape glyph to borrow. Roboto has none, so the gizmo drew four
        // visible staircases. Two strokes have no steps in them.
        var chevronList = new ManaUiDrawList();
        chevronList.Reset();
        ViewportCameraGizmoManaView.DrawChevron(
            chevronList, new UiRect(0.0f, 0.0f, 24.0f, 24.0f), UiIconDirection.Up, UiColor.White);

        Check("a direction chevron is two strokes, not a staircase",
            chevronList.InstanceCount == 2, $"{chevronList.InstanceCount} instances");

        // It used to be 120 with TOP, BOTTOM and PERSP spelled out on it. A word sets a floor on how small
        // a button can be; the icons are what let the whole thing come down.
        Check("and the gizmo is smaller than the words it used to carry",
            ViewportCameraGizmoManaView.GizmoSize <= 96.0f, $"{ViewportCameraGizmoManaView.GizmoSize:0}px");

        Check("a gizmo needs room", !ViewportCameraGizmoManaView.FitsIn(new Vector2(120.0f, 800.0f)), "hidden");
        Check("and appears once there is", ViewportCameraGizmoManaView.FitsIn(new Vector2(700.0f, 800.0f)));

        // The two used to differ by 26px, which made "a viewport too narrow for the gizmo still carries the
        // tripod" a wide band worth naming. The gizmo is 96 now and the band is 2px, so what is worth
        // asserting is the direction rather than the gap: the tripod never needs MORE room than the gizmo,
        // so there is no viewport that shows the presets without the axis readout beside them.
        Check("the tripod never needs more room than the gizmo",
            ViewportCameraGizmoManaView.TripodSize <= ViewportCameraGizmoManaView.GizmoSize,
            $"tripod {ViewportCameraGizmoManaView.TripodSize:0}, gizmo {ViewportCameraGizmoManaView.GizmoSize:0}");

        Check("and a viewport with room for the gizmo has room for both",
            ViewportCameraGizmoManaView.FitsIn(new Vector2(700.0f, 800.0f))
                && ViewportCameraGizmoManaView.TripodFitsIn(new Vector2(700.0f, 800.0f)));

        Check("while a genuinely cramped one carries neither",
            !ViewportCameraGizmoManaView.FitsIn(new Vector2(120.0f, 200.0f))
                && !ViewportCameraGizmoManaView.TripodFitsIn(new Vector2(120.0f, 200.0f)));

        var rendered = root.Render();
        Check("the layer renders", rendered.Instances.Length > 0, $"{rendered.Instances.Length} instances");
    }
}
