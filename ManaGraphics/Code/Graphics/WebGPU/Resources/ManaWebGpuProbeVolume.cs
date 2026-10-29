using System.Numerics;
using System.Runtime.InteropServices;
using ManaCore.Graphics;
using ManaGraphics.Code.Diagnostics;
using Silk.NET.WebGPU;
using WebGpuBuffer = Silk.NET.WebGPU.Buffer;

namespace ManaGraphics.Code.Graphics.Resources
{
    /// <summary>
    /// A level's baked light probes (ManaProbeVolumeSettings): the scenery in the box is voxelized on a background
    /// thread (ManaProbeVoxelizer) whenever it changes, then the probes are baked on the GPU whenever the light does --
    /// for each bounce, light the voxels (probe_volume_light.wgsl), gather each probe's rays through them
    /// (probe_volume_gather.wgsl), and pack the result, buried probes filled from their neighbours
    /// (probe_volume_pack.wgsl). The scene samples three textures of L1 harmonics (include/probe_volume.wgsl).
    /// </summary>
    internal sealed unsafe class ManaWebGpuProbeVolume : ManaWebGpuDeviceObject
    {
        private const int RaysPerProbe = 96;
        private const int ParamStride = 256;
        private const int MaxBounces = 6;
        // A moving sun or flickering lamps rebake no more often than this.
        private const int RebakeFrames = 10;
        // The scenery must hold still this long before it is voxelized: a level streams in over a few frames.
        private const int SettleFrames = 15;

        [StructLayout(LayoutKind.Sequential)]
        private struct Params
        {
            public Vector4 VolumeMin;
            public Vector4 VolumeSize;
            public uint VoxelsX, VoxelsY, VoxelsZ, LightCount;
            public uint ProbesX, ProbesY, ProbesZ, Bounce;
            public Vector4 SunDirection;
            public Vector4 SunColor;
            public Vector4 Sky;
        }

        private sealed class Step
        {
            public ShaderModule* Shader;
            public BindGroupLayout* Layout;
            public PipelineLayout* PipelineLayout;
            public ComputePipeline* Pipeline;
            public BindGroup* Group;
        }

        private readonly Step _light = new();
        private readonly Step _gather = new();
        private readonly Step _pack = new();
        private WebGpuBuffer* _params;
        private WebGpuBuffer* _lights;
        private WebGpuBuffer* _probes;
        private ulong _probesSize;
        private Sampler* _sampler;
        private readonly List<(nint Texture, nint View)> _textures = new();
        private TextureView* _albedo, _normal, _emission, _radiance;
        private TextureView* _gatherEnvironment;

        private ManaProbeVolumeSettings? _baked;
        private ManaProbeVoxels? _voxels;
        private Task<ManaProbeVoxels>? _voxelizing;
        private CancellationTokenSource? _cancel;
        private long _geometry;
        private long _candidate;
        private int _candidateFrames;
        private long _lighting = long.MinValue;
        private int _framesSinceBake = RebakeFrames;

