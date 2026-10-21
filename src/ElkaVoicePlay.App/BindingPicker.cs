using System.Windows;
using System.Windows.Controls;
using ElkaVoicePlay.Shared;
using Forms = System.Windows.Forms;

namespace ElkaVoicePlay.App;

public sealed class BindingPicker : UserControl
{
    private sealed record Choice(InputBinding? Value, string Label) { public override string ToString() => Label; }
    private readonly ComboBox choices = new() { MinWidth = 170 };
    private readonly Button learnButton;
    public void ShowLearning(bool active)
    {
        learnButton.Content = active ? "Press a button…" : "Learn";
        choices.IsEnabled = !active;
    }
    public InputBinding? Value
    {
        get => (choices.SelectedItem as Choice)?.Value;
        set
        {
            var found = choices.Items.Cast<Choice>().FirstOrDefault(c => c.Value == value);
            if (found == null) { found = new(value, value?.ToString() ?? "None"); choices.Items.Add(found); }
            choices.SelectedItem = found;
        }
    }
    public BindingPicker(bool optional, Action<BindingPicker> learn)
    {
        if (optional) choices.Items.Add(new Choice(null, "None"));
        foreach (int code in Enumerable.Range(0x70, 24)) Add(new(InputKind.Keyboard, code));
        foreach (int code in Enumerable.Range(1, 5)) Add(new(InputKind.Mouse, code));
        foreach (int code in Enum.GetValues<Forms.Keys>().Select(k => (int)k).Distinct().Where(k => k is >= 8 and <= 254 && (k < 0x70 || k > 0x87)).Order())
            Add(new(InputKind.Keyboard, code));
        var row = new DockPanel();
        learnButton = new Button { Content = "Learn", Margin = new Thickness(8, 0, 0, 0), ToolTip = "Click, then press and release one keyboard key or mouse button. Esc cancels." };
        learnButton.Click += (_, _) => learn(this);
        DockPanel.SetDock(learnButton, Dock.Right); row.Children.Add(learnButton); row.Children.Add(choices); Content = row;
    }
    private void Add(InputBinding binding)
    {
        string label = binding.ToString();
        if (binding.Kind == InputKind.Keyboard && label.StartsWith("Key ", StringComparison.Ordinal)) label = ((Forms.Keys)binding.Code).ToString();
        choices.Items.Add(new Choice(binding, label));
    }
}
