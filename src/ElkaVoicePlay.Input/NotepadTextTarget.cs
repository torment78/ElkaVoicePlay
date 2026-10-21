using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using ElkaVoicePlay.Shared;

namespace ElkaVoicePlay.Input;

/// <summary>Phase 1 permits only the focused document editor of a foreground Notepad process.</summary>
public sealed class NotepadTextTarget : ITextTarget, IDisposable
{
    private readonly NativeMethods.WinEventProc callback;
    private readonly nint focusHook;
    private readonly EditorTextInserter inserter;
    private long focusRevision;

    public NotepadTextTarget()
    {
        callback = (_, _, _, _, _, _, _) => Interlocked.Increment(ref focusRevision);
        focusHook = NativeMethods.SetWinEventHook(3, 3, 0, callback, 0, 0, 0);
        if (focusHook == 0) throw new InvalidOperationException("Could not install foreground-window protection.");
        inserter = new(IsCurrent, ReplaceSelection);
    }

    public WindowTarget? Capture()
    {
        var window = NativeMethods.GetForegroundWindow();
        if (window == 0 || !ClassName(window).Equals("Notepad", StringComparison.OrdinalIgnoreCase)) return null;
        uint thread = NativeMethods.GetWindowThreadProcessId(window, out uint pid);
        var info = new NativeMethods.GuiThreadInfo { Size = (uint)Marshal.SizeOf<NativeMethods.GuiThreadInfo>() };
        if (!NativeMethods.GetGUIThreadInfo(thread, ref info) || info.Focus == 0) return null;
        if (NativeMethods.GetAncestor(info.Focus, 2) != window || !IsDocumentEditorClass(ClassName(info.Focus))) return null;
        NativeMethods.GetWindowThreadProcessId(info.Focus, out uint editorPid);
        if (editorPid != pid) return null;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            if (!string.Equals(process.ProcessName, "notepad", StringComparison.OrdinalIgnoreCase)) return null;
            return new(window, (int)pid, process.StartTime.ToUniversalTime().Ticks, process.ProcessName,
                Interlocked.Read(ref focusRevision), info.Focus);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception) { return null; }
    }

    public bool IsCurrent(WindowTarget target)
    {
        if (NativeMethods.GetForegroundWindow() != target.Handle || Interlocked.Read(ref focusRevision) != target.FocusRevision) return false;
        var current = Capture();
        return current != null && current.ProcessId == target.ProcessId && current.ProcessStartTicks == target.ProcessStartTicks
            && current.EditorHandle == target.EditorHandle;
    }

    public Task InsertAsync(WindowTarget target, string text, CancellationToken cancellationToken) => inserter.InsertAsync(target, text, cancellationToken);

    private static void ReplaceSelection(nint editor, string text)
    {
        // A single synchronous Unicode edit. No VK_PACKET stream, clipboard, key events or focus stealing.
        // Windows marshals this system message's string across processes. TRUE preserves Ctrl+Z undo.
        // A timeout is ambiguous, so never retry automatically.
        const uint EmReplaceSel = 0x00C2;
        const uint AbortIfHungAndErrorOnExit = 0x0002 | 0x0020;
        if (NativeMethods.SendTextMessage(editor, EmReplaceSel, 1, text, AbortIfHungAndErrorOnExit, 2000, out _) == 0)
            throw new InvalidOperationException("Notepad did not acknowledge insertion. Check its document before retrying; the text may already be present. Run both apps at the same privilege level.");
    }

    internal static bool IsDocumentEditorClass(string name) => name.Equals("Edit", StringComparison.OrdinalIgnoreCase)
        || name.Equals("RichEditD2DPT", StringComparison.OrdinalIgnoreCase)
        || name.Equals("RichEditD2D", StringComparison.OrdinalIgnoreCase)
        || name.Equals("RICHEDIT50W", StringComparison.OrdinalIgnoreCase)
        || name.Equals("RichEdit20W", StringComparison.OrdinalIgnoreCase);

    private static string ClassName(nint window)
    {
        var name = new StringBuilder(256);
        return NativeMethods.GetClassName(window, name, name.Capacity) == 0 ? "" : name.ToString();
    }

    public void Dispose() { NativeMethods.UnhookWinEvent(focusHook); GC.KeepAlive(callback); }
}
