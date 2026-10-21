using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ElkaVoicePlay.Core;
using ElkaVoicePlay.Shared;

namespace ElkaVoicePlay.App;

public sealed class DownloadsView : UserControl
{
    private static readonly HttpClient Client = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly Dictionary<string, Button> buttons = new();
    private readonly Dictionary<string, TextBlock> states = new();
    private readonly Button all = new() { Content = "Install all extras", Background = new SolidColorBrush(Color.FromRgb(40, 121, 104)) };
    private readonly Button cancel = new() { Content = "Cancel download", IsEnabled = false, Margin = new Thickness(8, 0, 0, 0) };
    private readonly TextBlock message = new() { Text = "Game chat and Base are included. Download extras only when you want them.", Margin = new Thickness(0, 8, 0, 8) };
    private readonly ProgressBar progress = new() { Minimum = 0, Maximum = 100, Height = 5, Foreground = Brushes.MediumAquamarine };
    private CancellationTokenSource? cancellation;
    private Task active = Task.CompletedTask;
    public bool IsBusy => cancellation != null;
    public event Action? BusyChanged;
    public event Action? Installed;
    private readonly IReadOnlyList<OptionalComponent> components;

    public DownloadsView(string? componentId = null)
    {
        components = componentId == null ? OptionalComponents.All : new[] { OptionalComponents.Get(componentId) };
        bool transcription = componentId == TranscriptionModule.Id;
        var panel = new StackPanel { Margin = new Thickness(28, 0, 28, 20) };
        panel.Children.Add(new TextBlock { Text = transcription ? "Transcribe audio" : "Downloads", FontSize = 28, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = transcription ? "Download once to add multi-input transcription to VoicePlay" : "Add more power when you need it", Opacity = 0.7, Margin = new Thickness(0, 4, 0, 16) });
        foreach (var component in components)
        {
            var body = new StackPanel(); var row = new DockPanel();
            var button = new Button { Content = "Download", MinWidth = 110, Margin = new Thickness(18, 0, 0, 0) };
            buttons[component.Id] = button; button.Click += async (_, _) => await BeginAsync([component]);
            DockPanel.SetDock(button, Dock.Right); row.Children.Add(button);
            row.Children.Add(new TextBlock { Text = component.Name, FontSize = 19, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center }); body.Children.Add(row);
            body.Children.Add(new TextBlock { Text = component.Description, Margin = new Thickness(0, 8, 0, 8), Opacity = 0.8 });
            var status = new TextBlock { FontSize = 12, Foreground = Brushes.MediumAquamarine }; states[component.Id] = status; body.Children.Add(status);
            panel.Children.Add(new Border { Background = (Brush)FindResource("Panel"), Padding = new Thickness(18), CornerRadius = new CornerRadius(10), Child = body, Margin = new Thickness(0, 0, 0, 12) });
        }
        panel.Children.Add(new TextBlock { Text = "Downloads come from the public Elka VoicePlay GitHub release. Files are checked before installation. No account or administrator access is needed. Your recordings stay on this computer.", Opacity = 0.7, FontSize = 12, Margin = new Thickness(0, 2, 0, 10) });
        var github = new Button { Content = "View GitHub release", HorizontalAlignment = HorizontalAlignment.Left };
        github.Click += (_, _) => { try { Process.Start(new ProcessStartInfo(OptionalComponents.Repository + "/releases/tag/" + OptionalComponents.ReleaseTag) { UseShellExecute = true }); } catch (Exception ex) { message.Text = ex.Message; } };
        panel.Children.Add(github);
        panel.Children.Add(new TextBlock { Text = "Installed extras are shared by your copies of VoicePlay on this PC. Transcribe audio activates after its download. Medium and Large-v3 can also be selected under Game speech model; each mode keeps its own choice.", Opacity = 0.7, FontSize = 12, Margin = new Thickness(0, 14, 0, 0) });
        var footer = new StackPanel { Margin = new Thickness(28, 12, 28, 18) }; footer.Children.Add(message); footer.Children.Add(progress);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) }; actions.Children.Add(all); actions.Children.Add(cancel); footer.Children.Add(actions);
        all.Visibility = transcription ? Visibility.Collapsed : Visibility.Visible;
        all.Click += async (_, _) => await BeginAsync(components);
        cancel.Click += (_, _) => { cancellation?.Cancel(); cancel.IsEnabled = false; message.Text = "Cancelling… completed downloads will be reused on retry."; };
        var layout = new DockPanel(); DockPanel.SetDock(footer, Dock.Bottom); layout.Children.Add(footer);
        layout.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); Content = layout;
        Refresh();
    }
    public void Refresh()
    {
        foreach (var c in components)
        {
            bool installed = OptionalComponents.IsInstalled(c);
            states[c.Id].Text = (installed ? "Installed" : "Optional download") + " · " + OptionalComponents.SizeLabel(c.DownloadSize) + " download · " + OptionalComponents.SizeLabel(c.InstalledSize) + " on disk";
            buttons[c.Id].Content = installed ? "Installed" : "Download"; buttons[c.Id].IsEnabled = !IsBusy && !installed;
        }
        all.IsEnabled = !IsBusy && components.Any(c => !OptionalComponents.IsInstalled(c));
    }
    private async Task BeginAsync(IReadOnlyList<OptionalComponent> components)
    {
        if (IsBusy) return;
        cancellation = new(); cancel.IsEnabled = true; Refresh(); BusyChanged?.Invoke();
        active = InstallAsync(components, cancellation.Token);
        try { await active; }
        finally { cancellation.Dispose(); cancellation = null; cancel.IsEnabled = false; Refresh(); BusyChanged?.Invoke(); }
        if (components.All(c => OptionalComponents.IsInstalled(c))) Installed?.Invoke();
    }
    private async Task InstallAsync(IReadOnlyList<OptionalComponent> components, CancellationToken token)
    {
        try
        {
            var installer = new ComponentInstaller(Client, OptionalComponents.Root);
            var updates = new Progress<ComponentProgress>(p => { message.Text = p.Stage + $" · {OptionalComponents.SizeLabel(p.Completed)} / {OptionalComponents.SizeLabel(p.Total)}"; progress.Value = p.Total == 0 ? 0 : 100d * p.Completed / p.Total; });
            foreach (var component in components) await Task.Run(() => installer.InstallAsync(component, updates, token), token);
            progress.Value = 100; message.Text = "Installed. Choose your models in Transcribe audio → Models & speakers.";
        }
        catch (OperationCanceledException) { message.Text = token.IsCancellationRequested ? "Download cancelled. Click Download to retry; completed parts will be reused." : "The download timed out. Check your connection, then retry."; }
        catch (Exception ex) { message.Text = "Download could not finish. " + ex.Message + " Click Download to retry."; }
    }
    public async Task StopAndWaitAsync() { cancellation?.Cancel(); await active; }
}
