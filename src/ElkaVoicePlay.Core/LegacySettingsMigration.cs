namespace ElkaVoicePlay.Core;

/// <summary>One-time import of settings from the former product name; original data stays intact.</summary>
public static class LegacySettingsMigration
{
    public static void Import(string legacyRoot, string newRoot)
    {
        if (!Directory.Exists(legacyRoot)) return;
        Directory.CreateDirectory(newRoot);
        foreach (string name in new[] { "settings.json", "transcription-settings.json" })
        {
            string source = Path.Combine(legacyRoot, name), target = Path.Combine(newRoot, name);
            if (!File.Exists(source) || File.Exists(target)) continue;
            string temporary = target + ".import-" + Guid.NewGuid().ToString("N");
            try
            {
                File.Copy(source, temporary);
                File.Move(temporary, target, false);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
