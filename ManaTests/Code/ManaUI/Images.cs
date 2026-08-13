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

        // ---------------------------------------------------------------- colour emoji
        Console.WriteLine("  -- colour emoji --");

        // stb rasterises outlines and nothing else, so an emoji font is blank through it. The CBDT tables
        // hold a PNG per glyph, which is what this reads.
        // Loaded outside the ordinary font path on purpose: stb cannot open a CBDT emoji font at all,
        // because it has no glyf or loca table for stbtt_InitFont to work from.
        var emojiTable = fonts.ColorEmoji;
        Check("the colour emoji font is available", emojiTable != null,
            emojiTable == null
                ? $"searched {string.Join("; ", fonts.SearchDirectories)}"
                : $"{emojiTable.GlyphCount} glyphs at {emojiTable.PixelsPerEm}px, "
                  + $"{emojiTable.CharacterCount} codepoints");

        if (emojiTable != null)
        {
            Check("its character map reaches past the BMP",
                emojiTable.TryGetGlyphIndex(0x1F600, out var grinIndex) && grinIndex > 0,
                "U+1F600 maps to a glyph");

            // Reached through an ordinary text font, which is the whole point: a label containing an emoji
            // just works, with the emoji font supplying pixels stb never sees.
            var emoji = fonts.Get("Roboto", 15.0f);
            Check("a text font can reach the emoji", emoji.HasColorGlyphs, "colour source attached");

            // U+1F600 GRINNING FACE, the canonical smoke test.
            var grin = emoji.GetGlyph(0x1F600);
            Check("an emoji rasterises", grin.IsDrawable, $"{grin.Size.X:F1}x{grin.Size.Y:F1}px");
            Check("and is flagged as colour", grin.IsColor, "goes to the image atlas");
            Check("it is sized for the text around it", grin.Size.Y is > 4.0f and < 40.0f,
                $"{grin.Size.Y:F1}px tall at a 15px font");
            Check("it advances the pen", grin.Advance > 0.0f, $"{grin.Advance:F1}px");

            // The pixels must actually be in the atlas, in colour -- a blank region would still produce a
            // plausible-looking UV.
            var grinUv = grin.AtlasUv;
            var px = (int)MathF.Round(grinUv.X * fonts.Images.Width) + 1;
            var py = (int)MathF.Round(grinUv.Y * fonts.Images.Height) + 1;
            var opaque = 0;
            var coloured = 0;

            for (var y = 0; y < (int)grin.Size.Y - 2 && y < 64; y++)
            {
                for (var x = 0; x < (int)grin.Size.X - 2 && x < 64; x++)
                {
                    var o = ((py + y) * fonts.Images.Width + px + x) * 4;
                    if (fonts.Images.Pixels[o + 3] > 200)
                    {
                        opaque++;
                        if (fonts.Images.Pixels[o] != fonts.Images.Pixels[o + 1]
                            || fonts.Images.Pixels[o + 1] != fonts.Images.Pixels[o + 2])
                        {
                            coloured++;
                        }
                    }
                }
            }

            Check("the atlas holds real pixels", opaque > 20, $"{opaque} opaque texels");
            Check("and they are in colour, not greyscale", coloured > 10,
                $"{coloured} texels with unequal channels");

            // ------------------------------------------------------------ invisible codepoints
            // An emoji is rarely one codepoint. A heart pasted from Windows is U+2764 followed by U+FE0F,
            // the variation selector that asks for the emoji form -- and a selector left to the normal
            // path rasterises as .notdef, which is the box that appeared after every heart.
            Check("a variation selector is recognised as ignorable",
                ManaFont.IsDefaultIgnorable(0xFE0F), "U+FE0F");
            Check("so is the zero-width joiner", ManaFont.IsDefaultIgnorable(0x200D), "U+200D");
            Check("an ordinary character is not",
                !ManaFont.IsDefaultIgnorable('A') && !ManaFont.IsDefaultIgnorable(0x1F600), "A, U+1F600");

            var selector = emoji.GetGlyph(0xFE0F);
            Check("a variation selector draws nothing", !selector.HasPixels, "no quad");
            Check("and takes no space", selector.Advance == 0.0f, $"{selector.Advance}");
            Check("and is not reported missing", !selector.IsMissing, "invisible, not absent");

            // The sequence as Windows actually delivers it.
            var heartRoot = new UiRoot(fonts);
            heartRoot.Content = new TextBlock().Text("❤️");
            heartRoot.UpdateLayout(new Vector2(200, 60));
            var hearts = heartRoot.Render();

            Check("a heart emoji is one glyph, not two", hearts.InstanceCount == 1,
                $"{hearts.InstanceCount} instances");

            // ------------------------------------------------------------ text wins by default
            // An emoji font claims far more than a UI wants it to draw. Noto maps the ASCII digits and the
            // asterisk and hash, because they are the bases of the keycap sequences -- so with colour
            // tried first, every number in the interface became a coloured tile and a tree's collapsed
            // arrow became a play button.
            Check("the emoji font really does claim the digits",
                emojiTable.TryGetGlyphIndex('7', out _), "U+0037 is in its cmap");
            Check("and the play button triangle",
                emojiTable.TryGetGlyphIndex(0x25B6, out _), "U+25B6 is in its cmap");

            var digit = emoji.GetGlyph('7');
            Check("a digit stays text", !digit.IsColor, digit.IsColor ? "coloured tile" : "outline");

            var arrow = emoji.GetGlyph(0x25B6);
            Check("a triangle a tree view wants stays text", !arrow.IsColor,
                arrow.IsColor ? "play button" : "outline");

            // The supplementary planes are emoji and nothing else, so those need no selector.
            Check("a supplementary-plane emoji is colour without asking", grin.IsColor, "U+1F600");
            Check("and the BMP is not assumed to be",
                !ManaFont.IsEmojiPresentation(0x2764) && ManaFont.IsEmojiPresentation(0x1F600),
                "U+2764 text, U+1F600 emoji");

            // Which is what the variation selector is for: it asks for the colour form explicitly.
            var textHeart = emoji.GetGlyph(0x2764);
            var colorHeart = emoji.GetGlyph(0x2764, preferColor: true);
            Check("a bare heart is the text form", !textHeart.IsColor, "dingbat");
            Check("a selector asks for the colour form", colorHeart.IsColor,
                colorHeart.IsColor ? "colour" : "still text");
            Check("the selector is detected in a string",
                ManaFont.HasEmojiSelector("❤️".AsSpan(), 1), "U+FE0F follows");

            // A mixed line is still one draw call, because both atlases are bound at once.
            var mixedRoot = new UiRoot(fonts);
            mixedRoot.Content = new TextBlock().Text("Hi 😀 there");
            mixedRoot.UpdateLayout(new Vector2(400, 100));
            var mixedText = mixedRoot.Render();

            // "Hi <emoji> there" is seven letters and one emoji; the two spaces draw nothing.
            Check("text and emoji draw together", mixedText.InstanceCount == 8,
                $"{mixedText.InstanceCount} instances");
            Check("in a single batch", mixedText.Batches.Count == 1, $"{mixedText.Batches.Count} batches");

            var colourInstances = 0;
            foreach (var instance in mixedText.Instances)
            {
                if ((instance.Flags & (uint)ManaUiInstanceFlags.Texture) != 0)
                {
                    colourInstances++;
                }
            }

            Check("the emoji went out as an image, not coverage", colourInstances == 1,
                $"{colourInstances} textured instances");
        }

        // An image with nothing assigned must be silent rather than drawing the atlas corner.
        var empty = new UiImage();
        root.Content = null;
        root.Content = empty;
        root.UpdateLayout(new Vector2(400, 300));
        Check("an empty image draws nothing", root.Render().InstanceCount == 0,
            $"{root.Render().InstanceCount} instances");
    }
}
