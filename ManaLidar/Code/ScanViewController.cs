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

    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        Title = "ManaLidar";
        View!.BackgroundColor = UIColor.Black;
        NavigationItem.RightBarButtonItem = new UIBarButtonItem("Scans", UIBarButtonItemStyle.Plain,
            (_, _) => NavigationController?.PushViewController(new ScansViewController(), true));

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

    // Photos taken from now on use the new size; the mesh is split to the new edge length at export.
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
        _sessionDelegate.ClearOverlay();
        _sceneView.Session.Run(Configuration(), ARSessionRunOptions.ResetTracking | ARSessionRunOptions.RemoveExistingAnchors);
        _sessionDelegate.Capturing = true;
        _state = State.Scanning;
        UpdateUi();
    }

    private void ConfirmNewScan()
    {
        if (_recording.TriangleCount == 0)
        {
            StartNew();
            return;
        }
        var alert = UIAlertController.Create("Start a new scan?", "The current scan is lost unless you export it first.",
            UIAlertControllerStyle.Alert);
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
        var alert = UIAlertController.Create("Export scan",
            $"{_detail} detail: a {ScanDetailSettings.TextureSize(_detail)} texture. Saved as a GLB in ManaLidar's folder in Files.",
            UIAlertControllerStyle.Alert);
        alert.AddTextField(field =>
        {
            field.Text = $"Room {DateTime.Now:yyyy-MM-dd HH.mm}";
            field.ClearButtonMode = UITextFieldViewMode.WhileEditing;
        });
        alert.AddAction(UIAlertAction.Create("Cancel", UIAlertActionStyle.Cancel, null));
        alert.AddAction(UIAlertAction.Create("Export", UIAlertActionStyle.Default, _ =>
        {
            var name = alert.TextFields?.FirstOrDefault()?.Text;
            Export(string.IsNullOrWhiteSpace(name) ? "Room Scan" : name.Trim());
        }));
        PresentViewController(alert, true, null);
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
        if (triangles > 0)
        {
            lines.Add($"{triangles:N0} triangles  ·  {_recording.KeyframeCount} photos");
        }
        if (_state == State.Scanning && _sessionDelegate.TrackingHint is { } hint)
        {
            lines.Add(hint);
        }
        _status.Text = string.Join("\n", lines);

        _primaryButton.SetTitle(_state switch { State.Idle => "Start", State.Scanning => "Pause", _ => "Resume" },
            UIControlState.Normal);
        _primaryButton.BackgroundColor = _state == State.Scanning ? UIColor.SystemOrange : UIColor.SystemGreen;
        _exportButton.Enabled = triangles > 0 && !_exporting;
        _exportButton.Alpha = _exportButton.Enabled ? 1f : 0.45f;
        _newButton.Enabled = !_exporting;
        _primaryButton.Enabled = !_exporting;
    }

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
