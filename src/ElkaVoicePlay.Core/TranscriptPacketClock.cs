namespace ElkaVoicePlay.Core;

/// <summary>Counts delivered PCM frames; QPC anchors the stream and actual silent gaps, not each jittery packet.</summary>
public sealed class TranscriptPacketClock
{
    private bool anchored;
    public long NextSample { get; private set; }
    public long Place(long reportedSample, int frames, long committedSample)
    {
        if (frames <= 0) throw new ArgumentOutOfRangeException(nameof(frames));
        // Converted-device QPC can wobble several milliseconds despite contiguous PCM.
        // A forward gap over 50 ms is real elapsed silence; preserve it without dropping PCM.
        long start = !anchored || reportedSample - NextSample > 800 ? reportedSample : NextSample;
        start = Math.Max(start, committedSample);
        NextSample = checked(start + frames);
        anchored = true;
        return start;
    }
}
