using System.Text.Json;
using ElkaVoicePlay.Shared;

namespace ElkaVoicePlay.Core;

public sealed class SettingsStore(string path)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly SemaphoreSlim saveGate = new(1, 1);
    public async Task<AppSettings> LoadAsync()
    {
        if (!File.Exists(path)) return new AppSettings().Validate();
        await using var stream = File.OpenRead(path);
        var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream) ?? throw new InvalidDataException("Settings file is empty.");
        return settings.Validate();
    }

    public async Task SaveAsync(AppSettings settings)
    {
        var validated = settings.Validate();
        await saveGate.WaitAsync();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(validated, JsonOptions));
            // Preserve the first pre-profile settings before the schema migration replaces it.
            if (File.Exists(path) && !File.Exists(path + ".before-profiles.bak")) File.Copy(path, path + ".before-profiles.bak");
            File.Move(temporary, path, true);
        }
        finally { saveGate.Release(); }
    }
}