        public ManaWebGpuProbeVolume(ManaWebGpuDevice gpu)
            : base(gpu)
        {
            try
            {
                _params = CreateBuffer((ulong)(ParamStride * MaxBounces), BufferUsage.Uniform | BufferUsage.CopyDst);
                _lights = CreateBuffer((ulong)(ManaRenderScene.MaxLocalLights * 2 * sizeof(Vector4)), BufferUsage.Storage | BufferUsage.CopyDst);
                var samplerDescriptor = new SamplerDescriptor
                {
                    AddressModeU = AddressMode.ClampToEdge,
                    AddressModeV = AddressMode.ClampToEdge,
                    AddressModeW = AddressMode.ClampToEdge,
                    MagFilter = FilterMode.Linear,
                    MinFilter = FilterMode.Linear,
                    MipmapFilter = MipmapFilterMode.Nearest,
                    LodMaxClamp = 32.0f,
                    MaxAnisotropy = 1
                };
                _sampler = Api.DeviceCreateSampler(Device, &samplerDescriptor);
                CreateStep(_light, "probe_volume_light.wgsl", LightLayout());
                CreateStep(_gather, "probe_volume_gather.wgsl", GatherLayout());
                CreateStep(_pack, "probe_volume_pack.wgsl", PackLayout());
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        /// <summary>The harmonics the scene samples; null until a volume has been baked.</summary>
        public TextureView* Red { get; private set; }
        public TextureView* Green { get; private set; }
        public TextureView* Blue { get; private set; }

        /// <summary>For the frame uniform: xyz the low corner, w 1 when the probes hold light.</summary>
        public Vector4 FrameMin { get; private set; }

        /// <summary>For the frame uniform: xyz the probes' extent, w the spacing.</summary>
        public Vector4 FrameExtent { get; private set; }

        public int PipelineCount => 3;

        /// <summary>
        /// Keeps the bake in step with the frame's scene; encodes a bake when one is due. True when the harmonics
        /// textures were made anew, so the frame group must bind them.
        /// </summary>
        public bool Update(CommandEncoder* encoder, ManaRenderScene scene, TextureView* environment)
        {
            var settings = scene.ProbeVolume;
            if (settings == null)
            {
                FrameMin = new Vector4(FrameMin.X, FrameMin.Y, FrameMin.Z, 0.0f);
                return false;
            }

            var texturesChanged = false;
            var geometry = GeometrySignature(settings, scene.Meshes);
            if (geometry != _geometry)
            {
                if (geometry != _candidate)
                {
                    _candidate = geometry;
                    _candidateFrames = 0;
                }
                else if (++_candidateFrames >= SettleFrames && _voxelizing == null)
                {
                    StartVoxelizing(settings, scene.Meshes, geometry);
                }
            }

            if (_voxelizing is { IsCompleted: true } done)
            {
                _voxelizing = null;
                if (done.IsCompletedSuccessfully)
                {
                    _voxels = done.Result;
                    texturesChanged = Upload(settings, _voxels);
                    _lighting = long.MinValue;
                }
                else if (done.Exception != null)
                {
                    ManaConsole.WriteDiagnostic($"Probe volume: voxelizing failed: {done.Exception.GetBaseException().Message}");
                }
            }

            if (_baked == null || _voxels == null || environment == null)
            {
                return texturesChanged;
            }

            var lighting = LightingSignature(scene, environment);
            _framesSinceBake = Math.Min(_framesSinceBake + 1, RebakeFrames);
            if (lighting != _lighting && _framesSinceBake >= RebakeFrames)
            {
                Bake(encoder, scene, environment);
                _lighting = lighting;
                _framesSinceBake = 0;
            }
            return texturesChanged;
        }

        private void StartVoxelizing(ManaProbeVolumeSettings settings, IReadOnlyList<ManaRenderMeshInstance> meshes, long geometry)
        {
            _cancel?.Cancel();
            _cancel = new CancellationTokenSource();
            var token = _cancel.Token;
            var list = meshes.ToArray();
            _geometry = geometry;
            var started = System.Diagnostics.Stopwatch.StartNew();
            _voxelizing = Task.Run(() =>
            {
                var voxels = ManaProbeVoxelizer.Build(settings, list, token);
                ManaConsole.WriteDiagnostic($"Probe volume: voxelized {list.Length} meshes into {voxels.X}x{voxels.Y}x{voxels.Z} " +
                    $"({voxels.Occupied} with surface) in {started.Elapsed.TotalMilliseconds:0} ms.");
                return voxels;
            }, token);
        }

        // Makes the textures for a new volume and uploads its voxels. True when the harmonics textures changed.
        private bool Upload(ManaProbeVolumeSettings settings, ManaProbeVoxels voxels)
        {
            var (px, py, pz) = settings.ProbeCounts;
            var sizeChanged = _baked == null || _baked.ProbeCounts != settings.ProbeCounts || _baked.VoxelCounts != settings.VoxelCounts;
            if (sizeChanged)
            {
                ReleaseTextures();
                _albedo = Create3D(voxels.X, voxels.Y, voxels.Z, TextureFormat.Rgba8Unorm, TextureUsage.TextureBinding | TextureUsage.CopyDst);
                _normal = Create3D(voxels.X, voxels.Y, voxels.Z, TextureFormat.Rgba8Unorm, TextureUsage.TextureBinding | TextureUsage.CopyDst);
                _emission = Create3D(voxels.X, voxels.Y, voxels.Z, TextureFormat.Rgba16float, TextureUsage.TextureBinding | TextureUsage.CopyDst);
                _radiance = Create3D(voxels.X, voxels.Y, voxels.Z, TextureFormat.Rgba16float, TextureUsage.TextureBinding | TextureUsage.StorageBinding);
                Red = Create3D(px, py, pz, TextureFormat.Rgba16float, TextureUsage.TextureBinding | TextureUsage.StorageBinding);
                Green = Create3D(px, py, pz, TextureFormat.Rgba16float, TextureUsage.TextureBinding | TextureUsage.StorageBinding);
                Blue = Create3D(px, py, pz, TextureFormat.Rgba16float, TextureUsage.TextureBinding | TextureUsage.StorageBinding);
                if (_probes != null)
                {
                    Api.BufferRelease(_probes);
                }
                _probesSize = (ulong)(px * py * pz * 4 * sizeof(Vector4));
                _probes = CreateBuffer(_probesSize, BufferUsage.Storage);
                RebuildGroups();
            }

            fixed (byte* albedo = voxels.Albedo)
            fixed (byte* normal = voxels.Normal)
            fixed (Half* emission = voxels.Emission)
            {
                WriteVoxels(_albedo, albedo, voxels, 4);
                WriteVoxels(_normal, normal, voxels, 4);
                WriteVoxels(_emission, emission, voxels, 8);
            }

            _baked = settings;
            var spacing = settings.ProbeSpacing;
            FrameExtent = new Vector4(px * spacing, py * spacing, pz * spacing, spacing);
            FrameMin = new Vector4(settings.Min, 0.0f);
            ManaConsole.WriteDiagnostic($"Probe volume: {px}x{py}x{pz} probes {spacing} m apart over {settings.Size}.");
            return sizeChanged;
        }

        private void Bake(CommandEncoder* encoder, ManaRenderScene scene, TextureView* environment)
        {
            if (environment != _gatherEnvironment)
            {
                _gatherEnvironment = environment;
                RebuildGroups();
            }

            var settings = _baked!;
            var lights = scene.LocalLights;
            var lightCount = Math.Min(lights.Count, ManaRenderScene.MaxLocalLights);
            if (lightCount > 0)
            {
                var data = new Vector4[lightCount * 2];
                for (var i = 0; i < lightCount; i++)
                {
                    data[i * 2] = lights[i].Position;
                    data[i * 2 + 1] = lights[i].Color;
                }
                fixed (Vector4* pointer = data)
                {
                    Api.QueueWriteBuffer(Queue, _lights, 0, pointer, (nuint)(data.Length * sizeof(Vector4)));
                }
            }

            var (vx, vy, vz) = settings.VoxelCounts;
            var (px, py, pz) = settings.ProbeCounts;
            var bounces = Math.Clamp(settings.Bounces, 1, MaxBounces);
            for (var bounce = 0; bounce < bounces; bounce++)
            {
                var parameters = new Params
                {
                    VolumeMin = new Vector4(settings.Min, MathF.Max(settings.VoxelSize, 0.05f)),
                    VolumeSize = new Vector4(settings.Size, settings.ProbeSpacing),
                    VoxelsX = (uint)vx, VoxelsY = (uint)vy, VoxelsZ = (uint)vz, LightCount = (uint)lightCount,
                    ProbesX = (uint)px, ProbesY = (uint)py, ProbesZ = (uint)pz, Bounce = (uint)bounce,
                    SunDirection = new Vector4(Vector3.Normalize(scene.SunDirection), scene.HasSun ? 1.0f : 0.0f),
                    SunColor = new Vector4(new Vector3(scene.SunColor.X, scene.SunColor.Y, scene.SunColor.Z) * scene.SunColor.W, 0.0f),
                    Sky = new Vector4(SkyIntensity(scene), RaysPerProbe, 0.0f, 0.0f)
                };
                Api.QueueWriteBuffer(Queue, _params, (ulong)(bounce * ParamStride), &parameters, (nuint)sizeof(Params));
            }

            for (var bounce = 0; bounce < bounces; bounce++)
            {
                var offset = (uint)(bounce * ParamStride);
                Dispatch(encoder, _light, offset, vx, vy, vz);
                Dispatch(encoder, _gather, offset, px, py, pz);
                Dispatch(encoder, _pack, offset, px, py, pz);
            }
            FrameMin = new Vector4(settings.Min, 1.0f);
            _bakes++;
            if (_bakes <= 3 || _bakes % 100 == 0)
            {
                ManaConsole.WriteDiagnostic($"Probe volume: bake {_bakes} encoded ({bounces} bounces, {lightCount} lights, {RaysPerProbe} rays a probe).");
            }
        }

        private int _bakes;

        // As the frame uniform's IBL factor: a photo sky dims with the daylight; a computed one is drawn dimmed.
        private static float SkyIntensity(ManaRenderScene scene)
        {
            var intensity = scene.IrradianceSettings.IblIntensity;
            if (scene.Sky.Mode == ManaSkyMode.Photo)
            {
                intensity *= scene.Sky.Daylight;
            }
            return Math.Clamp(intensity, 0.0f, 8.0f);
        }

        private void Dispatch(CommandEncoder* encoder, Step step, uint offset, int x, int y, int z)
        {
            var descriptor = new ComputePassDescriptor();
            var pass = Api.CommandEncoderBeginComputePass(encoder, &descriptor);
            try
            {
                Api.ComputePassEncoderSetPipeline(pass, step.Pipeline);
                Api.ComputePassEncoderSetBindGroup(pass, 0, step.Group, 1, &offset);
                Api.ComputePassEncoderDispatchWorkgroups(pass, (uint)((x + 3) / 4), (uint)((y + 3) / 4), (uint)((z + 3) / 4));
                Api.ComputePassEncoderEnd(pass);
            }
            finally
            {
                Api.ComputePassEncoderRelease(pass);
            }
        }

        // What decides the voxels: the box, and each mesh's identity, version, place and size.
        private static long GeometrySignature(ManaProbeVolumeSettings settings, IReadOnlyList<ManaRenderMeshInstance> meshes)
        {
            var hash = new HashCode();
            hash.Add(settings);
            hash.Add(meshes.Count);
            foreach (var mesh in meshes)
            {
                hash.Add(mesh.MeshKey);
                hash.Add(mesh.MeshVersion);
                hash.Add(mesh.MeshData.Positions.Length);
                hash.Add(MathF.Round(mesh.WorldTransform.M41, 2));
                hash.Add(MathF.Round(mesh.WorldTransform.M42, 2));
                hash.Add(MathF.Round(mesh.WorldTransform.M43, 2));
            }
            return hash.ToHashCode();
        }

        // What decides the light: the sun, the placed lights, the sky.
        private static long LightingSignature(ManaRenderScene scene, TextureView* environment)
        {
            var hash = new HashCode();
            hash.Add(scene.HasSun);
            hash.Add(Vector3.Round(scene.SunDirection * 200.0f));
            hash.Add(Vector4.Round(scene.SunColor * 100.0f));
            hash.Add(scene.LocalLights.Count);
            foreach (var (position, color) in scene.LocalLights)
            {
                hash.Add(Vector4.Round(position * 20.0f));
                // Not w: that is the light's shadow slot, which moves with the camera and changes no bounce.
                hash.Add(Vector3.Round(new Vector3(color.X, color.Y, color.Z) * 50.0f));
            }
            hash.Add(scene.Sky.Mode);
            hash.Add(MathF.Round(SkyIntensity(scene), 2));
            hash.Add((nint)environment);
            return hash.ToHashCode();
        }

        private void RebuildGroups()
        {
            if (_albedo == null)
            {
                return;
            }
            Release(ref _light.Group);
            Release(ref _gather.Group);
            Release(ref _pack.Group);

            var paramsSize = (ulong)sizeof(Params);
            var light = stackalloc BindGroupEntry[10];
            light[0] = new BindGroupEntry { Binding = 0, Buffer = _params, Size = paramsSize };
            light[1] = new BindGroupEntry { Binding = 1, TextureView = _albedo };
            light[2] = new BindGroupEntry { Binding = 2, TextureView = _normal };
            light[3] = new BindGroupEntry { Binding = 3, TextureView = _emission };
            light[4] = new BindGroupEntry { Binding = 4, Buffer = _lights, Size = (ulong)(ManaRenderScene.MaxLocalLights * 2 * sizeof(Vector4)) };
            light[5] = new BindGroupEntry { Binding = 5, TextureView = Red };
            light[6] = new BindGroupEntry { Binding = 6, TextureView = Green };
            light[7] = new BindGroupEntry { Binding = 7, TextureView = Blue };
            light[8] = new BindGroupEntry { Binding = 8, Sampler = _sampler };
            light[9] = new BindGroupEntry { Binding = 9, TextureView = _radiance };
            _light.Group = CreateGroup(_light.Layout, light, 10);

            var pack = stackalloc BindGroupEntry[5];
            pack[0] = new BindGroupEntry { Binding = 0, Buffer = _params, Size = paramsSize };
            pack[1] = new BindGroupEntry { Binding = 1, Buffer = _probes, Size = _probesSize };
            pack[2] = new BindGroupEntry { Binding = 2, TextureView = Red };
            pack[3] = new BindGroupEntry { Binding = 3, TextureView = Green };
            pack[4] = new BindGroupEntry { Binding = 4, TextureView = Blue };
            _pack.Group = CreateGroup(_pack.Layout, pack, 5);

            if (_gatherEnvironment != null)
            {
                var gather = stackalloc BindGroupEntry[7];
                gather[0] = new BindGroupEntry { Binding = 0, Buffer = _params, Size = paramsSize };
                gather[1] = new BindGroupEntry { Binding = 1, TextureView = _albedo };
                gather[2] = new BindGroupEntry { Binding = 2, TextureView = _normal };
                gather[3] = new BindGroupEntry { Binding = 3, TextureView = _radiance };
                gather[4] = new BindGroupEntry { Binding = 4, TextureView = _gatherEnvironment };
                gather[5] = new BindGroupEntry { Binding = 5, Sampler = _sampler };
                gather[6] = new BindGroupEntry { Binding = 6, Buffer = _probes, Size = _probesSize };
                _gather.Group = CreateGroup(_gather.Layout, gather, 7);
            }
        }

        private BindGroup* CreateGroup(BindGroupLayout* layout, BindGroupEntry* entries, int count)
        {
            var descriptor = new BindGroupDescriptor { Layout = layout, EntryCount = (nuint)count, Entries = entries };
            var group = Api.DeviceCreateBindGroup(Device, &descriptor);
            if (group == null)
            {
                throw new InvalidOperationException("Failed to create a probe volume bind group.");
            }
            return group;
        }

        private static BindGroupLayoutEntry Uniform(uint binding) => new()
        {
            Binding = binding,
            Visibility = ShaderStage.Compute,
            Buffer = new BufferBindingLayout { Type = BufferBindingType.Uniform, HasDynamicOffset = true, MinBindingSize = (ulong)sizeof(Params) }
        };

        private static BindGroupLayoutEntry Texture3D(uint binding, TextureSampleType type) => new()
        {
            Binding = binding,
            Visibility = ShaderStage.Compute,
            Texture = new TextureBindingLayout { SampleType = type, ViewDimension = TextureViewDimension.Dimension3D }
        };

        private static BindGroupLayoutEntry Storage3D(uint binding) => new()
        {
            Binding = binding,
            Visibility = ShaderStage.Compute,
            StorageTexture = new StorageTextureBindingLayout
            {
                Access = StorageTextureAccess.WriteOnly,
                Format = TextureFormat.Rgba16float,
                ViewDimension = TextureViewDimension.Dimension3D
            }
        };

        private static BindGroupLayoutEntry Buffer(uint binding, BufferBindingType type) => new()
        {
            Binding = binding,
            Visibility = ShaderStage.Compute,
            Buffer = new BufferBindingLayout { Type = type }
        };

        private static BindGroupLayoutEntry FilteringSampler(uint binding) => new()
        {
            Binding = binding,
            Visibility = ShaderStage.Compute,
            Sampler = new SamplerBindingLayout { Type = SamplerBindingType.Filtering }
        };

        private static BindGroupLayoutEntry[] LightLayout() =>
        [
            Uniform(0),
            Texture3D(1, TextureSampleType.Float),
            Texture3D(2, TextureSampleType.Float),
            Texture3D(3, TextureSampleType.Float),
            Buffer(4, BufferBindingType.ReadOnlyStorage),
            Texture3D(5, TextureSampleType.Float),
            Texture3D(6, TextureSampleType.Float),
            Texture3D(7, TextureSampleType.Float),
            FilteringSampler(8),
            Storage3D(9)
        ];

        private static BindGroupLayoutEntry[] GatherLayout() =>
        [
            Uniform(0),
            Texture3D(1, TextureSampleType.Float),
            Texture3D(2, TextureSampleType.Float),
            Texture3D(3, TextureSampleType.Float),
            new()
            {
                Binding = 4,
                Visibility = ShaderStage.Compute,
                Texture = new TextureBindingLayout { SampleType = TextureSampleType.Float, ViewDimension = TextureViewDimension.DimensionCube }
            },
            FilteringSampler(5),
            Buffer(6, BufferBindingType.Storage)
        ];

        private static BindGroupLayoutEntry[] PackLayout() =>
        [
            Uniform(0),
            Buffer(1, BufferBindingType.ReadOnlyStorage),
            Storage3D(2),
            Storage3D(3),
            Storage3D(4)
        ];

        private void CreateStep(Step step, string shader, BindGroupLayoutEntry[] entries)
        {
            var source = ManaWgslPreprocessor.LoadResolved(ManaWebGpuDevice.ResolveAssetPath(ManaWebGpuDevice.ShaderPath(shader)));
            var code = Marshal.StringToCoTaskMemUTF8(source);
            var entryPoint = Marshal.StringToCoTaskMemUTF8("cs_main");
            try
            {
                var wgsl = new ShaderModuleWGSLDescriptor
                {
                    Chain = new ChainedStruct { SType = SType.ShaderModuleWgslDescriptor },
                    Code = (byte*)code
                };
                var shaderDescriptor = new ShaderModuleDescriptor { NextInChain = &wgsl.Chain };
                step.Shader = Api.DeviceCreateShaderModule(Device, &shaderDescriptor);
                if (step.Shader == null)
                {
                    throw new InvalidOperationException($"Failed to create the probe volume shader {shader}.");
                }

                fixed (BindGroupLayoutEntry* pointer = entries)
                {
                    var layoutDescriptor = new BindGroupLayoutDescriptor { EntryCount = (nuint)entries.Length, Entries = pointer };
                    step.Layout = Api.DeviceCreateBindGroupLayout(Device, &layoutDescriptor);
                }
                var layout = step.Layout;
                var pipelineLayoutDescriptor = new PipelineLayoutDescriptor { BindGroupLayoutCount = 1, BindGroupLayouts = &layout };
                step.PipelineLayout = Api.DeviceCreatePipelineLayout(Device, &pipelineLayoutDescriptor);
                var pipelineDescriptor = new ComputePipelineDescriptor
                {
                    Layout = step.PipelineLayout,
                    Compute = new ProgrammableStageDescriptor { Module = step.Shader, EntryPoint = (byte*)entryPoint }
                };
                step.Pipeline = Api.DeviceCreateComputePipeline(Device, &pipelineDescriptor);
                if (step.Pipeline == null)
                {
                    throw new InvalidOperationException($"Failed to create the probe volume pipeline {shader}.");
                }
            }
            finally
            {
                Marshal.FreeCoTaskMem(code);
                Marshal.FreeCoTaskMem(entryPoint);
            }
        }

        private TextureView* Create3D(int width, int height, int depth, TextureFormat format, TextureUsage usage)
        {
            var descriptor = new TextureDescriptor
            {
                Usage = usage,
                Dimension = TextureDimension.Dimension3D,
                Size = new Extent3D { Width = (uint)width, Height = (uint)height, DepthOrArrayLayers = (uint)depth },
                Format = format,
                MipLevelCount = 1,
                SampleCount = 1
            };
            var texture = Api.DeviceCreateTexture(Device, &descriptor);
            if (texture == null)
            {
                throw new InvalidOperationException("Failed to create a probe volume texture.");
            }
            var viewDescriptor = new TextureViewDescriptor
            {
                Format = format,
                Dimension = TextureViewDimension.Dimension3D,
                MipLevelCount = 1,
                ArrayLayerCount = 1,
                Aspect = TextureAspect.All
            };
            var view = Api.TextureCreateView(texture, &viewDescriptor);
            _textures.Add(((nint)texture, (nint)view));
            return view;
        }

        private void WriteVoxels(TextureView* view, void* data, ManaProbeVoxels voxels, int bytesPerVoxel)
        {
            var texture = (Texture*)_textures.First(entry => entry.View == (nint)view).Texture;
            var destination = new ImageCopyTexture { Texture = texture, MipLevel = 0, Origin = new Origin3D(), Aspect = TextureAspect.All };
            var layout = new TextureDataLayout { Offset = 0, BytesPerRow = (uint)(voxels.X * bytesPerVoxel), RowsPerImage = (uint)voxels.Y };
            var size = new Extent3D { Width = (uint)voxels.X, Height = (uint)voxels.Y, DepthOrArrayLayers = (uint)voxels.Z };
            Api.QueueWriteTexture(Queue, &destination, data, (nuint)((long)voxels.X * voxels.Y * voxels.Z * bytesPerVoxel), &layout, &size);
        }

        private WebGpuBuffer* CreateBuffer(ulong size, BufferUsage usage)
        {
            var descriptor = new BufferDescriptor { Usage = usage, Size = Math.Max(size, 16) };
            var buffer = Api.DeviceCreateBuffer(Device, &descriptor);
            if (buffer == null)
            {
                throw new InvalidOperationException("Failed to create a probe volume buffer.");
            }
            return buffer;
        }

        private void Release(ref BindGroup* group)
        {
            if (group != null)
            {
                Api.BindGroupRelease(group);
                group = null;
            }
        }

        private void ReleaseTextures()
        {
            foreach (var (texture, view) in _textures)
            {
                Api.TextureViewRelease((TextureView*)view);
                Api.TextureRelease((Texture*)texture);
            }
            _textures.Clear();
            _albedo = _normal = _emission = _radiance = null;
            Red = Green = Blue = null;
        }

        public override void Dispose()
        {
            _cancel?.Cancel();
            foreach (var step in new[] { _light, _gather, _pack })
            {
                Release(ref step.Group);
                if (step.Pipeline != null) { Api.ComputePipelineRelease(step.Pipeline); step.Pipeline = null; }
                if (step.PipelineLayout != null) { Api.PipelineLayoutRelease(step.PipelineLayout); step.PipelineLayout = null; }
                if (step.Layout != null) { Api.BindGroupLayoutRelease(step.Layout); step.Layout = null; }
                if (step.Shader != null) { Api.ShaderModuleRelease(step.Shader); step.Shader = null; }
            }
            ReleaseTextures();
            if (_probes != null) { Api.BufferRelease(_probes); _probes = null; }
            if (_params != null) { Api.BufferRelease(_params); _params = null; }
            if (_lights != null) { Api.BufferRelease(_lights); _lights = null; }
            if (_sampler != null) { Api.SamplerRelease(_sampler); _sampler = null; }
        }
    }
}
