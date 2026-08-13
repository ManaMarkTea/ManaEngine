using System;
using System.Numerics;
using ManaUI;
using Silk.NET.OpenGL;

namespace Mana.Code.Graphics
{
    /// <summary>
    /// Draws a <see cref="ManaUiDrawList"/> with OpenGL. The counterpart to the WebGPU renderer, sharing
    /// its draw list, its instance layout and its SDF shading -- only the API differs.
    /// <para>
    /// Like the WebGPU path this is one instanced draw of a unit quad with no vertex or index buffer, and
    /// clipping happens in the fragment shader rather than through scissor rects, so a whole frame is a
    /// single draw call.
    /// </para>
    /// </summary>
    internal sealed unsafe class ManaOpenGlUiRenderer : IDisposable
    {
        // Must match sizeof(ManaUiInstance). A mismatch reads every instance at the wrong offset, which
        // produces a frame of noise rather than an error.
        private const int InstanceStride = 84;

        private const string VertexShaderSource = @"#version 330 core

layout(location = 0) in vec4 aRect;
layout(location = 1) in vec4 aUv;
layout(location = 2) in vec4 aCorners;
layout(location = 3) in vec4 aClip;
layout(location = 4) in uint aFillColor;
layout(location = 5) in uint aBorderColor;
layout(location = 6) in float aBorderWidth;
layout(location = 7) in uint aFlags;
layout(location = 8) in uint aGradientColor;

uniform vec2 uViewportSize;

// Whether the target re-encodes linear output back to sRGB on write.
//
// WebGPU presents to a Bgra8UnormSrgb surface, so its shader must hand over linear values and the hardware
// encodes them. The OpenGL default framebuffer has no such encoding: linear values written there are shown
// as-is, which darkens every midtone -- #2B2D30 would land at roughly (6, 7, 7). So this path passes the
// authored sRGB colours straight through, which is also what the ImGui GL backend does.
uniform bool uConvertToLinear;

out vec2 vLocal;
out vec2 vUv;
flat out vec4 vRect;
flat out vec4 vCorners;
flat out vec4 vClip;
flat out vec4 vFill;
flat out vec4 vBorder;
flat out float vBorderWidth;
flat out uint vFlags;
flat out vec4 vGradient;
flat out vec4 vSourceRect;

vec4 unpackRgba(uint value)
{
    return vec4(
        float(value & 0xFFu),
        float((value >> 8) & 0xFFu),
        float((value >> 16) & 0xFFu),
        float((value >> 24) & 0xFFu)) / 255.0;
}

float srgbToLinearChannel(float value)
{
    return value <= 0.04045 ? value / 12.92 : pow((value + 0.055) / 1.055, 2.4);
}

vec4 toTargetSpace(vec4 color)
{
    if (!uConvertToLinear)
    {
        return color;
    }

    return vec4(
        srgbToLinearChannel(color.r),
        srgbToLinearChannel(color.g),
        srgbToLinearChannel(color.b),
        color.a);
}

void main()
{
    // A unit quad as a triangle strip, straight from the vertex index; no vertex buffer involved.
    vec2 corner = vec2(float(gl_VertexID & 1), float((gl_VertexID >> 1) & 1));
    vec2 position = mix(aRect.xy, aRect.zw, corner);

    vec2 normalized = position / uViewportSize;
    gl_Position = vec4(normalized.x * 2.0 - 1.0, 1.0 - normalized.y * 2.0, 0.0, 1.0);

    vLocal = position;
    vUv = mix(aUv.xy, aUv.zw, corner);
    vRect = aRect;
    vCorners = aCorners;
    vClip = aClip;
    vFill = toTargetSpace(unpackRgba(aFillColor));
    vBorder = toTargetSpace(unpackRgba(aBorderColor));
    vBorderWidth = aBorderWidth;
    vGradient = toTargetSpace(unpackRgba(aGradientColor));
    vFlags = aFlags;
    vSourceRect = aUv;
}
";

