using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CoreAnimation;
using Silk.NET.Core.Contexts;
using Silk.NET.WebGPU;

namespace ManaVoxel.iOS;

/// <summary>
/// First light on the phone: wgpu-native through Silk.NET's bindings onto a Metal layer, clearing to a colour that
/// cycles. Proves the library loads, the surface and device come up and frames present -- before the engine does.
/// </summary>
public sealed unsafe class WebGpuCheckViewController : UIViewController
{
    private readonly UILabel _label = new()
    {
        TextColor = UIColor.White,
        Font = UIFont.GetMonospacedSystemFont(13, UIFontWeight.Regular) ?? UIFont.SystemFontOfSize(13),
        Lines = 0,
        TranslatesAutoresizingMaskIntoConstraints = false,
    };

    private WebGPU? _api;
    private Instance* _instance;
    private Surface* _surface;
    private Adapter* _adapter;
    private Device* _device;
    private Queue* _queue;
    private TextureFormat _format;
    private (uint Width, uint Height) _configured;
    private CADisplayLink? _link;
    private double _start;
    private int _frames;
    private string _info = string.Empty;

    // Native callbacks run under full AOT: static [UnmanagedCallersOnly] methods, never delegates, with the
    // controller reached through a GCHandle in the userdata.
    private GCHandle _self;

