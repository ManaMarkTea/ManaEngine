using ManaCore.Audio;

namespace ManaAudio;

public static class ManaAudioDiagnostics
{
    private static readonly Lazy<ManaAudioClip> tone = new(() =>
    {
        const int rate = 48000;
        var samples = new short[rate / 3];
        for (int i = 0; i < samples.Length; i++)
        {
            // Quiet A4 with short fades so the diagnostic itself doesn't click.
            double envelope = Math.Min(1, Math.Min(i, samples.Length - 1 - i) / (rate * 0.01));
            samples[i] = (short)(Math.Sin(2 * Math.PI * 440 * i / rate) * 5000 * envelope);
        }
        return new ManaAudioClip(samples, 1, rate);
    });

    public static ManaAudioClip TestTone => tone.Value;
}
