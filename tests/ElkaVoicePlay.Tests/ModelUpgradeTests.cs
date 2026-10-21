using System.Text.Json;
using ElkaVoicePlay.Core;
using ElkaVoicePlay.Shared;
using Xunit;

namespace ElkaVoicePlay.Tests;

public class ModelUpgradeTests
{
    [Fact] public void OldSettingsReceiveIndependentModelDefaults()
    {
        var settings = JsonSerializer.Deserialize<TranscriptionSettings>("{\"Version\":1,\"Language\":\"de\"}")!;
        Assert.Equal(TranscriptModel.Base, settings.TranscriptModel); Assert.Equal(SpeakerModel.Classic, settings.SpeakerModel);
        Assert.Equal("de", settings.Language); Assert.False(settings.LabelSpeakers);
    }
    [Fact] public void MediumDoesNotChangeExistingLargeModelSettings()
    {
        var settings = JsonSerializer.Deserialize<TranscriptionSettings>("{\"TranscriptModel\":1}")!;
        Assert.Equal(TranscriptModel.LargeV3, settings.TranscriptModel);
        Assert.Equal(2, (int)TranscriptModel.Medium);
    }
    [Fact] public void WordAttributionPreservesTextAcrossTinyOverlapAndSpeakerChange()
    {
        SpeechSegment Word(double a, double b, string text) => new(TimeSpan.FromSeconds(a), TimeSpan.FromSeconds(b), text);
        SpeakerTurn Turn(double a, double b, string label) => new(TimeSpan.FromSeconds(a), TimeSpan.FromSeconds(b), label);
        var lines = TranscriptWordLines.Build([Word(0,0.5," Hello"), Word(0.5,1," there."), Word(1,1.5," Yes.")],
            [Turn(0,0.9,"Speaker 1"), Turn(0.9,1,"Overlapping speakers"), Turn(1,2,"Speaker 2")], 2, "Video", TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(2));
        Assert.Equal(new[] { "Hello there.", "Yes." }, lines.Select(l => l.Text));
        Assert.Equal(new[] { "Speaker 1", "Speaker 2" }, lines.Select(l => l.Speaker));
        Assert.Equal(TimeSpan.FromSeconds(11), lines[1].Start);
    }
    [Fact] public void CleanEvidenceKeepsBothSidesOfAnOverlap()
    {
        var voice = new SpeakerTurn(TimeSpan.Zero, TimeSpan.FromSeconds(5), "", 0);
        var other = new SpeakerTurn(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), "", 1);
        Assert.Equal(new[] { (0, 32000), (48000, 80000) }, SpeakerEvidence.CleanSamples(voice, [voice, other], 80000));
    }
    [Fact] public void NestedOverlapsDoNotDuplicateOrDiscardCleanSamples()
    {
        var voice = new SpeakerTurn(TimeSpan.Zero, TimeSpan.FromSeconds(5), "", 0);
        SpeakerTurn Other(double a, double b) => new(TimeSpan.FromSeconds(a), TimeSpan.FromSeconds(b), "", 1);
        Assert.Equal(new[] { (0, 16000), (64000, 80000) }, SpeakerEvidence.CleanSamples(voice, [Other(1, 4), Other(2, 3)], 80000));
    }
    [Fact] public void ShortNewTurnCanReuseStrongHistoricalEvidence()
    {
        var prior = new SpeakerTurn(TimeSpan.Zero, TimeSpan.FromSeconds(3), "Speaker 2", 7);
        var current = new SpeakerTurn(TimeSpan.Zero, TimeSpan.FromSeconds(3.4), "", 0);
        Assert.Equal("Speaker 2", SpeakerEvidence.ContinuingLabel([current], [prior]));
    }
    [Fact] public void ConflictingHistoryIsNotForcedIntoOneSpeaker()
    {
        var a = new SpeakerTurn(TimeSpan.Zero, TimeSpan.FromSeconds(2), "Speaker 1");
        var b = new SpeakerTurn(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), "Speaker 2");
        Assert.Null(SpeakerEvidence.ContinuingLabel([a with { End = b.End }], [a, b]));
    }
    [Fact] public async Task WorkerProtocolRoundTripsAudioExactly()
    {
        using var stream = new MemoryStream(); float[] original = [-0.75f, 0, 0.123456f, 1];
        await ModelProtocol.WriteJsonAsync(stream, new ModelRequest("speakers", 2), default);
        await ModelProtocol.WriteAudioAsync(stream, original, default); stream.Position = 0;
        Assert.Equal(2, (await ModelProtocol.ReadJsonAsync<ModelRequest>(stream, default)).Slot);
        Assert.Equal(original, await ModelProtocol.ReadAudioAsync(stream, default));
    }
    [Fact] public async Task WorkerRejectsOversizedOrTruncatedMessages()
    {
        using var invalid = new MemoryStream(BitConverter.GetBytes(int.MaxValue));
        await Assert.ThrowsAsync<InvalidDataException>(() => ModelProtocol.ReadAudioAsync(invalid, default));
        using var truncated = new MemoryStream([4,0,0,0,1,2]);
        await Assert.ThrowsAsync<EndOfStreamException>(() => ModelProtocol.ReadAudioAsync(truncated, default));
    }
}
