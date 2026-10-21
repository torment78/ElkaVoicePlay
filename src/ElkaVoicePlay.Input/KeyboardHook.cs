using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using ElkaVoicePlay.Shared;

namespace ElkaVoicePlay.Input;

/// <summary>Low-level hooks own a dedicated message-pump thread, independent of WPF and recognition.</summary>
public sealed class KeyboardHook : IDisposable
{
    private readonly NativeMethods.HookProc keyboardCallback, mouseCallback;
    private readonly InputRouter router = new();
    private readonly TalkGesture gesture = new();
    private readonly Timer gestureTimer;
    private readonly HookThread thread;
    private readonly Channel<Action> notifications = Channel.CreateUnbounded<Action>(
        new UnboundedChannelOptions { SingleReader = true, AllowSynchronousContinuations = false });
    private HookConfiguration configuration = HookConfiguration.Disabled;
    private nint keyboardHook, mouseHook;
    private int held, learning, disposed;
    private bool callbackReconcilePending;
    private TalkTrigger? routedControl;
    private InputBinding? routedButton;
    public bool IsHeld => Volatile.Read(ref held) != 0;
    public bool IsLearning => Volatile.Read(ref learning) != 0;
    public event Action<bool>? HoldChanged;
    public event Action<ChatCommandRequest>? CommandRequested;
    public event Action? CancelRequested;
    public event Action<InputBinding?>? Learned;
    public event Action<string>? Failed;

    public KeyboardHook()
    {
        keyboardCallback = OnKeyboard; mouseCallback = OnMouse;
        router.HoldChanged += down =>
        {
            Volatile.Write(ref held, down ? 1 : 0);
            if (down) gesture.Press(routedControl!, routedButton!, Environment.TickCount64);
            else
            {
                var config = Volatile.Read(ref configuration);
                if (!config.Enabled || !config.AllowRecording) gesture.CancelPending();
                gesture.Release(Environment.TickCount64);
            }
            ScheduleGesture();
            callbackReconcilePending = true;
        };
        gesture.HoldChanged += down => Notify(() => HoldChanged?.Invoke(down));
        gesture.Command += request => Notify(() => CommandRequested?.Invoke(request));
        router.Learned += value =>
        {
            Volatile.Write(ref learning, 0);
            Notify(() => Learned?.Invoke(value));
            callbackReconcilePending = true;
        };
        thread = new HookThread(UnhookAll);
        gestureTimer = new Timer(_ => Post(AdvanceGesture), null, Timeout.Infinite, Timeout.Infinite);
        _ = Task.Run(DeliverNotifications);
    }

    public void Configure(HookConfiguration value)
    {
        if (Volatile.Read(ref disposed) != 0) return;
        var old = Volatile.Read(ref configuration);
        if (old.Enabled == value.Enabled && old.AllowRecording == value.AllowRecording && old.Triggers.SequenceEqual(value.Triggers)) return;
        // Snapshot is immutable to the callback. No UI delegates, controller locks or foreground calls.
        Volatile.Write(ref configuration, value with { Triggers = value.Triggers.ToArray() });
        bool resetGesture = !value.Enabled || !value.AllowRecording || !old.Triggers.SequenceEqual(value.Triggers);
        Post(() => { if (resetGesture) { gesture.CancelPending(); ScheduleGesture(); } ReconcileHooks(); });
    }
    public void BeginLearning()
    {
        if (IsHeld || Interlocked.CompareExchange(ref learning, 1, 0) != 0)
            throw new InvalidOperationException("Release the current button first.");
        Post(() =>
        {
            if (router.IsHeld) { Volatile.Write(ref learning, 0); Notify(() => Learned?.Invoke(null)); return; }
            gesture.CancelPending(); ScheduleGesture();
            router.BeginLearning();
            ReconcileHooks();
        });
    }
    public void CancelLearning() => Post(() => { router.CancelLearning(); ReconcileHooks(); });
    public void CancelPendingGesture() => Post(() => { gesture.CancelPending(); ScheduleGesture(); });
    private void AdvanceGesture()
    {
        var config = Volatile.Read(ref configuration);
        if (!config.Enabled || !config.AllowRecording || router.IsLearning) gesture.CancelPending();
        gesture.Advance(Environment.TickCount64); ScheduleGesture();
    }
    private void ScheduleGesture()
    {
        // A one-shot timer exists only while distinguishing a short tap from a hold.
        int delay = gesture.DueAt is { } due ? (int)Math.Clamp(due - Environment.TickCount64, 1, 1000) : Timeout.Infinite;
        gestureTimer.Change(delay, Timeout.Infinite);
    }

