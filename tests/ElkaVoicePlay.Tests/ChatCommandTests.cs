using System.Text.Json;
using ElkaVoicePlay.Core;
using ElkaVoicePlay.Input;
using ElkaVoicePlay.Shared;
using Xunit;

namespace ElkaVoicePlay.Tests;

public sealed class ChatCommandTests
{
    private static readonly TalkTrigger Double = new(InputBinding.F9, true, 300);
    private static readonly TalkTrigger Separate = new(new(InputKind.Mouse, 4), true, CommandOnly: true);
    private static (TalkGesture Gesture, List<bool> Holds, List<ChatCommandRequest> Commands) Create()
    {
        var gesture = new TalkGesture(); var holds = new List<bool>(); var commands = new List<ChatCommandRequest>();
        gesture.HoldChanged += holds.Add; gesture.Command += commands.Add;
        return (gesture, holds, commands);
    }
    [Fact] public void TwoCompletedFastTapsRunOneCommandWithoutRecording()
    {
        var (g, holds, commands) = Create();
        g.Press(Double, InputBinding.F9, 0); g.Release(45);
        g.Press(Double, InputBinding.F9, 120); g.Release(170); g.Advance(1000);
        Assert.Empty(holds); Assert.Equal(new ChatCommandRequest(InputBinding.F9, false, 170), Assert.Single(commands));
        Assert.Null(g.DueAt);
    }
    [Fact] public void SingleShortTapExpiresWithoutRecordingOrSending()
    {
        var (g, holds, commands) = Create();
        g.Press(Double, InputBinding.F9, 0); g.Release(45); g.Advance(300);
        Assert.Empty(holds); Assert.Empty(commands); Assert.Null(g.DueAt);
    }
    [Fact] public void HeldButtonStartsRecordingAtThresholdAndPairsRelease()
    {
        var (g, holds, commands) = Create();
        g.Press(Double, InputBinding.F9, 0); g.Advance(299); Assert.Empty(holds);
        g.Advance(300); g.Advance(900); g.Release(1000);
        Assert.Equal(new[] { true, false }, holds); Assert.Empty(commands);
    }
    [Fact] public void SecondPressHeldPastDeadlineRecordsInsteadOfRunningCommand()
    {
        var (g, holds, commands) = Create();
        g.Press(Double, InputBinding.F9, 0); g.Release(40); g.Press(Double, InputBinding.F9, 160);
        g.Advance(300); g.Release(800);
        Assert.Equal(new[] { true, false }, holds); Assert.Empty(commands);
    }
    [Fact] public void LateSecondTapDoesNotCompleteFirstGesture()
    {
        var (g, holds, commands) = Create();
        g.Press(Double, InputBinding.F9, 0); g.Release(40); g.Press(Double, InputBinding.F9, 300);
        g.Release(350); g.Advance(600);
        Assert.Empty(holds); Assert.Empty(commands);
    }
    [Fact] public void SeparateButtonRunsOnceOnReleaseAndDoesNotRecord()
    {
        var (g, holds, commands) = Create();
        g.Press(Separate, Separate.Binding, 0); g.Press(Separate, Separate.Binding, 100); g.Advance(5000);
        Assert.Empty(commands); g.Release(6000); g.Release(6100);
        Assert.Empty(holds); Assert.Equal(new ChatCommandRequest(Separate.Binding, true), Assert.Single(commands));
    }
    [Fact] public void NormalTalkStillStartsImmediatelyWithoutDoubleTapMode()
    {
        var (g, holds, commands) = Create();
        g.Press(new(InputBinding.F9, true), InputBinding.F9, 0);
        Assert.Equal(new[] { true }, holds); g.Release(10);
        Assert.Equal(new[] { true, false }, holds); Assert.Empty(commands);
    }
    [Fact] public void CancellationClearsPendingTapButKeepsRecordingReleasePaired()
    {
        var (g, holds, commands) = Create();
        g.Press(Double, InputBinding.F9, 0); g.Release(40); g.CancelPending();
        g.Press(Double, InputBinding.F9, 120); g.Release(170); g.Advance(1000);
        Assert.Empty(holds); Assert.Empty(commands);
        g.Press(Double, InputBinding.F9, 2000); g.Advance(2300); g.CancelPending(); g.Release(2400);
        Assert.Equal(new[] { true, false }, holds);
    }
    [Fact] public void DifferentButtonsCannotCombineIntoDoubleTap()
    {
        var (g, holds, commands) = Create();
        g.Press(Double, InputBinding.F9, 0); g.Release(40);
        var mouse = Double with { Binding = new(InputKind.Mouse, 4) };
        g.Press(mouse, mouse.Binding, 120); g.Release(170); g.Advance(1000);
        Assert.Empty(holds); Assert.Empty(commands);
    }
    [Fact] public void TripleTapRunsOnlyOneCommandAndCustomTimingWorks()
    {
        var (g, holds, commands) = Create(); var fast = Double with { DoubleTapWindowMs = 100 };
        g.Press(fast, fast.Binding, 0); g.Release(20); g.Press(fast, fast.Binding, 50); g.Release(70);
        g.Press(fast, fast.Binding, 80); g.Release(90); g.Advance(200);
        Assert.Single(commands); Assert.Empty(holds);
    }
    [Fact] public void ProfilesRoundTripWithIndependentCommandButtonsAndTiming()
    {
        var profile = GameProfile.WarDogs() with { ChatCommand = new() { Enabled = true, UseSeparateButton = true,
            Button = new(InputKind.Mouse, 5), DoubleTapWindowMs = 210, Third = null, FirstDelayMs = 375 } };
        var settings = new AppSettings { Profiles = [profile], SelectedProfileId = profile.Id }.Validate();
        var loaded = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!.Validate();
        Assert.Equal(profile, Assert.Single(loaded.Profiles!));
        Assert.False(JsonSerializer.Deserialize<GameProfile>("{}")!.Validate().ChatCommand.Enabled);
        Assert.Throws<InvalidDataException>(() => (profile with { ChatCommand = profile.ChatCommand with { Button = profile.Trigger } }).Validate());
        Assert.Throws<InvalidDataException>(() => new ChatCommandSettings { Enabled = true, First = null, Second = null, Third = null }.Validate());
    }
    [Fact] public void ActualDestinationValidatesModeBindingAndDoubleTapTiming()
    {
        var profile = GameProfile.WarDogs() with { ChatCommand = new() { Enabled = true, DoubleTapWindowMs = 200 } };
        Assert.True(profile.ChatCommand.Accepts(profile, new(profile.Trigger, false, 190)));
        Assert.False(profile.ChatCommand.Accepts(profile, new(profile.Trigger, false, 200)));
        Assert.False(profile.ChatCommand.Accepts(profile, new(profile.Trigger, true)));
        Assert.False(profile.ChatCommand.Accepts(profile, new(new(InputKind.Mouse, 4), false, 190)));
        Assert.False(new ChatCommandSettings().Accepts(profile, new(profile.Trigger, false, 190)));
    }
    [Fact] public void SeparateMouseCommandInstallsMouseHookAndSharedTimingIncludesAutomaticProfiles()
    {
        var manual = GameProfile.Notepad() with { ChatCommand = new() { Enabled = true, UseSeparateButton = true, Button = new(InputKind.Mouse, 5) } };
        var auto = GameProfile.WarDogs() with { AutoActivate = true, Executable = "game.exe", ChatCommand = new() { Enabled = true, DoubleTapWindowMs = 450 } };
        var settings = new AppSettings { Profiles = [manual, auto], SelectedProfileId = manual.Id }.Validate();
        var config = HookConfiguration.ForSettings(settings, true, true);
        Assert.True(config.NeedsMouseHook);
        Assert.Equal(450, config.Triggers[0].DoubleTapWindowMs);
        Assert.True(config.Triggers[1].CommandOnly);
    }
    [Fact] public async Task CommandRunsConfiguredKeysInOrderWithoutText()
    {
        var input = new FakeInput();
        await new ChatCommandSequence(input).RunAsync(Target(), default);
        Assert.Equal(new[] { InputBinding.Enter, new InputBinding(InputKind.Keyboard, 9), InputBinding.Enter }, input.Buttons);
    }
    [Fact] public async Task OptionalStepsAndCustomMouseOutputWork()
    {
        var input = new FakeInput(); var target = Target();
        target = target with { Profile = target.Profile! with { ChatCommand = target.Profile.ChatCommand with
            { First = null, Second = new(InputKind.Mouse, 5), Third = null } } };
        await new ChatCommandSequence(input).RunAsync(target, default);
        Assert.Equal(new InputBinding(InputKind.Mouse, 5), Assert.Single(input.Buttons));
    }
    [Fact] public async Task FocusLossOrCancellationStopsRemainingCommandSteps()
    {
        var input = new FakeInput(); input.AfterTap = () => input.Current = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ChatCommandSequence(input).RunAsync(Target(), default));
        Assert.Single(input.Buttons);
        using var cts = new CancellationTokenSource();
        input = new FakeInput { AfterTap = cts.Cancel };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ChatCommandSequence(input).RunAsync(Target(), cts.Token));
        Assert.Single(input.Buttons);
    }
    [Fact] public async Task DisabledOrInvalidTargetSendsNothing()
    {
        var input = new FakeInput { Current = false };
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ChatCommandSequence(input).RunAsync(Target(), default));
        Assert.Empty(input.Buttons);
        input.Current = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ChatCommandSequence(input).RunAsync(Target() with { Profile = GameProfile.WarDogs() }, default));
        Assert.Empty(input.Buttons);
    }
    private static WindowTarget Target() => new(1, 2, 3, "test", 4, Profile: GameProfile.WarDogs() with
    { ChatCommand = new() { Enabled = true, FirstDelayMs = 0, SecondDelayMs = 0 } });
    private sealed class FakeInput : IChatInput
    {
        public bool Current = true;
        public List<InputBinding> Buttons = [];
        public Action? AfterTap;
        public bool IsCurrent(WindowTarget target) => Current;
        public void EnsureModifiersReleased() { }
        public Task TapAsync(WindowTarget target, InputBinding binding, int durationMs, CancellationToken token)
        { token.ThrowIfCancellationRequested(); Buttons.Add(binding); AfterTap?.Invoke(); return Task.CompletedTask; }
        public Task TypeAsync(WindowTarget target, string text, int delayMs, CancellationToken token) => throw new InvalidOperationException("Commands must never type a transcript.");
    }
}
