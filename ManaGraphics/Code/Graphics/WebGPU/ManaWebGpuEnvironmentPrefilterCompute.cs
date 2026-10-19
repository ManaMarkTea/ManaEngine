using System.Runtime.InteropServices;
using ManaCore.Graphics;
using Silk.NET.WebGPU;
using WebGpuBuffer = Silk.NET.WebGPU.Buffer;

namespace ManaGraphics.Code.Graphics
{
    internal unsafe sealed class ManaWebGpuEnvironmentPrefilterCompute : ManaComputeShader
    {
        private readonly WebGPU _api;
        private readonly Device* _device;
        private readonly Queue* _queue;
        private readonly ShaderModule* _shaderModule;
        private readonly BindGroupLayout* _bindGroupLayout;
        private readonly PipelineLayout* _pipelineLayout;
        private readonly ComputePipeline* _pipeline;
        private readonly Sampler* _sampler;

        public ManaWebGpuEnvironmentPrefilterCompute(
            WebGPU api,
            Device* device,
            Queue* queue,
            string shaderPath)
            : base(ManaGraphicsBackendKind.WebGpu, "Environment Prefilter")
        {
            _api = api;
            _device = device;
            _queue = queue;
            _sampler = CreateSampler();
            _shaderModule = CreateShaderModule(shaderPath);
            _bindGroupLayout = CreateBindGroupLayout();
            _pipelineLayout = CreatePipelineLayout();
            _pipeline = CreatePipeline();
        }

        public void DispatchNow(
            ManaWebGpuTextureResource sourceMap,
            ManaWebGpuTextureResource destinationMap,
            ManaWebGpuTextureResource irradianceMap)
        {
            var commandEncoderDescriptor = new CommandEncoderDescriptor();
            var commandEncoder = _api.DeviceCreateCommandEncoder(_device, &commandEncoderDescriptor);
            if (commandEncoder == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU command encoder for environment prefilter.");
            }

            CommandBuffer* commandBuffer = null;
            try
            {
                Dispatch(commandEncoder, sourceMap, destinationMap);
                DispatchIrradiance(commandEncoder, destinationMap, irradianceMap);

                var commandBufferDescriptor = new CommandBufferDescriptor();
                commandBuffer = _api.CommandEncoderFinish(commandEncoder, &commandBufferDescriptor);
                _api.QueueSubmit(_queue, 1, &commandBuffer);
            }
            finally
            {
                if (commandBuffer != null)
                {
                    _api.CommandBufferRelease(commandBuffer);
                }
                _api.CommandEncoderRelease(commandEncoder);
            }
        }

        public void Dispatch(
            CommandEncoder* commandEncoder,
            ManaWebGpuTextureResource sourceMap,
            ManaWebGpuTextureResource destinationMap)
        {
            if (commandEncoder == null ||
                sourceMap.TextureView == null ||
                destinationMap.StorageViews.Count == 0)
            {
                return;
            }

            for (var mip = 0; mip < destinationMap.StorageViews.Count; mip++)
            {
                var destinationView = (TextureView*)destinationMap.StorageViews[mip];
                if (destinationView == null)
                {
                    continue;
                }

                DispatchMip(commandEncoder, sourceMap, destinationMap, destinationView, mip);
            }
        }

        /// <summary>
        /// Convolves the prefiltered reflection cube into the diffuse irradiance cube. Run after <see cref="Dispatch"/>,
        /// which it reads: irradiance taken from the sharp sky shows its clouds on every curved surface.
        /// </summary>
        public void DispatchIrradiance(
            CommandEncoder* commandEncoder,
            ManaWebGpuTextureResource prefilteredMap,
            ManaWebGpuTextureResource irradianceMap)
        {
            if (commandEncoder == null ||
                prefilteredMap.TextureView == null ||
                irradianceMap.StorageViews.Count == 0 ||
                irradianceMap.StorageViews[0] == 0)
            {
                return;
            }

            var size = irradianceMap.Width;
            Run(commandEncoder, prefilteredMap.TextureView, (TextureView*)irradianceMap.StorageViews[0],
                prefilteredMap.Width, 1.0f, IrradianceSampleCount, IrradianceMode, size);
        }

        private const uint PrefilterMode = 0u;
        private const uint IrradianceMode = 1u;
        private const uint IrradianceSampleCount = 512u;

        private void Run(
            CommandEncoder* commandEncoder, TextureView* sourceView, TextureView* destinationView,
            uint sourceResolution, float roughness, uint sampleCount, uint mode, uint size)
        {
            WebGpuBuffer* parameterBuffer = null;
            BindGroup* bindGroup = null;
            try
            {
                parameterBuffer = CreateParameterBuffer(sourceResolution, roughness, sampleCount, mode);
                bindGroup = CreateBindGroup(sourceView, destinationView, parameterBuffer);
                Run(commandEncoder, bindGroup, size);
            }
            finally
            {
                if (bindGroup != null)
                {
                    _api.BindGroupRelease(bindGroup);
                }
                if (parameterBuffer != null)
                {
                    _api.BufferRelease(parameterBuffer);
                }
            }
        }

