namespace ElkaVoicePlay.Shared;

/// <summary>Versioned transcription runtime, independent of game dictation.</summary>
public static class TranscriptionModule
{
    public const string Id = "transcription";
    public static bool IsInstalled(string? root = null) => OptionalComponents.IsInstalled(Id, root);
    public static string WorkerPath(string? root = null) => OptionalComponents.FileFor(Id, "Workers/ElkaVoicePlay.ModelWorker.exe", root);
    public static string SpeakerModels(string? root = null) => OptionalComponents.FileFor(Id, "Models/Speakers", root);
    public static string RequireWorker(string? root = null)
    {
        if (!IsInstalled(root)) throw new InvalidOperationException("Transcribe audio is not installed or needs repair. Open Transcribe audio and click Download.");
        return WorkerPath(root);
    }
}
