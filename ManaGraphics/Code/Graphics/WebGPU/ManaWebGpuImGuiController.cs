using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using ImGuiNET;
using Silk.NET.Input;
using Silk.NET.WebGPU;
using Silk.NET.Windowing;
using ManaCore.Graphics;
using ManaGraphics.Code.Diagnostics;
using Buffer = Silk.NET.WebGPU.Buffer;
using SilkKey = Silk.NET.Input.Key;

namespace ManaGraphics.Code.Graphics
{
    public unsafe class ManaWebGpuImGuiController : IManaImGuiController
    {
        private readonly WebGPU _api;
        private readonly Device* _device;
        private readonly Queue* _queue;
        private readonly TextureFormat _surfaceFormat;
        private readonly IWindow _window;
        private readonly IInputContext _input;

        private ShaderModule* _shaderModule;
        private RenderPipeline* _pipeline;
        private BindGroupLayout* _bindGroupLayout;
        private Buffer* _uniformBuffer;
        private BindGroup* _fontBindGroup;

        private Texture* _fontTexture;
        private TextureView* _fontTextureView;
        private Sampler* _fontSampler;

        private Buffer* _vertexBuffer;
        private ulong _vertexBufferSize = 0;
        private Buffer* _indexBuffer;
        private ulong _indexBufferSize = 0;
        private readonly ManaImGuiClipboardBridge _clipboardBridge;
        private bool _frameFinalized;

        public ManaWebGpuImGuiController(WebGPU api, Device* device, Queue* queue, TextureFormat surfaceFormat, IWindow window, IInputContext input)
        {
            _api = api;
            _device = device;
            _queue = queue;
            _surfaceFormat = surfaceFormat;
            _window = window;
            _input = input;

            InitImGui();
            _clipboardBridge = new ManaImGuiClipboardBridge(_input.Keyboards.FirstOrDefault());
            SetupPipeline();
            RecreateFontDeviceTexture();
            SetupInputCallbacks();
        }

        private void InitImGui()
        {
            ImGui.CreateContext();
            var io = ImGui.GetIO();
            var iniPath = ManaImGuiSettingsStorage.IniFilePath;

            // IniFilename left null on purpose: setting it makes ImGui auto-save to that ONE fixed path
            // for the life of the context, which can't follow ManaImGuiSettingsStorage's now per-UI-scale
            // ini path. Save and load are both already driven explicitly from that property (see
            // SaveIniSettings and the load below), so the native filename was a redundant second
            // mechanism -- dropping it leaves exactly one, and it's the one that tracks the scale.
            io.NativePtr->IniFilename = null;
            io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset |
                ImGuiBackendFlags.HasMouseCursors;
            io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
            io.ConfigFlags &= ~ImGuiConfigFlags.NoMouseCursorChange;
            io.ConfigWindowsResizeFromEdges = true;
            io.DisplaySize = new Vector2(_window.Size.X, _window.Size.Y);
            io.DisplayFramebufferScale = new Vector2(
                _window.Size.X > 0 ? (float)_window.FramebufferSize.X / _window.Size.X : 1f,
                _window.Size.Y > 0 ? (float)_window.FramebufferSize.Y / _window.Size.Y : 1f);
            if (File.Exists(iniPath))
            {
                ImGui.LoadIniSettingsFromDisk(iniPath);
            }
        }

