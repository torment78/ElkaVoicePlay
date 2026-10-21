using System.Text.Json;

namespace ElkaVoicePlay.Shared;

public sealed record ComponentAsset(string Name, long Size, string Sha256);
public sealed record ComponentFile(string Path, long Size, string Sha256);
public sealed record OptionalComponent(string Id, string Name, string Description, string Version, string Format,
    ComponentAsset[] Assets, ComponentFile[] Files)
{
    public long DownloadSize => Assets.Sum(a => a.Size);
    public long InstalledSize => Files.Sum(a => a.Size);
}

public static class OptionalComponents
{
    public const string Repository = "https://github.com/torment78/ElkaVoicePlay";
    public const string ReleaseTag = "v0.6.0-preview.1";
    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ElkaSoft", "ElkaVoicePlay", "Components");
    public static IReadOnlyList<OptionalComponent> All { get; } = Load();
    private static OptionalComponent[] Load()
    {
        using var stream = typeof(OptionalComponents).Assembly.GetManifestResourceStream("ElkaVoicePlay.Shared.optional-components.json")
            ?? throw new InvalidOperationException("The download catalog is missing. Extract the complete application.");
        return JsonSerializer.Deserialize<OptionalComponent[]>(stream)!;
    }
    public static OptionalComponent Get(string id) => All.Single(c => c.Id == id);
    public static string DirectoryFor(OptionalComponent component, string? root = null) => Path.Combine(root ?? Root, component.Id + "-" + component.Version);
    public static string FileFor(string id, string path, string? root = null) => Path.Combine(DirectoryFor(Get(id), root), path);
    public static Uri DownloadUri(ComponentAsset asset) => new($"{Repository}/releases/download/{ReleaseTag}/{Uri.EscapeDataString(asset.Name)}");
    public static bool IsInstalled(string id, string? root = null) => IsInstalled(Get(id), root);
    public static bool IsInstalled(OptionalComponent component, string? root = null)
    {
        try
        {
            string directory = DirectoryFor(component, root);
            if (File.ReadAllText(Path.Combine(directory, ".complete")) != string.Join("\n", component.Assets.Select(a => a.Sha256))) return false;
            return component.Files.All(f => new FileInfo(Path.Combine(directory, f.Path)) is { Exists: true } file && file.Length == f.Size);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
    public static string SizeLabel(long bytes) => bytes >= 1_000_000_000 ? $"{bytes / 1_000_000_000d:0.00} GB" : $"{bytes / 1_000_000d:0.0} MB";
}
