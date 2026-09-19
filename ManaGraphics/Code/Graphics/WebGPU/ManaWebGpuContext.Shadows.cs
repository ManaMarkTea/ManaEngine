using System.Runtime.InteropServices;
using System.Numerics;
using ManaGraphics.Code.Diagnostics;
using Silk.NET.WebGPU;
using WebGpuBuffer = Silk.NET.WebGPU.Buffer;

namespace ManaGraphics.Code.Graphics
{
    // Cascaded shadow maps for the scene's single directional "sun" light (see ManaWorld.CollectLightsFromNode
    // / ManaRenderScene.HasSun). Point lights are never shadow-cast in this first pass -- only a Directional
    // ManaLightSourceComponent with CastsShadows set produces one.
    //
    // Three cascades, each a depth-only render into its own layer of one Texture2DArray, fit to a slice of
    // the main camera's frustum every frame (UpdateShadowCascades) and sampled back in pbr_irradiance.wgsl
    // via the shadow map + comparison sampler bound alongside the frame uniform in group 0 (see
    // CreateSharedPipelineLayouts in ManaWebGpuContext.Pipelines.cs).
    //
    // Known limitation: no texel snapping, so the cascades can shimmer slightly as the camera moves. Left
    // out for a first pass rather than risk subtly wrong math nobody could visually verify blind.
    public unsafe sealed partial class ManaWebGpuContext : IDisposable
    {
        private const int CascadeCount = 3;
        private const int ShadowMapResolution = 2048;
        private const float MaxShadowDistance = 300.0f;

        private readonly Matrix4x4[] _cascadeViewProjections = new Matrix4x4[CascadeCount];
        private readonly float[] _cascadeSplitFar = new float[CascadeCount];

        private Texture* _shadowMapTexture;
        private TextureView* _shadowMapArrayView;
        private readonly TextureView*[] _shadowCascadeRenderViews = new TextureView*[CascadeCount];
        private Sampler* _shadowSampler;

        private WebGpuBuffer* _shadowUniformBuffer;
        private BindGroupLayout* _shadowBindGroupLayout;
        private BindGroup* _shadowBindGroup;
        private PipelineLayout* _shadowPipelineLayout;
        private RenderPipeline* _shadowPipeline;

        [StructLayout(LayoutKind.Sequential)]
        private struct ManaWebGpuShadowUniform
        {
            public Matrix4x4 LightViewProjection;
        }