        private void SetupPipeline()
        {
            // Shader Source in WGSL
            string shaderSource = @"
struct VertexInput {
    @location(0) position : vec2<f32>,
    @location(1) uv : vec2<f32>,
    @location(2) color : vec4<f32>,
};

struct VertexOutput {
    @builtin(position) clip_position : vec4<f32>,
    @location(0) uv : vec2<f32>,
    @location(1) color : vec4<f32>,
};

struct Uniforms {
    proj_matrix : mat4x4<f32>,
};

@group(0) @binding(0) var<uniform> uniforms : Uniforms;
@group(0) @binding(1) var font_sampler : sampler;
@group(0) @binding(2) var font_texture : texture_2d<f32>;

fn srgb_to_linear_channel(value : f32) -> f32 {
    if (value <= 0.04045) {
        return value / 12.92;
    }

    return pow((value + 0.055) / 1.055, 2.4);
}

fn srgb_to_linear(value : vec3<f32>) -> vec3<f32> {
    return vec3<f32>(
        srgb_to_linear_channel(value.r),
        srgb_to_linear_channel(value.g),
        srgb_to_linear_channel(value.b)
    );
}

fn thicken_font_coverage(value : f32) -> f32 {
    return pow(clamp(value, 0.0, 1.0), 0.82);
}

@vertex
fn vs_main(input : VertexInput) -> VertexOutput {
    var output : VertexOutput;
    output.clip_position = uniforms.proj_matrix * vec4<f32>(input.position, 0.0, 1.0);
    output.uv = input.uv;
    output.color = input.color;
    return output;
}

@fragment
fn fs_main(input : VertexOutput) -> @location(0) vec4<f32> {
    let font_sample = textureSample(font_texture, font_sampler, input.uv);
    let linear_color = vec4<f32>(srgb_to_linear(input.color.rgb), input.color.a);
    let coverage = thicken_font_coverage(font_sample.a);
    return vec4<f32>(linear_color.rgb * font_sample.rgb, linear_color.a * coverage);
}
";

            byte[] shaderSourceBytes = Encoding.UTF8.GetBytes(shaderSource);
            fixed (byte* pCode = shaderSourceBytes)
            {
                var wgslDescriptor = new ShaderModuleWGSLDescriptor
                {
                    Chain = new ChainedStruct
                    {
                        SType = SType.ShaderModuleWgslDescriptor
                    },
                    Code = pCode
                };

                var shaderDescriptor = new ShaderModuleDescriptor
                {
                    NextInChain = &wgslDescriptor.Chain
                };

                _shaderModule = _api.DeviceCreateShaderModule(_device, &shaderDescriptor);
            }

            // Create Bind Group Layout
            var bglEntries = new BindGroupLayoutEntry[3];
            bglEntries[0] = new BindGroupLayoutEntry
            {
                Binding = 0,
                Visibility = ShaderStage.Vertex,
                Buffer = new BufferBindingLayout
                {
                    Type = BufferBindingType.Uniform
                }
            };
            bglEntries[1] = new BindGroupLayoutEntry
            {
                Binding = 1,
                Visibility = ShaderStage.Fragment,
                Sampler = new SamplerBindingLayout
                {
                    Type = SamplerBindingType.Filtering
                }
            };
            bglEntries[2] = new BindGroupLayoutEntry
            {
                Binding = 2,
                Visibility = ShaderStage.Fragment,
                Texture = new TextureBindingLayout
                {
                    SampleType = TextureSampleType.Float,
                    ViewDimension = TextureViewDimension.Dimension2D,
                    Multisampled = false
                }
            };

            fixed (BindGroupLayoutEntry* pEntries = bglEntries)
            {
                var bglDescriptor = new BindGroupLayoutDescriptor
                {
                    EntryCount = 3,
                    Entries = pEntries
                };
                _bindGroupLayout = _api.DeviceCreateBindGroupLayout(_device, &bglDescriptor);
            }

            // Create Uniform Buffer
            var bufferDesc = new BufferDescriptor
            {
                Usage = BufferUsage.Uniform | BufferUsage.CopyDst,
                Size = 64
            };
            _uniformBuffer = _api.DeviceCreateBuffer(_device, &bufferDesc);

            // Create Pipeline Layout
            var bgls = stackalloc BindGroupLayout*[1];
            bgls[0] = _bindGroupLayout;
            var pipelineLayoutDesc = new PipelineLayoutDescriptor
            {
                BindGroupLayoutCount = 1,
                BindGroupLayouts = bgls
            };
            var pipelineLayout = _api.DeviceCreatePipelineLayout(_device, &pipelineLayoutDesc);

            // Setup Vertex Formats
            var vertexAttributes = new VertexAttribute[3];
            vertexAttributes[0] = new VertexAttribute
            {
                Format = VertexFormat.Float32x2,
                Offset = 0,
                ShaderLocation = 0
            };
            vertexAttributes[1] = new VertexAttribute
            {
                Format = VertexFormat.Float32x2,
                Offset = 8,
                ShaderLocation = 1
            };
            vertexAttributes[2] = new VertexAttribute
            {
                Format = VertexFormat.Unorm8x4,
                Offset = 16,
                ShaderLocation = 2
            };

            var vertexBufferLayout = new VertexBufferLayout();
            fixed (VertexAttribute* pAttributes = vertexAttributes)
            {
                vertexBufferLayout = new VertexBufferLayout
                {
                    ArrayStride = 20,
                    StepMode = VertexStepMode.Vertex,
                    AttributeCount = 3,
                    Attributes = pAttributes
                };
            }

            // Color Target and Blending
            var blendState = new BlendState
            {
                Color = new BlendComponent
                {
                    SrcFactor = BlendFactor.SrcAlpha,
                    DstFactor = BlendFactor.OneMinusSrcAlpha,
                    Operation = BlendOperation.Add
                },
                Alpha = new BlendComponent
                {
                    SrcFactor = BlendFactor.One,
                    DstFactor = BlendFactor.OneMinusSrcAlpha,
                    Operation = BlendOperation.Add
                }
            };

            var colorTarget = new ColorTargetState
            {
                Format = _surfaceFormat,
                Blend = &blendState,
                WriteMask = ColorWriteMask.All
            };

            var depthStencilState = new DepthStencilState
            {
                Format = TextureFormat.Depth24Plus,
                DepthWriteEnabled = false,
                DepthCompare = CompareFunction.Always,
                StencilFront = new StencilFaceState { Compare = CompareFunction.Always },
                StencilBack = new StencilFaceState { Compare = CompareFunction.Always }
            };

            var fragmentState = new FragmentState
            {
                Module = _shaderModule,
                EntryPoint = (byte*)SilksEntry("fs_main"),
                TargetCount = 1,
                Targets = &colorTarget
            };

            var pipelineDesc = new RenderPipelineDescriptor
            {
                Layout = pipelineLayout,
                Vertex = new VertexState
                {
                    Module = _shaderModule,
                    EntryPoint = (byte*)SilksEntry("vs_main"),
                    BufferCount = 1,
                    Buffers = &vertexBufferLayout
                },
                Primitive = new PrimitiveState
                {
                    Topology = PrimitiveTopology.TriangleList,
                    StripIndexFormat = IndexFormat.Undefined,
                    FrontFace = FrontFace.Ccw,
                    CullMode = CullMode.None
                },
                Multisample = new MultisampleState
                {
                    Count = 1,
                    Mask = uint.MaxValue,
                    AlphaToCoverageEnabled = false
                },
                Fragment = &fragmentState,
                DepthStencil = null
            };

            _pipeline = _api.DeviceCreateRenderPipeline(_device, &pipelineDesc);
            _api.PipelineLayoutRelease(pipelineLayout);

            // Diagnostics
            ManaConsole.WriteDiagnostic($"[WebGPU ImGui Debug] ShaderModule: {(nint)_shaderModule:X}");
            ManaConsole.WriteDiagnostic($"[WebGPU ImGui Debug] BindGroupLayout: {(nint)_bindGroupLayout:X}");
            ManaConsole.WriteDiagnostic($"[WebGPU ImGui Debug] UniformBuffer: {(nint)_uniformBuffer:X}");
            ManaConsole.WriteDiagnostic($"[WebGPU ImGui Debug] Pipeline: {(nint)_pipeline:X}");
        }

