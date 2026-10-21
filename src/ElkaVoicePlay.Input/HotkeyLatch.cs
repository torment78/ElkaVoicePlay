namespace ElkaVoicePlay.Input;

/// <summary>Tracks physical edges; auto-repeat never creates a second recording.</summary>
public sealed class HotkeyLatch
{
    public bool IsHeld { get; private set; }
    public bool Update(bool down)
    {
        if (down == IsHeld) return false;
        IsHeld = down;
        return true;
    }
}
