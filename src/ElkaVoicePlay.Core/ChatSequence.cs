using ElkaVoicePlay.Shared;

namespace ElkaVoicePlay.Core;

public sealed class ChatSequence(IChatInput input)
{
    public event Action<SessionStatus>? StatusChanged;
    public async Task RunAsync(WindowTarget target, string text, CancellationToken token)
    {
        var profile = target.Profile ?? throw new InvalidOperationException("No chat profile was captured.");
        Check();
        if (string.IsNullOrWhiteSpace(text) || text.Any(char.IsControl)) throw new InvalidOperationException("No safe chat text to insert.");
        input.EnsureModifiersReleased();
        await using var keyboard = profile.BlockKeyboardWhileSending ? await input.BlockKeyboardAsync(token) : null;
        if (keyboard != null) token = keyboard.Token;
        try
        {
            Check();
            // Check again after installing the guard: a modifier may have been held during setup.
            input.EnsureModifiersReleased();
            if (profile.OpenChat != null)
            {
                Report(SessionState.OpeningChat, "Opening chat");
                await input.TapAsync(target, profile.OpenChat, profile.PressDurationMs, token);
                await Wait(profile.OpenDelayMs);
            }
            Check();
            Report(SessionState.InsertingText, "Typing text");
            await input.TypeAsync(target, text, profile.CharacterDelayMs, token);
            if (profile.SendAutomatically)
            {
                await Wait(profile.BeforeSendDelayMs);
                Report(SessionState.SendingMessage, "Sending message");
                await input.TapAsync(target, profile.Send, profile.PressDurationMs, token);
                if (profile.CloseChat != null)
                {
                    await Wait(profile.BeforeCloseDelayMs);
                    Report(SessionState.ClosingChat, "Closing chat");
                    await input.TapAsync(target, profile.CloseChat, profile.PressDurationMs, token);
                }
            }
            Check();
        }
        catch (OperationCanceledException) when (keyboard?.TimedOut == true)
        {
            throw new TimeoutException("Sending exceeded the 15-second keyboard-blocking limit. Keyboard input is restored. Check the chat before retrying; the transcript is kept.");
        }
        void Check()
        {
            token.ThrowIfCancellationRequested();
            if (!input.IsCurrent(target)) throw new InvalidOperationException("Focus changed. Chat sequence stopped; check the game before retrying.");
        }
        async Task Wait(int delay) { await Task.Delay(delay, token); Check(); }
        void Report(SessionState state, string message)
        {
            Check();
            string protection = keyboard != null ? " · keyboard blocked (Esc cancels)" : "";
            StatusChanged?.Invoke(new(state, $"{profile.Name} · {message}…{protection}", text));
        }
    }
}