        public void RecreateFontDeviceTexture()
        {
            var io = ImGui.GetIO();
            io.Fonts.GetTexDataAsRGBA32(out IntPtr pixels, out int width, out int height, out int bytesPerPixel);

            // Clean old resources
            ReleaseFontResources();

            // Create WebGPU Texture
            var textureDesc = new TextureDescriptor
            {
                Usage = TextureUsage.TextureBinding | TextureUsage.CopyDst,
                Dimension = TextureDimension.Dimension2D,
                Size = new Extent3D { Width = (uint)width, Height = (uint)height, DepthOrArrayLayers = 1 },
                Format = TextureFormat.Rgba8Unorm,
                MipLevelCount = 1,
                SampleCount = 1
            };
            _fontTexture = _api.DeviceCreateTexture(_device, &textureDesc);

            // Copy pixels
            var destination = new ImageCopyTexture
            {
                Texture = _fontTexture,
                MipLevel = 0,
                Origin = new Origin3D { X = 0, Y = 0, Z = 0 },
                Aspect = TextureAspect.All
            };
            var layout = new TextureDataLayout
            {
                Offset = 0,
                BytesPerRow = (uint)(width * bytesPerPixel),
                RowsPerImage = (uint)height
            };
            var size = new Extent3D
            {
                Width = (uint)width,
                Height = (uint)height,
                DepthOrArrayLayers = 1
            };
            _api.QueueWriteTexture(_queue, &destination, (void*)pixels, (nuint)(width * height * bytesPerPixel), &layout, &size);

            // View and Sampler
            var viewDesc = new TextureViewDescriptor
            {
                Format = TextureFormat.Rgba8Unorm,
                Dimension = TextureViewDimension.Dimension2D,
                MipLevelCount = 1,
                ArrayLayerCount = 1,
                Aspect = TextureAspect.All
            };
            _fontTextureView = _api.TextureCreateView(_fontTexture, &viewDesc);

            var samplerDesc = new SamplerDescriptor
            {
                AddressModeU = AddressMode.Repeat,
                AddressModeV = AddressMode.Repeat,
                AddressModeW = AddressMode.Repeat,
                MagFilter = FilterMode.Linear,
                MinFilter = FilterMode.Linear,
                MipmapFilter = MipmapFilterMode.Linear,
                LodMinClamp = 0.0f,
                LodMaxClamp = 32.0f,
                Compare = CompareFunction.Undefined,
                MaxAnisotropy = 1
            };
            _fontSampler = _api.DeviceCreateSampler(_device, &samplerDesc);

            // Create Bind Group
            var bindGroupEntries = new BindGroupEntry[3];
            bindGroupEntries[0] = new BindGroupEntry
            {
                Binding = 0,
                Buffer = _uniformBuffer,
                Offset = 0,
                Size = 64
            };
            bindGroupEntries[1] = new BindGroupEntry
            {
                Binding = 1,
                Sampler = _fontSampler
            };
            bindGroupEntries[2] = new BindGroupEntry
            {
                Binding = 2,
                TextureView = _fontTextureView
            };

            fixed (BindGroupEntry* pEntries = bindGroupEntries)
            {
                var bgDescriptor = new BindGroupDescriptor
                {
                    Layout = _bindGroupLayout,
                    EntryCount = 3,
                    Entries = pEntries
                };
                _fontBindGroup = _api.DeviceCreateBindGroup(_device, &bgDescriptor);
            }

            // Diagnostics
            ManaConsole.WriteDiagnostic($"[WebGPU ImGui Debug] FontTexture: {(nint)_fontTexture:X}");
            ManaConsole.WriteDiagnostic($"[WebGPU ImGui Debug] FontTextureView: {(nint)_fontTextureView:X}");
            ManaConsole.WriteDiagnostic($"[WebGPU ImGui Debug] FontSampler: {(nint)_fontSampler:X}");
            ManaConsole.WriteDiagnostic($"[WebGPU ImGui Debug] FontBindGroup: {(nint)_fontBindGroup:X}");
        }

