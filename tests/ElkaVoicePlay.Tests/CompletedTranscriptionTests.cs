using System.Text.Json;
using ElkaVoicePlay.Core;
using ElkaVoicePlay.Shared;
using Xunit;

namespace ElkaVoicePlay.Tests;

public sealed class CompletedTranscriptionTests
{
    [Fact] public void ConvertedPacketJitterPreservesEverySampleAcrossCommittedBoundaries()
    {
        var output = new List<TranscriptAudioWindow>(); var timeline = new TranscriptTimeline(output.Add, 8000);
        var clock = new TranscriptPacketClock();
        var expected = new List<float>();
        for (int i = 0; i < 10000; i++)
        {
            var samples = Enumerable.Repeat((float)(i + 1), 160).ToArray(); expected.AddRange(samples);
            long reported = i * 160L + (i == 0 ? 0 : (i % 11 - 5) * 16);
            timeline.Add(clock.Place(reported, samples.Length, timeline.CommittedSamples), samples);
        }
        timeline.Finish(clock.NextSample);
        Assert.Equal(expected, output.SelectMany(w => w.Samples));
    }
    [Fact] public void QuietLoopbackResumePreservesGapAndAllResumedSamples()
    {
        var clock = new TranscriptPacketClock();
        Assert.Equal(0, clock.Place(0, 160, 0));
        Assert.Equal(96000, clock.Place(96000, 160, 88000));
        Assert.Equal(96160, clock.Place(96120, 160, 88000));
        Assert.Equal(97000, clock.Place(96200, 160, 97000));
    }
    [Fact] public void OldSettingsDefaultToOriginalLanguageWithoutSpeakerLabels()
    {
        var settings = JsonSerializer.Deserialize<TranscriptionSettings>("{\"Version\":1,\"Language\":\"de\"}")!;
        Assert.False(settings.TranslateToEnglish); Assert.False(settings.LabelSpeakers);
        var roundtrip = JsonSerializer.Deserialize<TranscriptionSettings>(JsonSerializer.Serialize(settings with { TranslateToEnglish = true, LabelSpeakers = true }))!;
        Assert.True(roundtrip.TranslateToEnglish); Assert.True(roundtrip.LabelSpeakers); Assert.Equal("de", roundtrip.Language);
    }
    [Fact] public void SpeakerRegistryReusesVoicesAndRejectsInvalidAndAmbiguousEmbeddings()
    {
        var registry = new SpeakerRegistry();
        Assert.Equal("Speaker 1", registry.Match([1, 0]));
        Assert.Equal("Speaker 2", registry.Match([0, 1]));
        Assert.Equal("Speaker 1", registry.Match([0.95f, 0.05f]));
        Assert.Equal("Unknown speaker", registry.Match([0.7f, 0.7f]));
        Assert.Equal("Unknown speaker", registry.Match([float.NaN, 0]));
        Assert.Equal("Unknown speaker", registry.Match([0, 0]));
        Assert.Equal("Speaker 1", new SpeakerRegistry().Match([0, 1]));
        Assert.Equal("Speaker 1", registry.Match([0.5f, 0.5f], "Speaker 1"));
        Assert.Equal("Speaker 1", registry.Match([0.5f, 0.5f]));
    }
    [Fact] public void SpeakerTurnsCoverAudioOnceAndKeepOverlapUnattributed()
    {
        var turns = SpeakerTurnBuilder.Build([new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4), "Speaker 1"),
            new(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(6), "Speaker 2")], TimeSpan.FromSeconds(7));
        Assert.Equal(new[] { "Speaker 1", "Overlapping speakers", "Speaker 2" }, turns.Select(t => t.Label));
        Assert.Equal(TimeSpan.Zero, turns[0].Start); Assert.Equal(TimeSpan.FromSeconds(7), turns[^1].End);
        for (int i = 1; i < turns.Count; i++) Assert.Equal(turns[i - 1].End, turns[i].Start);
        Assert.Equal(TimeSpan.FromSeconds(3), turns[1].Start); Assert.Equal(TimeSpan.FromSeconds(4), turns[1].End);
    }
    [Fact] public void MissingSpeakerTurnsRetainUnattributedAudio()
    {
        var turns = SpeakerTurnBuilder.Build([], TimeSpan.FromSeconds(2));
        Assert.Single(turns); Assert.Equal("Unknown speaker", turns[0].Label); Assert.Equal(TimeSpan.FromSeconds(2), turns[0].End);
    }
    [Fact] public async Task TranslationAndSpeakerOffsetsReachSavedFinalLines()
    {
        string root = Path.Combine(Path.GetTempPath(), "VoicePlay-speaker-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var settings = new TranscriptionSettings { Folder = root, Language = "de", TranslateToEnglish = true, LabelSpeakers = true,
                Sources = Enumerable.Range(1, 3).Select(i => new TranscriptSource { Slot = i, Name = "Input " + i, DeviceId = "test", Enabled = i == 1 }).ToArray() };
            var recognizer = new Recognizer(); var diarizer = new Diarizer();
            var session = new TranscriptionSession(new Capture(), recognizer, diarizer);
            var lines = new List<TranscriptLine>(); session.LinesWritten += batch => lines.AddRange(batch);
            var result = await session.RunAsync(settings, default);
            Assert.Null(result.Error); Assert.Equal(2, lines.Count); Assert.Equal(2, recognizer.Calls); Assert.Equal(1, diarizer.Calls);
            Assert.Equal(TimeSpan.FromSeconds(3), lines[0].Start); Assert.Equal(TimeSpan.FromSeconds(4), lines[1].Start);
            Assert.Equal("Speaker 2", lines[1].Speaker);
            string text = await File.ReadAllTextAsync(result.Files[0]); Assert.Contains("English translation", text); Assert.Contains("[Speaker 2] hello", text);
            var disabled = new TranscriptionSession(new Capture(), new Recognizer(false), diarizer);
            Assert.Null((await disabled.RunAsync(settings with { LabelSpeakers = false, TranslateToEnglish = false }, default)).Error);
            Assert.Equal(1, diarizer.Calls);
        }
        finally
        {
            string full = Path.GetFullPath(root);
            Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), full, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith("VoicePlay-speaker-test-", Path.GetFileName(full));
            if (Directory.Exists(full)) Directory.Delete(full, true);
        }
    }
    private sealed class Capture : ITranscriptCapture
    {
        public Task RunAsync(TranscriptSource source, TranscriptClock clock, Action<TranscriptAudioWindow> onWindow, Action<float> onLevel, CancellationToken stop)
        {
            onWindow(new(0, Enumerable.Repeat(0.1f, 32000).ToArray(), 48000)); return Task.CompletedTask;
        }
    }
    private sealed class Diarizer : ISpeakerDiarizer
    {
        public int Calls;
        public Task<IReadOnlyList<SpeakerTurn>> AnalyzeAsync(int slot, float[] samples, CancellationToken token)
        {
            Calls++; return Task.FromResult<IReadOnlyList<SpeakerTurn>>([new(TimeSpan.Zero, TimeSpan.FromSeconds(1), "Speaker 1"), new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), "Speaker 2")]);
        }
    }
    private sealed class Recognizer(bool translated = true) : ITranscriptRecognizer
    {
        public int Calls;
        public Task<IReadOnlyList<SpeechSegment>> TranscribeSegmentsAsync(float[] samples, string language, CancellationToken token, bool translateToEnglish = false)
        {
            Assert.Equal("de", language); Assert.Equal(translated, translateToEnglish); Calls++;
            return Task.FromResult<IReadOnlyList<SpeechSegment>>([new(TimeSpan.Zero, TimeSpan.FromSeconds(samples.Length / 16000d), "hello")]);
        }
    }
}
