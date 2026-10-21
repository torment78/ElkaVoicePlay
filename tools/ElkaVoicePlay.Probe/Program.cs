using System.Diagnostics;
using ElkaVoicePlay.Audio;
using ElkaVoicePlay.Core;
using ElkaVoicePlay.Speech;
using NAudio.Wave;

if (args.Length == 6 && args[0] == "--components-fixture")
{
    try { return await ComponentProbe.RunAsync(args); }
    catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
}
if (args.Length == 3 && args[0] == "--worker-recovery")
{
    try { return await ModelWorkerProbe.RecoveryAsync(args[1], args[2]); }
    catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
}
if (args.Length == 6 && args[0] == "--model-session-fixture")
{
    try { return await ModelWorkerProbe.SessionAsync(args[1], args[2], args[3], args[4], args[5]); }
    catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
}
if (args.Length == 6 && args[0] == "--model-worker-fixture")
{
    try { return await ModelWorkerProbe.RunAsync(args[1], args[2], args[3], args[4], args[5]); }
    catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
}
if (args.Length > 0 && args[0] is "--check-transcript-capture" or "--transcript-fixture" or "--check-configured-capture" or "--speaker-fixture")
{
    try
    {
        if (args.Length == 3 && args[0] == "--check-configured-capture") return await TranscriptProbe.CaptureCheckAsync(args[1], int.Parse(args[2]));
        if (args.Length == 5 && args[0] == "--speaker-fixture") return await SpeakerProbe.RunAsync(args[1], args[2], args[3], args[4]);
        if (args.Length == 1 && args[0] == "--check-transcript-capture") return await TranscriptProbe.CaptureCheckAsync();
        if (args.Length == 4 && args[0] == "--transcript-fixture") return await TranscriptProbe.FixtureAsync(args[1], args[2], args[3]);
        throw new ArgumentException("Use --check-transcript-capture or --transcript-fixture <model> <16kHz mono WAV> <output folder>.");
    }
    catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
}
if (args.Length == 3 && args[0] == "--check-bundled-model")
{
    try
    {
        string directory = Path.GetFullPath(args[2]);
        if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
            throw new InvalidOperationException("Use an empty directory for the first-launch model check.");
        string path = await BundledModelSetup.ResolveAsync(Path.Combine(directory, BundledModelSetup.FileName), directory, Path.GetFullPath(args[1]));
        await using var speech = new WhisperRecognizer();
        await speech.LoadAsync(path);
        Console.WriteLine($"Bundled model installed and loaded successfully: {path}");
        return 0;
    }
    catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
}
if (args.Length == 1 && args[0] == "--devices")
{
    foreach (var device in MicrophoneRecorder.Enumerate()) Console.WriteLine(device.Name);
    return 0;
}
if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: ElkaVoicePlay.Probe <ggml-model.bin> <16 kHz mono PCM WAV> | --devices | --check-bundled-model <app folder> <empty model folder>");
    return 2;
}
try
{
    using var reader = new WaveFileReader(args[1]);
    if (reader.WaveFormat.SampleRate != 16000 || reader.WaveFormat.Channels != 1)
        throw new InvalidOperationException("Probe expects 16 kHz mono WAV.");
    var provider = reader.ToSampleProvider();
    var samples = new List<float>();
    var buffer = new float[4096];
    int count;
    while ((count = provider.Read(buffer, 0, buffer.Length)) > 0) samples.AddRange(buffer.AsSpan(0, count).ToArray());
    await using var speech = new WhisperRecognizer();
    var clock = Stopwatch.StartNew();
    await speech.LoadAsync(args[0]);
    Console.WriteLine($"Model loaded in {clock.Elapsed.TotalSeconds:F2}s");
    for (int i = 1; i <= 2; i++)
    {
        clock.Restart();
        string text = await speech.TranscribeAsync(samples.ToArray(), "en", CancellationToken.None);
        Console.WriteLine($"Pass {i} ({clock.Elapsed.TotalSeconds:F2}s): {text}");
        if (string.IsNullOrWhiteSpace(text)) return 1;
    }
    return 0;
}
catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
