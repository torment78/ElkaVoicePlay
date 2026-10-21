using ElkaVoicePlay.Core;
using ElkaVoicePlay.Shared;
using Xunit;

namespace ElkaVoicePlay.Tests;

public sealed class ChatSequenceTests
{
    private static WindowTarget Target(GameProfile? profile = null) => new(1, 2, 3, "game", 4, Profile: profile ??
        GameProfile.WarDogs() with { OpenDelayMs = 0, CharacterDelayMs = 0, BeforeSendDelayMs = 0, BeforeCloseDelayMs = 0 });
    [Fact] public async Task WarDogsOpensTypesSendsWithoutThirdPress()
    {
        var input = new FakeInput(); var sequence = new ChatSequence(input);
        await sequence.RunAsync(Target(), "Hello Þú 😀", default);
        Assert.Equal(new[] { "tap:Enter", "text:Hello Þú 😀", "tap:Enter" }, input.Events);
    }
    [Fact] public async Task OptionalCloseComesOnlyAfterSend()
    {
        var input = new FakeInput(); var target = Target();
        target = target with { Profile = target.Profile! with { CloseChat = new(InputKind.Keyboard, 0x1B) } };
        await new ChatSequence(input).RunAsync(target, "Hello", default);
        Assert.Equal(new[] { "tap:Enter", "text:Hello", "tap:Enter", "tap:Escape" }, input.Events);
        input.Events.Clear(); target = target with { Profile = target.Profile! with { SendAutomatically = false } };
        await new ChatSequence(input).RunAsync(target, "Review me", default);
        Assert.Equal(new[] { "tap:Enter", "text:Review me" }, input.Events);
    }
    [Fact] public async Task AlreadyOpenChatSkipsOpeningKey()
    {
        var input = new FakeInput(); var target = Target(); target = target with { Profile = target.Profile! with { OpenChat = null } };
        await new ChatSequence(input).RunAsync(target, "Hello", default);
        Assert.Equal(new[] { "text:Hello", "tap:Enter" }, input.Events);
    }
    [Fact] public async Task FocusLossAfterOpeningNeverTypesOrSends()
    {
        var input = new FakeInput(); input.AfterTap = () => input.Current = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ChatSequence(input).RunAsync(Target(), "Hello", default));
        Assert.Equal(new[] { "tap:Enter" }, input.Events);
    }
    [Fact] public async Task CancellationDuringOpenDelayNeverTypesOrSends()
    {
        using var cancellation = new CancellationTokenSource(); var input = new FakeInput { AfterTap = cancellation.Cancel };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ChatSequence(input).RunAsync(Target(), "Hello", cancellation.Token));
        Assert.Equal(new[] { "tap:Enter" }, input.Events);
    }
    [Fact] public async Task PartialTypingFailureNeverSendsOrCloses()
    {
        var input = new FakeInput { FailTyping = true };
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ChatSequence(input).RunAsync(Target(), "Hello", default));
        Assert.Equal(new[] { "tap:Enter", "text:Hello" }, input.Events);
    }
    [Fact] public async Task FocusLossAfterTextStopsSubmission()
    {
        var input = new FakeInput { LoseFocusOnText = true };
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ChatSequence(input).RunAsync(Target(), "Hello", default));
        Assert.Equal(new[] { "tap:Enter", "text:Hello" }, input.Events);
    }
    [Fact] public async Task ConfiguredDelayActuallySeparatesOpenAndText()
    {
        var input = new FakeInput(); var target = Target(); target = target with { Profile = target.Profile! with { OpenDelayMs = 150 } };
        var run = new ChatSequence(input).RunAsync(target, "Hello", default);
        Assert.Equal(new[] { "tap:Enter" }, input.Events);
        await run; Assert.Equal(3, input.Events.Count);
    }
    [Theory] [InlineData("")] [InlineData("hello\nworld")] [InlineData("hello\0")]
    public async Task InvalidTextNeverOpensChat(string text)
    {
        var input = new FakeInput();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ChatSequence(input).RunAsync(Target(), text, default));
        Assert.Empty(input.Events);
    }
    private sealed class FakeInput : IChatInput
    {
        internal List<string> Events = [];
        internal bool Current = true, FailTyping, LoseFocusOnText;
        internal Action? AfterTap;
        public bool IsCurrent(WindowTarget target) => Current;
        public void EnsureModifiersReleased() { }
        public Task TapAsync(WindowTarget target, InputBinding binding, int durationMs, CancellationToken token)
        { token.ThrowIfCancellationRequested(); Events.Add("tap:" + binding); AfterTap?.Invoke(); return Task.CompletedTask; }
        public Task TypeAsync(WindowTarget target, string text, int delayMs, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Events.Add("text:" + text);
            if (LoseFocusOnText) Current = false;
            if (FailTyping) throw new InvalidOperationException("Partial send"); return Task.CompletedTask;
        }
    }
}
