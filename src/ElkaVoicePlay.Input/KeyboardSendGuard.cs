using System.ComponentModel;
using System.Runtime.InteropServices;
using ElkaVoicePlay.Shared;

namespace ElkaVoicePlay.Input;

/// <summary>Exists only during delivery. Never polls the foreground or installs a mouse hook.</summary>
internal sealed class KeyboardSendGuard : IKeyboardSendGuard
{
    private const int MaximumMilliseconds = 15_000;
    private readonly KeyboardBlockFilter filter = new();
    private readonly CancellationTokenSource cancellation;
    private readonly CancellationTokenRegistration registration;
    private readonly NativeMethods.HookProc callback;
    private readonly HookThread thread;
    private readonly Timer watchdog;
    private nint handle;
    private long deadline;
    private int timedOut;
    public CancellationToken Token => cancellation.Token;
    public bool TimedOut => Volatile.Read(ref timedOut) != 0;

    private KeyboardSendGuard(CancellationToken token)
    {
        cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        registration = cancellation.Token.Register(filter.Disable);
        callback = OnKeyboard;
        thread = new HookThread(Unhook);
        watchdog = new Timer(_ => Stop(timedOut: true), null, Timeout.Infinite, Timeout.Infinite);
    }
    internal static async Task<KeyboardSendGuard> StartAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var guard = new KeyboardSendGuard(token);
        try
        {
            await guard.thread.InvokeAsync(() =>
            {
                token.ThrowIfCancellationRequested();
                // Preserve releases for keys the game already saw down, including held movement keys.
                for (int key = 8; key < 256; key++)
                    if ((NativeMethods.GetAsyncKeyState(key) & 0x8000) != 0) guard.filter.SeedHeld(key);
                guard.deadline = Environment.TickCount64 + MaximumMilliseconds;
                guard.handle = NativeMethods.SetWindowsHookEx(13, guard.callback, NativeMethods.GetModuleHandle(null), 0);
                if (guard.handle == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Keyboard protection could not start. Nothing was sent.");
                guard.watchdog.Change(MaximumMilliseconds, Timeout.Infinite);
            }).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return guard;
        }
        catch { await guard.DisposeAsync(); throw; }
    }
    private nint OnKeyboard(int code, nint message, nint data)
    {
        if (code >= 0 && (message == 0x100 || message == 0x104 || message == 0x101 || message == 0x105))
        {
            if (filter.Enabled && Environment.TickCount64 >= deadline) Stop(timedOut: true);
            var item = Marshal.PtrToStructure<NativeMethods.KeyboardHookData>(data);
            bool down = message == 0x100 || message == 0x104;
            if (filter.Block((int)item.Key, down, (item.Flags & 0x10) != 0))
            {
                // CancelAsync marks the token immediately but keeps subscribers off the native callback.
                if (down && item.Key == 0x1B) Stop(timedOut: false);
                return 1;
            }
        }
        return NativeMethods.CallNextHookEx(handle, code, message, data);
    }
    private void Stop(bool timedOut)
    {
        if (!filter.DisableOnce()) return;
        if (timedOut) Volatile.Write(ref this.timedOut, 1);
        _ = cancellation.CancelAsync();
    }
    private void Unhook()
    {
        if (handle != 0) { NativeMethods.UnhookWindowsHookEx(handle); handle = 0; }
    }
    public async ValueTask DisposeAsync()
    {
        filter.Disable();
        await watchdog.DisposeAsync().ConfigureAwait(false);
        registration.Dispose();
        thread.Dispose();
        cancellation.Dispose();
        GC.KeepAlive(callback);
    }
}

/// <summary>Physical key decisions on the hook thread; cancellation can only disable the filter.</summary>
internal sealed class KeyboardBlockFilter
{
    private readonly bool[] heldBefore = new bool[256];
    private int enabled = 1;
    internal bool Enabled => Volatile.Read(ref enabled) != 0;
    internal void SeedHeld(int key) { if ((uint)key < 256) heldBefore[key] = true; }
    internal void Disable() => DisableOnce();
    internal bool DisableOnce() => Interlocked.Exchange(ref enabled, 0) != 0;
    internal bool Block(int key, bool down, bool injected)
    {
        if (!Enabled || injected) return false;
        if (!down && (uint)key < 256 && heldBefore[key])
        {
            heldBefore[key] = false;
            return false;
        }
        return true;
    }
}
