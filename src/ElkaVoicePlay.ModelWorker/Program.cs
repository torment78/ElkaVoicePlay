using System.IO.Pipes;
using System.Runtime.InteropServices;
using ElkaVoicePlay.Shared;
using ElkaVoicePlay.Speech;

if (args.Length is not 4 and not 5) return 2;
using var pipe = new NamedPipeClientStream(".", args[0], PipeDirection.InOut, PipeOptions.Asynchronous);
await pipe.ConnectAsync(15000);
WhisperRecognizer? whisper = null;
IDisposable? speakerOwner = null;
ISpeakerDiarizer? speakers = null;
try
{
    var acceleration = Enum.Parse<ModelAcceleration>(args[3]);
    string componentsRoot = args.Length == 5 ? args[4] : OptionalComponents.Root;
    bool installedGpu = OptionalComponents.IsInstalled("nvidia", componentsRoot);
    string? gpuName = installedGpu && acceleration != ModelAcceleration.Cpu && args[1] != "classic" ? GpuDevice.Name() : null;
    bool gpu = args[1] != "classic" && acceleration != ModelAcceleration.Cpu && gpuName != null;
    if (args[1] != "classic" && acceleration == ModelAcceleration.NvidiaGpu && !gpu)
        throw new InvalidOperationException(installedGpu ? "No supported NVIDIA GPU was found. Update the NVIDIA driver or choose CPU processing." : "NVIDIA support is not installed. Open Downloads in VoicePlay, or choose CPU / Automatic.");
    string backend = gpu ? "NVIDIA GPU · " + gpuName : "CPU";
    string runtime = args[1] == "nemotron"
        ? OptionalComponents.FileFor(gpu ? "nvidia" : "nemotron", gpu ? "NemoCuda" : "NemoCpu", componentsRoot)
        : gpu ? OptionalComponents.FileFor("nvidia", "runtimes/cuda/win-x64", componentsRoot) : "";
    if (runtime.Length > 0)
    {
        // Only this isolated worker gets the additional DLL search locations.
        string common = OptionalComponents.FileFor("nvidia", "Cuda", componentsRoot);
        Environment.SetEnvironmentVariable("PATH", runtime + Path.PathSeparator + (gpu ? common + Path.PathSeparator : "") + Environment.GetEnvironmentVariable("PATH"));
        GpuDevice.SetDllDirectory(runtime);
    }
    if (gpu && args[1] == "whisper") Whisper.net.LibraryLoader.RuntimeOptions.LibraryPath = OptionalComponents.FileFor("nvidia", "whisper.dll", componentsRoot);
    if (args[1] == "whisper")
    {
        whisper = new(); await whisper.LoadAsync(args[2], useGpu: gpu);
        Console.Error.WriteLine($"Whisper loaded runtime: {Whisper.net.LibraryLoader.RuntimeOptions.LoadedLibrary}");
    }
    else if (args[1] == "nemotron") speakers = (NemotronDiarizer)(speakerOwner = new NemotronDiarizer(args[2], gpu));
    else if (args[1] == "classic") speakers = (SpeakerDiarizer)(speakerOwner = new SpeakerDiarizer(args[2]));
    else throw new InvalidDataException("Unsupported model worker.");
    await ModelProtocol.WriteJsonAsync(pipe, new ModelReply(Backend: backend), default);
    while (pipe.IsConnected)
    {
        var request = await ModelProtocol.ReadJsonAsync<ModelRequest>(pipe, default);
        var samples = await ModelProtocol.ReadAudioAsync(pipe, default);
        ModelReply reply;
        try
        {
            reply = request.Operation switch
            {
                "transcribe" when whisper != null => new(Segments: (await (request.Words ? whisper.TranscribeWordsAsync(samples, request.Language, default, request.Translate) : whisper.TranscribeSegmentsAsync(samples, request.Language, default, request.Translate))).ToArray()),
                "speakers" when speakers != null => new(Turns: (await speakers.AnalyzeAsync(request.Slot, samples, default)).ToArray()),
                _ => throw new InvalidDataException("Invalid model operation.")
            };
        }
        catch (Exception ex) { reply = new(Error: ex.Message); }
        await ModelProtocol.WriteJsonAsync(pipe, reply, default);
    }
}
catch (EndOfStreamException) { }
catch (IOException) when (!pipe.IsConnected) { }
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    try { await ModelProtocol.WriteJsonAsync(pipe, new ModelReply(Error: ex.Message), default); } catch (IOException) { }
    return 1;
}
finally { speakerOwner?.Dispose(); if (whisper != null) await whisper.DisposeAsync(); }
return 0;

internal static class GpuDevice
{
    [DllImport("nvcuda.dll")] private static extern int cuInit(uint flags);
    [DllImport("nvcuda.dll")] private static extern int cuDriverGetVersion(out int version);
    [DllImport("nvcuda.dll")] private static extern int cuDeviceGetCount(out int count);
    [DllImport("nvcuda.dll", CharSet = CharSet.Ansi)] private static extern int cuDeviceGetName(System.Text.StringBuilder name, int length, int device);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetDllDirectory(string directory);
    public static string? Name()
    {
        try { if (cuDriverGetVersion(out int version) != 0 || version < 13000 || cuInit(0) != 0 || cuDeviceGetCount(out int count) != 0 || count == 0) return null;
            var name = new System.Text.StringBuilder(256); return cuDeviceGetName(name, name.Capacity, 0) == 0 ? name.ToString() : null; }
        catch (DllNotFoundException) { return null; } catch (EntryPointNotFoundException) { return null; }
    }
}