        private void SetupInputCallbacks()
        {
            var keyboard = _input.Keyboards.FirstOrDefault();
            if (keyboard != null)
            {
                keyboard.KeyDown += OnKeyDown;
                keyboard.KeyUp += OnKeyUp;
                keyboard.KeyChar += OnKeyChar;
            }

            var mouse = _input.Mice.FirstOrDefault();
            if (mouse != null)
            {
                mouse.Scroll += OnMouseScroll;
            }
        }

        /// <inheritdoc />
        public Func<bool>? IsPointerOwnedByOverlay { get; set; }

        /// <inheritdoc />
        public Func<bool>? IsKeyboardOwnedByOverlay { get; set; }

        private bool PointerOwnedByOverlay => IsPointerOwnedByOverlay?.Invoke() ?? false;

        private bool KeyboardOwnedByOverlay => IsKeyboardOwnedByOverlay?.Invoke() ?? false;

        private void OnKeyDown(IKeyboard keyboard, Key key, int keyCode)
        {
            // Key up is deliberately NOT suppressed. Swallowing a release while letting the press through
            // leaves ImGui holding a key down forever, which is worse than the double input.
            if (KeyboardOwnedByOverlay)
            {
                return;
            }

            var io = ImGui.GetIO();
            var imguiKey = MapKey(key);
            if (imguiKey != ImGuiKey.None)
            {
                io.AddKeyEvent(imguiKey, true);
            }

            UpdateModifierKeyEvents(io, keyboard, key, true);
        }

        private void OnKeyUp(IKeyboard keyboard, Key key, int keyCode)
        {
            var io = ImGui.GetIO();
            var imguiKey = MapKey(key);
            if (imguiKey != ImGuiKey.None)
            {
                io.AddKeyEvent(imguiKey, false);
            }

            UpdateModifierKeyEvents(io, keyboard, key, false);
        }

        private void OnKeyChar(IKeyboard keyboard, char character)
        {
            if (KeyboardOwnedByOverlay)
            {
                return;
            }

            var io = ImGui.GetIO();
            io.AddInputCharacter(character);
        }

        private void OnMouseScroll(IMouse mouse, ScrollWheel scroll)
        {
            if (PointerOwnedByOverlay)
            {
                return;
            }

            var io = ImGui.GetIO();
            io.AddMouseWheelEvent(scroll.X, scroll.Y);
        }

