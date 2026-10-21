using ElkaVoicePlay.Shared;
using Whisper.net;
using Whisper.net.LibraryLoader;

namespace ElkaVoicePlay.Speech;

public sealed class WhisperRecognizer : ISpeechRecognizer, ITranscriptRecognizer, IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private WhisperFactory? factory;
    public bool IsReady => factory != null;
    public string? LoadedPath { get; private set; }

    public async Task LoadAsync(string path, CancellationToken token = default, bool useGpu = false)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Choose an installed ggml Whisper model, or run scripts/Get-Model.ps1.", path);
        await gate.WaitAsync(token);
        try
        {
            if (string.Equals(LoadedPath, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)) return;
            await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                RuntimeOptions.RuntimeLibraryOrder = useGpu ? [RuntimeLibrary.Cuda] : [RuntimeLibrary.Cpu];
                // Free the old model before loading another to avoid doubling peak model memory.
                factory?.Dispose();
                factory = null;
                LoadedPath = null;
                var loaded = WhisperFactory.FromPath(path, new WhisperFactoryOptions { UseGpu = useGpu, UseFlashAttention = useGpu });
                if (token.IsCancellationRequested) { loaded.Dispose(); token.ThrowIfCancellationRequested(); }
                factory = loaded;
                LoadedPath = Path.GetFullPath(path);
            }, token);
        }
        finally { gate.Release(); }
    }

    public async Task<string> TranscribeAsync(float[] samples, string language, CancellationToken cancellationToken)
    {
        var segments = await RecognizeAsync(samples, language, cancellationToken, false);
        return string.Concat(segments.Select(s => s.Text)).Trim();
    }

    public Task<IReadOnlyList<SpeechSegment>> TranscribeSegmentsAsync(float[] samples, string language, CancellationToken token, bool translateToEnglish = false)
        => RecognizeAsync(samples, language, token, true, translateToEnglish);

    public Task<IReadOnlyList<SpeechSegment>> TranscribeWordsAsync(float[] samples, string language, CancellationToken token, bool translateToEnglish)
        => RecognizeAsync(samples, language, token, true, translateToEnglish, !translateToEnglish);

    private async Task<IReadOnlyList<SpeechSegment>> RecognizeAsync(float[] samples, string language, CancellationToken token, bool transcript, bool translateToEnglish = false, bool words = false)
    {
        await gate.WaitAsync(token);
        try
        {
            ValidateTranslation(LoadedPath, translateToEnglish);
            if (factory == null) throw new InvalidOperationException("No speech model loaded.");
            return await Task.Run<IReadOnlyList<SpeechSegment>>(async () =>
            {
                var builder = factory.CreateBuilder()
                    .WithLanguage(language)
                    .WithNoContext()
                    .WithThreads(Math.Clamp(Environment.ProcessorCount / 2, 1, 8));
                if (translateToEnglish) builder.WithTranslate();
                if (transcript) builder.WithTokenTimestamps().WithMaxSegmentLength(words ? 1 : 100).SplitOnWord();
                using var processor = builder.Build();
                var results = new List<SpeechSegment>();
                await foreach (var segment in processor.ProcessAsync(samples, token))
                {
                    token.ThrowIfCancellationRequested();
                    results.Add(new(segment.Start, segment.End, segment.Text));
                }
                return results;
            }, token);
        }
        finally { gate.Release(); }
    }
    public static void ValidateTranslation(string? modelPath, bool enabled)
    {
        string name = Path.GetFileName(modelPath)?.ToLowerInvariant() ?? "";
        if (enabled && (name.Contains(".en") || name.Contains("turbo")))
            throw new InvalidOperationException("English translation needs a multilingual Whisper model, such as the included Base model. English-only and Turbo models do not support this mode.");
    }
    public async ValueTask DisposeAsync()
    {
        await gate.WaitAsync();
        try { factory?.Dispose(); factory = null; LoadedPath = null; }
        finally { gate.Release(); }
    }
}
