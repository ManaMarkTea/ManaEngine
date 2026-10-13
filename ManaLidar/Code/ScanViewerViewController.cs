using System.Numerics;
using System.Runtime.InteropServices;
using CoreAnimation;
using ManaVoxel.Code.Measure;
using SceneKit;

namespace ManaLidar;

/// <summary>
/// A saved scan to look round and measure: drag to orbit, pinch to zoom, two fingers to pan, tap to place a point.
/// Points snap to the corners, real edges and edge middles of the triangle under the finger, using the editor's
/// own measuring rules (the ManaVoxel Measure files, compiled in), so a measurement reads the same on the phone
/// as in Mana.
/// </summary>
public sealed class ScanViewerViewController : UIViewController
{
    /// <summary>A fingertip covers more than a pointer: snapping reaches further.</summary>
    private const float SnapPoints = 28.0f;

    private readonly string _path;
    private readonly MeasureTool _tool = new() { Active = true };
    private readonly Dictionary<IntPtr, ScanRoomMesh> _rooms = new();
    private SCNView _view = null!;
    private MeasureCanvas _canvas = null!;
    private UILabel _hint = null!;
    private UISegmentedControl _modes = null!;
    private UIButton _floor = null!;
    private UIButton _units = null!;
    private UIButton _done = null!;
    private CADisplayLink? _link;
    private ScanCamera? _camera;
    private readonly List<SCNMaterial> _materials = new();
    private UISlider _cut = null!;
    private UILabel _cutLabel = null!;
    private UIButton _walk = null!;
    private float _floorY;
    private float _topY = 3f;

    // Mid-grey rather than near-black: a hole in the scan then reads as a hole, not as a black thing in the way.
    private static readonly UIColor Backdrop = UIColor.FromWhiteAlpha(0.42f, 1);

    // Everything above the cut height is not drawn: the ceiling, then the tops of things, to look down into a room.
    private const string CutShader = """
        #pragma arguments
        float cutHeight;
        #pragma body
        float4 world = scn_frame.inverseViewTransform * float4(_surface.position, 1.0);
        if (world.y > cutHeight) {
            discard_fragment();
        }
        """;

    public ScanViewerViewController(string path)
    {
        _path = path;
        Title = Path.GetFileNameWithoutExtension(path);
    }

    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        View!.BackgroundColor = Backdrop;
        NavigationItem.RightBarButtonItem = new UIBarButtonItem(UIBarButtonSystemItem.Action,
            (_, _) => ScanFiles.Share(this, _path, _done));

        _view = new SCNView(CGRect.Empty)
        {
            TranslatesAutoresizingMaskIntoConstraints = false,
            Scene = new SCNScene(),
            BackgroundColor = Backdrop,
            AntialiasingMode = SCNAntialiasingMode.Multisampling4X,
        };
        View.AddSubview(_view);
        _view.AddGestureRecognizer(new UITapGestureRecognizer(tap => OnTap(tap.LocationInView(_view))));

        _canvas = new MeasureCanvas(_tool, _view)
        {
            TranslatesAutoresizingMaskIntoConstraints = false,
            UserInteractionEnabled = false,
            BackgroundColor = UIColor.Clear,
            Opaque = false,
        };
        View.AddSubview(_canvas);

        _hint = new UILabel
        {
            TranslatesAutoresizingMaskIntoConstraints = false,
            Text = "Loading...",
            TextColor = UIColor.White,
            TextAlignment = UITextAlignment.Center,
            Lines = 0,
        };
        if (UIFont.SystemFontOfSize(14) is { } hintFont)
        {
            _hint.Font = hintFont;
        }
        View.AddSubview(_hint);

