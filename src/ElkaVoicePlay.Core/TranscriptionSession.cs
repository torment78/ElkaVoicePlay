using System.Collections.Concurrent;
using System.Threading.Channels;
using ElkaVoicePlay.Shared;

namespace ElkaVoicePlay.Core;

public sealed class TranscriptionSession(ITranscriptCapture capture, ITranscriptRecognizer recognizer, ISpeakerDiarizer? diarizer = null)
{
    private int running, pending;
    public int PendingWindows => Volatile.Read(ref pending);
    public event Action<string, string[]>? FilesCreated;
    public event Action<IReadOnlyList<TranscriptLine>>? LinesWritten;
    public event Action<TranscriptLine>? LineRecognized;
    public event Action<int, float>? LevelChanged;
    public async Task<TranscriptResult> RunAsync(TranscriptionSettings requested, CancellationToken stop)
    {
        if (Interlocked.Exchange(ref running, 1) != 0) throw new InvalidOperationException("A transcription session is already running.");
        try { return await RunCoreAsync(requested.Validate(), stop).ConfigureAwait(false); }
        finally { Volatile.Write(ref running, 0); Volatile.Write(ref pending, 0); }
    }
    private async Task<TranscriptResult> RunCoreAsync(TranscriptionSettings settings, CancellationToken stop)
    {
        if (settings.LabelSpeakers && diarizer == null) throw new InvalidOperationException("Speaker detection models are not available.");
        using var captureStop = CancellationTokenSource.CreateLinkedTokenSource(stop);
        var clock = new TranscriptClock();
        await using var files = new TranscriptFiles(settings, clock.StartedAt);
        FilesCreated?.Invoke(files.DirectoryPath, files.Paths);
        var errors = new ConcurrentQueue<string>();
        var sources = settings.Sources.Where(s => s.Enabled).ToArray();
        // Half-second packets, at most sixty seconds per input. The sample budget also covers test/legacy packet sizes.
        var channels = sources.Select(_ => Channel.CreateBounded<TranscriptAudioWindow>(new BoundedChannelOptions(120)
            { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait })).ToArray();
        var queuedSamples = new int[sources.Length];
        var phrases = sources.Select(_ => new TranscriptPhrase()).ToArray();
        var completed = new bool[sources.Length];
        var waiting = new List<TranscriptLine>();
        var captures = sources.Select((source, i) => CaptureAsync(source, i)).ToArray();
        try
        {
            while (true)
            {
                var windows = await Task.WhenAll(channels.Select(ReadNext)).ConfigureAwait(false);
                for (int i = 0; i < windows.Length; i++)
                {
                    var window = windows[i];
                    var phrase = phrases[i];
                    if (window != null)
                    {
                        Interlocked.Decrement(ref pending);
                        Interlocked.Add(ref queuedSamples[i], -window.Samples.Length);
                        phrase.Add(window);
                    }
                    completed[i] = window == null || channels[i].Reader.Completion.IsCompleted;
                    bool final = completed[i] || phrase.IsComplete;
                    if (!phrase.HasAudio || !final) continue;
                    var audio = phrase.Snapshot();
                    var duration = TimeSpan.FromSeconds(audio.Length / (double)TranscriptClock.SampleRate);
                    IReadOnlyList<SpeakerTurn> turns = settings.LabelSpeakers
                        ? await diarizer!.AnalyzeAsync(sources[i].Slot, audio, CancellationToken.None).ConfigureAwait(false)
                        : [new(TimeSpan.Zero, duration, "")];
                    if (settings.LabelSpeakers && recognizer is IWordTranscriptRecognizer wordRecognizer)
                    {
                        var words = await wordRecognizer.TranscribeWordsAsync(audio, settings.Language, CancellationToken.None, settings.TranslateToEnglish).ConfigureAwait(false);
                        foreach (var line in TranscriptWordLines.Build(words, turns, sources[i].Slot, sources[i].Name, phrase.Start, duration))
                        { waiting.Add(line); LineRecognized?.Invoke(line); }
                    }
                    else foreach (var turn in turns.OrderBy(t => t.Start))
                    {
                        int first = (int)Math.Clamp(Math.Round(turn.Start.TotalSeconds * TranscriptClock.SampleRate), 0, audio.Length);
                        int last = (int)Math.Clamp(Math.Round(turn.End.TotalSeconds * TranscriptClock.SampleRate), first, audio.Length);
                        if (last == first) continue;
                        var turnStart = TimeSpan.FromSeconds(first / (double)TranscriptClock.SampleRate);
                        var turnDuration = TimeSpan.FromSeconds((last - first) / (double)TranscriptClock.SampleRate);
                        var segments = await recognizer.TranscribeSegmentsAsync(audio[first..last], settings.Language, CancellationToken.None, settings.TranslateToEnglish).ConfigureAwait(false);
                        foreach (var segment in segments)
                        {
                            string text = DictationController.Normalize(segment.Text);
                            if (text.Length == 0) continue;
                            var start = TimeSpan.FromTicks(Math.Clamp(segment.Start.Ticks, 0, turnDuration.Ticks));
                            var end = TimeSpan.FromTicks(Math.Clamp(segment.End.Ticks, start.Ticks, turnDuration.Ticks));
                            if (start >= turnDuration) continue;
                            var line = new TranscriptLine(sources[i].Slot, sources[i].Name, phrase.Start + turnStart + start,
                                phrase.Start + turnStart + end, text, settings.LabelSpeakers ? turn.Label : null);
                            waiting.Add(line);
                            LineRecognized?.Invoke(line);
                        }
                    }
                    phrase.Clear();
                }
                // A still-speaking input may later finish a line earlier than another input's line.
                // Append to both display and files only when every source has passed their start.
                var watermark = phrases.Select((p, i) => completed[i] ? TimeSpan.MaxValue : p.Watermark).Min();
                await FlushAsync(watermark).ConfigureAwait(false);
                if (windows.All(w => w == null)) break;
            }
        }
        catch (Exception ex) { errors.Enqueue(ex.Message); captureStop.Cancel(); }
        finally
        {
            captureStop.Cancel();
            await Task.WhenAll(captures).ConfigureAwait(false);
        }
        await FlushAsync(TimeSpan.MaxValue).ConfigureAwait(false);
        string? error = errors.IsEmpty ? null : string.Join(" | ", errors.Distinct());
        await files.CompleteAsync(error).ConfigureAwait(false);
        return new(files.DirectoryPath, files.Paths, error, files.Lines);

        async Task FlushAsync(TimeSpan watermark)
        {
            var ready = waiting.Where(l => l.Start < watermark).OrderBy(l => l.Start).ThenBy(l => l.Slot).ThenBy(l => l.End).ToArray();
            if (ready.Length == 0) return;
            await files.AppendAsync(ready).ConfigureAwait(false);
            waiting.RemoveAll(l => l.Start < watermark);
            LinesWritten?.Invoke(ready);
        }
        async Task CaptureAsync(TranscriptSource source, int i)
        {
            try
            {
                await capture.RunAsync(source, clock, window =>
                {
                    int samples = Interlocked.Add(ref queuedSamples[i], window.Samples.Length);
                    Interlocked.Increment(ref pending);
                    if (samples > 60 * TranscriptClock.SampleRate || !channels[i].Writer.TryWrite(window))
                    {
                        Interlocked.Decrement(ref pending);
                        Interlocked.Add(ref queuedSamples[i], -window.Samples.Length);
                        throw new InvalidOperationException("Transcription could not keep up. Capture stopped instead of silently dropping audio; try fewer inputs or a smaller model.");
                    }
                }, level => LevelChanged?.Invoke(source.Slot, level), captureStop.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (captureStop.IsCancellationRequested) { }
            catch (Exception ex) { errors.Enqueue($"{source.Name}: {ex.Message}"); captureStop.Cancel(); }
            finally { channels[i].Writer.TryComplete(); LevelChanged?.Invoke(source.Slot, 0); }
        }
        static async Task<TranscriptAudioWindow?> ReadNext(Channel<TranscriptAudioWindow> channel)
        {
            while (await channel.Reader.WaitToReadAsync().ConfigureAwait(false))
                if (channel.Reader.TryRead(out var window)) return window;
            return null;
        }
    }
}