        private const string FragmentShaderSource = @"#version 330 core

in vec2 vLocal;
in vec2 vUv;
flat in vec4 vRect;
flat in vec4 vCorners;
flat in vec4 vClip;
flat in vec4 vFill;
flat in vec4 vGradient;
flat in vec4 vBorder;
flat in float vBorderWidth;
flat in uint vFlags;
flat in vec4 vSourceRect;

uniform sampler2D uGlyphAtlas;
uniform sampler2D uImageAtlas;

out vec4 fragColor;

const uint FLAG_GLYPH = 1u;
const uint FLAG_TEXTURE = 2u;
const uint FLAG_SHADOW = 4u;
const uint FLAG_GRADIENT = 8u;

float cornerRadius(vec2 p, vec4 corners)
{
    if (p.y < 0.0)
    {
        return p.x > 0.0 ? corners.y : corners.x;
    }

    return p.x > 0.0 ? corners.z : corners.w;
}

float sdRoundedBox(vec2 p, vec2 halfSize, vec4 corners)
{
    float radius = min(cornerRadius(p, corners), min(halfSize.x, halfSize.y));
    vec2 q = abs(p) - halfSize + vec2(radius);
    return min(max(q.x, q.y), 0.0) + length(max(q, vec2(0.0))) - radius;
}

float coverage(float distance)
{
    float width = fwidth(distance);
    if (width <= 0.0)
    {
        return distance <= 0.0 ? 1.0 : 0.0;
    }

    return clamp(0.5 - distance / width, 0.0, 1.0);
}

void main()
{
    vec2 p = vLocal;
    if (p.x < vClip.x || p.y < vClip.y || p.x > vClip.z || p.y > vClip.w)
    {
        discard;
    }

    if ((vFlags & FLAG_GLYPH) != 0u)
    {
        float glyphCoverage = texture(uGlyphAtlas, vUv).r;
        float alpha = vFill.a * glyphCoverage;
        if (alpha <= 0.0)
        {
            discard;
        }

        fragColor = vec4(vFill.rgb * alpha, alpha);
        return;
    }

    vec2 centre = (vRect.xy + vRect.zw) * 0.5;
    vec2 halfSize = (vRect.zw - vRect.xy) * 0.5;

    if ((vFlags & FLAG_SHADOW) != 0u)
    {
        vec2 sourceCentre = (vSourceRect.xy + vSourceRect.zw) * 0.5;
        vec2 sourceHalf = (vSourceRect.zw - vSourceRect.xy) * 0.5;
        float distance = sdRoundedBox(p - sourceCentre, sourceHalf, vCorners);
        float falloff = 1.0 - smoothstep(0.0, max(vBorderWidth, 0.0001), distance);
        float alpha = vFill.a * falloff * falloff;
        if (alpha <= 0.0)
        {
            discard;
        }

        fragColor = vec4(vFill.rgb * alpha, alpha);
        return;
    }

    float distance = sdRoundedBox(p - centre, halfSize, vCorners);
    float outer = coverage(distance);
    if (outer <= 0.0)
    {
        discard;
    }

    // Interpolated down the box, in whatever space this target wants -- the colours were converted on
    // the way in, so mixing here matches what the WebGPU path does.
    vec4 fill = vFill;
    if ((vFlags & FLAG_GRADIENT) != 0u)
    {
        float height = max(vRect.w - vRect.y, 0.0001);
        fill = mix(vFill, vGradient, clamp((p.y - vRect.y) / height, 0.0, 1.0));
    }

    if ((vFlags & FLAG_TEXTURE) != 0u)
    {
        fill *= texture(uImageAtlas, vUv);
    }

    vec3 rgb;
    float alpha;

    if (vBorderWidth > 0.0)
    {
        float inner = coverage(distance + vBorderWidth);
        float ring = max(outer - inner, 0.0);
        rgb = fill.rgb * fill.a * inner + vBorder.rgb * vBorder.a * ring;
        alpha = fill.a * inner + vBorder.a * ring;
    }
    else
    {
        alpha = fill.a * outer;
        rgb = fill.rgb * alpha;
    }

    if (alpha <= 0.0)
    {
        discard;
    }

    fragColor = vec4(rgb, alpha);
}
";

