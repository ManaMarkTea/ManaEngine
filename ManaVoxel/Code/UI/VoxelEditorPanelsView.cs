using ImGuiNET;
using ManaVoxel.Code.Tools;
using Mana;
using System.Numerics;
using System.IO.Compression;
using System.Text;

namespace ManaVoxel.Code.UI
{
    public class VoxelEditorPanelsView
    {
        private TimeSpan[]? _fpsData;
        private float[] _recentFpsData = Array.Empty<float>();
        private float _fpsGraphCeiling = 60.0f;

        private readonly List<string> _meshPickerAssets = new();
        private string _meshPickerFilter = string.Empty;
        private string? _meshPickerSelectedAsset;
        private string? _pendingMeshSpawnPath;
        private string? _draggingMeshAssetPath;
        private DateTime _nextMeshAssetScanUtc = DateTime.MinValue;
        private Task<MeshScanResult>? _meshAssetScanTask;
        private readonly MeshAssetTreeNode _meshAssetTreeRoot = new("Assets");
        private bool _meshAssetTreeDirty = true;
        private string? _meshAssetScanError;
        private bool _showGlbAssets = true;
        private bool _showGltfAssets = true;
        private bool _showFbxAssets;
        private bool _showDaeAssets;
        private bool _showObjAssets;
        private bool _showStlAssets;
        private bool _showPlyAssets;

        private static readonly string[] MeshAssetExtensions =
        {
            ".gltf",
            ".glb",
            ".obj",
            ".fbx",
            ".dae",
            ".stl",
            ".ply"
        };

        private static readonly string[] MeshFormatPreference =
        {
            ".glb",
            ".gltf",
            ".fbx",
            ".dae",
            ".obj",
            ".stl"
        };

        private const string ZipAssetPrefix = "zipasset:";

        private readonly record struct MeshScanResult(
            List<string> Assets,
            string? Error);

        private readonly record struct MeshAssetVariantChoice(
            string PrimaryAsset,
            string DisplayName,
            IReadOnlyList<string> Variants);

        private sealed class MeshAssetTreeNode
        {
            public string Name { get; }
            public SortedDictionary<string, MeshAssetTreeNode> Children { get; } = new(StringComparer.OrdinalIgnoreCase);
            public List<string> Assets { get; } = new();

            public MeshAssetTreeNode(string name)
            {
                Name = name;
            }
        }

        private readonly record struct MeshAssetPreviewBadge(string Label, Vector4 Color);

        private const float FpsHistorySeconds = 5.0f;
        private static readonly float[] FpsGraphSteps = { 30.0f, 60.0f, 90.0f, 120.0f, 144.0f, 165.0f, 240.0f, 360.0f, 480.0f, 720.0f, 1000.0f };

        public void Draw(VoxelEditorViewModel viewModel, ref Vector3 rayOrigin, ref Vector3 rayDirection)
        {
            viewModel.ApplyRuntimeSettings();

            ImGui.Text(ManaTime.GlobalTime.FramesPerSecondAvg.ToString(" 0.00") + " fps");
            ImGui.SameLine();
            ImGui.Text(ManaTime.GlobalTime.DeltaTimeAvg.TotalMilliseconds.ToString(" 0") + " ms");
            DrawRecentFpsHistory();

            ImGui.Text($"Mode: {viewModel.ModeText}");
            ImGui.SameLine();
            ImGui.Text($"State: {viewModel.ReassemblyText}");
            ImGui.SameLine();
            ImGui.Text($"Baked Bodies: {viewModel.BakedPhysicsVoxelCount}");

            ImGui.Spacing();
            if (ImGui.CollapsingHeader("Session Stats"))
            {
                DrawSessionTable(viewModel);
            }

            ImGui.Separator();

            if (ImGui.BeginTabBar("ManaTabs"))
            {
                DrawEditorTab(viewModel);
                DrawImportTab(viewModel);
                DrawPhysicsTab(viewModel);
                DrawDebugTab(viewModel, ref rayOrigin, ref rayDirection);
                ImGui.EndTabBar();
            }
        }

        public string? ConsumePendingMeshSpawnPath()
        {
            var pending = _pendingMeshSpawnPath;
            _pendingMeshSpawnPath = null;
            return pending;
        }

        public string? CurrentDraggedMeshAssetPath => _draggingMeshAssetPath;

        public void ClearDraggedMeshAssetPath()
        {
            _draggingMeshAssetPath = null;
        }

        private static void DrawEditorTab(VoxelEditorViewModel viewModel)
        {
            if (!ImGui.BeginTabItem("Editor"))
            {
                return;
            }

            DrawVoxelNodeControls(viewModel);

            ImGui.Spacing();
            DrawPlacementReadout(viewModel);

            ImGui.EndTabItem();
        }

