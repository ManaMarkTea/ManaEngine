using Mana.Code.GameModes;
using ManaUI;

namespace ManaTests;

/// <summary>Editing a PolygonCollider in the viewport: which point a click takes, and where Ctrl+click adds one.</summary>
public static class PolygonEditChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> report)
    {
        void check(string what, bool ok, string detail = "") => report(what, ok, detail);

        float[] box = [-2, 0, 2, 0, 2, 1, -2, 1];
        check("a click near a point takes that point", VoxelEditorGameMode.NearestPolygonPoint(box, new Vector2(1.95f, 0.97f), 0.1f) == 2);
        check("and one out of reach of every point takes none", VoxelEditorGameMode.NearestPolygonPoint(box, new Vector2(0, 0.5f), 0.1f) == -1);

        var inserted = VoxelEditorGameMode.InsertPolygonPoint(box, new Vector2(0.4f, 1.05f), 0.2f);
        check("Ctrl+click on the top edge adds a point on it, between that edge's ends",
            inserted is { Index: 3 } add && add.Points.Length == 10 && MathF.Abs(add.Points[6] - 0.4f) < 1e-4f && MathF.Abs(add.Points[7] - 1.0f) < 1e-4f,
            inserted is { } shown ? string.Join(", ", shown.Points) : "none");
        var closing = VoxelEditorGameMode.InsertPolygonPoint(box, new Vector2(-2.05f, 0.5f), 0.2f);
        check("and on the closing edge (last point back to the first) it goes on the end",
            closing is { Index: 4 } close && MathF.Abs(close.Points[8] + 2.0f) < 1e-4f && MathF.Abs(close.Points[9] - 0.5f) < 1e-4f,
            closing is { } shownClose ? string.Join(", ", shownClose.Points) : "none");
        check("a click away from every edge adds nothing", VoxelEditorGameMode.InsertPolygonPoint(box, new Vector2(0, 0.5f), 0.2f) == null);
    }
}