        // Texture array + sampler live independently of the shadow-pass pipeline itself: the frame bind
        // group (built in CreateSharedPipelineLayouts) needs the view+sampler before anything else, and
        // every pipeline that includes group 0 -- lit or not -- shares this one array.
        private void CreateShadowMapResources()
        {
            var shadowTextureDescriptor = new TextureDescriptor
            {
                Usage = TextureUsage.RenderAttachment | TextureUsage.TextureBinding,
                Dimension = TextureDimension.Dimension2D,
                Size = new Extent3D
                {
                    Width = ShadowMapResolution,
                    Height = ShadowMapResolution,
                    DepthOrArrayLayers = CascadeCount
                },
                Format = TextureFormat.Depth32float,
                MipLevelCount = 1,
                SampleCount = 1
            };

            _shadowMapTexture = _api.DeviceCreateTexture(_device, &shadowTextureDescriptor);
            if (_shadowMapTexture == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU shadow map texture.");
            }

            var arrayViewDescriptor = new TextureViewDescriptor
            {
                Format = TextureFormat.Depth32float,
                Dimension = TextureViewDimension.Dimension2DArray,
                BaseArrayLayer = 0,
                ArrayLayerCount = CascadeCount,
                MipLevelCount = 1,
                Aspect = TextureAspect.DepthOnly
            };
            _shadowMapArrayView = _api.TextureCreateView(_shadowMapTexture, &arrayViewDescriptor);
            if (_shadowMapArrayView == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU shadow map array view.");
            }

            for (var cascade = 0; cascade < CascadeCount; cascade++)
            {
                var layerViewDescriptor = new TextureViewDescriptor
                {
                    Format = TextureFormat.Depth32float,
                    Dimension = TextureViewDimension.Dimension2D,
                    BaseArrayLayer = (uint)cascade,
                    ArrayLayerCount = 1,
                    MipLevelCount = 1,
                    Aspect = TextureAspect.DepthOnly
                };
                var view = _api.TextureCreateView(_shadowMapTexture, &layerViewDescriptor);
                if (view == null)
                {
                    throw new InvalidOperationException($"Failed to create WebGPU shadow cascade {cascade} view.");
                }

                _shadowCascadeRenderViews[cascade] = view;
            }

            // Nearest, not linear: a filtering comparison sampler is inconsistently supported across wgpu's
            // backends, whereas nearest + the shader's own manual PCF tap (pbr_irradiance.wgsl) works
            // everywhere and is what actually softens the edge.
            var shadowSamplerDescriptor = new SamplerDescriptor
            {
                AddressModeU = AddressMode.ClampToEdge,
                AddressModeV = AddressMode.ClampToEdge,
                AddressModeW = AddressMode.ClampToEdge,
                MagFilter = FilterMode.Nearest,
                MinFilter = FilterMode.Nearest,
                MipmapFilter = MipmapFilterMode.Nearest,
                LodMinClamp = 0.0f,
                LodMaxClamp = 0.0f,
                Compare = CompareFunction.LessEqual,
                MaxAnisotropy = 1
            };
            _shadowSampler = _api.DeviceCreateSampler(_device, &shadowSamplerDescriptor);
            if (_shadowSampler == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU shadow comparison sampler.");
            }
        }

        // Called from CreateDemoMeshPipeline, after CreateSharedPipelineLayouts (needs _objectBindGroupLayout).
        private void CreateShadowPassResources()
        {
            var shadowUniformBufferDescriptor = new BufferDescriptor
            {
                Usage = BufferUsage.Uniform | BufferUsage.CopyDst,
                Size = (ulong)sizeof(ManaWebGpuShadowUniform)
            };
            _shadowUniformBuffer = _api.DeviceCreateBuffer(_device, &shadowUniformBufferDescriptor);
            if (_shadowUniformBuffer == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU shadow uniform buffer.");
            }

            var shadowBindGroupLayoutEntry = new BindGroupLayoutEntry
            {
                Binding = 0,
                Visibility = ShaderStage.Vertex,
                Buffer = new BufferBindingLayout
                {
                    Type = BufferBindingType.Uniform,
                    HasDynamicOffset = false,
                    MinBindingSize = (ulong)sizeof(ManaWebGpuShadowUniform)
                }
            };
            var shadowBindGroupLayoutDescriptor = new BindGroupLayoutDescriptor
            {
                EntryCount = 1,
                Entries = &shadowBindGroupLayoutEntry
            };
            _shadowBindGroupLayout = _api.DeviceCreateBindGroupLayout(_device, &shadowBindGroupLayoutDescriptor);
            if (_shadowBindGroupLayout == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU shadow bind group layout.");
            }

            var shadowBindGroupEntry = new BindGroupEntry
            {
                Binding = 0,
                Buffer = _shadowUniformBuffer,
                Offset = 0,
                Size = (ulong)sizeof(ManaWebGpuShadowUniform)
            };
            var shadowBindGroupDescriptor = new BindGroupDescriptor
            {
                Layout = _shadowBindGroupLayout,
                EntryCount = 1,
                Entries = &shadowBindGroupEntry
            };
            _shadowBindGroup = _api.DeviceCreateBindGroup(_device, &shadowBindGroupDescriptor);
            if (_shadowBindGroup == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU shadow bind group.");
            }

            // Group 1 reuses _objectBindGroupLayout (the ordinary per-mesh model/material bind group) so
            // every scene mesh's existing ObjectBindGroup works here unmodified -- shadow_depth.wgsl only
            // reads object.model out of it.
            var shadowPipelineLayoutGroups = stackalloc BindGroupLayout*[2];
            shadowPipelineLayoutGroups[0] = _shadowBindGroupLayout;
            shadowPipelineLayoutGroups[1] = _objectBindGroupLayout;
            var shadowPipelineLayoutDescriptor = new PipelineLayoutDescriptor
            {
                BindGroupLayoutCount = 2,
                BindGroupLayouts = shadowPipelineLayoutGroups
            };
            _shadowPipelineLayout = _api.DeviceCreatePipelineLayout(_device, &shadowPipelineLayoutDescriptor);
            if (_shadowPipelineLayout == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU shadow pipeline layout.");
            }

            _shadowPipeline = CreateShadowDepthPipeline();
        }

