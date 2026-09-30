using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mana;
using Mana.Code.Levels;
using Mana.Code.Nodes;
using Mana.Code.UI;
using ManaVoxel.Code.Assets;
using ManaVoxel.Code.GridMaps;
using ManaVoxel.Code.Levels;

namespace ManaVoxel.Code.UI
{
    public sealed record PrefabPropertyChoice(string Label, string NodeId, string? ComponentId, string Property);

    public sealed class ProjectAssetViewModel
    {
        private static readonly ConditionalWeakTable<ManaWorld, ProjectAssetViewModel> Models = new();
        public static ProjectAssetViewModel For(ManaWorld world, VoxelEditorViewModel? editor = null)
        {
            var model = Models.GetValue(world, w => new ProjectAssetViewModel(w));
            model._editor = editor ?? model._editor;
            return model;
        }

        private readonly ManaWorld _world;
        private VoxelEditorViewModel? _editor;
        private List<ManaScene>? _savedScenes;
        private ManaScene? _savedDefault;
        private ManaScene? _savedActive;
        private EditorSelectionSnapshot _savedSelection;
        private SceneNodeTransformState? _savedCamera;
        private ManaScene? _editingScene;
        private ManaPrefab? _editingAsset;
        private string? _editingPath;
        private IReadOnlyList<ManaContentEntry>? _entries;
        private string? _sourceOnOpen;

        // The editor room's walls, ceiling and coves: a prefab is edited on the floor alone, like Unity's prefab stage.
        private static readonly HashSet<string> RoomOnlyNames = new(StringComparer.Ordinal)
        {
            "Grid Plane Ceiling", "Grid Plane YX", "Grid Plane ZY", "Grid Plane YX Far", "Grid Plane ZY Far",
            "Grid Cove X0-Z0", "Grid Cove X0-ZFar", "Grid Cove XFar-ZFar", "Grid Cove XFar-Z0"
        };

        private ProjectAssetViewModel(ManaWorld world) { _world = world; }
        public bool IsEditing => _editingAsset != null;
        public string EditingName => _editingAsset?.Name ?? string.Empty;
        public string Status { get; private set; } = string.Empty;
        public object Token { get; private set; } = new();
        public string AssetName { get; set; } = "New Prefab";
        public string Search { get; set; } = string.Empty;
        public string Folder { get; set; } = string.Empty;
        public string Kind { get; set; } = "All";
        public bool IncludeGenerator { get; set; }
        public IReadOnlyList<ManaContentEntry> Entries
        {
            get
            {
                if (_entries == null)
                {
                    try { _entries = ManaContentLibrary.List(); }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        Status = exception.Message;
                        _entries = [];
                    }
                }
                return _entries;
            }
        }
        public ManaPrefab? EditingAsset => _editingAsset;

        /// <summary>While a prefab is open, the one scene the Outliner shows: the prefab's own, as Unity's prefab stage.</summary>
        public IReadOnlyList<ManaScene>? EditingScenes { get; private set; }
        public ManaSceneNode? Selected => _world.Selection.SelectedNode;
        public ManaSceneNode? SelectedInstance => ManaPrefabRuntime.InstanceRoot(Selected);
        public bool CanEdit => _editor?.Levels.IsPlaying != true && (_editor == null || _editor.Levels.Host == null || _editor.Levels.CanPlay || IsEditing);

        public void Run(Action action)
        {
            try
            {
                if (!CanEdit)
                {
                    throw new InvalidOperationException("Stop play and wait for loading to finish before editing project assets.");
                }
                lock (_world.SceneLock)
                {
                    action();
                }
                Token = new object();
            }
            catch (Exception exception)
            {
                Status = exception.Message;
            }
        }

        public void Refresh() => Run(() => { _entries = ManaContentLibrary.List(); Status = $"{_entries.Count} project assets."; });

        public string? SelectedAsset { get; set; }
        public bool CanCreatePrefab(ManaSceneNode node) => CanEdit && !IsEditing
            && !node.IsEditorTransient && ManaPrefabRuntime.InstanceRoot(node) == null
            && SceneEditorOperations.FindOwningScene(_world, node) != null;