        public void Update(float deltaTime)
        {
            var io = ImGui.GetIO();
            io.DisplaySize = new Vector2(_window.Size.X, _window.Size.Y);
            io.DisplayFramebufferScale = new Vector2(
                _window.Size.X > 0 ? (float)_window.FramebufferSize.X / _window.Size.X : 1f,
                _window.Size.Y > 0 ? (float)_window.FramebufferSize.Y / _window.Size.Y : 1f);
            io.DeltaTime = deltaTime;

            var mouse = _input.Mice.FirstOrDefault();
            if (mouse != null)
            {
                if (PointerOwnedByOverlay)
                {
                    // ImGui's own convention for "there is no mouse this frame". Parking the position
                    // offscreen rather than only clearing the buttons is what stops widgets underneath the
                    // overlay lighting up on hover, and stops a press that began over the overlay being
                    // read as a click when it is released.
                    io.MousePos = new Vector2(float.MinValue, float.MinValue);
                    io.MouseDown[0] = false;
                    io.MouseDown[1] = false;
                    io.MouseDown[2] = false;
                }
                else
                {
                    io.MousePos = new Vector2(mouse.Position.X, mouse.Position.Y);
                    io.MouseDown[0] = mouse.IsButtonPressed(MouseButton.Left);
                    io.MouseDown[1] = mouse.IsButtonPressed(MouseButton.Right);
                    io.MouseDown[2] = mouse.IsButtonPressed(MouseButton.Middle);
                }
            }

            ImGui.NewFrame();
            _frameFinalized = false;
        }

        private static void UpdateMouseCursor(IMouse mouse, ImGuiIOPtr io)
        {
            try
            {
                var imguiCursor = ImGui.GetMouseCursor();
                if (io.MouseDrawCursor || imguiCursor == ImGuiMouseCursor.None)
                {
                    if (mouse.Cursor.IsSupported(CursorMode.Hidden))
                    {
                        mouse.Cursor.CursorMode = CursorMode.Hidden;
                    }

                    return;
                }

                if (mouse.Cursor.IsSupported(CursorMode.Normal))
                {
                    mouse.Cursor.CursorMode = CursorMode.Normal;
                }

                var requestedCursor = imguiCursor switch
                {
                    ImGuiMouseCursor.TextInput => StandardCursor.IBeam,
                    ImGuiMouseCursor.ResizeAll => StandardCursor.ResizeAll,
                    ImGuiMouseCursor.ResizeNS => StandardCursor.VResize,
                    ImGuiMouseCursor.ResizeEW => StandardCursor.HResize,
                    ImGuiMouseCursor.ResizeNESW => StandardCursor.NeswResize,
                    ImGuiMouseCursor.ResizeNWSE => StandardCursor.NwseResize,
                    ImGuiMouseCursor.Hand => StandardCursor.Hand,
                    ImGuiMouseCursor.NotAllowed => StandardCursor.NotAllowed,
                    _ => StandardCursor.Arrow
                };

                mouse.Cursor.Type = CursorType.Standard;
                try
                {
                    mouse.Cursor.StandardCursor = ResolveCursor(mouse, requestedCursor);
                }
                catch
                {
                    mouse.Cursor.StandardCursor = StandardCursor.Arrow;
                }
            }
            catch
            {
                // Cursor changes are cosmetic and must never interrupt rendering.
            }
        }

        private static StandardCursor ResolveCursor(IMouse mouse, StandardCursor requested)
        {
            if (mouse.Cursor.IsSupported(requested))
            {
                return requested;
            }

            if (requested is StandardCursor.HResize or StandardCursor.VResize or StandardCursor.NeswResize or StandardCursor.NwseResize)
            {
                if (mouse.Cursor.IsSupported(StandardCursor.ResizeAll))
                {
                    return StandardCursor.ResizeAll;
                }

                if (mouse.Cursor.IsSupported(StandardCursor.HResize))
                {
                    return StandardCursor.HResize;
                }

                if (mouse.Cursor.IsSupported(StandardCursor.VResize))
                {
                    return StandardCursor.VResize;
                }
            }

            return StandardCursor.Arrow;
        }

        public void Render()
        {
            FinalizeFrame();
        }

