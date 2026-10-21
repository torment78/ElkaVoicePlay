using System.Runtime.InteropServices;
using ElkaVoicePlay.Core;
using ElkaVoicePlay.Shared;

namespace ElkaVoicePlay.Speech;

// NeMo-Speech.cpp 0.2.0 stable C ABI. Each source owns a persistent arrival-order speaker cache.
internal sealed class NemotronDiarizer : ISpeakerDiarizer, IDisposable
{
    private IntPtr model;
    private readonly Dictionary<int, StreamState> streams = [];
    private sealed class StreamState(IntPtr handle) { public IntPtr Handle = handle; public long Samples; }
    public NemotronDiarizer(string modelPath, bool gpu)
    {
        var config = new ModelConfig { Size = (nuint)Marshal.SizeOf<ModelConfig>(), ModelPath = modelPath,
            Gpu = gpu ? 0 : -1, Preset = "v3-offline", LeftContext = -1 };
        Check(Create(ref config, out model));
        if (NumSpeakers(model) != 8) { Dispose(); throw new InvalidDataException("Expected the eight-speaker Nemotron-3 model."); }
    }
    public Task<IReadOnlyList<SpeakerTurn>> AnalyzeAsync(int slot, float[] samples, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (slot < 1 || slot > 3) throw new InvalidDataException("Invalid input slot.");
        if (!streams.TryGetValue(slot, out var state))
        { Check(Open(model, out var handle)); streams.Add(slot, state = new(handle)); }
        double start = state.Samples / 16000.0;
        double end = start + samples.Length / 16000.0;
        Check(Push(state.Handle, samples, (nuint)samples.Length, 16000));
        // The high-context preset needs at most 30.4 seconds of lookahead. Padding
        // supplies it immediately in memory; this does not wait 31 seconds in real time.
        // A completed phrase supplies silence as right context. Keep the same stream/cache alive;
        // Finish would close it and lose cross-phrase identities. Padding is excluded from output.
        float[] padding = new float[31 * 16000];
        Check(Push(state.Handle, padding, (nuint)padding.Length, 16000));
        state.Samples += samples.Length + padding.Length;
        if (FrameCount(state.Handle) * SecondsPerFrame(model) + 0.02 < end)
            throw new InvalidDataException("Speaker model has not completed the phrase.");
        var segmentation = new Segmentation { Size = (nuint)Marshal.SizeOf<Segmentation>(), Onset = 0.5f, Offset = 0.4f,
            PadOnset = 0.1, PadOffset = 0.1, MinGap = 0.2, MinDuration = 0.15 };
        Check(Segments(state.Handle, ref segmentation, null, 0, out nuint count));
        if (count > 1000000) throw new InvalidDataException("Too many speaker segments.");
        var segments = new Segment[(int)count];
        Check(Segments(state.Handle, ref segmentation, segments, count, out _));
        var turns = segments.Where(s => s.End > start && s.Start < end && s.Speaker >= 1 && s.Speaker <= 8)
            .Select(s => new SpeakerTurn(TimeSpan.FromSeconds(Math.Max(0, s.Start - start)),
                TimeSpan.FromSeconds(Math.Min(end, s.End) - start), $"Speaker {s.Speaker}", s.Speaker));
        return Task.FromResult(SpeakerTurnBuilder.Build(turns, TimeSpan.FromSeconds(samples.Length / 16000.0)));
    }
    private static void Check(int status) { if (status != 0) throw new InvalidOperationException(Marshal.PtrToStringUTF8(LastError()) ?? $"Nemotron error {status}"); }
    public void Dispose()
    {
        foreach (var state in streams.Values) Close(state.Handle); streams.Clear();
        if (model != IntPtr.Zero) { Destroy(model); model = IntPtr.Zero; }
    }
    [StructLayout(LayoutKind.Sequential)] private struct ModelConfig
    {
        public nuint Size; [MarshalAs(UnmanagedType.LPUTF8Str)] public string ModelPath; public int Gpu;
        [MarshalAs(UnmanagedType.LPUTF8Str)] public string Preset;
        public int Chunk, RightContext, LeftContext, Fifo, Cache, Update;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Segmentation
    { public nuint Size; public float Onset, Offset; public double PadOnset, PadOffset, MinGap, MinDuration; }
    [StructLayout(LayoutKind.Sequential)] private struct Segment { public double Start, End; public int Speaker; }
    private const string Library = "nemo_speech_asr_c.dll";
    [DllImport(Library, EntryPoint = "nemo_speech_diar_create", CallingConvention = CallingConvention.Cdecl)] private static extern int Create(ref ModelConfig config, out IntPtr model);
    [DllImport(Library, EntryPoint = "nemo_speech_diar_destroy", CallingConvention = CallingConvention.Cdecl)] private static extern void Destroy(IntPtr model);
    [DllImport(Library, EntryPoint = "nemo_speech_asr_last_error", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr LastError();
    [DllImport(Library, EntryPoint = "nemo_speech_diar_num_speakers", CallingConvention = CallingConvention.Cdecl)] private static extern int NumSpeakers(IntPtr model);
    [DllImport(Library, EntryPoint = "nemo_speech_diar_seconds_per_frame", CallingConvention = CallingConvention.Cdecl)] private static extern double SecondsPerFrame(IntPtr model);
    [DllImport(Library, EntryPoint = "nemo_speech_diar_stream_open", CallingConvention = CallingConvention.Cdecl)] private static extern int Open(IntPtr model, out IntPtr stream);
    [DllImport(Library, EntryPoint = "nemo_speech_diar_stream_push_f32", CallingConvention = CallingConvention.Cdecl)] private static extern int Push(IntPtr stream, float[] samples, nuint count, int rate);
    [DllImport(Library, EntryPoint = "nemo_speech_diar_stream_close", CallingConvention = CallingConvention.Cdecl)] private static extern void Close(IntPtr stream);
    [DllImport(Library, EntryPoint = "nemo_speech_diar_frame_count", CallingConvention = CallingConvention.Cdecl)] private static extern long FrameCount(IntPtr stream);
    [DllImport(Library, EntryPoint = "nemo_speech_diar_segments", CallingConvention = CallingConvention.Cdecl)] private static extern int Segments(IntPtr stream, ref Segmentation config, [Out] Segment[]? segments, nuint capacity, out nuint count);
}
