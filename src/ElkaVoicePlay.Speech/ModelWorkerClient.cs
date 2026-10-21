using System.Diagnostics;
using System.IO.Pipes;
using ElkaVoicePlay.Shared;

namespace ElkaVoicePlay.Speech;

public sealed class ModelWorkerClient : IWordTranscriptRecognizer, ISpeakerDiarizer, IAsyncDisposable
{
    private readonly NamedPipeServerStream pipe;
    private readonly SemaphoreSlim gate = new(1, 1);
    private Process? process;
    private readonly Queue<string> diagnostics = new();
    public string Backend { get; private set; } = "Not loaded";
    public int? ProcessId => process?.Id;
    public string Diagnostics { get { lock (diagnostics) return string.Join(Environment.NewLine, diagnostics); } }
    public static string DefaultExecutable => TranscriptionModule.RequireWorker();
    public async Task StartAsync(string kind, string modelPath, ModelAcceleration acceleration, CancellationToken token, string? executable = null, string? componentsRoot = null)
    {
        executable ??= TranscriptionModule.RequireWorker(componentsRoot);
        if (!File.Exists(executable)) throw new FileNotFoundException("The transcription worker is missing. Open Transcribe audio and download the component again.", executable);
        if (!File.Exists(modelPath) && kind != "classic") throw new FileNotFoundException("Selected model is missing. Open Downloads in VoicePlay, or extract the complete small application folder including Models.", modelPath);
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(executable)!, RedirectStandardError = true, RedirectStandardOutput = true };
        info.ArgumentList.Add(pipeName); info.ArgumentList.Add(kind); info.ArgumentList.Add(Path.GetFullPath(modelPath)); info.ArgumentList.Add(acceleration.ToString()); info.ArgumentList.Add(componentsRoot ?? OptionalComponents.Root);
        process = new Process { StartInfo = info, EnableRaisingEvents = true };
        process.ErrorDataReceived += (_, e) => Log(e.Data); process.OutputDataReceived += (_, e) => Log(e.Data);
        if (!process.Start()) throw new InvalidOperationException("Could not start the model worker.");
        process.BeginErrorReadLine(); process.BeginOutputReadLine();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromMinutes(3));
        try
        {
            var connected = pipe.WaitForConnectionAsync(deadline.Token);
            var exited = process.WaitForExitAsync(deadline.Token);
            if (await Task.WhenAny(connected, exited) != connected) throw new IOException("Model worker exited before connecting.");
            await connected;
            var ready = await ModelProtocol.ReadJsonAsync<ModelReply>(pipe, deadline.Token);
            if (ready.Error != null) throw new InvalidOperationException(ready.Error);
            Backend = ready.Backend ?? "Ready";
        }
        catch (Exception ex) when (!token.IsCancellationRequested) { throw Failure(ex); }
    }
    private readonly string pipeName;
    public ModelWorkerClient()
    {
        pipeName = "ElkaVoicePlay." + Guid.NewGuid().ToString("N");
        pipe = new(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    }
    private void Log(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        lock (diagnostics) { diagnostics.Enqueue(line.Length > 1000 ? line[..1000] : line); while (diagnostics.Count > 100) diagnostics.Dequeue(); }
    }
    private Exception Failure(Exception cause)
    {
        string log; lock (diagnostics) log = string.Join(Environment.NewLine, diagnostics.TakeLast(4));
        return new InvalidOperationException("The transcription model worker stopped or could not start. Completed text remains saved. " + cause.Message +
            (log.Length == 0 ? "" : Environment.NewLine + log), cause);
    }
    private async Task<ModelReply> RequestAsync(ModelRequest request, float[] samples, CancellationToken token)
    {
        await gate.WaitAsync(token);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromMinutes(5));
        try
        {
            await ModelProtocol.WriteJsonAsync(pipe, request, deadline.Token); await ModelProtocol.WriteAudioAsync(pipe, samples, deadline.Token);
            var reply = await ModelProtocol.ReadJsonAsync<ModelReply>(pipe, deadline.Token);
            if (reply.Error != null) throw new InvalidOperationException(reply.Error);
            return reply;
        }
        catch (Exception ex) when (!token.IsCancellationRequested) { throw Failure(ex); }
        finally { gate.Release(); }
    }
    public async Task<IReadOnlyList<SpeechSegment>> TranscribeSegmentsAsync(float[] samples, string language, CancellationToken token, bool translateToEnglish = false)
        => (await RequestAsync(new("transcribe", Language: language, Translate: translateToEnglish), samples, token)).Segments ?? [];
    public async Task<IReadOnlyList<SpeechSegment>> TranscribeWordsAsync(float[] samples, string language, CancellationToken token, bool translateToEnglish)
        => (await RequestAsync(new("transcribe", Language: language, Translate: translateToEnglish, Words: true), samples, token)).Segments ?? [];
    public async Task<IReadOnlyList<SpeakerTurn>> AnalyzeAsync(int slot, float[] samples, CancellationToken token)
        => (await RequestAsync(new("speakers", Slot: slot), samples, token)).Turns ?? [];
    public async ValueTask DisposeAsync()
    {
        pipe.Dispose();
        if (process != null)
        {
            try { if (!process.HasExited) { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3)); await process.WaitForExitAsync(timeout.Token); } }
            catch (OperationCanceledException)
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { /* The worker exited between the check and Kill. */ }
                await process.WaitForExitAsync();
            }
            catch (InvalidOperationException) { /* Process.Start failed; there is no process to release. */ }
            finally { process.Dispose(); process = null; }
        }
    }
}
