using System.Runtime.InteropServices;
using ManaCore.Graphics;
using Silk.NET.WebGPU;
using WebGpuBuffer = Silk.NET.WebGPU.Buffer;
using ManaGraphics.Code.Graphics.Resources;

namespace ManaGraphics.Code.Graphics.Renderers
{
    internal unsafe sealed class ManaWebGpuEnvironmentPrefilterCompute : ManaWebGpuDeviceObject, IManaComputeShader
    {
        private readonly ShaderModule* _shaderModule;
        private readonly BindGroupLayout* _bindGroupLayout;
        private readonly PipelineLayout* _pipelineLayout;
        private readonly ComputePipeline* _pipeline;
        private readonly Sampler* _sampler;

        public ManaGraphicsBackendKind Backend => ManaGraphicsBackendKind.WebGpu;
        public string Name => "Environment Prefilter";

        public ManaWebGpuEnvironmentPrefilterCompute(ManaWebGpuDevice gpu, string shaderPath)
            : base(gpu)
        {
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
            var commandEncoder = Api.DeviceCreateCommandEncoder(Device, &commandEncoderDescriptor);
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
                commandBuffer = Api.CommandEncoderFinish(commandEncoder, &commandBufferDescriptor);
                Api.QueueSubmit(Queue, 1, &commandBuffer);
            }
            finally
            {
                if (commandBuffer != null)
                {
                    Api.CommandBufferRelease(commandBuffer);
                }
                Api.CommandEncoderRelease(commandEncoder);
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

        /// <summary>Convolves the prefiltered cube into irradiance; reading the sharp cube instead put clouds on every curve.</summary>
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
                    Api.BindGroupRelease(bindGroup);
                }
                if (parameterBuffer != null)
                {
                    Api.BufferRelease(parameterBuffer);
                }
            }
        }

        private void Run(CommandEncoder* commandEncoder, BindGroup* bindGroup, uint size)
        {
            var computePassDescriptor = new ComputePassDescriptor();
            var computePass = Api.CommandEncoderBeginComputePass(commandEncoder, &computePassDescriptor);
            Api.ComputePassEncoderSetPipeline(computePass, _pipeline);
            Api.ComputePassEncoderSetBindGroup(computePass, 0, bindGroup, 0, null);
            var dispatchSize = new ManaComputeDispatchSize((size + 7u) / 8u, (size + 7u) / 8u, 6u);
            Api.ComputePassEncoderDispatchWorkgroups(computePass, dispatchSize.X, dispatchSize.Y, dispatchSize.Z);
            Api.ComputePassEncoderEnd(computePass);
            Api.ComputePassEncoderRelease(computePass);
        }

        public override void Dispose()
        {
            if (_sampler != null)
            {
                Api.SamplerRelease(_sampler);
            }

            if (_pipeline != null)
            {
                Api.ComputePipelineRelease(_pipeline);
            }

            if (_pipelineLayout != null)
            {
                Api.PipelineLayoutRelease(_pipelineLayout);
            }

            if (_bindGroupLayout != null)
            {
                Api.BindGroupLayoutRelease(_bindGroupLayout);
            }

            if (_shaderModule != null)
            {
                Api.ShaderModuleRelease(_shaderModule);
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
            var parameterBuffer = Api.DeviceCreateBuffer(Device, &bufferDescriptor);
            if (parameterBuffer == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU environment prefilter parameter buffer.");
            }

            Api.QueueWriteBuffer(Queue, parameterBuffer, 0, &parameters, (nuint)sizeof(ManaWebGpuEnvironmentPrefilterUniform));
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
            var bindGroup = Api.DeviceCreateBindGroup(Device, &bindGroupDescriptor);
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
            var sampler = Api.DeviceCreateSampler(Device, &samplerDescriptor);
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

                var shaderModule = Api.DeviceCreateShaderModule(Device, &shaderDescriptor);
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
            var layout = Api.DeviceCreateBindGroupLayout(Device, &descriptor);
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
            var layout = Api.DeviceCreatePipelineLayout(Device, &descriptor);
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
                var pipeline = Api.DeviceCreateComputePipeline(Device, &descriptor);
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