        private void DrawImportTab(VoxelEditorViewModel viewModel)
        {
            if (!ImGui.BeginTabItem("Import"))
            {
                return;
            }

            ImGui.SeparatorText("Import Settings");
            ImGui.TextWrapped("These settings apply to models imported when dropping file assets (.vox or .gltf) onto the editor window.");
            ImGui.Spacing();

            const ImGuiTableFlags flags =
                ImGuiTableFlags.BordersInner |
                ImGuiTableFlags.RowBg |
                ImGuiTableFlags.SizingStretchProp;

            if (ImGui.BeginTable("ImportSettingsTable", 2, flags))
            {
                ImGui.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthFixed, 112.0f);
                ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted("Pos Offset");
                ImGui.TableNextColumn();
                ImGui.PushID("PosOffset");
                ImGuiExt.DrawVector3Value(ref viewModel.ImportPositionOffset);
                ImGui.PopID();

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted("Rot (deg)");
                ImGui.TableNextColumn();
                ImGui.PushID("RotDeg");
                ImGuiExt.DrawVector3Value(ref viewModel.ImportRotationDegrees);
                ImGui.PopID();

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted("Scale");
                ImGui.TableNextColumn();
                ImGui.PushID("Scale");
                ImGuiExt.DrawVector3Value(ref viewModel.ImportScale);
                ImGui.PopID();

                ImGui.EndTable();
            }

            ImGui.Spacing();
            DrawMeshAssetPicker();

            ImGui.EndTabItem();
        }

        private void DrawMeshAssetPicker()
        {
            ImGui.SeparatorText("Mesh Asset Picker");

            EnsureMeshAssetsUpToDate();
            PollMeshAssetScanCompletion();

            if (ImGui.Button("Refresh Mesh List"))
            {
                EnsureMeshAssetsUpToDate(force: true);
            }

            ImGui.SameLine();
            ImGui.TextDisabled($"{_meshPickerAssets.Count} assets");
            if (_meshAssetScanTask is { IsCompleted: false })
            {
                ImGui.SameLine();
                ImGui.TextDisabled("Scanning...");
            }

            ImGui.SetNextItemWidth(-1.0f);
            if (ImGui.InputText("Search", ref _meshPickerFilter, 256))
            {
                _meshAssetTreeDirty = true;
            }

            DrawFormatTypeFilters();

            if (!string.IsNullOrWhiteSpace(_meshAssetScanError))
            {
                ImGui.TextWrapped($"Scan warning: {_meshAssetScanError}");
            }

            RebuildMeshAssetTreeIfNeeded();

            if (ImGui.BeginChild("MeshPickerList", new Vector2(0.0f, 240.0f), ImGuiChildFlags.Border))
            {
                DrawMeshAssetTree(_meshAssetTreeRoot, isRoot: true);
            }

            ImGui.EndChild();

            ImGui.TextWrapped("Drag a mesh from the list into the scene to preview it under the cursor, then release to place. You can also click Place Selected Mesh to preview and click in-scene to confirm. Press Escape to cancel preview placement.");

            var hasSelection = !string.IsNullOrWhiteSpace(_meshPickerSelectedAsset);
            ImGui.BeginDisabled(!hasSelection);
            if (ImGui.Button("Place Selected Mesh"))
            {
                _pendingMeshSpawnPath = _meshPickerSelectedAsset;
            }

            ImGui.EndDisabled();
        }

        private void EnsureMeshAssetsUpToDate(bool force = false)
        {
            var now = DateTime.UtcNow;
            if (!force && now < _nextMeshAssetScanUtc)
            {
                return;
            }

            _nextMeshAssetScanUtc = now.AddSeconds(2);
            if (_meshAssetScanTask is { IsCompleted: false })
            {
                return;
            }

            _meshAssetScanTask = Task.Run(ScanMeshAssets);
        }

        private MeshScanResult ScanMeshAssets()
        {
            var discovered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var errorMessages = new List<string>();

            foreach (var root in GetMeshAssetRoots())
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                foreach (var extension in MeshAssetExtensions)
                {
                    foreach (var file in Directory.EnumerateFiles(root, $"*{extension}", SearchOption.AllDirectories))
                    {
                        discovered.Add(Path.GetFullPath(file));
                    }
                }

                foreach (var zipFile in Directory.EnumerateFiles(root, "*.zip", SearchOption.AllDirectories))
                {
                    TryDiscoverZipMeshAssets(zipFile, discovered, errorMessages);
                }
            }

            var sorted = discovered.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
            var error = errorMessages.Count > 0
                ? $"{errorMessages.Count} archive(s) could not be read"
                : null;

