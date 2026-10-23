using System.Numerics;
using ManaCore.Graphics;
using Silk.NET.WebGPU;

namespace ManaGraphics.Code.Graphics.Resources
{
    // Cascaded shadow maps for the scene's directional sun: three cascades in one texture array, fit to the camera's
    // frustum every frame, sized in steps and snapped to whole texels so they hold still. Apart from ManaWebGpuShadowPass
    // because the frame bind group needs the map before any pipeline exists.
    internal sealed unsafe class ManaWebGpuShadowMap : ManaWebGpuDeviceObject
    {
        public const int CascadeCount = 3;
        // Per cascade. 4096 on desktop (three Depth32 layers, about 200 MB); the browser keeps 2048, where memory is tighter.
#if MANA_BROWSER
        private const int Resolution = 2048;
#else
        private const int Resolution = 4096;
#endif
        // Shadows reach this far from the camera. Three cascades over 300 units left each texel too coarse
        // for anything the size of a lamp post to cast a shadow worth the name.
        private const float MaxShadowDistance = 150.0f;

        // How far behind each cascade the light's box starts, so casters outside the view -- up a tall
        // tower, or behind the camera -- still shadow what is in it.
        private const float CasterReach = 200.0f;

        private Texture* _texture;
        private readonly TextureView*[] _cascadeViews = new TextureView*[CascadeCount];

