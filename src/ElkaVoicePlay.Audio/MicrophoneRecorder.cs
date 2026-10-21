using ElkaVoicePlay.Shared;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace ElkaVoicePlay.Audio;

public sealed class MicrophoneRecorder : IAudioRecorder
{
    public event Action<float>? LevelChanged;

    public static IReadOnlyList<MicrophoneInfo> Enumerate()
    {
        using var enumerator = new MMDeviceEnumerator();
        var result = new List<MicrophoneInfo> { new(null, "Windows default input") };
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
        {
            using (device) result.Add(new(device.ID, device.FriendlyName));
        }
        return result;
    }

    public Task<float[]> CaptureAsync(CaptureRequest request, Task released, CancellationToken cancellationToken) =>
        Task.Run(() => CaptureCoreAsync(request, released, cancellationToken), cancellationToken);

    private async Task<float[]> CaptureCoreAsync(CaptureRequest request, Task released, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (released.IsCompleted) return [];
        using var enumerator = new MMDeviceEnumerator();
        using var device = string.IsNullOrEmpty(request.DeviceId)
            ? enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console)
            : enumerator.GetDevice(request.DeviceId);
        if (device.DataFlow != DataFlow.Capture || device.State != DeviceState.Active)
            throw new InvalidOperationException("The selected microphone is unavailable. Refresh the input list.");
        using var capture = new WasapiCapture(device, false, 20)
        {
            // Shared-mode WASAPI performs conversion with AutoConvertPcm / SrcDefaultQuality.
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(16000, 1)
        };
        var samples = new float[16000 * Math.Clamp(request.MaximumSeconds, 1, 120)];
        int length = 0;
        var stopped = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var full = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = token.Register(() => cancelled.TrySetResult());
        using var timeout = new CancellationTokenSource();
        var maximum = Task.Delay(TimeSpan.FromSeconds(request.MaximumSeconds), timeout.Token);
        capture.DataAvailable += (_, args) =>
        {
            if (released.IsCompleted || token.IsCancellationRequested) return;
            int count = Math.Min(args.BytesRecorded / sizeof(float), samples.Length - length);
            Buffer.BlockCopy(args.Buffer, 0, samples, length * sizeof(float), count * sizeof(float));
            float peak = 0;
            for (int i = length; i < length + count; i++) peak = Math.Max(peak, Math.Abs(samples[i]));
            length += count;
            LevelChanged?.Invoke(Math.Clamp(peak, 0, 1));
            if (length == samples.Length) full.TrySetResult();
        };
        capture.RecordingStopped += (_, args) => stopped.TrySetResult(args.Exception);
        bool started = false;
        try
        {
            capture.StartRecording();
            started = true;
            var reason = await Task.WhenAny(released, cancelled.Task, stopped.Task, full.Task, maximum);
            capture.StopRecording();
            var captureError = await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));
            token.ThrowIfCancellationRequested();
            if (captureError != null) throw new InvalidOperationException("Microphone capture failed: " + captureError.Message, captureError);
            if (reason == full.Task || reason == maximum) throw new RecordingLimitException();
            if (reason == stopped.Task) throw new InvalidOperationException("Microphone stopped unexpectedly. Nothing was inserted.");
            return samples.AsSpan(0, length).ToArray();
        }
        finally
        {
            timeout.Cancel();
            if (started) capture.StopRecording();
            LevelChanged?.Invoke(0);
            // Disposing capture waits for its native capture thread and frees the audio client.
        }
    }
}
