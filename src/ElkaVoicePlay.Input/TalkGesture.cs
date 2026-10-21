using ElkaVoicePlay.Shared;

namespace ElkaVoicePlay.Input;

/// <summary>Clock-driven gesture state. Called only on the input thread; no process or UI work.</summary>
internal sealed class TalkGesture
{
    private TalkTrigger? control;
    private InputBinding? button;
    private long started;
    private bool down, second, recording;
    public long? DueAt { get; private set; }
    public bool IsRecording => recording;
    public event Action<bool>? HoldChanged;
    public event Action<ChatCommandRequest>? Command;

    public void Press(TalkTrigger trigger, InputBinding physicalButton, long now)
    {
        Advance(now);
        if (down) return;
        if (control == trigger && button == physicalButton && DueAt != null)
        { down = true; second = true; return; }
        Reset();
        control = trigger; button = physicalButton; down = true; started = now;
        if (trigger.CommandOnly) return;
        if (trigger.DoubleTapWindowMs > 0) DueAt = now + trigger.DoubleTapWindowMs;
        else StartRecording();
    }

    public void Release(long now)
    {
        if (!down || control == null || button == null) return;
        Advance(now);
        down = false;
        if (recording) { Reset(); HoldChanged?.Invoke(false); }
        else if (control.CommandOnly)
        {
            var request = new ChatCommandRequest(button, true);
            Reset(); Command?.Invoke(request);
        }
        else if (second)
        {
            var request = new ChatCommandRequest(button, false, checked((int)(now - started)));
            Reset(); Command?.Invoke(request);
        }
        // First short release waits for the second tap until the same deadline.
    }

    public void Advance(long now)
    {
        if (DueAt is not { } deadline || now < deadline) return;
        DueAt = null;
        if (down) StartRecording();
        else Reset();
    }

    public void CancelPending()
    {
        // A real recording still needs its physical release; its owner handles cancellation.
        if (!recording) Reset();
    }
    private void StartRecording() { DueAt = null; recording = true; HoldChanged?.Invoke(true); }
    private void Reset() { control = null; button = null; down = second = recording = false; DueAt = null; }
}
