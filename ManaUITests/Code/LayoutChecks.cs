using ManaUI;

namespace ManaUITests;

/// <summary>
/// Layout, text measurement, draw-list recording and invalidation. The oldest checks in the suite --
/// these caught the arrange-ordering bug that made every container lay out against a zero rect.
/// </summary>
public static class LayoutChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        // ---------------------------------------------------------------- fonts + text
        Console.WriteLine("== fonts ==");
        ManaFont? font = null;
        try
        {
            font = fonts.Get("Roboto", 15.0f);
            Check("Roboto-Regular resolved", true, font.Name);
        }
        catch (Exception ex)
        {
            Check("Roboto-Regular resolved", false, ex.Message);
        }

        if (font != null)
        {
            Check("ascent positive", font.Ascent > 0, $"{font.Ascent:F2}");
            Check("descent positive-down", font.Descent > 0, $"{font.Descent:F2}");
            // Roboto reports a zero line gap, so the font's own line height is exactly the em size; the
            // comfortable default comes from the theme's LineSpacing instead.
            Check("line height sane for 15px", font.LineHeight is >= 15 and < 24, $"{font.LineHeight:F2}");

            var w = font.MeasureWidth("Hello");
            Check("'Hello' has width", w > 10, $"{w:F2}px");
            Check("wider string measures wider", font.MeasureWidth("Hello world") > w);
            Check("space advances", font.GetGlyph(' ').Advance > 0 && !font.GetGlyph(' ').HasPixels);
            Check("'H' rasterised", font.GetGlyph('H').HasPixels);
            Check("atlas dirty after raster", fonts.Atlas.IsDirty);

            // HiDPI: logical metrics must be scale-independent.
            var font2x = fonts.Get("Roboto", 15.0f, 2.0f);
            var delta = MathF.Abs(font2x.MeasureWidth("Hello world") - font.MeasureWidth("Hello world"));
            Check("2x scale keeps logical width", delta < 1.0f, $"delta {delta:F3}px");
            Check("2x glyph bakes bigger bitmap", font2x.RenderScale == 2.0f);
        }

        // ---------------------------------------------------------------- layout
        Console.WriteLine();
        Console.WriteLine("== layout ==");

        var root = new UiRoot(fonts);

        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 10, Padding = new Thickness(8) };
        var a = new Border { Height = 30, Background = UiColor.White };
        var b = new Border { Height = 50, Background = UiColor.White };
        stack.Children.Add(a);
        stack.Children.Add(b);
        root.Content = stack;

        root.UpdateLayout(new Vector2(400, 300));

        Check("stack fills width", stack.Bounds.Width == 400, stack.Bounds.ToString());
        Check("first child below padding", a.Bounds.Y == 8, $"y={a.Bounds.Y}");
        Check("first child inset by padding", a.Bounds.X == 8 && a.Bounds.Width == 384, a.Bounds.ToString());
        Check("spacing applied", b.Bounds.Y == 8 + 30 + 10, $"y={b.Bounds.Y}");
        Check("explicit heights honoured", a.Bounds.Height == 30 && b.Bounds.Height == 50);

        // Grid: Auto | * | 2* with spacing
        var grid = new Grid { ColumnSpacing = 4 };
        grid.SetColumns("60, *, 2*");
        grid.SetRows("*");
        var c0 = new Border { Background = UiColor.White };
        var c1 = new Border { Background = UiColor.White };
        var c2 = new Border { Background = UiColor.White };
        c1.EnsureLayoutSlot().GridColumn = 1;
        c2.EnsureLayoutSlot().GridColumn = 2;
        grid.Children.Add(c0);
        grid.Children.Add(c1);
        grid.Children.Add(c2);

        root.Content = null;
        root.Content = grid;
        root.UpdateLayout(new Vector2(400, 200));

        // 400 - 60 fixed - 8 spacing = 332 to split 1:2
        Check("fixed column exact", c0.Bounds.Width == 60, $"{c0.Bounds.Width}");
        Check("star split 1:2", MathF.Abs(c1.Bounds.Width * 2 - c2.Bounds.Width) < 0.01f,
            $"{c1.Bounds.Width:F2} vs {c2.Bounds.Width:F2}");
        Check("columns fill width", MathF.Abs(c2.Bounds.Right - 400) < 0.01f, $"right={c2.Bounds.Right:F2}");
        Check("spacing between columns", MathF.Abs(c1.Bounds.X - c0.Bounds.Right - 4) < 0.01f);

        // DockPanel
        var dock = new DockPanel();
        var top = new Border { Height = 24, Background = UiColor.White };
        var left = new Border { Width = 100, Background = UiColor.White };
        var fill = new Border { Background = UiColor.White };
        top.EnsureLayoutSlot().Dock = Dock.Top;
        left.EnsureLayoutSlot().Dock = Dock.Left;
        dock.Children.Add(top);
        dock.Children.Add(left);
        dock.Children.Add(fill);

        root.Content = null;
        root.Content = dock;
        root.UpdateLayout(new Vector2(800, 600));

        Check("docked top spans width", top.Bounds.Width == 800 && top.Bounds.Height == 24, top.Bounds.ToString());
        Check("docked left below top", left.Bounds.Y == 24 && left.Bounds.Height == 576, left.Bounds.ToString());
        Check("fill takes remainder", fill.Bounds == UiRect.FromEdges(100, 24, 800, 600), fill.Bounds.ToString());

        // Text wrapping drives an Auto row's height
        var wrapText = new TextBlock("The quick brown fox jumps over the lazy dog many times over")
        {
            TextWrapping = TextWrapping.Wrap
        };
        var wrapGrid = new Grid();
        wrapGrid.SetColumns("200");
        wrapGrid.SetRows("Auto");
        wrapGrid.Children.Add(wrapText);

        root.Content = null;
        root.Content = wrapGrid;
        root.UpdateLayout(new Vector2(600, 400));

        Check("wrapped text fits column", wrapText.Bounds.Width <= 200.5f, $"{wrapText.Bounds.Width:F2}");
        Check("auto row grew for wrapped text", wrapGrid.RowDefinitions[0].Resolved > (font?.LineHeight ?? 0) * 1.5f,
            $"row={wrapGrid.RowDefinitions[0].Resolved:F2}");

        // ---------------------------------------------------------------- draw list
        Console.WriteLine();
        Console.WriteLine("== draw list ==");

        var drawList = root.Render();
        Check("instances recorded", drawList.InstanceCount > 5, $"{drawList.InstanceCount} instances");
        Check("single batch (one draw call)", drawList.Batches.Count == 1, $"{drawList.Batches.Count} batches");
        Check("batch covers all instances", drawList.Batches[0].InstanceCount == drawList.InstanceCount);

        var glyphCount = 0;
        foreach (var instance in drawList.Instances)
        {
            if ((instance.Flags & (uint)ManaUiInstanceFlags.GlyphAtlas) != 0)
            {
                glyphCount++;
            }
        }
        Check("glyphs emitted", glyphCount > 20, $"{glyphCount} glyph quads");

        // Clipping must actually reject offscreen content.
        var clipStack = new StackPanel { Orientation = Orientation.Vertical };
        for (var i = 0; i < 200; i++)
        {
            clipStack.Children.Add(new Border { Height = 20, Background = UiColor.White });
        }
        var clipHost = new Border { ClipToBounds = true, Height = 100, Child = clipStack };
        root.Content = null;
        root.Content = clipHost;
        root.UpdateLayout(new Vector2(300, 100));
        var clipped = root.Render();
        Check("offscreen rows culled", clipped.InstanceCount < 30, $"{clipped.InstanceCount} of 200 rows drawn");

        // ---------------------------------------------------------------- invalidation
        Console.WriteLine();
        Console.WriteLine("== invalidation ==");

        root.Content = null;
        var invStack = new StackPanel();
        var invChild = new Border { Height = 20, Background = UiColor.White };
        invStack.Children.Add(invChild);
        root.Content = invStack;
        root.UpdateLayout(new Vector2(200, 200));

        Check("layout valid after pass", root.IsLayoutValid);
        invChild.Height = 40;
        Check("child change invalidates root", !root.IsLayoutValid);
        root.UpdateLayout(new Vector2(200, 200));
        Check("re-layout picks up change", invChild.Bounds.Height == 40, $"{invChild.Bounds.Height}");
    }
}
