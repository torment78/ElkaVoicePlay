using ElkaVoicePlay.Shared;

namespace ElkaVoicePlay.Input;

public sealed record TalkTrigger(InputBinding Binding, bool Suppress, int DoubleTapWindowMs = 0, bool CommandOnly = false);

public sealed record HookConfiguration(bool Enabled, bool AllowRecording, TalkTrigger[] Triggers)
{
    public static HookConfiguration Disabled { get; } = new(false, false, []);
    internal bool NeedsMouseHook => Enabled && AllowRecording && Triggers.Any(t => t.Binding.Kind == InputKind.Mouse);
    public static HookConfiguration ForSettings(AppSettings settings, bool enabled, bool allowRecording)
    {
        var selected = settings.Profiles!.First(p => p.Id == settings.SelectedProfileId);
        // No process/foreground polling. Optional automatic profiles contribute global talk bindings;
        // their EXE rules choose the delivery sequence only after transcription.
        var profiles = new[] { selected }.Concat(settings.AutomaticProfiles ? settings.Profiles!.Where(p => p.AutoActivate) : []);
        var controls = profiles.SelectMany(p =>
        {
            var command = p.ChatCommand;
            var talk = new TalkTrigger(p.Trigger, p.SuppressTrigger,
                command.Enabled && !command.UseSeparateButton ? command.DoubleTapWindowMs : 0);
            return command.Enabled && command.UseSeparateButton
                ? new[] { talk, new TalkTrigger(command.Button, command.SuppressButton, CommandOnly: true) } : [talk];
        });
        // Shared talk buttons can recognize every candidate's timing without inspecting the target.
        // The actual destination's profile validates the gesture immediately before sending.
        return new(enabled, allowRecording, controls.GroupBy(t => t.Binding).Select(group =>
        {
            var preferred = group.First();
            return preferred.CommandOnly ? preferred : preferred with
            { DoubleTapWindowMs = group.Where(t => !t.CommandOnly).Max(t => t.DoubleTapWindowMs) };
        }).ToArray());
    }
}
