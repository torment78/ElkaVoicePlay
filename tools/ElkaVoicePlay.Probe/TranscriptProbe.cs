using System.Collections.Concurrent;
using System.Text.Json;
using ElkaVoicePlay.Audio;
using ElkaVoicePlay.Core;
using ElkaVoicePlay.Shared;
using ElkaVoicePlay.Speech;
using NAudio.Wave;

internal static class TranscriptProbe
{
    internal static async Task<int> CaptureCheckAsync(string? settingsFile = null, int seconds = 12)
    {
        var devices = TranscriptCapture.Enumerate();
        var sources = new[] { TranscriptInputKind.Microphone, TranscriptInputKind.Loopback }.Select((kind, i) =>
        {
            var device = devices.FirstOrDefault(d => d.Kind == kind && d.IsDefault) ?? devices.First(d => d.Kind == kind);
            return new TranscriptSource { Slot = i + 1, Enabled = true, Name = device.Name, Kind = kind, DeviceId = device.Id };
        }).ToArray();
        if (settingsFile != null)
            sources = (await new TranscriptionSettingsStore(settingsFile).LoadAsync()).Sources.Where(s => s.Enabled).ToArray();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        var clock = new TranscriptClock(); var results = new ConcurrentDictionary<int, int>();
        var capture = new TranscriptCapture();
        await Task.WhenAll(sources.Select(source => capture.RunAsync(source, clock,
            window => results.AddOrUpdate(source.Slot, window.Samples.Length, (_, n) => n + window.Samples.Length), _ => { }, stop.Token)));
        if (results.Count != sources.Length || results.Values.Any(n => n < (seconds - 1) * TranscriptClock.SampleRate)) throw new InvalidOperationException("Capture did not produce the expected windows.");
        Console.WriteLine(JsonSerializer.Serialize(new { Check = "Native microphone and loopback capture", Passed = true,
            Inputs = sources.Select(s => new { s.Kind, s.Name, Samples = results[s.Slot] }), AudioSaved = false }));
        return 0;
    }
    internal static async Task<int> FixtureAsync(string model, string wav, string output)
    {
        using var reader = new WaveFileReader(wav);
        if (reader.WaveFormat.SampleRate != 16000 || reader.WaveFormat.Channels != 1) throw new InvalidOperationException("Use 16-kHz mono fixture audio.");
        var sampleProvider = reader.ToSampleProvider(); var buffer = new float[4096]; var data = new List<float>(); int count;
        while ((count = sampleProvider.Read(buffer, 0, buffer.Length)) > 0) data.AddRange(buffer.AsSpan(0, count).ToArray());
        await using var speech = new WhisperRecognizer(); await speech.LoadAsync(model);
        var settings = new TranscriptionSettings { Folder = Path.GetFullPath(output), Language = "en", Sources = Enumerable.Range(1, 3)
            .Select(i => new TranscriptSource { Slot = i, Enabled = true, Name = "Fixture " + i, DeviceId = "fixture-" + i }).ToArray() };
        var evidence = new List<object>();
        foreach (bool combined in new[] { true, false })
        {
            var lines = new List<TranscriptLine>(); var session = new TranscriptionSession(new FixtureCapture(data.ToArray(), combined), speech);
            session.LinesWritten += batch => lines.AddRange(batch);
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            double? firstCompletedSeconds = null;
            session.LinesWritten += lines => { if (lines.Count > 0) firstCompletedSeconds ??= elapsed.Elapsed.TotalSeconds; };
            var result = await session.RunAsync(settings with { Combine = combined }, default);
            if (result.Error != null || lines.Select(l => l.Slot).Distinct().Count() != 3 || result.Files.Length != (combined ? 1 : 3) ||
                !lines.Select(l => l.Start).SequenceEqual(lines.Select(l => l.Start).Order()) || lines.Any(l => l.End < l.Start))
                throw new InvalidOperationException("Fixture transcription failed: " + result.Error);
            evidence.Add(new { FirstCompletedSeconds = firstCompletedSeconds, Combined = combined, result.Directory, result.Files, result.Lines, Ordered = true, Sources = 3 });
        }
        Console.WriteLine(JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true })); return 0;
    }
    private sealed class FixtureCapture(float[] samples, bool paced) : ITranscriptCapture
    {
        public async Task RunAsync(TranscriptSource source, TranscriptClock clock, Action<TranscriptAudioWindow> onWindow, Action<float> onLevel, CancellationToken stop)
        {
            var packets = new List<TranscriptAudioWindow>(); var timeline = new TranscriptTimeline(packets.Add, 8000); long offset = (source.Slot - 1) * 2L * TranscriptClock.SampleRate;
            timeline.Add(offset, samples); timeline.Finish(offset + samples.Length);
            foreach (var packet in packets)
            {
                if (paced) await Task.Delay(TimeSpan.FromSeconds(packet.Samples.Length / 16000d), stop);
                onWindow(packet);
            }
        }
    }
}
