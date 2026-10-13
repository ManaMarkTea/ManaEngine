using ARKit;
using CoreFoundation;
using SceneKit;

namespace ManaLidar;

/// <summary>
/// The scanner: the camera with the LiDAR mesh drawn over it, Start/Pause, New and Export. Walk the room slowly,
/// pointing at every wall, the floor and the furniture until the wireframe covers them; then Export.
/// </summary>
public sealed class ScanViewController : UIViewController
{
    private enum State { Idle, Scanning, Paused }

    private readonly ScanRecording _recording = new();
    private ARSCNView _sceneView = null!;
    private ScanSessionDelegate _sessionDelegate = null!;
    private ARWorldTrackingConfiguration? _configuration;
    private UILabel _status = null!;
    private UIButton _primaryButton = null!;
    private UIButton _newButton = null!;
    private UIButton _exportButton = null!;
    private NSTimer? _statusTimer;
    private State _state = State.Idle;
    private bool _exporting;
    private UISegmentedControl _detailPicker = null!;
    private ScanDetail _detail;
    private const string DetailKey = "ScanDetail";
    // A building scanned a room at a time; its GLB is rewritten as each room goes in.
    private ScanBuilding? _building;
    private string? _buildingPath;
    private string? _notice;
    private UIBarButtonItem _streamButton = null!;
    private const string LastEditorKey = "LastStreamEditor";

    // How much more iOS will let this app have before it closes it: the number that matters on a phone.
    [System.Runtime.InteropServices.DllImport("/usr/lib/libSystem.dylib")]
    private static extern nuint os_proc_available_memory();

    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        Title = "ManaLidar";
        View!.BackgroundColor = UIColor.Black;
        NavigationItem.RightBarButtonItem = new UIBarButtonItem("Scans", UIBarButtonItemStyle.Plain,
            (_, _) => NavigationController?.PushViewController(new ScansViewController(), true));
        _streamButton = new UIBarButtonItem("Stream", UIBarButtonItemStyle.Plain, (_, _) => ToggleStream());
        NavigationItem.LeftBarButtonItem = _streamButton;

        _sceneView = new ARSCNView
        {
            TranslatesAutoresizingMaskIntoConstraints = false,
            Scene = new SCNScene(),
            AutomaticallyUpdatesLighting = false,
        };
        View.AddSubview(_sceneView);

        _sessionDelegate = new ScanSessionDelegate(_recording, _sceneView.Scene.RootNode);
        _sceneView.Session.Delegate = _sessionDelegate;
        _sceneView.Session.DelegateQueue = new DispatchQueue("ManaLidar.session");

        _status = new UILabel
        {
            TranslatesAutoresizingMaskIntoConstraints = false,
            Lines = 0,
            TextAlignment = UITextAlignment.Center,
            TextColor = UIColor.White,
            BackgroundColor = UIColor.FromWhiteAlpha(0, 0.45f),
        };
        if (UIFont.MonospacedDigitSystemFontOfSize(15, UIFontWeight.Medium) is { } statusFont)
        {
            _status.Font = statusFont;
        }
        _status.Layer.CornerRadius = 10;
        _status.ClipsToBounds = true;
        View.AddSubview(_status);

        _detailPicker = new UISegmentedControl("Standard", "High", "Ultra")
        {
            TranslatesAutoresizingMaskIntoConstraints = false,
            BackgroundColor = UIColor.FromWhiteAlpha(0, 0.45f),
        };
        _detailPicker.ValueChanged += (_, _) => SetDetail((ScanDetail)(int)_detailPicker.SelectedSegment);
        View.AddSubview(_detailPicker);
        var saved = (int)NSUserDefaults.StandardUserDefaults.IntForKey(DetailKey);
        SetDetail(Enum.IsDefined(typeof(ScanDetail), saved) ? (ScanDetail)saved : ScanDetail.Standard);