        public void Render(RenderPassEncoder* renderPass)
        {
            FinalizeFrame();
            var drawData = ImGui.GetDrawData();
            if (drawData.CmdListsCount == 0) return;

            // Calculate dynamic buffer size requirements (padded to multiple of 4)
            ulong totalVtxSize = (ulong)(drawData.TotalVtxCount * sizeof(ImDrawVert));
            ulong totalIdxSize = (ulong)(drawData.TotalIdxCount * sizeof(ushort));
            ulong alignedVtxSize = (totalVtxSize + 3) & ~3UL;
            ulong alignedIdxSize = (totalIdxSize + 3) & ~3UL;

            UpdateBuffers(alignedVtxSize, alignedIdxSize);

            // Upload projection matrix
            float L = drawData.DisplayPos.X;
            float R = drawData.DisplayPos.X + drawData.DisplaySize.X;
            float T = drawData.DisplayPos.Y;
            float B = drawData.DisplayPos.Y + drawData.DisplaySize.Y;
            Matrix4x4 orthoProj = new Matrix4x4(
                2.0f / (R - L), 0.0f, 0.0f, 0.0f,
                0.0f, 2.0f / (T - B), 0.0f, 0.0f,
                0.0f, 0.0f, 0.5f, 0.0f,
                (R + L) / (L - R), (T + B) / (B - T), 0.5f, 1.0f
            );
            _api.QueueWriteBuffer(_queue, _uniformBuffer, 0, &orthoProj, (nuint)64);

            // Copy all vertices and indices to temporary contiguous buffers (safe & aligned to 4 bytes)
            void* pTempVtx = NativeMemory.Alloc((nuint)alignedVtxSize);
            void* pTempIdx = NativeMemory.Alloc((nuint)alignedIdxSize);

            try
            {
                byte* pDstVtx = (byte*)pTempVtx;
                byte* pDstIdx = (byte*)pTempIdx;

                for (int i = 0; i < drawData.CmdListsCount; i++)
                {
                    var cmdList = drawData.CmdLists[i];
                    ulong cmdVtxSize = (ulong)(cmdList.VtxBuffer.Size * sizeof(ImDrawVert));
                    ulong cmdIdxSize = (ulong)(cmdList.IdxBuffer.Size * sizeof(ushort));

                    System.Buffer.MemoryCopy((void*)cmdList.VtxBuffer.Data, pDstVtx, cmdVtxSize, cmdVtxSize);
                    System.Buffer.MemoryCopy((void*)cmdList.IdxBuffer.Data, pDstIdx, cmdIdxSize, cmdIdxSize);

                    pDstVtx += cmdVtxSize;
                    pDstIdx += cmdIdxSize;
                }

                _api.QueueWriteBuffer(_queue, _vertexBuffer, 0, pTempVtx, (nuint)alignedVtxSize);
                _api.QueueWriteBuffer(_queue, _indexBuffer, 0, pTempIdx, (nuint)alignedIdxSize);
            }
            finally
            {
                NativeMemory.Free(pTempVtx);
                NativeMemory.Free(pTempIdx);
            }

            // Draw ImGui commands
            _api.RenderPassEncoderSetPipeline(renderPass, _pipeline);
            _api.RenderPassEncoderSetBindGroup(renderPass, 0, _fontBindGroup, 0, null);

            _api.RenderPassEncoderSetVertexBuffer(renderPass, 0, _vertexBuffer, 0, _vertexBufferSize);
            _api.RenderPassEncoderSetIndexBuffer(renderPass, _indexBuffer, IndexFormat.Uint16, 0, _indexBufferSize);

            int globalVtxOffset = 0;
            int globalIdxOffset = 0;

            for (int n = 0; n < drawData.CmdListsCount; n++)
            {
                var cmdList = drawData.CmdLists[n];
                for (int cmdI = 0; cmdI < cmdList.CmdBuffer.Size; cmdI++)
                {
                    var cmd = cmdList.CmdBuffer[cmdI];
                    if (cmd.UserCallback != IntPtr.Zero)
                    {
                        // Custom user rendering callbacks not supported in standard WebGPU wrapper
                    }
                    else
                    {
                        // Scale clip rect from logical points to physical framebuffer pixels (needed for HiDPI/Retina)
                        var fbScale = drawData.FramebufferScale;
                        float clipMinX = Math.Max(0.0f, (cmd.ClipRect.X - drawData.DisplayPos.X) * fbScale.X);
                        float clipMinY = Math.Max(0.0f, (cmd.ClipRect.Y - drawData.DisplayPos.Y) * fbScale.Y);
                        float clipMaxX = Math.Min(drawData.DisplaySize.X * fbScale.X, (cmd.ClipRect.Z - drawData.DisplayPos.X) * fbScale.X);
                        float clipMaxY = Math.Min(drawData.DisplaySize.Y * fbScale.Y, (cmd.ClipRect.W - drawData.DisplayPos.Y) * fbScale.Y);

                        if (clipMaxX > clipMinX && clipMaxY > clipMinY)
                        {
                            _api.RenderPassEncoderSetScissorRect(renderPass, 
                                (uint)clipMinX, (uint)clipMinY, 
                                (uint)(clipMaxX - clipMinX), (uint)(clipMaxY - clipMinY));

                            _api.RenderPassEncoderDrawIndexed(renderPass, 
                                cmd.ElemCount, 1, 
                                (uint)(cmd.IdxOffset + globalIdxOffset), 
                                (int)(cmd.VtxOffset + globalVtxOffset), 0);
                        }
                    }
                }
                globalVtxOffset += cmdList.VtxBuffer.Size;
                globalIdxOffset += cmdList.IdxBuffer.Size;
            }
        }

