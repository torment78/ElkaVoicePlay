using System.Runtime.InteropServices;
using ElkaVoicePlay.Core;
using ElkaVoicePlay.Shared;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace ElkaVoicePlay.Audio;

public sealed class TranscriptCapture : ITranscriptCapture
{
    public static IReadOnlyList<AudioEndpointChoice> Enumerate()
    {
        using var enumerator = new MMDeviceEnumerator();
        var choices = new List<AudioEndpointChoice>();
        foreach (var kind in new[] { TranscriptInputKind.Microphone, TranscriptInputKind.Loopback })
        {
            var flow = kind == TranscriptInputKind.Loopback ? DataFlow.Render : DataFlow.Capture;
            string? defaultId = null;
            try { using var device = enumerator.GetDefaultAudioEndpoint(flow, Role.Console); defaultId = device.ID; }
            catch (COMException) { }
            foreach (var device in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
                using (device) choices.Add(new(device.ID, device.FriendlyName, kind, device.ID == defaultId));
        }
        return choices;
    }
    public Task RunAsync(TranscriptSource source, TranscriptClock clock, Action<TranscriptAudioWindow> onWindow,
        Action<float> onLevel, CancellationToken stop) => Task.Factory.StartNew(
            () => Capture(source, clock, onWindow, onLevel, stop), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    private static void Capture(TranscriptSource source, TranscriptClock clock, Action<TranscriptAudioWindow> onWindow,
        Action<float> onLevel, CancellationToken stop)
    {
        if (stop.IsCancellationRequested) return;
        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDevice(source.DeviceId!);
        var expectedFlow = source.Kind == TranscriptInputKind.Loopback ? DataFlow.Render : DataFlow.Capture;
        if (device.State != DeviceState.Active || device.DataFlow != expectedFlow)
            throw new InvalidOperationException("The selected device is unavailable or has the wrong input type. Refresh devices.");
        using var client = device.AudioClient;
        var flags = AudioClientStreamFlags.AutoConvertPcm | AudioClientStreamFlags.SrcDefaultQuality;
        if (source.Kind == TranscriptInputKind.Loopback) flags |= AudioClientStreamFlags.Loopback;
        client.Initialize(AudioClientShareMode.Shared, flags, 2_000_000, 0,
            WaveFormat.CreateIeeeFloatWaveFormat(TranscriptClock.SampleRate, 1), Guid.Empty);
        var packets = client.AudioCaptureClient; // Owned and disposed by AudioClient.
        var buffer = new float[client.BufferSize];
        var timeline = new TranscriptTimeline(onWindow, TranscriptClock.SampleRate / 2);
        var packetClock = new TranscriptPacketClock();
        bool receivedPacket = false;
        client.Start();
        try
        {
            while (!stop.IsCancellationRequested)
            {
                ReadPackets();
                // Loopback may deliver no packets during silence. Advance the shared clock without compressing gaps.
                timeline.Advance(Math.Max(0, clock.ElapsedSamples - TranscriptClock.SampleRate / 2));
                stop.WaitHandle.WaitOne(20);
            }
            ReadPackets();
            timeline.Finish(Math.Max(clock.ElapsedSamples, packetClock.NextSample));
        }
        finally { client.Stop(); onLevel(0); }

        void ReadPackets()
        {
            while (packets.GetNextPacketSize() > 0)
            {
                nint data = packets.GetBuffer(out int frames, out var packetFlags, out _, out long qpc100ns);
                try
                {
                    if (frames == 0) continue;
                    if ((packetFlags & AudioClientBufferFlags.TimestampError) != 0 || qpc100ns <= 0)
                        throw new InvalidOperationException("The audio device reported an unreliable timestamp. Recording stopped to preserve alignment.");
                    if (receivedPacket && (packetFlags & AudioClientBufferFlags.DataDiscontinuity) != 0)
                        throw new InvalidOperationException("The audio device lost samples. Completed text is saved; check the device before restarting.");
                    receivedPacket = true;
                    if (frames > buffer.Length) throw new InvalidOperationException("Unexpected audio packet size.");
                    if ((packetFlags & AudioClientBufferFlags.Silent) != 0) Array.Clear(buffer, 0, frames);
                    else Marshal.Copy(data, buffer, 0, frames);
                    float peak = 0;
                    for (int i = 0; i < frames; i++)
                    {
                        if (!float.IsFinite(buffer[i])) buffer[i] = 0;
                        peak = Math.Max(peak, Math.Abs(buffer[i]));
                    }
                    onLevel(Math.Clamp(peak, 0, 1));
                    timeline.Add(packetClock.Place(clock.SampleAt(qpc100ns), frames, timeline.CommittedSamples), buffer.AsSpan(0, frames));
                }
                finally { if (frames > 0) packets.ReleaseBuffer(frames); }
            }
        }
    }
}
