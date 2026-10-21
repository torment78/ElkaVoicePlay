using ElkaVoicePlay.Shared;

namespace ElkaVoicePlay.Core;

public static class TranscriptWordLines
{
    // Decode the complete phrase once, then attach voices to word boundaries. Tiny overlap
    // intervals never become standalone ASR requests that lose context or hallucinate text.
    public static IReadOnlyList<TranscriptLine> Build(IEnumerable<SpeechSegment> words, IReadOnlyList<SpeakerTurn> turns,
        int slot, string source, TimeSpan offset, TimeSpan duration)
    {
        var result = new List<TranscriptLine>();
        string text = "", label = ""; TimeSpan begin = default, end = default;
        void Flush()
        {
            string normalized = DictationController.Normalize(text);
            if (normalized.Length > 0) result.Add(new(slot, source, offset + begin, offset + end, normalized, label));
            text = "";
        }
        foreach (var word in words)
        {
            if (string.IsNullOrWhiteSpace(word.Text)) continue;
            var start = TimeSpan.FromTicks(Math.Clamp(word.Start.Ticks, 0, duration.Ticks));
            var finish = TimeSpan.FromTicks(Math.Clamp(word.End.Ticks, start.Ticks, duration.Ticks));
            var voice = turns.OrderByDescending(t => Math.Max(0, Math.Min(t.End.Ticks, finish.Ticks) - Math.Max(t.Start.Ticks, start.Ticks)))
                .ThenBy(t => Math.Abs((t.Start - start).Ticks)).FirstOrDefault();
            string next = voice?.Label ?? "Unknown speaker";
            if (text.Length > 0 && (next != label || text.Length + word.Text.Length > 110 || start - end > TimeSpan.FromSeconds(1))) Flush();
            if (text.Length == 0) { begin = start; label = next; }
            text += word.Text; end = finish > end || text == word.Text ? finish : end;
            if (word.Text.TrimEnd().EndsWith('.') || word.Text.TrimEnd().EndsWith('!') || word.Text.TrimEnd().EndsWith('?') || word.Text.TrimEnd().EndsWith('。')) Flush();
        }
        Flush(); return result;
    }
}