        private void FinalizeFrame()
        {
            if (_frameFinalized)
            {
                return;
            }

            var io = ImGui.GetIO();
            ImGui.Render();

            var mouse = _input.Mice.FirstOrDefault();
            if (mouse != null)
            {
                // Apply cursor shape after frame finalization (Render/EndFrame), so docking splitters
                // can request resize cursors reliably.
                UpdateMouseCursor(mouse, io);
            }

            SavePendingIniSettings();
            _frameFinalized = true;
        }

        private void UpdateBuffers(ulong vertexNeededSize, ulong indexNeededSize)
        {
            if (vertexNeededSize > _vertexBufferSize)
            {
                if (_vertexBuffer != null) _api.BufferRelease(_vertexBuffer);
                _vertexBufferSize = Math.Max(1024 * 64, vertexNeededSize * 2);
                var bufferDesc = new BufferDescriptor
                {
                    Usage = BufferUsage.Vertex | BufferUsage.CopyDst,
                    Size = _vertexBufferSize
                };
                _vertexBuffer = _api.DeviceCreateBuffer(_device, &bufferDesc);
            }

            if (indexNeededSize > _indexBufferSize)
            {
                if (_indexBuffer != null) _api.BufferRelease(_indexBuffer);
                _indexBufferSize = Math.Max(1024 * 16, indexNeededSize * 2);
                var bufferDesc = new BufferDescriptor
                {
                    Usage = BufferUsage.Index | BufferUsage.CopyDst,
                    Size = _indexBufferSize
                };
                _indexBuffer = _api.DeviceCreateBuffer(_device, &bufferDesc);
            }
        }

        private void ReleaseFontResources()
        {
            if (_fontBindGroup != null)
            {
                _api.BindGroupRelease(_fontBindGroup);
                _fontBindGroup = null;
            }
            if (_fontSampler != null)
            {
                _api.SamplerRelease(_fontSampler);
                _fontSampler = null;
            }
            if (_fontTextureView != null)
            {
                _api.TextureViewRelease(_fontTextureView);
                _fontTextureView = null;
            }
            if (_fontTexture != null)
            {
                _api.TextureRelease(_fontTexture);
                _fontTexture = null;
            }
        }

        public void Dispose()
        {
            SaveIniSettings();
            var keyboard = _input.Keyboards.FirstOrDefault();
            if (keyboard != null)
            {
                keyboard.KeyDown -= OnKeyDown;
                keyboard.KeyUp -= OnKeyUp;
                keyboard.KeyChar -= OnKeyChar;
            }

            var mouse = _input.Mice.FirstOrDefault();
            if (mouse != null)
            {
                mouse.Scroll -= OnMouseScroll;
            }

            ReleaseFontResources();

            if (_vertexBuffer != null) _api.BufferRelease(_vertexBuffer);
            if (_indexBuffer != null) _api.BufferRelease(_indexBuffer);
            if (_uniformBuffer != null) _api.BufferRelease(_uniformBuffer);
            if (_bindGroupLayout != null) _api.BindGroupLayoutRelease(_bindGroupLayout);
            if (_pipeline != null) _api.RenderPipelineRelease(_pipeline);
            if (_shaderModule != null) _api.ShaderModuleRelease(_shaderModule);

            _clipboardBridge.Dispose();
            ImGui.DestroyContext();
        }

        private static void SavePendingIniSettings()
        {
            var io = ImGui.GetIO();
            if (!io.WantSaveIniSettings)
            {
                return;
            }

            SaveIniSettings();
            io.WantSaveIniSettings = false;
        }

        private static void SaveIniSettings()
        {
            ImGui.SaveIniSettingsToDisk(ManaImGuiSettingsStorage.IniFilePath);
        }

        private static byte* SilksEntry(string entry)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(entry + "\0");
            fixed (byte* p = bytes)
            {
                return p;
            }
        }