        _modes = new UISegmentedControl("Distance", "Radius", "Path", "Ray") { TranslatesAutoresizingMaskIntoConstraints = false, SelectedSegment = 0 };
        _modes.ValueChanged += (_, _) =>
        {
            _tool.Mode = (MeasureMode)(int)_modes.SelectedSegment;
            UpdateControls();
        };
        _floor = Button("Floor", () => _tool.StayOnFirstSurface = !_tool.StayOnFirstSurface);
        _units = Button("m", () => _tool.Units = _tool.Units == MeasureUnits.Metric ? MeasureUnits.Imperial : MeasureUnits.Metric);
        var undo = Button("Undo", _tool.Undo);
        var clear = Button("Clear", _tool.Clear);
        // Done finishes a path; in Ray mode the same button picks which way the ray fires.
        _done = Button("Done", () =>
        {
            if (_tool.Mode == MeasureMode.Ray)
            {
                _tool.RayDirection = (MeasureRayDirection)(((int)_tool.RayDirection + 1) % 3);
            }
            else
            {
                _tool.Finish();
            }
        });
        // Ray mode's ray: SceneKit's own test along a segment, the scan's back faces included (a ceiling seen from
        // below, a cabinet's far side).
        _tool.CastRay = (origin, direction) =>
        {
            var end = origin + direction * 50f;
            var hits = _view.Scene?.RootNode.HitTest(new SCNVector3(origin.X, origin.Y, origin.Z),
                new SCNVector3(end.X, end.Y, end.Z), new SCNHitTestOptions { SearchMode = SCNHitTestSearchMode.Closest, BackFaceCulling = false });
            return hits?.FirstOrDefault() is { } hit ? ToVector(hit.WorldCoordinates) : null;
        };
        _walk = Button("Walk", () =>
        {
            _camera?.SetWalking(!_camera.Walking);
            UpdateControls();
        });
        var top = Button("Top", () => _camera?.TopDown());
        var moves = new UIStackView(new UIView[] { _walk, top })
        {
            TranslatesAutoresizingMaskIntoConstraints = false,
            Axis = UILayoutConstraintAxis.Vertical,
            Spacing = 8,
        };
        View.AddSubview(moves);

        _cut = new UISlider { TranslatesAutoresizingMaskIntoConstraints = false, MinValue = 0, MaxValue = 1, Value = 1 };
        _cut.ValueChanged += (_, _) => ApplyCut();
        _cutLabel = new UILabel { TranslatesAutoresizingMaskIntoConstraints = false, TextColor = UIColor.White, Text = "Cut: off" };
        if (UIFont.MonospacedDigitSystemFontOfSize(13, UIFontWeight.Medium) is { } cutFont)
        {
            _cutLabel.Font = cutFont;
        }
        View.AddSubview(_cut);
        View.AddSubview(_cutLabel);

        var buttons = new UIStackView(new UIView[] { _floor, _units, undo, clear, _done })
        {
            TranslatesAutoresizingMaskIntoConstraints = false,
            Axis = UILayoutConstraintAxis.Horizontal,
            Distribution = UIStackViewDistribution.FillEqually,
            Spacing = 8,
        };
        View.AddSubview(_modes);
        View.AddSubview(buttons);

