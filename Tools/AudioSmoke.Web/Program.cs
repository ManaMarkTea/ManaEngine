using System.Numerics;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using ManaAudio;
using ManaCore.Audio;

await AudioSmoke.Initialize();

[SupportedOSPlatform("browser")]
public static partial class AudioSmoke
{
    private static IManaAudio? audio;
    private static IManaAudioVoice? pending;
    public static async Task Initialize()
    {
        await ManaAudioFactory.InitializeBrowserAsync();
        audio = ManaAudioFactory.Create(Console.WriteLine);
    }

    [JSExport]
    public static string BeforeGesture()
    {
        var a = audio ?? throw new InvalidOperationException("Not initialized");
        if (!a.IsAvailable || a.IsUnlocked) { throw new Exception($"Expected available but locked device (available={a.IsAvailable}, unlocked={a.IsUnlocked})."); }
        using var dropped = a.Play(ManaAudioDiagnostics.TestTone);
        if (dropped.IsPlaying) { throw new Exception("Locked one-shot was queued."); }
        pending = a.Play(ManaAudioDiagnostics.TestTone, new() { Loop = true, Volume = 0 });
        if (!pending.IsPlaying) { throw new Exception("Loop was not retained for unlock."); }
        return "PASS: locked one-shot dropped; loop retained. Click Unlock Audio.";
    }

    [JSExport]
    public static async Task<string> AfterGesture()
    {
        var a = audio ?? throw new InvalidOperationException("Not initialized");
        // resume() is asynchronous even though it must be invoked from the gesture handler.
        for (int i = 0; i < 50 && !a.IsUnlocked; i++) { await Task.Delay(20); }
        if (!a.IsUnlocked) { throw new Exception("Gesture did not unlock audio."); }
        pending?.Stop();
        var oneShot = a.Play(ManaAudioDiagnostics.TestTone);
        if (!oneShot.IsPlaying) { throw new Exception("One-shot did not start."); }
        for (int i = 0; i < 100 && oneShot.IsPlaying; i++) { await Task.Delay(20); }
        a.Update();
        if (oneShot.IsPlaying) { throw new Exception("One-shot did not finish."); }
        using var loop = a.Play(ManaAudioDiagnostics.TestTone, new() { Loop = true, Position = new(2, 0, -2) });
        a.SetListener(Vector3.Zero, -Vector3.UnitZ, Vector3.UnitY);
        a.MasterVolume = 0.5f;
        a.SetBusVolume(ManaAudioBus.Sfx, 0.5f);
        loop.Position = new(-2, 0, -2);
        loop.Pitch = 1.1f;
        await Task.Delay(100);
        if (!loop.IsPlaying) { throw new Exception("Loop did not stay active."); }
        a.StopAll();
        if (loop.IsPlaying) { throw new Exception("Loop did not stop."); }
        a.Dispose();
        a.Dispose();
        if (a.IsAvailable || loop.IsPlaying) { throw new Exception("Disposed device retained a voice."); }
        return "PASS: WASM PCM upload, gesture unlock, one-shot completion, spatial loop, gain/pitch updates and disposal.";
    }
}