        _newButton = MakeButton("New", UIColor.SystemGray, ConfirmNewScan);
        _primaryButton = MakeButton("Start", UIColor.SystemGreen, TogglePrimary);
        _exportButton = MakeButton("Export", UIColor.SystemBlue, AskExport);
        var buttons = new UIStackView(new UIView[] { _newButton, _primaryButton, _exportButton })
        {
            TranslatesAutoresizingMaskIntoConstraints = false,
            Axis = UILayoutConstraintAxis.Horizontal,
            Distribution = UIStackViewDistribution.FillEqually,
            Spacing = 12,
        };
        View.AddSubview(buttons);

        var safe = View.SafeAreaLayoutGuide;
        NSLayoutConstraint.ActivateConstraints(new[]
        {
            _sceneView.TopAnchor.ConstraintEqualTo(View.TopAnchor),
            _sceneView.BottomAnchor.ConstraintEqualTo(View.BottomAnchor),
            _sceneView.LeadingAnchor.ConstraintEqualTo(View.LeadingAnchor),
            _sceneView.TrailingAnchor.ConstraintEqualTo(View.TrailingAnchor),

            _status.TopAnchor.ConstraintEqualTo(safe.TopAnchor, 8),
            _status.LeadingAnchor.ConstraintEqualTo(safe.LeadingAnchor, 16),
            _status.TrailingAnchor.ConstraintEqualTo(safe.TrailingAnchor, -16),
            _status.HeightAnchor.ConstraintGreaterThanOrEqualTo(44),

            _detailPicker.LeadingAnchor.ConstraintEqualTo(safe.LeadingAnchor, 16),
            _detailPicker.TrailingAnchor.ConstraintEqualTo(safe.TrailingAnchor, -16),
            _detailPicker.BottomAnchor.ConstraintEqualTo(buttons.TopAnchor, -12),

            buttons.LeadingAnchor.ConstraintEqualTo(safe.LeadingAnchor, 16),
            buttons.TrailingAnchor.ConstraintEqualTo(safe.TrailingAnchor, -16),
            buttons.BottomAnchor.ConstraintEqualTo(safe.BottomAnchor, -12),
            buttons.HeightAnchor.ConstraintEqualTo(56),
        });

