using System.Runtime.InteropServices;
using ARKit;
using SceneKit;

namespace ManaLidar;

/// <summary>
/// Runs on its own queue: keeps the recording's pieces in step with ARKit's mesh anchors, draws each as a wireframe
/// over the camera, and keeps a keyframe whenever the phone has moved enough.
/// </summary>
public sealed class ScanSessionDelegate : ARSessionDelegate
{
    private readonly ScanRecording _recording;
    private readonly SCNNode _overlayRoot;
    // Touched from the session queue and, on New Scan, the main thread.
    private readonly object _overlayLock = new();
    private readonly Dictionary<Guid, SCNNode> _overlayNodes = new();
    // Rooms already added to a building: still drawn, dimmed, but no longer ARKit's to update.
    private readonly List<SCNNode> _finishedNodes = new();
    private readonly HashSet<Guid> _finishedAnchors = new();
    private readonly SCNMaterial _wireMaterial;
    private readonly SCNMaterial _finishedMaterial;

    public ScanSessionDelegate(ScanRecording recording, SCNNode overlayRoot)
    {
        _recording = recording;
        _overlayRoot = overlayRoot;
        _wireMaterial = new SCNMaterial
        {
            FillMode = SCNFillMode.Lines,
            LightingModelName = SCNLightingModel.Constant,
            DoubleSided = true,
        };
        _wireMaterial.Diffuse.Contents = UIColor.FromRGBA(0.35f, 0.85f, 1f, 0.55f);
        _finishedMaterial = new SCNMaterial
        {
            FillMode = SCNFillMode.Lines,
            LightingModelName = SCNLightingModel.Constant,
            DoubleSided = true,
        };
        _finishedMaterial.Diffuse.Contents = UIColor.FromRGBA(1f, 1f, 1f, 0.18f);
    }

    /// <summary>Set by the controller: photos are only worth keeping while a scan is running.</summary>
    public volatile bool Capturing;

    /// <summary>Size of the photos kept from now on; each keyframe remembers its own, so this can change mid-scan.</summary>
    public (int Width, int Height) PhotoSize = ScanDetailSettings.PhotoSize(ScanDetail.Standard);

    /// <summary>What to tell the person about tracking ("Move slower"), or null when all is well.</summary>
    public string? TrackingHint { get; private set; }

    public override void DidUpdateFrame(ARSession session, ARFrame frame)
    {
        // A frame not disposed here stays alive until the GC runs, and ARKit stops delivering after a handful.
        using (frame)
        {
            var camera = frame.Camera;
            TrackingHint = camera.TrackingState switch
            {
                ARTrackingState.Normal => null,
                ARTrackingState.NotAvailable => "Tracking unavailable",
                _ => camera.TrackingStateReason switch
                {
                    ARTrackingStateReason.ExcessiveMotion => "Move slower",
                    ARTrackingStateReason.InsufficientFeatures => "Point at something with more detail or light",
                    ARTrackingStateReason.Initializing => "Starting up -- move the phone slowly",
                    ARTrackingStateReason.Relocalizing => "Finding where you were -- go back to a scanned spot",
                    _ => "Tracking limited",
                },
            };

            if (!Capturing || camera.TrackingState != ARTrackingState.Normal)
            {
                return;
            }
            var pose = ArkitConvert.ToNumerics(camera.Transform);
            if (_recording.WantsKeyframe(pose, frame.Timestamp) && ArkitConvert.CaptureKeyframe(frame, PhotoSize.Width, PhotoSize.Height) is { } keyframe)
            {
                _recording.AddKeyframe(keyframe);
            }
        }
    }

    public override void DidAddAnchors(ARSession session, ARAnchor[] anchors) => UpdateMeshes(anchors);

    public override void DidUpdateAnchors(ARSession session, ARAnchor[] anchors) => UpdateMeshes(anchors);

    public override void DidRemoveAnchors(ARSession session, ARAnchor[] anchors)
    {
        EditOverlay(() =>
        {
            foreach (var anchor in anchors.OfType<ARMeshAnchor>())
            {
                var id = new Guid(anchor.Identifier.GetBytes());
                _recording.RemovePiece(id);
                if (_overlayNodes.Remove(id, out var node))
                {
                    node.RemoveFromParentNode();
                }
            }
        });
    }

    /// <summary>Forgets every overlay node, finished rooms too; the controller calls it when a new scan starts.</summary>
    public void ClearOverlay()
    {
        EditOverlay(() =>
        {
            foreach (var node in _overlayNodes.Values.Concat(_finishedNodes))
            {
                node.RemoveFromParentNode();
            }
            _overlayNodes.Clear();
            _finishedNodes.Clear();
            _finishedAnchors.Clear();
        });
    }

    /// <summary>
    /// A room has gone into the building: its wireframe stays on screen, dimmed, so you can see where you have
    /// been, and late updates from its anchors (about to be removed) are ignored.
    /// </summary>
    public void FinishRoom()
    {
        EditOverlay(() =>
        {
            foreach (var (id, node) in _overlayNodes)
            {
                if (node.Geometry is { } geometry)
                {
                    geometry.FirstMaterial = _finishedMaterial;
                }
                _finishedNodes.Add(node);
                _finishedAnchors.Add(id);
            }
            _overlayNodes.Clear();
        });
    }

    private void UpdateMeshes(ARAnchor[] anchors)
    {
        ARMeshAnchor[] current;
        lock (_overlayLock)
        {
            current = anchors.OfType<ARMeshAnchor>()
                .Where(a => !_finishedAnchors.Contains(new Guid(a.Identifier.GetBytes()))).ToArray();
        }
        var updates = current.Select(ArkitConvert.ReadMesh).ToArray();
        foreach (var piece in updates)
        {
            _recording.SetPiece(piece);
        }
        var geometries = updates.Select(piece => (piece.Id, Geometry: BuildWireframe(piece))).ToArray();
        EditOverlay(() =>
        {
            foreach (var (id, geometry) in geometries)
            {
                if (!_overlayNodes.TryGetValue(id, out var node))
                {
                    node = new SCNNode();
                    _overlayNodes[id] = node;
                    _overlayRoot.AddChildNode(node);
                }
                node.Geometry = geometry;
            }
        });
    }

    // Off the main thread there is no run loop to commit SceneKit's implicit transaction, so changes made here
    // could sit unseen; an explicit one shows them on the next frame.
    private void EditOverlay(Action edit)
    {
        lock (_overlayLock)
        {
            SCNTransaction.Begin();
            try
            {
                edit();
            }
            finally
            {
                SCNTransaction.Commit();
            }
        }
    }

    // Pieces are already in world space, so their nodes sit at the scene's origin.
    private SCNGeometry BuildWireframe(ScanMeshPiece piece)
    {
        var vertices = new SCNVector3[piece.Positions.Length];
        for (var i = 0; i < vertices.Length; i++)
        {
            var p = piece.Positions[i];
            vertices[i] = new SCNVector3(p.X, p.Y, p.Z);
        }
        var indexData = NSData.FromArray(MemoryMarshal.AsBytes(piece.Indices.AsSpan()).ToArray());
        var element = SCNGeometryElement.FromData(indexData, SCNGeometryPrimitiveType.Triangles, piece.TriangleCount, sizeof(int));
        var geometry = SCNGeometry.Create(new[] { SCNGeometrySource.FromVertices(vertices) }, new[] { element });
        geometry.FirstMaterial = _wireMaterial;
        return geometry;
    }
}
