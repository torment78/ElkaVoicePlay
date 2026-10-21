using System.ComponentModel;
using System.Diagnostics;
using ElkaVoicePlay.Core;
using ElkaVoicePlay.Shared;

namespace ElkaVoicePlay.Input;

public sealed class ProfileTextTarget : ITextTarget, IDisposable
{
    private readonly Func<AppSettings> settings;
    private readonly NotepadTextTarget notepad;
    private readonly NativeMethods.WinEventProc callback;
    private readonly nint focusHook;
    private long revision;
    private readonly ChatSequence sequence;
    public event Action<SessionStatus>? StatusChanged;
    public ProfileTextTarget(Func<AppSettings> settings, NotepadTextTarget notepad)
    {
        this.settings = settings; this.notepad = notepad;
        callback = (_, _, _, _, _, _, _) => Interlocked.Increment(ref revision);
        focusHook = NativeMethods.SetWinEventHook(3, 3, 0, callback, 0, 0, 0);
        if (focusHook == 0) throw new InvalidOperationException("Could not install foreground-window protection.");
        sequence = new(new WindowsChatInput(IsCurrent));
        sequence.StatusChanged += value => StatusChanged?.Invoke(value);
    }
    public static bool IsForeground(nint window) => window != 0 && NativeMethods.GetForegroundWindow() == window;
    public WindowTarget? Capture()
    {
        nint window = NativeMethods.GetForegroundWindow();
        NativeMethods.GetWindowThreadProcessId(window, out uint pid);
        if (window == 0 || pid == Environment.ProcessId) return null;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            string name = process.ProcessName;
            if (name.Equals("explorer", StringComparison.OrdinalIgnoreCase) || name.Equals("ShellExperienceHost", StringComparison.OrdinalIgnoreCase)) return null;
            string? path = null;
            try { path = process.MainModule?.FileName; } catch (Win32Exception) { }
            var profile = ProfileSelector.Select(settings(), name, path);
            if (profile == null) return null;
            if (profile.Delivery == TextDelivery.NotepadEditor)
            {
                var document = notepad.Capture();
                return document == null ? null : document with { Profile = profile };
            }
            // An EXE link also limits manual use to that executable. Unlinked manual profiles use the focused app.
            if (!string.IsNullOrWhiteSpace(profile.Executable) && !ProfileSelector.Matches(profile, name, path)) return null;
            return new(window, (int)pid, process.StartTime.ToUniversalTime().Ticks, name,
                Interlocked.Read(ref revision), Profile: profile);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception) { return null; }
    }
    public bool IsCurrent(WindowTarget target)
    {
        if (target.Profile?.Delivery == TextDelivery.NotepadEditor) return notepad.IsCurrent(target);
        if (!IsForeground(target.Handle) || target.FocusRevision != Interlocked.Read(ref revision)) return false;
        NativeMethods.GetWindowThreadProcessId(target.Handle, out uint pid);
        if (pid != target.ProcessId) return false;
        try { using var p = Process.GetProcessById(target.ProcessId); return p.StartTime.ToUniversalTime().Ticks == target.ProcessStartTicks; }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception) { return false; }
    }
    public Task InsertAsync(WindowTarget target, string text, CancellationToken token) => target.Profile?.Delivery == TextDelivery.NotepadEditor
        ? notepad.InsertAsync(target, text, token) : sequence.RunAsync(target, text, token);
    public async Task<string> RunCommandAsync(ChatCommandRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var destination = Capture() ?? throw new InvalidOperationException("No valid destination for this profile. No command was sent.");
        var profile = destination.Profile!;
        if (!profile.ChatCommand.Accepts(profile, request))
            throw new InvalidOperationException("This button or double-tap timing does not match the focused profile's enabled chat command. Nothing was sent.");
        await new ChatCommandSequence(new WindowsChatInput(IsCurrent)).RunAsync(destination, token);
        return profile.Name;
    }
    public void Dispose() { NativeMethods.UnhookWinEvent(focusHook); GC.KeepAlive(callback); }
}
