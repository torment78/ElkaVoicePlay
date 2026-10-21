using System.Security.Cryptography;
using ElkaVoicePlay.Core;
using Xunit;

namespace ElkaVoicePlay.Tests;

public sealed class BundledModelTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "VoicePlay-model-test-" + Guid.NewGuid().ToString("N"));
    private static readonly byte[] Sample = "small deterministic model fixture"u8.ToArray();
    private static readonly string Hash = Convert.ToHexString(SHA256.HashData(Sample));
    private string Destination => Path.Combine(root, "Other user's Documents", "Models", BundledModelSetup.FileName);
    private async Task<string> Source(byte[]? bytes = null)
    {
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "source.bin");
        await File.WriteAllBytesAsync(path, bytes ?? Sample);
        return path;
    }
    [Fact] public async Task InstallsVerifiedFileIntoNewUserDirectory()
    {
        string source = await Source();
        await BundledModelSetup.CopyVerifiedAsync(source, Destination, Sample.Length, Hash);
        Assert.Equal(Sample, await File.ReadAllBytesAsync(Destination));
        Assert.Equal(Sample, await File.ReadAllBytesAsync(source));
        Assert.Empty(Directory.GetFiles(root, "*.partial", SearchOption.AllDirectories));
    }
    [Fact] public async Task ExistingModelIsNeverOverwritten()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Destination)!);
        await File.WriteAllTextAsync(Destination, "existing model");
        await BundledModelSetup.CopyVerifiedAsync("missing source", Destination, Sample.Length, Hash);
        Assert.Equal("existing model", await File.ReadAllTextAsync(Destination));
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task IncompleteOrDamagedBundleLeavesNoInstalledOrPartialFile(bool truncated)
    {
        byte[] damaged = truncated ? Sample[..^1] : Enumerable.Repeat((byte)0, Sample.Length).ToArray();
        string source = await Source(damaged);
        await Assert.ThrowsAsync<InvalidDataException>(() => BundledModelSetup.CopyVerifiedAsync(source, Destination, Sample.Length, Hash));
        Assert.False(File.Exists(Destination));
        Assert.Empty(Directory.GetFiles(root, "*.partial", SearchOption.AllDirectories));
    }
    [Fact] public async Task CancellationDoesNotInstallAModel()
    {
        string source = await Source(); using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => BundledModelSetup.CopyVerifiedAsync(source, Destination, Sample.Length, Hash, cts.Token));
        Assert.False(File.Exists(Destination));
        Assert.Empty(Directory.GetFiles(root, "*.partial", SearchOption.AllDirectories));
    }
    [Fact] public async Task MissingPathFromAnotherAccountUsesThisUsersExistingBaseModel()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Destination)!);
        await File.WriteAllBytesAsync(Destination, Sample);
        string actual = await BundledModelSetup.ResolveAsync(Path.Combine(root, "old account", BundledModelSetup.FileName),
            Path.GetDirectoryName(Destination)!, Path.Combine(root, "app"));
        Assert.Equal(Destination, actual);
    }
    [Fact] public async Task ExistingCustomModelIsKept()
    {
        string custom = await Source();
        Assert.Equal(custom, await BundledModelSetup.ResolveAsync(custom, Path.GetDirectoryName(Destination)!, root));
        Assert.False(File.Exists(Destination));
    }
    [Fact] public async Task MissingCustomModelDoesNotSilentlySelectBase()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Destination)!);
        await File.WriteAllBytesAsync(Destination, Sample);
        await Assert.ThrowsAsync<FileNotFoundException>(() => BundledModelSetup.ResolveAsync(Path.Combine(root, "ggml-small.bin"),
            Path.GetDirectoryName(Destination)!, root));
    }
    [Fact] public async Task MissingBundledModelExplainsCompleteFolderRequirement()
    {
        var error = await Assert.ThrowsAsync<FileNotFoundException>(() => BundledModelSetup.ResolveAsync(Destination,
            Path.GetDirectoryName(Destination)!, Path.Combine(root, "app")));
        Assert.Contains("complete VoicePlay package", error.Message);
        Assert.False(File.Exists(Destination));
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