        public void CreatePrefab(ManaSceneNode node)
        {
            if (!CanCreatePrefab(node)) { return; }
            EditorSelectionSnapshot.SelectNode(_world.Selection, node, true);
            var name = string.Concat((node.Name ?? "Prefab").Split(Path.GetInvalidFileNameChars())).Trim().Trim('.');
            if (name.Length == 0) { name = "Prefab"; }
            AssetName = name;
            for (var suffix = 2; File.Exists(Path.Combine(ManaContentLibrary.Root, "Prefabs", AssetName + ManaPrefab.Extension)); suffix++)
            {
                AssetName = $"{name} {suffix}";
            }
            CreatePrefab();
            ManaUi.ManaUiDockDemo.RequestPanel("project");
        }

        public void RequestPlacement(string path) => Run(() =>
        {
            if (IsEditing) { throw new InvalidOperationException("Finish editing the prefab before placing instances."); }
            ManaPrefab.Load(path);
            ManaUi.MeshImportRequests.RequestPlacement(path);
            Status = "Move into the viewport and click to place. Escape cancels.";
        });

        public ManaSceneNode InstantiateForPlacement(string path)
        {
            if (!CanEdit || IsEditing) { throw new InvalidOperationException("Finish editing or stop play before placing a prefab."); }
            var asset = ManaPrefab.Load(path);
            var scene = _editor?.ActiveScene;
            if (scene == null || !_world.Scenes.Contains(scene))
            {
                scene = _world.Scenes.Contains(_world.DefaultScene) ? _world.DefaultScene : _world.Scenes.FirstOrDefault();
            }
            if (scene == null)
            {
                scene = new ManaScene(_world.Context) { Name = "Scene" };
                _world.Scenes.Add(scene);
                _world.DefaultScene = scene;
            }
            return ManaPrefabRuntime.Instantiate(_world.Context, scene, new ManaLevelNode
            {
                Name = asset.Name, Kind = ManaLevelSourceKind.Prefab,
                Prefab = new ManaPrefabReference { AssetId = asset.Id, AssetPath = ManaContentLibrary.Reference(path) }
            });
        }

