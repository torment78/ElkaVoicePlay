using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using ElkaVoicePlay.Core;
using ElkaVoicePlay.Shared;
using Xunit;

namespace ElkaVoicePlay.Tests;

public sealed class ComponentInstallerTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "VoicePlayTests-" + Guid.NewGuid().ToString("N"));
    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data));
    private static OptionalComponent Parts(params byte[][] data) => new("fixture", "Fixture", "Test", "1", "parts",
        data.Select((d, i) => new ComponentAsset($"fixture-{i}.part", d.Length, Hash(d))).ToArray(),
        [new("Models/model.bin", data.Sum(d => d.Length), Hash(data.SelectMany(d => d).ToArray()))]);
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Calls++; cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(respond(request)); }
    }
    private static HttpResponseMessage Response(byte[] data) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(data) };
    [Fact] public async Task SplitModelInstallsExactlyAndSecondInstallDoesNotDownload()
    {
        byte[][] data = [[1, 2, 3], [4, 5]]; var component = Parts(data);
        var handler = new Handler(r => Response(data[component.Assets.ToList().FindIndex(a => r.RequestUri!.AbsolutePath.EndsWith(a.Name))]));
        using var client = new HttpClient(handler); var installer = new ComponentInstaller(client, root);
        await installer.InstallAsync(component, null, default);
        Assert.Equal(new byte[] { 1,2,3,4,5 }, File.ReadAllBytes(Path.Combine(OptionalComponents.DirectoryFor(component, root), "Models/model.bin")));
        Assert.True(OptionalComponents.IsInstalled(component, root));
        await installer.InstallAsync(component, null, default); Assert.Equal(2, handler.Calls);
    }
    [Fact] public async Task BadChecksumCannotBecomeAnInstalledComponent()
    {
        using var client = new HttpClient(new Handler(_ => Response([9, 9, 9]))); var component = Parts([1,2,3]);
        await Assert.ThrowsAsync<InvalidDataException>(() => new ComponentInstaller(client, root).InstallAsync(component, null, default));
        Assert.False(OptionalComponents.IsInstalled(component, root));
        Assert.Empty(Directory.GetDirectories(root, ".install-*"));
        Assert.Empty(Directory.GetFiles(root, "*.partial", SearchOption.AllDirectories));
    }
    [Fact] public async Task TruncatedDownloadDoesNotReplaceExistingFiles()
    {
        var component = Parts([1,2,3]); string destination = OptionalComponents.DirectoryFor(component, root); Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(destination, "old.txt"), "preserved");
        using var client = new HttpClient(new Handler(_ => Response([1,2])));
        await Assert.ThrowsAsync<InvalidDataException>(() => new ComponentInstaller(client, root).InstallAsync(component, null, default));
        Assert.Equal("preserved", File.ReadAllText(Path.Combine(destination, "old.txt")));
    }
    [Fact] public async Task InterruptedSecondPartReusesTheVerifiedFirstPart()
    {
        var component = Parts([1,2,3], [4,5]); bool fail = true; int first = 0;
        using var client = new HttpClient(new Handler(r =>
        {
            if (r.RequestUri!.AbsolutePath.EndsWith(component.Assets[0].Name)) { first++; return Response([1,2,3]); }
            return fail ? new(HttpStatusCode.ServiceUnavailable) : Response([4,5]);
        }));
        var installer = new ComponentInstaller(client, root);
        await Assert.ThrowsAsync<HttpRequestException>(() => installer.InstallAsync(component, null, default));
        fail = false; await installer.InstallAsync(component, null, default);
        Assert.Equal(1, first); Assert.True(OptionalComponents.IsInstalled(component, root));
    }
    [Theory]
    [InlineData("../outside.dll")]
    [InlineData("folder/../../outside.dll")]
    [InlineData("C:/outside.dll")]
    [InlineData("folder\\outside.dll")]
    [InlineData("file.dll:stream")]
    public void UnsafeExtractionPathsAreRejected(string path) => Assert.Throws<InvalidDataException>(() => ComponentInstaller.SafePath(root, path));
    [Fact] public async Task ArchiveWithUnexpectedEntryIsRejected()
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, true))
        using (var writer = new StreamWriter(archive.CreateEntry("../outside.dll").Open())) writer.Write("bad");
        byte[] zip = memory.ToArray();
        var component = new OptionalComponent("fixture", "Fixture", "", "1", "zip", [new("test.zip",zip.Length,Hash(zip))], [new("safe.dll",3,Hash(Encoding.UTF8.GetBytes("bad")))]);
        using var client = new HttpClient(new Handler(_ => Response(zip)));
        await Assert.ThrowsAsync<InvalidDataException>(() => new ComponentInstaller(client, root).InstallAsync(component, null, default));
        Assert.False(OptionalComponents.IsInstalled(component, root));
    }
    [Fact] public async Task CancelledInstallationLeavesNoPartialModel()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        using var client = new HttpClient(new Handler(_ => Response([1,2,3]))); var component = Parts([1,2,3]);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ComponentInstaller(client, root).InstallAsync(component, null, cancellation.Token));
        Assert.False(OptionalComponents.IsInstalled(component, root)); Assert.Empty(Directory.GetDirectories(root, ".install-*"));
    }
    [Fact] public void SettingsMigrationPreservesValuesAndNeverOverwritesNewSettings()
    {
        string old = Path.Combine(root,"old"), current = Path.Combine(root,"new"); Directory.CreateDirectory(old);
        File.WriteAllText(Path.Combine(old,"settings.json"), "old profiles");
        LegacySettingsMigration.Import(old,current);
        Assert.Equal("old profiles",File.ReadAllText(Path.Combine(current,"settings.json")));
        File.WriteAllText(Path.Combine(current,"settings.json"),"new profiles");
        LegacySettingsMigration.Import(old,current);
        Assert.Equal("new profiles",File.ReadAllText(Path.Combine(current,"settings.json")));
        Assert.Equal("old profiles",File.ReadAllText(Path.Combine(old,"settings.json")));
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
