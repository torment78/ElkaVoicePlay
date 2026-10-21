using ElkaVoicePlay.Shared;

namespace ElkaVoicePlay.Core;

/// <summary>Retains audio until a completed phrase is ready; silence closes a phrase without waiting for a fixed block.</summary>
public sealed class TranscriptPhrase
{
    private readonly List<float> samples = [];
    private int lastSignal;
    public TimeSpan Start { get; private set; }
    public TimeSpan Through { get; private set; }
    public bool HasAudio => samples.Count > 0;
    public bool IsComplete => HasAudio && (samples.Count - lastSignal >= TranscriptClock.SampleRate * 0.6 || samples.Count >= TranscriptClock.SampleRate * 30);
    public TimeSpan Watermark => HasAudio ? Start : Through;

    public void Add(TranscriptAudioWindow window)
    {
        const int frame = TranscriptClock.SampleRate / 50, padding = TranscriptClock.SampleRate / 10;
        int first = -1, last = 0;
        for (int offset = 0; offset < window.Samples.Length; offset += frame)
        {
            int count = Math.Min(frame, window.Samples.Length - offset);
            double energy = 0;
            for (int j = offset; j < offset + count; j++) energy += window.Samples[j] * window.Samples[j];
            if (energy / count < 0.000001) continue;
            if (first < 0) first = offset;
            last = offset + count;
        }
        Through = window.Start + TimeSpan.FromSeconds(window.Samples.Length / (double)TranscriptClock.SampleRate);
        if (!HasAudio)
        {
            if (first < 0) return;
            int trim = Math.Max(0, first - padding);
            Start = window.Start + TimeSpan.FromSeconds(trim / (double)TranscriptClock.SampleRate);
            samples.AddRange(window.Samples.AsSpan(trim).ToArray());
            lastSignal = last - trim;
        }
        else
        {
            int expected = (int)Math.Round((window.Start - Start).TotalSeconds * TranscriptClock.SampleRate);
            if (expected != samples.Count) throw new InvalidOperationException("The live audio timeline is not continuous.");
            if (last > 0) lastSignal = samples.Count + last;
            samples.AddRange(window.Samples);
        }
    }
    public float[] Snapshot()
    {
        int count = Math.Min(samples.Count, lastSignal + TranscriptClock.SampleRate / 10);
        return samples.GetRange(0, count).ToArray();
    }
    public void Clear() { samples.Clear(); lastSignal = 0; }
}