    public override void LoadView() => View = new MetalView();

    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        View!.AddSubview(_label);
        NSLayoutConstraint.ActivateConstraints(new[]
        {
            _label.LeadingAnchor.ConstraintEqualTo(View.SafeAreaLayoutGuide.LeadingAnchor, 16),
            _label.TrailingAnchor.ConstraintEqualTo(View.SafeAreaLayoutGuide.TrailingAnchor, -16),
            _label.TopAnchor.ConstraintEqualTo(View.SafeAreaLayoutGuide.TopAnchor, 16),
        });
        try
        {
            Start();
        }
        catch (Exception ex)
        {
            _label.Text = $"WebGPU failed:\n{ex}";
        }
    }

    private void Start()
    {
        _self = GCHandle.Alloc(this);
        // The framework is embedded and linked, so it is already loaded; its full path names it for Silk.NET.
        var library = Path.Combine(NSBundle.MainBundle.PrivateFrameworksPath!, "wgpu_native.framework", "wgpu_native");
        _api = new WebGPU(new DefaultNativeContext(library));

        var instanceDescriptor = new InstanceDescriptor();
        _instance = _api.CreateInstance(&instanceDescriptor);
        if (_instance == null)
        {
            throw new InvalidOperationException("No WebGPU instance.");
        }

        var layer = (CAMetalLayer)View!.Layer;
        var metal = new SurfaceDescriptorFromMetalLayer
        {
            Chain = new ChainedStruct { SType = SType.SurfaceDescriptorFromMetalLayer },
            Layer = (void*)layer.Handle,
        };
        var surfaceDescriptor = new SurfaceDescriptor { NextInChain = &metal.Chain };
        _surface = _api.InstanceCreateSurface(_instance, &surfaceDescriptor);
        if (_surface == null)
        {
            throw new InvalidOperationException("No WebGPU surface on the Metal layer.");
        }

        // wgpu-native answers both requests before returning.
        var options = new RequestAdapterOptions { CompatibleSurface = _surface, PowerPreference = PowerPreference.HighPerformance };
        _api.InstanceRequestAdapter(_instance, &options, new PfnRequestAdapterCallback(&OnAdapter), (void*)GCHandle.ToIntPtr(_self));
        if (_adapter == null)
        {
            throw new InvalidOperationException("No WebGPU adapter.");
        }
        AdapterProperties properties;
        _api.AdapterGetProperties(_adapter, &properties);
        _info = $"{Marshal.PtrToStringUTF8((nint)properties.Name)} ({properties.BackendType})";

        var deviceDescriptor = new DeviceDescriptor();
        _api.AdapterRequestDevice(_adapter, &deviceDescriptor, new PfnRequestDeviceCallback(&OnDevice), (void*)GCHandle.ToIntPtr(_self));
        if (_device == null)
        {
            throw new InvalidOperationException("No WebGPU device.");
        }
        _api.DeviceSetUncapturedErrorCallback(_device, new PfnErrorCallback(&OnError), (void*)GCHandle.ToIntPtr(_self));
        _queue = _api.DeviceGetQueue(_device);
        _format = _api.SurfaceGetPreferredFormat(_surface, _adapter);

        _start = CAAnimation.CurrentMediaTime();
        _link = CADisplayLink.Create(Frame);
        _link.AddToRunLoop(NSRunLoop.Main, NSRunLoopMode.Common);
    }

    private void Configure()
    {
        var layer = (CAMetalLayer)View!.Layer;
        var scale = View.Window?.Screen.NativeScale ?? UIScreen.MainScreen.NativeScale;
        var width = (uint)Math.Max(1, Math.Round(View.Bounds.Width * scale));
        var height = (uint)Math.Max(1, Math.Round(View.Bounds.Height * scale));
        if (_configured == (width, height))
        {
            return;
        }
        layer.ContentsScale = scale;
        var configuration = new SurfaceConfiguration
        {
            Device = _device,
            Format = _format,
            Usage = TextureUsage.RenderAttachment,
            Width = width,
            Height = height,
            PresentMode = PresentMode.Fifo,
            AlphaMode = CompositeAlphaMode.Opaque,
        };
        _api!.SurfaceConfigure(_surface, &configuration);
        _configured = (width, height);
    }

    private void Frame()
    {
        if (_api == null || _device == null)
        {
            return;
        }
        Configure();
        SurfaceTexture surfaceTexture;
        _api.SurfaceGetCurrentTexture(_surface, &surfaceTexture);
        if (surfaceTexture.Status != SurfaceGetCurrentTextureStatus.Success || surfaceTexture.Texture == null)
        {
            _configured = default;
            return;
        }

        var t = CAAnimation.CurrentMediaTime() - _start;
        var view = _api.TextureCreateView(surfaceTexture.Texture, null);
        var encoder = _api.DeviceCreateCommandEncoder(_device, null);
        var attachment = new RenderPassColorAttachment
        {
            View = view,
            LoadOp = LoadOp.Clear,
            StoreOp = StoreOp.Store,
            ClearValue = new Color(0.39 + 0.25 * Math.Sin(t), 0.58 + 0.2 * Math.Sin(t * 0.7 + 2), 0.93, 1),
        };
        var pass = new RenderPassDescriptor { ColorAttachmentCount = 1, ColorAttachments = &attachment };
        var encoderPass = _api.CommandEncoderBeginRenderPass(encoder, &pass);
        _api.RenderPassEncoderEnd(encoderPass);
        _api.RenderPassEncoderRelease(encoderPass);
        var commands = _api.CommandEncoderFinish(encoder, null);
        _api.QueueSubmit(_queue, 1, &commands);
        _api.SurfacePresent(_surface);
        _api.CommandBufferRelease(commands);
        _api.CommandEncoderRelease(encoder);
        _api.TextureViewRelease(view);
        _api.TextureRelease(surfaceTexture.Texture);

        if (++_frames % 30 == 1)
        {
            _label.Text = $"Mana on WebGPU, natively\n{_info}\n{_format}  {_configured.Width} x {_configured.Height}\n{_frames} frames, {_frames / Math.Max(t, 0.001):0} fps";
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void OnAdapter(RequestAdapterStatus status, Adapter* adapter, byte* message, void* userdata)
    {
        var self = (WebGpuCheckViewController)GCHandle.FromIntPtr((nint)userdata).Target!;
        if (status == RequestAdapterStatus.Success)
        {
            self._adapter = adapter;
        }
        else
        {
            self._info = $"Adapter: {status} {Marshal.PtrToStringUTF8((nint)message)}";
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void OnDevice(RequestDeviceStatus status, Device* device, byte* message, void* userdata)
    {
        var self = (WebGpuCheckViewController)GCHandle.FromIntPtr((nint)userdata).Target!;
        if (status == RequestDeviceStatus.Success)
        {
            self._device = device;
        }
        else
        {
            self._info = $"Device: {status} {Marshal.PtrToStringUTF8((nint)message)}";
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void OnError(ErrorType type, byte* message, void* userdata)
    {
        Console.WriteLine($"[WebGPU] {type}: {Marshal.PtrToStringUTF8((nint)message)}");
    }

    /// <summary>A view whose layer is a CAMetalLayer, for wgpu to draw into.</summary>
    private sealed class MetalView : UIView
    {
        [Export("layerClass")]
        public static ObjCRuntime.Class GetLayerClass() => new(typeof(CAMetalLayer));
    }
}
