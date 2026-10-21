using System.Collections.Concurrent;
using ElkaVoicePlay.Core;
using ElkaVoicePlay.Shared;
using Xunit;

namespace ElkaVoicePlay.Tests;

public sealed class TranscriptionTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "VoicePlay-transcription-tests-" + Guid.NewGuid().ToString("N"));
    private TranscriptionSettings Settings(bool combine = true, int enabled = 3) => new()
    {
        Folder = root, Combine = combine, Sources = Enumerable.Range(1, 3).Select(i => new TranscriptSource
        { Slot = i, Name = $"Source {i}", Enabled = i <= enabled, DeviceId = $"device-{i}", Kind = i == 2 ? TranscriptInputKind.Loopback : TranscriptInputKind.Microphone }).ToArray()
    };
    [Fact] public void RequiresAtLeastOneInputAndUniqueNamesAndDevices()
    {
        Assert.Throws<InvalidDataException>(() => Settings(enabled: 0).Validate());
        var value = Settings();
        Assert.Throws<InvalidDataException>(() => (value with { Sources = [value.Sources[0], value.Sources[1] with { Name = "source 1" }, value.Sources[2]] }).Validate());
        Assert.Throws<InvalidDataException>(() => (value with { Sources = [value.Sources[0], value.Sources[1], value.Sources[2] with { DeviceId = "device-1" }] }).Validate());
        Assert.Throws<InvalidDataException>(() => (value with { Version = 2 }).Validate());
    }
    [Fact] public void DisabledRowsNeedNoDeviceOrName()
    {
        var value = Settings(enabled: 1);
        value = value with { Sources = [value.Sources[0], value.Sources[1] with { DeviceId = null, Name = "" }, value.Sources[2] with { DeviceId = null, Name = "" }] };
        Assert.Single(value.Validate().Sources, s => s.Enabled);
    }
    [Fact] public void EnabledRowRequiresAnExplicitDeviceAndCleanName()
    {
        var value = Settings(enabled: 1);
        Assert.Throws<InvalidDataException>(() => (value with { Sources = [value.Sources[0] with { DeviceId = null }, value.Sources[1], value.Sources[2]] }).Validate());
        Assert.Throws<InvalidDataException>(() => (value with { Sources = [value.Sources[0] with { Name = "bad\nlabel" }, value.Sources[1], value.Sources[2]] }).Validate());
    }
    [Fact] public void SharedTimelineRetainsStartupAndSilentGaps()
    {
        var windows = new List<TranscriptAudioWindow>(); var timeline = new TranscriptTimeline(windows.Add);
        timeline.Add(16000, new float[] { 0.3f, 0.4f });
        timeline.Advance(160000); timeline.Advance(320000);
        timeline.Add(320010, new float[] { 0.9f }); timeline.Finish(320100);
        Assert.Equal(new[] { 0, 1, 2 }, windows.Select(w => w.Index));
        Assert.Equal(0f, windows[0].Samples[15999]); Assert.Equal(0.3f, windows[0].Samples[16000]);
        Assert.All(windows[1].Samples, value => Assert.Equal(0f, value));
        Assert.Equal(100, windows[2].Samples.Length); Assert.Equal(0.9f, windows[2].Samples[10]);
        Assert.Equal(TimeSpan.FromSeconds(20), windows[2].Start);
    }
    [Fact] public void PacketCrossingWindowBoundaryKeepsEverySample()
    {
        var windows = new List<TranscriptAudioWindow>(); var timeline = new TranscriptTimeline(windows.Add);
        timeline.Add(159998, new float[] { 1, 2, 3, 4 }); timeline.Finish(160002);
        Assert.Equal(new float[] { 1, 2 }, windows[0].Samples[^2..]); Assert.Equal(new float[] { 3, 4 }, windows[1].Samples);
        timeline.Finish(160002); Assert.Equal(2, windows.Count);
    }
    [Fact] public void PreSessionAudioIsTrimmedAndLateAudioIsReported()
    {
        var windows = new List<TranscriptAudioWindow>(); var timeline = new TranscriptTimeline(windows.Add);
        timeline.Add(-2, new float[] { 1, 2, 3 }); timeline.Advance(160000);
        Assert.Equal(3f, windows[0].Samples[0]);
        Assert.Throws<InvalidOperationException>(() => timeline.Add(120000, new float[] { 1 }));
    }
    [Fact] public void PacketRoundingAtCommittedBoundaryDoesNotStopCapture()
    {
        var windows = new List<TranscriptAudioWindow>(); var timeline = new TranscriptTimeline(windows.Add);
        timeline.Add(159998, new float[] { 1, 2 });
        timeline.Add(159999, new float[] { 2, 3, 4 }); timeline.Finish(160002);
        Assert.Equal(new float[] { 3, 4 }, windows[1].Samples);
    }
    [Fact] public async Task QuietInputRetainsItsOffsetWhenWhisperReportsZeroStart()
    {
        var result = await new TranscriptionSession(new Capture { Leading = 32000, Trailing = 32000 }, new Recognizer { BadRange = true }).RunAsync(Settings(enabled: 1), default);
        string text = await File.ReadAllTextAsync(result.Files[0]);
        Assert.Contains("[00:00:01.900 --> 00:00:08.100] [Source 1] first second", text);
    }
    [Fact] public void ClockUsesHundredNanosecondPacketTimestamps()
    {
        var clock = new TranscriptClock();
        Assert.Equal(16000, clock.SampleAt(clock.Origin100ns + TimeSpan.TicksPerSecond));
        Assert.Equal(0, clock.SampleAt(clock.Origin100ns));
    }
    [Fact] public async Task CombinedFileSortsOverlappingSourcesAndPreservesUnicode()
    {
        await using var files = new TranscriptFiles(Settings(), DateTimeOffset.Now);
        await files.AppendAsync([
            new(1,"Me",TimeSpan.FromSeconds(5),TimeSpan.FromSeconds(8),"Þú ert hér 😀"),
            new(2,"Video",TimeSpan.FromSeconds(2),TimeSpan.FromSeconds(6),"Other speaker")]);
        await files.CompleteAsync(null);
        string text = await ReadSharedAsync(Assert.Single(files.Paths));
        Assert.True(text.IndexOf("[Video]", StringComparison.Ordinal) < text.IndexOf("[Me]", StringComparison.Ordinal));
        Assert.Contains("[00:00:05.000 --> 00:00:08.000] [Me] Þú ert hér 😀", text);
    }
    [Fact] public async Task SeparateFilesExistOnlyForEnabledInputsAndCannotEscapeTheirFolder()
    {
        var value = Settings(combine: false, enabled: 2);
        value = value with { Sources = [value.Sources[0] with { Name = "../CON:video" }, value.Sources[1], value.Sources[2]] };
        await using var files = new TranscriptFiles(value, DateTimeOffset.Now);
        Assert.Equal(2, files.Paths.Length);
        Assert.All(files.Paths, p => Assert.Equal(files.DirectoryPath, Path.GetDirectoryName(p)));
        await files.AppendAsync([new(2,"Source 2",TimeSpan.Zero,TimeSpan.FromSeconds(1),"only second")]);
        Assert.DoesNotContain("only second", await ReadSharedAsync(files.Paths[0]));
        Assert.Contains("only second", await ReadSharedAsync(files.Paths[1]));
    }
    [Fact] public async Task NewSessionsNeverOverwritePreviousFiles()
    {
        var now = DateTimeOffset.Now;
        await using var first = new TranscriptFiles(Settings(), now);
        await using var second = new TranscriptFiles(Settings(), now);
        Assert.NotEqual(first.DirectoryPath, second.DirectoryPath);
    }
    [Fact] public void LongSessionsDoNotWrapAtTwentyFourHours()
        => Assert.Equal("25:01:02.123", TranscriptFiles.FormatTime(TimeSpan.FromHours(25) + TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(2.123)));
    [Fact] public async Task ThreeInputsAreRecognizedSeparatelyAndMergedByAudioTime()
    {
        var capture = new Capture(); var recognizer = new Recognizer();
        var result = await new TranscriptionSession(capture, recognizer).RunAsync(Settings(), default);
        Assert.Null(result.Error); Assert.Equal(3, result.Lines); Assert.Equal(3, capture.Slots.Count);
        string text = await File.ReadAllTextAsync(result.Files[0]);
        Assert.True(text.IndexOf("[Source 2]", StringComparison.Ordinal) < text.IndexOf("[Source 3]", StringComparison.Ordinal));
        Assert.True(text.IndexOf("[Source 3]", StringComparison.Ordinal) < text.IndexOf("[Source 1]", StringComparison.Ordinal));
        Assert.Equal(1, recognizer.MaximumConcurrent);
    }
    [Fact] public async Task DisabledInputsAreNeverOpenedOrRecognized()
    {
        var capture = new Capture(); var recognizer = new Recognizer();
        var result = await new TranscriptionSession(capture, recognizer).RunAsync(Settings(false, 1), default);
        Assert.Equal(new[] { 1 }, capture.Slots); Assert.Single(result.Files); Assert.Equal(1, recognizer.Calls);
    }
    [Fact] public async Task SilenceProducesNoInventedTranscriptLines()
    {
        var recognizer = new Recognizer();
        var result = await new TranscriptionSession(new Capture { Silent = true }, recognizer).RunAsync(Settings(), default);
        Assert.Equal(0, result.Lines); Assert.Equal(0, recognizer.Calls); Assert.Null(result.Error);
    }
    [Fact] public async Task StopFinishesPartialAudioInsteadOfCancellingRecognition()
    {
        using var stop = new CancellationTokenSource(); var capture = new Capture { WaitForStop = true };
        var session = new TranscriptionSession(capture, new Recognizer());
        var run = session.RunAsync(Settings(enabled: 1), stop.Token);
        await capture.Started.Task; stop.Cancel(); var result = await run;
        Assert.Null(result.Error); Assert.Equal(1, result.Lines);
    }
    [Fact] public async Task OverloadIsExplicitAndMemoryQueueIsBounded()
    {
        var result = await new TranscriptionSession(new Capture { Burst = 20 }, new Recognizer()).RunAsync(Settings(enabled: 1), default);
        Assert.Contains("could not keep up", result.Error); Assert.InRange(result.Lines, 1, 6);
        Assert.Contains("Recording stopped with an error", await File.ReadAllTextAsync(result.Files[0]));
    }
    [Fact] public async Task DeviceFailureStopsSessionButKeepsOtherCompletedText()
    {
        var capture = new Capture { FailSlot = 2 };
        var result = await new TranscriptionSession(capture, new Recognizer()).RunAsync(Settings(), default);
        Assert.Contains("Source 2", result.Error); Assert.True(File.Exists(result.Files[0]));
    }
    [Fact] public async Task RecognitionFailureStopsCaptureAndKeepsAReadablePartialFile()
    {
        var result = await new TranscriptionSession(new Capture(), new Recognizer { Fail = true }).RunAsync(Settings(), default);
        Assert.Contains("recognition test failure", result.Error); Assert.True(File.Exists(result.Files[0]));
    }
    [Fact] public async Task TimestampRangesAreClampedToCapturedAudioAndTextIsNormalized()
    {
        var result = await new TranscriptionSession(new Capture(), new Recognizer { BadRange = true }).RunAsync(Settings(enabled: 1), default);
        string text = await File.ReadAllTextAsync(result.Files[0]);
        Assert.Contains("[00:00:00.000 --> 00:00:10.000] [Source 1] first second", text);
    }
    [Fact] public async Task SettingsRoundTripWithoutChangingGameSettings()
    {
        var path = Path.Combine(root, "transcription-settings.json"); var store = new TranscriptionSettingsStore(path);
        await store.SaveAsync(Settings(false, 2)); var loaded = await store.LoadAsync();
        Assert.False(loaded.Combine); Assert.Equal(2, loaded.Sources.Count(s => s.Enabled));
        Assert.Equal(TranscriptInputKind.Loopback, loaded.Sources[1].Kind);
        Assert.False(File.Exists(Path.Combine(root, "settings.json")));
    }
    private sealed class Capture : ITranscriptCapture
    {
        internal ConcurrentQueue<int> Slots = new();
        internal bool Silent, WaitForStop;
        internal int Burst = 1, FailSlot, Leading, Trailing;
        internal TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task RunAsync(TranscriptSource source, TranscriptClock clock, Action<TranscriptAudioWindow> onWindow, Action<float> level, CancellationToken stop)
        {
            Slots.Enqueue(source.Slot); Started.TrySetResult();
            if (FailSlot == source.Slot) throw new InvalidOperationException("device disconnected");
            if (WaitForStop) { try { await Task.Delay(Timeout.Infinite, stop); } catch (OperationCanceledException) { } }
            for (int i = 0; i < Burst; i++)
            {
                var samples = new float[TranscriptAudioWindow.SampleCount];
                if (!Silent) Array.Fill(samples, source.Slot, Leading, samples.Length - Leading - Trailing);
                onWindow(new(i, samples));
            }
        }
    }
    private sealed class Recognizer : ITranscriptRecognizer
    {
        internal int Calls, MaximumConcurrent;
        private int concurrent;
        internal bool Fail, BadRange;
        public async Task<IReadOnlyList<SpeechSegment>> TranscribeSegmentsAsync(float[] samples, string language, CancellationToken token, bool translateToEnglish = false)
        {
            Assert.False(token.IsCancellationRequested); Calls++;
            MaximumConcurrent = Math.Max(MaximumConcurrent, Interlocked.Increment(ref concurrent));
            try
            {
                await Task.Yield();
                if (Fail) throw new InvalidOperationException("recognition test failure");
                if (BadRange) return [new(TimeSpan.FromSeconds(-1), TimeSpan.FromSeconds(20), "first\nsecond")];
                int seconds = samples[0] == 1 ? 5 : samples[0] == 2 ? 1 : 3;
                return [new(TimeSpan.FromSeconds(seconds), TimeSpan.FromSeconds(seconds + 1), "recognized")];
            }
            finally { Interlocked.Decrement(ref concurrent); }
        }
    }
    private static async Task<string> ReadSharedAsync(string path)
    {
        using var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
        return await reader.ReadToEndAsync();
    }
    public void Dispose()
    {
        var full = Path.GetFullPath(root);
        if (!full.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("VoicePlay-transcription-tests-", StringComparison.Ordinal))
            throw new InvalidOperationException("Unexpected test directory.");
        if (Directory.Exists(full)) Directory.Delete(full, true);
    }
}