        private readonly GL _gl;

        private uint _program;
        private uint _vertexArray;
        private uint _instanceBuffer;
        private uint _glyphTexture;
        private uint _imageTexture;

        private int _viewportUniform;
        private int _glyphAtlasUniform;
        private int _imageAtlasUniform;
        private int _convertToLinearUniform;

        /// <summary>
        /// True only when the target re-encodes to sRGB on write. The GL default framebuffer does not, so
        /// this stays false; it exists so the difference from the WebGPU path is explicit rather than an
        /// unexplained divergence between two otherwise identical shaders.
        /// </summary>
        public bool ConvertToLinear { get; set; }

        private int _instanceBufferCapacity;
        private int _glyphAtlasWidth;
        private int _glyphAtlasHeight;
        private Vector2 _lastLogicalSize;
        private bool _loggedFirstFrame;
        private bool _disposed;

        public ManaOpenGlUiRenderer(GL gl)
        {
            _gl = gl;

            _program = CreateProgram();
            _viewportUniform = _gl.GetUniformLocation(_program, "uViewportSize");
            _glyphAtlasUniform = _gl.GetUniformLocation(_program, "uGlyphAtlas");
            _imageAtlasUniform = _gl.GetUniformLocation(_program, "uImageAtlas");
            _convertToLinearUniform = _gl.GetUniformLocation(_program, "uConvertToLinear");

            CreateBuffers();
            CreateImagePlaceholder();
        }

        public void Render(ManaUiDrawList drawList, ManaGlyphAtlas atlas, Vector2 logicalSize, Vector2 framebufferSize)
        {
            if (_disposed || drawList.InstanceCount == 0 || logicalSize.X <= 0.0f || logicalSize.Y <= 0.0f)
            {
                return;
            }

            _lastLogicalSize = logicalSize;

            SyncGlyphAtlas(atlas);
            UploadInstances(drawList);

            // OpenGL state is global and this pass shares it with the scene and with ImGui. Captured and
            // put back afterwards rather than assumed: leaving blending on, or the depth mask off, would
            // surface as an inexplicable bug in next frame's scene pass rather than here.
            var hadBlend = _gl.IsEnabled(EnableCap.Blend);
            var hadDepthTest = _gl.IsEnabled(EnableCap.DepthTest);
            var hadCullFace = _gl.IsEnabled(EnableCap.CullFace);
            var hadScissor = _gl.IsEnabled(EnableCap.ScissorTest);
            _gl.GetInteger(GetPName.DepthWritemask, out var hadDepthMask);

            // Depth testing would bury the UI under the scene, and ImGui sets a scissor rect per draw
            // command that would otherwise clip the whole UI to its last widget -- the same trap the
            // WebGPU renderer has to step around.
            _gl.Disable(EnableCap.DepthTest);
            _gl.DepthMask(false);
            _gl.Disable(EnableCap.CullFace);
            _gl.Disable(EnableCap.ScissorTest);
            _gl.Disable(EnableCap.StencilTest);

            _gl.Enable(EnableCap.Blend);
            _gl.BlendEquation(GLEnum.FuncAdd);

            // Premultiplied alpha, matching what the shader writes.
            _gl.BlendFuncSeparate(
                BlendingFactor.One,
                BlendingFactor.OneMinusSrcAlpha,
                BlendingFactor.One,
                BlendingFactor.OneMinusSrcAlpha);

            _gl.Viewport(0, 0, (uint)MathF.Max(1.0f, framebufferSize.X), (uint)MathF.Max(1.0f, framebufferSize.Y));

            _gl.UseProgram(_program);
            _gl.Uniform2(_viewportUniform, logicalSize.X, logicalSize.Y);
            _gl.Uniform1(_convertToLinearUniform, ConvertToLinear ? 1 : 0);

            _gl.ActiveTexture(TextureUnit.Texture0);
            _gl.BindTexture(TextureTarget.Texture2D, _glyphTexture);
            _gl.Uniform1(_glyphAtlasUniform, 0);

            _gl.ActiveTexture(TextureUnit.Texture1);
            _gl.BindTexture(TextureTarget.Texture2D, _imageTexture);
            _gl.Uniform1(_imageAtlasUniform, 1);

            _gl.BindVertexArray(_vertexArray);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _instanceBuffer);

