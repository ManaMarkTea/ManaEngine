using ManaEditor.Code.GridMaps;
using BepuPhysics.Collidables;
using ManaUI;
using System.Linq;
using Mana.Code.Components;
using Mana.Code.Nodes;
using Mana.Code.Shapes;
using Mana.Code.UI;
using ManaEditor.Code.Services;
using ManaEditor.Code.Tools;
using ManaEditor.Code.Assets;
using ManaEditor.Code.Levels;
using ManaEditor.Code.Search;
using ManaEditor.Code.UI;
using ManaEditor.Code.UI.ManaUi;
using ManaCore.Diagnostics;
using ManaCore.Graphics;
using ManaCore.Loading;
using ManaGraphics.Code.Diagnostics;
using ManaGraphics.Code.Graphics;
using ManaGeoTools.Code;
using ManaGeoTools.Code.Planet;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;

namespace Mana.Code.GameModes
{
    // Placing a model dragged or clicked out of the Import panel: the live preview, its rotation, commit and cancel.
    internal sealed class AssetPlacementController(
        ManaContext context, VoxelEditorViewModel viewModel, GridMapInputController gridMapInput,
        Func<UiRoot?> rootProvider, Func<RayHit> placementHit, Func<bool> openingLevel,
        Func<bool> uiWantsMouse, Func<bool> uiWantsKeyboard)
    {
        /// <summary>
        /// A model dragged out of the asset browser and let go over the Grid Map window's palette joins the
        /// selected map's palette as the brush -- painting or not. True when that happened.
        /// </summary>
        private bool TakeGridPaletteDrop(IMouse mouse)
        {
            var leftDown = mouse.IsButtonPressed(MouseButton.Left);
            if (leftDown || !_meshPickerDragWasDown
                || MeshImportRequests.Dragging == null
                || Path.GetExtension(MeshImportRequests.Dragging) == ManaEditor.Code.Assets.ManaPrefab.Extension
                || context.World.Selection.SelectedNode is not ManaGridMapNode map
                || rootProvider() is not { } root
                || !GridMapToolManaView.IsOverPalette(root.Input.PointerPosition))
            {
                return false;
            }

            gridMapInput.AddAllToGridPalette(map, MeshImportRequests.TakeDragAll());

            MeshAssetRow.ForceEndActiveDrag();
            _meshPickerDragWasDown = false;
            return true;
        }

        /// <summary>
        /// While painting, choosing a model in the asset browser adds it to the palette as the brush instead of
        /// placing it. True when that happened, so the ordinary placement path stands down.
        /// </summary>
        private bool TakeGridPaletteRequest(IMouse mouse)
        {
            var target = GridMapEditing.Target;
            if (target == null
                || Path.GetExtension(MeshImportRequests.PendingPlacement) == ManaEditor.Code.Assets.ManaPrefab.Extension
                || Path.GetExtension(MeshImportRequests.Dragging) == ManaEditor.Code.Assets.ManaPrefab.Extension
                || _meshPickerPreviewNode?.PrefabLink != null)
            {
                return false;
            }

            if (MeshImportRequests.TakePlacement() is { Length: > 0 } picked)
            {
                gridMapInput.AddToGridPalette(target, picked);
            }

            gridMapInput.AddAllToGridPalette(target, MeshImportRequests.TakePaletteAdd());

            var leftDown = mouse.IsButtonPressed(MouseButton.Left);
            if (!leftDown && _meshPickerDragWasDown && MeshImportRequests.Dragging != null)
            {
                var overUi = MeshImportRequests.PointerOverUi;
                var dropped = MeshImportRequests.TakeDragAll();
                if (!overUi)
                {
                    gridMapInput.AddAllToGridPalette(target, dropped);
                }

                MeshAssetRow.ForceEndActiveDrag();
            }

            _meshPickerDragWasDown = leftDown;
            return true;
        }

public ManaSceneNode? PreviewNode => _meshPickerPreviewNode;
        private string? _meshDownloadPath;
        private string? _meshDownloadFailedPath;
        private int _meshDownloadGeneration;
        private bool _meshPickerDragWasDown;
        private bool _meshPickerPlaceClickWasDown;
        private bool _meshPickerEscapeWasDown;
        private string? _meshPickerPreviewAssetPath;
        private ManaSceneNode? _meshPickerPreviewNode;
        private int _meshPickerPreviewSceneStartIndex = -1;
        private int _meshPickerPreviewMeshStartIndex = -1;
        private int _meshPickerPreviewMaterialStartIndex = -1;
        private EditorSelectionSnapshot? _meshPickerPreviewSelectionBefore;

        public void ProcessMeshPickerPlacement(Silk.NET.Input.IMouse mouseState, Vector2 sceneMin, Vector2 sceneMax)
        {
            if (HandleMeshPickerEscapeCancel())
            {
                MeshImportRequests.Clear();
            }

            if (TakeGridPaletteDrop(mouseState) || TakeGridPaletteRequest(mouseState))
            {
                return;
            }

            // MeshImportRequests, fed by VoxelEditorPanelsManaView, is the only source of a placement now.
            var pendingPath = MeshImportRequests.TakePlacement();
            if (!string.IsNullOrWhiteSpace(pendingPath))
            {
                _meshDownloadFailedPath = null;
                BeginMeshPickerPlacementPreview(pendingPath);
            }

            var leftMouseDown = mouseState.IsButtonPressed(Silk.NET.Input.MouseButton.Left);
            if (leftMouseDown && !_meshPickerDragWasDown) _meshDownloadFailedPath = null;
            var mousePosition = mouseState.Position;
            var isOverScene = mousePosition.X >= sceneMin.X &&
                              mousePosition.X <= sceneMax.X &&
                              mousePosition.Y >= sceneMin.Y &&
                              mousePosition.Y <= sceneMax.Y;
            var draggedPath = MeshImportRequests.Dragging;

            // The scene rectangle is the whole ImGui viewport, which the ManaUI columns sit on top of -- so
            // "over the scene" is true above a panel as well. The picker reports what is actually under the
            // cursor, and only that says whether there is scene there to drop onto.
            var overPanel = MeshImportRequests.PointerOverUi;
            var canDrop = isOverScene && !overPanel;

            if (!string.IsNullOrWhiteSpace(draggedPath) && canDrop && draggedPath != _meshDownloadFailedPath)
            {
                BeginMeshPickerPlacementPreview(draggedPath);
            }

            if (_meshPickerPreviewNode != null && canDrop)
            {
                UpdateMeshPickerPlacementPreviewTransform();
            }

            if (!leftMouseDown && _meshPickerDragWasDown)
            {
                if (!string.IsNullOrWhiteSpace(draggedPath))
                {
                    if (canDrop && string.Equals(draggedPath, _meshPickerPreviewAssetPath, StringComparison.OrdinalIgnoreCase))
                    {
                        CommitMeshPickerPlacementPreview();
                    }
                    else if (canDrop && string.Equals(draggedPath, _meshDownloadPath, StringComparison.OrdinalIgnoreCase))
                    {
                        // Keep the requested preview alive until its download completes. The next click
                        // places it; releasing a drag early must not silently discard the selection.
                        viewModel.Levels.SetStatus("Downloading asset — click to place it when ready.");
                    }
                    else
                    {
                        CancelMeshPickerPlacementPreview();
                    }

                    MeshImportRequests.TakeDrag();
                }

                // Polled real button state rather than the row's own PointerReleased, which sometimes never
                // fires (window loses focus mid-drag, the release lands outside the window) -- leaving that
                // row's ghost floating in the viewport and its pointer capture stuck until something else
                // happened to drag over the same row again. This runs every up-edge regardless of whether a
                // mailbox drag was pending, since the two can desync.
                MeshAssetRow.ForceEndActiveDrag();
            }

            var placeClick = leftMouseDown && !_meshPickerPlaceClickWasDown;
            _meshPickerPlaceClickWasDown = leftMouseDown;
            if (placeClick &&
                _meshPickerPreviewNode != null &&
                string.IsNullOrWhiteSpace(draggedPath) &&
                isOverScene &&
                !uiWantsMouse())
            {
                CommitMeshPickerPlacementPreview();
            }

            _meshPickerDragWasDown = leftMouseDown;
        }

        private bool HandleMeshPickerEscapeCancel()
        {
            var keyboard = context.Input.Keyboards.FirstOrDefault();
            if (keyboard == null)
            {
                _meshPickerEscapeWasDown = false;
                return false;
            }

            var keyDown = !uiWantsKeyboard() && keyboard.IsKeyPressed(Silk.NET.Input.Key.Escape);
            var pressed = keyDown && !_meshPickerEscapeWasDown;
            _meshPickerEscapeWasDown = keyDown;

            if (!pressed)
            {
                return false;
            }

            CancelMeshPickerPlacementPreview();
            return true;
        }

        private async void BeginMeshPickerPlacementPreview(string assetReference)
        {
            if (openingLevel() || _meshDownloadPath == assetReference) return;
            if (_meshPickerPreviewNode != null &&
                string.Equals(_meshPickerPreviewAssetPath, assetReference, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            CancelMeshPickerPlacementPreview();

            if (ManaCore.Assets.ManaAssetCatalog.Current is { } catalog && (Path.GetExtension(assetReference) == ManaPrefab.Extension || !catalog.IsReady(assetReference)))
            {
                _meshDownloadPath = assetReference;
                var generation = _meshDownloadGeneration;
                try
                {
                    viewModel.Levels.SetStatus("Downloading selected asset…");
                    if (Path.GetExtension(assetReference) == ManaPrefab.Extension)
                    {
                        var contentRoot = ManaContentLibrary.RootFor(context.World);
                        await ManaEditor.Code.Assets.ManaLevelAssetPreloader.EnsureAsync(new ManaLevel { Nodes = [ManaPrefab.Load(assetReference, contentRoot).Root] }, contentRoot);
                    }
                    else
                    {
                        await catalog.EnsureAsync(assetReference);
                    }
                    if (generation != _meshDownloadGeneration) return;
                    viewModel.Levels.SetStatus("Asset ready — click in the scene to place it.");
                }
                catch (Exception ex)
                {
                    if (generation == _meshDownloadGeneration)
                    {
                        _meshDownloadFailedPath = assetReference;
                        viewModel.Levels.SetStatus($"Download failed: {ex.Message}. Select the asset again to retry.");
                    }
                    return;
                }
                finally
                {
                    if (generation == _meshDownloadGeneration) _meshDownloadPath = null;
                }
            }

            var world = context.World;
            var sceneStart = world.Scenes.Count;
            var meshStart = world.Meshes.Count;
            var materialStart = world.Materials.Count;

            try
            {
                _meshPickerPreviewSelectionBefore = EditorSelectionSnapshot.Capture(world.Selection);
                var previewNode = LoadMeshPickerAsset(assetReference);
                _meshPickerPreviewAssetPath = assetReference;
                _meshPickerPreviewNode = previewNode;
                if (previewNode.PrefabLink != null) { previewNode.IsEditorTransient = true; }
                _meshPickerPreviewSceneStartIndex = sceneStart;
                _meshPickerPreviewMeshStartIndex = meshStart;
                _meshPickerPreviewMaterialStartIndex = materialStart;
                UpdateMeshPickerPlacementPreviewTransform();
            }
            catch (Exception ex)
            {
                RemoveMeshPickerPreviewWorldData(sceneStart, meshStart, materialStart);
                _meshPickerPreviewAssetPath = null;
                _meshPickerPreviewNode = null;
                _meshPickerPreviewSceneStartIndex = -1;
                _meshPickerPreviewMeshStartIndex = -1;
                _meshPickerPreviewMaterialStartIndex = -1;
                _meshPickerPreviewSelectionBefore = null;
                Trace.TraceWarning($"Failed to start mesh placement preview for '{assetReference}': {ex}");
                ManaConsole.WriteDiagnostic($"Failed to start mesh placement preview for '{assetReference}': {ex}");

                // Said out loud, not only to a log file nobody has open. A drag that produces no preview and
                // no message is indistinguishable from a drag the editor never noticed -- which is exactly
                // what an unreadable asset looked like: you drop it, nothing appears, nothing explains.
                viewModel.Levels.SetStatus(
                    $"{System.IO.Path.GetFileName(assetReference)}: {DescribeImportFailure(ex)}");
            }
        }

        /// <summary>
        /// An import failure in one line. A NotSupportedException from the glTF loader already carries a
        /// sentence written for this; anything else gets its own message, which is at least specific.
        /// </summary>
        private static string DescribeImportFailure(Exception ex)
        {
            var innermost = ex;
            while (innermost.InnerException != null)
            {
                innermost = innermost.InnerException;
            }

            if (ex is NotSupportedException || innermost is NotSupportedException)
            {
                var message = (innermost as NotSupportedException ?? (NotSupportedException)ex).Message;

                // The loader's message already names the file; the caller prefixes it again.
                var because = message.IndexOf(" because ", StringComparison.Ordinal);
                return because >= 0 ? message[(because + " because ".Length)..] : message;
            }

            return $"could not be imported ({innermost.GetType().Name}: {innermost.Message})";
        }

        private void UpdateMeshPickerPlacementPreviewTransform()
        {
            if (_meshPickerPreviewNode == null)
            {
                return;
            }

            var mouseHit = placementHit();
            if (!mouseHit.Hit)
            {
                return;
            }

            // Scroll while positioning spins the preview instead of zooming the camera --
            // ShouldCaptureEditorMouseInput reports this preview as an editor mouse capture for exactly that
            // reason. The spin is written into the Placement panel's Rotation, so what the panel shows is
            // what the next placement will use, rather than a hidden extra angle nobody can see or reset.
            var scrollY = context.Input.Mice.FirstOrDefault()?.ScrollWheels[0].Y ?? 0.0f;
            if (scrollY != 0.0f)
            {
                SpinPlacementRotation(scrollY, mouseHit.Normal);
            }

            _meshPickerPreviewNode.Position = viewModel.Snap.SnapPosition(mouseHit.Position) + viewModel.ImportPositionOffset;

            _meshPickerPreviewNode.Rotation = new Vector3(
                viewModel.ImportRotationDegrees.X.ToRadians(),
                viewModel.ImportRotationDegrees.Y.ToRadians(),
                viewModel.ImportRotationDegrees.Z.ToRadians());

            _meshPickerPreviewNode.Scale = new Vector3(
                MathF.Max(0.01f, viewModel.ImportScale.X),
                MathF.Max(0.01f, viewModel.ImportScale.Y),
                MathF.Max(0.01f, viewModel.ImportScale.Z));
            _meshPickerPreviewNode.UpdateTransform();
        }

        /// <summary>
        /// Turns the Placement rotation by <paramref name="notches"/> scroll notches around the surface the
        /// cursor is over. A notch is the rotation snap step while snapping is on, and 15 degrees otherwise.
        /// </summary>
        private void SpinPlacementRotation(float notches, Vector3 surfaceNormal)
        {
            var snap = viewModel.Snap;
            var degrees = notches * (snap.Enabled ? snap.RotationStepDegrees : 15.0f);
            var rotation = viewModel.ImportRotationDegrees;

            if (surfaceNormal == Vector3.Zero || MathF.Abs(surfaceNormal.Y) > 0.999f)
            {
                // The common case -- a floor, or nothing under the cursor -- is a plain yaw, kept as one
                // exact number on Y instead of round-tripping through a quaternion and picking up noise on
                // the other two axes. A ceiling faces down, so the same notch spins the other way round.
                var yaw = rotation.Y + (surfaceNormal.Y < 0.0f ? -degrees : degrees);
                yaw = snap.SnapRotationDegrees(yaw);
                rotation.Y = yaw - 360.0f * MathF.Round(yaw / 360.0f);
            }
            else
            {
                // A wall: spin about its normal, as before, and read the result back as the three angles the
                // panel shows.
                var current = new Vector3(
                    rotation.X.ToRadians(),
                    rotation.Y.ToRadians(),
                    rotation.Z.ToRadians()).ToQuaternionFromEuler();
                var spin = Quaternion.CreateFromAxisAngle(Vector3.Normalize(surfaceNormal), degrees.ToRadians());
                var euler = Quaternion.Concatenate(current, spin).ToEuler();
                rotation = new Vector3(
                    float.RadiansToDegrees(euler.X),
                    float.RadiansToDegrees(euler.Y),
                    float.RadiansToDegrees(euler.Z));
            }

            viewModel.ImportRotationDegrees = rotation;
        }

        private void CommitMeshPickerPlacementPreview()
        {
            var world = context.World;
            var placedNode = _meshPickerPreviewNode;
            var assetPath = _meshPickerPreviewAssetPath;
            if (placedNode != null &&
                _meshPickerPreviewSceneStartIndex >= 0 &&
                _meshPickerPreviewMeshStartIndex >= 0 &&
                _meshPickerPreviewMaterialStartIndex >= 0)
            {
                if (placedNode.PrefabLink != null) { placedNode.IsEditorTransient = false; }
                var insertion = EditorWorldInsertion.Capture(
                    world,
                    _meshPickerPreviewSceneStartIndex,
                    _meshPickerPreviewMeshStartIndex,
                    _meshPickerPreviewMaterialStartIndex,
                    placedNode);
                var selectionBefore = _meshPickerPreviewSelectionBefore
                    ?? EditorSelectionSnapshot.Capture(world.Selection);

                EditorSelectionSnapshot.SelectNode(world.Selection, placedNode, sceneGraphChanged: true);
                if (placedNode.PrefabLink != null)
                {
                    // Prefabs may own physics and audio; the mesh-only insertion undo cannot restore those yet.
                    viewModel.History.Clear();
                }
                else if (!insertion.IsEmpty)
                {
                    var displayName = !string.IsNullOrWhiteSpace(assetPath)
                        ? MeshAssetLibrary.DisplayName(assetPath)
                        : placedNode.Name ?? "Actor";
                    viewModel.History.RecordWorldInsertion(
                        insertion,
                        placedNode,
                        selectionBefore,
                        $"Place {displayName}",
                        world.Selection);
                }
            }

            _meshPickerPreviewAssetPath = null;
            _meshPickerPreviewNode = null;
            _meshPickerPreviewSceneStartIndex = -1;
            _meshPickerPreviewMeshStartIndex = -1;
            _meshPickerPreviewMaterialStartIndex = -1;
            _meshPickerPreviewSelectionBefore = null;
        }

        public void CancelMeshPickerPlacementPreview()
        {
            _meshDownloadGeneration++;
            _meshDownloadPath = null;
            if (_meshPickerPreviewSceneStartIndex >= 0 ||
                _meshPickerPreviewMeshStartIndex >= 0 ||
                _meshPickerPreviewMaterialStartIndex >= 0)
            {
                RemoveMeshPickerPreviewWorldData(
                    _meshPickerPreviewSceneStartIndex,
                    _meshPickerPreviewMeshStartIndex,
                    _meshPickerPreviewMaterialStartIndex,
                    _meshPickerPreviewNode);
            }

            _meshPickerPreviewAssetPath = null;
            _meshPickerPreviewNode = null;
            _meshPickerPreviewSceneStartIndex = -1;
            _meshPickerPreviewMeshStartIndex = -1;
            _meshPickerPreviewMaterialStartIndex = -1;
            _meshPickerPreviewSelectionBefore = null;
        }

        /// <summary>
        /// A node drawing the asset, for a sprite asset or a picture: a sheet playing its first animation, a 9-slice
        /// three tiles wide, or a picture as a Sprite. Saved with the level like any placed group. Null for a model.
        /// </summary>
        private ManaSceneNode? SpriteNodeFor(string assetReference)
        {
            var extension = Path.GetExtension(assetReference);
            ManaSceneNode Node(string name) => new(context, Matrix4x4.Identity)
            {
                Name = name, LevelSource = new Mana.Code.Levels.ManaLevelSource { Kind = Mana.Code.Levels.ManaLevelSourceKind.Group }
            };

            ManaSceneNode? node = null;
            if (extension == Mana.Code.Content.ManaSpriteSheet.Extension)
            {
                node = Node(Path.GetFileNameWithoutExtension(assetReference));
                var sheet = Mana.Code.Content.ManaContentAssets.Load<Mana.Code.Content.ManaSpriteSheet>(assetReference);
                node.Components.Add(new ManaSpriteSheetComponent(node)
                {
                    Sheet = Mana.Code.Content.ManaContentAssets.Reference(assetReference), Animation = sheet?.Animations.FirstOrDefault()?.Name ?? string.Empty
                });
            }
            else if (extension == Mana.Code.Content.ManaSpriteSlice.Extension)
            {
                node = Node(Path.GetFileNameWithoutExtension(assetReference));
                node.Components.Add(new ManaSprite9SliceComponent(node) { Slice = Mana.Code.Content.ManaContentAssets.Reference(assetReference) });
            }
            else if (MeshAssetLibrary.FormatOf(assetReference) == MeshAssetFormats.Png)
            {
                node = Node(Path.GetFileNameWithoutExtension(MeshAssetLibrary.FileName(assetReference)));
                node.Components.Add(new ManaSpriteComponent(node) { Texture = MeshAssetLibrary.SpritePicture(assetReference) });
            }

            if (node != null)
            {
                var scene = viewModel.ActiveScene ?? context.World.DefaultScene;
                lock (context.World.SceneLock)
                {
                    scene.Children.Add(node);
                }
                node.UpdateTransform();
            }
            return node;
        }

        private ManaSceneNode LoadMeshPickerAsset(string assetReference)
        {
            if (Path.GetExtension(assetReference) == ManaPrefab.Extension)
            {
                return ProjectAssetViewModel.For(context.World, viewModel).InstantiateForPlacement(assetReference);
            }
            if (SpriteNodeFor(assetReference) is { } sprite)
            {
                return sprite;
            }
            // Straight into the scene the editor is working in, not a scene of its own per drag.
            var scene = viewModel.ActiveScene ?? context.World.DefaultScene;

            if (TryDecodeZipAssetReference(assetReference, out var zipPath, out var entryPath))
            {
                using var archive = ZipFile.OpenRead(zipPath);
                var entry = archive.GetEntry(entryPath);
                if (entry == null)
                {
                    throw new FileNotFoundException($"Entry '{entryPath}' not found in zip '{zipPath}'.", zipPath);
                }

                using var entryStream = entry.Open();
                using var memory = new MemoryStream();
                entryStream.CopyTo(memory);
                var sidecarResolver = ZipModelSidecars.CreateZipSidecarResolver(archive, entryPath);
                return context.World.LoadFromGltfMemory(memory.ToArray(), entryPath, sidecarResolver, into: scene);
            }

            return context.World.LoadFromGltf(assetReference, into: scene);
        }

        private void RemoveMeshPickerPreviewWorldData(
            int sceneStart, int meshStart, int materialStart, ManaSceneNode? previewNode = null)
        {
            var world = context.World;
            lock (world.SceneLock)
            {
                // The preview lives in an existing scene now, so it has to be taken out of it by hand.
                if (previewNode != null)
                {
                    if (previewNode.PrefabLink != null) { ManaPrefabRuntime.Destroy(previewNode); }
                    foreach (var scene in world.Scenes)
                    {
                        scene.Children.Remove(previewNode);
                    }
                }

                if (sceneStart >= 0 && sceneStart < world.Scenes.Count)
                {
                    world.Scenes.RemoveRange(sceneStart, world.Scenes.Count - sceneStart);
                }

                if (meshStart >= 0 && meshStart < world.Meshes.Count)
                {
                    world.Meshes.RemoveRange(meshStart, world.Meshes.Count - meshStart);
                }

                if (materialStart >= 0 && materialStart < world.Materials.Count)
                {
                    world.Materials.RemoveRange(materialStart, world.Materials.Count - materialStart);
                }
            }
        }

        /// <summary>
        /// Delegated rather than implemented here. Two copies of this encoding is exactly what broke mesh
        /// import: the picker was rewritten with a different separator, this decoder rejected everything it
        /// produced, and every archived mesh was passed on as a filename and failed to open.
        /// </summary>
        private static bool TryDecodeZipAssetReference(string assetReference, out string zipPath, out string entryPath) =>
            ZipAssetReferences.TryDecodeZip(assetReference, out zipPath, out entryPath);

    }
}
