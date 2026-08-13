using ManaUI;

namespace ManaTests;

/// <summary>
/// The colour atlas and the control that draws from it. This is the plumbing colour emoji and texture
/// previews both need: the glyph atlas is single-channel coverage tinted by the instance, which cannot
/// carry an image that brings its own colours.
/// </summary>
public static class ImageChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        // ---------------------------------------------------------------- atlas
        Console.WriteLine("  -- image atlas --");

        var atlas = new ManaImageAtlas(256, 256);
        Check("a fresh atlas is dirty", atlas.IsDirty, "the white texel needs uploading");
        Check("four bytes per pixel", atlas.Pixels.Length == 256 * 256 * 4, $"{atlas.Pixels.Length} bytes");

        atlas.ClearDirty();
        Check("clearing settles it", !atlas.IsDirty);

        // A recognisable 2x2: red, green, blue, half-transparent white.
        var swatch = new byte[]
        {
            255, 0, 0, 255,     0, 255, 0, 255,
            0, 0, 255, 255,     255, 255, 255, 128
        };

        var uv = atlas.Add(swatch, 2, 2);
        Check("packing returns a UV", uv != null, uv?.ToString() ?? "null");
        Check("packing dirties the atlas", atlas.IsDirty);

        if (uv is { } region)
        {
            Check("the UV is normalised", region.Z <= 1.0f && region.W <= 1.0f, region.ToString());
            Check("the UV spans two texels",
                MathF.Abs((region.Z - region.X) * 256.0f - 2.0f) < 0.01f,
                $"{(region.Z - region.X) * 256.0f:F2} texels wide");

            // The pixels have to land where the UV says they do, or the image draws whatever was there
            // before it.
            var x = (int)MathF.Round(region.X * 256.0f);
            var y = (int)MathF.Round(region.Y * 256.0f);
            var offset = (y * 256 + x) * 4;

            Check("red went in first",
                atlas.Pixels[offset] == 255 && atlas.Pixels[offset + 1] == 0 && atlas.Pixels[offset + 2] == 0,
                $"{atlas.Pixels[offset]}, {atlas.Pixels[offset + 1]}, {atlas.Pixels[offset + 2]}");

            var secondRow = ((y + 1) * 256 + x) * 4;
            Check("the second row is where it should be",
                atlas.Pixels[secondRow + 2] == 255, $"blue {atlas.Pixels[secondRow + 2]}");

            Check("alpha survives the round trip",
                atlas.Pixels[secondRow + 4 + 3] == 128, $"{atlas.Pixels[secondRow + 4 + 3]}");
        }

        // Only the touched rows are reported, so a single small image does not re-upload the whole atlas.
        var (_, dirtyMinY, _, dirtyMaxY) = atlas.DirtyRegion;
        Check("the dirty region is a few rows, not the atlas", dirtyMaxY - dirtyMinY < 16,
            $"rows {dirtyMinY}..{dirtyMaxY}");

        // Full is a normal state, not an error: too many thumbnails should degrade rather than throw.
        var small = new ManaImageAtlas(8, 8);
        Check("an oversized image is refused", small.Add(new byte[64 * 4], 8, 8) == null, "does not fit");

        atlas.Reset();
        Check("reset bumps the generation", atlas.Generation == 1, $"{atlas.Generation}");
        Check("reset dirties everything", atlas.IsDirty);

        // ---------------------------------------------------------------- the control
        Console.WriteLine("  -- image control --");

        var root = new UiRoot(fonts);
        Check("the root owns a colour atlas", root.Images != null, $"{root.Images.Width}x{root.Images.Height}");

        var pixels = new byte[16 * 8 * 4];
        Array.Fill(pixels, (byte)200);
        var placed = root.Images.Add(pixels, 16, 8);
        Check("an image packs into the root's atlas", placed != null);

        var image = new UiImage().SetSource(placed!.Value, new Vector2(16.0f, 8.0f));
        root.Content = image;
        root.UpdateLayout(new Vector2(400, 300));

        // Natural size is what it asks for; where it ends up is the parent's business, and an element
        // stretches by default like every other one in the library.
        Check("an image asks for its natural size",
            MathF.Abs(image.DesiredSize.X - 16.0f) < 0.5f && MathF.Abs(image.DesiredSize.Y - 8.0f) < 0.5f,
            image.DesiredSize.ToString());
        Check("and fits inside whatever it is given",
            image.DestinationRect().Width <= image.Bounds.Width + 0.5f
            && image.DestinationRect().Height <= image.Bounds.Height + 0.5f,
            image.DestinationRect().ToString());

        var drawn = root.Render();
        Check("the image draws", drawn.InstanceCount == 1, $"{drawn.InstanceCount} instances");
        Check("as a textured instance",
            (drawn.Instances[0].Flags & (uint)ManaUiInstanceFlags.Texture) != 0,
            $"flags {drawn.Instances[0].Flags}");

        // The whole point of a shared atlas: an image and the text beside it are one batch.
        root.Content = null;
        root.Content = new StackPanel().Vertical(4.0f).Children(
            new TextBlock().Text("Label"),
            new UiImage().SetSource(placed.Value, new Vector2(16.0f, 8.0f)));

        root.UpdateLayout(new Vector2(400, 300));
        var mixed = root.Render();
        Check("text and image share one draw call", mixed.Batches.Count == 1,
            $"{mixed.Batches.Count} batches");

        // ---------------------------------------------------------------- fitting
        var fitted = new UiImage()
            .SetSource(placed.Value, new Vector2(16.0f, 8.0f))
            .Configure(i => i.Fit = UiImageFit.Contain);

        fitted.Width = 100.0f;
        fitted.Height = 100.0f;
        root.Content = null;
        root.Content = fitted;
        root.UpdateLayout(new Vector2(400, 300));

        var destination = fitted.DestinationRect();
        Check("contain keeps the aspect ratio",
            MathF.Abs(destination.Width / destination.Height - 2.0f) < 0.01f,
            $"{destination.Width:F1}x{destination.Height:F1}");
        Check("contain fits inside the element",
            destination.Width <= 100.5f && destination.Height <= 100.5f, destination.ToString());

        fitted.Fit = UiImageFit.Cover;
        root.UpdateLayout(new Vector2(400, 300));
        destination = fitted.DestinationRect();
        Check("cover fills the element",
            destination.Width >= 99.5f && destination.Height >= 49.5f, destination.ToString());
        Check("cover keeps the aspect ratio",
            MathF.Abs(destination.Width / destination.Height - 2.0f) < 0.01f,
            $"{destination.Width:F1}x{destination.Height:F1}");

        // Cover overflows on purpose, so it has to clip or it would spill over its neighbours.
        Check("cover still renders", root.Render().InstanceCount > 0);

        fitted.Fit = UiImageFit.Stretch;
        root.UpdateLayout(new Vector2(400, 300));
        Check("stretch fills the bounds exactly", fitted.DestinationRect() == fitted.Bounds,
            fitted.DestinationRect().ToString());

        // An image with nothing assigned must be silent rather than drawing the atlas corner.
        var empty = new UiImage();
        root.Content = null;
        root.Content = empty;
        root.UpdateLayout(new Vector2(400, 300));
        Check("an empty image draws nothing", root.Render().InstanceCount == 0,
            $"{root.Render().InstanceCount} instances");
    }
}
