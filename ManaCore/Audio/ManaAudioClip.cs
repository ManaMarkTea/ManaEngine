using System.Buffers.Binary;

namespace ManaCore.Audio;

/// <summary>Immutable interleaved PCM16 audio, shared by all playback backends.</summary>
public sealed class ManaAudioClip
{
    private readonly short[] samples;
    public ReadOnlyMemory<short> Samples => samples;
    public int Channels { get; }
    public int SampleRate { get; }
    public double Duration => (double)samples.Length / Channels / SampleRate;

    public ManaAudioClip(ReadOnlySpan<short> samples, int channels, int sampleRate)
    {
        if (channels is not (1 or 2) || sampleRate < 8000 || sampleRate > 192000 ||
            samples.Length == 0 || samples.Length % channels != 0)
        {
            throw new ArgumentException("Audio must contain complete mono/stereo frames at 8–192 kHz.");
        }
        this.samples = samples.ToArray();
        Channels = channels;
        SampleRate = sampleRate;
    }

    /// <summary>Reads RIFF WAV with PCM8/16/24/32 or IEEE float32 samples. Rejects compressed WAV.</summary>
    public static ManaAudioClip FromWav(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 12 || !bytes[..4].SequenceEqual("RIFF"u8) || !bytes.Slice(8, 4).SequenceEqual("WAVE"u8))
        {
            throw new InvalidDataException("Expected a RIFF WAVE file.");
        }
        long end = 8L + BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]);
        if (end > bytes.Length || end < 12)
        {
            throw new InvalidDataException("Truncated WAV container.");
        }
        ReadOnlySpan<byte> format = default;
        ReadOnlySpan<byte> data = default;
        for (long offset = 12; offset < end;)
        {
            if (offset + 8 > end)
            {
                throw new InvalidDataException("Truncated WAV chunk header.");
            }
            var header = bytes[(int)offset..];
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
            long next = offset + 8 + size + (size & 1);
            if (next > end)
            {
                throw new InvalidDataException("Truncated WAV chunk.");
            }
            var chunk = bytes.Slice((int)offset + 8, (int)size);
            if (header[..4].SequenceEqual("fmt "u8))
            {
                format = chunk;
            }
            else if (header[..4].SequenceEqual("data"u8))
            {
                data = chunk;
            }
            offset = next;
        }
        if (format.Length < 16 || data.IsEmpty)
        {
            throw new InvalidDataException("WAV needs format and nonempty data chunks.");
        }
        int encoding = BinaryPrimitives.ReadUInt16LittleEndian(format);
        int channels = BinaryPrimitives.ReadUInt16LittleEndian(format[2..]);
        uint rate = BinaryPrimitives.ReadUInt32LittleEndian(format[4..]);
        int alignment = BinaryPrimitives.ReadUInt16LittleEndian(format[12..]);
        int bits = BinaryPrimitives.ReadUInt16LittleEndian(format[14..]);
        if (channels is not (1 or 2) || rate < 8000 || rate > 192000 ||
            !((encoding == 1 && bits is 8 or 16 or 24 or 32) || (encoding == 3 && bits == 32)))
        {
            throw new NotSupportedException("Supported WAV: mono/stereo PCM8/16/24/32 or float32, 8–192 kHz.");
        }
        int width = bits / 8;
        if (alignment != channels * width || data.Length % alignment != 0 || data.Length > 64 * 1024 * 1024)
        {
            throw new InvalidDataException("Invalid WAV frame alignment or clip exceeds 64 MiB. Stream long music separately.");
        }
        var pcm = new short[data.Length / width];
        for (int i = 0; i < pcm.Length; i++)
        {
            var sample = data[(i * width)..];
            if (encoding == 3)
            {
                float value = BinaryPrimitives.ReadSingleLittleEndian(sample);
                if (!float.IsFinite(value))
                {
                    throw new InvalidDataException("WAV contains a non-finite sample.");
                }
                pcm[i] = (short)Math.Clamp((int)(Math.Clamp(value, -1, 1) * 32768), short.MinValue, short.MaxValue);
            }
            else
            {
                pcm[i] = bits switch
                {
                    8 => (short)((sample[0] - 128) << 8),
                    16 => BinaryPrimitives.ReadInt16LittleEndian(sample),
                    24 => (short)((sample[0] | sample[1] << 8 | sample[2] << 16) >> 8),
                    _ => (short)(BinaryPrimitives.ReadInt32LittleEndian(sample) >> 16)
                };
            }
        }
        return new ManaAudioClip(pcm, channels, (int)rate);
    }
}