        private static ImGuiKey MapKey(SilkKey key)
        {
            return key switch
            {
                Key.Tab => ImGuiKey.Tab,
                Key.Left => ImGuiKey.LeftArrow,
                Key.Right => ImGuiKey.RightArrow,
                Key.Up => ImGuiKey.UpArrow,
                Key.Down => ImGuiKey.DownArrow,
                Key.PageUp => ImGuiKey.PageUp,
                Key.PageDown => ImGuiKey.PageDown,
                Key.Home => ImGuiKey.Home,
                Key.End => ImGuiKey.End,
                Key.Insert => ImGuiKey.Insert,
                Key.Delete => ImGuiKey.Delete,
                Key.Backspace => ImGuiKey.Backspace,
                Key.Space => ImGuiKey.Space,
                Key.Enter => ImGuiKey.Enter,
                Key.KeypadEnter => ImGuiKey.KeypadEnter,
                Key.Escape => ImGuiKey.Escape,
                Key.ControlLeft => ImGuiKey.LeftCtrl,
                Key.ControlRight => ImGuiKey.RightCtrl,
                Key.ShiftLeft => ImGuiKey.LeftShift,
                Key.ShiftRight => ImGuiKey.RightShift,
                Key.AltLeft => ImGuiKey.LeftAlt,
                Key.AltRight => ImGuiKey.RightAlt,
                Key.SuperLeft => ImGuiKey.LeftSuper,
                Key.SuperRight => ImGuiKey.RightSuper,
                Key.A => ImGuiKey.A,
                Key.B => ImGuiKey.B,
                Key.C => ImGuiKey.C,
                Key.D => ImGuiKey.D,
                Key.E => ImGuiKey.E,
                Key.F => ImGuiKey.F,
                Key.G => ImGuiKey.G,
                Key.H => ImGuiKey.H,
                Key.I => ImGuiKey.I,
                Key.J => ImGuiKey.J,
                Key.K => ImGuiKey.K,
                Key.L => ImGuiKey.L,
                Key.M => ImGuiKey.M,
                Key.N => ImGuiKey.N,
                Key.O => ImGuiKey.O,
                Key.P => ImGuiKey.P,
                Key.Q => ImGuiKey.Q,
                Key.R => ImGuiKey.R,
                Key.S => ImGuiKey.S,
                Key.T => ImGuiKey.T,
                Key.U => ImGuiKey.U,
                Key.V => ImGuiKey.V,
                Key.W => ImGuiKey.W,
                Key.X => ImGuiKey.X,
                Key.Y => ImGuiKey.Y,
                Key.Z => ImGuiKey.Z,
                Key.Number0 => ImGuiKey._0,
                Key.Number1 => ImGuiKey._1,
                Key.Number2 => ImGuiKey._2,
                Key.Number3 => ImGuiKey._3,
                Key.Number4 => ImGuiKey._4,
                Key.Number5 => ImGuiKey._5,
                Key.Number6 => ImGuiKey._6,
                Key.Number7 => ImGuiKey._7,
                Key.Number8 => ImGuiKey._8,
                Key.Number9 => ImGuiKey._9,
                _ => ImGuiKey.None
            };
        }

        private static void UpdateModifierKeyEvents(ImGuiIOPtr io, IKeyboard keyboard, SilkKey eventKey, bool eventDown)
        {
            io.AddKeyEvent(ImGuiKey.ModCtrl, IsModifierDown(keyboard, Key.ControlLeft, Key.ControlRight, eventKey, eventDown));
            io.AddKeyEvent(ImGuiKey.ModShift, IsModifierDown(keyboard, Key.ShiftLeft, Key.ShiftRight, eventKey, eventDown));
            io.AddKeyEvent(ImGuiKey.ModAlt, IsModifierDown(keyboard, Key.AltLeft, Key.AltRight, eventKey, eventDown));
            io.AddKeyEvent(ImGuiKey.ModSuper, IsModifierDown(keyboard, Key.SuperLeft, Key.SuperRight, eventKey, eventDown));
        }

        private static bool IsModifierDown(
            IKeyboard keyboard,
            SilkKey leftKey,
            SilkKey rightKey,
            SilkKey eventKey,
            bool eventDown)
        {
            if (eventKey == leftKey)
            {
                return eventDown || keyboard.IsKeyPressed(rightKey);
            }

            if (eventKey == rightKey)
            {
                return eventDown || keyboard.IsKeyPressed(leftKey);
            }

            return keyboard.IsKeyPressed(leftKey) || keyboard.IsKeyPressed(rightKey);
        }
    }
}
