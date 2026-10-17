using Mana.Code.Components;
using ManaVoxel.Code.GridMaps;
using ManaVoxel.Code.Levels;

namespace ManaVoxel.Code.Assets
{
    /// <summary>Prepares catalog assets and decodes pictures before synchronous scene construction.</summary>
    public static class ManaLevelAssetPreloader
    {
        public static async Task EnsureAsync(ManaLevel level, string? contentRoot = null)
        {
            var catalog = ManaCore.Assets.ManaAssetCatalog.Current;
            if (catalog == null)
            {
                return;
            }
            var nodes = new Queue<ManaLevelNode>(level.Nodes);
            while (nodes.TryDequeue(out var node))
            {
                if (node.Prefab is { } reference)
                {
                    try
                    {
                        var source = ManaPrefab.Load(ManaContentLibrary.ResolvePrefab(reference.AssetId, reference.AssetPath, contentRoot), contentRoot);
                        var resolved = ManaPrefabOverrides.Resolve(source.Root, reference.Overrides);
                        new ManaPrefab { Root = resolved }.Validate();
                        nodes.Enqueue(resolved);
                    }
                    catch (Exception exception) when (exception is IOException or System.Text.Json.JsonException)
                    {
                        ManaCore.Diagnostics.ManaLog.WriteDiagnostic($"Could not preload prefab '{reference.AssetPath}': {exception.Message}");
                    }
                }
                foreach (var surface in (node.Material is { } shapeMaterial ? new[] { shapeMaterial } : [])
                    .Concat(node.MeshMaterials?.Values.AsEnumerable() ?? []))
                {
                    foreach (var picture in surface.Pictures)
                    {
                        if (!string.IsNullOrEmpty(picture))
                        {
                            await catalog.EnsureAsync(picture);
                        }
                    }
                }
                foreach (var script in node.Scripts ?? [])
                {
                    var pictures = ManaSpriteAssetDependencies.Pictures(script, contentRoot);
                    foreach (var pack in pictures.Select(ManaSpriteAssetDependencies.PackOf).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        try { await catalog.EnsureAsync(pack); }
                        catch (Exception ex) { ManaCore.Diagnostics.ManaLog.WriteDiagnostic($"Could not preload sprite pack '{pack}': {ex.Message}"); }
                    }
                    // Each picture on its own as well: the browser decodes pictures only ahead of their synchronous load.
                    foreach (var picture in pictures)
                    {
                        try { await catalog.EnsureAsync(picture); }
                        catch (Exception ex) { ManaCore.Diagnostics.ManaLog.WriteDiagnostic($"Could not preload sprite picture '{picture}': {ex.Message}"); }
                    }
                    if (!string.Equals(script.Type, ManaAudioSourceComponent.ScriptType, StringComparison.OrdinalIgnoreCase)) { continue; }
                    string Text(string key) => script.Properties.FirstOrDefault(pair =>
                        string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)).Value is var value
                        && value.ValueKind == System.Text.Json.JsonValueKind.String ? value.GetString() ?? "" : "";
                    foreach (var path in ManaAudioSourceComponent.Paths(Text("clipPath"), Text("variants")))
                    {
                        try { await catalog.EnsureAsync(path); }
                        catch (Exception ex) { ManaCore.Diagnostics.ManaLog.WriteDiagnostic($"Could not preload audio '{path}': {ex.Message}"); }
                    }
                }
                if (!string.IsNullOrWhiteSpace(node.Asset))
                {
                    await catalog.EnsureAsync(node.Asset);
                }
                foreach (var tile in node.GridMap?.Palette ?? [])
                {
                    try { await catalog.EnsureAsync(ManaGridMapNode.AssetOf(tile)); }
                    catch (Exception ex) { ManaCore.Diagnostics.ManaLog.WriteDiagnostic($"Could not preload grid map tile '{tile}': {ex.Message}"); }
                }
                if (node.Children != null)
                {
                    foreach (var child in node.Children)
                    {
                        nodes.Enqueue(child);
                    }
                }
            }

            // What the level's game modes build at play time, which no node names.
            foreach (var entry in level.GameModes ?? new())
            {
                foreach (var asset in ManaLevelGameModes.Create(entry.Type)?.Assets ?? [])
                {
                    try { await catalog.EnsureAsync(asset); }
                    catch (Exception ex) { ManaCore.Diagnostics.ManaLog.WriteDiagnostic($"Could not preload '{asset}' for game mode {entry.Type}: {ex.Message}"); }
                }
            }
        }

    }
}
