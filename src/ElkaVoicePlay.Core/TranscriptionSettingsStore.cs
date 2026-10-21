using System.Text.Json;
using ElkaVoicePlay.Shared;

namespace ElkaVoicePlay.Core;

public sealed class TranscriptionSettingsStore(string path)
{
    public async Task<TranscriptionSettings> LoadAsync()
    {
        if (!File.Exists(path)) return new TranscriptionSettings();
        await using var stream = File.OpenRead(path);
        return (await JsonSerializer.DeserializeAsync<TranscriptionSettings>(stream)
            ?? throw new InvalidDataException("Transcription settings are empty.")).Validate(false);
    }
    public async Task SaveAsync(TranscriptionSettings settings)
    {
        settings = settings.Validate(false);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
