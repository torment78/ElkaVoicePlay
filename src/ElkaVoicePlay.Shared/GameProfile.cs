namespace ElkaVoicePlay.Shared;

public enum InputKind { Keyboard, Mouse }
public enum MouseControl { Left = 1, Right, Middle, X1, X2 }
public enum TextDelivery { Unicode, NotepadEditor }

public sealed record InputBinding(InputKind Kind, int Code)
{
    public static InputBinding F9 => new(InputKind.Keyboard, 0x78);
    public static InputBinding Enter => new(InputKind.Keyboard, 0x0D);
    public bool IsValid => Kind == InputKind.Keyboard ? Code is >= 8 and <= 254
        : Kind == InputKind.Mouse && Code is >= 1 and <= 5;
    public bool Matches(InputBinding other) => this == other || (Kind == InputKind.Keyboard && other.Kind == InputKind.Keyboard &&
        (Code == 0x10 && other.Code is 0xA0 or 0xA1 || Code == 0x11 && other.Code is 0xA2 or 0xA3 || Code == 0x12 && other.Code is 0xA4 or 0xA5));
    public override string ToString() => Kind == InputKind.Mouse ? (MouseControl)Code switch
    {
        MouseControl.Left => "Mouse left", MouseControl.Right => "Mouse right", MouseControl.Middle => "Mouse middle",
        MouseControl.X1 => "Mouse side 1", MouseControl.X2 => "Mouse side 2", _ => "Unknown mouse button"
    } : Code switch
    {
        0x08 => "Backspace", 0x09 => "Tab", 0x0D => "Enter", 0x10 => "Shift", 0x11 => "Ctrl", 0x12 => "Alt",
        0x1B => "Escape", 0x20 => "Space", 0x21 => "Page Up", 0x22 => "Page Down", 0x23 => "End", 0x24 => "Home",
        0x25 => "Left", 0x26 => "Up", 0x27 => "Right", 0x28 => "Down", 0x2D => "Insert", 0x2E => "Delete",
        0x5B => "Left Windows", 0x5C => "Right Windows", 0xA0 => "Left Shift", 0xA1 => "Right Shift",
        0xA2 => "Left Ctrl", 0xA3 => "Right Ctrl", 0xA4 => "Left Alt", 0xA5 => "Right Alt",
        >= 0x70 and <= 0x87 => $"F{Code - 0x6F}", >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A => ((char)Code).ToString(),
        >= 0x60 and <= 0x69 => $"Numpad {Code - 0x60}", _ => $"Key 0x{Code:X2}"
    };
}

public sealed record GameProfile
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "New game";
    public string Executable { get; init; } = "";
    public bool AutoActivate { get; init; }
    public InputBinding Trigger { get; init; } = InputBinding.F9;
    public bool SuppressTrigger { get; init; } = true;
    public TextDelivery Delivery { get; init; } = TextDelivery.Unicode;
    public InputBinding? OpenChat { get; init; } = InputBinding.Enter;
    public int OpenDelayMs { get; init; } = 250;
    public int CharacterDelayMs { get; init; } = 10;
    public bool SendAutomatically { get; init; } = true;
    public bool BlockKeyboardWhileSending { get; init; }
    public InputBinding Send { get; init; } = InputBinding.Enter;
    public int BeforeSendDelayMs { get; init; } = 150;
    public InputBinding? CloseChat { get; init; }
    public int BeforeCloseDelayMs { get; init; } = 150;
    public int PressDurationMs { get; init; } = 40;
    public ChatCommandSettings ChatCommand { get; init; } = new();

    public GameProfile Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(Name)) throw new InvalidDataException("Every profile needs a name and ID.");
        if (Trigger?.IsValid != true || Send?.IsValid != true || OpenChat?.IsValid == false || CloseChat?.IsValid == false)
            throw new InvalidDataException("A profile has an invalid keyboard or mouse binding.");
        if (!Enum.IsDefined(Delivery)) throw new InvalidDataException("Unknown text insertion method.");
        if (AutoActivate && string.IsNullOrWhiteSpace(Executable)) throw new InvalidDataException($"Choose an EXE for automatic profile '{Name}'.");
        var command = (ChatCommand ?? throw new InvalidDataException("Invalid chat command settings.")).Validate();
        if (command.Enabled && command.UseSeparateButton && (Trigger.Matches(command.Button) || command.Button.Matches(Trigger)))
            throw new InvalidDataException("Choose a separate command button different from the talk button.");
        return this with { ChatCommand = command, Name = Name.Trim(), Executable = Executable.Trim(), OpenDelayMs = Math.Clamp(OpenDelayMs, 0, 10000),
            CharacterDelayMs = Math.Clamp(CharacterDelayMs, 0, 100), BeforeSendDelayMs = Math.Clamp(BeforeSendDelayMs, 0, 10000),
            BeforeCloseDelayMs = Math.Clamp(BeforeCloseDelayMs, 0, 10000), PressDurationMs = Math.Clamp(PressDurationMs, 1, 1000) };
    }
    public override string ToString() => Name;
    public static GameProfile Notepad(int legacyKey = 0x78, bool suppress = true) => new()
    {
        Id = "notepad", Name = "Notepad", Executable = "notepad.exe", AutoActivate = true,
        Trigger = new(InputKind.Keyboard, legacyKey), SuppressTrigger = suppress, Delivery = TextDelivery.NotepadEditor,
        OpenChat = null, SendAutomatically = false
    };
    public static GameProfile WarDogs() => new() { Id = "war-dogs", Name = "War Dogs" };
}

public static class ProfileSelector
{
    public static bool Matches(GameProfile profile, string executableName, string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(profile.Executable)) return false;
        return Path.IsPathFullyQualified(profile.Executable)
            ? string.Equals(profile.Executable, executablePath, StringComparison.OrdinalIgnoreCase)
            : string.Equals(Path.GetFileNameWithoutExtension(profile.Executable), Path.GetFileNameWithoutExtension(executableName), StringComparison.OrdinalIgnoreCase);
    }

    public static GameProfile? Select(AppSettings settings, string executableName, string? executablePath)
    {
        if (settings.AutomaticProfiles)
        {
            // Exact paths take precedence over name-only matches. Only the foreground process is considered.
            var matches = settings.Profiles!.Where(p => p.AutoActivate && Matches(p, executableName, executablePath)).ToArray();
            var automatic = matches.FirstOrDefault(p => Path.IsPathFullyQualified(p.Executable)) ?? matches.FirstOrDefault();
            if (automatic != null) return automatic;
        }
        return settings.Profiles!.FirstOrDefault(p => p.Id == settings.SelectedProfileId);
    }
}
