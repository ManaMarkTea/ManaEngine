using ManaCore.Graphics;
using ManaCore.Imaging;

namespace ManaVoxel.Code.Assets
{
    /// <summary>
    /// Draws a small shaded preview of a mesh on the CPU.
    /// <para>
    /// On the CPU, and not through the renderer, on purpose. A thumbnail needs no pipeline, no bind group and
    /// no render pass -- it needs a few thousand triangles projected into a tiny square, which is less
    /// work than the GPU path's setup would be. More importantly it can run on a background thread, which is
    /// what lets a folder of a thousand assets fill in gradually instead of stalling the frame that opened
    /// it; a render-to-texture path would have to be marshalled onto the render thread and read back, one
    /// frame at a time. Decoding a texture here is still CPU-only work -- ImageSharp never touches a
    /// graphics device -- so that constraint holds even now that this reads real material colour.
    /// </para>
    /// <para>
    /// Being pure -- geometry in, pixels out, no graphics device anywhere -- it is also checkable headlessly,
    /// which matters for something whose output nobody reads character by character.
    /// </para>
    /// </summary>
    public static class MeshThumbnailRasterizer
    {
        private const int BytesPerPixel = 4;

        // Same fixed top-down isometric angle used by the WebGPU thumbnail renderer. The extra Z flip keeps
        // this CPU rasterizer's older depth convention: lower Z is nearer, larger Z is farther away.
        private static readonly Vector3 ViewForwardWorld = Vector3.Normalize(new Vector3(-1.0f, -1.35f, -1.0f));
        private static readonly Vector3 ViewUpWorld = Vector3.UnitY;
        private static readonly Matrix4x4 ViewTransform = CreateViewTransform();

        /// <summary>
        /// Where the light comes from, in view space. Above, in front and to the left: the standard
        /// three-quarter key light, which is what makes a silhouette read as a solid rather than a blob.
        /// </summary>
        private static readonly Vector3 LightDirection = Vector3.Normalize(Vector3.TransformNormal(
            Vector3.Normalize(new Vector3(-0.45f, 0.85f, 0.35f)),
            ViewTransform));

        /// <summary>Floor under the shading, so a face turned away is dim rather than black.</summary>
        private const float AmbientLight = 0.28f;

        /// <summary>
        /// Fraction of the tile left empty around the model. Without it the silhouette touches the edges and
        /// a grid of thumbnails reads as a grid of rectangles.
        /// </summary>
        private const float Inset = 0.1f;

        /// <summary>
        /// A textured fragment whose sampled alpha falls below this is treated as not there at all: no
        /// colour written, no depth written. Matches glTF's own default MASK cutoff.
        /// <para>
        /// A texture is usually a shared sheet -- many sprites packed into one image, with transparent
        /// padding between them, and PNG encoders routinely zero out the RGB of a fully transparent pixel to
        /// compress better. Without this, a triangle whose UVs land on that padding (leaf cards, hair cards,
        /// any cutout foliage) painted a solid black rectangle -- and because that rectangle was opaque as
        /// far as the depth buffer knew, it also wrongly won against whatever should have shown through the
        /// gaps, which is what made the depth test look broken on top of looking black.
        /// </para>
        /// </summary>
        private const float AlphaCutoff = 0.5f;

        /// <summary>
        /// Edge length a source texture is downsized to before sampling. A character sheet can be a few
        /// thousand pixels square; sampling that directly at thumbnail resolution would alias into near-random
        /// noise, since each output pixel would land on one arbitrary source texel rather than an average of
        /// the many it actually covers. Resizing once with a proper filter does that averaging up front,
        /// which is also far cheaper than filtering per pixel during rasterization.
        /// </summary>
        private const int TextureSampleSize = 48;

