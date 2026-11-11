using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using Mana;
using Mana.Code;
using ManaUI;
using ManaEditor.Code.Imports;
using ManaEditor.Code.Levels;

namespace ManaTests;

public static class LevelDownloadChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check) => RunAsync(check).GetAwaiter().GetResult();

    private static async Task RunAsync(Action<string, bool, string> check)
    {
        var folder = Directory.CreateTempSubdirectory("mana-level-download-");
        try
        {
            using var archive = new MemoryStream();
            using (var zip = new ZipArchive(archive, ZipArchiveMode.Create, leaveOpen: true))
            using (var writer = new StreamWriter(zip.CreateEntry("Tiny.gltf").Open()))
            {
                writer.Write("{\"asset\":{\"version\":\"2.0\"},\"nodes\":[]}");
            }
            var bytes = archive.ToArray();
            var download = new ManaLevelDownload
            {
                File = "Tiny.zip", Url = "https://example.test/tiny.zip", Sha256 = Convert.ToHexString(SHA256.HashData(bytes))
            };
            var level = new ManaLevel { Downloads = [download] };
            using var handler = new PackHandler(bytes);
            using var http = new HttpClient(handler);
            await ManaLevelDownloads.EnsureAsync(level, folder.FullName, http: http);
            var zipPath = Path.Combine(folder.FullName, "Downloaded", "Tiny.zip");
            var imported = ManaDownloads.CurrentLevels(folder.FullName, zipPath);
            check("opening a level downloads and imports its declared pack", handler.Requests == 1 && imported is { Count: 1 }, "");
            var timestamp = File.GetLastWriteTimeUtc(imported![0]);
            await ManaLevelDownloads.EnsureAsync(level, folder.FullName, http: http);
            check("reopening uses the cached download and import", handler.Requests == 1 && File.GetLastWriteTimeUtc(imported[0]) == timestamp, "");

            var world = new ManaWorld();
            var context = new ManaContext(null!, null!, world) { DeferGpuUploads = true };
            var scene = new ManaScene(context);
            world.Scenes.Add(scene);
            ManaLevelLoader.Load(ManaLevel.FromJson(level.ToJson()), context, scene, false);
            var captured = ManaLevelCapture.CaptureScene(world, scene, "Tiny");
            check("download declarations survive load, capture and save as", captured.Downloads?.Single().Sha256 == download.Sha256
                && ManaLevel.FromJson(captured.ToJson()).Downloads?.Single().Url == download.Url, "");
            check("levels without downloads keep the old JSON", !new ManaLevel().ToJson().Contains("downloads"), "");

            var bistro = Path.Combine(ManaLevelLibrary.ResolveFolder(), "Downloaded", "Bistro");
            var variants = Directory.EnumerateFiles(bistro, "*.manalevel").Select(ManaLevel.Load).ToList();
            foreach (var variant in variants)
            {
                foreach (var dependency in variant.Downloads ?? [])
                {
                    ManaLevelDownloads.Validate(dependency);
                }
            }
            // Desktop only: the browser cannot fetch or convert the source pack, and the web build must not need it.
            check("all five checked-in Bistro variants declare their source pack and stay off the web", variants.Count == 5
                && variants.All(variant => variant.Downloads is { Count: 1 } && variant.Web?.Publish != true), "");

            download.File = "Bad.zip";
            download.Sha256 = new string('0', 64);
            var rejected = false;
            try { await ManaLevelDownloads.EnsureAsync(level, folder.FullName, http: http); }
            catch (InvalidDataException) { rejected = true; }
            check("a failed checksum never installs a pack or leaves a partial file", rejected
                && !File.Exists(Path.Combine(folder.FullName, "Downloaded", "Bad.zip"))
                && !File.Exists(Path.Combine(folder.FullName, "Downloaded", "Bad.zip.download")), "");
            download.Sha256 = Convert.ToHexString(SHA256.HashData(bytes));
            await ManaLevelDownloads.EnsureAsync(level, folder.FullName, http: http);
            check("a failed download can be retried", File.Exists(Path.Combine(folder.FullName, "Downloaded", "Bad.zip")), "");

            download.File = "Tiny.zip";
            File.WriteAllText(zipPath, "corrupt cache");
            rejected = false;
            try { await ManaLevelDownloads.EnsureAsync(level, folder.FullName, http: http); }
            catch (InvalidDataException) { rejected = true; }
            check("a corrupt cached ZIP is rejected before import", rejected, "");

            foreach (var name in new[] { "../escape.zip", "..\\escape.zip", "C:escape.zip", "/escape.zip" })
            {
                download.File = name;
                rejected = false;
                try { ManaLevelDownloads.Validate(download); }
                catch (InvalidDataException) { rejected = true; }
                check($"download paths stay inside Downloaded: {name}", rejected, "");
            }
            download.File = "Tiny.zip";
            download.Url = "file:///tmp/tiny.zip";
            rejected = false;
            try { ManaLevelDownloads.Validate(download); }
            catch (InvalidDataException) { rejected = true; }
            check("downloads require HTTPS", rejected, "");
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    private sealed class PackHandler(byte[] bytes) : HttpMessageHandler
    {
        public int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        }
    }
}
