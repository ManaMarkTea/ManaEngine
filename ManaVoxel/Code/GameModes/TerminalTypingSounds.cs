using ManaCore.Audio;

namespace ManaVoxel.Code.GameModes;

/// <summary>Short, damped key taps in irregular phrases, generated once rather than streamed or looped.</summary>
internal sealed class TerminalTypingSounds
{
    private readonly IManaAudio _audio;
    private readonly Random _random;
    private readonly ManaAudioClip[] _keys = new ManaAudioClip[6];
    private readonly List<IManaAudioVoice> _voices = [];
    private float _next;
    private int _remaining;

    public TerminalTypingSounds(IManaAudio audio, int seed)
    {
        _audio = audio;
        _random = seed == 0 ? new Random() : new Random(seed);
        for (var key = 0; key < _keys.Length; key++) _keys[key] = MakeKey(key);
    }

    public void Advance(float delta, float volume)
    {
        for (var i = _voices.Count - 1; i >= 0; i--)
            if (!_voices[i].IsPlaying)
            {
                _voices[i].Dispose();
                _voices.RemoveAt(i);
            }
        _next -= delta;
        if (_next > 0) return;
        if (_remaining-- <= 0)
        {
            _remaining = _random.Next(4, 18);
            _next = .35f + _random.NextSingle() * 1.8f;
            return;
        }
        // Never catch up missed clicks after a slow frame: the ambience should stay sparse.
        _next = .055f + _random.NextSingle() * .15f;
        if (volume <= 0 || !_audio.IsAvailable || !_audio.IsUnlocked) return;
        _voices.Add(_audio.Play(_keys[_random.Next(_keys.Length)], new()
        {
            Volume = volume * (.45f + _random.NextSingle() * .55f),
            Pitch = .9f + _random.NextSingle() * .2f,
            Bus = ManaAudioBus.Sfx
        }));
    }

    public void Stop()
    {
        foreach (var voice in _voices) voice.Dispose();
        _voices.Clear();
    }

    private static ManaAudioClip MakeKey(int key)
    {
        const int rate = 24000;
        var samples = new short[rate / 20 * 2];
        var random = new Random(715 + key);
        var low = 0f;
        var pan = (key % 3 - 1) * .35f;
        for (var frame = 0; frame < samples.Length / 2; frame++)
        {
            var t = (float)frame / rate;
            var noise = random.NextSingle() * 2 - 1;
            low += .22f * (noise - low);
            var attack = MathF.Min(1, t / .0008f);
            var body = MathF.Sin(2 * MathF.PI * (360 + key * 71) * t) * MathF.Exp(-t * 240);
            var click = (noise - low) * MathF.Exp(-t * 650);
            var release = t < .016f ? 0 : low * MathF.Exp(-(t - .016f) * 470) * .3f;
            var value = (body * .18f + click * .35f + release) * attack;
            samples[frame * 2] = (short)(value * (1 - pan) * 14000);
            samples[frame * 2 + 1] = (short)(value * (1 + pan) * 14000);
        }
        return new ManaAudioClip(samples, 2, rate);
    }
}