        var safe = View.SafeAreaLayoutGuide;
        NSLayoutConstraint.ActivateConstraints(new[]
        {
            _view.TopAnchor.ConstraintEqualTo(View.TopAnchor),
            _view.BottomAnchor.ConstraintEqualTo(View.BottomAnchor),
            _view.LeadingAnchor.ConstraintEqualTo(View.LeadingAnchor),
            _view.TrailingAnchor.ConstraintEqualTo(View.TrailingAnchor),
            _canvas.TopAnchor.ConstraintEqualTo(View.TopAnchor),
            _canvas.BottomAnchor.ConstraintEqualTo(View.BottomAnchor),
            _canvas.LeadingAnchor.ConstraintEqualTo(View.LeadingAnchor),
            _canvas.TrailingAnchor.ConstraintEqualTo(View.TrailingAnchor),

            _hint.TopAnchor.ConstraintEqualTo(safe.TopAnchor, 8),
            _hint.LeadingAnchor.ConstraintEqualTo(safe.LeadingAnchor, 16),
            _hint.TrailingAnchor.ConstraintEqualTo(safe.TrailingAnchor, -16),

            buttons.LeadingAnchor.ConstraintEqualTo(safe.LeadingAnchor, 12),
            buttons.TrailingAnchor.ConstraintEqualTo(safe.TrailingAnchor, -12),
            buttons.BottomAnchor.ConstraintEqualTo(safe.BottomAnchor, -10),
            buttons.HeightAnchor.ConstraintEqualTo(40),
            _modes.LeadingAnchor.ConstraintEqualTo(safe.LeadingAnchor, 12),
            _modes.TrailingAnchor.ConstraintEqualTo(safe.TrailingAnchor, -12),
            _modes.BottomAnchor.ConstraintEqualTo(buttons.TopAnchor, -8),

            _cutLabel.LeadingAnchor.ConstraintEqualTo(safe.LeadingAnchor, 14),
            _cutLabel.CenterYAnchor.ConstraintEqualTo(_cut.CenterYAnchor),
            _cutLabel.WidthAnchor.ConstraintEqualTo(96),
            _cut.LeadingAnchor.ConstraintEqualTo(_cutLabel.TrailingAnchor, 6),
            _cut.TrailingAnchor.ConstraintEqualTo(safe.TrailingAnchor, -14),
            _cut.BottomAnchor.ConstraintEqualTo(_modes.TopAnchor, -8),

            moves.TrailingAnchor.ConstraintEqualTo(safe.TrailingAnchor, -12),
            moves.TopAnchor.ConstraintEqualTo(_hint.BottomAnchor, 10),
            moves.WidthAnchor.ConstraintEqualTo(64),
            _walk.HeightAnchor.ConstraintEqualTo(36),
            top.HeightAnchor.ConstraintEqualTo(36),
        });

