using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ElkaVoicePlay.Input;
using ElkaVoicePlay.Shared;

namespace ElkaVoicePlay.App;

public sealed class ProfileEditorWindow : Window
{
    private readonly GameProfile original;
    private readonly KeyboardHook? hook;
    private readonly TextBox name = new(), executable = new();
    private readonly CheckBox automatic = new() { Content = "Automatically select this profile when this EXE is running" };
    private readonly CheckBox suppress = new() { Content = "Keep the talk button from reaching the game" };
    private readonly CheckBox blockKeyboard = new() { Content = "Block keyboard input while sending" };
    private readonly CheckBox sendAutomatically = new() { Content = "Send the message automatically" };
    private readonly CheckBox commandEnabled = new() { Content = "Enable chat-switch command for this profile" };
    private readonly CheckBox separateCommandButton = new() { Content = "Use a separate button (one press)" };
    private readonly CheckBox suppressCommandButton = new() { Content = "Keep the separate button from reaching the game" };
    private readonly BindingPicker commandButton, commandFirst, commandSecond, commandThird;
    private readonly TextBox doubleTapWindow = new(), commandFirstDelay = new(), commandSecondDelay = new(), commandPressDuration = new();
    private readonly ComboBox processes = new(), delivery = new();
    private readonly BindingPicker trigger, open, send, close;
    private readonly TextBox openDelay = new(), characterDelay = new(), sendDelay = new(), closeDelay = new(), pressDuration = new();
    private readonly TextBlock feedback = new() { Margin = new Thickness(0, 10, 0, 10), Foreground = System.Windows.Media.Brushes.LightSeaGreen };
    private readonly Button save = new() { Content = "Save profile", Margin = new Thickness(8, 0, 0, 0) };
    private readonly DispatcherTimer learnTimer = new() { Interval = TimeSpan.FromSeconds(20) };
    private BindingPicker? learning;
    public GameProfile? Result { get; private set; }
    private sealed record ProcessChoice(string Name, string? Path) { public override string ToString() => Name; }