        UpdateUi();
    }

    public override void ViewDidAppear(bool animated)
    {
        base.ViewDidAppear(animated);
        UIApplication.SharedApplication.IdleTimerDisabled = true;
        _statusTimer = NSTimer.CreateRepeatingScheduledTimer(0.4, _ => UpdateUi());

        if (!ARWorldTrackingConfiguration.SupportsSceneReconstruction(ARSceneReconstruction.MeshWithClassification))
        {
            ShowMessage("No LiDAR", "Scanning needs an iPhone or iPad with a LiDAR sensor (Pro models from iPhone 12 Pro on).");
        }
    }

    public override void ViewWillDisappear(bool animated)
    {
        base.ViewWillDisappear(animated);
        UIApplication.SharedApplication.IdleTimerDisabled = false;
        _statusTimer?.Invalidate();
        _statusTimer = null;
        if (_state == State.Scanning)
        {
            Pause();
        }
    }

    private ARWorldTrackingConfiguration Configuration()
    {
        if (_configuration != null)
        {
            return _configuration;
        }
        _configuration = new ARWorldTrackingConfiguration
        {
            SceneReconstruction = ARSceneReconstruction.MeshWithClassification,
            // Gravity-aligned, so the exported room stands upright with Y up.
            WorldAlignment = ARWorldAlignment.Gravity,
            PlaneDetection = ARPlaneDetection.None,
        };
        if (ARWorldTrackingConfiguration.SupportsFrameSemantics(ARFrameSemantics.SceneDepth))
        {
            // The LiDAR depth per photo is what keeps a chair from being painted onto the wall behind it.
            _configuration.FrameSemantics = ARFrameSemantics.SceneDepth;
        }
        return _configuration;
    }

    // Photos taken from now on use the new size; the texture size and simplifying follow it at export.
    private void SetDetail(ScanDetail detail)
    {
        _detail = detail;
        _detailPicker.SelectedSegment = (int)detail;
        _sessionDelegate.PhotoSize = ScanDetailSettings.PhotoSize(detail);
        _recording.MaxKeyframes = ScanDetailSettings.MaxKeyframes(detail);
        NSUserDefaults.StandardUserDefaults.SetInt((int)detail, DetailKey);
    }

    private void TogglePrimary()
    {
        switch (_state)
        {
            case State.Idle:
                StartNew();
                break;
            case State.Scanning:
                Pause();
                break;
            case State.Paused:
                // Without a reset ARKit picks up where it left off, once it recognises the room again.
                _sceneView.Session.Run(Configuration(), 0);
                _sessionDelegate.Capturing = true;
                _state = State.Scanning;
                break;
        }
        UpdateUi();
    }

    private void Pause()
    {
        _sessionDelegate.Capturing = false;
        _sceneView.Session.Pause();
        _state = State.Paused;
        UpdateUi();
    }

    private void StartNew()
    {
        _sessionDelegate.Capturing = false;
        _recording.Clear();
        _sessionDelegate.Stream?.SendClear();
        _sessionDelegate.ClearOverlay();
        // A reset starts a new coordinate frame, so later rooms could no longer line up with the building's.
        _building = null;
        _buildingPath = null;
        _notice = null;
        _sceneView.Session.Run(Configuration(), ARSessionRunOptions.ResetTracking | ARSessionRunOptions.RemoveExistingAnchors);
        _sessionDelegate.Capturing = true;
        _state = State.Scanning;
        UpdateUi();
    }

    private void ConfirmNewScan()
    {
        if (_recording.TriangleCount == 0 && _building == null)
        {
            StartNew();
            return;
        }
        var message = _building == null
            ? "The current scan is lost unless you export it first."
            : $"This ends the building \"{_building.Name}\" (its {_building.Rooms.Count} rooms are saved). A room in progress is lost unless you add it first.";
        var alert = UIAlertController.Create("Start a new scan?", message, UIAlertControllerStyle.Alert);
        alert.AddAction(UIAlertAction.Create("Cancel", UIAlertActionStyle.Cancel, null));
        alert.AddAction(UIAlertAction.Create("New Scan", UIAlertActionStyle.Destructive, _ => StartNew()));
        PresentViewController(alert, true, null);
    }

    private void AskExport()
    {
        if (_state == State.Scanning)
        {
            Pause();
        }
        var hasRoom = _recording.TriangleCount > 0;
        var message = _building == null
            ? $"{_detail} detail: a {ScanDetailSettings.TextureSize(_detail)} texture. Export Room saves this scan on its own; "
                + "Add Room starts a building -- carry on into the next room and each one lines up with the last."
            : $"Building \"{_building.Name}\", {_building.Rooms.Count} rooms so far.";
        var alert = UIAlertController.Create("Export", message, UIAlertControllerStyle.Alert);
        if (hasRoom)
        {
            alert.AddTextField(field =>
            {
                field.Placeholder = "Room name";
                field.Text = _building == null ? $"Room {DateTime.Now:yyyy-MM-dd HH.mm}" : $"Room {_building.Rooms.Count + 1}";
                field.ClearButtonMode = UITextFieldViewMode.WhileEditing;
            });
        }
        if (hasRoom && _building == null)
        {
            alert.AddTextField(field =>
            {
                field.Placeholder = "Building name";
                field.Text = $"Building {DateTime.Now:yyyy-MM-dd HH.mm}";
                field.ClearButtonMode = UITextFieldViewMode.WhileEditing;
            });
        }
        string Field(int index, string fallback) =>
            alert.TextFields?.ElementAtOrDefault(index)?.Text?.Trim() is { Length: > 0 } text ? text : fallback;

        if (hasRoom && _building == null)
        {
            alert.AddAction(UIAlertAction.Create("Export Room", UIAlertActionStyle.Default, _ => Export(Field(0, "Room Scan"))));
        }
        if (hasRoom)
        {
            alert.AddAction(UIAlertAction.Create(_building == null ? "Add Room to a Building" : "Add Room", UIAlertActionStyle.Default,
                _ => AddRoom(Field(0, "Room"), Field(1, "Building"))));
        }
        if (_building != null)
        {
            alert.AddAction(UIAlertAction.Create("Finish Building", UIAlertActionStyle.Default, _ => FinishBuilding()));
        }
        alert.AddAction(UIAlertAction.Create("Cancel", UIAlertActionStyle.Cancel, null));
        PresentViewController(alert, true, null);
    }

    private async void AddRoom(string roomName, string buildingName)
    {
        _exporting = true;
        UpdateUi();
        var progressAlert = UIAlertController.Create($"Adding {roomName}", "Painting the texture...", UIAlertControllerStyle.Alert);
        PresentViewController(progressAlert, true, null);

        var building = _building ?? new ScanBuilding(buildingName);
        var path = _buildingPath ?? ScanFiles.PathFor(building.Name);
        var (pieces, keyframes) = _recording.Snapshot();
        var options = ScanDetailSettings.Export(_detail, roomName);
        options.EncodeImage = EncodeJpeg;
        options.Progress = new Progress<float>(p => progressAlert.Message = $"Painting the texture... {p:P0}");
        Exception? failure = null;
        try
        {
            await Task.Run(() =>
            {
                building.AddRoom(pieces, keyframes, options);
                try
                {
                    building.SaveGlb(path);
                }
                catch
                {
                    building.RemoveLastRoom();
                    throw;
                }
            });
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        _exporting = false;
        await DismissViewControllerAsync(true);
        if (failure != null)
        {
            UpdateUi();
            ShowMessage("Adding the room failed", failure.Message);
            return;
        }

        _building = building;
        _buildingPath = path;
        _notice = $"{options.Name} added -- walk on to the next room";
        // The room's photos and mesh are in the building now; let them go. Removing ARKit's anchors without
        // resetting tracking keeps one coordinate frame, so the next room lines up with this one.
        _sessionDelegate.Capturing = false;
        _sessionDelegate.FinishRoom();
        _recording.Clear();
        _sceneView.Session.Run(Configuration(), ARSessionRunOptions.RemoveExistingAnchors);
        _sessionDelegate.Capturing = true;
        _state = State.Scanning;
        UpdateUi();
    }

    private void FinishBuilding()
    {
        if (_buildingPath is not { } path)
        {
            return;
        }
        _building = null;
        _buildingPath = null;
        _notice = null;
        UpdateUi();
        ScanFiles.Share(this, path, _exportButton);
    }

    private async void Export(string name)
    {
        _exporting = true;
        UpdateUi();
        var progressAlert = UIAlertController.Create("Exporting", "Painting the texture...", UIAlertControllerStyle.Alert);
        PresentViewController(progressAlert, true, null);

        var (pieces, keyframes) = _recording.Snapshot();
        var path = ScanFiles.PathFor(name);
        var options = ScanDetailSettings.Export(_detail, name);
        options.EncodeImage = EncodeJpeg;
        options.Progress = new Progress<float>(p => progressAlert.Message = $"Painting the texture... {p:P0}");
        Exception? failure = null;
        try
        {
            await Task.Run(() => ScanGltfExporter.SaveGlb(path, pieces, keyframes, options));
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        _exporting = false;
        UpdateUi();
        await DismissViewControllerAsync(true);
        if (failure != null)
        {
            ShowMessage("Export failed", failure.Message);
            return;
        }
        ScanFiles.Share(this, path, _exportButton);
    }

    // ---------------------------------------------------------------- streaming to the editor

    private async void ToggleStream()
    {
        if (_sessionDelegate.Stream is { Failure: null } current)
        {
            _sessionDelegate.Stream = null;
            current.Dispose();
            UpdateUi();
            return;
        }

        _streamButton.Enabled = false;
        // Long enough for a PC to answer; the first search also waits behind iOS's Local Network question.
        var found = await ScanStreamBrowser.FindAsync(3.0);
        _streamButton.Enabled = true;

        var sheet = UIAlertController.Create("Stream to Mana",
            found.Count == 0
                ? "No editor found. On the PC: Panels > Live Scan > Receive from the phone. On this phone: Settings > Privacy & "
                    + "Security > Local Network > ManaLidar on. Then search again, or enter the PC's address."
                : "Pick the editor to stream to. The scan appears there as you walk.",
            UIAlertControllerStyle.ActionSheet);
        foreach (var editor in found)
        {
            sheet.AddAction(UIAlertAction.Create(editor.Name, UIAlertActionStyle.Default,
                _ => Connect(editor.Name, editor.Host, editor.Port)));
        }
        sheet.AddAction(UIAlertAction.Create("Search again", UIAlertActionStyle.Default, _ => ToggleStream()));
        sheet.AddAction(UIAlertAction.Create("Enter address...", UIAlertActionStyle.Default, _ => AskAddress()));
        sheet.AddAction(UIAlertAction.Create("Cancel", UIAlertActionStyle.Cancel, null));
        if (sheet.PopoverPresentationController is { } popover)
        {
            popover.BarButtonItem = _streamButton;
        }
        PresentViewController(sheet, true, null);
    }

    private void AskAddress()
    {
        var alert = UIAlertController.Create("Editor address", "As the Live Scan panel shows it, e.g. 192.168.1.20:47800.",
            UIAlertControllerStyle.Alert);
        alert.AddTextField(field =>
        {
            field.Text = NSUserDefaults.StandardUserDefaults.StringForKey(LastEditorKey) ?? string.Empty;
            field.KeyboardType = UIKeyboardType.NumbersAndPunctuation;
        });
        alert.AddAction(UIAlertAction.Create("Cancel", UIAlertActionStyle.Cancel, null));
        alert.AddAction(UIAlertAction.Create("Connect", UIAlertActionStyle.Default, _ =>
        {
            var text = alert.TextFields?.FirstOrDefault()?.Text?.Trim() ?? string.Empty;
            var parts = text.Split(':');
            var port = parts.Length > 1 && int.TryParse(parts[1], out var p) ? p : ScanStream.Port;
            if (parts[0].Length > 0)
            {
                NSUserDefaults.StandardUserDefaults.SetString(text, LastEditorKey);
                Connect(parts[0], parts[0], port);
            }
        }));
        PresentViewController(alert, true, null);
    }

    private void Connect(string name, string host, int port)
    {
        var stream = new ScanStreamClient(name, host, port, UIDevice.CurrentDevice.Name, EncodeJpeg);
        // An editor joining part-way gets everything scanned so far, then follows along.
        var (pieces, keyframes) = _recording.Snapshot();
        stream.SendAll(pieces, keyframes);
        _sessionDelegate.Stream?.Dispose();
        _sessionDelegate.Stream = stream;
        UpdateUi();
    }

    // JPEG keeps a 4K photo texture to a few MB; Core's own PNG would be several times that.
    private static byte[] EncodeJpeg(int width, int height, byte[] rgb)
    {
        var rgba = new byte[width * height * 4];
        for (int i = 0, j = 0; i < rgb.Length; i += 3, j += 4)
        {
            rgba[j] = rgb[i];
            rgba[j + 1] = rgb[i + 1];
            rgba[j + 2] = rgb[i + 2];
            rgba[j + 3] = 255;
        }
        using var space = CGColorSpace.CreateDeviceRGB();
        using var context = new CGBitmapContext(rgba, width, height, 8, width * 4, space, CGImageAlphaInfo.NoneSkipLast);
        using var cgImage = context.ToImage() ?? throw new InvalidOperationException("Could not make the texture image.");
        using var image = UIImage.FromImage(cgImage);
        using var jpeg = image.AsJPEG(0.88f) ?? throw new InvalidOperationException("Could not encode the texture.");
        return jpeg.ToArray();
    }

    private void UpdateUi()
    {
        if (_status == null)
        {
            return;
        }
        var triangles = _recording.TriangleCount;
        var lines = new List<string>();
        lines.Add(_state switch
        {
            State.Idle => "Tap Start, then walk the room slowly",
            State.Scanning => "Scanning",
            _ => "Paused",
        });
        if (_building != null)
        {
            lines.Add($"{_building.Name}  ·  {_building.Rooms.Count} {(_building.Rooms.Count == 1 ? "room" : "rooms")}");
        }
        if (triangles > 0)
        {
            lines.Add($"{triangles:N0} triangles  ·  {_recording.KeyframeCount} photos");
        }
        if (_sessionDelegate.Stream is { } stream)
        {
            lines.Add(stream.Failure != null
                ? $"Streaming stopped: {stream.Failure}"
                : stream.Connected
                    ? $"Streaming to {stream.Name}  �  {Megabytes(stream.BytesSent)} sent"
                    : $"Connecting to {stream.Name}...");
        }
        _streamButton.Title = _sessionDelegate.Stream is { Failure: null } ? "Stop" : "Stream";
        var held = _recording.EstimatedBytes + (_building?.EstimatedBytes ?? 0);
        if (held > 0)
        {
            lines.Add($"Scan {Megabytes(held)}  ·  {Megabytes((long)os_proc_available_memory())} left");
        }
        if (_state == State.Scanning && _sessionDelegate.TrackingHint is { } hint)
        {
            lines.Add(hint);
        }
        else if (_notice != null && _state == State.Scanning && triangles < 2000)
        {
            lines.Add(_notice);
        }
        _status.Text = string.Join("\n", lines);

        _primaryButton.SetTitle(_state switch { State.Idle => "Start", State.Scanning => "Pause", _ => "Resume" },
            UIControlState.Normal);
        _primaryButton.BackgroundColor = _state == State.Scanning ? UIColor.SystemOrange : UIColor.SystemGreen;
        _exportButton.Enabled = (triangles > 0 || _building != null) && !_exporting;
        _exportButton.Alpha = _exportButton.Enabled ? 1f : 0.45f;
        _newButton.Enabled = !_exporting;
        _primaryButton.Enabled = !_exporting;
    }

    private static string Megabytes(long bytes) => bytes >= 1L << 30
        ? $"{bytes / (double)(1L << 30):0.0} GB"
        : $"{bytes / (double)(1L << 20):0} MB";

    private static UIButton MakeButton(string title, UIColor color, Action tapped)
    {
        var button = UIButton.FromType(UIButtonType.System);
        button.SetTitle(title, UIControlState.Normal);
        button.SetTitleColor(UIColor.White, UIControlState.Normal);
        if (button.TitleLabel is { } label && UIFont.SystemFontOfSize(18, UIFontWeight.Semibold) is { } font)
        {
            label.Font = font;
        }
        button.BackgroundColor = color;
        button.Layer.CornerRadius = 14;
        button.TouchUpInside += (_, _) => tapped();
        return button;
    }

    private void ShowMessage(string title, string message)
    {
        var alert = UIAlertController.Create(title, message, UIAlertControllerStyle.Alert);
        alert.AddAction(UIAlertAction.Create("OK", UIAlertActionStyle.Default, null));
        PresentViewController(alert, true, null);
    }
}
