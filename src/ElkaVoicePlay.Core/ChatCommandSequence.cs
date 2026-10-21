using ElkaVoicePlay.Shared;

namespace ElkaVoicePlay.Core;

/// <summary>Runs only the configured buttons; never inserts or reuses a transcript.</summary>
public sealed class ChatCommandSequence(IChatInput input)
{
    public async Task RunAsync(WindowTarget target, CancellationToken token)
    {
        var command = target.Profile?.ChatCommand.Validate() ?? throw new InvalidOperationException("No command profile was selected.");
        if (!command.Enabled) throw new InvalidOperationException("The chat command is disabled for this profile.");
        var steps = new[] { (command.First, command.FirstDelayMs), (command.Second, command.SecondDelayMs), (command.Third, 0) };
        foreach (var (button, delay) in steps)
        {
            if (button == null) continue;
            token.ThrowIfCancellationRequested();
            if (!input.IsCurrent(target)) throw new InvalidOperationException("Focus changed. Chat command stopped; check the chat before retrying.");
            input.EnsureModifiersReleased();
            await input.TapAsync(target, button, command.PressDurationMs, token);
            if (delay > 0) await Task.Delay(delay, token);
        }
    }
}
