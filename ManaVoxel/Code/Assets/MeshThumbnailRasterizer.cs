using ManaCore.Graphics;

namespace ManaVoxel.Code.Assets
{
    /// <summary>
    /// Draws a small shaded preview of a mesh on the CPU.
    /// <para>
    /// On the CPU, and not through the renderer, on purpose. A thumbnail needs no pipeline, no material, no
    /// bind group and no render pass -- it needs a few thousand triangles projected into 64 pixels square,
    /// which is less work than the GPU path's setup would be. More importantly it can run on a background
    /// thread, which is what lets a folder of a thousand assets fill in gradually instead of stalling the
    /// frame that opened it; a render-to-texture path would have to be marshalled onto the render thread and
    /// read back, one frame at a time.
    /// </para>
    /// <para>
    /// Being pure -- geometry in, pixels out, no graphics device anywhere -- it is also checkable headlessly,
    /// which matters for something whose output nobody reads character by character.
    /// </para>
    /// </summary>
    public static class MeshThumbnailRasterizer
    {
        private const int BytesPerPixel = 4;

        /// <summary>
        /// Where the light comes from, in view space. Above, in front and to the left: the standard
        /// three-quarter key light, which is what makes a silhouette read as a solid rather than a blob.
        /// </summary>
        private static readonly Vector3 LightDirection = Vector3.Normalize(new Vector3(-0.4f, 0.75f, 0.5f));

        /// <summary>Floor under the shading, so a face turned away is dim rather than black.</summary>
        private const float AmbientLight = 0.28f;

        /// <summary>
        /// Fraction of the tile left empty around the model. Without it the silhouette touches the edges and
        /// a grid of thumbnails reads as a grid of rectangles.
        /// </summary>
        private const float Inset = 0.1f;

        /// <summary>
        /// Renders <paramref name="mesh"/> to a straight-alpha RGBA buffer <paramref name="size"/> square, or
        /// returns null if there is nothing to draw. Transparent where the model is not.
        /// </summary>
        public static byte[]? Render(ManaMeshCpuData mesh, int size, Vector4 tint)
        {
            if (size <= 0 || mesh.Positions.Length == 0 || mesh.Indexes.Length < 3)
            {
                return null;
            }

            // Rotated a little around both axes before projecting: a model authored facing the camera shows
            // one flat face head-on, which says nothing about its shape. Three-quarter view says everything.
            var view = Matrix4x4.CreateRotationY(-0.62f) * Matrix4x4.CreateRotationX(0.42f);

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

            for (var i = 0; i + 2 < indexes.Length; i += 3)
            {
                var ia = indexes[i];
                var ib = indexes[i + 1];
                var ic = indexes[i + 2];

                if (ia >= positions.Length || ib >= positions.Length || ic >= positions.Length)
                {
                    continue;
                }

                var a = Project(positions[ia], centre, view, scale, size);
                var b = Project(positions[ib], centre, view, scale, size);
                var c = Project(positions[ic], centre, view, scale, size);

                // A face normal from the projected triangle, so a mesh that shipped without normals still
                // shades. Flat rather than smooth, which at 64px is indistinguishable.
                var shade = normals != null
                    ? Shade(Vector3.TransformNormal(
                        normals[ia] + normals[ib] + normals[ic], view))
                    : Shade(Vector3.Cross(b - a, c - a));

                FillTriangle(pixels, depth, size, a, b, c, shade, tint);
            }

            return pixels;
        }

        private static Vector3 Project(Vector3 point, Vector3 centre, Matrix4x4 view, float scale, int size)
        {
            var local = Vector3.Transform(point - centre, view);
            var half = size * 0.5f;

            // Y is flipped: model space is Y-up, an image is row-major from the top.
            return new Vector3(
                half + (local.X * scale),
                half - (local.Y * scale),
                local.Z);
        }

        private static float Shade(Vector3 normal)
        {
            if (normal.LengthSquared() <= 1e-12f)
            {
                return AmbientLight;
            }

            var lambert = Vector3.Dot(Vector3.Normalize(normal), LightDirection);

            // Absolute rather than clamped: a thumbnail is a preview, not a lighting model, and a mesh with
            // inconsistent winding would otherwise show half its faces as unlit holes.
            return AmbientLight + ((1.0f - AmbientLight) * MathF.Abs(lambert));
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
        /// </summary>
        private static void FillTriangle(
            byte[] pixels,
            float[] depth,
            int size,
            Vector3 a,
            Vector3 b,
            Vector3 c,
            float shade,
            Vector4 tint)
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
            var red = (byte)Math.Clamp(tint.X * shade * 255.0f, 0.0f, 255.0f);
            var green = (byte)Math.Clamp(tint.Y * shade * 255.0f, 0.0f, 255.0f);
            var blue = (byte)Math.Clamp(tint.Z * shade * 255.0f, 0.0f, 255.0f);

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
    }
}
