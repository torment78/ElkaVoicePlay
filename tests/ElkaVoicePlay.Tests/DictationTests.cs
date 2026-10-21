using ElkaVoicePlay.Core;
using ElkaVoicePlay.Input;
using ElkaVoicePlay.Shared;
using Xunit;

namespace ElkaVoicePlay.Tests;

public sealed class DictationTests
{
    [Fact] public async Task RecordingWaitsForReleaseAndInsertsExactlyOnce()
    {
        var f = new Fixture();
        Assert.True(f.Controller.Begin(new()));
        await f.Audio.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(f.Controller.Begin(new()));
        Assert.Equal(0, f.Speech.Calls);
        f.Controller.Release(); f.Controller.Release();
        await f.Finish();
        Assert.Equal(new[] { "Hello there" }, f.Target.Inserted);
        Assert.Equal(1, f.Audio.Calls);
        Assert.Equal(1, f.Speech.Calls);
        Assert.False(f.Controller.IsBusy);
    }

    [Theory] [InlineData("")] [InlineData(" \r\n\t ")] [InlineData("\u0000\u0001")]
    public async Task EmptyOrControlOnlyResultNeverInserts(string result)
    {
        var f = new Fixture(); f.Speech.Result = result;
        f.Controller.Begin(new()); f.Controller.Release(); await f.Finish();
        Assert.Empty(f.Target.Inserted); Assert.True(f.Last!.IsError);
    }

    [Fact] public async Task TargetThatBecomesInvalidAtDeliveryRejectsOutput()
    {
        var f = new Fixture(); f.Speech.Wait = NewSignal();
        f.Controller.Begin(new()); f.Controller.Release();
        await f.Speech.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        f.Target.Current = false; f.Speech.Wait.SetResult(); await f.Finish();
        Assert.Empty(f.Target.Inserted); Assert.Equal("Hello there", f.Last!.Text);
    }