        /// <summary>
        /// Renders <paramref name="mesh"/> to a straight-alpha RGBA buffer <paramref name="size"/> square, or
        /// returns null if there is nothing to draw. Transparent where the model is not.
        /// <para>
        /// The colour at each pixel comes from whatever the mesh actually carries, in order: a sampled base
        /// colour texture, then per-vertex colour, then the material's flat base colour factor, then
        /// <paramref name="fallbackTint"/> for a mesh with none of the above. A base colour factor multiplies
        /// whichever tier supplied the colour, matching how a factor and a texture combine in glTF.
        /// </para>
        /// </summary>
        public static byte[]? Render(ManaMeshCpuData mesh, int size, Vector4 fallbackTint)
        {
            if (size <= 0 || mesh.Positions.Length == 0 || mesh.Indexes.Length < 3)
            {
                return null;
            }

            var view = ViewTransform;

            var (centre, radius) = BoundingSphere(mesh.Positions);
            if (radius <= 0.0f)
            {
                return null;
            }

            // Orthographic, fitted to the bounding sphere: a perspective projection would need a distance
            // chosen per model, and at this size the difference is invisible anyway.
            var half = radius / (1.0f - (Inset * 2.0f));
            var scale = size / (half * 2.0f);
            var pixels = new byte[size * size * BytesPerPixel];
            var depth = new float[size * size];
            Array.Fill(depth, float.MaxValue);

            var indexes = mesh.Indexes;
            var positions = mesh.Positions;
            var normals = mesh.Normals.Length == positions.Length ? mesh.Normals : null;
            var vertexColors = mesh.Colors.Length == positions.Length ? mesh.Colors : null;
            var uvs = mesh.TextureCoordinates0.Length == positions.Length ? mesh.TextureCoordinates0 : null;

            var texture = uvs != null ? DecodeTexture(mesh.AlbedoTextureBytes) : null;
            var factor = mesh.AlbedoFactor;

            for (var i = 0; i + 2 < indexes.Length; i += 3)
            {
                var ia = indexes[i];
                var ib = indexes[i + 1];
                var ic = indexes[i + 2];

                if (ia >= positions.Length || ib >= positions.Length || ic >= positions.Length)
                {
                    continue;
                }

                // View-space first, screen second: the fallback face normal below needs the real 3D
                // triangle, not the screen-projected one -- see ToScreen's note on why.
                var va = Vector3.Transform(positions[ia] - centre, view);
                var vb = Vector3.Transform(positions[ib] - centre, view);
                var vc = Vector3.Transform(positions[ic] - centre, view);

                var a = ToScreen(va, scale, size);
                var b = ToScreen(vb, scale, size);
                var c = ToScreen(vc, scale, size);

                // A face normal from the triangle itself, so a mesh that shipped without normals still
                // shades. Flat rather than smooth, which at thumbnail size is indistinguishable.
                var normal = normals != null
                    ? Vector3.TransformNormal(normals[ia] + normals[ib] + normals[ic], view)
                    : Vector3.Cross(vb - va, vc - va);

                var shade = Shade(normal);

                if (texture != null && uvs != null)
                {
                    // The factor multiplies the sampled texel here, exactly like a glTF baseColorFactor does
                    // -- defaulting to white (a no-op) when the material has none.
                    FillTriangle(
                        pixels, depth, size, a, b, c, shade,
                        factor ?? Vector4.One, texture, uvs[ia], uvs[ib], uvs[ic]);
                    continue;
                }

                // No texture to sample, so the whole triangle is one flat colour: vertex colour if the mesh
                // carries any (averaged across the triangle -- flat shading, same call as the normal above),
                // else the material's factor, else the caller's fallback.
                var flatColor = vertexColors != null
                    ? (vertexColors[ia] + vertexColors[ib] + vertexColors[ic]) / 3.0f
                    : factor ?? fallbackTint;

                FillTriangle(pixels, depth, size, a, b, c, shade, flatColor, texture: null, default, default, default);
            }

            return pixels;
        }

        private static Matrix4x4 CreateViewTransform()
        {
            var view = Matrix4x4.CreateLookAt(-ViewForwardWorld, Vector3.Zero, ViewUpWorld);
            return view * Matrix4x4.CreateScale(1.0f, 1.0f, -1.0f);
        }

