using ElkaVoicePlay.Core;
using ElkaVoicePlay.Shared;
using Xunit;

namespace ElkaVoicePlay.Tests;

public sealed class LiveTranscriptionTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "VoicePlay-live-tests-" + Guid.NewGuid().ToString("N"));
    private TranscriptionSettings Settings(int count = 1) => new()
    {
        Folder = root, Sources = Enumerable.Range(1, 3).Select(i =>
            new TranscriptSource { Slot = i, Name = $"Input {i}", Enabled = i <= count, DeviceId = $"device-{i}" }).ToArray()
    };
    [Fact] public async Task NoDraftDecodesAndOnlyCompletedTextIsSavedOnce()
    {
        using var stop = new CancellationTokenSource();
        var capture = new PausedCapture();
        var recognizer = new Recognizer();
        var final = new List<TranscriptLine>();
        var session = new TranscriptionSession(capture, recognizer);
        session.LineRecognized += final.Add;
        var run = session.RunAsync(Settings(), stop.Token);
        await capture.Ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(run.IsCompleted);
        Assert.Empty(final);
        Assert.Equal(0, recognizer.Calls);
        stop.Cancel();
        var result = await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(result.Error); Assert.Single(final); Assert.Equal(1, result.Lines);
        Assert.Equal(1, recognizer.Calls);
        string text = await File.ReadAllTextAsync(result.Files[0]);
        Assert.Equal(1, text.Split("source-1").Length - 1);
        Assert.DoesNotContain("[live]", text);
    }
    [Fact] public async Task ShorterSecondInputDisplaysFirstButCombinedFileRemainsChronological()
    {
        var displayed = new List<TranscriptLine>(); var written = new List<TranscriptLine>();
        var session = new TranscriptionSession(new OverlapCapture(), new Recognizer());
        session.LineRecognized += displayed.Add;
        session.LinesWritten += lines => written.AddRange(lines);
        var result = await session.RunAsync(Settings(2), default);
        Assert.Null(result.Error);
        Assert.Equal(new[] { 2, 1 }, displayed.Select(l => l.Slot));
        Assert.Equal(new[] { 1, 2 }, written.Select(l => l.Slot));
        Assert.Equal(TimeSpan.FromSeconds(1), written[1].Start);
    }
    [Fact] public void PhraseEndsOnPauseAndKeepsLeadingSilenceOffset()
    {
        var phrase = new TranscriptPhrase();
        phrase.Add(Window(0, 0)); phrase.Add(Window(1, 0)); phrase.Add(Window(2, 1));
        phrase.Add(Window(3, 1)); phrase.Add(Window(4, 1));
        Assert.False(phrase.IsComplete);
        Assert.Equal(TimeSpan.FromSeconds(1), phrase.Start);
        phrase.Add(Window(5, 0)); Assert.False(phrase.IsComplete);
        phrase.Add(Window(6, 0)); Assert.True(phrase.IsComplete);
        Assert.Equal(25600, phrase.Snapshot().Length);
        phrase.Clear(); Assert.False(phrase.HasAudio);
        Assert.Equal(TimeSpan.FromSeconds(3.5), phrase.Watermark);
    }
    [Fact] public void ContinuousSpeechIsBounded()
    {
        var phrase = new TranscriptPhrase();
        for (int i = 0; i < 60; i++) phrase.Add(Window(i, 1));
        Assert.True(phrase.IsComplete);
        Assert.Equal(30 * TranscriptClock.SampleRate, phrase.Snapshot().Length);
    }
    [Fact] public void HalfSecondCapturePacketsPreserveAbsoluteTimeAndTail()
    {
        var windows = new List<TranscriptAudioWindow>();
        var timeline = new TranscriptTimeline(windows.Add, 8000);
        timeline.Add(16000, new float[] { 1, 2 }); timeline.Finish(16002);
        Assert.Equal(3, windows.Count);
        Assert.Equal(TimeSpan.FromSeconds(1), windows[2].Start);
        Assert.Equal(new float[] { 1, 2 }, windows[2].Samples);
    }
    [Fact] public void SubMillisecondDeviceJitterIsTrimmedButMateriallyLateAudioFails()
    {
        var windows = new List<TranscriptAudioWindow>();
        var timeline = new TranscriptTimeline(windows.Add, 8000);
        timeline.Advance(8000);
        timeline.Add(7997, new float[] { 1, 2, 3, 4 });
        timeline.Finish(8001);
        Assert.Equal(new float[] { 4 }, windows[1].Samples);
        var late = new TranscriptTimeline(_ => { }, 8000);
        late.Advance(8000);
        Assert.Throws<InvalidOperationException>(() => late.Add(7983, new float[20]));
    }
    private static TranscriptAudioWindow Window(int index, float value)
        => new(index, Enumerable.Repeat(value, 8000).ToArray(), index * 8000L);
    private sealed class PausedCapture : ITranscriptCapture
    {
        public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task RunAsync(TranscriptSource source, TranscriptClock clock, Action<TranscriptAudioWindow> onWindow, Action<float> onLevel, CancellationToken stop)
        {
            for (int i = 0; i < 3; i++) onWindow(Window(i, 1));
            Ready.SetResult();
            try { await Task.Delay(Timeout.Infinite, stop); } catch (OperationCanceledException) { }
        }
    }
    private sealed class OverlapCapture : ITranscriptCapture
    {
        public Task RunAsync(TranscriptSource source, TranscriptClock clock, Action<TranscriptAudioWindow> onWindow, Action<float> onLevel, CancellationToken stop)
        {
            for (int i = 0; i < 12; i++)
                onWindow(Window(i, (source.Slot == 1 ? i < 8 : i >= 2 && i < 4) ? source.Slot : 0));
            return Task.CompletedTask;
        }
    }
    private sealed class Recognizer : ITranscriptRecognizer
    {
        public int Calls { get; private set; }
        public Task<IReadOnlyList<SpeechSegment>> TranscribeSegmentsAsync(float[] samples, string language, CancellationToken token, bool translateToEnglish = false)
            { Calls++; return Task.FromResult<IReadOnlyList<SpeechSegment>>([new(TimeSpan.Zero, TimeSpan.FromSeconds(samples.Length / 16000d), $"source-{samples.First(s => s != 0)}")]); }
    }
    public void Dispose()
    {
        string full = Path.GetFullPath(root);
        if (!full.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("VoicePlay-live-tests-", StringComparison.Ordinal))
            throw new InvalidOperationException("Unexpected test directory.");
        if (Directory.Exists(full)) Directory.Delete(full, true);
    }
}