    public ProfileEditorWindow(GameProfile profile, KeyboardHook? hook)
    {
        original = profile; this.hook = hook;
        Title = "Profile · " + profile.Name; Width = 760; Height = 820; MinWidth = 640; MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Style = (Style)FindResource(typeof(Window));
        trigger = new(false, Learn); open = new(true, Learn); send = new(false, Learn); close = new(true, Learn);
        commandButton = new(false, Learn); commandFirst = new(true, Learn); commandSecond = new(true, Learn); commandThird = new(true, Learn);
        var command = profile.ChatCommand;
        commandEnabled.IsChecked = command.Enabled; separateCommandButton.IsChecked = command.UseSeparateButton;
        suppressCommandButton.IsChecked = command.SuppressButton; commandButton.Value = command.Button;
        commandFirst.Value = command.First; commandSecond.Value = command.Second; commandThird.Value = command.Third;
        doubleTapWindow.Text = command.DoubleTapWindowMs.ToString();
        commandFirstDelay.Text = command.FirstDelayMs.ToString(); commandSecondDelay.Text = command.SecondDelayMs.ToString();
        commandPressDuration.Text = command.PressDurationMs.ToString();
        name.Text = profile.Name; executable.Text = profile.Executable; automatic.IsChecked = profile.AutoActivate;
        suppress.IsChecked = profile.SuppressTrigger; sendAutomatically.IsChecked = profile.SendAutomatically;
        blockKeyboard.IsChecked = profile.BlockKeyboardWhileSending;
        trigger.Value = profile.Trigger; open.Value = profile.OpenChat; send.Value = profile.Send; close.Value = profile.CloseChat;
        openDelay.Text = profile.OpenDelayMs.ToString(); characterDelay.Text = profile.CharacterDelayMs.ToString();
        sendDelay.Text = profile.BeforeSendDelayMs.ToString(); closeDelay.Text = profile.BeforeCloseDelayMs.ToString(); pressDuration.Text = profile.PressDurationMs.ToString();
        delivery.ItemsSource = new[] { "Game chat · Unicode typing", "Notepad · native document insertion" };
        delivery.SelectedIndex = profile.Delivery == TextDelivery.Unicode ? 0 : 1;
        var panel = new StackPanel { Margin = new Thickness(22, 8, 22, 0) };
        var header = new StackPanel { Margin = new Thickness(22, 18, 22, 8) };
        header.Children.Add(new TextBlock { Text = "Game profile", FontSize = 25, FontWeight = FontWeights.SemiBold });
        Field(header, "Profile name", name);
        Field(header, "Hold-to-talk button", trigger);
        var link = new StackPanel();
        var exeRow = new DockPanel();
        var browse = new Button { Content = "Browse EXE…", Margin = new Thickness(8, 0, 0, 0) };
        browse.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Application (*.exe)|*.exe", Title = "Link this profile to a game executable" };
            if (dialog.ShowDialog(this) == true) executable.Text = dialog.FileName;
        };
        DockPanel.SetDock(browse, Dock.Right); exeRow.Children.Add(browse); exeRow.Children.Add(executable);
        Field(link, "EXE link (optional) · file name or full path", exeRow);
        var processRow = new DockPanel();
        var refresh = new Button { Content = "Refresh", Margin = new Thickness(8, 0, 0, 0) };
        refresh.Click += async (_, _) => await RefreshProcesses();
        var choose = new Button { Content = "Use process", Margin = new Thickness(8, 0, 0, 0) };
        choose.Click += (_, _) => { if (processes.SelectedItem is ProcessChoice p) executable.Text = p.Path ?? p.Name; };
        DockPanel.SetDock(refresh, Dock.Right); DockPanel.SetDock(choose, Dock.Right);
        processRow.Children.Add(refresh); processRow.Children.Add(choose); processRow.Children.Add(processes);
        Field(link, "Or choose a running application", processRow);
        link.Children.Add(automatic);
        link.Children.Add(new TextBlock { Text = "EXE rules are checked only when text is ready to send. Enabled automatic profiles also make their talk buttons available globally. With no EXE link, the manual profile uses the app focused at delivery.", FontSize = 12, Opacity = 0.7 });
        panel.Children.Add(SectionCard.Create("Game & automatic selection", "Optional EXE link and running application", link));
        var protection = new StackPanel(); protection.Children.Add(suppress);
        protection.Children.Add(new TextBlock { Text = "Learn one key or mouse button, then release it. Mouse left, right, middle and both side buttons are supported. Extra device buttons can use a keyboard mapping.", FontSize = 12, Opacity = 0.7 });
        var commandPanel = new StackPanel { Margin = new Thickness(0, 10, 0, 8) };
        commandPanel.Children.Add(commandEnabled);
        var commandOptions = new StackPanel();
        var commandTiming = new StackPanel();
        commandOptions.Children.Add(separateCommandButton);
        Field(commandOptions, "Separate command button", commandButton);
        commandOptions.Children.Add(suppressCommandButton);
        Field(commandTiming, "Double-tap window (ms) · both taps must finish within this time", doubleTapWindow);
        commandOptions.Children.Add(new TextBlock { Text = "Without a separate button: double-tap the talk button for this command, or hold beyond the window to start recording. One short tap does nothing. A separate button keeps hold-to-talk immediate and runs this command when released.", FontSize = 12, Opacity = 0.7 });
        Field(commandOptions, "Command 1 · button (None to skip)", commandFirst);
        Field(commandTiming, "Wait after command 1 (ms)", commandFirstDelay);
        Field(commandOptions, "Command 2 · button", commandSecond);
        Field(commandTiming, "Wait after command 2 (ms)", commandSecondDelay);
        Field(commandOptions, "Command 3 · button", commandThird);
        Field(commandTiming, "Command button down → up duration (ms)", commandPressDuration);
        commandOptions.Children.Add(new TextBlock { Text = "Example: Enter → Tab → Enter cycles the chat destination in apps that use those keys. Change each button for this profile. This runs only these buttons, without speech text or a profile change. Have the chat empty and closed before running that example.", FontSize = 12, Opacity = 0.7 });
        commandOptions.Children.Add(SectionCard.Create("Command timings", "Double-tap window, waits and press duration", commandTiming));
        commandPanel.Children.Add(commandOptions);
        var commandCard = SectionCard.Create("Chat-switch command", "Double-tap or a separate button · up to three command steps", commandPanel);
        void CommandAvailability()
        {
            commandOptions.IsEnabled = commandEnabled.IsChecked == true;
            bool separate = separateCommandButton.IsChecked == true;
            commandButton.IsEnabled = separate; suppressCommandButton.IsEnabled = separate;
            doubleTapWindow.IsEnabled = !separate;
        }
        commandEnabled.Click += (_, _) => CommandAvailability();
        separateCommandButton.Click += (_, _) => CommandAvailability(); CommandAvailability();
        Field(protection, "Text insertion", delivery);
        var sequence = new StackPanel();
        var timing = new StackPanel();
        sequence.Children.Add(new TextBlock { Text = "After you release the talk button and transcription finishes:", Margin = new Thickness(0, 14, 0, 0) });
        protection.Children.Add(blockKeyboard);
        protection.Children.Add(new TextBlock { Text = "Blocks your keyboard only while opening chat, typing and sending. Mouse remains usable. Esc cancels; 15 seconds is the maximum. Keys pressed during this time are discarded, not replayed.", FontSize = 12, Opacity = 0.7 });
        Field(sequence, "1 · Open chat (None if already open)", open);
        Pair(timing, "Wait after opening (ms)", openDelay, "Delay between characters (ms)", characterDelay);
        sequence.Children.Add(sendAutomatically);
        var sendPanel = new StackPanel();
        Field(sendPanel, "2 · Send message", send);
        Field(timing, "Wait before sending (ms)", sendDelay);
        Field(sendPanel, "3 · Close chat (optional; None for War Dogs)", close);
        Field(timing, "Wait before closing (ms)", closeDelay);
        sequence.Children.Add(sendPanel);
        Field(timing, "Button down → up duration (ms)", pressDuration);
        panel.Children.Add(SectionCard.Create("Chat sequence", "Open chat → type → send → optional close", sequence));
        panel.Children.Add(SectionCard.Create("Chat timings", "Opening, typing, sending and button press delays", timing));
        panel.Children.Add(commandCard);
        panel.Children.Add(SectionCard.Create("Input & keyboard protection", "Typing method and input blocking during delivery", protection));
        void Availability()
        {
            sequence.IsEnabled = timing.IsEnabled = delivery.SelectedIndex == 0;
            sendPanel.IsEnabled = sendAutomatically.IsChecked == true;
        }
        delivery.SelectionChanged += (_, _) => Availability();
        sendAutomatically.Click += (_, _) => Availability(); Availability();
        panel.Children.Add(new TextBlock { Text = "1,000 ms = 1 second. Each action presses and releases its button. Keep the game active until finished. Cancelling stops later steps; it does not erase or resend text already delivered.", FontSize = 12, Opacity = 0.7, Margin = new Thickness(0, 12, 0, 0) });
        var footer = new StackPanel { Margin = new Thickness(22, 0, 22, 18) };
        footer.Children.Add(feedback);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel" }; cancel.Click += (_, _) => Close();
        save.Click += (_, _) => Save(); buttons.Children.Add(cancel); buttons.Children.Add(save); footer.Children.Add(buttons);
        var layout = new DockPanel();
        DockPanel.SetDock(header, Dock.Top); layout.Children.Add(header);
        DockPanel.SetDock(footer, Dock.Bottom); layout.Children.Add(footer);
        layout.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = layout;
        if (hook != null) hook.Learned += OnLearned;
        learnTimer.Tick += (_, _) => { learnTimer.Stop(); hook?.CancelLearning(); };
        Closed += (_, _) => { learnTimer.Stop(); hook?.CancelLearning(); if (hook != null) hook.Learned -= OnLearned; };
        Loaded += async (_, _) => await RefreshProcesses();
    }
    private static void Field(Panel panel, string label, UIElement value)
    {
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 12, 0, 5), Opacity = 0.85 }); panel.Children.Add(value);
    }
    private static void Pair(Panel panel, string a, UIElement first, string b, UIElement second)
    {
        var grid = new Grid(); grid.ColumnDefinitions.Add(new()); grid.ColumnDefinitions.Add(new() { Width = new GridLength(14) }); grid.ColumnDefinitions.Add(new());
        var left = new StackPanel(); var right = new StackPanel(); Field(left, a, first); Field(right, b, second);
        Grid.SetColumn(right, 2); grid.Children.Add(left); grid.Children.Add(right); panel.Children.Add(grid);
    }
    private async Task RefreshProcesses()
    {
        processes.ItemsSource = await Task.Run(() => Process.GetProcesses().Select(p =>
        {
            using (p)
            {
                try
                {
                    if (p.Id == Environment.ProcessId || p.MainWindowHandle == 0) return null;
                    string? path = null; try { path = p.MainModule?.FileName; } catch (Win32Exception) { }
                    return new ProcessChoice(p.ProcessName + ".exe", path);
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { return null; }
            }
        }).Where(p => p != null).Distinct().OrderBy(p => p!.Name).ToArray());
        if (processes.Items.Count > 0) processes.SelectedIndex = 0;
    }
    private void Learn(BindingPicker picker)
    {
        if (hook == null) return;
        if (hook.IsHeld || hook.IsLearning) { feedback.Text = "Finish the current press or press Esc to cancel learning."; return; }
        learning = picker; picker.ShowLearning(true); save.IsEnabled = false;
        feedback.Text = "Listening… press and release a key or mouse button. Esc cancels; timeout is 20 seconds.";
        hook.BeginLearning(); learnTimer.Start();
    }
    private void OnLearned(InputBinding? value) => Dispatcher.BeginInvoke(() =>
    {
        if (!IsVisible) return;
        learnTimer.Stop(); if (value != null && learning != null) learning.Value = value;
        learning?.ShowLearning(false);
        feedback.Text = value == null ? "Learning cancelled. Previous choice kept." : $"Learned {value}. Save the profile to apply it.";
        learning = null; save.IsEnabled = true;
    });
    private void Save()
    {
        try
        {
            if (hook?.IsLearning == true) return;
            Result = (original with { Name = name.Text, Executable = executable.Text, AutoActivate = automatic.IsChecked == true,
                Trigger = trigger.Value!, SuppressTrigger = suppress.IsChecked == true, Delivery = delivery.SelectedIndex == 0 ? TextDelivery.Unicode : TextDelivery.NotepadEditor,
                BlockKeyboardWhileSending = blockKeyboard.IsChecked == true, OpenChat = open.Value, SendAutomatically = sendAutomatically.IsChecked == true, Send = send.Value!, CloseChat = close.Value,
                OpenDelayMs = Number(openDelay, 0, 10000), CharacterDelayMs = Number(characterDelay, 0, 100),
                BeforeSendDelayMs = Number(sendDelay, 0, 10000), BeforeCloseDelayMs = Number(closeDelay, 0, 10000), PressDurationMs = Number(pressDuration, 1, 1000),
                ChatCommand = new() { Enabled = commandEnabled.IsChecked == true, UseSeparateButton = separateCommandButton.IsChecked == true,
                    Button = commandButton.Value!, SuppressButton = suppressCommandButton.IsChecked == true,
                    DoubleTapWindowMs = Number(doubleTapWindow, 100, 1000), First = commandFirst.Value, Second = commandSecond.Value, Third = commandThird.Value,
                    FirstDelayMs = Number(commandFirstDelay, 0, 10000), SecondDelayMs = Number(commandSecondDelay, 0, 10000),
                    PressDurationMs = Number(commandPressDuration, 1, 1000) } }).Validate();
            DialogResult = true;
        }
        catch (Exception ex) { feedback.Text = ex.Message; }
    }
    private static int Number(TextBox box, int min, int max) => int.TryParse(box.Text, out int value) && value >= min && value <= max
        ? value : throw new InvalidOperationException($"Timing must be a whole number between {min} and {max} ms.");
}
