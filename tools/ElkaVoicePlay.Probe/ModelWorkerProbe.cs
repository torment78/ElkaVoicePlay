using System.Diagnostics;
using System.Text.Json;
using ElkaVoicePlay.Shared;
using ElkaVoicePlay.Core;
using ElkaVoicePlay.Speech;
using NAudio.Wave;

internal static class ModelWorkerProbe
{
    public static async Task<int> RecoveryAsync(string executable, string model)
    {
        await using (var worker = new ModelWorkerClient())
        {
            await worker.StartAsync("whisper", model, ModelAcceleration.Cpu, default, executable);
            using var process = Process.GetProcessById(worker.ProcessId!.Value); process.Kill(); await process.WaitForExitAsync();
            try { await worker.TranscribeSegmentsAsync(new float[16000], "en", default); throw new InvalidDataException("Expected a worker failure"); }
            catch (InvalidOperationException ex) when (ex.Message.Contains("model worker")) { Console.WriteLine("Worker termination surfaced as a recoverable error."); }
        }
        await using var replacement = new ModelWorkerClient();
        await replacement.StartAsync("whisper", model, ModelAcceleration.Cpu, default, executable);
        Console.WriteLine("A replacement worker loaded successfully."); return 0;
    }
    public static async Task<int> SessionAsync(string executable, string models, string wav, string output, string device)
    {
        using var reader = new WaveFileReader(wav); var provider = reader.ToSampleProvider();
        float[] buffer = new float[4096]; var samples = new List<float>(); int n;
        while ((n = provider.Read(buffer, 0, buffer.Length)) > 0) samples.AddRange(buffer.AsSpan(0, n).ToArray());
        await using var speech = new ModelWorkerClient(); await using var speakers = new ModelWorkerClient();
        var acceleration = Enum.Parse<ModelAcceleration>(device);
        await speech.StartAsync("whisper", Path.Combine(models, "ggml-large-v3.bin"), acceleration, default, executable);
        await speakers.StartAsync("nemotron", Path.Combine(models, "Nemotron-3-Diarization.q8_0.gguf"), acceleration, default, executable);
        Console.WriteLine($"Transcriber: {speech.Backend}; speakers: {speakers.Backend}");
        var settings = new TranscriptionSettings { Folder = Path.GetFullPath(output), Language = "zh", TranslateToEnglish = true, LabelSpeakers = true,
            Sources = Enumerable.Range(1,3).Select(i => new TranscriptSource { Slot=i, Name="Input " + i, Enabled=i==1, DeviceId="fixture" }).ToArray() };
        var session = new TranscriptionSession(new Fixture(samples.ToArray()), speech, speakers);
        var lines = new List<TranscriptLine>(); session.LinesWritten += batch => lines.AddRange(batch);
        var clock = Stopwatch.StartNew(); var result = await session.RunAsync(settings, default);
        Console.WriteLine(JsonSerializer.Serialize(new { result, Seconds=clock.Elapsed.TotalSeconds, Lines=lines }, new JsonSerializerOptions { WriteIndented=true }));
        Console.WriteLine(speech.Diagnostics); Console.WriteLine(speakers.Diagnostics);
        if (result.Error != null || lines.Count == 0 || lines.Any(l => l.Text.Any(c => c >= '\u4e00' && c <= '\u9fff')))
            throw new InvalidOperationException("Combined GPU transcription/translation failed.");
        if (lines.Select(l => l.Speaker).Where(l => l?.StartsWith("Speaker ") == true).Distinct().Count() != 4)
            throw new InvalidOperationException("Expected four anonymous speaker labels.");
        if (!lines.Select(l => l.Start).SequenceEqual(lines.Select(l => l.Start).Order())) throw new InvalidOperationException("Lines out of order");
        return 0;
    }
    private sealed class Fixture(float[] samples) : ITranscriptCapture
    {
        public Task RunAsync(TranscriptSource source, TranscriptClock clock, Action<TranscriptAudioWindow> onWindow, Action<float> onLevel, CancellationToken stop)
        { var timeline = new TranscriptTimeline(onWindow, 8000); timeline.Add(0, samples); timeline.Finish(samples.Length); return Task.CompletedTask; }
    }
    public static async Task<int> RunAsync(string executable, string kind, string model, string wav, string device)
    {
        using var reader = new WaveFileReader(wav);
        if (reader.WaveFormat.SampleRate != 16000 || reader.WaveFormat.Channels != 1) throw new InvalidDataException("16 kHz mono required");
        var provider = reader.ToSampleProvider(); var samples = new List<float>(); float[] buffer = new float[4096]; int n;
        while ((n = provider.Read(buffer, 0, buffer.Length)) > 0) samples.AddRange(buffer.AsSpan(0, n).ToArray());
        await using var worker = new ModelWorkerClient(); var clock = Stopwatch.StartNew();
        await worker.StartAsync(kind, model, Enum.Parse<ModelAcceleration>(device), default, executable);
        Console.WriteLine($"Ready in {clock.Elapsed.TotalSeconds:F2}s: {worker.Backend}");
        if (kind == "whisper")
        {
            clock.Restart(); var lines = await worker.TranscribeSegmentsAsync(samples.ToArray(), "auto", default, true);
            Console.WriteLine(JsonSerializer.Serialize(lines));
            if (lines.Count == 0 || lines.All(l => string.IsNullOrWhiteSpace(l.Text))) throw new InvalidDataException("No transcription");
        }
        else
        {
            clock.Restart(); var labels = new HashSet<string>();
            // Exercise streaming across small completed phrases, including a sub-second final fragment.
            for (int first = 0; first < samples.Count; first += 8 * 16000)
            {
                var audio = samples.Skip(first).Take(8 * 16000).ToArray();
                var turns = await worker.AnalyzeAsync(1, audio, default);
                Console.WriteLine($"{first / 16000.0:F2}s: {JsonSerializer.Serialize(turns)}");
                foreach (var turn in turns) if (turn.Label.StartsWith("Speaker ")) labels.Add(turn.Label);
                if (turns.Count == 0 || turns[0].Start != TimeSpan.Zero || Math.Abs(turns[^1].End.TotalSeconds - audio.Length / 16000.0) > 0.001)
                    throw new InvalidDataException("Speaker coverage incomplete");
            }
            Console.WriteLine("Distinct: " + string.Join(", ", labels));
            if (labels.Count < 2) throw new InvalidDataException("Multiple voices were not identified.");
        }
        Console.WriteLine(worker.Diagnostics);
        Console.WriteLine($"Inference: {clock.Elapsed.TotalSeconds:F2}s for {samples.Count / 16000.0:F2}s of audio");
        return 0;
    }
}