            foreach (var batch in drawList.Batches)
            {
                // The batch's starting instance is baked into the attribute offsets rather than passed as
                // a base instance: glDrawArraysInstancedBaseInstance needs GL 4.2, and this backend targets
                // 3.3 core. Re-specifying eight pointers costs nothing at one batch per frame, which is
                // what a UI without custom textures produces.
                ConfigureAttributes(batch.InstanceOffset * InstanceStride);

                _gl.DrawArraysInstanced(PrimitiveType.TriangleStrip, 0, 4, (uint)batch.InstanceCount);
            }

            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);

            _gl.BindVertexArray(0);
            _gl.UseProgram(0);

            if (!_loggedFirstFrame)
            {
                _loggedFirstFrame = true;
                LogSampledPixel(drawList, framebufferSize);

                // Reported once, with the GL error state, because a silently failing draw looks exactly
                // like a UI that simply has nothing to show.
                var error = _gl.GetError();
                ManaCore.Diagnostics.ManaLog.WriteDiagnostic(
                    $"[ManaUI/GL] first frame: {drawList.InstanceCount} instances in {drawList.Batches.Count} "
                    + $"batch(es), logical={logicalSize.X}x{logicalSize.Y}, atlas={atlas.Width}x{atlas.Height}, "
                    + $"glError={error}");
            }