        public ManaWebGpuShadowMap(ManaWebGpuDevice gpu)
            : base(gpu)
        {
            try
            {
                Create(gpu.Device);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public TextureView* ArrayView { get; private set; }
        public Sampler* Sampler { get; private set; }
        public Matrix4x4[] CascadeViewProjections { get; } = new Matrix4x4[CascadeCount];
        public float[] CascadeSplitFar { get; } = new float[CascadeCount];

        public TextureView* CascadeView(int cascade) => _cascadeViews[cascade];

        public static bool IsCasting(ManaRenderScene? scene) => scene is { HasSun: true, SunCastsShadows: true };

        /// <summary>Whether a mesh casts a sun shadow: the editor room and helpers do not, nor anything see-through.</summary>
        public static bool IsCaster(ManaRenderMeshInstance instance) =>
            !instance.IsTransparent && !instance.AlwaysOnTop && instance.IncludeInEnvironmentCapture
            && instance.ShaderName != "ManaGridShader";

        /// <summary>How far from the camera shadows reach this frame: <see cref="MaxShadowDistance"/>, or less in a small scene.</summary>
        public float ShadowDistance { get; private set; } = MaxShadowDistance;

        private void Create(Device* device)
        {
            var textureDescriptor = new TextureDescriptor
            {
                Usage = TextureUsage.RenderAttachment | TextureUsage.TextureBinding,
                Dimension = TextureDimension.Dimension2D,
                Size = new Extent3D
                {
                    Width = Resolution,
                    Height = Resolution,
                    DepthOrArrayLayers = CascadeCount
                },
                Format = TextureFormat.Depth32float,
                MipLevelCount = 1,
                SampleCount = 1
            };

            _texture = Api.DeviceCreateTexture(device, &textureDescriptor);
            if (_texture == null)
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
            ArrayView = Api.TextureCreateView(_texture, &arrayViewDescriptor);
            if (ArrayView == null)
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
                var view = Api.TextureCreateView(_texture, &layerViewDescriptor);
                if (view == null)
                {
                    throw new InvalidOperationException($"Failed to create WebGPU shadow cascade {cascade} view.");
                }

                _cascadeViews[cascade] = view;
            }

            // Nearest: filtering comparison samplers are unevenly supported; the shader does its own PCF.
            var samplerDescriptor = new SamplerDescriptor
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
            Sampler = Api.DeviceCreateSampler(device, &samplerDescriptor);
            if (Sampler == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU shadow comparison sampler.");
            }
        }

        // Recomputes the cascade view-projection matrices from this frame's camera and sun direction. Cheap
        // (a handful of vector ops), so it runs unconditionally rather than only when the camera moved.
        public void UpdateCascades(ManaRenderScene? scene)
        {
            if (scene == null || !IsCasting(scene))
            {
                return;
            }

            var camera = scene.Camera;
            if (!Matrix4x4.Invert(camera.View, out var inverseView))
            {
                return;
            }

            var projection = camera.Projection;
            var tanHalfFovY = MathF.Abs(1.0f / projection.M22);
            var tanHalfFovX = MathF.Abs(1.0f / projection.M11);

            var near = MathF.Max(camera.NearClipPlane, 0.05f);
            var sunDirection = Vector3.Normalize(scene.SunDirection);
            var up = MathF.Abs(Vector3.Dot(sunDirection, Vector3.UnitY)) > 0.99f ? Vector3.UnitZ : Vector3.UnitY;
            // The sun's view, turning only: x and y across the light, the same axes every cascade's box uses.
            var sunView = Matrix4x4.CreateLookAt(Vector3.Zero, sunDirection, up);
            Matrix4x4.Invert(sunView, out var sunToWorld);

            // Two things from the casters. How far the farthest is from the camera: past it (and the shadow it throws)
            // there is nothing to shadow, so a small scene spreads its cascades over what is there. And their footprint
            // seen from the sun: a shadow falls straight behind its caster, so nothing outside it is ever in shadow.
            var reach = 0.0f;
            var footprintMin = new Vector2(float.MaxValue);
            var footprintMax = new Vector2(float.MinValue);
            foreach (var mesh in scene.Meshes)
            {
                if (!IsCaster(mesh))
                {
                    continue;
                }
                var (min, max) = mesh.MeshData.WorldBounds(mesh.WorldTransform);
                var farthest = Vector3.Max(Vector3.Abs(min - camera.Position), Vector3.Abs(max - camera.Position));
                reach = MathF.Max(reach, farthest.Length());
                for (var corner = 0; corner < 8; corner++)
                {
                    var point = new Vector3((corner & 1) == 0 ? min.X : max.X, (corner & 2) == 0 ? min.Y : max.Y, (corner & 4) == 0 ? min.Z : max.Z);
                    var inSun = Vector3.Transform(point, sunView);
                    footprintMin = Vector2.Min(footprintMin, new Vector2(inSun.X, inSun.Y));
                    footprintMax = Vector2.Max(footprintMax, new Vector2(inSun.X, inSun.Y));
                }
            }
            var hasFootprint = footprintMin.X <= footprintMax.X;

            // Rounded up to whole steps of 8 m, so the cascades do not breathe as the camera moves.
            ShadowDistance = Math.Clamp(MathF.Ceiling((reach * 1.3f + 4.0f) / 8.0f) * 8.0f, 16.0f, MaxShadowDistance);
            var far = MathF.Min(camera.FarClipPlane, ShadowDistance);
            if (far <= near)
            {
                far = near + 1.0f;
            }

            Span<float> splits = stackalloc float[CascadeCount];
            // Mostly logarithmic, so the nearest cascade is a few metres deep and its texels a few millimetres
            // across; at 0.6 it reached 20 m and a shadow at your feet was drawn in blocks, at 0.9 still 5.7 m.
            const float splitLambda = 0.95f;
            for (var i = 0; i < CascadeCount; i++)
            {
                var p = (i + 1) / (float)CascadeCount;
                var logSplit = near * MathF.Pow(far / near, p);
                var uniformSplit = near + (far - near) * p;
                splits[i] = splitLambda * logSplit + (1.0f - splitLambda) * uniformSplit;
            }

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

                // The sphere round the slice, seen from the sun, cut down to the casters' footprint: a wide slice of
                // the view is mostly sky and empty ground, and its texels were spread over all of it.
                var middle = Vector3.Transform(center, sunView);
                var low = new Vector2(middle.X - radius, middle.Y - radius);
                var high = new Vector2(middle.X + radius, middle.Y + radius);
                if (hasFootprint)
                {
                    var cutLow = Vector2.Max(low, footprintMin);
                    var cutHigh = Vector2.Min(high, footprintMax);
                    if (cutLow.X < cutHigh.X && cutLow.Y < cutHigh.Y)
                    {
                        low = cutLow;
                        high = cutHigh;
                    }
                }

                // Stable cascades: each side in whole steps and the centre on a whole texel, so a moving camera does
                // not slide the texels under still shadows (they crawled and shimmered).
                var step = MathF.Pow(2.0f, MathF.Floor(MathF.Log2(radius))) / 8.0f;
                var width = MathF.Max(step, MathF.Ceiling((high.X - low.X) / step) * step);
                var height = MathF.Max(step, MathF.Ceiling((high.Y - low.Y) / step) * step);
                var texel = new Vector2(width, height) / Resolution;
                var boxCentre = (low + high) * 0.5f;
                boxCentre = new Vector2(MathF.Round(boxCentre.X / texel.X) * texel.X, MathF.Round(boxCentre.Y / texel.Y) * texel.Y);
                center = Vector3.Transform(new Vector3(boxCentre, middle.Z), sunToWorld);

                var lightEye = center - sunDirection * (radius + CasterReach);
                var lightView = Matrix4x4.CreateLookAt(lightEye, center, up);
                var lightProjection = Matrix4x4.CreateOrthographic(width, height, 0.01f, radius * 2.0f + CasterReach);

                CascadeViewProjections[i] = lightView * lightProjection;
                CascadeSplitFar[i] = cascadeFar;

                cascadeNear = cascadeFar;
            }
        }

        // Frustum corners from the projection's scale terms, so the depth convention does not matter.
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

        public override void Dispose()
        {
            if (Sampler != null)
            {
                Api.SamplerRelease(Sampler);
                Sampler = null;
            }

            for (var cascade = 0; cascade < CascadeCount; cascade++)
            {
                if (_cascadeViews[cascade] != null)
                {
                    Api.TextureViewRelease(_cascadeViews[cascade]);
                    _cascadeViews[cascade] = null;
                }
            }

            if (ArrayView != null)
            {
                Api.TextureViewRelease(ArrayView);
                ArrayView = null;
            }

            if (_texture != null)
            {
                Api.TextureRelease(_texture);
                _texture = null;
            }
        }
    }
}