        UpdateControls();
        Load();
    }

    public override void ViewDidAppear(bool animated)
    {
        base.ViewDidAppear(animated);
        // The labels follow the scan as the camera moves, so the canvas redraws with the view.
        _link = CADisplayLink.Create(() =>
        {
            _canvas.SetNeedsDisplay();
            UpdateControls();
        });
        _link.PreferredFramesPerSecond = 30;
        _link.AddToRunLoop(NSRunLoop.Main, NSRunLoopMode.Common);
    }

    public override void ViewWillDisappear(bool animated)
    {
        base.ViewWillDisappear(animated);
        _link?.Invalidate();
        _link = null;
    }

    private async void Load()
    {
        IReadOnlyList<ScanRoomMesh> rooms;
        try
        {
            rooms = await Task.Run(() => ScanGlbReader.Read(_path));
        }
        catch (Exception ex)
        {
            _hint.Text = $"Could not open the scan: {ex.Message}";
            return;
        }

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var room in rooms)
        {
            var node = BuildNode(room);
            if (node.Geometry?.FirstMaterial is { } material)
            {
                _materials.Add(material);
            }
            _rooms[node.Handle] = room;
            _view.Scene!.RootNode.AddChildNode(node);
            foreach (var p in room.Positions)
            {
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
        }
        if (rooms.Count == 0 || min.X > max.X)
        {
            _hint.Text = "This scan is empty.";
            return;
        }

        _floorY = min.Y;
        _topY = max.Y;
        var camera = new SCNNode { Camera = new SCNCamera { ZNear = 0.02, ZFar = MathF.Max((max - min).Length(), 1f) * 20, FieldOfView = 60 } };
        _view.Scene!.RootNode.AddChildNode(camera);
        _view.PointOfView = camera;
        _camera = new ScanCamera(_view, camera, PickVisible);
        _camera.Frame(min, max);
        ApplyCut();
        _hint.Text = "Tap to measure. Drag to turn, two fingers to move, pinch to zoom, hold to go to a spot.";
    }

    private void OnTap(CGPoint at)
    {
        var pointer = new Vector2((float)at.X, (float)at.Y);
        MeasureSnap? snap = null;
        if (VisibleHit(at) is { } hit && _rooms.TryGetValue(hit.Node.Handle, out var room))
        {
            var first = (int)hit.FaceIndex * 3;
            if (first + 2 < room.Indices.Length)
            {
                var a = room.Positions[room.Indices[first]];
                var b = room.Positions[room.Indices[first + 1]];
                var c = room.Positions[room.Indices[first + 2]];
                var normal = Vector3.Normalize(Vector3.Cross(b - a, c - a));
                var cameraPosition = _view.PointOfView?.WorldPosition is { } eye ? new Vector3(eye.X, eye.Y, eye.Z) : Vector3.Zero;
                var position = ToVector(hit.WorldCoordinates);
                // Facing the viewer, like the editor's picker: the side you see is the one you measure from.
                if (Vector3.Dot(normal, cameraPosition - position) < 0)
                {
                    normal = -normal;
                }
                var creases = MeasureSnapping.CreaseEdges(room.Positions, room.Indices, first);
                snap = MeasureSnapping.FromTriangle(position, normal, a, b, c, pointer, ToScreen, SnapPoints, _tool.Targets, creases);
            }
        }
        if (snap == null)
        {
            // Off the scan: the floor plane it was exported on (y = 0).
            var near = _view.UnprojectPoint(new SCNVector3((float)at.X, (float)at.Y, 0));
            var far = _view.UnprojectPoint(new SCNVector3((float)at.X, (float)at.Y, 1));
            snap = MeasureSnapping.OnGround(ToVector(near), Vector3.Normalize(ToVector(far) - ToVector(near)), 0f);
        }
        _tool.Point(snap, shiftHeld: false, click: true);
        if (_tool.Mode == MeasureMode.Ray)
        {
            _hint.Text = _tool.RayPreview is { To: null } ? "No surface that way." : "Tap a spot: the ray goes to the next surface.";
        }
        _canvas.SetNeedsDisplay();
    }

    // The nearest hit the cut has not hidden: taps go through a cut-away ceiling to what can be seen.
    private SCNHitTestResult? VisibleHit(CGPoint at)
    {
        var hits = _view.HitTest(at, new SCNHitTestOptions { SearchMode = SCNHitTestSearchMode.All, SortResults = true });
        var cut = CutHeight();
        return hits.FirstOrDefault(h => h.WorldCoordinates.Y <= cut);
    }

    private Vector3? PickVisible(CGPoint at) => VisibleHit(at) is { } hit ? ToVector(hit.WorldCoordinates) : null;

    private float CutHeight() => _cut.Value >= 0.999f ? float.MaxValue : _floorY + (_topY - _floorY) * _cut.Value;

    private void ApplyCut()
    {
        var height = CutHeight();
        // Off is a height far above anything, so the shader runs the same either way.
        var value = NSNumber.FromFloat(height == float.MaxValue ? 1e6f : height);
        foreach (var material in _materials)
        {
            material.SetValueForKey(value, new NSString("cutHeight"));
        }
        _cutLabel.Text = height == float.MaxValue ? "Cut: off" : $"Cut: {_tool.Format(height - _floorY)}";
    }

    private Vector2? ToScreen(Vector3 world)
    {
        var p = _view.ProjectPoint(new SCNVector3(world.X, world.Y, world.Z));
        return p.Z is >= 0 and <= 1 ? new Vector2(p.X, p.Y) : null;
    }

    private void UpdateControls()
    {
        _modes.SelectedSegment = (int)_tool.Mode;
        _floor.BackgroundColor = _tool.StayOnFirstSurface ? UIColor.SystemBlue : UIColor.FromWhiteAlpha(0.25f, 0.9f);
        _units.SetTitle(_tool.Units == MeasureUnits.Metric ? "m" : "ft", UIControlState.Normal);
        _walk.SetTitle(_camera?.Walking == true ? "Orbit" : "Walk", UIControlState.Normal);
        var ray = _tool.Mode == MeasureMode.Ray;
        _done.SetTitle(ray ? _tool.RayDirection switch { MeasureRayDirection.Up => "Up", MeasureRayDirection.Down => "Down", _ => "Out" } : "Done",
            UIControlState.Normal);
        _done.Enabled = ray || _tool.Current is { Mode: MeasureMode.Path, Points.Count: >= 2 };
        _done.Alpha = _done.Enabled ? 1f : 0.4f;
    }

    private static UIButton Button(string title, Action tapped)
    {
        var button = UIButton.FromType(UIButtonType.System);
        button.SetTitle(title, UIControlState.Normal);
        button.SetTitleColor(UIColor.White, UIControlState.Normal);
        button.BackgroundColor = UIColor.FromWhiteAlpha(0.25f, 0.9f);
        button.Layer.CornerRadius = 10;
        button.TouchUpInside += (_, _) => tapped();
        return button;
    }

    private static Vector3 ToVector(SCNVector3 v) => new(v.X, v.Y, v.Z);

    private static SCNNode BuildNode(ScanRoomMesh room)
    {
        var vertices = room.Positions.Select(p => new SCNVector3(p.X, p.Y, p.Z)).ToArray();
        var sources = new List<SCNGeometrySource>
        {
            SCNGeometrySource.FromVertices(vertices),
            SCNGeometrySource.FromNormals(room.Normals.Select(n => new SCNVector3(n.X, n.Y, n.Z)).ToArray()),
        };
        if (room.Uvs != null)
        {
            // glTF and SceneKit both put (0, 0) at the image's top-left, so the UVs carry over as they are.
            sources.Add(SCNGeometrySource.FromTextureCoordinates(room.Uvs.Select(uv => new CGPoint(uv.X, uv.Y)).ToArray()));
        }
        if (room.Colors != null)
        {
            var colours = new float[room.Colors.Length * 4];
            for (var i = 0; i < room.Colors.Length; i++)
            {
                var c = room.Colors[i];
                colours[i * 4] = (c & 0xFF) / 255f;
                colours[i * 4 + 1] = ((c >> 8) & 0xFF) / 255f;
                colours[i * 4 + 2] = ((c >> 16) & 0xFF) / 255f;
                colours[i * 4 + 3] = 1f;
            }
            sources.Add(SCNGeometrySource.FromData(NSData.FromArray(MemoryMarshal.AsBytes(colours.AsSpan()).ToArray()),
                SCNGeometrySourceSemantics.Color, room.Colors.Length, true, 4, sizeof(float), 0, 4 * sizeof(float)));
        }
        var indexData = NSData.FromArray(MemoryMarshal.AsBytes(room.Indices.AsSpan()).ToArray());
        var element = SCNGeometryElement.FromData(indexData, SCNGeometryPrimitiveType.Triangles, room.TriangleCount, sizeof(uint));
        var geometry = SCNGeometry.Create(sources.ToArray(), new[] { element });

        // Unlit, as in Mana: the photos already hold the room's light.
        var material = new SCNMaterial
        {
            LightingModelName = room.Unlit ? SCNLightingModel.Constant : SCNLightingModel.PhysicallyBased,
            // Said outright rather than left to the default: a surface seen from behind is not drawn, so walls
            // between you and the room vanish from outside it.
            CullMode = SCNCullMode.Back,
            DoubleSided = false,
            ShaderModifiers = new SCNShaderModifiers { EntryPointFragment = CutShader },
        };
        material.Diffuse.Contents = room.Image != null
            ? UIImage.LoadFromData(NSData.FromArray(room.Image)) ?? (NSObject)UIColor.White
            : UIColor.White;
        material.Diffuse.MipFilter = SCNFilterMode.Linear;
        material.Diffuse.WrapS = SCNWrapMode.Clamp;
        material.Diffuse.WrapT = SCNWrapMode.Clamp;
        geometry.FirstMaterial = material;
        return new SCNNode { Geometry = geometry, Name = room.Name };
    }
}
