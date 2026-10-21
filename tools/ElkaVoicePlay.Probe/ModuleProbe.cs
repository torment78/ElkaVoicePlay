using ElkaVoicePlay.Core;
using ElkaVoicePlay.Shared;
using ElkaVoicePlay.Speech;
using NAudio.Wave;
using System.Net;

internal static class ModuleProbe
{
    private sealed class FilesHandler(string folder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(File.OpenRead(Path.Combine(folder,Uri.UnescapeDataString(request.RequestUri!.Segments.Last())))) });
    }
    public static async Task<int> RunAsync(string[] args)
    {
        string source=args[1], root=Path.GetFullPath(args[2]), app=Path.GetFullPath(args[3]);
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any()) throw new InvalidOperationException("Use an empty test component root.");
        if (Directory.Exists(Path.Combine(app,"Workers")) || Directory.Exists(Path.Combine(app,"Models","Speakers")) || File.Exists(Path.Combine(app,"onnxruntime.dll"))) throw new InvalidOperationException("Game package contains transcription binaries.");
        if (TranscriptionModule.IsInstalled(root)) throw new InvalidOperationException("Module is unexpectedly installed.");
        try { TranscriptionModule.RequireWorker(root); throw new InvalidOperationException("Missing module was accepted."); }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("Transcribe audio is not installed")) { }
        using var http=source=="github" ? new HttpClient() : new HttpClient(new FilesHandler(Path.GetFullPath(source)));
        http.Timeout=Timeout.InfiniteTimeSpan;
        Console.WriteLine("Downloading and verifying transcription module from " + source);
        await new ComponentInstaller(http,root).InstallAsync(OptionalComponents.Get(TranscriptionModule.Id),null,default);
        if (!TranscriptionModule.IsInstalled(root)) throw new InvalidOperationException("Transcription is not active after installation.");
        // Intentionally omit executable override: exercise the same installed-worker lookup as the UI.
        await using (var speech=new ModelWorkerClient())
        {
            await speech.StartAsync("whisper",Path.Combine(app,"Models","ggml-base.bin"),ModelAcceleration.Automatic,default,componentsRoot:root);
            var lines=await speech.TranscribeSegmentsAsync(Read(args[4]),"en",default);
            string text=string.Join(" ",lines.Select(s=>s.Text));
            if (speech.Backend!="CPU" || !text.Contains("country",StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Shared Base/CPU fixture failed.");
            Console.WriteLine("Installed module uses shared Base on CPU: " + text);
        }
        await using (var speakers=new ModelWorkerClient())
        {
            await speakers.StartAsync("classic",TranscriptionModule.SpeakerModels(root),ModelAcceleration.Cpu,default,componentsRoot:root);
            var turns=await speakers.AnalyzeAsync(1,Read(args[5]),default);
            var labels=turns.Select(t=>t.Label).Where(l=>l.StartsWith("Speaker ")).Distinct().ToArray();
            Console.WriteLine("Standard speakers: " + string.Join(", ",labels));
            if (labels.Length<2) throw new InvalidOperationException("Standard speaker fixture failed.");
        }
        Console.WriteLine("PASS: missing module gate, real download and install, shared Base, optional worker lookup and standard speakers.");
        return 0;
    }
    private static float[] Read(string path)
    {
        using var wav=new WaveFileReader(path);
        if (wav.WaveFormat.SampleRate!=16000 || wav.WaveFormat.Channels!=1 || wav.WaveFormat.BitsPerSample!=16) throw new InvalidDataException("Expected PCM16 16kHz mono fixture.");
        byte[] bytes=new byte[wav.Length];wav.ReadExactly(bytes);float[] samples=new float[bytes.Length/2];
        for(int i=0;i<samples.Length;i++)samples[i]=BitConverter.ToInt16(bytes,i*2)/32768f;
        return samples;
    }
}
