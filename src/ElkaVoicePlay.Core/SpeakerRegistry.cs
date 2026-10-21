namespace ElkaVoicePlay.Core;

/// <summary>Session-only anonymous voice centroids. A separate registry is used for each input.</summary>
public sealed class SpeakerRegistry
{
    private readonly List<float[]> voices = [];
    public string Match(float[] vector, string? continuingLabel = null)
    {
        if (vector.Length == 0 || vector.Any(v => !float.IsFinite(v))) return "Unknown speaker";
        double norm = Math.Sqrt(vector.Sum(v => (double)v * v));
        if (norm < 1e-8) return "Unknown speaker";
        var normalized = vector.Select(v => (float)(v / norm)).ToArray();
        if (voices.Count > 0 && voices[0].Length != vector.Length) return "Unknown speaker";
        // Overlapping audio context can establish continuity even when a very short voice sample changes its embedding.
        if (continuingLabel?.StartsWith("Speaker ", StringComparison.Ordinal) == true &&
            int.TryParse(continuingLabel.AsSpan(8), out int known) && known > 0 && known <= voices.Count)
        {
            voices[known - 1] = normalized;
            return continuingLabel;
        }
        var ranked = voices.Select((v, i) => (Index: i, Score: v.Zip(normalized, (a, b) => (double)a * b).Sum())).OrderByDescending(v => v.Score).ToArray();
        if (ranked.Length > 0 && ranked[0].Score >= 0.55)
        {
            if (ranked.Length > 1 && ranked[0].Score - ranked[1].Score < 0.05) return "Unknown speaker";
            int index = ranked[0].Index;
            // Only adapt a confident match, so ambiguous short turns do not contaminate a voice.
            if (ranked[0].Score >= 0.7)
            {
                var updated = voices[index].Zip(normalized, (a, b) => a * 0.9f + b * 0.1f).ToArray();
                double length = Math.Sqrt(updated.Sum(v => (double)v * v));
                voices[index] = updated.Select(v => (float)(v / length)).ToArray();
            }
            return $"Speaker {index + 1}";
        }
        if (voices.Count >= 32) return "Unknown speaker";
        voices.Add(normalized);
        return $"Speaker {voices.Count}";
    }
}
