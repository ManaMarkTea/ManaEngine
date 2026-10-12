using Android;
using Android.App;
using Android.Content.PM;
using Android.Opengl;
using Android.OS;
using Android.Views;
using Android.Widget;
using Google.AR.Core;

[assembly: UsesPermission(Manifest.Permission.Camera)]
[assembly: UsesFeature("android.hardware.camera.ar")]
[assembly: MetaData("com.google.ar.core", Value = "required")]

namespace ManaLidar.Droid;

/// <summary>Camera view with Start/Export: ARCore tracks, <see cref="ArRenderer"/> keeps keyframes, export writes a GLB.</summary>
[Activity(Label = "ManaLidar", MainLauncher = true, Exported = true, ScreenOrientation = ScreenOrientation.Portrait,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize)]
public sealed class MainActivity : Activity
{
    private const int CameraRequest = 1;

    private readonly ScanRecording _recording = new();
    private GLSurfaceView _surface = null!;
    private ArRenderer _renderer = null!;
    private TextView _status = null!;
    private Button _start = null!;
    private Button _export = null!;
    private Session? _session;
    private bool _installRequested;
    private bool _exporting;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        _surface = new GLSurfaceView(this);
        _surface.SetEGLContextClientVersion(2);
        _surface.SetEGLConfigChooser(8, 8, 8, 8, 16, 0);
        _renderer = new ArRenderer(this, _recording);
        _surface.SetRenderer(_renderer);
        _surface.RenderMode = Rendermode.Continuously;

        _status = new TextView(this) { Text = "Starting..." };
        _status.SetTextColor(global::Android.Graphics.Color.White);
        _status.SetBackgroundColor(global::Android.Graphics.Color.Argb(140, 0, 0, 0));
        _status.SetPadding(24, 16, 24, 16);

        _start = new Button(this) { Text = "Start" };
        _start.Click += (_, _) =>
        {
            _renderer.Capturing = !_renderer.Capturing;
            _start.Text = _renderer.Capturing ? "Pause" : "Resume";
        };
        var clear = new Button(this) { Text = "New" };
        clear.Click += (_, _) =>
        {
            _renderer.Capturing = false;
            _recording.Clear();
            _start.Text = "Start";
        };
        _export = new Button(this) { Text = "Export" };
        _export.Click += (_, _) => ExportScan();

        var buttons = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        foreach (var button in new[] { _start, clear, _export })
        {
            buttons.AddView(button, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
        }
        var bottom = new LinearLayout(this) { Orientation = Orientation.Vertical };
        bottom.AddView(_status);
        bottom.AddView(buttons);

        var root = new FrameLayout(this);
        root.AddView(_surface);
        root.AddView(bottom, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent, GravityFlags.Bottom));
        SetContentView(root);

        // Counts and hints a few times a second; the renderer thread only writes them.
        var handler = new Handler(Looper.MainLooper!);
        void Tick()
        {
            _status.Text = $"{_renderer.Hint ?? "Tracking"}\n{_recording.KeyframeCount} photos, {_recording.PieceCount} pieces, {_recording.TriangleCount:N0} triangles";
            handler.PostDelayed(Tick, 250);
        }
        handler.Post(Tick);
    }

    protected override void OnResume()
    {
        base.OnResume();
        if (CheckSelfPermission(Manifest.Permission.Camera) != Permission.Granted)
        {
            RequestPermissions(new[] { Manifest.Permission.Camera }, CameraRequest);
            return;
        }
        if (!EnsureSession())
        {
            return;
        }
        _session!.Resume();
        _renderer.Session = _session;
        _surface.OnResume();
    }

    protected override void OnPause()
    {
        base.OnPause();
        _surface.OnPause();
        _session?.Pause();
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        if (grantResults.Length == 0 || grantResults[0] != Permission.Granted)
        {
            _status.Text = "ManaLidar needs the camera.";
        }
    }

    private bool EnsureSession()
    {
        if (_session != null)
        {
            return true;
        }
        try
        {
            if (ArCoreApk.Instance.RequestInstall(this, !_installRequested) == ArCoreApk.InstallStatus.InstallRequested)
            {
                _installRequested = true;
                return false;
            }
            var session = new Session(this);
            var config = new Config(session);
            config.SetDepthMode(session.IsDepthModeSupported(Config.DepthMode.Automatic) ? Config.DepthMode.Automatic : Config.DepthMode.Disabled);
            config.SetUpdateMode(Config.UpdateMode.LatestCameraImage);
            config.SetFocusMode(Config.FocusMode.Auto);
            session.Configure(config);
            _session = session;
            return true;
        }
        catch (Exception e)
        {
            _status.Text = $"ARCore unavailable: {e.Message}";
            return false;
        }
    }

    private void ExportScan()
    {
        if (_exporting)
        {
            return;
        }
        _renderer.Capturing = false;
        _start.Text = "Resume";
        _exporting = true;
        _export.Enabled = false;
        var folder = GetExternalFilesDir("Scans")!.AbsolutePath;
        var path = Path.Combine(folder, $"Scan {DateTime.Now:yyyy-MM-dd HH-mm-ss}.glb");
        Task.Run(() =>
        {
            string message;
            try
            {
                var (pieces, keyframes) = _recording.Snapshot();
                ScanGltfExporter.SaveGlb(path, pieces, keyframes, new ScanExportOptions { Name = Path.GetFileNameWithoutExtension(path) });
                message = $"Saved {path}";
            }
            catch (Exception e)
            {
                message = $"Export failed: {e.Message}";
            }
            RunOnUiThread(() =>
            {
                _exporting = false;
                _export.Enabled = true;
                Toast.MakeText(this, message, ToastLength.Long)!.Show();
            });
        });
    }
}
