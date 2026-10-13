#if !MANA_BROWSER
using System.Collections.Concurrent;
using System.Numerics;
using Mana;
using Mana.Code;
using Mana.Code.Materials;
using Mana.Code.Nodes;
using Mana.Code.Shapes;
using ManaCore.Diagnostics;
using ManaCore.Graphics;
using ManaCore.Imaging;
using ManaLidar;

namespace ManaVoxel.Code.LiveScan;

/// <summary>
/// A scan streaming in from a phone, shown in the editor as it grows. Pieces arrive as the phone's mesh changes and
/// photos as it keeps them; a worker colours each piece from the photos (or by how well they cover it) on this PC,
/// and the editor thread swaps the result into the piece's node. Bake runs the full textured export here and hands
/// the GLB back for the level. The live nodes are editor-only and never saved.
/// </summary>
public sealed class LiveScanSession : IDisposable
{
    public enum Colouring
    {
        /// <summary>Each vertex the colour the photos show there.</summary>
        Photos,
        /// <summary>Red where no photo has seen it, amber where only poorly, green where well: where to point next.</summary>
        Coverage,
    }

    private readonly ManaContext _context;
    private readonly object _lock = new();
    private readonly Dictionary<Guid, ScanMeshPiece> _pieces = new();
    private readonly List<ScanKeyframe> _keyframes = new();
    private readonly HashSet<Guid> _dirty = new();
    private readonly ConcurrentQueue<(Guid Id, ManaMeshCpuData? Data, Vector3 Min, Vector3 Max)> _built = new();
    private readonly Dictionary<Guid, ManaSceneNode> _nodes = new();
    private readonly SemaphoreSlim _work = new(0);
    private readonly CancellationTokenSource _stop = new();
    // Work that must touch the scene, handed back from a background task to run in Update.
    private readonly ConcurrentQueue<Action> _onEditorThread = new();
    private ManaMaterial? _material;
    private ManaSceneNode? _root;
    private LiveScanServer? _server;
    private Colouring _colour = Colouring.Photos;
    private long _lastFloorCheck;

    public LiveScanSession(ManaContext context)
    {
        _context = context;
        _ = Task.Run(ColourLoop);
    }

    /// <summary>The session the editor runs, for the panel and the overlay.</summary>
    public static LiveScanSession? Current { get; set; }

    public LiveScanServer? Server => _server;
    public bool Listening => _server != null;
    public string? StatusMessage { get; private set; }

    /// <summary>Where the phone is now (camera to world, in the scene's frame), while it is streaming.</summary>
    public Matrix4x4? PhonePose { get; private set; }

    // Tallied in Update rather than counted under the lock by every binding that asks, every frame.
    public int PieceCount { get; private set; }
    public int TriangleCount { get; private set; }
    public int PhotoCount { get; private set; }
    private long _lastCount;

    /// <summary>Where the scan sits: the phone's own frame lifted so its floor is at y = 0.</summary>
    public Vector3 Offset { get; private set; }

    public Colouring Colour
    {
        get => _colour;
        set
        {
            if (_colour == value)
            {
                return;
            }
            _colour = value;
            lock (_lock)
            {
                _dirty.UnionWith(_pieces.Keys);
            }
            _work.Release();
        }
    }

    public bool Baking { get; private set; }

    public void Start()
    {
        if (_server != null || OperatingSystem.IsBrowser())
        {
            return;
        }
        try
        {
            _server = new LiveScanServer();
            StatusMessage = null;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not listen for a phone: {ex.Message}";
        }
    }

    public void Stop()
    {
        _server?.Dispose();
        _server = null;
        PhonePose = null;
    }

    /// <summary>Applies what has come in. Called by the editor once a frame.</summary>
    public void Update()
    {
        while (_onEditorThread.TryDequeue(out var action))
        {
            action();
        }

        if (_server != null)
        {
            // A cap per frame keeps a burst (a phone catching up on a whole room) from stalling one frame.
            for (var n = 0; n < 200 && _server.Received.TryDequeue(out var message); n++)
            {
                Apply(message);
            }
        }

        for (var n = 0; n < 32 && _built.TryDequeue(out var built); n++)
        {
            Show(built.Id, built.Data, built.Min, built.Max);
        }

        var now = Environment.TickCount64;
        if (now - _lastCount > 250)
        {
            _lastCount = now;
            lock (_lock)
            {
                PieceCount = _pieces.Count;
                TriangleCount = _pieces.Values.Sum(p => p.TriangleCount);
                PhotoCount = _keyframes.Count;
            }
        }

        // The floor settles as more of it is scanned: check now and then, and lift the scan to stand on y = 0.
        if (now - _lastFloorCheck > 2000)
        {
            _lastFloorCheck = now;
            ScanMeshPiece[] pieces;
            lock (_lock)
            {
                pieces = _pieces.Values.ToArray();
            }
            if (pieces.Length > 0)
            {
                Offset = new Vector3(0, ScanGltfExporter.RecenterOffset(pieces).Y, 0);
                if (_root != null)
                {
                    _root.Position = Offset;
                }
            }
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _pieces.Clear();
            _keyframes.Clear();
            _dirty.Clear();
        }
        while (_built.TryDequeue(out _))
        {
        }
        lock (_context.World.SceneLock)
        {
            foreach (var node in _nodes.Values)
            {
                node.Parent?.RemoveChild(node);
            }
        }
        _nodes.Clear();
        PhonePose = null;
        PieceCount = 0;
        TriangleCount = 0;
        PhotoCount = 0;
    }

