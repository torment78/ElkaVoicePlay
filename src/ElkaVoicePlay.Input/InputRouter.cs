using ElkaVoicePlay.Shared;

namespace ElkaVoicePlay.Input;

/// <summary>Physical edges only. Repeats and releases retain the decision made on key-down.</summary>
internal sealed class InputRouter
{
    private InputBinding? held, learned;
    private bool suppressHeld, cancelLearning;
    private readonly HashSet<InputBinding> physicalDown = [];
    public bool IsHeld => held != null;
    public bool IsMouseHeld => held?.Kind == InputKind.Mouse;
    public bool IsLearning { get; private set; }
    public event Action<bool>? HoldChanged;
    public event Action<InputBinding?>? Learned;
    public void SeedPressed(InputBinding input) => physicalDown.Add(input);
    public void ClearPressed(InputKind kind) => physicalDown.RemoveWhere(input => input.Kind == kind);
    public void BeginLearning()
    {
        if (IsHeld || IsLearning) throw new InvalidOperationException("Release the current button first.");
        IsLearning = true; cancelLearning = false;
    }
    public void CancelLearning()
    {
        if (!IsLearning) return;
        cancelLearning = true;
        if (learned == null) FinishLearning(null);
    }
    private void FinishLearning(InputBinding? result) { IsLearning = false; learned = null; Learned?.Invoke(result); }
    public bool Process(InputBinding input, bool down, InputBinding trigger, bool canStart, bool suppress)
    {
        bool firstDown = down && physicalDown.Add(input);
        if (!down) physicalDown.Remove(input);
        if (IsLearning)
        {
            if (learned == null && firstDown) learned = input;
            if (input == learned)
            {
                if (!down) FinishLearning(cancelLearning || input == new InputBinding(InputKind.Keyboard, 0x1B) ? null : input);
                return true;
            }
            return false;
        }
        if (held == null && trigger.Matches(input) && firstDown && canStart)
        {
            held = input; suppressHeld = suppress; HoldChanged?.Invoke(true);
            return suppressHeld;
        }
        if (held != input) return false;
        bool consume = suppressHeld;
        if (!down) { held = null; suppressHeld = false; HoldChanged?.Invoke(false); }
        return consume;
    }
}
