using ElkaVoicePlay.Shared;

namespace ElkaVoicePlay.Input;

/// <summary>Keeps validation and one whole-message edit inseparable; errors never trigger a retry.</summary>
internal sealed class EditorTextInserter(Func<WindowTarget, bool> isCurrent, Action<nint, string> replaceSelection)
{
    internal Task InsertAsync(WindowTarget target, string text, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(text) || text.IndexOf('\0') >= 0)
            throw new InvalidOperationException("The text is empty or contains a null character. Nothing was inserted.");
        if (target.EditorHandle == 0 || !isCurrent(target))
            throw new InvalidOperationException("The Notepad document or focus changed. Nothing was inserted.");
        cancellationToken.ThrowIfCancellationRequested();
        replaceSelection(target.EditorHandle, text);
        return Task.CompletedTask;
    }
}
