#if !MANA_BROWSER
using System.Security.Cryptography;
using ManaEditor.Code.Levels;

namespace ManaEditor.Code.Imports;

/// <summary>Fetches source packs before level construction; downloads are atomic and imports stay regenerable.</summary>
public static class ManaLevelDownloads
{
    private static readonly SemaphoreSlim Gate = new(1);
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromHours(1) };

    public static void Validate(ManaLevelDownload download)
    {
        if (string.IsNullOrWhiteSpace(download.File) || download.File.Contains('/') || download.File.Contains('\\')
            || download.File.Contains(':') || !download.File.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            || download.File is ".zip" or "..zip")
        {
            throw new InvalidDataException("A level download must name a ZIP file inside Downloaded/.");
        }
        if (!Uri.TryCreate(download.Url, UriKind.Absolute, out var url) || url.Scheme != "https")
        {
            throw new InvalidDataException("A level download URL must use HTTPS.");
        }
        if (download.Sha256.Length != 64 || !download.Sha256.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException("A level download needs its SHA256 checksum.");
        }
    }

    public static async Task EnsureAsync(ManaLevel level, string checkout, IProgress<string>? progress = null,
        CancellationToken token = default, HttpClient? http = null)
    {
        foreach (var download in level.Downloads ?? [])
        {
            Validate(download);
        }
        await Gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            foreach (var download in level.Downloads ?? [])
            {
                var zip = Path.Combine(ManaDownloads.Folder(checkout), download.File);
                await EnsurePackAsync(download, zip, progress, token, http ?? Http).ConfigureAwait(false);
                await ManaSceneImporter.EnsureAsync(checkout, zip, progress, token).ConfigureAwait(false);
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task EnsurePackAsync(ManaLevelDownload download, string zip, IProgress<string>? progress,
        CancellationToken token, HttpClient http)
    {
        if (System.IO.File.Exists(zip))
        {
            // The importer stamps the ZIP; verify it before trusting even an already-present pack.
            await VerifyAsync(zip, download.Sha256, token).ConfigureAwait(false);
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(zip)!);
        var temporary = zip + ".download";
        try
        {
            progress?.Report($"Downloading {download.File}");
            using var response = await http.GetAsync(download.Url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            await using (var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
            await using (var output = System.IO.File.Create(temporary))
            {
                var buffer = new byte[128 * 1024];
                long received = 0;
                long reported = 0;
                int count;
                while ((count = await input.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                    received += count;
                    if (received - reported >= 8 * 1024 * 1024)
                    {
                        progress?.Report($"Downloading {download.File}: {received / 1048576} MiB / {total / 1048576} MiB");
                        reported = received;
                    }
                }
                if (total is { } expected && received != expected)
                {
                    throw new InvalidDataException($"Incomplete download: {download.File}");
                }
            }
            await VerifyAsync(temporary, download.Sha256, token).ConfigureAwait(false);
            System.IO.File.Move(temporary, zip);
        }
        finally
        {
            if (System.IO.File.Exists(temporary))
            {
                System.IO.File.Delete(temporary);
            }
        }
    }

    private static async Task VerifyAsync(string path, string expected, CancellationToken token)
    {
        await using var stream = System.IO.File.OpenRead(path);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false));
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Checksum mismatch for {Path.GetFileName(path)}. Remove the cached ZIP and retry.");
        }
    }
}
#endif