        private RenderPipeline* CreateShadowDepthPipeline()
        {
            var shaderModule = CreateShaderModule(Path.Combine("Assets", "Shaders", "WebGpu", "shadow_depth.wgsl"));
            _shaderModules["ShadowDepth"] = (nint)shaderModule;

            nint vertexEntryPoint = Marshal.StringToCoTaskMemUTF8("vs_main");
            try
            {
                var vertexAttribute = new VertexAttribute
                {
                    Format = VertexFormat.Float32x3,
                    Offset = 0,
                    ShaderLocation = 0
                };

                var vertexBufferLayout = new VertexBufferLayout
                {
                    ArrayStride = ManaWebGpuMesh.VertexStride,
                    StepMode = VertexStepMode.Vertex,
                    AttributeCount = 1,
                    Attributes = &vertexAttribute
                };

                var vertexState = new VertexState
                {
                    Module = shaderModule,
                    EntryPoint = (byte*)vertexEntryPoint,
                    BufferCount = 1,
                    Buffers = &vertexBufferLayout
                };

                var depthStencilState = new DepthStencilState
                {
                    Format = TextureFormat.Depth32float,
                    DepthWriteEnabled = true,
                    DepthCompare = CompareFunction.Less,
                    StencilFront = new StencilFaceState
                    {
                        Compare = CompareFunction.Always,
                        FailOp = StencilOperation.Keep,
                        DepthFailOp = StencilOperation.Keep,
                        PassOp = StencilOperation.Keep
                    },
                    StencilBack = new StencilFaceState
                    {
                        Compare = CompareFunction.Always,
                        FailOp = StencilOperation.Keep,
                        DepthFailOp = StencilOperation.Keep,
                        PassOp = StencilOperation.Keep
                    },
                    StencilReadMask = uint.MaxValue,
                    StencilWriteMask = uint.MaxValue,
                    // A little slope-scaled bias to fight shadow acne without introducing much peter-panning
                    // at the scale a voxel editor scene runs at (see MaxShadowDistance).
                    DepthBias = 2,
                    DepthBiasSlopeScale = 2.0f,
                    DepthBiasClamp = 0.0f
                };

                var pipelineDescriptor = new RenderPipelineDescriptor
                {
                    Layout = _shadowPipelineLayout,
                    Vertex = vertexState,
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
                        Mask = uint.MaxValue
                    },
                    DepthStencil = &depthStencilState,
                    Fragment = null
                };

                var pipeline = _api.DeviceCreateRenderPipeline(_device, &pipelineDescriptor);
                if (pipeline == null)
                {
                    throw new InvalidOperationException("Failed to create WebGPU shadow depth pipeline.");
                }

                return pipeline;
            }
            finally
            {
                Marshal.FreeCoTaskMem(vertexEntryPoint);
            }
        }

