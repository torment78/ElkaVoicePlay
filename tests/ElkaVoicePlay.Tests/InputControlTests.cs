using System.Runtime.InteropServices;
using ElkaVoicePlay.Input;
using ElkaVoicePlay.Shared;
using Xunit;

namespace ElkaVoicePlay.Tests;

public sealed class InputControlTests
{
    [Theory] [InlineData(InputKind.Keyboard, 0x41)] [InlineData(InputKind.Mouse, 4)]
    public void HoldRepeatsAndReleaseAfterDisableStayPaired(InputKind kind, int code)
    {
        var router = new InputRouter(); var key = new InputBinding(kind, code); var edges = new List<bool>();
        router.HoldChanged += edges.Add;
        Assert.True(router.Process(key, true, key, true, true));
        for (int i = 0; i < 100; i++) Assert.True(router.Process(key, true, key, false, false));
        Assert.True(router.Process(key, false, key, false, false));
        Assert.False(router.Process(key, false, key, false, false));
        Assert.Equal(new[] { true, false }, edges); Assert.False(router.IsHeld);
    }
    [Fact] public void HoldingBeforeActivationCannotStartOnARepeat()
    {
        var router = new InputRouter(); int count = 0; router.HoldChanged += _ => count++;
        router.Process(InputBinding.F9, true, InputBinding.F9, false, true);
        router.Process(InputBinding.F9, true, InputBinding.F9, true, true);
        router.Process(InputBinding.F9, false, InputBinding.F9, true, true);
        Assert.Equal(0, count);
        router.Process(InputBinding.F9, true, InputBinding.F9, true, true); Assert.Equal(1, count);
    }
    [Fact] public void LearnConsumesOneWholeMouseClickAndNeverRecords()
    {
        var router = new InputRouter(); var mouse = new InputBinding(InputKind.Mouse, 5);
        InputBinding? learned = null; int edges = 0;
        router.HoldChanged += _ => edges++; router.Learned += value => learned = value;
        router.BeginLearning();
        Assert.True(router.Process(mouse, true, mouse, true, true)); Assert.Null(learned);
        Assert.True(router.Process(mouse, false, mouse, true, true));
        Assert.Equal(mouse, learned); Assert.Equal(0, edges); Assert.False(router.IsLearning);
        Assert.True(router.Process(mouse, true, mouse, true, true)); Assert.Equal(1, edges);
    }
    [Fact] public void CancelLearningMidHoldStillConsumesRelease()
    {
        var router = new InputRouter(); InputBinding? learned = InputBinding.Enter;
        router.Learned += value => learned = value; router.BeginLearning();
        router.Process(InputBinding.F9, true, InputBinding.F9, true, true);
        router.CancelLearning(); Assert.True(router.IsLearning);
        Assert.True(router.Process(InputBinding.F9, false, InputBinding.F9, true, true));
        Assert.Null(learned); Assert.False(router.IsLearning);
    }
    [Fact] public void EscapeCancelsLearningAndGenericCtrlMatchesEitherSide()
    {
        var router = new InputRouter(); InputBinding? learned = InputBinding.Enter;
        router.Learned += value => learned = value; router.BeginLearning();
        var escape = new InputBinding(InputKind.Keyboard, 0x1B);
        router.Process(escape, true, InputBinding.F9, false, true); router.Process(escape, false, InputBinding.F9, false, true);
        Assert.Null(learned);
        Assert.True(new InputBinding(InputKind.Keyboard, 0x11).Matches(new(InputKind.Keyboard, 0xA3)));
        Assert.False(new InputBinding(InputKind.Keyboard, 0xA2).Matches(new(InputKind.Keyboard, 0xA3)));
    }
    [Theory] [InlineData(0x201, 0u, 1, true)] [InlineData(0x205, 0u, 2, false)]
    [InlineData(0x207, 0u, 3, true)] [InlineData(0x20B, 65536u, 4, true)] [InlineData(0x20C, 131072u, 5, false)]
    public void WindowsMouseEventsDecode(int message, uint data, int button, bool down)
    {
        var decoded = KeyboardHook.DecodeMouse(message, data)!.Value;
        Assert.Equal(new InputBinding(InputKind.Mouse, button), decoded.Binding); Assert.Equal(down, decoded.Down);
        Assert.Null(KeyboardHook.DecodeMouse(0x20A, 0)); // Wheel scrolling has no held/released state.
    }
    [Fact] public void NativeInputLayoutsAndUnicodePairsMatchWindowsX64()
    {
        Assert.Equal(40, Marshal.SizeOf<NativeMethods.NativeInput>());
        Assert.Equal(8, Marshal.OffsetOf<NativeMethods.NativeInput>("Keyboard").ToInt32());
        Assert.Equal(24, Marshal.SizeOf<NativeMethods.KeyboardInput>());
        Assert.Equal(32, Marshal.SizeOf<NativeMethods.MouseInput>());
        Assert.Equal(32, Marshal.SizeOf<NativeMethods.MouseHookData>());
        const string text = "A Þ 😀";
        var events = WindowsChatInput.UnicodeEvents(text);
        Assert.Equal(text.Length * 2, events.Length);
        for (int i = 0; i < text.Length; i++)
        {
            Assert.Equal(text[i], (char)events[2 * i].Keyboard.Scan);
            Assert.Equal(4u, events[2 * i].Keyboard.Flags); Assert.Equal(6u, events[2 * i + 1].Keyboard.Flags);
            Assert.Equal(0, events[2 * i].Keyboard.Key);
        }
        var down = WindowsChatInput.ButtonEvent(new(InputKind.Mouse, 5), false);
        var up = WindowsChatInput.ButtonEvent(new(InputKind.Mouse, 5), true);
        Assert.Equal(2u, down.Mouse.Data); Assert.Equal(0x80u, down.Mouse.Flags); Assert.Equal(0x100u, up.Mouse.Flags);
    }
}
