using System.Text.Json.Nodes;
using ElkaVoicePlay.Core;
using Xunit;

namespace ElkaVoicePlay.Tests;
public sealed class ElkaStorageMigrationTests : IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"VoicePlayStorage-"+Guid.NewGuid().ToString("N"));
    private string P(string part)=>Path.GetFullPath(Path.Combine(root,part));
    private void Write(string part,JsonObject value) { string path=P(part);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path,value.ToJsonString()); }
    [Fact] public void ImportsNewestProfileAndDefaultPathsAndCopiesTranscriptsWithoutRemovingOriginals()
    {
        Write("new-name/settings.json",new() { ["ModelPath"]=P("old-name/Models/ggml-base.bin"),["SelectedProfileId"]="war-dogs",["FutureField"]="preserve" });
        Write("old-name/settings.json",new() { ["SelectedProfileId"]="older-profile" });
        Write("new-name/transcription-settings.json",new() { ["Folder"]=P("old-name/Transcripts") });
        Directory.CreateDirectory(P("old-name/Transcripts/session"));File.WriteAllText(P("old-name/Transcripts/session/combined.txt"),"fixture transcript");
        string original=File.ReadAllText(P("new-name/settings.json"));
        ElkaStorageMigration.Import([P("new-name"),P("old-name")],P("ElkaSoft/Saves"),P("ElkaSoft/Models"),P("ElkaSoft/Transcripts"));
        var imported=JsonNode.Parse(File.ReadAllText(P("ElkaSoft/Saves/settings.json")))!;
        Assert.Equal("war-dogs",imported["SelectedProfileId"]!.GetValue<string>());
        Assert.Equal("preserve",imported["FutureField"]!.GetValue<string>());
        Assert.Equal(P("ElkaSoft/Models/ggml-base.bin"),imported["ModelPath"]!.GetValue<string>());
        Assert.Equal(original,File.ReadAllText(P("new-name/settings.json")));
        Assert.Equal(original,File.ReadAllText(P("ElkaSoft/Saves/settings.json.imported-original.bak")));
        Assert.Equal("fixture transcript",File.ReadAllText(P("ElkaSoft/Transcripts/session/combined.txt")));
        Assert.True(File.Exists(P("old-name/Transcripts/session/combined.txt")));
    }
    [Fact] public void ExistingElkaSoftSettingsAndCustomPathsArePreserved()
    {
        Write("legacy/settings.json",new() { ["ModelPath"]=P("custom/ggml-medium.bin") });
        Write("legacy/transcription-settings.json",new() { ["Folder"]=P("custom/output") });
        ElkaStorageMigration.Import([P("legacy")],P("ElkaSoft/Saves"),P("ElkaSoft/Models"),P("ElkaSoft/Transcripts"));
        string imported=File.ReadAllText(P("ElkaSoft/Saves/settings.json"));
        Assert.Contains("medium",imported);
        Assert.Equal(P("custom/output"),JsonNode.Parse(File.ReadAllText(P("ElkaSoft/Saves/transcription-settings.json")))!["Folder"]!.GetValue<string>());
        Write("legacy/settings.json",new() { ["ModelPath"]="changed" });
        ElkaStorageMigration.Import([P("legacy")],P("ElkaSoft/Saves"),P("ElkaSoft/Models"),P("ElkaSoft/Transcripts"));
        Assert.Equal(imported,File.ReadAllText(P("ElkaSoft/Saves/settings.json")));
    }
    [Fact] public void InvalidLegacyJsonDoesNotCreateNewSettingsOrReplaceOriginal()
    {
        Directory.CreateDirectory(P("legacy"));File.WriteAllText(P("legacy/settings.json"),"invalid");
        Assert.ThrowsAny<System.Text.Json.JsonException>(()=>ElkaStorageMigration.Import([P("legacy")],P("ElkaSoft/Saves"),P("ElkaSoft/Models"),P("ElkaSoft/Transcripts")));
        Assert.False(File.Exists(P("ElkaSoft/Saves/settings.json")));
        Assert.Equal("invalid",File.ReadAllText(P("legacy/settings.json")));
    }
    public void Dispose() { if(Directory.Exists(root))Directory.Delete(root,true); }
}