        private void Run(CommandEncoder* commandEncoder, BindGroup* bindGroup, uint size)
        {
            var computePassDescriptor = new ComputePassDescriptor();
            var computePass = _api.CommandEncoderBeginComputePass(commandEncoder, &computePassDescriptor);
            _api.ComputePassEncoderSetPipeline(computePass, _pipeline);
            _api.ComputePassEncoderSetBindGroup(computePass, 0, bindGroup, 0, null);
            var dispatchSize = new ManaComputeDispatchSize((size + 7u) / 8u, (size + 7u) / 8u, 6u);
            _api.ComputePassEncoderDispatchWorkgroups(computePass, dispatchSize.X, dispatchSize.Y, dispatchSize.Z);
            _api.ComputePassEncoderEnd(computePass);
            _api.ComputePassEncoderRelease(computePass);
        }

        public override void Dispose()
        {
            if (_sampler != null)
            {
                _api.SamplerRelease(_sampler);
            }

            if (_pipeline != null)
            {
                _api.ComputePipelineRelease(_pipeline);
            }

            if (_pipelineLayout != null)
            {
                _api.PipelineLayoutRelease(_pipelineLayout);
            }

            if (_bindGroupLayout != null)
            {
                _api.BindGroupLayoutRelease(_bindGroupLayout);
            }

            if (_shaderModule != null)
            {
                _api.ShaderModuleRelease(_shaderModule);
            }
        }

        private void DispatchMip(
            CommandEncoder* commandEncoder,
            ManaWebGpuTextureResource sourceMap,
            ManaWebGpuTextureResource destinationMap,
            TextureView* destinationView,
            int mip)
        {
            var mipSize = Math.Max(1u, destinationMap.Width >> mip);
            var roughness = destinationMap.StorageViews.Count <= 1
                ? 0.0f
                : mip / (float)(destinationMap.StorageViews.Count - 1);
            var sampleCount = GetPrefilterSampleCount(mip, roughness);
            Run(commandEncoder, sourceMap.TextureView, destinationView, sourceMap.Width, roughness, sampleCount, PrefilterMode, mipSize);
        }

        private WebGpuBuffer* CreateParameterBuffer(uint sourceResolution, float roughness, uint sampleCount, uint mode)
        {
            var parameters = new ManaWebGpuEnvironmentPrefilterUniform
            {
                Roughness = roughness,
                SourceResolution = Math.Max(1.0f, sourceResolution),
                SampleCount = sampleCount,
                Mode = mode
            };

            var bufferDescriptor = new BufferDescriptor
            {
                Usage = BufferUsage.Uniform | BufferUsage.CopyDst,
                Size = (ulong)sizeof(ManaWebGpuEnvironmentPrefilterUniform)
            };
            var parameterBuffer = _api.DeviceCreateBuffer(_device, &bufferDescriptor);
            if (parameterBuffer == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU environment prefilter parameter buffer.");
            }

            _api.QueueWriteBuffer(_queue, parameterBuffer, 0, &parameters, (nuint)sizeof(ManaWebGpuEnvironmentPrefilterUniform));
            return parameterBuffer;
        }

        private BindGroup* CreateBindGroup(
            TextureView* sourceView,
            TextureView* destinationView,
            WebGpuBuffer* parameterBuffer)
        {
            var bindGroupEntries = stackalloc BindGroupEntry[4];
            bindGroupEntries[0] = new BindGroupEntry
            {
                Binding = 0,
                Sampler = _sampler
            };
            bindGroupEntries[1] = new BindGroupEntry
            {
                Binding = 1,
                TextureView = sourceView
            };
            bindGroupEntries[2] = new BindGroupEntry
            {
                Binding = 2,
                TextureView = destinationView
            };
            bindGroupEntries[3] = new BindGroupEntry
            {
                Binding = 3,
                Buffer = parameterBuffer,
                Offset = 0,
                Size = (ulong)sizeof(ManaWebGpuEnvironmentPrefilterUniform)
            };

            var bindGroupDescriptor = new BindGroupDescriptor
            {
                Layout = _bindGroupLayout,
                EntryCount = 4,
                Entries = bindGroupEntries
            };
            var bindGroup = _api.DeviceCreateBindGroup(_device, &bindGroupDescriptor);
            if (bindGroup == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU environment prefilter bind group.");
            }

            return bindGroup;
        }

        private Sampler* CreateSampler()
        {
            var samplerDescriptor = new SamplerDescriptor
            {
                AddressModeU = AddressMode.ClampToEdge,
                AddressModeV = AddressMode.ClampToEdge,
                AddressModeW = AddressMode.ClampToEdge,
                MagFilter = FilterMode.Linear,
                MinFilter = FilterMode.Linear,
                MipmapFilter = MipmapFilterMode.Linear,
                LodMinClamp = 0.0f,
                LodMaxClamp = 32.0f,
                Compare = CompareFunction.Undefined,
                MaxAnisotropy = 1
            };
            var sampler = _api.DeviceCreateSampler(_device, &samplerDescriptor);
            if (sampler == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU environment prefilter sampler.");
            }

            return sampler;
        }

