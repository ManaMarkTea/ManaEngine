using ManaCore.Graphics;
using ManaCore.Imaging;
using ManaCore.Loading;
using ManaGraphics.Code.Graphics;
using ManaUI;
using ManaGraphics.Code.Graphics.Resources;

namespace ManaTests;

public static class EnvironmentLifetimeChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check) =>
        RunAsync(check).GetAwaiter().GetResult();

    private sealed class IgnoreProgress : IProgress<ManaLoadingProgress>
    {
        public void Report(ManaLoadingProgress value) { }
    }

    private static async Task RunAsync(Action<string, bool, string> check)
    {
        void Check(string name, bool passed) => check(name, passed, "");
        var load = new ManaWebGpuEnvironmentLoad<string>();
        var began = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOld = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken oldToken = default;
        IProgress<ManaLoadingProgress>? oldProgress = null;
        load.Start(async (progress, token) =>
        {
            oldToken = token;
            oldProgress = progress;
            began.SetResult();
            return await releaseOld.Task;
        });
        var oldWork = load.Completion;
        await began.Task.WaitAsync(TimeSpan.FromSeconds(5));
        load.Start((progress, _) =>
        {
            progress.Report(new ManaLoadingProgress("current", 0.5f));
            return Task.FromResult("new source");
        });
        await load.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Check("source replacement cancels the previous CPU generation", oldToken.IsCancellationRequested);
        oldProgress!.Report(new ManaLoadingProgress("stale", 0.1f));
        releaseOld.SetResult("old source");
        await oldWork.WaitAsync(TimeSpan.FromSeconds(5));
        Check("late CPU progress cannot overwrite the current generation", load.TakeProgress()?.Status == "current");
        Check("late CPU results cannot replace a newer source", load.TakeResult()?.Value == "new source");
        Check("prepared results are consumed once at the render boundary", load.TakeResult() == null);

        var error = new IOException("missing environment photo");
        load.Start((_, _) => Task.FromException<string>(error));
        await load.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Check("preparation errors are observed and handed to the render thread", ReferenceEquals(load.TakeResult()?.Error, error));
        load.Start((_, _) => Task.FromResult("retry"));
        await load.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Check("a later request can recover from preparation failure", load.TakeResult()?.Value == "retry");

        var lateFailure = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        load.Start((_, _) => lateFailure.Task);
        var failingWork = load.Completion;
        load.Invalidate();
        lateFailure.SetException(error);
        await failingWork.WaitAsync(TimeSpan.FromSeconds(5));
        Check("invalidated job faults are observed without publishing stale failure", load.TakeResult() == null);

        load.Start((progress, _) =>
        {
            progress.Report(new ManaLoadingProgress("ready", 1f));
            return Task.FromResult("ready pixels");
        });
        await load.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        load.Retire();
        load.Retire();
        Check("retirement discards prepared pixels and pending progress", load.TakeResult() == null && load.TakeProgress() == null);
        var startedAfterRetire = false;
        load.Start((_, _) => { startedAfterRetire = true; return Task.FromResult("late"); });
        await load.Completion;
        Check("a retired renderer cannot start new environment work", !startedAfterRetire);

        var waiting = new ManaWebGpuEnvironmentLoad<string>();
        var waitingBegan = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var download = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        waiting.Start(async (_, token) =>
        {
            waitingBegan.SetResult();
            return await download.Task.WaitAsync(token);
        });
        await waitingBegan.Task.WaitAsync(TimeSpan.FromSeconds(5));
        waiting.Retire();
        await waiting.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Check("retirement stops waiting for shared downloads without canceling them", !download.Task.IsCompleted && waiting.TakeResult() == null);
        download.SetResult("shared download finishes normally");

        var fresh = new ManaWebGpuEnvironmentLoad<string>();
        fresh.Start((_, _) => Task.FromResult("fresh renderer"));
        await fresh.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Check("another renderer remains independent of retired jobs", fresh.TakeResult()?.Value == "fresh renderer");
        fresh.Retire();

        var path = Path.Combine(Path.GetTempPath(), $"mana-environment-{Guid.NewGuid():N}.png");
        try
        {
            ManaImageCodec.SavePng(new ManaImage(1, 1, [32, 64, 128, 255]), path);
            var photo = ManaWebGpuEnvironmentReflectionMap.PreparePhoto(path, 16, null, CancellationToken.None);
            var facesMatch = photo.Width == 16 && photo.Height == 16 && photo.Layers == 6
                && photo.BytesPerRow == 256 && photo.Pixels.Length == 256 * 16 * 6;
            for (var face = 0; face < 6; face++)
            {
                for (var y = 0; y < 16; y++)
                {
                    var row = photo.Pixels.AsSpan((face * 16 + y) * 256, 256);
                    for (var x = 0; x < 16; x++)
                    {
                        facesMatch &= Math.Abs(row[x * 4] - 32) <= 1 && Math.Abs(row[x * 4 + 1] - 64) <= 1
                            && Math.Abs(row[x * 4 + 2] - 128) <= 1 && row[x * 4 + 3] == 255;
                    }
                    facesMatch &= row[64..].IndexOfAnyExcept((byte)0) < 0;
                }
            }
            Check("CPU photo preparation preserves all six faces and padded upload rows", facesMatch);
            var prepared = ManaWebGpuEnvironmentReflectionMap.PrepareData(new ManaEnvironmentReflectionSource { BrdfLutSize = 16 },
                null, null, CancellationToken.None);
            var lut = prepared.Brdf;
            var finite = prepared.Photo == null && lut.Width == 16 && lut.Height == 16 && lut.Layers == 1 && lut.BytesPerRow == 256;
            for (var y = 0; y < 16; y++)
            {
                foreach (var value in System.Runtime.InteropServices.MemoryMarshal.Cast<byte, Half>(lut.Pixels.AsSpan(y * 256, 16 * 4)))
                {
                    finite &= Half.IsFinite(value) && value >= (Half)0;
                }
            }
            Check("computed-sky preparation produces a finite RG16 LUT without reading a photo", finite);
            var resizedPhoto = await ManaPreparedEnvironment.PrepareAsync(new ManaEnvironmentReflectionSource
                { SourcePath = path, EnvironmentCubeSize = 32 }, path, true, true,
                new IgnoreProgress(), CancellationToken.None, 16);
            Check("photo updates use the active texture size even for a smaller placeholder", resizedPhoto.Photo?.Width == 16);
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            var canceledBeforeRead = false;
            try
            {
                ManaWebGpuEnvironmentReflectionMap.PreparePhoto(path + ".missing", 16, null, canceled.Token);
            }
            catch (OperationCanceledException)
            {
                canceledBeforeRead = true;
            }
            Check("retired photo preparation stops before decoding or generating faces", canceledBeforeRead);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
