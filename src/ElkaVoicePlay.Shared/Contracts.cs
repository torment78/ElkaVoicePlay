namespace ElkaVoicePlay.Shared;

public enum SessionState { Idle, Recording, Processing, Validating, OpeningChat, InsertingText, SendingMessage, ClosingChat }
public sealed record SessionStatus(SessionState State, string Message, string? Text = null, bool IsError = false);
public sealed record WindowTarget(nint Handle, int ProcessId, long ProcessStartTicks, string Name, long FocusRevision, nint EditorHandle = default, GameProfile? Profile = null);

public interface IChatInput
{
    bool IsCurrent(WindowTarget target);
    void EnsureModifiersReleased();
    Task<IKeyboardSendGuard> BlockKeyboardAsync(CancellationToken token) => throw new NotSupportedException("Keyboard blocking is unavailable for this input method.");
    Task TapAsync(WindowTarget target, InputBinding binding, int durationMs, CancellationToken token);
    Task TypeAsync(WindowTarget target, string text, int delayMs, CancellationToken token);
}
public sealed record CaptureRequest(string? DeviceId, int MaximumSeconds);
public sealed record MicrophoneInfo(string? Id, string Name)
{
    public override string ToString() => Name;
}

public interface IAudioRecorder
{
    event Action<float>? LevelChanged;
    Task<float[]> CaptureAsync(CaptureRequest request, Task released, CancellationToken cancellationToken);
}

public interface ISpeechRecognizer
{
    bool IsReady { get; }
    Task<string> TranscribeAsync(float[] samples, string language, CancellationToken cancellationToken);
}

public interface ITextTarget
{
    WindowTarget? Capture();
    bool IsCurrent(WindowTarget target);
    Task InsertAsync(WindowTarget target, string text, CancellationToken cancellationToken);
}

public sealed class RecordingLimitException : Exception
{
    public RecordingLimitException() : base("Recording limit reached. Nothing was inserted; release the hotkey and try a shorter message.") { }
}

public interface IKeyboardSendGuard : IAsyncDisposable
{
    CancellationToken Token { get; }
    bool TimedOut { get; }
}
