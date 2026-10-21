using System.Runtime.InteropServices;
using ElkaVoicePlay.Input;
using ElkaVoicePlay.Shared;
using Xunit;

namespace ElkaVoicePlay.Tests;

public sealed class IdleInputTests
{
    [Fact] public void MouseMotionAndWheelNeverReadNativeDataOrAllocate()
    {
        // A null lParam is intentionally invalid for decoding. Motion must return before reading it.
        for (int i = 0; i < 1000; i++) KeyboardHook.TryReadMouseButton(0, 0x200, 0, out _);
        long before = GC.GetAllocatedBytesForCurrentThread();
        int decoded = 0;
        for (int i = 0; i < 100000; i++)
        {
            if (KeyboardHook.TryReadMouseButton(0, 0x200, 0, out _)) decoded++;
            if (KeyboardHook.TryReadMouseButton(0, 0x20A, 0, out _)) decoded++;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, decoded); Assert.Equal(0, allocated);
        Assert.False(KeyboardHook.TryReadMouseButton(-1, 0x201, 0, out _));
    }

    [Fact] public void NativeMouseButtonsStillDecodeAndInjectedButtonsAreIgnored()
    {
        nint buffer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.MouseHookData>());
        try
        {
            Marshal.StructureToPtr(new NativeMethods.MouseHookData { MouseData = 65536 }, buffer, false);
            Assert.True(KeyboardHook.TryReadMouseButton(0, 0x20B, buffer, out var down));
            Assert.Equal(new InputBinding(InputKind.Mouse, 4), down.Binding); Assert.True(down.Down);
            Marshal.StructureToPtr(new NativeMethods.MouseHookData { MouseData = 65536, Flags = 1 }, buffer, false);
            Assert.False(KeyboardHook.TryReadMouseButton(0, 0x20B, buffer, out _));
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    [Fact] public async Task HookThreadPumpsWithoutCallingThreadAndCleansUpOnItsOwnThread()
    {
        int caller = Environment.CurrentManagedThreadId, executedOn = 0, cleanedOn = 0;
        using var completed = new ManualResetEventSlim();
        var pump = new HookThread(() => cleanedOn = Environment.CurrentManagedThreadId);
        var work = pump.InvokeAsync(() => { executedOn = Environment.CurrentManagedThreadId; completed.Set(); });
        // Block the creating thread exactly as a busy UI would. The input pump must keep progressing.
        Assert.True(completed.Wait(TimeSpan.FromSeconds(2)));
        await work;
        Assert.NotEqual(caller, executedOn); Assert.Equal(pump.ManagedThreadId, executedOn);
        pump.Dispose();
        Assert.Equal(executedOn, cleanedOn);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => pump.InvokeAsync(() => { }));
    }

    [Fact] public async Task HookThreadPreservesOrderAndSurvivesACommandFailure()
    {
        using var pump = new HookThread(() => { });
        var output = new List<int>();
        var first = pump.InvokeAsync(() => output.Add(1));
        var failure = pump.InvokeAsync(() => throw new InvalidOperationException("test"));
        var last = pump.InvokeAsync(() => output.Add(2));
        await first; await Assert.ThrowsAsync<InvalidOperationException>(() => failure); await last;
        Assert.Equal(new[] { 1, 2 }, output);
        Assert.Equal(48, Marshal.SizeOf<NativeMethods.ThreadMessage>());
    }

    [Fact] public void MouseHookIsUnneededForKeyboardOnlyProfilesOrDisabledRecording()
    {
        var notepad = GameProfile.Notepad();
        var settings = new AppSettings { Profiles = [notepad], SelectedProfileId = notepad.Id }.Validate();
        Assert.False(HookConfiguration.ForSettings(settings, true, true).NeedsMouseHook);
        var mouse = new GameProfile { Trigger = new(InputKind.Mouse, 4) };
        settings = (settings with { Profiles = [mouse], SelectedProfileId = mouse.Id }).Validate();
        Assert.True(HookConfiguration.ForSettings(settings, true, true).NeedsMouseHook);
        Assert.False(HookConfiguration.ForSettings(settings, false, true).NeedsMouseHook);
        Assert.False(HookConfiguration.ForSettings(settings, true, false).NeedsMouseHook);
    }

    [Fact] public void AutomaticBindingsAreAvailableWithoutAnyProcessLookup()
    {
        var manual = new GameProfile { Trigger = new(InputKind.Mouse, 4), SuppressTrigger = false };
        var auto = GameProfile.Notepad();
        var settings = new AppSettings { Profiles = [manual, auto], SelectedProfileId = manual.Id }.Validate();
        var config = HookConfiguration.ForSettings(settings, true, true);
        Assert.Equal(new[] { manual.Trigger, auto.Trigger }, config.Triggers.Select(t => t.Binding));
        Assert.False(config.Triggers[0].Suppress);
        Assert.Single(HookConfiguration.ForSettings(settings with { AutomaticProfiles = false }, true, true).Triggers);
    }

    [Fact] public void HeldButtonAtHookInstallationRequiresAReleaseBeforeRecording()
    {
        var router = new InputRouter(); int edges = 0; router.HoldChanged += _ => edges++;
        router.SeedPressed(InputBinding.F9);
        Assert.False(router.Process(InputBinding.F9, true, InputBinding.F9, true, true));
        Assert.False(router.Process(InputBinding.F9, false, InputBinding.F9, true, true));
        Assert.Equal(0, edges);
        Assert.True(router.Process(InputBinding.F9, true, InputBinding.F9, true, true));
        Assert.Equal(1, edges);
    }
}
