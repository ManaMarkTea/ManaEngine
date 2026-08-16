using Mana;
using Mana.Code;
using Mana.Code.Shapes;
using ManaUI;

namespace ManaTests;

/// <summary>
/// Paint, Erase and Flood Fill all bypass IVoxelTool entirely and mutate <see cref="ManaVoxelShape"/>
/// directly, since none of the drag-geometry tools have any read access to voxels that already exist.
/// This exercises those three new methods directly against a headless shape -- no window or GPU needed,
/// same as every other algorithm-level suite here.
/// </summary>
public static class VoxelShapeDirectEditChecks
{
    private static readonly Vector4 Red = new(1.0f, 0.0f, 0.0f, 1.0f);
    private static readonly Vector4 Green = new(0.0f, 1.0f, 0.0f, 1.0f);
    private static readonly Vector4 Blue = new(0.0f, 0.0f, 1.0f, 1.0f);

    private static ManaVoxelShape CreateShape()
    {
        var world = new ManaWorld();
        // Uploads deferred, so this never touches a GPU that is not there.
        var context = new ManaContext(null!, null!, world) { DeferGpuUploads = true };
        return new ManaVoxelShape(context);
    }

    private static void AddAndCommit(ManaVoxelShape shape, Vector3 position, Vector4 color)
    {
        shape.AddVoxel(position, color);
        shape.CommitData();
    }

    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        // ---------------------------------------------------------------- paint
        var paintShape = CreateShape();
        AddAndCommit(paintShape, new Vector3(0.5f, 0.5f, 0.5f), Red);
        AddAndCommit(paintShape, new Vector3(1.5f, 0.5f, 0.5f), Red);
        AddAndCommit(paintShape, new Vector3(10.5f, 0.5f, 0.5f), Red);

        var paintedCount = paintShape.PaintRadius(new Vector3(0.5f, 0.5f, 0.5f), 1.5f, Blue);
        Check("paint recolours every voxel in radius", paintedCount == 2, $"changed {paintedCount}");
        Check("paint leaves the far voxel untouched",
            paintShape.CommittedVoxels.First(v => v.Position.X > 5.0f).Color == Red);
        Check("paint does not change the voxel count",
            paintShape.VoxelCount == 3, $"{paintShape.VoxelCount}");
        Check("painting an empty radius changes nothing",
            paintShape.PaintRadius(new Vector3(100.0f, 100.0f, 100.0f), 1.0f, Blue) == 0);

        // ---------------------------------------------------------------- erase
        var eraseShape = CreateShape();
        AddAndCommit(eraseShape, new Vector3(0.5f, 0.5f, 0.5f), Red);
        AddAndCommit(eraseShape, new Vector3(1.5f, 0.5f, 0.5f), Red);
        AddAndCommit(eraseShape, new Vector3(10.5f, 0.5f, 0.5f), Red);

        var erasedCount = eraseShape.EraseRadius(new Vector3(0.5f, 0.5f, 0.5f), 1.5f);
        Check("erase removes every voxel in radius", erasedCount == 2, $"removed {erasedCount}");
        Check("erase leaves the far voxel behind", eraseShape.VoxelCount == 1, $"{eraseShape.VoxelCount}");
        Check("erasing an empty radius changes nothing",
            eraseShape.EraseRadius(new Vector3(100.0f, 100.0f, 100.0f), 1.0f) == 0);

        // ---------------------------------------------------------------- flood fill
        var floodShape = CreateShape();
        // A 3-long red run, a green voxel breaking the run, then one more red voxel past the break --
        // flood fill from one end must stop at the green boundary and never reach the far red voxel.
        AddAndCommit(floodShape, new Vector3(0.5f, 0.5f, 0.5f), Red);
        AddAndCommit(floodShape, new Vector3(1.5f, 0.5f, 0.5f), Red);
        AddAndCommit(floodShape, new Vector3(2.5f, 0.5f, 0.5f), Red);
        AddAndCommit(floodShape, new Vector3(3.5f, 0.5f, 0.5f), Green);
        AddAndCommit(floodShape, new Vector3(4.5f, 0.5f, 0.5f), Red);

