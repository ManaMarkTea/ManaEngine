using Mana.Code.Components;
using ManaEditor.Code.GridMaps;
using ManaEditor.Code.Levels;

namespace ManaEditor.Code.Assets
{
    /// <summary>Prepares catalog assets and decodes pictures before synchronous scene construction.</summary>
    public static class ManaLevelAssetPreloader
    {
        /// <summary>
        /// Natural Earth data a planet reads synchronously: the water mask (lakes, ocean), the borders painted into its
        /// picture and the airport list. publish_assets.py publishes these from Assets/GeoData; keep the two lists alike.
        /// </summary>
        public static readonly string[] PlanetAssets =
        [
            "Assets/GeoData/ne_10m_lakes.zip",
            "Assets/GeoData/ne_10m_ocean.zip",
            "Assets/GeoData/ne_50m_admin_0_countries.zip",
            "Assets/GeoData/ne_50m_admin_1_states_provinces.zip",
            "Assets/GeoData/airports.csv"
        ];

        public static async Task EnsureAsync(ManaLevel level, string? contentRoot = null)
        {
            var catalog = ManaCore.Assets.ManaAssetCatalog.Current;
            if (catalog == null)
            {
                return;
            }
            if (level.Sky?.Photo is { Length: > 0 } skyPhoto)
            {
                await catalog.EnsureAsync(skyPhoto);
            }
            var nodes = new Queue<ManaLevelNode>(level.Nodes);
            // What the level's game modes build at play time, which no node names. A prefab among them (the runner's
            // track sections) is walked like a placed one, for the models it uses.
            var modeAssets = new List<(string Asset, string Mode)>();
            foreach (var entry in level.GameModes ?? new())
            {
                foreach (var asset in ManaLevelGameModes.Create(entry.Type)?.Assets ?? [])
                {
                    if (!asset.EndsWith(ManaPrefab.Extension, StringComparison.OrdinalIgnoreCase))
                    {
                        modeAssets.Add((asset, entry.Type));
                        continue;
                    }
                    try
                    {
                        var file = Mana.Code.Content.ManaContentMounts.FullPath(contentRoot ?? ManaContentLibrary.Root, asset)
                            ?? throw new FileNotFoundException($"No content folder for '{asset}'.");
                        nodes.Enqueue(ManaPrefab.Load(file, contentRoot).Root);
                    }
                    catch (Exception exception) when (exception is IOException or System.Text.Json.JsonException or InvalidDataException)
                    {
                        ManaCore.Diagnostics.ManaLog.WriteDiagnostic($"Could not preload prefab '{asset}' for game mode {entry.Type}: {exception.Message}");
                    }
                }
            }
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
                    foreach (var model in ManaLevelComponentScripts.ModelPaths(script))
                    {
                        try { await catalog.EnsureAsync(model); }
                        catch (Exception ex) { ManaCore.Diagnostics.ManaLog.WriteDiagnostic($"Could not preload model '{model}': {ex.Message}"); }
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
                if (node.Terrain is { Textured: not false })
                {
                    foreach (var atlas in new[] { Mana.Code.Nodes.ManaTerrainNode.LayerAlbedoAtlas, Mana.Code.Nodes.ManaTerrainNode.LayerNormalAtlas })
                    {
                        try { await catalog.EnsureAsync(atlas); }
                        catch (Exception ex) { ManaCore.Diagnostics.ManaLog.WriteDiagnostic($"Could not preload terrain pictures '{atlas}': {ex.Message}"); }
                    }
                }
                if (!string.IsNullOrWhiteSpace(node.Asset))
                {
                    if (OperatingSystem.IsBrowser() && node.Asset.StartsWith("Library/Imported/", StringComparison.Ordinal)
                        && catalog.LocalFileOf(node.Asset) == null)
                    {
                        throw new InvalidDataException($"This web build is missing the prepared scene asset '{node.Asset}'. Prepare level downloads before publishing.");
                    }
                    await catalog.EnsureAsync(node.Asset);
                }
                if (node.Kind == Mana.Code.Levels.ManaLevelSourceKind.Planet)
                {
                    foreach (var asset in PlanetAssets)
                    {
                        try { await catalog.EnsureAsync(asset); }
                        catch (Exception ex) { ManaCore.Diagnostics.ManaLog.WriteDiagnostic($"Could not preload planet data '{asset}': {ex.Message}"); }
                    }
                }
                // The aircraft models flight traffic places near the camera (FlightTrafficModels), prefabs no node names.
                if (node.Scripts?.Any(script => string.Equals(script.Type, ManaGeoTools.Code.Flights.FlightTrafficComponent.ScriptType, StringComparison.OrdinalIgnoreCase)) == true)
                {
                    foreach (var prefab in new[] { ManaGeoTools.Code.Flights.FlightTrafficModels.AirlinerPrefab, ManaGeoTools.Code.Flights.FlightTrafficModels.LightPrefab })
                    {
                        try
                        {
                            var file = Mana.Code.Content.ManaContentMounts.FullPath(contentRoot ?? ManaContentLibrary.Root, prefab)
                                ?? throw new FileNotFoundException($"No content folder for '{prefab}'.");
                            nodes.Enqueue(ManaPrefab.Load(file, contentRoot).Root);
                        }
                        catch (Exception exception) when (exception is IOException or System.Text.Json.JsonException or InvalidDataException)
                        {
                            ManaCore.Diagnostics.ManaLog.WriteDiagnostic($"Could not preload aircraft '{prefab}': {exception.Message}");
                        }
                    }
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

            foreach (var (asset, mode) in modeAssets)
            {
                try { await catalog.EnsureAsync(asset); }
                catch (Exception ex) { ManaCore.Diagnostics.ManaLog.WriteDiagnostic($"Could not preload '{asset}' for game mode {mode}: {ex.Message}"); }
            }
        }

    }
}
