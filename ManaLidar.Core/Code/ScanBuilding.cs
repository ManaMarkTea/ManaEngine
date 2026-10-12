using System.Numerics;
using SharpGLTF.Schema2;

namespace ManaLidar;

/// <summary>
/// A building scanned a room at a time. Each finished room is exported and kept here in its small, simplified form
/// while the scan's photos and mesh are let go, so memory goes back down between rooms. Every room is moved by the
/// first room's offset, and the phone keeps tracking in one frame throughout, so the rooms line up; the GLB holds
/// a node per room.
/// </summary>
public sealed class ScanBuilding
{
    private readonly List<ScanRoomMesh> _rooms = new();

    public ScanBuilding(string name)
    {
        Name = name;
    }

    public string Name { get; }
    public IReadOnlyList<ScanRoomMesh> Rooms => _rooms;
    /// <summary>Set by the first room (its floor to y = 0, its middle over the origin) and shared by the rest.</summary>
    public Vector3? Offset { get; private set; }
    public long EstimatedBytes => _rooms.Sum(r => r.EstimatedBytes);

    /// <summary>Exports a room into the building; the caller can then clear the scan and carry on next door.</summary>
    public ScanRoomMesh AddRoom(IReadOnlyList<ScanMeshPiece> pieces, IReadOnlyList<ScanKeyframe> keyframes, ScanExportOptions options)
    {
        Offset ??= options.Recenter ? ScanGltfExporter.RecenterOffset(pieces) : Vector3.Zero;
        var name = options.Name;
        for (var n = 2; _rooms.Any(r => r.Name == name); n++)
        {
            name = $"{options.Name} {n}";
        }
        options.Name = name;
        var room = ScanGltfExporter.MakeRoom(pieces, keyframes, options, Offset.Value);
        _rooms.Add(room);
        return room;
    }

    public void RemoveLastRoom()
    {
        if (_rooms.Count > 0)
        {
            _rooms.RemoveAt(_rooms.Count - 1);
        }
    }

    public ModelRoot Build()
    {
        var model = ModelRoot.CreateModel();
        var scene = model.UseScene(Name);
        // One parent, so the building moves as a piece in Mana and the rooms show beneath it in the Outliner.
        var root = scene.CreateNode(Name);
        foreach (var room in _rooms)
        {
            ScanGltfExporter.WriteRoom(model, root, room);
        }
        return model;
    }

    public void SaveGlb(string path) => Build().SaveGLB(path);
}
