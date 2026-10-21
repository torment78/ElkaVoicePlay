using System.Text;
using ElkaVoicePlay.Shared;

namespace ElkaVoicePlay.Core;

public sealed class TranscriptFiles : IAsyncDisposable
{
    private readonly Dictionary<int, StreamWriter> writers = [];
    private readonly bool combine;
    public string DirectoryPath { get; }
    public string[] Paths { get; }
    public int Lines { get; private set; }
    public TranscriptFiles(TranscriptionSettings settings, DateTimeOffset startedAt)
    {
        settings = settings.Validate(); combine = settings.Combine;
        DirectoryPath = Path.Combine(settings.Folder, $"{startedAt:yyyy-MM-dd_HH-mm-ss}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(DirectoryPath);
        var paths = new List<string>();
        try
        {
            foreach (var source in settings.Sources.Where(s => s.Enabled))
            {
                if (combine && writers.Count > 0) break;
                string file = combine ? "Transcript.txt" : $"{source.Slot:00}-{SafeName(source.Name)}.txt";
                string path = Path.Combine(DirectoryPath, file);
                var writer = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false)) { AutoFlush = true };
                writers.Add(combine ? 0 : source.Slot, writer); paths.Add(path);
                writer.WriteLine("ELKA VOICEPLAY · Audio transcript");
                writer.WriteLine($"Session started: {startedAt:O}");
                writer.WriteLine("Times are elapsed from session start. Speech boundaries are estimates. Input names identify audio sources.");
                writer.WriteLine($"Input language: {settings.Language}; output: {(settings.TranslateToEnglish ? "English translation" : "original language")}");
                if (settings.LabelSpeakers) writer.WriteLine("Speaker labels are estimates, separate for each input and session. Unknown/overlapping voices cannot be reliably attributed.");
                writer.WriteLine("Inputs: " + string.Join("; ", settings.Sources.Where(s => s.Enabled && (combine || s.Slot == source.Slot)).Select(s => $"{s.Slot}: {s.Name} ({s.Kind})")));
                writer.WriteLine();
            }
        }
        catch { foreach (var writer in writers.Values) writer.Dispose(); throw; }
        Paths = paths.ToArray();
    }
    private static string SafeName(string name) => string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).TrimEnd(' ', '.');
    public static string FormatTime(TimeSpan time) => $"{(long)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}.{time.Milliseconds:000}";
    public static string Format(TranscriptLine line) => $"[{FormatTime(line.Start)} --> {FormatTime(line.End)}] [{line.Source}]{(line.Speaker == null ? "" : " [" + line.Speaker + "]")} {line.Text}";
    public async Task AppendAsync(IEnumerable<TranscriptLine> lines)
    {
        foreach (var line in lines.OrderBy(l => l.Start).ThenBy(l => l.Slot).ThenBy(l => l.End))
        {
            await writers[combine ? 0 : line.Slot].WriteLineAsync(Format(line)).ConfigureAwait(false);
            Lines++;
        }
    }
    public async Task CompleteAsync(string? error)
    {
        foreach (var writer in writers.Values)
        {
            await writer.WriteLineAsync().ConfigureAwait(false);
            await writer.WriteLineAsync(error == null ? "# Recording stopped; captured audio processed." : "# Recording stopped with an error: " + error.Replace('\r', ' ').Replace('\n', ' ')).ConfigureAwait(false);
        }
    }
    public async ValueTask DisposeAsync() { foreach (var writer in writers.Values) await writer.DisposeAsync().ConfigureAwait(false); }
}
