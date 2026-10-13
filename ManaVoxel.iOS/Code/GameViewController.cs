using CoreAnimation;
using Mana.Code.GameModes;
using ManaCore.Graphics;
using ManaGraphics.Code.Graphics;
using Silk.NET.Core.Contexts;
using Silk.NET.Input;
using Silk.NET.WebGPU;
using Silk.NET.Windowing;

namespace ManaVoxel.iOS;

/// <summary>
/// The game, full screen: the engine's WebGPU renderer drawing into a Metal layer (wgpu-native on Metal), with the
/// touch controls over it. Runs the same <see cref="VoxelPlayGameMode"/> as <c>ManaVoxel --play</c>.
/// </summary>
public sealed class GameViewController : UIViewController
{
    // Until the app has a level selector: the one the desktop's --play opens by default.
    private const string LevelPath = "Content/Levels/Sample Course.manalevel";

    private readonly IosInput _input = new();
    private IosWindow? _window;
    private TouchControlsView? _controls;
    private VoxelPlayGameMode? _game;
    private NSObject[] _observers = Array.Empty<NSObject>();

    public override void LoadView() => View = new MetalView();

    public override bool PrefersStatusBarHidden() => true;
    public override bool PrefersHomeIndicatorAutoHidden => true;
    // The game's controls sit near the edges: a first swipe there shows the home bar rather than leaving the app.
    public override UIRectEdge PreferredScreenEdgesDeferringSystemGestures => UIRectEdge.All;

    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        View!.BackgroundColor = UIColor.Black;
        _window = new IosWindow(View, _input);
        _controls = new TouchControlsView(_input) { Frame = View.Bounds, AutoresizingMask = UIViewAutoresizing.FlexibleDimensions };
        View.AddSubview(_controls);

        _observers = new[]
        {
            NSNotificationCenter.DefaultCenter.AddObserver(UIApplication.WillResignActiveNotification, _ =>
            {
                _controls.Reset();
                _window.SetPaused(true);
            }),
            NSNotificationCenter.DefaultCenter.AddObserver(UIApplication.DidBecomeActiveNotification, _ => _window.SetPaused(false)),
        };
    }

    public override void ViewDidLayoutSubviews()
    {
        base.ViewDidLayoutSubviews();
        _window?.RefreshSize();
    }

    public override void ViewDidAppear(bool animated)
    {
        base.ViewDidAppear(animated);
        if (_game != null || _window == null)
        {
            return;
        }
        // Started once the view has its real size: the surface is configured from it.
        _window.RefreshSize();
        try
        {
            ManaWebGpuNativeHost.Current = new IosWebGpuHost(_window, _input, (CAMetalLayer)View!.Layer);
            _game = new VoxelPlayGameMode(LevelPath, ManaGraphicsBackendKind.WebGpu);
            _game.RunWebGpu(null);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ManaVoxel.iOS] The game failed to start: {ex}");
            ShowFailure(ex);
        }
    }

    private void ShowFailure(Exception ex)
    {
        var label = new UILabel
        {
            Text = $"Mana could not start:\n\n{ex}",
            TextColor = UIColor.White,
            Lines = 0,
            Frame = View!.Bounds.Inset(24, 48),
        };
        if (UIFont.GetMonospacedSystemFont(11, UIFontWeight.Regular) is { } mono)
        {
            label.Font = mono;
        }
        View.AddSubview(label);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var observer in _observers)
            {
                NSNotificationCenter.DefaultCenter.RemoveObserver(observer);
            }
        }
        base.Dispose(disposing);
    }

    /// <summary>A view whose layer is a CAMetalLayer, for wgpu to draw into.</summary>
    private sealed class MetalView : UIView
    {
        [Export("layerClass")]
        public static ObjCRuntime.Class GetLayerClass() => new(typeof(CAMetalLayer));
    }
}

/// <summary>wgpu-native from the app's own framework, presenting into the game view's Metal layer.</summary>
public sealed unsafe class IosWebGpuHost : IManaWebGpuNativeHost
{
    private readonly CAMetalLayer _layer;

    public IosWebGpuHost(IosWindow window, IosInput input, CAMetalLayer layer)
    {
        Window = window;
        Input = input;
        _layer = layer;
    }

    public IWindow Window { get; }
    public IInputContext Input { get; }

    // The framework is embedded and linked, so it is already loaded; its full path names it for Silk.NET.
    public WebGPU CreateApi() => new(new DefaultNativeContext(
        Path.Combine(NSBundle.MainBundle.PrivateFrameworksPath!, "wgpu_native.framework", "wgpu_native")));

    public Surface* CreateSurface(WebGPU api, Instance* instance)
    {
        var metal = new SurfaceDescriptorFromMetalLayer
        {
            Chain = new ChainedStruct { SType = SType.SurfaceDescriptorFromMetalLayer },
            Layer = (void*)_layer.Handle,
        };
        var descriptor = new SurfaceDescriptor { NextInChain = &metal.Chain };
        return api.InstanceCreateSurface(instance, &descriptor);
    }
}
