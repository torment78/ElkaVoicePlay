using System.Buffers.Binary;
using System.Text.Json;

namespace ElkaVoicePlay.Shared;

// Bounded messages on a private, current-user pipe. Native logging uses stdout/stderr, never this channel.
public sealed record ModelRequest(string Operation, int Slot = 0, string Language = "auto", bool Translate = false, bool Words = false);
public sealed record ModelReply(string? Error = null, string? Backend = null, SpeechSegment[]? Segments = null, SpeakerTurn[]? Turns = null);
public static class ModelProtocol
{
    public const int MaxSamples = 60 * TranscriptClock.SampleRate;
    public static async Task WriteJsonAsync<T>(Stream stream, T value, CancellationToken token)
        => await WriteBytesAsync(stream, JsonSerializer.SerializeToUtf8Bytes(value), token);
    public static async Task<T> ReadJsonAsync<T>(Stream stream, CancellationToken token)
        => JsonSerializer.Deserialize<T>(await ReadBytesAsync(stream, 4 * 1024 * 1024, token)) ?? throw new InvalidDataException("Empty model response.");
    public static async Task WriteAudioAsync(Stream stream, float[] samples, CancellationToken token)
    {
        if (samples.Length > MaxSamples) throw new InvalidDataException("Model audio request is too large.");
        byte[] bytes = new byte[samples.Length * sizeof(float)]; Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        await WriteBytesAsync(stream, bytes, token);
    }
    public static async Task<float[]> ReadAudioAsync(Stream stream, CancellationToken token)
    {
        var bytes = await ReadBytesAsync(stream, MaxSamples * sizeof(float), token);
        if (bytes.Length % sizeof(float) != 0) throw new InvalidDataException("Invalid audio packet.");
        float[] samples = new float[bytes.Length / sizeof(float)]; Buffer.BlockCopy(bytes, 0, samples, 0, bytes.Length);
        if (samples.Any(v => !float.IsFinite(v))) throw new InvalidDataException("Invalid audio samples.");
        return samples;
    }
    private static async Task WriteBytesAsync(Stream stream, byte[] bytes, CancellationToken token)
    {
        byte[] prefix = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(prefix, bytes.Length);
        await stream.WriteAsync(prefix, token); await stream.WriteAsync(bytes, token); await stream.FlushAsync(token);
    }
    private static async Task<byte[]> ReadBytesAsync(Stream stream, int maximum, CancellationToken token)
    {
        byte[] prefix = new byte[4]; await stream.ReadExactlyAsync(prefix, token);
        int length = BinaryPrimitives.ReadInt32LittleEndian(prefix);
        if (length < 0 || length > maximum) throw new InvalidDataException("Invalid model message length.");
        byte[] bytes = new byte[length]; await stream.ReadExactlyAsync(bytes, token); return bytes;
    }
}