        /// <summary>
        /// Decodes and downsizes a texture's raw bytes, or returns null for anything missing or unreadable --
        /// a texture that fails to decode should cost the preview its colour, not the whole preview.
        /// </summary>
        private static ManaImage? DecodeTexture(byte[]? textureBytes)
        {
            if (textureBytes == null || textureBytes.Length == 0)
            {
                return null;
            }

            try
            {
                var image = ManaImageCodec.Load(textureBytes);

                // Flipped for the same reason ManaWebGpuMeshTextureCache flips every texture it uploads:
                // the loaders store V already flipped (see ManaSharpGltfLoader's uv0 comment), so a
                // consumer that samples the image the way it was decoded reads the wrong row. Without
                // this, a Kenney model's UVs -- which sit in the top fifth of its colormap -- landed in
                // the opaque black band at the bottom instead, and every thumbnail drew as a silhouette.
                return image.FlipVertical().Resized(TextureSampleSize, TextureSampleSize);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                return null;
            }
        }

        private static Vector3 ToScreen(Vector3 viewPoint, float scale, int size)
        {
            var half = size * 0.5f;

            // Y is flipped: model space is Y-up, an image is row-major from the top.
            return new Vector3(
                half + (viewPoint.X * scale),
                half - (viewPoint.Y * scale),
                viewPoint.Z);
        }

        private static float Shade(Vector3 normal)
        {
            if (normal.LengthSquared() <= 1e-12f)
            {
                return AmbientLight;
            }

            normal = Vector3.Normalize(normal);

            // Flipped to face the camera rather than trusted as-is -- a mesh with inconsistent winding, or
            // the cross-product fallback above (which inherits whatever winding the source triangle
            // happened to have), would otherwise hand back a normal for the far side of the surface you are
            // actually looking at. The depth test already guarantees this is the nearest triangle at this
            // pixel, i.e. the one facing the viewer, so its shading normal always should too.
            //
            // This used to be `MathF.Abs(lambert)` instead: same fix for the winding problem, but it also
            // erased which way the surface faced the light, so a face angled away lit up exactly as bright
            // as one facing it. On a convex bump that makes the shadowed far side glow like the near side --
            // the bump reads as a dent. Clamping the correctly-oriented normal fixes both: consistent
            // winding no longer matters, and a face pointing away from the light actually goes dark.
            if (normal.Z > 0.0f)
            {
                normal = -normal;
            }

            var lambert = Vector3.Dot(normal, LightDirection);
            var linear = AmbientLight + ((1.0f - AmbientLight) * Math.Clamp(lambert, 0.0f, 1.0f));

            // The GPU thumbnail lights in linear space and writes to an sRGB target, so the light multiplies the
            // colour as shade^(1/2.2) on the stored values. Applied straight, the same shade came out far darker.
            return MathF.Pow(linear, 1.0f / 2.2f);
        }

        private static (Vector3 Centre, float Radius) BoundingSphere(Vector3[] positions)
        {
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);

            foreach (var position in positions)
            {
                min = Vector3.Min(min, position);
                max = Vector3.Max(max, position);
            }

            var centre = (min + max) * 0.5f;

            // Half the diagonal, not the longest axis: fitting the box's extent alone lets a rotated model's
            // corner poke outside the tile.
            return (centre, Vector3.Distance(min, max) * 0.5f);
        }

