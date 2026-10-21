using System.Diagnostics;

namespace ElkaVoicePlay.Shared;

public enum TranscriptModel { Base = 0, LargeV3 = 1, Medium = 2 }
public enum SpeakerModel { Classic, Nemotron3 }
public enum ModelAcceleration { Automatic, Cpu, NvidiaGpu }
public enum TranscriptInputKind { Microphone, Loopback }
public sealed record TranscriptSource
{
    public int Slot { get; init; }
    public bool Enabled { get; init; }
    public string Name { get; init; } = "";
    public TranscriptInputKind Kind { get; init; }
    public string? DeviceId { get; init; }
}
public sealed record TranscriptionSettings
{
    public int Version { get; init; } = 1;
    public bool Combine { get; init; } = true;
    public bool TranslateToEnglish { get; init; }
    public bool LabelSpeakers { get; init; }
    public TranscriptModel TranscriptModel { get; init; } = TranscriptModel.Base;
    public SpeakerModel SpeakerModel { get; init; } = SpeakerModel.Classic;
    public ModelAcceleration Acceleration { get; init; } = ModelAcceleration.Automatic;
    public string Folder { get; init; } = DataPaths.Transcripts;
    public string Language { get; init; } = "auto";
    public TranscriptSource[] Sources { get; init; } =
    [
        new() { Slot = 1, Name = "Microphone", Enabled = true },
        new() { Slot = 2, Name = "Headphones", Enabled = true, Kind = TranscriptInputKind.Loopback },
        new() { Slot = 3, Name = "Aux" }
    ];
    public TranscriptionSettings Validate(bool forRecording = true)
    {
        if (!Enum.IsDefined(TranscriptModel) || !Enum.IsDefined(SpeakerModel) || !Enum.IsDefined(Acceleration))
            throw new InvalidDataException("Choose supported transcription, speaker and processing options.");
        if (Version != 1) throw new InvalidDataException("Unsupported transcription settings version; the file has been preserved.");
        if (Sources == null || Sources.Length != 3 || Sources.Any(s => s == null) ||
            !Sources.Select(s => s.Slot).Order().SequenceEqual(new[] { 1, 2, 3 }))
            throw new InvalidDataException("Three unique input slots are required.");
        var sources = Sources.Select(s => s with { Name = s.Name?.Trim() ?? "" }).OrderBy(s => s.Slot).ToArray();
        var enabled = sources.Where(s => s.Enabled).ToArray();
        if (forRecording && enabled.Length == 0) throw new InvalidDataException("Enable at least one audio input.");
        foreach (var s in enabled)
        {
            if (string.IsNullOrWhiteSpace(s.Name) || s.Name.Length > 60 || s.Name.Any(char.IsControl))
                throw new InvalidDataException($"Give input {s.Slot} a name of 1–60 characters without line breaks.");
            if (!Enum.IsDefined(s.Kind) || (forRecording && string.IsNullOrWhiteSpace(s.DeviceId)))
                throw new InvalidDataException($"Choose an available device for {s.Name}.");
        }
        if (enabled.Select(s => s.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != enabled.Length)
            throw new InvalidDataException("Give each enabled input a different name.");
        if (forRecording && enabled.Select(s => (s.Kind, s.DeviceId)).Distinct().Count() != enabled.Length)
            throw new InvalidDataException("Choose a different device for each enabled input.");
        if (string.IsNullOrWhiteSpace(Folder) || !Path.IsPathFullyQualified(Folder))
            throw new InvalidDataException("Choose an absolute output folder.");
        return this with { Sources = sources, Folder = Path.GetFullPath(Folder), Language = string.IsNullOrWhiteSpace(Language) ? "auto" : Language.Trim().ToLowerInvariant() };
    }
}
public sealed record AudioEndpointChoice(string Id, string Name, TranscriptInputKind Kind, bool IsDefault = false)
{
    public override string ToString() => $"{(Kind == TranscriptInputKind.Loopback ? "Speakers / loopback" : "Microphone / input")} · {Name}";
}
public sealed class TranscriptClock
{
    public const int SampleRate = 16000;
    public DateTimeOffset StartedAt { get; } = DateTimeOffset.Now;
    public long Origin100ns { get; } = Now100ns;
    public static long Now100ns => (long)(Stopwatch.GetTimestamp() * (TimeSpan.TicksPerSecond / (double)Stopwatch.Frequency));
    public long ElapsedSamples => Math.Max(0, SampleAt(Now100ns));
    public long SampleAt(long timestamp100ns) => (long)Math.Round((timestamp100ns - Origin100ns) * (SampleRate / (double)TimeSpan.TicksPerSecond));
}
public sealed record TranscriptAudioWindow(int Index, float[] Samples, long? FirstSample = null)
{
    public const int Seconds = 10;
    public const int SampleCount = Seconds * TranscriptClock.SampleRate;
    public TimeSpan Start => TimeSpan.FromSeconds(FirstSample is long first ? first / (double)TranscriptClock.SampleRate : (double)Index * Seconds);
}
public sealed record SpeechSegment(TimeSpan Start, TimeSpan End, string Text);
public sealed record TranscriptLine(int Slot, string Source, TimeSpan Start, TimeSpan End, string Text, string? Speaker = null);
public sealed record SpeakerTurn(TimeSpan Start, TimeSpan End, string Label, int? LocalSpeaker = null);
public interface ISpeakerDiarizer
{
    Task<IReadOnlyList<SpeakerTurn>> AnalyzeAsync(int slot, float[] samples, CancellationToken token);
}
public interface ITranscriptRecognizer
{
    Task<IReadOnlyList<SpeechSegment>> TranscribeSegmentsAsync(float[] samples, string language, CancellationToken token, bool translateToEnglish = false);
}
public interface IWordTranscriptRecognizer : ITranscriptRecognizer
{
    Task<IReadOnlyList<SpeechSegment>> TranscribeWordsAsync(float[] samples, string language, CancellationToken token, bool translateToEnglish);
}
public interface ITranscriptCapture
{
    Task RunAsync(TranscriptSource source, TranscriptClock clock, Action<TranscriptAudioWindow> onWindow,
        Action<float> onLevel, CancellationToken stop);
}
public sealed record TranscriptResult(string Directory, string[] Files, string? Error, int Lines);