    [Fact] public async Task MissingDestinationIsCheckedAfterTranscriptionAndKeepsTheText()
    {
        var f = new Fixture(); f.Target.Available = false;
        Assert.True(f.Controller.Begin(new()));
        await f.Audio.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, f.Target.CaptureCalls);
        f.Controller.Release(); await f.Finish();
        Assert.Equal(1, f.Speech.Calls); Assert.Equal(1, f.Target.CaptureCalls);
        Assert.Empty(f.Target.Inserted); Assert.True(f.Last!.IsError); Assert.Equal("Hello there", f.Last.Text);
        Assert.Contains("No valid destination", f.Last.Message);
    }

    [Fact] public async Task NoForegroundInspectionDuringRecordingOrRecognition()
    {
        var f = new Fixture(); f.Speech.Wait = NewSignal();
        Assert.True(f.Controller.Begin(new()));
        await f.Audio.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, f.Target.CaptureCalls);
        f.Controller.Release();
        await f.Speech.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, f.Target.CaptureCalls);
        f.Speech.Wait.SetResult(); await f.Finish();
        Assert.Equal(1, f.Target.CaptureCalls); Assert.Single(f.Target.Inserted);
    }

    [Fact] public async Task DestinationCanBecomeAvailableWhileRecognizing()
    {
        var f = new Fixture(); f.Target.Available = false; f.Speech.Wait = NewSignal();
        f.Controller.Begin(new()); f.Controller.Release();
        await f.Speech.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        f.Target.Available = true; f.Speech.Wait.SetResult(); await f.Finish();
        Assert.Single(f.Target.Inserted); Assert.False(f.Last!.IsError);
    }

    [Fact] public void MissingModelDoesNotOpenMicrophone()
    {
        var f = new Fixture(); f.Speech.IsReady = false;
        Assert.False(f.Controller.Begin(new())); Assert.Equal(0, f.Audio.Calls);
    }

    [Fact] public async Task CancelDuringRecordingReleasesAudioAndSkipsRecognition()
    {
        var f = new Fixture(); f.Controller.Begin(new());
        await f.Audio.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        f.Controller.Cancel(); await f.Finish();
        Assert.True(f.Audio.Released); Assert.Equal(0, f.Speech.Calls); Assert.Empty(f.Target.Inserted);
    }

    [Fact] public async Task CancelDuringRecognitionNeverInsertsEvenIfBackendReturnsText()
    {
        var f = new Fixture(); f.Speech.Wait = NewSignal();
        f.Controller.Begin(new()); f.Controller.Release();
        await f.Speech.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(f.Controller.Begin(new()));
        f.Controller.Cancel(); f.Speech.Wait.SetResult(); await f.Finish();
        Assert.Empty(f.Target.Inserted); Assert.False(f.Controller.IsBusy); Assert.Equal(0, f.Target.CaptureCalls);
    }

    [Fact] public async Task OverlongTextIsRetainedButNeverTruncatedOrInserted()
    {
        var f = new Fixture(); f.Controller.Begin(new() { MaximumMessageLength = 3 });
        f.Controller.Release(); await f.Finish();
        Assert.Empty(f.Target.Inserted); Assert.Equal("Hello there", f.Last!.Text); Assert.True(f.Last.IsError); Assert.Equal(0, f.Target.CaptureCalls);
    }

    [Fact] public async Task CaptureFailureReturnsToIdleAndNextPressWorks()
    {
        var f = new Fixture(); f.Audio.Failure = new InvalidOperationException("Disconnected");
        f.Controller.Begin(new()); f.Controller.Release(); await f.Finish();
        Assert.True(f.Last!.IsError); Assert.True(f.Audio.Released); Assert.False(f.Controller.IsBusy);
        f.Audio.Failure = null;
        Assert.True(f.Controller.Begin(new())); f.Controller.Release(); await f.Finish();
        Assert.Single(f.Target.Inserted);
    }

    [Fact] public async Task RecordingLimitNeverProcessesPartialAudio()
    {
        var f = new Fixture(); f.Audio.Failure = new RecordingLimitException();
        f.Controller.Begin(new()); f.Controller.Release(); await f.Finish();
        Assert.Equal(0, f.Speech.Calls); Assert.Empty(f.Target.Inserted); Assert.True(f.Last!.IsError);
    }

    [Fact] public async Task RecognitionFailureDoesNotInsertAndReleasesSession()
    {
        var f = new Fixture(); f.Speech.Failure = new InvalidOperationException("Backend error");
        f.Controller.Begin(new()); f.Controller.Release(); await f.Finish();
        Assert.Empty(f.Target.Inserted); Assert.False(f.Controller.IsBusy); Assert.True(f.Last!.IsError);
    }

    [Fact] public async Task SilenceSkipsRecognition()
    {
        var f = new Fixture(); f.Audio.Samples = new float[16000];
        f.Controller.Begin(new()); f.Controller.Release(); await f.Finish();
        Assert.Equal(0, f.Speech.Calls); Assert.Empty(f.Target.Inserted);
    }

    [Fact] public async Task PreviewHasNoTargetAndNeverInserts()
    {
        var f = new Fixture(); f.Target.Available = false;
        Assert.True(f.Controller.Begin(new(), true)); f.Controller.Release(); await f.Finish();
        Assert.Empty(f.Target.Inserted); Assert.Equal("Hello there", f.Last!.Text); Assert.False(f.Last.IsError); Assert.Equal(0, f.Target.CaptureCalls);
    }

    [Fact] public async Task NormalizationCannotInjectNewlinesOrTabs()
    {
        var f = new Fixture(); f.Speech.Result = " hi\r\nthere\tworld\0 ";
        f.Controller.Begin(new()); f.Controller.Release(); await f.Finish();
        Assert.Equal("hi there world", Assert.Single(f.Target.Inserted));
    }

    [Fact] public void KeyboardRepeatRequiresARealRelease()
    {
        var latch = new HotkeyLatch();
        Assert.True(latch.Update(true));
        for (int i = 0; i < 100; i++) Assert.False(latch.Update(true));
        Assert.True(latch.Update(false)); Assert.False(latch.Update(false)); Assert.True(latch.Update(true));
    }

    [Fact] public void SettingsClampUnsafeLimitsAndRejectFutureSchema()
    {
        var valid = new AppSettings { MaximumRecordingSeconds = int.MaxValue, MaximumMessageLength = -1 }.Validate();
        Assert.Equal(120, valid.MaximumRecordingSeconds); Assert.Equal(1, valid.MaximumMessageLength);
        Assert.Throws<InvalidDataException>(() => new AppSettings { SchemaVersion = 99 }.Validate());
    }

    [Fact] public async Task CorruptSettingsArePreserved()
    {
        string path = Path.Combine(Path.GetTempPath(), "ElkaVoicePlay-test-" + Guid.NewGuid() + ".json");
        try
        {
            await File.WriteAllTextAsync(path, "broken");
            await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => new SettingsStore(path).LoadAsync());
            Assert.Equal("broken", await File.ReadAllTextAsync(path));
        }
        finally { File.Delete(path); }
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private sealed class Fixture
    {
        internal FakeAudio Audio { get; } = new();
        internal FakeSpeech Speech { get; } = new();
        internal FakeTarget Target { get; } = new();
        internal DictationController Controller { get; }
        internal SessionStatus? Last;
        internal Fixture() { Controller = new(Audio, Speech, Target); Controller.StatusChanged += s => Last = s; }
        internal Task Finish() => Controller.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }
    private sealed class FakeAudio : IAudioRecorder
    {
        public event Action<float>? LevelChanged { add { } remove { } }
        internal TaskCompletionSource Started = NewSignal();
        internal int Calls;
        internal bool Released;
        internal Exception? Failure;
        internal float[] Samples = Enumerable.Repeat(0.1f, 16000).ToArray();
        public async Task<float[]> CaptureAsync(CaptureRequest request, Task released, CancellationToken token)
        {
            Calls++; Started.TrySetResult();
            try { await released.WaitAsync(token); if (Failure != null) throw Failure; return Samples; }
            finally { Released = true; }
        }
    }
    private sealed class FakeSpeech : ISpeechRecognizer
    {
        public bool IsReady { get; set; } = true;
        internal TaskCompletionSource Started = NewSignal();
        internal TaskCompletionSource? Wait;
        internal int Calls;
        internal string Result = "Hello there";
        internal Exception? Failure;
        public async Task<string> TranscribeAsync(float[] samples, string language, CancellationToken token)
        {
            Calls++; Started.TrySetResult();
            if (Wait != null) await Wait.Task;
            if (Failure != null) throw Failure;
            return Result;
        }
    }
    private sealed class FakeTarget : ITextTarget
    {
        internal bool Available = true, Current = true;
        internal int CaptureCalls;
        internal List<string> Inserted = [];
        public WindowTarget? Capture() { CaptureCalls++; return Available ? new(123, 456, 789, "notepad", 1) : null; }
        public bool IsCurrent(WindowTarget target) => Current;
        public Task InsertAsync(WindowTarget target, string text, CancellationToken token) { token.ThrowIfCancellationRequested(); Inserted.Add(text); return Task.CompletedTask; }
    }
}
