using ElkaVoicePlay.Shared;

namespace ElkaVoicePlay.Core;

public static class SpeakerEvidence
{
    public static IReadOnlyList<(int First, int Last)> CleanSamples(SpeakerTurn turn, IEnumerable<SpeakerTurn> others, int sampleCount)
    {
        int first = Math.Clamp((int)Math.Round(turn.Start.TotalSeconds * TranscriptClock.SampleRate), 0, sampleCount);
        int last = Math.Clamp((int)Math.Round(turn.End.TotalSeconds * TranscriptClock.SampleRate), first, sampleCount);
        var spans = new List<(int First, int Last)>();
        foreach (var other in others.Where(t => t.LocalSpeaker != turn.LocalSpeaker && t.Start < turn.End && t.End > turn.Start).OrderBy(t => t.Start))
        {
            int a = Math.Clamp((int)Math.Round(other.Start.TotalSeconds * TranscriptClock.SampleRate), first, last);
            int b = Math.Clamp((int)Math.Round(other.End.TotalSeconds * TranscriptClock.SampleRate), first, last);
            if (a > first) spans.Add((first, a)); first = Math.Max(first, b);
        }
        if (last > first) spans.Add((first, last));
        return spans;
    }
    public static string? ContinuingLabel(IEnumerable<SpeakerTurn> current, IEnumerable<SpeakerTurn> previous)
    {
        var turns = current.ToArray();
        var overlap = previous.Where(t => t.Label.StartsWith("Speaker ", StringComparison.Ordinal))
            .GroupBy(t => t.Label).Select(g => (Label: g.Key, Seconds: g.Sum(t => turns.Sum(s =>
                Math.Max(0, Math.Min(t.End.TotalSeconds, s.End.TotalSeconds) - Math.Max(t.Start.TotalSeconds, s.Start.TotalSeconds))))))
            .Where(x => x.Seconds > 0).OrderByDescending(x => x.Seconds).ToArray();
        return overlap.Length > 0 && overlap[0].Seconds >= 1 && overlap[0].Seconds >= overlap.Sum(x => x.Seconds) * 0.8 ? overlap[0].Label : null;
    }
}
