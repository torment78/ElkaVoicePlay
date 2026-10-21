using System.Text.Json;
using ElkaVoicePlay.Core;
using ElkaVoicePlay.Input;
using ElkaVoicePlay.Shared;
using Xunit;

namespace ElkaVoicePlay.Tests;

public sealed class KeyboardBlockingTests
{
    private static WindowTarget Target(bool enabled = true, bool automatic = true) => new(1, 2, 3, "game", 4,
        Profile: GameProfile.WarDogs() with { BlockKeyboardWhileSending = enabled, SendAutomatically = automatic,
            OpenDelayMs = 0, CharacterDelayMs = 0, BeforeSendDelayMs = 0, BeforeCloseDelayMs = 0,
            CloseChat = new(InputKind.Keyboard, 0x1B) });

    [Fact] public void ExistingProfilesStayOptInAndTheChoiceRoundTrips()
    {
        Assert.False(JsonSerializer.Deserialize<GameProfile>("{}")!.Validate().BlockKeyboardWhileSending);
        var profile = Target().Profile!;
        Assert.True(JsonSerializer.Deserialize<GameProfile>(JsonSerializer.Serialize(profile))!.Validate().BlockKeyboardWhileSending);
        Assert.False(GameProfile.Notepad().BlockKeyboardWhileSending);
    }
    [Theory] [InlineData(0x57)] [InlineData(0x10)] [InlineData(0x11)] [InlineData(0x12)] [InlineData(0x0D)]
    public void NewPhysicalPressReleaseAndRepeatsAreDiscarded(int key)
    {
        var filter = new KeyboardBlockFilter();
        Assert.True(filter.Block(key, true, false));
        Assert.True(filter.Block(key, true, false));
        Assert.True(filter.Block(key, false, false));
    }
    [Fact] public void PreviouslyHeldKeyReleasesPassWithoutAllowingRepeatedLetters()
    {
        var filter = new KeyboardBlockFilter(); filter.SeedHeld(0x57);
        Assert.True(filter.Block(0x57, true, false));
        Assert.False(filter.Block(0x57, false, false));
        Assert.True(filter.Block(0x57, true, false));
        Assert.True(filter.Block(0x57, false, false));
    }
    [Fact] public void VoicePlayInjectedEventsPassAndDoNotConsumeAPhysicalRelease()
    {
        var filter = new KeyboardBlockFilter(); filter.SeedHeld(0x0D);
        Assert.False(filter.Block(0x0D, true, true));
        Assert.False(filter.Block(0x0D, false, true));
        Assert.False(filter.Block(0x0D, false, false));
        Assert.False(filter.Block(0xE7, true, true));
    }
    [Fact] public void DisabledFilterRestoresBothDirectionsWithoutReplayingAnything()
    {
        var filter = new KeyboardBlockFilter(); Assert.True(filter.Block(0x41, true, false));
        Assert.True(filter.DisableOnce()); Assert.False(filter.DisableOnce());
        Assert.False(filter.Block(0x41, true, false)); Assert.False(filter.Block(0x41, false, false));
        Assert.False(filter.Block(0x1B, true, false));
    }
    [Fact] public async Task GuardCoversOpenTextSendAndCloseThenReleases()
    {
        var input = new Input();
        await new ChatSequence(input).RunAsync(Target(), "hello", default);
        Assert.Equal(new[] { "block", "Enter", "text", "Enter", "Escape", "unblock" }, input.Events);
        Assert.True(input.Guard!.Disposed);
    }
    [Fact] public async Task DisabledOptionDoesNotAcquireAGuard()
    {
        var input = new Input(); await new ChatSequence(input).RunAsync(Target(false), "hello", default);
        Assert.Null(input.Guard); Assert.DoesNotContain("block", input.Events);
    }
    [Fact] public async Task ManualSendRestoresKeyboardImmediatelyAfterTyping()
    {
        var input = new Input(); await new ChatSequence(input).RunAsync(Target(automatic: false), "hello", default);
        Assert.Equal(new[] { "block", "Enter", "text", "unblock" }, input.Events);
    }
    [Fact] public async Task InvalidDestinationNeverBlocksOrSends()
    {
        var input = new Input { Current = false };
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ChatSequence(input).RunAsync(Target(), "hello", default));
        Assert.Empty(input.Events);
    }
    [Fact] public async Task InvalidTextNeverBlocksOrSends()
    {
        var input = new Input();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ChatSequence(input).RunAsync(Target(), "bad\ntext", default));
        Assert.Empty(input.Events);
    }
    [Fact] public async Task GuardInstallationFailureStopsBeforeOpeningChat()
    {
        var input = new Input { FailStart = true };
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ChatSequence(input).RunAsync(Target(), "hello", default));
        Assert.Empty(input.Events);
    }
    [Fact] public async Task TypingFailureReleasesKeyboardAndNeverSends()
    {
        var input = new Input { OnText = _ => throw new InvalidOperationException("partial typing") };
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ChatSequence(input).RunAsync(Target(), "hello", default));
        Assert.Equal(new[] { "block", "Enter", "text", "unblock" }, input.Events);
    }
    [Fact] public async Task FocusLossReleasesKeyboardAndNeverSends()
    {
        var input = new Input { OnText = i => i.Current = false };
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ChatSequence(input).RunAsync(Target(), "hello", default));
        Assert.Equal(new[] { "block", "Enter", "text", "unblock" }, input.Events);
    }
    [Fact] public async Task EscapeCancellationUsesGuardTokenAndReleasesKeyboard()
    {
        var input = new Input { OnText = i => i.Guard!.Cancel() };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ChatSequence(input).RunAsync(Target(), "hello", default));
        Assert.Equal(new[] { "block", "Enter", "text", "unblock" }, input.Events);
    }
    [Fact] public async Task WatchdogStopsLaterStepsReportsTimeoutAndReleasesKeyboard()
    {
        var input = new Input { OnText = i => { i.Guard!.TimedOut = true; i.Guard.Cancel(); } };
        var error = await Assert.ThrowsAsync<TimeoutException>(() => new ChatSequence(input).RunAsync(Target(), "hello", default));
        Assert.Contains("15-second", error.Message);
        Assert.Equal(new[] { "block", "Enter", "text", "unblock" }, input.Events);
    }
    [Fact] public async Task ExternalCancellationStillReleasesKeyboard()
    {
        using var cancellation = new CancellationTokenSource();
        var input = new Input { OnText = _ => cancellation.Cancel() };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ChatSequence(input).RunAsync(Target(), "hello", cancellation.Token));
        Assert.True(input.Guard!.Disposed);
    }
    [Fact] public async Task CancellationBeforeDeliveryNeverAcquiresGuard()
    {
        var input = new Input();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ChatSequence(input).RunAsync(Target(), "hello", new(true)));
        Assert.Empty(input.Events);
    }
    private sealed class Input : IChatInput
    {
        internal List<string> Events = [];
        internal Guard? Guard;
        internal bool Current = true, FailStart;
        internal Action<Input>? OnText;
        public bool IsCurrent(WindowTarget target) => Current;
        public void EnsureModifiersReleased() { }
        public Task<IKeyboardSendGuard> BlockKeyboardAsync(CancellationToken token)
        {
            if (FailStart) throw new InvalidOperationException("hook failed");
            Events.Add("block"); Guard = new(token, () => Events.Add("unblock"));
            return Task.FromResult<IKeyboardSendGuard>(Guard);
        }
        public Task TapAsync(WindowTarget target, InputBinding binding, int durationMs, CancellationToken token)
        { Check(token); Events.Add(binding.ToString()); return Task.CompletedTask; }
        public Task TypeAsync(WindowTarget target, string text, int delayMs, CancellationToken token)
        { Check(token); Events.Add("text"); OnText?.Invoke(this); token.ThrowIfCancellationRequested(); return Task.CompletedTask; }
        private void Check(CancellationToken token) { token.ThrowIfCancellationRequested(); if (Guard != null) Assert.False(Guard.Disposed); }
    }
    private sealed class Guard(CancellationToken token, Action onDispose) : IKeyboardSendGuard
    {
        private readonly CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        public CancellationToken Token => cancellation.Token;
        public bool TimedOut { get; set; }
        internal bool Disposed;
        internal void Cancel() => cancellation.Cancel();
        public ValueTask DisposeAsync() { Disposed = true; onDispose(); cancellation.Dispose(); return ValueTask.CompletedTask; }
    }
}
