using System.Numerics;

namespace ManaLidar;

/// <summary>
/// Everything a scan has gathered so far: the latest mesh of every anchor and the keyframes to colour it from.
/// ARKit calls in from its own queue while the UI reads counts, so all of it is behind one lock.
/// </summary>
public sealed class ScanRecording
{
    private readonly object _lock = new();
    private readonly Dictionary<Guid, ScanMeshPiece> _pieces = new();
    private readonly List<ScanKeyframe> _keyframes = new();
    private Matrix4x4? _lastKeyframePose;
    private double _lastKeyframeTime = double.NegativeInfinity;

    /// <summary>A new photo once the phone has moved this far (metres)...</summary>
    public float KeyframeMinMove { get; set; } = 0.12f;
    /// <summary>...or turned this far (degrees)...</summary>
    public float KeyframeMinTurnDegrees { get; set; } = 8f;
    /// <summary>...but never more often than this (seconds).</summary>
    public double KeyframeMinInterval { get; set; } = 0.25;
    /// <summary>Past this many, every other keyframe is dropped and the spacing doubled, so a long scan fits in memory.</summary>
    public int MaxKeyframes { get; set; } = 400;

    public int KeyframeCount
    {
        get { lock (_lock) { return _keyframes.Count; } }
    }

    public int PieceCount
    {
        get { lock (_lock) { return _pieces.Count; } }
    }

    public int TriangleCount
    {
        get { lock (_lock) { return _pieces.Values.Sum(p => p.TriangleCount); } }
    }

    /// <summary>
    /// What the scan holds in memory: photos (and their depth) are nearly all of it. An estimate of the arrays'
    /// sizes, not of the process, so it can be shown live without asking the runtime anything.
    /// </summary>
    public long EstimatedBytes
    {
        get
        {
            lock (_lock)
            {
                var photos = _keyframes.Sum(k => (long)k.Rgb.Length + (k.Depth?.Length ?? 0) * 4L);
                var mesh = _pieces.Values.Sum(p => p.Positions.Length * 24L + p.Indices.Length * 4L + (p.FaceSurfaces?.Length ?? 0));
                return photos + mesh;
            }
        }
    }

    public void SetPiece(ScanMeshPiece piece)
    {
        lock (_lock)
        {
            _pieces[piece.Id] = piece;
        }
    }

    public void RemovePiece(Guid id)
    {
        lock (_lock)
        {
            _pieces.Remove(id);
        }
    }

    /// <summary>Whether a frame taken from this pose is worth keeping as a keyframe.</summary>
    public bool WantsKeyframe(Matrix4x4 cameraToWorld, double timestamp)
    {
        lock (_lock)
        {
            if (timestamp - _lastKeyframeTime < KeyframeMinInterval)
            {
                return false;
            }
            if (_lastKeyframePose is not { } last)
            {
                return true;
            }
            if (Vector3.Distance(last.Translation, cameraToWorld.Translation) >= KeyframeMinMove)
            {
                return true;
            }
            var lastForward = -new Vector3(last.M31, last.M32, last.M33);
            var forward = -new Vector3(cameraToWorld.M31, cameraToWorld.M32, cameraToWorld.M33);
            var cos = Vector3.Dot(Vector3.Normalize(lastForward), Vector3.Normalize(forward));
            return cos <= MathF.Cos(KeyframeMinTurnDegrees * MathF.PI / 180f);
        }
    }

    public void AddKeyframe(ScanKeyframe keyframe)
    {
        lock (_lock)
        {
            _keyframes.Add(keyframe);
            _lastKeyframePose = keyframe.CameraToWorld;
            _lastKeyframeTime = keyframe.Timestamp;
            if (_keyframes.Count > MaxKeyframes)
            {
                for (var i = _keyframes.Count - 2; i >= 0; i -= 2)
                {
                    _keyframes.RemoveAt(i);
                }
                KeyframeMinMove *= 2;
                KeyframeMinTurnDegrees *= 2;
            }
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _pieces.Clear();
            _keyframes.Clear();
            _lastKeyframePose = null;
            _lastKeyframeTime = double.NegativeInfinity;
        }
    }

    /// <summary>A copy of the lists, for exporting while scanning carries on.</summary>
    public (ScanMeshPiece[] Pieces, ScanKeyframe[] Keyframes) Snapshot()
    {
        lock (_lock)
        {
            return (_pieces.Values.ToArray(), _keyframes.ToArray());
        }
    }
}
