using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using ElkaVoicePlay.Shared;

namespace ElkaVoicePlay.Core;

public sealed record ComponentProgress(string Stage, long Completed, long Total);

/// <summary>Downloads a release-pinned package, verifies it, then atomically makes it available.</summary>
public sealed class ComponentInstaller(HttpClient client, string root)
{
    public async Task InstallAsync(OptionalComponent component, IProgress<ComponentProgress>? progress, CancellationToken token)
    {
        Directory.CreateDirectory(root);
        // One installer per data folder, including across application instances.
        await using var installLock = new FileStream(Path.Combine(root, ".install.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (OptionalComponents.IsInstalled(component, root)) return;
        ValidateManifest(component);
        string cache = Path.Combine(root, ".downloads"); Directory.CreateDirectory(cache);
        string staging = Path.Combine(root, ".install-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(staging);
        string destination = OptionalComponents.DirectoryFor(component, root);
        string backup = destination + ".previous-" + Guid.NewGuid().ToString("N");
        var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root))!);
        try
        {
            if (drive.AvailableFreeSpace < component.DownloadSize + component.InstalledSize + 64L * 1024 * 1024)
                throw new IOException($"Not enough free space. Allow {OptionalComponents.SizeLabel(component.DownloadSize + component.InstalledSize)} for download and installation.");
            long downloaded = 0;
            foreach (var asset in component.Assets)
            {
                string path = Path.Combine(cache, asset.Sha256 + ".asset");
                if (!await MatchesAsync(path, asset.Size, asset.Sha256, token))
                {
                    string partial = path + ".partial";
                    try
                    {
                        using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(token); requestTimeout.CancelAfter(TimeSpan.FromMinutes(2));
                        using var response = await client.GetAsync(OptionalComponents.DownloadUri(asset), HttpCompletionOption.ResponseHeadersRead, requestTimeout.Token);
                        response.EnsureSuccessStatusCode();
                        if (response.Content.Headers.ContentLength is long length && length != asset.Size) throw new InvalidDataException("Download size does not match the release catalog.");
                        await using (var input = await response.Content.ReadAsStreamAsync(token))
                        await using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 131072, true))
                        {
                            await CopyBoundedAsync(input, output, asset.Size, token,
                                bytes => progress?.Report(new("Downloading " + component.Name, downloaded + bytes, component.DownloadSize)));
                        }
                        progress?.Report(new("Verifying download", downloaded, component.DownloadSize));
                        if (!await MatchesAsync(partial, asset.Size, asset.Sha256, token)) throw new InvalidDataException("Download checksum mismatch. Please retry.");
                        File.Move(partial, path, true);
                    }
                    finally { TryDeleteFile(partial); }
                }
                downloaded += asset.Size; progress?.Report(new("Download verified", downloaded, component.DownloadSize));
            }
            progress?.Report(new("Installing " + component.Name, 0, component.InstalledSize));
            if (component.Format == "parts")
            {
                string path = SafePath(staging, component.Files.Single().Path); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true);
                long copied = 0;
                foreach (var asset in component.Assets)
                {
                    await using var input = File.OpenRead(Path.Combine(cache, asset.Sha256 + ".asset"));
                    await CopyBoundedAsync(input, output, asset.Size, token, n => progress?.Report(new("Installing " + component.Name, copied + n, component.InstalledSize)));
                    copied += asset.Size;
                }
            }
            else
            {
                using var archive = ZipFile.OpenRead(Path.Combine(cache, component.Assets.Single().Sha256 + ".asset"));
                if (archive.Entries.Count != component.Files.Length) throw new InvalidDataException("Unexpected files in component archive.");
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long copied = 0;
                foreach (var entry in archive.Entries)
                {
                    string path = SafePath(staging, entry.FullName);
                    var expected = component.Files.SingleOrDefault(f => f.Path == entry.FullName);
                    if (expected == null || !seen.Add(entry.FullName) || entry.Length != expected.Size || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                        throw new InvalidDataException("Unexpected or invalid component file.");
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    await using var input = entry.Open();
                    await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true);
                    await CopyBoundedAsync(input, output, expected.Size, token, n => progress?.Report(new("Installing " + component.Name, copied + n, component.InstalledSize)));
                    copied += expected.Size;
                }
            }
            foreach (var file in component.Files)
            {
                token.ThrowIfCancellationRequested(); progress?.Report(new("Checking installed files", component.InstalledSize, component.InstalledSize));
                if (!await MatchesAsync(SafePath(staging, file.Path), file.Size, file.Sha256, token)) throw new InvalidDataException("Installed file checksum mismatch. Please retry.");
            }
            await File.WriteAllTextAsync(Path.Combine(staging, ".complete"), string.Join("\n", component.Assets.Select(a => a.Sha256)), token);
            token.ThrowIfCancellationRequested();
            if (Directory.Exists(destination)) Directory.Move(destination, backup);
            try { Directory.Move(staging, destination); }
            catch { if (Directory.Exists(backup)) Directory.Move(backup, destination); throw; }
            TryDeleteDirectory(backup);
            foreach (var asset in component.Assets) TryDeleteFile(Path.Combine(cache, asset.Sha256 + ".asset"));
            progress?.Report(new("Installed " + component.Name, component.InstalledSize, component.InstalledSize));
        }
        finally { TryDeleteDirectory(staging); }
    }

    internal static string SafePath(string root, string relative)
    {
        if (string.IsNullOrEmpty(relative) || relative.Contains('\\') || relative.Contains(':') || relative.Split('/').Any(p => p is "" or "." or ".." || p.EndsWith('.') || p.EndsWith(' ')) || Path.IsPathRooted(relative))
            throw new InvalidDataException("Unsafe component path.");
        string full = Path.GetFullPath(Path.Combine(root, relative));
        if (!full.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Component path escapes installation folder.");
        return full;
    }
    private static void ValidateManifest(OptionalComponent component)
    {
        if (component.Id.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-') || component.Version.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '.' and not '-')) throw new InvalidDataException("Invalid component identity.");
        if (component.Format is not "zip" and not "parts" || component.Assets.Length == 0 || component.Files.Length == 0 ||
            component.Format == "zip" && component.Assets.Length != 1 || component.Format == "parts" && (component.Files.Length != 1 || component.DownloadSize != component.InstalledSize)) throw new InvalidDataException("Invalid component format.");
        if (component.Files.Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != component.Files.Length) throw new InvalidDataException("Duplicate component files.");
        foreach (var file in component.Files) { SafePath(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar), file.Path); ValidateHash(file.Size, file.Sha256); }
        foreach (var asset in component.Assets) { if (asset.Name != Path.GetFileName(asset.Name) || asset.Name.Contains(':')) throw new InvalidDataException("Invalid asset name."); ValidateHash(asset.Size, asset.Sha256); }
    }
    private static void ValidateHash(long size, string hash) { if (size < 0 || hash.Length != 64 || hash.Any(c => !Uri.IsHexDigit(c))) throw new InvalidDataException("Invalid file checksum."); }
    private static async Task<bool> MatchesAsync(string path, long size, string hash, CancellationToken token)
    {
        if (!File.Exists(path) || new FileInfo(path).Length != size) return false;
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token)).Equals(hash, StringComparison.OrdinalIgnoreCase);
    }
    private static async Task CopyBoundedAsync(Stream input, Stream output, long length, CancellationToken token, Action<long> report)
    {
        byte[] buffer = new byte[131072]; long copied = 0; var clock = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            using var idle = CancellationTokenSource.CreateLinkedTokenSource(token); idle.CancelAfter(TimeSpan.FromMinutes(2));
            int count = await input.ReadAsync(buffer, idle.Token);
            if (count == 0) break;
            copied += count; if (copied > length) throw new InvalidDataException("Component is larger than expected.");
            await output.WriteAsync(buffer.AsMemory(0, count), token);
            if (clock.ElapsedMilliseconds >= 100) { report(copied); clock.Restart(); }
        }
        if (copied != length) throw new InvalidDataException("Download was interrupted or is incomplete. Please retry.");
        report(copied);
    }
    private static void TryDeleteFile(string path) { try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    private static void TryDeleteDirectory(string path) { try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
}