        private ShaderModule* CreateShaderModule(string shaderPath)
        {
            var source = ManaWgslPreprocessor.LoadResolved(shaderPath);
            nint sourcePointer = Marshal.StringToCoTaskMemUTF8(source);

            try
            {
                var wgslDescriptor = new ShaderModuleWGSLDescriptor
                {
                    Chain = new ChainedStruct
                    {
                        SType = SType.ShaderModuleWgslDescriptor
                    },
                    Code = (byte*)sourcePointer
                };

                var shaderDescriptor = new ShaderModuleDescriptor
                {
                    NextInChain = &wgslDescriptor.Chain
                };

                var shaderModule = _api.DeviceCreateShaderModule(_device, &shaderDescriptor);
                if (shaderModule == null)
                {
                    throw new InvalidOperationException($"Failed to create WebGPU environment prefilter shader module from {shaderPath}.");
                }

                return shaderModule;
            }
            finally
            {
                Marshal.FreeCoTaskMem(sourcePointer);
            }
        }

        private BindGroupLayout* CreateBindGroupLayout()
        {
            var samplerEntry = new BindGroupLayoutEntry
            {
                Binding = 0,
                Visibility = ShaderStage.Compute,
                Sampler = new SamplerBindingLayout
                {
                    Type = SamplerBindingType.Filtering
                }
            };

            var sourceEntry = new BindGroupLayoutEntry
            {
                Binding = 1,
                Visibility = ShaderStage.Compute,
                Texture = new TextureBindingLayout
                {
                    SampleType = TextureSampleType.Float,
                    ViewDimension = TextureViewDimension.DimensionCube,
                    Multisampled = false
                }
            };

            var destinationEntry = new BindGroupLayoutEntry
            {
                Binding = 2,
                Visibility = ShaderStage.Compute,
                StorageTexture = new StorageTextureBindingLayout
                {
                    Access = StorageTextureAccess.WriteOnly,
                    Format = TextureFormat.Rgba8Unorm,
                    ViewDimension = TextureViewDimension.Dimension2DArray
                }
            };

            var parametersEntry = new BindGroupLayoutEntry
            {
                Binding = 3,
                Visibility = ShaderStage.Compute,
                Buffer = new BufferBindingLayout
                {
                    Type = BufferBindingType.Uniform,
                    HasDynamicOffset = false,
                    MinBindingSize = (ulong)sizeof(ManaWebGpuEnvironmentPrefilterUniform)
                }
            };

            var entries = stackalloc BindGroupLayoutEntry[4];
            entries[0] = samplerEntry;
            entries[1] = sourceEntry;
            entries[2] = destinationEntry;
            entries[3] = parametersEntry;

            var descriptor = new BindGroupLayoutDescriptor
            {
                EntryCount = 4,
                Entries = entries
            };
            var layout = _api.DeviceCreateBindGroupLayout(_device, &descriptor);
            if (layout == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU environment prefilter bind group layout.");
            }

            return layout;
        }

        private PipelineLayout* CreatePipelineLayout()
        {
            var bindGroupLayout = _bindGroupLayout;
            var descriptor = new PipelineLayoutDescriptor
            {
                BindGroupLayoutCount = 1,
                BindGroupLayouts = &bindGroupLayout
            };
            var layout = _api.DeviceCreatePipelineLayout(_device, &descriptor);
            if (layout == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU environment prefilter pipeline layout.");
            }

            return layout;
        }

        private ComputePipeline* CreatePipeline()
        {
            nint entryPoint = Marshal.StringToCoTaskMemUTF8("cs_main");
            try
            {
                var descriptor = new ComputePipelineDescriptor
                {
                    Layout = _pipelineLayout,
                    Compute = new ProgrammableStageDescriptor
                    {
                        Module = _shaderModule,
                        EntryPoint = (byte*)entryPoint
                    }
                };
                var pipeline = _api.DeviceCreateComputePipeline(_device, &descriptor);
                if (pipeline == null)
                {
                    throw new InvalidOperationException("Failed to create WebGPU environment prefilter compute pipeline.");
                }

                return pipeline;
            }
            finally
            {
                Marshal.FreeCoTaskMem(entryPoint);
            }
        }

        private static uint GetPrefilterSampleCount(int mip, float roughness)
        {
            if (roughness < 0.0001f)
            {
                return 1u;
            }

            return 1024u;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ManaWebGpuEnvironmentPrefilterUniform
        {
            public float Roughness;
            public float SourceResolution;
            public uint SampleCount;
            public uint Mode;
        }
    }
}
