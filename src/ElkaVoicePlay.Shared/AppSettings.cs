namespace ElkaVoicePlay.Shared;

public sealed record AppSettings
{
    public int SchemaVersion { get; init; } = 1;
    public string? MicrophoneId { get; init; }
    public string ModelPath { get; init; } = Path.Combine(DataPaths.Models, "ggml-base.bin");
    public string Language { get; init; } = "auto";
    public int HotkeyVirtualKey { get; init; } = 0x78;
    public bool SuppressHotkey { get; init; } = true;
    public int MaximumRecordingSeconds { get; init; } = 30;
    public int MaximumMessageLength { get; init; } = 500;
    public bool CloseToTray { get; init; } = true;
    public GameProfile[]? Profiles { get; init; }
    public string SelectedProfileId { get; init; } = "notepad";
    public bool AutomaticProfiles { get; init; } = true;

    public AppSettings Validate()
    {
        if (SchemaVersion > 2) throw new InvalidDataException("These settings were saved by a newer version. They have not been overwritten.");
        var profiles = Profiles ?? [GameProfile.Notepad(Math.Clamp(HotkeyVirtualKey, 0x70, 0x87), SuppressHotkey), GameProfile.WarDogs()];
        if (profiles.Length == 0 || profiles.Any(p => p == null)) throw new InvalidDataException("At least one valid profile is required.");
        profiles = profiles.Select(p => p.Validate()).ToArray();
        if (profiles.Select(p => p.Id).Distinct().Count() != profiles.Length) throw new InvalidDataException("Profile IDs must be unique.");
        return this with
        {
            SchemaVersion = 2,
            Profiles = profiles,
            SelectedProfileId = profiles.Any(p => p.Id == SelectedProfileId) ? SelectedProfileId : profiles[0].Id,
            ModelPath = string.IsNullOrWhiteSpace(ModelPath) ? Path.Combine(DataPaths.Models, "ggml-base.bin") : ModelPath,
            Language = string.IsNullOrWhiteSpace(Language) ? "auto" : Language.Trim().ToLowerInvariant(),
            HotkeyVirtualKey = Math.Clamp(HotkeyVirtualKey, 0x70, 0x87),
            MaximumRecordingSeconds = Math.Clamp(MaximumRecordingSeconds, 1, 120),
            MaximumMessageLength = Math.Clamp(MaximumMessageLength, 1, 4000)
        };
    }
}

public static class DataPaths
{
    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ElkaSoft", "ElkaVoicePlay", "Saves");
    public static string LocalRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ElkaSoft", "ElkaVoicePlay");
    public static string Models => Path.Combine(LocalRoot, "Models");
    public static string Transcripts => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ElkaSoft", "ElkaVoicePlay", "Transcripts");
    public static string Settings => Path.Combine(Root, "settings.json");
}