        // Recomputes the 3 cascade view-projection matrices from this frame's camera + sun direction. Cheap
        // (a handful of vector ops), so it runs unconditionally rather than only when the camera moved.
        private void UpdateShadowCascades()
        {
            if (_activeRenderScene == null || !_activeRenderScene.HasSun || !_activeRenderScene.SunCastsShadows)
            {
                return;
            }

            var camera = _activeRenderScene.Camera;
            if (!Matrix4x4.Invert(camera.View, out var inverseView))
            {
                return;
            }

            var projection = camera.Projection;
            var tanHalfFovY = MathF.Abs(1.0f / projection.M22);
            var tanHalfFovX = MathF.Abs(1.0f / projection.M11);

            var near = MathF.Max(camera.NearClipPlane, 0.05f);
            var far = MathF.Min(camera.FarClipPlane, MaxShadowDistance);
            if (far <= near)
            {
                far = near + 1.0f;
            }

            Span<float> splits = stackalloc float[CascadeCount];
            const float splitLambda = 0.6f;
            for (var i = 0; i < CascadeCount; i++)
            {
                var p = (i + 1) / (float)CascadeCount;
                var logSplit = near * MathF.Pow(far / near, p);
                var uniformSplit = near + (far - near) * p;
                splits[i] = splitLambda * logSplit + (1.0f - splitLambda) * uniformSplit;
            }

            var sunDirection = Vector3.Normalize(_activeRenderScene.SunDirection);
            var up = MathF.Abs(Vector3.Dot(sunDirection, Vector3.UnitY)) > 0.99f ? Vector3.UnitZ : Vector3.UnitY;

            Span<Vector3> corners = stackalloc Vector3[8];
            var cascadeNear = near;
            for (var i = 0; i < CascadeCount; i++)
            {
                var cascadeFar = splits[i];
                FillFrustumCorners(corners, inverseView, tanHalfFovX, tanHalfFovY, cascadeNear, cascadeFar);

                var center = Vector3.Zero;
                for (var c = 0; c < 8; c++)
                {
                    center += corners[c];
                }
                center /= 8.0f;

                var radius = 0.5f;
                for (var c = 0; c < 8; c++)
                {
                    radius = MathF.Max(radius, Vector3.Distance(center, corners[c]));
                }

                var lightEye = center - sunDirection * radius * 1.5f;
                var lightView = Matrix4x4.CreateLookAt(lightEye, center, up);
                var lightProjection = Matrix4x4.CreateOrthographic(radius * 2.0f, radius * 2.0f, 0.01f, radius * 3.0f);

                _cascadeViewProjections[i] = lightView * lightProjection;
                _cascadeSplitFar[i] = cascadeFar;

                cascadeNear = cascadeFar;
            }
        }

        // View-space frustum corners at the given near/far distance, reconstructed straight from the
        // projection's x/y scale terms (M11/M22) rather than by unprojecting NDC -- that way this does not
        // need to know the projection matrix's near/far-to-NDC-z convention at all, only that it is a
        // standard symmetric perspective matrix.
        private static void FillFrustumCorners(
            Span<Vector3> corners, Matrix4x4 inverseView, float tanHalfFovX, float tanHalfFovY, float near, float far)
        {
            var index = 0;
            for (var d = 0; d < 2; d++)
            {
                var depth = d == 0 ? near : far;
                var halfHeight = depth * tanHalfFovY;
                var halfWidth = depth * tanHalfFovX;
                for (var sx = -1; sx <= 1; sx += 2)
                {
                    for (var sy = -1; sy <= 1; sy += 2)
                    {
                        // The engine's view space looks down -Z (see ManaSceneNode.Forward).
                        var viewSpace = new Vector3(sx * halfWidth, sy * halfHeight, -depth);
                        corners[index++] = Vector3.Transform(viewSpace, inverseView);
                    }
                }
            }
        }

