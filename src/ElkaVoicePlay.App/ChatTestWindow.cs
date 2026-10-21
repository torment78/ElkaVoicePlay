using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using ElkaVoicePlay.Core;
using ElkaVoicePlay.Input;
using ElkaVoicePlay.Shared;
using InputBinding = ElkaVoicePlay.Shared.InputBinding;

namespace ElkaVoicePlay.App;

/// <summary>A local chat receiver for testing the real Windows input path without posting to a game.</summary>
public sealed class ChatTestWindow : Window
{
    private const string Sample = "Now we're talking about other stuff so that is so. Þú ert á Íslandi — æ ö ü. 😀";
    private readonly GameProfile profile;
    private readonly TextBox chat = new() { IsReadOnly = true, MinHeight = 90, TextWrapping = TextWrapping.Wrap };
    private readonly TextBox log = new() { IsReadOnly = true, MinHeight = 160, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBlock result = new() { Text = "Ready. Run the sequence to test keyboard events and text delivery locally.", Margin = new Thickness(0, 12, 0, 12) };
    private readonly Button run = new() { Content = "Run local chat test" };
    private readonly Button runCommand = new() { Content = "Test command buttons", Margin = new Thickness(8, 0, 0, 0) };
    private bool testingCommand;
    private readonly List<InputBinding> commandDowns = [], commandUps = [];
    private bool running, open, sent, focusLost, closing;
    private string received = "";
    private int sends;
    private CancellationTokenSource? cancellation;
    private Task pending = Task.CompletedTask;
    public ChatTestWindow(GameProfile profile)
    {
        this.profile = profile.Delivery == TextDelivery.Unicode ? profile : GameProfile.WarDogs() with { ChatCommand = profile.ChatCommand };
        Title = "Local chat test · " + this.profile.Name; Width = 710; Height = 570;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Style = (Style)FindResource(typeof(Window));
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = "Local chat test", FontSize = 26, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = "Uses your profile's buttons and delays against this test window. No microphone is opened and no message is sent online.", Margin = new Thickness(0, 8, 0, 12) });
        panel.Children.Add(new TextBlock { Text = $"Open: {this.profile.OpenChat?.ToString() ?? "None"} → type → Send: {(this.profile.SendAutomatically ? this.profile.Send.ToString() : "Manual")} → Close: {this.profile.CloseChat?.ToString() ?? "None"}", Margin = new Thickness(0, 0, 0, 12) });
        panel.Children.Add(new TextBlock { Text = this.profile.BlockKeyboardWhileSending
            ? "Keyboard blocking: On during delivery. Esc cancels; mouse remains usable."
            : "Keyboard blocking: Off. Enable it in Edit / Learn to test protection.", Margin = new Thickness(0, 0, 0, 10) });
        panel.Children.Add(chat); panel.Children.Add(result); panel.Children.Add(log);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        run.Click += async (_, _) => { pending = RunAsync(); await pending; };
        runCommand.Click += async (_, _) => { pending = RunAsync(true); await pending; };
        var cancel = new Button { Content = "Cancel test", Margin = new Thickness(8, 0, 0, 0) }; cancel.Click += (_, _) => cancellation?.Cancel();
        row.Children.Add(run); row.Children.Add(runCommand); row.Children.Add(cancel); panel.Children.Add(row);
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        PreviewKeyDown += (_, e) => { if (running && Handle(new(InputKind.Keyboard, KeyInterop.VirtualKeyFromKey(e.Key == Key.System ? e.SystemKey : e.Key)), true)) e.Handled = true; };
        PreviewKeyUp += (_, e) => { if (running && Handle(new(InputKind.Keyboard, KeyInterop.VirtualKeyFromKey(e.Key == Key.System ? e.SystemKey : e.Key)), false)) e.Handled = true; };
        PreviewMouseDown += (_, e) => { if (running && Handle(Mouse(e.ChangedButton), true)) e.Handled = true; };
        PreviewMouseUp += (_, e) => { if (running && Handle(Mouse(e.ChangedButton), false)) e.Handled = true; };
        Deactivated += (_, _) => { if (running) focusLost = true; };
        Closing += async (_, e) =>
        {
            if (closing || !running) return;
            e.Cancel = true; cancellation?.Cancel(); await pending; closing = true; Close();
        };
    }
    private static InputBinding Mouse(MouseButton button) => new(InputKind.Mouse, button switch
    { MouseButton.Left => 1, MouseButton.Right => 2, MouseButton.Middle => 3, MouseButton.XButton1 => 4, _ => 5 });
    private bool Handle(InputBinding key, bool down)
    {
        if (testingCommand)
        {
            if (key != profile.ChatCommand.First && key != profile.ChatCommand.Second && key != profile.ChatCommand.Third) return false;
            log.AppendText($"{key} {(down ? "down" : "up")}\n");
            (down ? commandDowns : commandUps).Add(key);
            return true;
        }
        bool action = profile.OpenChat?.Matches(key) == true || profile.Send.Matches(key) || profile.CloseChat?.Matches(key) == true;
        if (!action) return false;
        log.AppendText($"{key} {(down ? "down" : "up")}\n");
        if (!down) return true;
        if (sent && profile.CloseChat?.Matches(key) == true) { open = false; chat.IsReadOnly = true; log.AppendText("Chat closed\n"); }
        else if (open && profile.Send.Matches(key))
        { received = chat.Text; sends++; sent = true; open = profile.CloseChat != null; chat.IsReadOnly = true; log.AppendText("Message received locally\n"); }
        else if (!open && profile.OpenChat?.Matches(key) == true) { open = true; chat.IsReadOnly = false; log.AppendText("Chat opened\n"); chat.Focus(); }
        return true;
    }
    private async Task RunAsync(bool commandOnly = false)
    {
        if (running) return;
        running = true; focusLost = false; sent = false; sends = 0; received = ""; open = profile.OpenChat == null;
        testingCommand = commandOnly; commandDowns.Clear(); commandUps.Clear();
        cancellation = new(); run.IsEnabled = runCommand.IsEnabled = false; log.Clear(); chat.Clear(); chat.IsReadOnly = commandOnly || !open; chat.Focus();
        result.Text = "Testing… keep this window active.";
        try
        {
            nint handle = new WindowInteropHelper(this).Handle;
            var target = new WindowTarget(handle, Environment.ProcessId, 0, "local test", 0, Profile: profile);
            if (commandOnly)
            {
                target = target with { Profile = profile with { ChatCommand = profile.ChatCommand with { Enabled = true } } };
                await new ChatCommandSequence(new WindowsChatInput(_ => !focusLost && ProfileTextTarget.IsForeground(handle)))
                    .RunAsync(target, cancellation.Token);
                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                var expected = new[] { profile.ChatCommand.First, profile.ChatCommand.Second, profile.ChatCommand.Third }.OfType<InputBinding>().ToArray();
                bool correct = commandDowns.SequenceEqual(expected) && commandUps.SequenceEqual(expected) && chat.Text.Length == 0;
                result.Text = correct ? "PASS · command buttons received in order with balanced releases; no text inserted." : "FAIL · command events differed. Review the log.";
                return;
            }
            var sequence = new ChatSequence(new WindowsChatInput(_ => !focusLost && ProfileTextTarget.IsForeground(handle)));
            await sequence.RunAsync(target, Sample, cancellation.Token);
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            string actual = profile.SendAutomatically ? received : chat.Text;
            bool passed = actual == Sample && sends == (profile.SendAutomatically ? 1 : 0) && (!profile.SendAutomatically || !open);
            result.Text = passed ? "PASS · exact text received; the expected chat steps completed." : "FAIL · the received text or chat steps differed. Review the event log.";
            log.AppendText($"Exact text: {actual == Sample}; sends: {sends}; chat open: {open}\n");
            chat.Text = actual;
        }
        catch (OperationCanceledException) { result.Text = "Cancelled. No further steps will run."; }
        catch (Exception ex) { result.Text = ex.Message; }
        finally { running = false; testingCommand = false; cancellation.Dispose(); cancellation = null; run.IsEnabled = runCommand.IsEnabled = true; chat.IsReadOnly = true; }
    }
}