        var floodedCount = floodShape.FloodFill(new Vector3(0.5f, 0.5f, 0.5f), Blue);
        Check("flood fill recolours the whole connected run", floodedCount == 3, $"changed {floodedCount}");
        Check("flood fill stops at the colour boundary",
            floodShape.CommittedVoxels.First(v => v.Position.X > 3.0f && v.Position.X < 4.0f).Color == Green);
        Check("flood fill never reaches past the boundary",
            floodShape.CommittedVoxels.First(v => v.Position.X > 4.0f).Color == Red);

        Check("flood fill from an empty cell changes nothing",
            floodShape.FloodFill(new Vector3(100.0f, 100.0f, 100.0f), Blue) == 0);
        Check("flood fill onto its own colour changes nothing",
            floodShape.FloodFill(new Vector3(3.5f, 0.5f, 0.5f), Green) == 0);

        // ---------------------------------------------------------------- segment sweep
        // A brush dragged fast enough (or delayed by a frame hitch) only ever reports its start and end
        // position, never the cells in between -- paint/erase must sweep the whole segment rather than
        // dabbing a single point, or the skipped cells are simply never touched.
        var sweepShape = CreateShape();
        AddAndCommit(sweepShape, new Vector3(0.5f, 0.5f, 0.5f), Red);
        AddAndCommit(sweepShape, new Vector3(1.5f, 0.5f, 0.5f), Red);
        AddAndCommit(sweepShape, new Vector3(2.5f, 0.5f, 0.5f), Red);
        AddAndCommit(sweepShape, new Vector3(3.5f, 0.5f, 0.5f), Red);
        AddAndCommit(sweepShape, new Vector3(10.5f, 0.5f, 0.5f), Red);

        var sweepPaintedCount = sweepShape.PaintRadius(
            new Vector3(0.5f, 0.5f, 0.5f), new Vector3(3.5f, 0.5f, 0.5f), 0.5f, Blue);
        Check("a segment paint touches every cell along the way, not just its two endpoints",
            sweepPaintedCount == 4, $"changed {sweepPaintedCount}");
        Check("but not a cell the segment never passes near",
            sweepShape.CommittedVoxels.First(v => v.Position.X > 5.0f).Color == Red);

        var sweepEraseShape = CreateShape();
        AddAndCommit(sweepEraseShape, new Vector3(0.5f, 0.5f, 0.5f), Red);
        AddAndCommit(sweepEraseShape, new Vector3(1.5f, 0.5f, 0.5f), Red);
        AddAndCommit(sweepEraseShape, new Vector3(2.5f, 0.5f, 0.5f), Red);
        AddAndCommit(sweepEraseShape, new Vector3(10.5f, 0.5f, 0.5f), Red);

        var sweepErasedCount = sweepEraseShape.EraseRadius(
            new Vector3(0.5f, 0.5f, 0.5f), new Vector3(2.5f, 0.5f, 0.5f), 0.5f);
        Check("a segment erase removes every cell along the way, not just its two endpoints",
            sweepErasedCount == 3, $"removed {sweepErasedCount}");
        Check("leaving the far voxel the segment never passes near",
            sweepEraseShape.VoxelCount == 1, $"{sweepEraseShape.VoxelCount}");

        var removedVoxels = new List<ManaVoxelData>();
        var removalShape = CreateShape();
        removalShape.OnVoxelRemoved += voxel => removedVoxels.Add(voxel);
        AddAndCommit(removalShape, new Vector3(0.5f, 0.5f, 0.5f), Red);
        removalShape.EraseRadius(new Vector3(0.5f, 0.5f, 0.5f), 0.5f);
        Check("erase fires OnVoxelRemoved for each voxel it takes out",
            removedVoxels.Count == 1 && removedVoxels[0].Position == new Vector3(0.5f, 0.5f, 0.5f),
            $"{removedVoxels.Count} events");
    }
}