        public void DeleteAsset(string path) => Run(() =>
        {
            var full = Path.GetFullPath(path);
            if (!full.StartsWith(Path.GetFullPath(ManaContentLibrary.Root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || !Entries.Any(entry => string.Equals(Path.GetFullPath(entry.Path), full, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("Choose an asset inside Content.");
            }
            if (string.Equals(_editingPath, full, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Close this prefab before deleting its source.");
            }
            File.Delete(full);
            SelectedAsset = null;
            _entries = ManaContentLibrary.List();
            Status = $"Deleted {Path.GetFileNameWithoutExtension(full)}. Placed instances remain in the world.";
        });

        /// <summary>
        /// Makes a variant of a prefab beside it -- "Lamp Variant" -- holding no changes yet, and opens it to edit.
        /// Whatever is changed and saved there is all the variant keeps; the rest follows its base.
        /// </summary>
        public void CreateVariant(string path) => Run(() =>
        {
            if (IsEditing)
            {
                throw new InvalidOperationException("Finish editing this prefab before making a variant.");
            }

            var source = ManaPrefab.Load(path);
            var folder = Path.GetRelativePath(ManaContentLibrary.Root, Path.GetDirectoryName(Path.GetFullPath(path))!);
            var name = $"{source.Name} Variant";
            for (var suffix = 2; File.Exists(Path.Combine(ManaContentLibrary.Root, folder, name + ManaPrefab.Extension)); suffix++)
            {
                name = $"{source.Name} Variant {suffix}";
            }

            var variantPath = ManaContentLibrary.NewPath(name, folder, ManaPrefab.Extension);
            ManaPrefab.CreateVariant(source, path, name).Save(variantPath);
            ManaUi.ManaUiDockDemo.MeshThumbnails?.ForceRefresh(variantPath);
            _entries = ManaContentLibrary.List();
            Status = $"Made {name}, a variant of {source.Name}. Edit it and save: only what you change is kept.";
        });

        public void CreatePrefab() => Run(() =>
        {
            if (IsEditing)
            {
                throw new InvalidOperationException("Finish editing this prefab before creating another. Nested prefabs are not supported yet.");
            }
            var node = Selected ?? throw new InvalidOperationException("Select a node or group to create a prefab.");
            if (ManaPrefabRuntime.InstanceRoot(node) != null)
            {
                throw new InvalidOperationException("Unpack the existing instance before turning any of its nodes into another prefab.");
            }
            var root = ManaLevelCapture.CaptureSubtree(node);
            var path = ManaContentLibrary.NewPath(AssetName, "Prefabs", ManaPrefab.Extension);
            root.Position = [0, 0, 0];
            root.Rotation = [0, 0, 0];
            root.Scale = [1, 1, 1];
            var asset = new ManaPrefab { Name = AssetName.Trim(), Root = root };
            asset.Save(path);
            ManaUi.ManaUiDockDemo.MeshThumbnails?.ForceRefresh(path);
            // Convert in place: existing selection, node data and components are retained beneath the link.
            var scene = SceneEditorOperations.FindOwningScene(_world, node) ?? throw new InvalidOperationException("Select a node in the world.");
            var parent = node.Parent;
            var siblings = parent?.Children ?? scene.Children;
            var index = siblings.IndexOf(node);
            var wrapper = new ManaSceneNode(_world.Context, Matrix4x4.Identity)
            {
                Name = asset.Name, Position = node.Position, Rotation = node.Rotation, Scale = node.Scale,
                Parent = parent, LevelSource = new ManaLevelSource { Kind = ManaLevelSourceKind.Prefab },
                PrefabLink = new ManaPrefabLink
                {
                    AssetId = asset.Id, AssetPath = ManaContentLibrary.Reference(path),
                    BaselineJson = JsonSerializer.Serialize(root, ManaLevel.SerializerOptions)
                }
            };
            siblings[index] = wrapper;
            wrapper.Children.Add(node);
            node.Parent = wrapper;
            node.Position = Vector3.Zero;
            node.Rotation = Vector3.Zero;
            node.Scale = Vector3.One;
            wrapper.UpdateTransform();
            _editor?.History.Clear();
            EditorSelectionSnapshot.SelectNode(_world.Selection, wrapper, true);
            _entries = ManaContentLibrary.List();
            Status = $"Created {asset.Name}; selected object is now a linked instance.";
        });

        public void Place(string path, Vector3? position = null) => Run(() =>
        {
            if (IsEditing)
            {
                throw new InvalidOperationException("Nested prefabs are not supported yet. Finish editing this prefab first.");
            }
            var root = InstantiateForPlacement(path);
            root.Position = position ?? (_world.CameraNode is { } camera ? camera.Position + camera.Forward * 5 : Vector3.Zero);
            root.UpdateTransform();
            _editor?.History.Clear();
            EditorSelectionSnapshot.SelectNode(_world.Selection, root, true);
            Status = root.Children.Count == 0 ? "Prefab reference placed, but its source could not be loaded." : $"Placed {root.Name}.";
        });

        public void OpenPrefab(string path) => Run(() =>
        {
            if (IsEditing)
            {
                throw new InvalidOperationException("Save and close, or discard the current prefab edit first.");
            }
            var asset = ManaPrefab.Load(path);
            var scene = new ManaScene(_world.Context) { Name = $"Prefab: {asset.Name}" };
            var result = ManaLevelLoader.Load(new ManaLevel { Nodes = [asset.Root] }, _world.Context, scene, false);
            if (result.Warnings.Count > 0 || scene.Children.Count != 1)
            {
                foreach (var node in scene.Children) { ManaPrefabRuntime.Destroy(node); }
                throw new InvalidDataException(string.Join("\n", result.Warnings));
            }
            _savedScenes = _world.Scenes.ToList();
            _savedDefault = _world.DefaultScene;
            _savedActive = _editor?.ActiveScene;
            _savedSelection = EditorSelectionSnapshot.Capture(_world.Selection);
            _savedCamera = _world.CameraNode == null ? null : SceneNodeTransformState.Capture(_world.CameraNode);
            // Retain camera and editor helpers in a separate scene; authored level content is isolated.
            var helpers = new ManaScene(_world.Context) { Name = "Editor" };
            helpers.Children.AddRange(_savedScenes.SelectMany(s => s.Children).Where(n => n.LevelSource == null && !RoomOnlyNames.Contains(n.Name ?? "")));

            _world.Scenes = [helpers, scene];
            _world.DefaultScene = scene;
            _editingScene = scene;
            EditingScenes = [scene];
            _editingAsset = asset;
            _editingPath = path;
            _sourceOnOpen = File.ReadAllText(path);
            _editor?.AttachScene(_world.Context, scene);
            _editor?.ResetVoxelLayersAfterLevelChange();
            _editor?.History.Clear();
            EditorSelectionSnapshot.SelectNode(_world.Selection, scene.Children[0], true);
            // A three-quarter view from above, so the prefab is seen standing on the floor, then framed whole.
            scene.Children[0].UpdateTransform();
            if (_world.CameraNode != null)
            {
                _world.CameraNode.Rotation = new Vector3(-25.0f.ToRadians(), 35.0f.ToRadians(), 0.0f);
            }
            _world.MainCamera?.TryFrameSelection(null, scene.Children[0]);
            Status = $"Editing {asset.Name}. Save updates its placed instances; Discard returns to the level.";
        });

        public void SavePrefab(bool close = false) => Run(() =>
        {
            if (_editingAsset == null || _editingScene == null || _editingPath == null)
            {
                return;
            }
            if (File.ReadAllText(_editingPath) != _sourceOnOpen)
            {
                throw new IOException("The prefab changed on disk while being edited. Discard and reopen it before saving.");
            }
            var roots = _editingScene.Children.Where(n => n.LevelSource != null).ToList();
            if (roots.Count != 1)
            {
                throw new InvalidDataException("A prefab needs exactly one root. Parent new objects beneath its root in the Outliner.");
            }
            var root = ManaLevelCapture.CaptureSubtree(roots[0]);
            if (root.Id != _editingAsset.Root.Id)
            {
                throw new InvalidDataException("Keep the prefab's original root so instances and exposed properties retain their identity.");
            }
            _editingAsset.Root = root;
            _editingAsset.Save(_editingPath);
            ManaUi.ManaUiDockDemo.MeshThumbnails?.ForceRefresh(_editingPath);
            _sourceOnOpen = File.ReadAllText(_editingPath);
            var id = _editingAsset.Id;
            var name = _editingAsset.Name;
            if (close)
            {
                CloseEditing();
            }
            var scenes = _savedScenes ?? _world.Scenes;
            RefreshInstances(id, scenes);
            _entries = ManaContentLibrary.List();
            Status = $"Saved {name}; placed instances updated.";
        });

        public void DiscardEditing() => Run(() => { CloseEditing(); Status = "Returned to the level."; });

        private void CloseEditing()
        {
            if (_savedScenes == null)
            {
                return;
            }
            foreach (var root in _editingScene?.Children ?? []) { ManaPrefabRuntime.Destroy(root); }
            _world.Scenes = _savedScenes;
            _world.DefaultScene = _savedDefault!;
            _editor?.AttachScene(_world.Context, _savedActive ?? _savedDefault!);
            _editor?.ResetVoxelLayersAfterLevelChange();
            if (_savedCamera.HasValue && _world.CameraNode != null)
            {
                _savedCamera.Value.Apply(_world.CameraNode);
            }
            _savedSelection.Apply(_world.Selection, true);
            _savedScenes = null;
            _editingScene = null;
            EditingScenes = null;
            _editingAsset = null;
            _editingPath = null;
            _editor?.History.Clear();
        }

        private void RefreshInstances(string id, List<ManaScene> scenes)
        {
            // Resolution needs the owning scenes, including those temporarily hidden by prefab editing.
            var visible = _world.Scenes;
            try
            {
                _world.Scenes = scenes;
                var instances = scenes.SelectMany(s => s.Children).SelectMany(ManaPrefabRuntime.Walk)
                    .Where(n => n.PrefabLink?.AssetId == id).ToList();
                foreach (var instance in instances)
                {
                    var selected = _savedSelection.Node;
                    var selectedId = selected?.AuthoredId;
                    var wasSelected = ManaPrefabRuntime.InstanceRoot(selected) == instance;
                    var replacement = ManaPrefabRuntime.Replace(instance, ManaPrefabRuntime.CaptureReference(instance));
                    if (wasSelected)
                    {
                        _savedSelection = new(null, ManaPrefabRuntime.Walk(replacement).FirstOrDefault(n => n.AuthoredId == selectedId) ?? replacement, null);
                    }
                }
                _editor?.History.Clear();
            }
            finally { _world.Scenes = visible; }
        }

        public void RefreshSelected() => Run(() =>
        {
            var root = SelectedInstance ?? throw new InvalidOperationException("Select a prefab instance.");
            ManaPrefabRuntime.Replace(root, ManaPrefabRuntime.CaptureReference(root));
            _editor?.History.Clear();
            Status = "Instance refreshed from its source.";
        });

        public void Revert(ManaSceneNode root, ManaPrefabProperty? property = null) => Run(() =>
        {
            var link = root.PrefabLink ?? throw new InvalidOperationException("Select a prefab instance.");
            var reference = property == null
                ? new ManaPrefabReference { AssetId = link.AssetId, AssetPath = link.AssetPath }
                : ManaPrefabRuntime.CaptureReference(root);
            if (property != null)
            {
                var path = PropertyPath(property);
                reference.Overrides.RemoveAll(o => o.Path.SequenceEqual(path));
            }
            ManaPrefabRuntime.Replace(root, reference);
            _editor?.History.Clear();
            Status = "Reverted prefab overrides.";
        });

        public void RevertOverride(ManaSceneNode root, ManaPrefabOverride change, string? selectId = null) => Run(() =>
        {
            var reference = ManaPrefabRuntime.CaptureReference(root);
            reference.Overrides.RemoveAll(o => o.Path.Take(change.Path.Length).SequenceEqual(change.Path));
            var replacement = ManaPrefabRuntime.Replace(root, reference);
            _editor?.History.Clear();
            Reselect(replacement, selectId);
            Status = "Reverted override.";
        });

        /// <summary>What the variant open for editing changes from its base, as it stands in the scene now.</summary>
        public List<ManaPrefabOverride> VariantOverrides()
        {
            var root = _editingScene?.Children.FirstOrDefault(n => n.LevelSource != null);
            if (_editingAsset?.BaseRoot is not { } baseRoot || root == null)
            {
                return [];
            }

            return ManaPrefabOverrides.Diff(ManaPrefabOverrides.Flatten(baseRoot), ManaPrefabOverrides.Flatten(ManaLevelCapture.CaptureSubtree(root)));
        }

        /// <summary>Puts one of the open variant's values back to its base's, rebuilding the tree it is edited in.</summary>
        public void RevertVariantOverride(ManaPrefabOverride change, string? selectId = null) => Run(() =>
        {
            if (_editingAsset?.BaseRoot is not { } baseRoot || _editingScene == null)
            {
                return;
            }

            var remaining = VariantOverrides().Where(o => !o.Path.Take(change.Path.Length).SequenceEqual(change.Path)).ToList();
            var tree = ManaPrefabOverrides.Resolve(baseRoot, remaining);
            var old = _editingScene.Children.ToList();
            var result = ManaLevelLoader.Load(new ManaLevel { Nodes = [tree] }, _world.Context, _editingScene, false);
            if (result.Warnings.Count > 0)
            {
                throw new InvalidDataException(string.Join("; ", result.Warnings));
            }

            // Only the prefab's own nodes are swapped; anything else in its scene stays put.
            foreach (var node in old.Where(n => n.LevelSource != null))
            {
                ManaPrefabRuntime.Destroy(node);
                _editingScene.Children.Remove(node);
            }

            _editor?.History.Clear();
            if (_editingScene.Children.FirstOrDefault(n => n.LevelSource != null) is { } root)
            {
                Reselect(root, selectId);
            }

            Status = "Reverted to the base's value.";
        });

        private void Reselect(ManaSceneNode root, string? id)
        {
            var node = id == null ? root : ManaPrefabRuntime.Walk(root).FirstOrDefault(n => n.AuthoredId == id) ?? root;
            EditorSelectionSnapshot.SelectNode(_world.Selection, node, true);
            _world.Selection.NotifySceneGraphChanged();
        }

        public void Unpack(ManaSceneNode root) => Run(() =>
        {
            if (root.Children.Count != 1)
            {
                throw new InvalidOperationException("Restore the missing prefab source before unpacking.");
            }
            root.PrefabLink = null;
            root.LevelSource = ManaLevelSource.Group;
            foreach (var node in ManaPrefabRuntime.Walk(root))
            {
                node.AuthoredId = Guid.NewGuid().ToString("N");
                foreach (var component in node.Components) { component.AuthoredId = Guid.NewGuid().ToString("N"); }
            }
            _editor?.History.Clear();
            _world.Selection.NotifySceneGraphChanged();
            Status = "Unpacked instance into an independent scene tree.";
        });

        public void ApplyToSource(ManaSceneNode instance) => Run(() =>
        {
            var reference = ManaPrefabRuntime.CaptureReference(instance);
            var path = ManaContentLibrary.ResolvePrefab(reference.AssetId, reference.AssetPath);
            var asset = ManaPrefab.Load(path);
            asset.Root = ManaPrefabOverrides.Resolve(asset.Root, reference.Overrides);
            asset.Save(path);
            ManaUi.ManaUiDockDemo.MeshThumbnails?.ForceRefresh(path);
            // The applying instance now inherits the values it just published.
            reference.Overrides.Clear();
            ManaPrefabRuntime.Replace(instance, reference);
            RefreshInstances(asset.Id, _world.Scenes);
            Status = $"Applied instance changes to {asset.Name}.";
        });

        public IReadOnlyList<PrefabPropertyChoice> PropertyChoices()
        {
            if (!IsEditing || Selected == null || Selected.LevelSource == null) { return []; }
            var node = ManaLevelCapture.CaptureSubtree(Selected);
            var result = new List<PrefabPropertyChoice>();
            foreach (var property in new[] { "position", "rotation", "scale", "name" })
            {
                result.Add(new($"{node.Name} / {property}", node.Id!, null, property));
            }
            foreach (var script in node.Scripts ?? [])
            {
                foreach (var property in script.Properties.Where(p => IsEditable(p.Value)))
                {
                    result.Add(new($"{script.Name ?? script.Type} / {property.Key}", node.Id!, script.Id, property.Key));
                }
            }
            return result;
        }

        private static bool IsEditable(JsonElement value) => value.ValueKind is JsonValueKind.String or JsonValueKind.Number
            or JsonValueKind.True or JsonValueKind.False || (value.ValueKind == JsonValueKind.Array && value.EnumerateArray().All(v => v.ValueKind == JsonValueKind.Number));

        public void Expose(PrefabPropertyChoice choice, string name, double? minimum, double? maximum) => Run(() =>
        {
            if (_editingAsset == null) { return; }
            name = string.IsNullOrWhiteSpace(name) ? choice.Property : name.Trim();
            if (minimum > maximum || (minimum.HasValue && !double.IsFinite(minimum.Value)) || (maximum.HasValue && !double.IsFinite(maximum.Value)))
            {
                throw new InvalidDataException("Use finite bounds with minimum no greater than maximum.");
            }
            if (_editingAsset.Properties.Any(p => p.Name == name || (p.NodeId == choice.NodeId && p.ComponentId == choice.ComponentId && p.Property == choice.Property)))
            {
                throw new InvalidDataException("This name or property is already exposed.");
            }
            _editingAsset.Properties.Add(new()
            {
                Name = name, NodeId = choice.NodeId,
                ComponentId = choice.ComponentId, Property = choice.Property, Minimum = minimum, Maximum = maximum
            });
            Status = "Property exposed. Save the prefab to publish it.";
        });

        public static string[] PropertyPath(ManaPrefabProperty property) => property.ComponentId == null
            ? [property.NodeId, property.Property] : [property.NodeId, "scripts", property.ComponentId, "properties", property.Property];

        public static JsonNode? ReadProperty(ManaLevelNode root, ManaPrefabProperty property)
        {
            JsonNode? value = ManaPrefabOverrides.Flatten(root);
            foreach (var segment in PropertyPath(property)) { value = (value as JsonObject)?[segment]; }
            return value;
        }

        public void SetProperty(ManaSceneNode instance, ManaPrefabProperty property, string text) => Run(() =>
        {
            var root = ManaLevelCapture.CaptureSubtree(instance.Children.Single());
            var current = ReadProperty(root, property) ?? throw new InvalidDataException("The exposed property target is missing.");
            JsonNode? value;
            if (current is JsonArray)
            {
                var numbers = text.Split(',').Select(v => double.Parse(v.Trim(), CultureInfo.InvariantCulture)).ToArray();
                if (numbers.Length != current.AsArray().Count || numbers.Any(n => !double.IsFinite(n) || n < property.Minimum || n > property.Maximum))
                {
                    throw new InvalidDataException("Enter the same number of finite values within the exposed range.");
                }
                value = new JsonArray(numbers.Select(n => (JsonNode?)JsonValue.Create(n)).ToArray());
            }
            else if (current.GetValueKind() == JsonValueKind.String) { value = JsonValue.Create(text); }
            else if (current.GetValueKind() is JsonValueKind.True or JsonValueKind.False) { value = JsonValue.Create(bool.Parse(text)); }
            else
            {
                var number = double.Parse(text, CultureInfo.InvariantCulture);
                if (!double.IsFinite(number) || number < property.Minimum || number > property.Maximum)
                {
                    throw new InvalidDataException("The value is outside the exposed property's range.");
                }
                value = JsonValue.Create(number);
            }
            var reference = ManaPrefabRuntime.CaptureReference(instance);
            var path = PropertyPath(property);
            reference.Overrides.RemoveAll(o => o.Path.SequenceEqual(path));
            reference.Overrides.Add(new() { Path = path, Value = value });
            ManaPrefabRuntime.Replace(instance, reference);
            _editor?.History.Clear();
            Status = $"Updated {property.Name}.";
        });

        public void SaveGridPreset() => Run(() =>
        {
            var map = Selected as ManaGridMapNode ?? throw new InvalidOperationException("Select a grid map.");
            var preset = ManaGridMapPreset.Capture(map, AssetName.Trim());
            if (IncludeGenerator) { preset.Generator = ManaGridGeneratorPreset.Capture(); }
            preset.Save(ManaContentLibrary.NewPath(AssetName, "GridMapPresets", ManaGridMapPreset.Extension));
            _entries = ManaContentLibrary.List();
            Status = "Saved grid map settings as a project asset.";
        });

        public void ApplyGridPreset(string path) => Run(() =>
        {
            var map = Selected as ManaGridMapNode ?? throw new InvalidOperationException("Select a grid map to apply this preset.");
            var preset = ManaGridMapPreset.Load(path);
            var before = map.CaptureState();
            var generatorBefore = preset.Generator == null ? null : ManaGridGeneratorPreset.Capture();
            preset.Apply(map);
            preset.Generator?.Apply();
            var after = map.CaptureState();
            _editor?.History.Record("Apply grid map preset", () => { map.RestoreState(before); generatorBefore?.Apply(); },
                () => { map.RestoreState(after); preset.Generator?.Apply(); });
            Status = "Applied preset; painted cells and layers retained.";
        });
    }
}
