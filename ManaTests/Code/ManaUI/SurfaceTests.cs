using ManaUI;

namespace ManaTests;

/// <summary>Application-owned pictures: the surface itself, and the element that shows one.</summary>
public static class SurfaceChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        var surface = new ManaUiSurface();
        var other = new ManaUiSurface();
        Check("each surface has its own id", surface.Id != other.Id && surface.Id != 0);
        Check("a new surface has no pixels", !surface.HasPixels && surface.Revision == 0);

        var red = new byte[4 * 2 * 2];
        for (var i = 0; i < red.Length; i += 4)
        {
            red[i] = 255;
            red[i + 3] = 255;
        }
        surface.Update(2, 2, red);
        Check("Update stores size and pixels and bumps the revision",
            surface.Width == 2 && surface.Height == 2 && surface.Revision == 1 && surface.Pixels[0] == 255 && surface.Pixels.Length == 16);
        surface.Update(2, 2, red);
        Check("every Update is a new revision, so the backend re-uploads", surface.Revision == 2);

        var shortUpdate = false;
        try
        {
            surface.Update(4, 4, red);
        }
        catch (ArgumentException)
        {
            shortUpdate = true;
        }
        Check("too few pixels for the size is refused", shortUpdate && surface.Width == 2);

        // -------------------------------------------------------------- the element
        var root = new UiRoot(fonts);
        var image = new UiSurfaceImage { Surface = surface, Width = 120, Height = 80 };
        root.Content = new StackPanel().Children(image);
        root.UpdateLayout(new Vector2(400, 300));
        var drawList = root.Render();
        Check("an element showing a surface draws a batch from its texture", drawList.DrawsTexture(surface.Id));
        Check("and registers the picture with the root for the backend", root.Surfaces.ContainsKey(surface.Id));
        Check("nothing draws from a surface that is not shown", !drawList.DrawsTexture(other.Id));

        image.Surface = null;
        root.UpdateLayout(new Vector2(400, 300));
        Check("an empty element draws nothing", !root.Render().DrawsTexture(surface.Id));

        // Contain letterboxes; Stretch fills.
        image.Surface = surface;
        image.Fit = UiImageFit.Contain;
        root.UpdateLayout(new Vector2(400, 300));
        var contained = image.DestinationRect();
        Check("Contain keeps the picture's proportions inside the element",
            MathF.Abs(contained.Width - contained.Height) < 0.01f && contained.Height <= image.Bounds.Height + 0.01f, $"{contained.Width}x{contained.Height}");
        image.Fit = UiImageFit.Stretch;
        Check("Stretch fills the element", image.DestinationRect().Width == image.Bounds.Width);

        Check("the element reports its size in device pixels, to render at",
            image.DevicePixelSize.X == MathF.Round(image.Bounds.Width * root.Scale), image.DevicePixelSize.ToString());

        // -------------------------------------------------------------- pointer
        var presses = 0;
        var wheels = 0;
        image.PointerPressed += (_, _) => presses++;
        image.PointerWheel += (_, _) => wheels++;
        root.UpdateLayout(new Vector2(400, 300));
        var inside = image.Bounds.Center;
        root.Input.InjectPointerMoved(inside);
        root.Input.InjectPointerPressed(inside, PointerButton.Left, 10.0);
        Check("a press captures the pointer, so a drag keeps arriving", root.Input.PointerCaptureTarget == image);
        root.Input.InjectPointerReleased(inside, PointerButton.Left);
        root.Input.InjectPointerWheel(new Vector2(0, 1));
        Check("hosts hear presses and the wheel through the ordinary events", presses == 1 && wheels == 1, $"{presses} {wheels}");
        Check("release lets go", root.Input.PointerCaptureTarget == null);

        surface.Dispose();
        Check("a disposed surface says so and drops its pixels", surface.IsDisposed && !surface.HasPixels);
        root.UpdateLayout(new Vector2(400, 300));
        Check("and is no longer drawn", !root.Render().DrawsTexture(surface.Id));
    }
}
