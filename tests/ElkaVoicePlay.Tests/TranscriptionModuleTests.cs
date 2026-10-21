using ElkaVoicePlay.Shared;
using Xunit;

namespace ElkaVoicePlay.Tests;
public sealed class TranscriptionModuleTests
{
    [Fact] public void MissingModuleGivesActionableDownloadMessage()
    {
        string absent=Path.Combine(Path.GetTempPath(),"VoicePlayMissing-"+Guid.NewGuid().ToString("N"));
        Assert.False(TranscriptionModule.IsInstalled(absent));
        Assert.Contains("click Download",Assert.Throws<InvalidOperationException>(()=>TranscriptionModule.RequireWorker(absent)).Message);
    }
    [Fact] public void ModuleCatalogContainsWorkerAndSpeakersWithoutDuplicatingBaseOrGpu()
    {
        var module=OptionalComponents.Get(TranscriptionModule.Id);
        Assert.Single(module.Assets);
        Assert.Contains(module.Files,f=>f.Path=="Workers/ElkaVoicePlay.ModelWorker.exe");
        Assert.Contains(module.Files,f=>f.Path=="Models/Speakers/embedding.onnx");
        Assert.Contains(module.Files,f=>f.Path=="Models/Speakers/segmentation.onnx");
        Assert.DoesNotContain(module.Files,f=>f.Path.Contains("ggml-base.bin") || f.Path.Contains("cublas") || f.Path.Contains("cudart"));
        Assert.All(module.Files,f=>Assert.Equal(64,f.Sha256.Length));
    }
    [Fact] public void ExistingModelDownloadsStayPinnedWhileTheNewModuleUsesTheNewRelease()
    {
        Assert.Contains("v0.6.0-preview.1",OptionalComponents.DownloadUri(OptionalComponents.Get("medium").Assets[0]).AbsoluteUri);
        Assert.Contains("v0.7.0-preview.1",OptionalComponents.DownloadUri(OptionalComponents.Get(TranscriptionModule.Id).Assets[0]).AbsoluteUri);
    }
}
