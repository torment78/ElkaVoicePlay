using System.Security.Cryptography;
using ElkaVoicePlay.Core;
using ElkaVoicePlay.Shared;
using SherpaOnnx;

namespace ElkaVoicePlay.Speech;

/// <summary>CPU-only, lazy-loaded speaker detection. Voice embeddings live only for this session.</summary>
public sealed class SpeakerDiarizer : ISpeakerDiarizer, IDisposable
{
    public const string SegmentationHash = "220AD67CA923BEF2FA91F2390C786097BF305BCEB5E261D4AF67B38E938E1079";
    public const string EmbeddingHash = "1A331345F04805BADBB495C775A6DDFFCDD1A732567D5EC8B3D5749E3C7A5E4B";
    private readonly OfflineSpeakerDiarization detector;
    private readonly SpeakerEmbeddingExtractor extractor;
    private readonly Dictionary<int, SpeakerRegistry> registries = [];
    private readonly Dictionary<int, float[]> history = [];
    private readonly Dictionary<int, SpeakerTurn[]> previousTurns = [];
    public static string DefaultDirectory => Path.Combine(AppContext.BaseDirectory, "Models", "Speakers");
    public static bool ModelsPresent => File.Exists(Path.Combine(DefaultDirectory, "segmentation.onnx")) && File.Exists(Path.Combine(DefaultDirectory, "embedding.onnx"));

    public SpeakerDiarizer(string? directory = null)
    {
        directory ??= DefaultDirectory;
        string segmentation = Verify(directory, "segmentation.onnx", SegmentationHash);
        string embedding = Verify(directory, "embedding.onnx", EmbeddingHash);
        var config = new OfflineSpeakerDiarizationConfig();
        config.Segmentation.Pyannote.Model = segmentation;
        config.Segmentation.NumThreads = 2;
        config.Segmentation.Provider = "cpu";
        config.Embedding.Model = embedding;
        config.Embedding.NumThreads = 2;
        config.Embedding.Provider = "cpu";
        config.Clustering.NumClusters = -1;
        config.Clustering.Threshold = 0.5f;
        config.MinDurationOn = 0.3f;
        config.MinDurationOff = 0.5f;
        detector = new OfflineSpeakerDiarization(config);
        try
        {
            if (detector.SampleRate != TranscriptClock.SampleRate) throw new InvalidOperationException("Unexpected speaker model sample rate.");
            extractor = new SpeakerEmbeddingExtractor(new SpeakerEmbeddingExtractorConfig { Model = embedding, NumThreads = 2, Provider = "cpu" });
        }
        catch { detector.Dispose(); throw; }
    }
    private static string Verify(string directory, string file, string hash)
    {
        string path = Path.Combine(directory, file);
        if (!File.Exists(path)) throw new FileNotFoundException("Speaker models are missing. Extract the complete VoicePlay folder, including Models/Speakers, or turn off Label speakers.", path);
        using var stream = File.OpenRead(path);
        if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(hash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Speaker model {file} is incomplete or damaged. Extract the complete application again.");
        return path;
    }
    public Task<IReadOnlyList<SpeakerTurn>> AnalyzeAsync(int slot, float[] samples, CancellationToken token) => Task.Run<IReadOnlyList<SpeakerTurn>>(() =>
    {
        token.ThrowIfCancellationRequested();
        var duration = TimeSpan.FromSeconds(samples.Length / (double)TranscriptClock.SampleRate);
        // Keep recent speech context so a returning voice is not re-clustered from a tiny fragment.
        // Retain at most one minute per source, in memory only; prior displayed lines never change.
        var previous = history.GetValueOrDefault(slot) ?? [];
        int keep = Math.Min(previous.Length, Math.Max(0, 60 * TranscriptClock.SampleRate - samples.Length));
        var contextual = new float[keep + samples.Length];
        previous.AsSpan(previous.Length - keep, keep).CopyTo(contextual);
        samples.CopyTo(contextual, keep);
        history[slot] = contextual;
        var offset = TimeSpan.FromSeconds(keep / (double)TranscriptClock.SampleRate);
        var cropped = TimeSpan.FromSeconds((previous.Length - keep) / (double)TranscriptClock.SampleRate);
        var prior = (previousTurns.GetValueOrDefault(slot) ?? []).Select(t => t with { Start = t.Start - cropped, End = t.End - cropped })
            .Where(t => t.End > TimeSpan.Zero).ToArray();
        if (contextual.Length < TranscriptClock.SampleRate) return [new(TimeSpan.Zero, duration, "Unknown speaker")];
        var segments = detector.Process(contextual);
        var detected = segments.Select(s => new SpeakerTurn(TimeSpan.FromSeconds(s.Start), TimeSpan.FromSeconds(s.End), "", s.Speaker)).ToArray();
        token.ThrowIfCancellationRequested();
        if (!registries.TryGetValue(slot, out var registry)) registries.Add(slot, registry = new());
        var labels = new Dictionary<int, string>();
        foreach (var group in segments.GroupBy(s => s.Speaker).OrderBy(g => g.Min(s => s.Start)))
        {
            var voice = new List<float>();
            var groupTurns = detected.Where(t => t.LocalSpeaker == group.Key).ToArray();
            foreach (var turn in groupTurns)
                foreach (var (first, last) in SpeakerEvidence.CleanSamples(turn, detected, contextual.Length))
                    voice.AddRange(contextual.AsSpan(first, last - first).ToArray());
            string? continuing = SpeakerEvidence.ContinuingLabel(groupTurns, prior);
            string label = continuing ?? "Unknown speaker";
            if (voice.Count >= TranscriptClock.SampleRate)
            {
                using var stream = extractor.CreateStream();
                stream.AcceptWaveform(TranscriptClock.SampleRate, voice.ToArray()); stream.InputFinished();
                if (extractor.IsReady(stream)) label = registry.Match(extractor.Compute(stream), continuing);
            }
            labels.Add(group.Key, label);
        }
        previousTurns[slot] = segments.Select(s => new SpeakerTurn(TimeSpan.FromSeconds(s.Start), TimeSpan.FromSeconds(s.End), labels[s.Speaker], s.Speaker)).ToArray();
        return SpeakerTurnBuilder.Build(segments.Where(s => TimeSpan.FromSeconds(s.End) > offset)
            .Select(s => new SpeakerTurn(TimeSpan.FromSeconds(s.Start) - offset, TimeSpan.FromSeconds(s.End) - offset, labels[s.Speaker], s.Speaker)), duration);
    }, token);

    public void Dispose() { extractor.Dispose(); detector.Dispose(); registries.Clear(); history.Clear(); previousTurns.Clear(); }
}
