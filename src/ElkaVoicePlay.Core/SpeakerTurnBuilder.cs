using ElkaVoicePlay.Shared;

namespace ElkaVoicePlay.Core;

public static class SpeakerTurnBuilder
{
    // Partition every sample exactly once. Overlapping voices are left explicitly unattributed.
    public static IReadOnlyList<SpeakerTurn> Build(IEnumerable<SpeakerTurn> detected, TimeSpan duration)
    {
        var turns = detected.Where(t => t.End > t.Start && t.End > TimeSpan.Zero && t.Start < duration)
            .Select(t => t with { Start = t.Start < TimeSpan.Zero ? TimeSpan.Zero : t.Start, End = t.End > duration ? duration : t.End }).ToArray();
        if (turns.Length == 0) return [new(TimeSpan.Zero, duration, "Unknown speaker")];
        var boundaries = turns.SelectMany(t => new[] { t.Start, t.End }).Append(TimeSpan.Zero).Append(duration).Distinct().Order().ToArray();
        var result = new List<SpeakerTurn>();
        for (int i = 0; i < boundaries.Length - 1; i++)
        {
            var start = boundaries[i]; var end = boundaries[i + 1];
            var active = turns.Where(t => t.Start < end && t.End > start).DistinctBy(t => t.LocalSpeaker is int id ? "cluster:" + id : t.Label).Select(t => t.Label).ToArray();
            // A silent gap belongs to the nearest turn; it does not become a separate Whisper request.
            string label = active.Length > 1 ? "Overlapping speakers" : active.Length == 1 ? active[0]
                : turns.OrderBy(t => Math.Min(Math.Abs((start - t.End).Ticks), Math.Abs((end - t.Start).Ticks))).First().Label;
            if (result.Count > 0 && result[^1].Label == label) result[^1] = result[^1] with { End = end };
            else result.Add(new(start, end, label));
        }
        return result;
    }
}
