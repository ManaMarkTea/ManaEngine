using ManaUI;

namespace ManaTests;

/// <summary>Inventory slots, slot grids, hotbars and panel frames.</summary>
public static class SlotChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        check("counts shorten as they grow", SlotStyle.FormatCount(950) == "950" && SlotStyle.FormatCount(1234) == "1.2k"
            && SlotStyle.FormatCount(45_678) == "45k" && SlotStyle.FormatCount(3_150_000) == "3.1M",
            $"{SlotStyle.FormatCount(1234)} {SlotStyle.FormatCount(45_678)} {SlotStyle.FormatCount(3_150_000)}");

        var grid = new SlotGrid { Columns = 10, SlotSize = 40, Spacing = 2, MinimumRows = 4 };
        var clicks = new List<(int Slot, PointerButton Button)>();
        for (var i = 0; i < 13; i++)
        {
            var index = i;
            var slot = grid.Add(new ItemIcon((ItemIconShape)(1 + i % 10), new Vector4(0.8f, 0.5f, 0.2f, 1)));
            slot.Count = i * 100;
            slot.Clicked += (_, button) => clicks.Add((index, button));
        }
        var root = new UiRoot(fonts) { Content = grid };
        root.UpdateLayout(new Vector2(800, 600));
        root.Render();
        check("a grid is as wide as its columns", MathF.Abs(grid.DesiredSize.X - (10 * 40 + 9 * 2)) < 0.01f, $"{grid.DesiredSize.X}");
        check("a grid keeps its minimum rows of empty wells", grid.Rows == 4 && MathF.Abs(grid.DesiredSize.Y - (4 * 40 + 3 * 2)) < 0.01f, $"{grid.Rows} rows");
        var thirteenth = (ItemSlot)grid.Children[12];
        check("slots fill left to right, then the next row", thirteenth.Bounds.X == 2 * 42 && thirteenth.Bounds.Y == 42, $"{thirteenth.Bounds}");
        check("a slot shows its count shortened", ((ItemSlot)grid.Children[12]).CornerText == "1.2k", ((ItemSlot)grid.Children[12]).CornerText ?? "");

        void Click(Vector2 at, PointerButton button)
        {
            root.Input.InjectPointerPressed(at, button, 1);
            root.Input.InjectPointerReleased(at, button);
        }
        Click(new Vector2(2 * 42 + 20, 42 + 20), PointerButton.Left);
        Click(new Vector2(20, 20), PointerButton.Right);
        Click(new Vector2(41, 20), PointerButton.Left);
        check("a click reaches the slot under it, with its button", clicks.SequenceEqual([(12, PointerButton.Left), (0, PointerButton.Right)]),
            string.Join(", ", clicks));

        var bar = new Hotbar(2, 10);
        bar[0, 3].Icon = new ItemIcon(ItemIconShape.Gear, Vector4.One);
        bar[1, 9].Hotkey = "0";
        var frame = new PanelFrame { Title = "Character", Child = bar };
        root = new UiRoot(fonts) { Content = frame };
        root.UpdateLayout(new Vector2(800, 600));
        root.Render();
        check("a hotbar has its rows of slots", bar.RowCount == 2 && bar.ColumnCount == 10 && bar[0, 3].Icon is ItemIcon, "");
        check("a panel frame holds its content below the title", bar.Bounds.Y >= frame.Bounds.Y + 30 && bar.Bounds.X > frame.Bounds.X, $"{bar.Bounds}");
        var rowTwo = root.Content!.HitTest(new Vector2(bar.Bounds.X + 20, bar.Bounds.Y + 44 + 20));
        Click(new Vector2(bar.Bounds.X + 20, bar.Bounds.Y + 44 + 20), PointerButton.Left);
        check("clicking a row's number makes it the active row", bar.ActiveRow == 1, $"{bar.ActiveRow} hit {rowTwo?.GetType().Name}");

        var drawn = new ManaUiDrawList();
        foreach (var shape in Enum.GetValues<ItemIconShape>())
        {
            ItemIcon.Draw(drawn, shape, new UiRect(0, 0, 32, 32), new Vector4(0.4f, 0.6f, 0.9f, 1));
        }
        check("every stock icon shape draws something", drawn.InstanceCount >= Enum.GetValues<ItemIconShape>().Length - 1, $"{drawn.InstanceCount} primitives");
    }
}