        // One depth-only pass per cascade, into that cascade's own layer of the shadow map array. Run
        // before the main scene pass begins (same commandEncoder, sequential passes) so the main pass can
        // bind the finished array for sampling. Only opaque meshes cast a shadow here -- see the class doc
        // comment.
        private void RenderShadowCascades(CommandEncoder* commandEncoder)
        {
            if (_activeRenderScene == null || !_activeRenderScene.HasSun || !_activeRenderScene.SunCastsShadows
                || _shadowPipeline == null || _shadowBindGroup == null || _shadowUniformBuffer == null)
            {
                return;
            }

            for (var cascade = 0; cascade < CascadeCount; cascade++)
            {
                var lightViewProjection = _cascadeViewProjections[cascade];
                _api.QueueWriteBuffer(_queue, _shadowUniformBuffer, 0, &lightViewProjection, (nuint)sizeof(Matrix4x4));

                var depthAttachment = new RenderPassDepthStencilAttachment
                {
                    View = _shadowCascadeRenderViews[cascade],
                    DepthLoadOp = LoadOp.Clear,
                    DepthStoreOp = StoreOp.Store,
                    DepthClearValue = 1.0f,
                    DepthReadOnly = false,
                    StencilLoadOp = LoadOp.Undefined,
                    StencilStoreOp = StoreOp.Undefined,
                    StencilClearValue = 0,
                    StencilReadOnly = true
                };

                var passDescriptor = new RenderPassDescriptor
                {
                    ColorAttachmentCount = 0,
                    ColorAttachments = null,
                    DepthStencilAttachment = &depthAttachment
                };

                var pass = _api.CommandEncoderBeginRenderPass(commandEncoder, &passDescriptor);
                _api.RenderPassEncoderSetPipeline(pass, _shadowPipeline);
                _api.RenderPassEncoderSetBindGroup(pass, 0, _shadowBindGroup, 0, null);

                foreach (var sceneMesh in _sceneMeshes)
                {
                    if (sceneMesh.Instance.IsTransparent)
                    {
                        continue;
                    }

                    // The main pass's own TryUpdateObjectUniform (DrawSceneMesh) has not run yet this frame
                    // -- this pass renders first -- so the object bind group's buffer would otherwise still
                    // hold last frame's transform. Cheap: it is a dirty-check against the same value the
                    // main pass will compare against right after.
                    sceneMesh.TryUpdateObjectUniform(_api, _queue, BuildObjectUniform(sceneMesh.Instance));

                    if (sceneMesh.ObjectBindGroup != null)
                    {
                        _api.RenderPassEncoderSetBindGroup(pass, 1, sceneMesh.ObjectBindGroup, 0, null);
                    }

                    sceneMesh.Mesh.Draw(pass);
                }

                _api.RenderPassEncoderEnd(pass);
            }
        }

        private void ReleaseShadowResources()
        {
            if (_shadowPipeline != null)
            {
                _api.RenderPipelineRelease(_shadowPipeline);
                _shadowPipeline = null;
            }

            if (_shadowPipelineLayout != null)
            {
                _api.PipelineLayoutRelease(_shadowPipelineLayout);
                _shadowPipelineLayout = null;
            }

            if (_shadowBindGroup != null)
            {
                _api.BindGroupRelease(_shadowBindGroup);
                _shadowBindGroup = null;
            }

            if (_shadowBindGroupLayout != null)
            {
                _api.BindGroupLayoutRelease(_shadowBindGroupLayout);
                _shadowBindGroupLayout = null;
            }

            if (_shadowUniformBuffer != null)
            {
                _api.BufferRelease(_shadowUniformBuffer);
                _shadowUniformBuffer = null;
            }

            if (_shadowSampler != null)
            {
                _api.SamplerRelease(_shadowSampler);
                _shadowSampler = null;
            }

            for (var cascade = 0; cascade < CascadeCount; cascade++)
            {
                if (_shadowCascadeRenderViews[cascade] != null)
                {
                    _api.TextureViewRelease(_shadowCascadeRenderViews[cascade]);
                    _shadowCascadeRenderViews[cascade] = null;
                }
            }

            if (_shadowMapArrayView != null)
            {
                _api.TextureViewRelease(_shadowMapArrayView);
                _shadowMapArrayView = null;
            }

            if (_shadowMapTexture != null)
            {
                _api.TextureRelease(_shadowMapTexture);
                _shadowMapTexture = null;
            }
        }
    }
}
