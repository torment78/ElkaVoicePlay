using ElkaVoicePlay.Shared;

namespace ElkaVoicePlay.Core;

/// <summary>Places timestamped packets onto a common 16-kHz session timeline, retaining silent gaps.</summary>
public sealed class TranscriptTimeline(Action<TranscriptAudioWindow> emit, int windowSamples = TranscriptAudioWindow.SampleCount)
{
    private float[] window = new float[windowSamples];
    private int index;
    private bool finished;
    public long CommittedSamples => (long)index * windowSamples;
    public void Add(long firstSample, ReadOnlySpan<float> samples)
    {
        if (finished) throw new InvalidOperationException("Capture already finished.");
        if (firstSample < 0) { int trim = (int)Math.Min(-firstSample, samples.Length); firstSample += trim; samples = samples[trim..]; }
        if (samples.IsEmpty) return;
        long committed = (long)index * window.Length;
        // Converted-device packet timestamps can jitter by a fraction of a millisecond at a committed boundary.
        if (firstSample < committed && committed - firstSample <= TranscriptClock.SampleRate / 1000)
        {
            int trim = (int)Math.Min(committed - firstSample, samples.Length);
            firstSample += trim; samples = samples[trim..];
            if (samples.IsEmpty) return;
        }
        if (firstSample < committed) throw new InvalidOperationException($"Audio arrived {(committed - firstSample) / 16d:F3} ms behind the committed timeline (sample {firstSample}, boundary {committed}). Recording stopped to preserve transcript timing.");
        while (!samples.IsEmpty)
        {
            long end = (long)(index + 1) * window.Length;
            if (firstSample >= end) { Emit(window.Length); continue; }
            int offset = (int)(firstSample - (long)index * window.Length);
            int count = Math.Min(samples.Length, window.Length - offset);
            samples[..count].CopyTo(window.AsSpan(offset));
            firstSample += count; samples = samples[count..];
            if (offset + count == window.Length) Emit(window.Length);
        }
    }
    public void Advance(long watermark)
    {
        if (finished) return;
        while ((long)(index + 1) * window.Length <= watermark) Emit(window.Length);
    }
    public void Finish(long endSample)
    {
        if (finished) return;
        Advance(endSample);
        int tail = (int)Math.Clamp(endSample - (long)index * window.Length, 0, window.Length);
        if (tail > 0) Emit(tail);
        finished = true;
    }
    private void Emit(int count)
    {
        var complete = count == window.Length ? window : window.AsSpan(0, count).ToArray();
        emit(new(index, complete, (long)index * windowSamples));
        index++; window = new float[windowSamples];
    }
}
