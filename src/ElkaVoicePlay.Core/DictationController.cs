using System.Text.RegularExpressions;
using ElkaVoicePlay.Shared;

namespace ElkaVoicePlay.Core;

/// <summary>One capture, one recognition and at most one insertion for each physical press.</summary>
public sealed partial class DictationController(IAudioRecorder recorder, ISpeechRecognizer speech, ITextTarget target)
{
    private readonly object gate = new();
    private CancellationTokenSource? cancellation;
    private TaskCompletionSource? release;
    private Task completion = Task.CompletedTask;
    public event Action<SessionStatus>? StatusChanged;
    public bool IsBusy { get { lock (gate) return cancellation != null; } }
    public Task Completion { get { lock (gate) return completion; } }

    public bool Begin(AppSettings settings, bool previewOnly = false)
    {
        lock (gate)
        {
            if (cancellation != null) return false;
            if (!speech.IsReady) { Report(SessionState.Idle, "Load a Whisper model first.", error: true); return false; }
            cancellation = new CancellationTokenSource();
            release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var token = cancellation.Token;
            var released = release.Task;
            completion = Task.Run(() => RunAsync(settings.Validate(), previewOnly, released, token));
            return true;
        }
    }

    public void Release() { lock (gate) release?.TrySetResult(); }
    public void Cancel() { lock (gate) { cancellation?.Cancel(); release?.TrySetResult(); } }

    private async Task RunAsync(AppSettings settings, bool previewOnly, Task released, CancellationToken token)
    {
        SessionStatus final = new(SessionState.Idle, "Ready");
        try
        {
            token.ThrowIfCancellationRequested();
            Report(SessionState.Recording, "Recording — release to transcribe");
            var samples = await recorder.CaptureAsync(new(settings.MicrophoneId, settings.MaximumRecordingSeconds), released, token);
            token.ThrowIfCancellationRequested();
            if (samples.Length < 1600 || !samples.Any(s => Math.Abs(s) >= 0.001f))
                throw new InvalidOperationException("No usable microphone audio. Check the input device and level, then try again.");
            Report(SessionState.Processing, "Transcribing locally…");
            var text = Normalize(await speech.TranscribeAsync(samples, settings.Language, token));
            token.ThrowIfCancellationRequested();
            Report(SessionState.Validating, "Checking transcription and target", text);
            if (text.Length == 0) throw new InvalidOperationException("No speech recognized. Nothing was inserted.");
            if (text.Length > settings.MaximumMessageLength)
            {
                final = new(SessionState.Idle, $"Message has {text.Length} characters; the limit is {settings.MaximumMessageLength}. Nothing was inserted.", text, true);
                return;
            }
            // Resolve the destination once, only when there is valid text ready to deliver.
            // Recording and recognition never inspect the foreground app.
            var destination = previewOnly ? null : target.Capture();
            if (!previewOnly && destination == null)
            {
                final = new(SessionState.Idle, "No valid destination for this profile. Nothing was sent; your text is kept below.", text, true);
                return;
            }
            if (destination != null)
            {
                if (!target.IsCurrent(destination))
                {
                    final = new(SessionState.Idle, "Focus changed or the target closed. Nothing was inserted.", text, true);
                    return;
                }
                Report(SessionState.InsertingText, $"Delivering to {destination.Profile?.Name ?? destination.Name}…", text);
                await target.InsertAsync(destination, text, token);
            }
            final = new(SessionState.Idle, destination == null ? "Preview complete — nothing inserted" :
                destination.Profile is { Delivery: TextDelivery.Unicode, SendAutomatically: true } ? "Chat sequence completed — verify the message in game" : "Text inserted", text);
        }
        catch (OperationCanceledException) { final = new(SessionState.Idle, "Cancelled"); }
        catch (Exception ex) { final = new(SessionState.Idle, ex.Message, IsError: true); }
        finally
        {
            lock (gate)
            {
                cancellation?.Dispose();
                cancellation = null;
                release = null;
            }
            StatusChanged?.Invoke(final);
        }
    }

    private void Report(SessionState state, string message, string? text = null, bool error = false) =>
        StatusChanged?.Invoke(new(state, message, text, error));

    // Line breaks and controls must never become chat-submit keystrokes.
    public static string Normalize(string text) => Whitespace().Replace(new string(text.Where(c => !char.IsControl(c) || char.IsWhiteSpace(c)).ToArray()), " ").Trim();
    [GeneratedRegex(@"\s+")] private static partial Regex Whitespace();
}