        /// <summary>
        /// Solid triangle with a depth test. Scanline over the bounding box using barycentric coverage --
        /// enough for a thumbnail, and it needs no edge-walking bookkeeping to get right.
        /// <para>
        /// <paramref name="texture"/> null means <paramref name="colorOrFactor"/> is the whole triangle's flat
        /// colour; given, it is a multiplier and the per-pixel colour comes from sampling the texture at the
        /// barycentric-interpolated UV.
        /// </para>
        /// </summary>
        private static void FillTriangle(
            byte[] pixels,
            float[] depth,
            int size,
            Vector3 a,
            Vector3 b,
            Vector3 c,
            float shade,
            Vector4 colorOrFactor,
            ManaImage? texture,
            Vector2 uvA,
            Vector2 uvB,
            Vector2 uvC)
        {
            var minX = Math.Max(0, (int)MathF.Floor(MathF.Min(a.X, MathF.Min(b.X, c.X))));
            var maxX = Math.Min(size - 1, (int)MathF.Ceiling(MathF.Max(a.X, MathF.Max(b.X, c.X))));
            var minY = Math.Max(0, (int)MathF.Floor(MathF.Min(a.Y, MathF.Min(b.Y, c.Y))));
            var maxY = Math.Min(size - 1, (int)MathF.Ceiling(MathF.Max(a.Y, MathF.Max(b.Y, c.Y))));

            if (minX > maxX || minY > maxY)
            {
                return;
            }

            var area = ((b.X - a.X) * (c.Y - a.Y)) - ((b.Y - a.Y) * (c.X - a.X));
            if (MathF.Abs(area) < 1e-9f)
            {
                return;
            }

            var inverseArea = 1.0f / area;

            // Constant across the triangle when there is no texture to sample per pixel.
            byte flatRed = 0, flatGreen = 0, flatBlue = 0;
            if (texture == null)
            {
                flatRed = (byte)Math.Clamp(colorOrFactor.X * shade * 255.0f, 0.0f, 255.0f);
                flatGreen = (byte)Math.Clamp(colorOrFactor.Y * shade * 255.0f, 0.0f, 255.0f);
                flatBlue = (byte)Math.Clamp(colorOrFactor.Z * shade * 255.0f, 0.0f, 255.0f);
            }

            for (var y = minY; y <= maxY; y++)
            {
                for (var x = minX; x <= maxX; x++)
                {
                    var px = x + 0.5f;
                    var py = y + 0.5f;

                    var w0 = (((b.X - px) * (c.Y - py)) - ((b.Y - py) * (c.X - px))) * inverseArea;
                    var w1 = (((c.X - px) * (a.Y - py)) - ((c.Y - py) * (a.X - px))) * inverseArea;
                    var w2 = 1.0f - w0 - w1;

                    if (w0 < 0.0f || w1 < 0.0f || w2 < 0.0f)
                    {
                        continue;
                    }

                    byte red, green, blue;
                    if (texture != null)
                    {
                        var uv = (uvA * w0) + (uvB * w1) + (uvC * w2);
                        var texel = SampleTexture(texture, uv);

                        // Sampled before the depth test, and skipped here rather than after: a transparent
                        // fragment is not part of the surface at all, so it must not occlude anything either
                        // -- writing depth for it would let this "empty" pixel win the z-test against
                        // whatever should actually be visible behind it.
                        if (texel.W < AlphaCutoff)
                        {
                            continue;
                        }

                        red = (byte)Math.Clamp(texel.X * colorOrFactor.X * shade * 255.0f, 0.0f, 255.0f);
                        green = (byte)Math.Clamp(texel.Y * colorOrFactor.Y * shade * 255.0f, 0.0f, 255.0f);
                        blue = (byte)Math.Clamp(texel.Z * colorOrFactor.Z * shade * 255.0f, 0.0f, 255.0f);
                    }
                    else
                    {
                        red = flatRed;
                        green = flatGreen;
                        blue = flatBlue;
                    }

                    var z = (a.Z * w0) + (b.Z * w1) + (c.Z * w2);
                    var slot = (y * size) + x;

                    // Greater-than test with Z increasing away from the viewer after the view rotation, so
                    // the nearest surface wins.
                    if (z >= depth[slot])
                    {
                        continue;
                    }

                    depth[slot] = z;

                    var offset = slot * BytesPerPixel;
                    pixels[offset] = red;
                    pixels[offset + 1] = green;
                    pixels[offset + 2] = blue;
                    pixels[offset + 3] = 0xFF;
                }
            }
        }

        /// <summary>Nearest-neighbour sample, wrapped -- a tiling texture (a lot of these are) should repeat, not smear at the seam.</summary>
        private static Vector4 SampleTexture(ManaImage texture, Vector2 uv)
        {
            var u = Wrap(uv.X);
            var v = Wrap(uv.Y);

            var x = Math.Clamp((int)(u * texture.Width), 0, texture.Width - 1);
            var y = Math.Clamp((int)(v * texture.Height), 0, texture.Height - 1);

            var i = (y * texture.Width + x) * 4;
            var pixels = texture.Pixels;
            return new Vector4(pixels[i] / 255.0f, pixels[i + 1] / 255.0f, pixels[i + 2] / 255.0f, pixels[i + 3] / 255.0f);
        }

        private static float Wrap(float value)
        {
            var wrapped = value - MathF.Floor(value);
            return wrapped is < 0.0f or >= 1.0f ? 0.0f : wrapped;
        }
    }
}
