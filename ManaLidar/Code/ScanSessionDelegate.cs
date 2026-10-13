using System.Runtime.InteropServices;
using ARKit;
using SceneKit;

namespace ManaLidar;

/// <summary>
/// Runs on its own queue: keeps the recording's pieces in step with ARKit's mesh anchors, draws each over the
/// camera, and keeps a keyframe whenever the phone has moved enough. The overlay is see-through, so a hole in the
/// mesh shows as a gap, and tinted by photo coverage (ScanCoverage): red where no photo has seen it yet, amber
/// where only from far or askew, green when well covered.
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
    private readonly SCNMaterial _fillMaterial;
    private readonly SCNMaterial _finishedFillMaterial;
    // Each live piece and its per-vertex coverage, so a new photo can raise the scores without a recount.
    private readonly Dictionary<Guid, (ScanMeshPiece Piece, float[] Scores)> _coverage = new();

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
        // Faint: the coverage colour underneath is what to read; the lines only give the surface its shape.
        _wireMaterial.Diffuse.Contents = UIColor.FromRGBA(1f, 1f, 1f, 0.22f);
        _finishedMaterial = new SCNMaterial
        {
            FillMode = SCNFillMode.Lines,
            LightingModelName = SCNLightingModel.Constant,
            DoubleSided = true,
        };
        _finishedMaterial.Diffuse.Contents = UIColor.FromRGBA(1f, 1f, 1f, 0.18f);

        // Vertex colours carry the coverage; the material only makes it see-through.
        _fillMaterial = new SCNMaterial
        {
            LightingModelName = SCNLightingModel.Constant,
            DoubleSided = true,
            Transparency = 0.42f,
            WritesToDepthBuffer = false,
        };
        _fillMaterial.Diffuse.Contents = UIColor.White;
        _finishedFillMaterial = new SCNMaterial
        {
            LightingModelName = SCNLightingModel.Constant,
            DoubleSided = true,
            Transparency = 0.12f,
            WritesToDepthBuffer = false,
        };
        _finishedFillMaterial.Diffuse.Contents = UIColor.White;
    }

    /// <summary>Set by the controller: photos are only worth keeping while a scan is running.</summary>
    public volatile bool Capturing;

    /// <summary>The editor the scan streams to, while one is connected; everything below is sent on to it too.</summary>
    public volatile ScanStreamClient? Stream;
    private double _lastPoseSent;

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
            // Ten times a second is plenty to show where the phone is.
            if (Stream is { } stream && frame.Timestamp - _lastPoseSent >= 0.1)
            {
                _lastPoseSent = frame.Timestamp;
                stream.SendPose(pose, frame.Timestamp);
            }
            if (_recording.WantsKeyframe(pose, frame.Timestamp) && ArkitConvert.CaptureKeyframe(frame, PhotoSize.Width, PhotoSize.Height) is { } keyframe)
            {
                _recording.AddKeyframe(keyframe);
                Stream?.SendKeyframe(keyframe);
                RecolourFor(keyframe);
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
                _coverage.Remove(id);
                // A finished room's anchors go when the next room starts, but the room stays in the building --
                // and in the editor.
                if (!_finishedAnchors.Contains(id))
                {
                    Stream?.SendRemove(id);
                }
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
            _coverage.Clear();
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
                if (node.Geometry is { } fill)
                {
                    fill.FirstMaterial = _finishedFillMaterial;
                }
                foreach (var child in node.ChildNodes)
                {
                    if (child.Geometry is { } wire)
                    {
                        wire.FirstMaterial = _finishedMaterial;
                    }
                }
                _finishedNodes.Add(node);
                _finishedAnchors.Add(id);
            }
            _overlayNodes.Clear();
            _coverage.Clear();
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
            Stream?.SendPiece(piece);
        }
        // A changed mesh is scored afresh against every photo; its vertices are not the ones it had.
        var keyframes = _recording.Snapshot().Keyframes;
        var drawn = updates.Select(piece =>
        {
            var scores = ScanCoverage.Score(piece, keyframes);
            return (piece, scores, Overlay: BuildOverlay(piece, scores));
        }).ToArray();
        EditOverlay(() =>
        {
            foreach (var (piece, scores, overlay) in drawn)
            {
                _coverage[piece.Id] = (piece, scores);
                Show(piece.Id, overlay);
            }
        });
    }

    // A new photo can only improve coverage: fold it into the pieces it sees and redraw those.
    private void RecolourFor(ScanKeyframe keyframe)
    {
        (ScanMeshPiece Piece, float[] Scores)[] pieces;
        lock (_overlayLock)
        {
            pieces = _coverage.Values.ToArray();
        }
        var single = new[] { keyframe };
        var redrawn = new List<(Guid Id, (SCNGeometry Fill, SCNGeometry Wire) Overlay)>();
        foreach (var (piece, scores) in pieces)
        {
            if (ScanCoverage.Improve(piece, scores, single))
            {
                redrawn.Add((piece.Id, BuildOverlay(piece, scores)));
            }
        }
        if (redrawn.Count == 0)
        {
            return;
        }
        EditOverlay(() =>
        {
            foreach (var (id, overlay) in redrawn)
            {
                // Only if the piece is still live: it may have been finished or removed meanwhile.
                if (_overlayNodes.ContainsKey(id))
                {
                    Show(id, overlay);
                }
            }
        });
    }

    // Called inside EditOverlay. The fill is the node's geometry, the faint wireframe a child sharing its outline.
    private void Show(Guid id, (SCNGeometry Fill, SCNGeometry Wire) overlay)
    {
        if (!_overlayNodes.TryGetValue(id, out var node))
        {
            node = new SCNNode();
            node.AddChildNode(new SCNNode());
            _overlayNodes[id] = node;
            _overlayRoot.AddChildNode(node);
        }
        node.Geometry = overlay.Fill;
        node.ChildNodes[0].Geometry = overlay.Wire;
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
    private (SCNGeometry Fill, SCNGeometry Wire) BuildOverlay(ScanMeshPiece piece, float[] scores)
    {
        var vertices = new SCNVector3[piece.Positions.Length];
        var colours = new float[piece.Positions.Length * 4];
        for (var i = 0; i < vertices.Length; i++)
        {
            var p = piece.Positions[i];
            vertices[i] = new SCNVector3(p.X, p.Y, p.Z);
            var c = ScanCoverage.Colour(scores[i]);
            colours[i * 4] = c.X;
            colours[i * 4 + 1] = c.Y;
            colours[i * 4 + 2] = c.Z;
            colours[i * 4 + 3] = 1f;
        }
        var positions = SCNGeometrySource.FromVertices(vertices);
        var colourSource = SCNGeometrySource.FromData(NSData.FromArray(MemoryMarshal.AsBytes(colours.AsSpan()).ToArray()),
            SCNGeometrySourceSemantics.Color, vertices.Length, true, 4, sizeof(float), 0, 4 * sizeof(float));
        var indexData = NSData.FromArray(MemoryMarshal.AsBytes(piece.Indices.AsSpan()).ToArray());
        var element = SCNGeometryElement.FromData(indexData, SCNGeometryPrimitiveType.Triangles, piece.TriangleCount, sizeof(int));

        var fill = SCNGeometry.Create(new[] { positions, colourSource }, new[] { element });
        fill.FirstMaterial = _fillMaterial;
        var wire = SCNGeometry.Create(new[] { positions }, new[] { element });
        wire.FirstMaterial = _wireMaterial;
        return (fill, wire);
    }
}
