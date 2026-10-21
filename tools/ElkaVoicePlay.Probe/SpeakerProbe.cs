using System.Diagnostics;
using System.Text.Json;
using ElkaVoicePlay.Core;
using ElkaVoicePlay.Shared;
using ElkaVoicePlay.Speech;
using NAudio.Wave;

internal static class SpeakerProbe
{
    internal static async Task<int> RunAsync(string models, string whisperModel, string wav, string output)
    {
        using var reader = new WaveFileReader(wav);
        if (reader.WaveFormat.SampleRate != 16000 || reader.WaveFormat.Channels != 1) throw new InvalidOperationException("Use 16-kHz mono WAV.");
        var provider = reader.ToSampleProvider(); var buffer = new float[4096]; var samples = new List<float>(); int count;
        while ((count = provider.Read(buffer, 0, buffer.Length)) > 0) samples.AddRange(buffer.AsSpan(0, count).ToArray());
        using var diarizer = new SpeakerDiarizer(models);
        var timer = Stopwatch.StartNew();
        var first = await diarizer.AnalyzeAsync(1, samples.ToArray(), default);
        var repeat = await diarizer.AnalyzeAsync(1, samples.ToArray(), default);
        if (!first.Select(t => t.Label).SequenceEqual(repeat.Select(t => t.Label))) throw new InvalidOperationException("Repeated fixture speaker labels changed.");
        if (first.Select(t => t.Label).Where(l => l.StartsWith("Speaker ")).Distinct().Count() != 4) throw new InvalidOperationException("Multi-speaker fixture did not separate voices.");
        Console.WriteLine(JsonSerializer.Serialize(new { Phase = "Full fixture speaker turns", Seconds = timer.Elapsed.TotalSeconds, Turns = first }, new JsonSerializerOptions { WriteIndented = true }));
        await using var speech = new WhisperRecognizer(); await speech.LoadAsync(whisperModel);
        var settings = new TranscriptionSettings { Folder = Path.GetFullPath(output), Language = "zh", TranslateToEnglish = true, LabelSpeakers = true,
            Sources = Enumerable.Range(1, 3).Select(i => new TranscriptSource { Slot = i, Enabled = i == 1, Name = "Chinese fixture", DeviceId = "fixture" }).ToArray() };
        using var sessionDiarizer = new SpeakerDiarizer(models);
        var session = new TranscriptionSession(new FixtureCapture(samples.ToArray()), speech, sessionDiarizer);
        var lines = new List<TranscriptLine>(); session.LinesWritten += batch => lines.AddRange(batch);
        timer.Restart();
        var result = await session.RunAsync(settings, default);
        if (result.Error != null || lines.Count == 0 || lines.Any(l => l.Speaker == null)) throw new InvalidOperationException("Translated speaker transcript failed: " + result.Error);
        var identities = lines.Select(l => l.Speaker).Where(l => l != null).Distinct().Order().ToArray();
        if (!identities.SequenceEqual(new[] { "Speaker 1", "Speaker 2", "Speaker 3", "Speaker 4" }) ||
            lines[^1].Speaker != lines[0].Speaker || lines.Any(l => l.Text.Any(c => c >= '\u4e00' && c <= '\u9fff')))
            throw new InvalidOperationException("Expected four stable anonymous labels and English output.");
        Console.WriteLine(JsonSerializer.Serialize(new { Phase = "Completed phrases with translation and speakers", Seconds = timer.Elapsed.TotalSeconds, result, Text = lines.Select(TranscriptFiles.Format) }, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
    private sealed class FixtureCapture(float[] samples) : ITranscriptCapture
    {
        public Task RunAsync(TranscriptSource source, TranscriptClock clock, Action<TranscriptAudioWindow> onWindow, Action<float> onLevel, CancellationToken stop)
        {
            var timeline = new TranscriptTimeline(onWindow, 8000);
            timeline.Add(0, samples); timeline.Finish(samples.Length);
            return Task.CompletedTask;
        }
    }
}
