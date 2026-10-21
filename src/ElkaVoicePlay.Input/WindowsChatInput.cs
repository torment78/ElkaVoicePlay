using System.Runtime.InteropServices;
using ElkaVoicePlay.Shared;

namespace ElkaVoicePlay.Input;

public sealed class WindowsChatInput(Func<WindowTarget, bool> isCurrent) : IChatInput
{
    public bool IsCurrent(WindowTarget target) => isCurrent(target);
    public async Task<IKeyboardSendGuard> BlockKeyboardAsync(CancellationToken token) => await KeyboardSendGuard.StartAsync(token);
    public void EnsureModifiersReleased()
    {
        if (new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C }.Any(k => (NativeMethods.GetAsyncKeyState(k) & 0x8000) != 0))
            throw new InvalidOperationException("Release Shift, Ctrl, Alt and Windows keys before text delivery. The message is kept below.");
    }
    private void Check(WindowTarget target, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!IsCurrent(target)) throw new InvalidOperationException("Focus changed. Input stopped; check the chat before retrying.");
        EnsureModifiersReleased();
    }
    public async Task TapAsync(WindowTarget target, InputBinding binding, int durationMs, CancellationToken token)
    {
        Check(target, token);
        int vk = binding.Kind == InputKind.Keyboard ? binding.Code : binding.Code switch { 1 => 1, 2 => 2, 3 => 4, 4 => 5, _ => 6 };
        if ((NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0) throw new InvalidOperationException($"Release {binding} before sending chat.");
        bool down = false;
        try
        {
            Send([ButtonEvent(binding, false)]); down = true;
            await Task.Delay(durationMs, token);
        }
        finally { if (down) Send([ButtonEvent(binding, true)]); }
    }
    public async Task TypeAsync(WindowTarget target, string text, int delayMs, CancellationToken token)
    {
        foreach (var rune in text.EnumerateRunes())
        {
            Check(target, token);
            var events = UnicodeEvents(rune.ToString());
            uint count = NativeMethods.SendInput((uint)events.Length, events, Marshal.SizeOf<NativeMethods.NativeInput>());
            if (count != events.Length)
            {
                // Never retry text after a partial send. Release only an accepted unmatched key-down.
                if (count % 2 == 1) NativeMethods.SendInput(1, [events[count]], Marshal.SizeOf<NativeMethods.NativeInput>());
                throw new InvalidOperationException("Windows accepted only part of the text. Check the chat before retrying; automatic send was stopped.");
            }
            if (delayMs > 0) await Task.Delay(delayMs, token);
        }
    }
    internal static NativeMethods.NativeInput[] UnicodeEvents(string text) => text.SelectMany(c => new[]
    {
        new NativeMethods.NativeInput { Type = 1, Keyboard = new() { Scan = c, Flags = 4 } },
        new NativeMethods.NativeInput { Type = 1, Keyboard = new() { Scan = c, Flags = 6 } }
    }).ToArray();
    internal static NativeMethods.NativeInput ButtonEvent(InputBinding binding, bool up)
    {
        if (binding.Kind == InputKind.Mouse)
        {
            uint flags = binding.Code switch { 1 => up ? 4u : 2u, 2 => up ? 16u : 8u, 3 => up ? 64u : 32u, _ => up ? 256u : 128u };
            return new() { Type = 0, Mouse = new() { Flags = flags, Data = binding.Code >= 4 ? (uint)binding.Code - 3 : 0 } };
        }
        uint scan = NativeMethods.MapVirtualKey((uint)binding.Code, 4);
        if ((scan & 0xFF00) == 0xE100) scan = 0; // Pause has a multi-byte scan sequence; use its virtual key.
        bool extended = (scan & 0xFF00) == 0xE000;
        return new() { Type = 1, Keyboard = new() { Key = scan == 0 ? (ushort)binding.Code : (ushort)0,
            Scan = (ushort)(scan & 0xFF), Flags = (scan == 0 ? 0u : 8u) | (up ? 2u : 0u) | (extended ? 1u : 0u) } };
    }
    private static void Send(NativeMethods.NativeInput[] events)
    {
        if (NativeMethods.SendInput((uint)events.Length, events, Marshal.SizeOf<NativeMethods.NativeInput>()) != events.Length)
            throw new InvalidOperationException("Windows blocked simulated input. Run VoicePlay and the target at the same privilege level. Some games do not support this input method.");
    }
}