            return new MeshScanResult(sorted, error);
        }

        private void PollMeshAssetScanCompletion()
        {
            if (_meshAssetScanTask is not { IsCompleted: true } task)
            {
                return;
            }

            _meshAssetScanTask = null;
            var result = task.GetAwaiter().GetResult();

            _meshPickerAssets.Clear();
            _meshPickerAssets.AddRange(result.Assets);
            _meshAssetScanError = result.Error;
            _meshAssetTreeDirty = true;

            if (!string.IsNullOrWhiteSpace(_meshPickerSelectedAsset) && !_meshPickerAssets.Contains(_meshPickerSelectedAsset, StringComparer.OrdinalIgnoreCase))
            {
                _meshPickerSelectedAsset = null;
            }
        }

        private void RebuildMeshAssetTreeIfNeeded()
        {
            if (!_meshAssetTreeDirty)
            {
                return;
            }

            _meshAssetTreeRoot.Children.Clear();
            _meshAssetTreeRoot.Assets.Clear();

            var filter = _meshPickerFilter.Trim();
            foreach (var asset in _meshPickerAssets)
            {
                if (!IsAssetFormatEnabled(asset))
                {
                    continue;
                }

                var label = BuildMeshAssetLabel(asset);
                if (!string.IsNullOrWhiteSpace(filter) && !label.Contains(filter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                AddAssetToTree(_meshAssetTreeRoot, label, asset);
            }

            _meshAssetTreeDirty = false;
        }

        private void DrawFormatTypeFilters()
        {
            ImGui.SeparatorText("Formats");

            var changed = false;
            changed |= ImGui.Checkbox("GLB", ref _showGlbAssets);
            ImGui.SameLine();
            changed |= ImGui.Checkbox("GLTF", ref _showGltfAssets);
            ImGui.SameLine();
            changed |= ImGui.Checkbox("FBX", ref _showFbxAssets);
            ImGui.SameLine();
            changed |= ImGui.Checkbox("DAE", ref _showDaeAssets);

            changed |= ImGui.Checkbox("OBJ", ref _showObjAssets);
            ImGui.SameLine();
            changed |= ImGui.Checkbox("STL", ref _showStlAssets);
            ImGui.SameLine();
            changed |= ImGui.Checkbox("PLY", ref _showPlyAssets);

            if (changed)
            {
                _meshAssetTreeDirty = true;
            }
        }

        private bool IsAssetFormatEnabled(string assetReference)
        {
            var extension = BuildMeshAssetExtension(assetReference);
            return extension.ToLowerInvariant() switch
            {
                ".glb" => _showGlbAssets,
                ".gltf" => _showGltfAssets,
                ".fbx" => _showFbxAssets,
                ".dae" => _showDaeAssets,
                ".obj" => _showObjAssets,
                ".stl" => _showStlAssets,
                ".ply" => _showPlyAssets,
                _ => false,
            };
        }

        private static void AddAssetToTree(MeshAssetTreeNode root, string displayPath, string assetReference)
        {
            var parts = displayPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                root.Assets.Add(assetReference);
                return;
            }

            var node = root;
            for (var i = 0; i < parts.Length - 1; i++)
            {
                var part = parts[i];
                if (!node.Children.TryGetValue(part, out var child))
                {
                    child = new MeshAssetTreeNode(part);
                    node.Children.Add(part, child);
                }

                node = child;
            }

            node.Assets.Add(assetReference);
        }

        private void DrawMeshAssetTree(MeshAssetTreeNode node, bool isRoot)
        {
            if (isRoot)
            {
                foreach (var child in node.Children.Values)
                {
                    DrawCollapsedMeshAssetSubtree(child);
                }

                var groupedRootAssets = BuildPreferredAssetChoices(node.Assets);
                foreach (var choice in groupedRootAssets)
                {
                    DrawMeshAssetLeaf(choice);
                }
                return;
            }

            DrawCollapsedMeshAssetSubtree(node);
        }

        private void DrawCollapsedMeshAssetSubtree(MeshAssetTreeNode startNode)
        {
            var (label, terminalNode) = CollapseSingleChildPath(startNode);
            var opened = ImGui.TreeNodeEx(label, ImGuiTreeNodeFlags.SpanFullWidth | ImGuiTreeNodeFlags.DefaultOpen);
            if (!opened)
            {
                return;
            }

            foreach (var child in terminalNode.Children.Values)
            {
                DrawCollapsedMeshAssetSubtree(child);
            }

            var groupedAssets = BuildPreferredAssetChoices(terminalNode.Assets);
            foreach (var choice in groupedAssets)
            {
                DrawMeshAssetLeaf(choice);
            }

            ImGui.TreePop();
        }

        private static IReadOnlyList<MeshAssetVariantChoice> BuildPreferredAssetChoices(IEnumerable<string> assetReferences)
        {
            var groups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var assetReference in assetReferences)
            {
                var fileName = BuildMeshAssetFileName(assetReference);
                var key = Path.GetFileNameWithoutExtension(fileName);
                if (!groups.TryGetValue(key, out var assets))
                {
                    assets = new List<string>();
                    groups.Add(key, assets);
                }

                assets.Add(assetReference);
            }

            var choices = new List<MeshAssetVariantChoice>(groups.Count);
            foreach (var group in groups)
            {
                var variants = group.Value
                    .OrderBy(GetMeshAssetPriority)
                    .ThenBy(asset => BuildMeshAssetFileName(asset), StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var primary = variants[0];
                choices.Add(new MeshAssetVariantChoice(primary, BuildMeshAssetFileName(primary), variants));
            }

            choices.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.DisplayName, b.DisplayName));
            return choices;
        }

        private static (string Label, MeshAssetTreeNode TerminalNode) CollapseSingleChildPath(MeshAssetTreeNode startNode)
        {
            var label = startNode.Name;
            var node = startNode;

            while (node.Assets.Count == 0 && node.Children.Count == 1)
            {
                var onlyChild = node.Children.Values.First();
                label = $"{label}/{onlyChild.Name}";
                node = onlyChild;
            }

            return (label, node);
        }

        private void DrawMeshAssetLeaf(MeshAssetVariantChoice choice)
        {
            var fileName = choice.DisplayName;
            var badge = GetMeshAssetPreviewBadge(choice.PrimaryAsset);
            var isSelected = choice.Variants.Any(asset => string.Equals(_meshPickerSelectedAsset, asset, StringComparison.OrdinalIgnoreCase));
            var hasVariants = choice.Variants.Count > 1;
            var label = hasVariants
                ? $"{badge.Label} {fileName} (+{choice.Variants.Count - 1})"
                : $"{badge.Label} {fileName}";

            ImGui.PushID(choice.PrimaryAsset);
            ImGui.ColorButton("##AssetBadge", badge.Color, ImGuiColorEditFlags.NoTooltip, new Vector2(12.0f, 12.0f));
            ImGui.SameLine();

            if (ImGui.Selectable(label, isSelected, ImGuiSelectableFlags.AllowDoubleClick | ImGuiSelectableFlags.SpanAllColumns))
            {
                _meshPickerSelectedAsset = choice.PrimaryAsset;
                if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                {
                    _pendingMeshSpawnPath = choice.PrimaryAsset;
                }
            }

            if (ImGui.IsItemHovered())
            {
                if (hasVariants)
                {
                    var tooltip = new StringBuilder();
                    tooltip.AppendLine(BuildMeshAssetTooltip(choice.PrimaryAsset));
                    tooltip.AppendLine();
                    tooltip.AppendLine("Variants:");
                    foreach (var variant in choice.Variants)
                    {
                        tooltip.AppendLine($"- {BuildMeshAssetFileName(variant)}");
                    }

                    ImGui.SetTooltip(tooltip.ToString());
                }
                else
                {
                    ImGui.SetTooltip(BuildMeshAssetTooltip(choice.PrimaryAsset));
                }
            }

            if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left, 2.0f))
            {
                _draggingMeshAssetPath = choice.PrimaryAsset;
            }

            ImGui.PopID();
        }

        private static int GetMeshAssetPriority(string assetReference)
        {
            var extension = BuildMeshAssetExtension(assetReference);
            for (var i = 0; i < MeshFormatPreference.Length; i++)
            {
                if (extension.Equals(MeshFormatPreference[i], StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            // Unlisted formats keep deterministic order after preferred ones.
            return MeshFormatPreference.Length + 10;
        }

        private static string BuildMeshAssetFileName(string assetReference)
        {
            if (TryDecodeZipAssetReference(assetReference, out _, out var entryPath))
            {
                return Path.GetFileName(entryPath);
            }

            return Path.GetFileName(assetReference);
        }

        private static string BuildMeshAssetExtension(string assetReference)
        {
            if (TryDecodeZipAssetReference(assetReference, out _, out var entryPath))
            {
                return Path.GetExtension(entryPath);
            }

            return Path.GetExtension(assetReference);
        }

        private static IEnumerable<string> GetMeshAssetRoots()
        {
            yield return Path.Combine(Environment.CurrentDirectory, "Assets");
            yield return Path.Combine(Environment.CurrentDirectory, "Mana", "Assets");

            yield return Path.Combine(AppContext.BaseDirectory, "Assets");
            yield return Path.Combine(AppContext.BaseDirectory, "Mana", "Assets");

            yield return Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Assets");
            yield return Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Mana", "Assets");
            yield return Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Assets");
            yield return Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Mana", "Assets");
        }

        private static string BuildMeshAssetLabel(string assetReference)
        {
            if (TryDecodeZipAssetReference(assetReference, out var zipPath, out var entryPath))
            {
                var zipLabel = ToWorkspaceRelativePath(zipPath);
                return $"zip/{zipLabel}/{entryPath.Replace('\\', '/')}";
            }

            return ToWorkspaceRelativePath(assetReference);
        }

        private static string BuildMeshAssetTooltip(string assetReference)
        {
            if (TryDecodeZipAssetReference(assetReference, out var zipPath, out var entryPath))
            {
                return $"{zipPath}\n{entryPath.Replace('\\', '/')}";
            }

            return assetReference;
        }

        private static string ToWorkspaceRelativePath(string path)
        {
            var cwdRelative = Path.GetRelativePath(Environment.CurrentDirectory, path);
            return string.IsNullOrWhiteSpace(cwdRelative)
                ? Path.GetFileName(path)
                : cwdRelative.Replace('\\', '/');
        }

        private static void TryDiscoverZipMeshAssets(string zipFile, HashSet<string> discovered, List<string> errorMessages)
        {
            try
            {
                using var archive = ZipFile.OpenRead(zipFile);
                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrWhiteSpace(entry.Name))
                    {
                        continue;
                    }

                    var extension = Path.GetExtension(entry.FullName);
                    if (!IsSupportedMeshExtension(extension))
                    {
                        continue;
                    }

                    discovered.Add(EncodeZipAssetReference(zipFile, entry.FullName));
                }
            }
            catch
            {
                // Invalid or locked zip files are ignored so one bad archive does not break scanning.
                errorMessages.Add(zipFile);
            }
        }

        private static MeshAssetPreviewBadge GetMeshAssetPreviewBadge(string assetReference)
        {
            string extension;
            if (TryDecodeZipAssetReference(assetReference, out _, out var entryPath))
            {
                extension = Path.GetExtension(entryPath);
            }
            else
            {
                extension = Path.GetExtension(assetReference);
            }

            return extension.ToLowerInvariant() switch
            {
                ".glb" => new MeshAssetPreviewBadge("GLB", new Vector4(0.22f, 0.67f, 0.95f, 1.0f)),
                ".gltf" => new MeshAssetPreviewBadge("GLTF", new Vector4(0.32f, 0.78f, 0.56f, 1.0f)),
                ".obj" => new MeshAssetPreviewBadge("OBJ", new Vector4(0.95f, 0.62f, 0.24f, 1.0f)),
                ".fbx" => new MeshAssetPreviewBadge("FBX", new Vector4(0.86f, 0.39f, 0.39f, 1.0f)),
                ".dae" => new MeshAssetPreviewBadge("DAE", new Vector4(0.62f, 0.48f, 0.90f, 1.0f)),
                ".stl" => new MeshAssetPreviewBadge("STL", new Vector4(0.54f, 0.84f, 0.42f, 1.0f)),
                ".ply" => new MeshAssetPreviewBadge("PLY", new Vector4(0.48f, 0.77f, 0.86f, 1.0f)),
                _ => new MeshAssetPreviewBadge("MESH", new Vector4(0.75f, 0.75f, 0.75f, 1.0f)),
            };
        }

        private static bool IsSupportedMeshExtension(string extension)
        {
            foreach (var candidate in MeshAssetExtensions)
            {
                if (extension.Equals(candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string EncodeZipAssetReference(string zipPath, string entryPath)
        {
            var zip = Convert.ToBase64String(Encoding.UTF8.GetBytes(Path.GetFullPath(zipPath)));
            var entry = Convert.ToBase64String(Encoding.UTF8.GetBytes(entryPath.Replace('\\', '/')));
            return $"{ZipAssetPrefix}{zip}:{entry}";
        }

        private static bool TryDecodeZipAssetReference(string assetReference, out string zipPath, out string entryPath)
        {
            zipPath = string.Empty;
            entryPath = string.Empty;
            if (!assetReference.StartsWith(ZipAssetPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            var payload = assetReference.Substring(ZipAssetPrefix.Length);
            var separator = payload.IndexOf(':');
            if (separator <= 0 || separator >= payload.Length - 1)
            {
                return false;
            }

            try
            {
                var zipBytes = Convert.FromBase64String(payload[..separator]);
                var entryBytes = Convert.FromBase64String(payload[(separator + 1)..]);
                zipPath = Encoding.UTF8.GetString(zipBytes);
                entryPath = Encoding.UTF8.GetString(entryBytes);
                return !string.IsNullOrWhiteSpace(zipPath) && !string.IsNullOrWhiteSpace(entryPath);
            }
            catch
            {
                return false;
            }
        }

        private static void DrawSessionTable(VoxelEditorViewModel viewModel)
        {
            const ImGuiTableFlags flags =
                ImGuiTableFlags.Borders |
                ImGuiTableFlags.RowBg |
                ImGuiTableFlags.SizingStretchProp;

            if (!ImGui.BeginTable("SessionStats", 2, flags))
            {
                return;
            }

            ImGui.TableSetupColumn("Metric", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Count", ImGuiTableColumnFlags.WidthFixed, 96.0f);
            ImGui.TableHeadersRow();

            DrawSessionTableRow("Voxels", viewModel.VoxelCount);
            DrawSessionTableRow("Voxel Nodes", viewModel.VoxelLayers.Count);
            DrawSessionTableRow("Active Node Voxels", viewModel.ActiveVoxelCount);
            DrawSessionTableRow("Created This Session", viewModel.CreatedThisSessionVoxelCount);
            DrawSessionTableRow("Pending Voxels", viewModel.PendingVoxelCount);
            DrawSessionTableRow("Duplicate Skips", viewModel.DuplicateVoxelRejectCount);
            DrawSessionTableRow("Physics Baked Voxels", viewModel.BakedPhysicsVoxelCount);

            ImGui.EndTable();
        }

        private static void DrawVoxelNodeControls(VoxelEditorViewModel viewModel)
        {
            ImGui.SeparatorText("Active Voxel Node");
            if (viewModel.VoxelLayers.Count == 0)
            {
                ImGui.TextUnformatted("None");
                return;
            }

            var layerNames = viewModel.VoxelLayerNames;
            viewModel.ActiveVoxelLayerIndex = Math.Clamp(
                viewModel.ActiveVoxelLayerIndex,
                0,
                layerNames.Length - 1);
            ImGui.SetNextItemWidth(-1.0f);
            ImGui.Combo("##ActiveNode", ref viewModel.ActiveVoxelLayerIndex, layerNames, layerNames.Length);
        }

        private static void DrawPlacementReadout(VoxelEditorViewModel viewModel)
        {
            ImGui.SeparatorText("Placement");
            if (!viewModel.HasHoverVoxel)
            {
                ImGui.TextUnformatted("No hovered voxel");
                return;
            }

            const ImGuiTableFlags flags =
                ImGuiTableFlags.BordersInner |
                ImGuiTableFlags.RowBg |
                ImGuiTableFlags.SizingStretchProp;

            if (ImGui.BeginTable("PlacementTable", 2, flags))
            {
                ImGui.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthFixed, 112.0f);
                ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);

                // Row 1: Cell
                var cell = VoxelRasterizer.ToCell(viewModel.HoverVoxelPosition);
                var cellVec = new Vector3(cell.X, cell.Y, cell.Z);
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted("Cell");
                ImGui.TableNextColumn();
                ImGui.PushID("Cell");
                ImGuiExt.DrawVector3Value(ref cellVec, 0.0f, readOnly: true);
                ImGui.PopID();

                // Row 2: Center
                var centerVec = viewModel.HoverVoxelPosition;
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted("Center");
                ImGui.TableNextColumn();
                ImGui.PushID("Center");
                ImGuiExt.DrawVector3Value(ref centerVec, 0.0f, readOnly: true);
                ImGui.PopID();

                // Row 3: Delta From Last
                if (viewModel.HasHoverDelta)
                {
                    var deltaVec = viewModel.HoverDeltaFromLast;
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextUnformatted("Delta From Last");
                    ImGui.TableNextColumn();
                    ImGui.PushID("Delta");
                    ImGuiExt.DrawVector3Value(ref deltaVec, 0.0f, readOnly: true);
                    ImGui.PopID();
                }

                // Row 4: Camera Distance
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted("Cam Distance");
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.Text($"{viewModel.HoverCameraDistance:0.0}");

                

                ImGui.EndTable();
            }
        }

        private static void DrawSessionTableRow(string label, int value)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(label);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(value.ToString());
        }

        private static string FormatVector(Vector3 value)
        {
            return $"{value.X:0.0}, {value.Y:0.0}, {value.Z:0.0}";
        }

        private static void DrawPhysicsTab(VoxelEditorViewModel viewModel)
        {
            if (!ImGui.BeginTabItem("Physics"))
            {
                return;
            }

            ImGui.SeparatorText("Tuning");
            ImGui.SliderFloat("Explosion Strength", ref viewModel.BakeExplosionStrength, 1.0f, 150.0f);
            ImGui.SliderFloat("Click Explosion Scale", ref viewModel.ClickExplosionStrengthScale, 0.02f, 10.0f);
            ImGui.SliderFloat("Explosion Radius Padding", ref viewModel.BakeExplosionRadiusPadding, 0.0f, 6.0f);
            ImGui.SliderFloat("Reassemble Pull", ref viewModel.ReassemblePullGain, 0.0f, 30.0f);
            ImGui.SliderFloat("Reassemble Damping", ref viewModel.ReassembleVelocityDamping, 0.0f, 0.99f);
            ImGui.SliderFloat("Reassemble Rotation Lerp", ref viewModel.ReassembleRotationLerp, 0.0f, 1.0f);
            ImGui.SliderFloat("Settle Distance", ref viewModel.ReassembleSettleDistance, 0.01f, 0.5f);
            ImGui.SliderFloat("Settle Velocity", ref viewModel.ReassembleSettleVelocity, 0.01f, 2.0f);

            ImGui.Spacing();
            ImGui.SeparatorText("Bake");

            var canBake = viewModel.HasEditableVoxels;
            if (!canBake)
            {
                ImGui.BeginDisabled();
            }

            if (ImGui.Button("Bake All Voxel Nodes To Physics", new Vector2(ImGui.GetContentRegionAvail().X, 0)))
            {
                viewModel.BakeToPhysics();
            }

            if (!canBake)
            {
                ImGui.EndDisabled();
            }

            ImGui.Spacing();
            ImGui.SeparatorText("Lock / Overlap");

            var hasBakedVoxels = viewModel.HasBakedVoxels;
            if (!hasBakedVoxels)
            {
                ImGui.BeginDisabled();
            }

            ImGui.Checkbox("Preview Lock Snap", ref viewModel.ShowLockPreview);
            if (ImGui.Combo(
                "Lock Overlap Policy",
                ref viewModel.LockOverlapPolicyIndex,
                viewModel.LockOverlapPolicyOptions,
                viewModel.LockOverlapPolicyOptions.Length))
            {
                viewModel.ApplyRuntimeSettings();
            }

            if (viewModel.ShowLockPreview && hasBakedVoxels)
            {
                ImGui.Text($"Lock Preview Voxels: {viewModel.GetLockPreviewVoxelCount()}");
                ImGui.Text($"Lock Preview Overlaps: {viewModel.GetLockPreviewOverlapCount()}");
            }

            ImGui.Spacing();
            ImGui.SeparatorText("Actions");

            if (ImGui.Button("Reassemble All Baked Voxels", new Vector2(ImGui.GetContentRegionAvail().X, 0)))
            {
                viewModel.BeginReassembleBakedVoxels();
            }

            if (ImGui.Button("Lock All Voxels For Editing", new Vector2(ImGui.GetContentRegionAvail().X, 0)))
            {
                viewModel.LockBakedVoxelsForEditing();
            }

            if (ImGui.Button("Explode All Baked Voxels", new Vector2(ImGui.GetContentRegionAvail().X, 0)))
            {
                viewModel.ExplodeBakedVoxels();
            }

            if (hasBakedVoxels)
            {
                ImGui.Text(viewModel.IsReassembling ? "Reassembly: in progress" : "Reassembly: idle");
            }

            if (!hasBakedVoxels)
            {
                ImGui.EndDisabled();
            }

            ImGui.EndTabItem();
        }

        private void DrawDebugTab(VoxelEditorViewModel viewModel, ref Vector3 rayOrigin, ref Vector3 rayDirection)
        {
            if (!ImGui.BeginTabItem("Debug"))
            {
                return;
            }

            ImGui.SeparatorText("Timing");
            if (_fpsData == null)
            {
                _fpsData = new TimeSpan[ManaTime.GlobalTime.History.Length];
            }

            var frameTimeCount = ManaTime.GlobalTime.CopyOrderedHistory(_fpsData);
            if (frameTimeCount > 1)
            {
                var data = new float[frameTimeCount];
                for (var i = 0; i < frameTimeCount; i++)
                {
                    data[i] = (float)_fpsData[i].TotalMilliseconds;
                }

                ImGui.PlotLines("Frame Time", ref data[0], data.Length, 0, "ms", 0, 100, new Vector2(150, 90));
            }

            ImGui.Spacing();
            ImGui.SeparatorText("Memory");
            DrawMemoryHistory();

            ImGui.Spacing();
            ImGui.SeparatorText("Ray");
            ImGui.InputFloat3("RAY", ref rayOrigin);
            ImGui.InputFloat3("DIR", ref rayDirection);

            ImGui.Spacing();
            ImGui.SeparatorText("Tools");
            ImGui.Checkbox("Show ImGui Demo Window", ref viewModel.ShowImGuiDemoWindow);

            ImGui.EndTabItem();
        }

        private float[] _memoryHistoryData = Array.Empty<float>();

        // Working-set (OS-visible resident memory) history -- mirrors DrawRecentFpsHistory's plot style
        // below, feeding from ManaMemoryStats' own self-throttled ring buffer instead of ManaTime's.
        // scale_max=float.MaxValue tells ImGui.PlotLines to auto-fit the Y range to whatever's currently
        // visible, unlike the FPS graph's manual step-ceiling logic -- simpler, and fine for a debug tool
        // whose whole point is watching memory grow, not a stable axis.
        private void DrawMemoryHistory()
        {
            if (_memoryHistoryData.Length != ManaMemoryStats.HistoryCapacity)
            {
                _memoryHistoryData = new float[ManaMemoryStats.HistoryCapacity];
            }

            var count = ManaMemoryStats.CopyOrderedHistory(_memoryHistoryData);
            if (count <= 1)
            {
                return;
            }

            ImGui.Text($"Working Set: {ManaMemoryStats.Latest:0} MB");
            ImGui.PlotLines(
                "Memory",
                ref _memoryHistoryData[0],
                count,
                0,
                "MB",
                0.0f,
                float.MaxValue,
                new Vector2(ImGui.GetContentRegionAvail().X, 70));
        }

        private void DrawRecentFpsHistory()
        {
            if (_fpsData == null)
            {
                _fpsData = new TimeSpan[ManaTime.GlobalTime.History.Length];
            }

            var historyCount = ManaTime.GlobalTime.CopyOrderedHistory(_fpsData);
            if (historyCount <= 1)
            {
                return;
            }

            // Keep approximately the most recent N seconds of frame history.
            float avgFrameSeconds = MathF.Max(0.001f, (float)ManaTime.GlobalTime.DeltaTimeAvg.TotalSeconds);
            int minimumSamples = Math.Min(60, historyCount);
            int desiredSamples = Math.Clamp((int)(FpsHistorySeconds / avgFrameSeconds), minimumSamples, historyCount);
            int startIndex = Math.Max(0, historyCount - desiredSamples);
            int sampleCount = historyCount - startIndex;
            if (sampleCount <= 1)
            {
                return;
            }

            if (_recentFpsData.Length != sampleCount)
            {
                _recentFpsData = new float[sampleCount];
            }

            double sampledSeconds = 0.0;
            for (int i = 0; i < sampleCount; i++)
            {
                var frame = _fpsData[startIndex + i];
                sampledSeconds += frame.TotalSeconds;
                _recentFpsData[i] = frame.TotalSeconds > 0
                    ? 1.0f / (float)frame.TotalSeconds
                    : 0.0f;
            }

            float fpsMin = _recentFpsData.Min();
            float fpsMax = _recentFpsData.Max();
            float fpsAvg = sampledSeconds > 0.0
                ? (float)(sampleCount / sampledSeconds)
                : 0.0f;
            float fpsCurrent = _recentFpsData[^1];
            float fpsGraphMax = fpsMax;

            if (MathF.Abs(fpsGraphMax - fpsMin) < 1.0f)
            {
                fpsGraphMax = fpsMin + 1.0f;
            }

            float currentFpsGraphCeiling = NextFpsGraphStep(fpsGraphMax);
            _fpsGraphCeiling = MathF.Max(_fpsGraphCeiling, currentFpsGraphCeiling);

            if (ImGui.SmallButton("Reset FPS Range"))
            {
                _fpsGraphCeiling = currentFpsGraphCeiling;
            }

            ImGui.PlotLines(
                $"FPS History ({FpsHistorySeconds:0}s)",
                ref _recentFpsData[0],
                _recentFpsData.Length,
                0,
                string.Empty,
                0.0f,
                _fpsGraphCeiling,
                new Vector2(ImGui.GetContentRegionAvail().X, 70));

            DrawFpsStatsTable(fpsCurrent, fpsAvg, fpsMin, fpsMax, _fpsGraphCeiling);
        }

        private static void DrawFpsStatsTable(float current, float average, float minimum, float maximum, float graphCeiling)
        {
            const ImGuiTableFlags flags =
                ImGuiTableFlags.Borders |
                ImGuiTableFlags.RowBg |
                ImGuiTableFlags.SizingStretchSame;

            if (!ImGui.BeginTable("FpsStats", 5, flags))
            {
                return;
            }

            ImGui.TableSetupColumn("Current");
            ImGui.TableSetupColumn("Avg");
            ImGui.TableSetupColumn("Min");
            ImGui.TableSetupColumn("Max");
            ImGui.TableSetupColumn("Range");
            ImGui.TableHeadersRow();

            ImGui.TableNextRow();
            DrawFpsStatsCell($"{current:0.0}");
            DrawFpsStatsCell($"{average:0.0}");
            DrawFpsStatsCell($"{minimum:0.0}");
            DrawFpsStatsCell($"{maximum:0.0}");
            DrawFpsStatsCell($"0-{graphCeiling:0}");

            ImGui.EndTable();
        }

        private static void DrawFpsStatsCell(string value)
        {
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(value);
        }

        private static float NextFpsGraphStep(float value)
        {
            foreach (var step in FpsGraphSteps)
            {
                if (value <= step)
                {
                    return step;
                }
            }

            return FpsGraphSteps[^1];
        }
    }
}