    /// <summary>
    /// The full textured export, from everything streamed so far, into <paramref name="path"/>. Returns where to place
    /// the GLB so it lands exactly where the live scan was (the export recentres it).
    /// </summary>
    public async Task<Vector3> BakeAsync(string path, ScanDetail detail, IProgress<float>? progress)
    {
        ScanMeshPiece[] pieces;
        ScanKeyframe[] keyframes;
        lock (_lock)
        {
            pieces = _pieces.Values.ToArray();
            keyframes = _keyframes.ToArray();
        }
        if (pieces.Length == 0)
        {
            throw new InvalidOperationException("Nothing has been scanned yet.");
        }
        Baking = true;
        try
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var options = ScanDetailSettings.Export(detail, name);
            options.Progress = progress;
            options.EncodeImage = EncodeJpeg;
            var recenter = ScanGltfExporter.RecenterOffset(pieces);
            await Task.Run(() => ScanGltfExporter.SaveGlb(path, pieces, keyframes, options));
            return Offset - recenter;
        }
        finally
        {
            Baking = false;
        }
    }

    /// <summary>
    /// Bakes in the background, then calls <paramref name="done"/> on the editor thread with the file and where to put
    /// it, or <paramref name="failed"/> with what went wrong.
    /// </summary>
    public void Bake(string path, ScanDetail detail, IProgress<float>? progress, Action<string, Vector3> done, Action<string> failed)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var at = await BakeAsync(path, detail, progress);
                _onEditorThread.Enqueue(() => done(path, at));
            }
            catch (Exception ex)
            {
                _onEditorThread.Enqueue(() => failed(ex.Message));
            }
        });
    }

    public void Dispose()
    {
        _stop.Cancel();
        _work.Release();
        Stop();
    }

    private void Apply(ScanStreamMessage message)
    {
        switch (message)
        {
            case ScanPieceMessage { Piece: var piece }:
                lock (_lock)
                {
                    _pieces[piece.Id] = piece;
                    _dirty.Add(piece.Id);
                }
                _work.Release();
                break;
            case ScanRemoveMessage remove:
                lock (_lock)
                {
                    _pieces.Remove(remove.Id);
                    _dirty.Remove(remove.Id);
                }
                Show(remove.Id, null, default, default);
                break;
            case ScanPoseMessage pose:
                PhonePose = pose.CameraToWorld * Matrix4x4.CreateTranslation(Offset);
                break;
            case ScanKeyframeMessage photo:
                // Decoding a JPEG is for the worker, not the frame.
                _ = Task.Run(() => AddPhoto(photo));
                break;
            case ScanClearMessage:
                Clear();
                break;
        }
    }

    private void AddPhoto(ScanKeyframeMessage photo)
    {
        ScanKeyframe keyframe;
        try
        {
            keyframe = photo.ToKeyframe(DecodeRgb);
        }
        catch (Exception ex)
        {
            ManaLog.WriteDiagnostic($"[LiveScan] A photo could not be read: {ex.Message}");
            return;
        }
        lock (_lock)
        {
            _keyframes.Add(keyframe);
            // Only pieces this photo can see change colour.
            foreach (var piece in _pieces.Values)
            {
                var (centre, radius) = ScanColorizer.Bounds(piece.Positions);
                if (ScanColorizer.Candidates(centre, radius, new[] { keyframe }).Count > 0)
                {
                    _dirty.Add(piece.Id);
                }
            }
        }
        _work.Release();
    }

    private async Task ColourLoop()
    {
        while (!_stop.IsCancellationRequested)
        {
            await _work.WaitAsync();
            while (!_stop.IsCancellationRequested)
            {
                ScanMeshPiece? piece = null;
                ScanKeyframe[] keyframes;
                lock (_lock)
                {
                    var id = _dirty.FirstOrDefault();
                    if (id == Guid.Empty || !_pieces.TryGetValue(id, out piece))
                    {
                        _dirty.Remove(id);
                        if (_dirty.Count == 0)
                        {
                            break;
                        }
                        continue;
                    }
                    _dirty.Remove(id);
                    keyframes = _keyframes.ToArray();
                }
                try
                {
                    _built.Enqueue(Build(piece, keyframes));
                }
                catch (Exception ex)
                {
                    ManaLog.WriteDiagnostic($"[LiveScan] Could not colour a piece: {ex.Message}");
                }
            }
        }
    }

    private (Guid, ManaMeshCpuData?, Vector3, Vector3) Build(ScanMeshPiece piece, ScanKeyframe[] keyframes)
    {
        Vector3[] colours;
        if (_colour == Colouring.Coverage)
        {
            colours = ScanCoverage.Score(piece, keyframes).Select(s => { var c = ScanCoverage.Colour(s); return new Vector3(c.X, c.Y, c.Z); }).ToArray();
        }
        else if (keyframes.Length > 0)
        {
            colours = ScanColorizer.Colorize(piece, keyframes);
        }
        else
        {
            colours = ScanColorizer.VertexSurfaces(piece).Select(ScanColorizer.SurfaceColor).ToArray();
        }

        var count = piece.Positions.Length;
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var p in piece.Positions)
        {
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }
        var data = new ManaMeshCpuData
        {
            Positions = piece.Positions,
            Normals = piece.Normals,
            Colors = colours.Select(c => new Vector4(c, 1)).ToArray(),
            Indexes = piece.Indices.Select(i => (uint)i).ToArray(),
            TextureCoordinates0 = new Vector2[count],
            TextureCoordinates1 = new Vector2[count],
        };
        return (piece.Id, data, min, max);
    }

    private void Show(Guid id, ManaMeshCpuData? data, Vector3 min, Vector3 max)
    {
        if (data == null || data.Positions.Length == 0)
        {
            if (_nodes.Remove(id, out var gone))
            {
                lock (_context.World.SceneLock)
                {
                    gone.Parent?.RemoveChild(gone);
                }
            }
            return;
        }
        lock (_lock)
        {
            // Removed while it was being coloured.
            if (!_pieces.ContainsKey(id))
            {
                return;
            }
        }

        if (_nodes.TryGetValue(id, out var node) && node.GetComponent<MeshRendererComponent>()?.Mesh is { } existing)
        {
            existing.SetCpuData(data);
            existing.SetSize(min, max);
            return;
        }

        var mesh = new ManaMesh(_context) { Name = "Live Scan" };
        mesh.SetCpuData(data);
        mesh.SetSize(min, max);
        mesh.Materials = new[] { Material() };
        node = new ManaSceneNode(_context, Matrix4x4.Identity)
        {
            Name = "Live Scan Piece",
            IsEditorTransient = true,
            IncludeInEnvironmentCapture = false,
        };
        node.Components.Add(new MeshRendererComponent(node, mesh));
        lock (_context.World.SceneLock)
        {
            Root().AddChild(node);
        }
        _nodes[id] = node;
    }

    private ManaSceneNode Root()
    {
        if (_root == null)
        {
            _root = new ManaSceneNode(_context, Matrix4x4.Identity)
            {
                Name = "Live Scan",
                IsEditorTransient = true,
                IncludeInEnvironmentCapture = false,
                Position = Offset,
            };
            _context.World.DefaultScene.Children.Add(_root);
        }
        return _root;
    }

    // Unlit: the colours come from photos that already carry the room's light. White, because the vertex colours
    // are multiplied by it and a new material's own colour is not.
    private ManaMaterial Material()
    {
        if (_material == null)
        {
            _material = new ManaMaterial(_context)
            {
                Name = "Live Scan",
                Shader = new ManaPbrShader(_context),
                Unlit = true,
            };
            _material.Albedo.Value = Vector4.One;
        }
        return _material;
    }

    private static byte[] DecodeRgb(byte[] encoded)
    {
        var image = ManaImageCodec.Load(encoded);
        var rgb = new byte[image.Width * image.Height * 3];
        for (int i = 0, j = 0; i < rgb.Length; i += 3, j += 4)
        {
            rgb[i] = image.Pixels[j];
            rgb[i + 1] = image.Pixels[j + 1];
            rgb[i + 2] = image.Pixels[j + 2];
        }
        return rgb;
    }

    private static byte[] EncodeJpeg(int width, int height, byte[] rgb)
    {
        using var stream = new MemoryStream();
        new StbImageWriteSharp.ImageWriter().WriteJpg(rgb, width, height, StbImageWriteSharp.ColorComponents.RedGreenBlue, stream, 88);
        return stream.ToArray();
    }
}
#endif
