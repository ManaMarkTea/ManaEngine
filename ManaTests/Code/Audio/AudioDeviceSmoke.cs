using ManaAudio;
using ManaAudio.Desktop;

namespace ManaTests;

public static class AudioDeviceSmoke
{
    /// <summary>Opt-in audible device check, excluded from ordinary headless tests.</summary>
    public static int Run()
    {
        try
        {
            using var audio = new ManaAudioSystem(new ManaOpenAlBackend());
            using var voice = audio.Play(ManaAudioDiagnostics.TestTone);
            if (!voice.IsPlaying) { throw new InvalidOperationException("Source did not start."); }
            Thread.Sleep(600);
            audio.Update();
            if (voice.IsPlaying) { throw new InvalidOperationException("Source did not finish."); }
            using var loop = audio.Play(ManaAudioDiagnostics.TestTone, new() { Loop = true, Position = new(2, 0, -2) });
            audio.SetListener(Vector3.Zero, -Vector3.UnitZ, Vector3.UnitY);
            loop.Volume = 0.2f;
            loop.Pitch = 1.1f;
            loop.Position = new(-2, 0, -2);
            Thread.Sleep(100);
            loop.Stop();
            if (loop.IsPlaying) { throw new InvalidOperationException("Source did not stop."); }
            Console.WriteLine("PASS: OpenAL default device, playback, completion, spatial updates, looping and cleanup.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }
}
