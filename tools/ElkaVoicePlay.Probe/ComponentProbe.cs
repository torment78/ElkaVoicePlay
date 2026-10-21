using System.Net;
using ElkaVoicePlay.Core;
using ElkaVoicePlay.Shared;
using ElkaVoicePlay.Speech;
using NAudio.Wave;

internal static class ComponentProbe
{
    private sealed class FilesHandler(string folder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = Path.Combine(folder, Uri.UnescapeDataString(request.RequestUri!.Segments.Last()));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(File.OpenRead(path)) });
        }
    }
    public static async Task<int> RunAsync(string[] args)
    {
        string source = args[1], root = Path.GetFullPath(args[2]), workerPath = Path.GetFullPath(args[3]), fixture = Path.GetFullPath(args[4]), voices = Path.GetFullPath(args[5]);
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any()) throw new InvalidOperationException("Use an empty component test directory.");
        string app = Path.GetDirectoryName(Path.GetDirectoryName(workerPath))!;
        await using (var baseWorker = new ModelWorkerClient())
        {
            await baseWorker.StartAsync("whisper", Path.Combine(app,"Models","ggml-base.bin"), ModelAcceleration.Automatic, default, workerPath, root);
            if (baseWorker.Backend != "CPU") throw new InvalidOperationException("Small bundle did not fall back to CPU.");
            Console.WriteLine("Small bundle Automatic: " + baseWorker.Backend);
        }
        using var client = source == "github" ? new HttpClient() : new HttpClient(new FilesHandler(Path.GetFullPath(source)));
        client.Timeout = Timeout.InfiniteTimeSpan;
        var installer = new ComponentInstaller(client, root);
        foreach (var component in OptionalComponents.All)
        {
            Console.WriteLine("Installing and verifying " + component.Name + " from " + source);
            await installer.InstallAsync(component, null, default);
            if (!OptionalComponents.IsInstalled(component, root)) throw new InvalidOperationException("Missing installed marker: " + component.Id);
        }
        foreach (string id in new[] { "medium", "large-v3" })
        {
            await using var speech = new ModelWorkerClient();
            await speech.StartAsync("whisper", OptionalComponents.FileFor(id,"Models/ggml-" + id + ".bin",root), ModelAcceleration.NvidiaGpu, default, workerPath, root);
            var lines = await speech.TranscribeSegmentsAsync(Read(fixture), "en", default);
            string text = string.Join(" ", lines.Select(s=>s.Text)); Console.WriteLine(speech.Backend + ": " + text);
            if (!text.Contains("country",StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException(id + " fixture failed.");
        }
        foreach (var device in new[] { ModelAcceleration.Cpu, ModelAcceleration.NvidiaGpu })
        {
            await using var diarizer = new ModelWorkerClient();
            await diarizer.StartAsync("nemotron",OptionalComponents.FileFor("nemotron","Models/Nemotron-3-Diarization.q8_0.gguf",root),device,default,workerPath,root);
            float[] samples = Read(voices); var labels = new HashSet<string>();
            for (int position=0; position<samples.Length; position+=128000)
                foreach (var turn in await diarizer.AnalyzeAsync(1,samples.Skip(position).Take(128000).ToArray(),default)) if (turn.Label.StartsWith("Speaker ",StringComparison.Ordinal)) labels.Add(turn.Label);
            Console.WriteLine(diarizer.Backend + ": " + string.Join(", ",labels));
            if (labels.Count < 2) throw new InvalidOperationException("Nemotron fixture lost speaker labels.");
        }
        Console.WriteLine("All optional assets verified, installed and exercised successfully."); return 0;
    }
    private static float[] Read(string path)
    {
        using var wav = new WaveFileReader(path);
        if (wav.WaveFormat.SampleRate != 16000 || wav.WaveFormat.Channels != 1 || wav.WaveFormat.BitsPerSample != 16) throw new InvalidDataException("Use PCM16 16kHz mono.");
        byte[] bytes = new byte[wav.Length]; wav.ReadExactly(bytes); float[] samples = new float[bytes.Length/2];
        for(int i=0;i<samples.Length;i++) samples[i] = BitConverter.ToInt16(bytes,i*2)/32768f;
        return samples;
    }
}