    private void Post(Action work)
    {
        if (Volatile.Read(ref disposed) != 0) return;
        _ = Observe(thread.InvokeAsync(work));
    }
    private async Task Observe(Task work)
    {
        try { await work.ConfigureAwait(false); }
        catch (Exception ex)
        {
            if (Volatile.Read(ref disposed) != 0) return;
            Volatile.Write(ref learning, 0);
            Notify(() => Failed?.Invoke(ex.Message));
        }
    }
    private void Notify(Action action) => notifications.Writer.TryWrite(action);
    private async Task DeliverNotifications()
    {
        await foreach (var action in notifications.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            if (Volatile.Read(ref disposed) != 0) continue;
            // Subscribers run off the hook thread, in order. UI congestion cannot block mouse delivery.
            try { action(); } catch (Exception) { /* Never unwind through a native input callback. */ }
        }
    }
    private void ReconcileHooks()
    {
        callbackReconcilePending = false;
        var config = Volatile.Read(ref configuration);
        bool keyboardNeeded = config.Enabled || router.IsLearning || router.IsHeld;
        bool mouseNeeded = config.NeedsMouseHook || router.IsLearning || router.IsMouseHeld;
        UpdateHook(ref keyboardHook, keyboardNeeded, 13, keyboardCallback, InputKind.Keyboard);
        UpdateHook(ref mouseHook, mouseNeeded, 14, mouseCallback, InputKind.Mouse);
    }
    private void UpdateHook(ref nint handle, bool needed, int kind, NativeMethods.HookProc callback, InputKind inputKind)
    {
        if (needed && handle == 0)
        {
            router.ClearPressed(inputKind);
            // A button already down when the hook is installed must be released before it can record.
            if (inputKind == InputKind.Keyboard)
            {
                for (int key = 8; key <= 254; key++)
                    if ((NativeMethods.GetAsyncKeyState(key) & 0x8000) != 0) router.SeedPressed(new(inputKind, key));
            }
            else
            {
                for (int button = 1; button <= 5; button++)
                {
                    int vk = button switch { 1 => 1, 2 => 2, 3 => 4, 4 => 5, _ => 6 };
                    if ((NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0) router.SeedPressed(new(inputKind, button));
                }
            }
            handle = NativeMethods.SetWindowsHookEx(kind, callback, NativeMethods.GetModuleHandle(null), 0);
            if (handle == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not install input controls.");
        }
        else if (!needed && handle != 0) { NativeMethods.UnhookWindowsHookEx(handle); handle = 0; router.ClearPressed(inputKind); }
    }
    private nint OnKeyboard(int code, nint message, nint data)
    {
        if (code >= 0 && (message == 0x100 || message == 0x104 || message == 0x101 || message == 0x105))
        {
            var item = Marshal.PtrToStructure<NativeMethods.KeyboardHookData>(data);
            if ((item.Flags & 0x10) == 0)
            {
                bool down = message == 0x100 || message == 0x104;
                var input = new InputBinding(InputKind.Keyboard, (int)item.Key);
                var config = Volatile.Read(ref configuration);
                if (!router.IsLearning && config.Enabled && item.Key == 0x1B && down && !config.Triggers.Any(t => t.Binding.Matches(input)))
                {
                    gesture.CancelPending(); ScheduleGesture();
                    Notify(() => CancelRequested?.Invoke());
                }
                if (Route(input, down, config)) return 1;
            }
        }
        return NativeMethods.CallNextHookEx(keyboardHook, code, message, data);
    }
    private nint OnMouse(int code, nint message, nint data)
    {
        // Movement/wheel traffic returns before reading lParam, allocating, routing or notifying anyone.
        if (TryReadMouseButton(code, message, data, out var value) && Route(value.Binding, value.Down, Volatile.Read(ref configuration))) return 1;
        return NativeMethods.CallNextHookEx(mouseHook, code, message, data);
    }
    internal static bool TryReadMouseButton(int code, nint message, nint data, out (InputBinding Binding, bool Down) value)
    {
        value = default;
        if (code < 0 || !IsMouseButtonMessage((int)message)) return false;
        var item = Marshal.PtrToStructure<NativeMethods.MouseHookData>(data);
        if ((item.Flags & 1) != 0) return false;
        if (DecodeMouse((int)message, item.MouseData) is not { } decoded) return false;
        value = decoded;
        return true;
    }
    internal static bool IsMouseButtonMessage(int message) => message is 0x201 or 0x202 or 0x204 or 0x205 or 0x207 or 0x208 or 0x20B or 0x20C;
    private bool Route(InputBinding input, bool down, HookConfiguration config)
    {
        var match = config.Triggers.FirstOrDefault(t => t.Binding.Matches(input));
        routedControl = match; routedButton = input;
        if (down && match == null && !router.IsHeld) { gesture.CancelPending(); ScheduleGesture(); }
        bool suppress = router.Process(input, down, match?.Binding ?? InputBinding.F9,
            config.Enabled && config.AllowRecording && match != null, match?.Suppress == true);
        // Hook installation/removal runs as posted work after returning to the Windows input chain.
        if (callbackReconcilePending) { callbackReconcilePending = false; Post(ReconcileHooks); }
        return suppress;
    }
    internal static (InputBinding Binding, bool Down)? DecodeMouse(int message, uint data) => message switch
    {
        0x201 or 0x202 => (new(InputKind.Mouse, 1), message == 0x201),
        0x204 or 0x205 => (new(InputKind.Mouse, 2), message == 0x204),
        0x207 or 0x208 => (new(InputKind.Mouse, 3), message == 0x207),
        0x20B or 0x20C when (data >> 16) is 1 or 2 => (new(InputKind.Mouse, (int)(data >> 16) + 3), message == 0x20B),
        _ => null
    };
    private void UnhookAll()
    {
        if (keyboardHook != 0) { NativeMethods.UnhookWindowsHookEx(keyboardHook); keyboardHook = 0; }
        if (mouseHook != 0) { NativeMethods.UnhookWindowsHookEx(mouseHook); mouseHook = 0; }
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        thread.Dispose();
        gestureTimer.Dispose();
        notifications.Writer.TryComplete();
        GC.KeepAlive(keyboardCallback); GC.KeepAlive(mouseCallback);
    }
}
