namespace ElkaVoicePlay.Shared;

public sealed record ChatCommandSettings
{
    public bool Enabled { get; init; }
    public bool UseSeparateButton { get; init; }
    public InputBinding Button { get; init; } = new(InputKind.Keyboard, 0x79);
    public bool SuppressButton { get; init; } = true;
    public int DoubleTapWindowMs { get; init; } = 300;
    public InputBinding? First { get; init; } = InputBinding.Enter;
    public int FirstDelayMs { get; init; } = 150;
    public InputBinding? Second { get; init; } = new(InputKind.Keyboard, 0x09);
    public int SecondDelayMs { get; init; } = 150;
    public InputBinding? Third { get; init; } = InputBinding.Enter;
    public int PressDurationMs { get; init; } = 40;

    public ChatCommandSettings Validate()
    {
        if (Button?.IsValid != true || First?.IsValid == false || Second?.IsValid == false || Third?.IsValid == false)
            throw new InvalidDataException("The chat command has an invalid button.");
        if (Enabled && First == null && Second == null && Third == null)
            throw new InvalidDataException("Choose at least one key or mouse action for the chat command.");
        return this with { DoubleTapWindowMs = Math.Clamp(DoubleTapWindowMs, 100, 1000),
            FirstDelayMs = Math.Clamp(FirstDelayMs, 0, 10000), SecondDelayMs = Math.Clamp(SecondDelayMs, 0, 10000),
            PressDurationMs = Math.Clamp(PressDurationMs, 1, 1000) };
    }

    public bool Accepts(GameProfile profile, ChatCommandRequest request) => Enabled && UseSeparateButton == request.SeparateButton
        && (UseSeparateButton ? Button : profile.Trigger).Matches(request.Button)
        && (UseSeparateButton || request.ElapsedMs >= 0 && request.ElapsedMs < DoubleTapWindowMs);
}

public sealed record ChatCommandRequest(InputBinding Button, bool SeparateButton, int ElapsedMs = 0);
