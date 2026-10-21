using System.Text.Json;
using System.Text.Json.Nodes;

namespace ElkaVoicePlay.Core;

/// <summary>Import legacy defaults into the ElkaSoft family layout without deleting originals.</summary>
public static class ElkaStorageMigration
{
    public static void Import(IReadOnlyList<string> legacyRoots, string saves, string models, string transcripts)
    {
        foreach (string name in new[] { "settings.json", "transcription-settings.json" })
        {
            string destination = Path.Combine(saves, name);
            if (File.Exists(destination)) continue;
            string? source = legacyRoots.Select(r => Path.Combine(r, name)).FirstOrDefault(File.Exists);
            if (source == null) continue;
            string original = File.ReadAllText(source);
            var settings = JsonNode.Parse(original) as JsonObject ?? throw new InvalidDataException("Previous settings are not a JSON object.");
            if (name == "settings.json" && settings["ModelPath"]?.GetValue<string>() is string model &&
                legacyRoots.Any(r => SamePath(model, Path.Combine(r, "Models", BundledModelSetup.FileName))))
                settings["ModelPath"] = Path.Combine(models, BundledModelSetup.FileName);
            if (name == "transcription-settings.json" && settings["Folder"]?.GetValue<string>() is string folder &&
                legacyRoots.Any(r => SamePath(folder, Path.Combine(r, "Transcripts"))))
            {
                CopyTreeIfAbsent(folder, transcripts);
                settings["Folder"] = transcripts;
            }
            Directory.CreateDirectory(saves);
            // Keep the exact original alongside the imported settings as well as in its old folder.
            if (!File.Exists(destination + ".imported-original.bak")) File.Copy(source, destination + ".imported-original.bak");
            string temporary = destination + ".import-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temporary, settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                File.Move(temporary, destination, false);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
    private static bool SamePath(string a, string b)
        => Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar).Equals(Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
    private static void CopyTreeIfAbsent(string source, string destination)
    {
        if (!Directory.Exists(source) || Directory.Exists(destination)) return;
        string parent = Path.GetDirectoryName(Path.GetFullPath(destination))!;
        Directory.CreateDirectory(parent);
        string temporary = Path.Combine(parent, ".transcripts-import-" + Guid.NewGuid().ToString("N"));
        try
        {
            CopyDirectory(source, temporary);
            Directory.Move(temporary, destination);
        }
        finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
    }
    private static void CopyDirectory(string source, string destination)
    {
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0) throw new IOException("A linked transcript folder needs to be selected manually; original files are unchanged.");
        Directory.CreateDirectory(destination);
        foreach (var entry in new DirectoryInfo(source).EnumerateFileSystemInfos())
        {
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("A linked transcript file needs to be selected manually; original files are unchanged.");
            string target = Path.Combine(destination, entry.Name);
            if (entry is DirectoryInfo) CopyDirectory(entry.FullName, target); else File.Copy(entry.FullName, target, false);
        }
    }
}