            SetEnabled(EnableCap.Blend, hadBlend);
            SetEnabled(EnableCap.DepthTest, hadDepthTest);
            SetEnabled(EnableCap.CullFace, hadCullFace);
            SetEnabled(EnableCap.ScissorTest, hadScissor);
            _gl.DepthMask(hadDepthMask != 0);
            _gl.ActiveTexture(TextureUnit.Texture0);
        }

        /// <summary>
        /// Reads back the centre of the first opaque box drawn and logs it against the colour that was
        /// asked for. Colour space bugs are invisible to every other kind of check -- the UI still draws,
        /// still reports no GL error, and simply comes out the wrong shade -- so this measures the one
        /// thing that actually distinguishes them.
        /// </summary>
        private void LogSampledPixel(ManaUiDrawList drawList, Vector2 framebufferSize)
        {
            ManaUiInstance sample = default;
            var found = false;

            foreach (var instance in drawList.Instances)
            {
                // A large, fully opaque, untextured box: a panel background rather than a glyph or a
                // shadow, whose blended edges would not compare cleanly.
                var opaque = (instance.FillColor >> 24) == 0xFF;
                var plain = instance.Flags == 0;
                var wide = instance.Rect.Z - instance.Rect.X > 40.0f && instance.Rect.W - instance.Rect.Y > 40.0f;

                if (opaque && plain && wide)
                {
                    sample = instance;
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                return;
            }

            var centre = new Vector2((sample.Rect.X + sample.Rect.Z) * 0.5f, (sample.Rect.Y + sample.Rect.W) * 0.5f);

            // Logical to framebuffer pixels, then flipped: GL reads with the origin at the bottom left.
            var scale = framebufferSize.Y / MathF.Max(1.0f, _lastLogicalSize.Y);
            var x = (int)(centre.X * scale);
            var y = (int)(framebufferSize.Y - centre.Y * scale);

            if (x < 0 || y < 0 || x >= framebufferSize.X || y >= framebufferSize.Y)
            {
                return;
            }

            var pixel = stackalloc byte[4];
            _gl.PixelStore(PixelStoreParameter.PackAlignment, 1);
            _gl.ReadPixels(x, y, 1, 1, PixelFormat.Rgba, PixelType.UnsignedByte, pixel);

            var expected = UiColor.Unpack(sample.FillColor);
            ManaCore.Diagnostics.ManaLog.WriteDiagnostic(
                $"[ManaUI/GL] colour check at ({x},{y}): expected sRGB "
                + $"({(int)(expected.X * 255)},{(int)(expected.Y * 255)},{(int)(expected.Z * 255)}), "
                + $"read ({pixel[0]},{pixel[1]},{pixel[2]}), convertToLinear={ConvertToLinear}");
        }

        private void SetEnabled(EnableCap capability, bool enabled)
        {
            if (enabled)
            {
                _gl.Enable(capability);
            }
            else
            {
                _gl.Disable(capability);
            }
        }

        // ----- resources ------------------------------------------------------------------------------

        private uint CreateProgram()
        {
            var vertex = CompileShader(ShaderType.VertexShader, VertexShaderSource);
            var fragment = CompileShader(ShaderType.FragmentShader, FragmentShaderSource);

            var program = _gl.CreateProgram();
            _gl.AttachShader(program, vertex);
            _gl.AttachShader(program, fragment);
            _gl.LinkProgram(program);

            _gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out var linked);
            if (linked == 0)
            {
                var log = _gl.GetProgramInfoLog(program);
                throw new InvalidOperationException($"ManaUI OpenGL program failed to link: {log}");
            }

            _gl.DetachShader(program, vertex);
            _gl.DetachShader(program, fragment);
            _gl.DeleteShader(vertex);
            _gl.DeleteShader(fragment);
            return program;
        }

        private uint CompileShader(ShaderType type, string source)
        {
            var shader = _gl.CreateShader(type);
            _gl.ShaderSource(shader, source);
            _gl.CompileShader(shader);

            _gl.GetShader(shader, ShaderParameterName.CompileStatus, out var compiled);
            if (compiled == 0)
            {
                var log = _gl.GetShaderInfoLog(shader);
                _gl.DeleteShader(shader);
                throw new InvalidOperationException($"ManaUI {type} failed to compile: {log}");
            }

            return shader;
        }

        private void CreateBuffers()
        {
            _vertexArray = _gl.GenVertexArray();
            _instanceBuffer = _gl.GenBuffer();

            _gl.BindVertexArray(_vertexArray);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _instanceBuffer);

            ConfigureAttributes(0);

            _gl.BindVertexArray(0);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
        }

        /// <summary>
        /// Points every attribute at the instance stream, starting <paramref name="baseOffset"/> bytes in.
        /// Every attribute advances once per instance rather than per vertex, which is what lets four
        /// vertices with no buffer of their own draw an arbitrary number of boxes.
        /// </summary>
        private void ConfigureAttributes(int baseOffset)
        {
            SetFloatAttribute(0, 4, baseOffset + 0);
            SetFloatAttribute(1, 4, baseOffset + 16);
            SetFloatAttribute(2, 4, baseOffset + 32);
            SetFloatAttribute(3, 4, baseOffset + 48);
            SetIntegerAttribute(4, baseOffset + 64);
            SetIntegerAttribute(5, baseOffset + 68);
            SetFloatAttribute(6, 1, baseOffset + 72);
            SetIntegerAttribute(7, baseOffset + 76);
            SetIntegerAttribute(8, baseOffset + 80);
        }

        private void SetFloatAttribute(uint index, int components, int offset)
        {
            _gl.EnableVertexAttribArray(index);
            _gl.VertexAttribPointer(index, components, VertexAttribPointerType.Float, false, InstanceStride, (void*)offset);
            _gl.VertexAttribDivisor(index, 1);
        }

        /// <summary>
        /// Integer attributes need VertexAttribIPointer. Routing them through the float path would convert
        /// the packed colours and flags to floating point and quietly corrupt them.
        /// </summary>
        private void SetIntegerAttribute(uint index, int offset)
        {
            _gl.EnableVertexAttribArray(index);
            _gl.VertexAttribIPointer(index, 1, VertexAttribIType.UnsignedInt, InstanceStride, (void*)offset);
            _gl.VertexAttribDivisor(index, 1);
        }

        private void UploadInstances(ManaUiDrawList drawList)
        {
            var required = drawList.InstanceCount * InstanceStride;
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _instanceBuffer);

            var instances = drawList.Instances;
            fixed (ManaUiInstance* data = instances)
            {
                if (required > _instanceBufferCapacity)
                {
                    // Grown with headroom so a UI that adds a few elements per frame is not reallocating
                    // every frame.
                    _instanceBufferCapacity = Math.Max(required + required / 2, 64 * InstanceStride);
                    _gl.BufferData(
                        BufferTargetARB.ArrayBuffer,
                        (nuint)_instanceBufferCapacity,
                        null,
                        BufferUsageARB.StreamDraw);
                }

                _gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, (nuint)required, data);
            }

            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
        }

        private void SyncGlyphAtlas(ManaGlyphAtlas atlas)
        {
            if (_glyphTexture == 0 || _glyphAtlasWidth != atlas.Width || _glyphAtlasHeight != atlas.Height)
            {
                if (_glyphTexture != 0)
                {
                    _gl.DeleteTexture(_glyphTexture);
                }

                _glyphTexture = CreateGlyphTexture(atlas.Width, atlas.Height);
                _glyphAtlasWidth = atlas.Width;
                _glyphAtlasHeight = atlas.Height;

                UploadGlyphRows(atlas, 0, atlas.Height);
                atlas.ClearDirty();
                return;
            }

            if (!atlas.IsDirty)
            {
                return;
            }

            var (_, minY, _, maxY) = atlas.DirtyRegion;
            UploadGlyphRows(atlas, minY, maxY);
            atlas.ClearDirty();
        }

        private uint CreateGlyphTexture(int width, int height)
        {
            var texture = _gl.GenTexture();
            _gl.BindTexture(TextureTarget.Texture2D, texture);

            _gl.TexImage2D(
                TextureTarget.Texture2D,
                0,
                InternalFormat.R8,
                (uint)width,
                (uint)height,
                0,
                PixelFormat.Red,
                PixelType.UnsignedByte,
                null);

            SetTextureParameters();
            return texture;
        }

        private void SetTextureParameters()
        {
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
        }

        private void UploadGlyphRows(ManaGlyphAtlas atlas, int startRow, int endRow)
        {
            startRow = Math.Clamp(startRow, 0, atlas.Height);
            endRow = Math.Clamp(endRow, startRow, atlas.Height);

            var rowCount = endRow - startRow;
            if (rowCount <= 0)
            {
                return;
            }

            _gl.BindTexture(TextureTarget.Texture2D, _glyphTexture);

            // The atlas is single-channel and tightly packed, so the default 4-byte row alignment would
            // shear every upload whose width is not a multiple of four.
            _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);

            fixed (byte* pixels = &atlas.Pixels[startRow * atlas.Width])
            {
                _gl.TexSubImage2D(
                    TextureTarget.Texture2D,
                    0,
                    0,
                    startRow,
                    (uint)atlas.Width,
                    (uint)rowCount,
                    PixelFormat.Red,
                    PixelType.UnsignedByte,
                    pixels);
            }
        }

        private void CreateImagePlaceholder()
        {
            _imageTexture = _gl.GenTexture();
            _gl.BindTexture(TextureTarget.Texture2D, _imageTexture);

            var white = stackalloc byte[4] { 255, 255, 255, 255 };
            _gl.TexImage2D(
                TextureTarget.Texture2D,
                0,
                InternalFormat.Rgba8,
                1,
                1,
                0,
                PixelFormat.Rgba,
                PixelType.UnsignedByte,
                white);

            SetTextureParameters();
            _gl.BindTexture(TextureTarget.Texture2D, 0);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_instanceBuffer != 0)
            {
                _gl.DeleteBuffer(_instanceBuffer);
            }

            if (_vertexArray != 0)
            {
                _gl.DeleteVertexArray(_vertexArray);
            }

            if (_glyphTexture != 0)
            {
                _gl.DeleteTexture(_glyphTexture);
            }

            if (_imageTexture != 0)
            {
                _gl.DeleteTexture(_imageTexture);
            }

            if (_program != 0)
            {
                _gl.DeleteProgram(_program);
            }
        }
    }
}
