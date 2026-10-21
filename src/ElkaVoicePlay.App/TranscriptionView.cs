using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ElkaVoicePlay.Audio;
using ElkaVoicePlay.Core;
using ElkaVoicePlay.Shared;
using ElkaVoicePlay.Speech;
using Forms = System.Windows.Forms;

namespace ElkaVoicePlay.App;

public sealed class TranscriptionView : UserControl
{
    private static readonly TranscriptModel[] ModelOrder = [TranscriptModel.Base, TranscriptModel.Medium, TranscriptModel.LargeV3];
    private static string? ComponentFor(TranscriptModel model) => model switch { TranscriptModel.Medium => "medium", TranscriptModel.LargeV3 => "large-v3", _ => null };
    private readonly ComboBox transcriptModel = new() { ItemsSource = new[] { "Whisper Base · included", "Whisper Medium · optional", "Whisper Large-v3 · optional" }, SelectedIndex = 0 };
    private readonly ComboBox speakerModel = new() { ItemsSource = new[] { "Standard speakers · Classic (CPU)", "Nemotron-3 · up to 8 speakers per input" }, SelectedIndex = 0 };
    private readonly ComboBox acceleration = new() { ItemsSource = new[] { "Automatic · NVIDIA GPU when available", "CPU", "NVIDIA GPU" }, SelectedIndex = 0 };
    private readonly TranscriptionSettingsStore store = new(Path.Combine(DataPaths.Root, "transcription-settings.json"));
    private TranscriptionSettings settings = new();
    private readonly StackPanel options = new();
    private readonly CheckBox combine = new() { Content = "Combine all enabled inputs into one TXT file", IsChecked = true };
    private readonly TextBox folder = new();
    private readonly ComboBox language = new();
    private readonly ComboBox outputLanguage = new() { ItemsSource = new[] { "Same as input (transcribe)", "English (translate)" }, SelectedIndex = 0 };
    private readonly CheckBox labelSpeakers = new() { Content = "Label speakers · Speaker 1, Speaker 2… (experimental)", Margin = new Thickness(0, 8, 0, 0) };
    private readonly InputRow[] rows;
    private readonly TextBlock state = new() { Text = "Choose inputs", FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = Brushes.MediumAquamarine };
    private readonly TextBlock detail = new() { Text = "Enable the sources you want, give them names, then start.", Margin = new Thickness(0, 6, 0, 0) };
    private readonly TextBlock elapsed = new() { Text = "00:00:00 · not recording", Margin = new Thickness(0, 5, 0, 0) };
    private readonly TextBlock output = new() { Text = "A new session folder and TXT file(s) will be created automatically.", Margin = new Thickness(0, 8, 0, 8) };
    private readonly TextBox preview = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 150, MaxHeight = 240 };
    private readonly Button start = new() { Content = "Start transcription", Background = new SolidColorBrush(Color.FromRgb(40, 121, 104)), IsEnabled = false };
    private readonly Button stop = new() { Content = "Stop & save", IsEnabled = false, Margin = new Thickness(8, 0, 0, 0) };
    private readonly Button openFolder = new() { Content = "Open folder", Margin = new Thickness(8, 0, 0, 0) };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly float[] levels = new float[3];
    private readonly List<TranscriptLine> recentLines = [];
    private readonly TextBlock modelLabel = new() { Opacity = 0.7, FontSize = 12, Margin = new Thickness(0, 5, 0, 10) };
    private Task? initialization;
    private readonly Stopwatch stopwatch = new();
    private CancellationTokenSource? captureStop;
    private TranscriptionSession? session;
    private Task activeTask = Task.CompletedTask;
    private bool busy, settingsWritable = true;
    private string? sessionDirectory;
    private sealed record LanguageChoice(string Code, string Name) { public override string ToString() => Name; }
    private static readonly LanguageChoice[] Languages =
    [new("auto", "Automatic"), new("en", "English"), new("is", "Icelandic"), new("de", "German"), new("fr", "French"),
     new("es", "Spanish"), new("da", "Danish"), new("no", "Norwegian"), new("sv", "Swedish"), new("pl", "Polish"),
     new("pt", "Portuguese"), new("it", "Italian"), new("ja", "Japanese"), new("ko", "Korean"), new("zh", "Chinese"), new("uk", "Ukrainian")];

    public TranscriptionView(string initialLanguage, Action? openDownloads = null)
    {
        settings = settings with { Language = initialLanguage };
        var panel = new StackPanel { Margin = new Thickness(26, 0, 26, 20) };
        panel.Children.Add(new TextBlock { Text = "Transcribe audio", FontSize = 28, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 4) });
        panel.Children.Add(new TextBlock { Text = "Turn up to three audio sources into timestamped text. Game controls are paused on this tab.", Opacity = 0.75, Margin = new Thickness(0, 0, 0, 12) });
        var status = new StackPanel(); status.Children.Add(state); status.Children.Add(detail); status.Children.Add(elapsed);
        panel.Children.Add(new Border { Background = (Brush)FindResource("Panel"), CornerRadius = new CornerRadius(10), Padding = new Thickness(14), Child = status, Margin = new Thickness(0, 0, 0, 12) });
        rows = Enumerable.Range(1, 3).Select(i => new InputRow(i)).ToArray();
        var inputs = new StackPanel(); var languages = new StackPanel(); var models = new StackPanel(); var files = new StackPanel();
        foreach (var row in rows) inputs.Children.Add(row.Panel);
        var refresh = new Button { Content = "Refresh audio devices", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 10) };
        refresh.Click += async (_, _) => await RefreshDevicesAsync(); inputs.Children.Add(refresh);
        inputs.Children.Add(new TextBlock { Text = "Microphone / input includes USB microphones and VoiceMeeter recording endpoints. Speakers / loopback captures what plays through the selected headphones or speakers.", Opacity = 0.7, FontSize = 12, Margin = new Thickness(0, 0, 0, 10) });
        languages.Children.Add(new TextBlock { Text = "Input language", Margin = new Thickness(0, 0, 0, 5) });
        language.ItemsSource = Languages; language.SelectedItem = Languages.FirstOrDefault(l => l.Code == initialLanguage) ?? Languages[0]; languages.Children.Add(language);
        languages.Children.Add(new TextBlock { Text = "Output language", Margin = new Thickness(0, 8, 0, 5) });
        languages.Children.Add(outputLanguage);
        models.Children.Add(new TextBlock { Text = "Transcription model", Margin = new Thickness(0, 10, 0, 5) });
        models.Children.Add(transcriptModel);
        models.Children.Add(labelSpeakers);
        models.Children.Add(new TextBlock { Text = "Speaker model", Margin = new Thickness(0, 8, 0, 5) });
        models.Children.Add(speakerModel);
        labelSpeakers.Checked += (_, _) => speakerModel.IsEnabled = true;
        labelSpeakers.Unchecked += (_, _) => speakerModel.IsEnabled = false;
        models.Children.Add(new TextBlock { Text = "Processing", Margin = new Thickness(0, 8, 0, 5) });
        models.Children.Add(acceleration);
        models.Children.Add(new TextBlock { Text = "Speaker labels are estimates within each input and session, not names. Overlapping voices are marked separately; unclear voices can still be unknown. Nemotron remembers speakers across phrases.", TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 12 });
        models.Children.Add(modelLabel);
        files.Children.Add(combine);
        files.Children.Add(new TextBlock { Text = "Untick for a separate TXT file per enabled input. Combined lines are sorted by speech start time. Speaker labels, when enabled, appear beside the input name.", Opacity = 0.7, FontSize = 12, Margin = new Thickness(0, 0, 0, 10) });
        files.Children.Add(new TextBlock { Text = "Save transcripts in", Margin = new Thickness(0, 0, 0, 5) });
        var folderRow = new DockPanel(); var browse = new Button { Content = "Browse…", Margin = new Thickness(8, 0, 0, 0) };
        browse.Click += (_, _) => { using var dialog = new Forms.FolderBrowserDialog { Description = "Choose the transcript output folder", UseDescriptionForTitle = true, SelectedPath = folder.Text }; if (dialog.ShowDialog() == Forms.DialogResult.OK) folder.Text = dialog.SelectedPath; };
        DockPanel.SetDock(browse, Dock.Right); folderRow.Children.Add(browse); folderRow.Children.Add(folder); files.Children.Add(folderRow);
        var downloads = new Button { Content = "Get optional models & NVIDIA support", Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        downloads.Click += (_, _) => openDownloads?.Invoke(); models.Children.Add(downloads);
        options.Children.Add(SectionCard.Create("Audio inputs", "Choose and name up to three microphones or loopback sources", inputs));
        options.Children.Add(SectionCard.Create("Language & translation", "Input language and optional English output", languages));
        options.Children.Add(SectionCard.Create("Models & speakers", "Base and standard speakers included · optional models in Downloads", models));
        options.Children.Add(SectionCard.Create("Transcript files", "Combined or separate TXT files and save location", files));
        panel.Children.Add(options);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        start.Click += async (_, _) => { if (!busy) { activeTask = RunAsync(); await activeTask; } };
        stop.Click += async (_, _) => await StopAndWaitAsync();
        openFolder.Click += (_, _) => { try { var path = sessionDirectory ?? folder.Text; Directory.CreateDirectory(path); Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch (Exception ex) { detail.Text = ex.Message; } };
        buttons.Children.Add(start); buttons.Children.Add(stop); buttons.Children.Add(openFolder);
        panel.Children.Add(output);
        panel.Children.Add(new TextBlock { Text = "Completed lines · each line is added once, after the phrase is ready", Opacity = 0.75, Margin = new Thickness(0, 6, 0, 6) });
        panel.Children.Add(preview);
        panel.Children.Add(new TextBlock { Text = "Timestamps share the session clock; speech boundaries are estimates, not frame-exact video timecodes. Audio is processed in memory and is not saved.", Opacity = 0.65, FontSize = 12, Margin = new Thickness(0, 8, 0, 0) });
        var layout = new DockPanel();
        buttons.Margin = new Thickness(26, 12, 26, 14);
        DockPanel.SetDock(buttons, Dock.Bottom); layout.Children.Add(buttons);
        layout.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = layout;
        timer.Tick += (_, _) =>
        {
            elapsed.Text = $"{TranscriptFiles.FormatTime(stopwatch.Elapsed)} · {(captureStop?.IsCancellationRequested == true ? "finishing captured audio" : "recording")} · {session?.PendingWindows ?? 0} audio packets queued";
            for (int i = 0; i < rows.Length; i++) rows[i].Level.Value = Volatile.Read(ref levels[i]);
        };

    }
    private async Task LoadAsync()
    {
        try
        {
            if (File.Exists(Path.Combine(DataPaths.Root, "transcription-settings.json"))) settings = await store.LoadAsync();
        }
        catch (Exception ex) { settingsWritable = false; state.Text = "Settings need attention"; detail.Text = ex.Message + " Session defaults are available; the existing file will be preserved."; }
        folder.Text = settings.Folder; combine.IsChecked = settings.Combine;
        outputLanguage.SelectedIndex = settings.TranslateToEnglish ? 1 : 0; labelSpeakers.IsChecked = settings.LabelSpeakers;
        language.SelectedItem = Languages.FirstOrDefault(l => l.Code == settings.Language) ?? Languages[0];
        foreach (var row in rows) row.Apply(settings.Sources.Single(s => s.Slot == row.Slot));
        await RefreshDevicesAsync();
        string? requestedComponent = ComponentFor(settings.TranscriptModel);
        bool missingSpeech = requestedComponent != null && !OptionalComponents.IsInstalled(requestedComponent);
        bool fallback = missingSpeech ||
            settings.SpeakerModel == SpeakerModel.Nemotron3 && !OptionalComponents.IsInstalled("nemotron");
        transcriptModel.SelectedIndex = missingSpeech ? 0 : Array.IndexOf(ModelOrder, settings.TranscriptModel);
        speakerModel.SelectedIndex = settings.SpeakerModel == SpeakerModel.Nemotron3 && OptionalComponents.IsInstalled("nemotron") ? 1 : 0;
        if (fallback) detail.Text = "Using included Base and standard speakers. Your optional models can be installed from Downloads.";
        acceleration.SelectedIndex = settings.Acceleration == ModelAcceleration.NvidiaGpu && !OptionalComponents.IsInstalled("nvidia") ? 0 : (int)settings.Acceleration; speakerModel.IsEnabled = settings.LabelSpeakers;
        start.IsEnabled = true;
    }
    private async Task RefreshDevicesAsync()
    {
        try
        {
            var devices = await Task.Run(TranscriptCapture.Enumerate);
            foreach (var row in rows) row.SetDevices(devices);
        }
        catch (Exception ex) { detail.Text = "Audio devices: " + ex.Message; }
    }
    private TranscriptionSettings ReadSettings() => (settings with { Combine = combine.IsChecked == true,
        TranslateToEnglish = outputLanguage.SelectedIndex == 1, LabelSpeakers = labelSpeakers.IsChecked == true,
        TranscriptModel = ModelOrder[transcriptModel.SelectedIndex], SpeakerModel = (SpeakerModel)speakerModel.SelectedIndex,
        Acceleration = (ModelAcceleration)acceleration.SelectedIndex,
        Folder = folder.Text.Trim(), Language = (language.SelectedItem as LanguageChoice)?.Code ?? "auto", Sources = rows.Select(r => r.Read()).ToArray() }).Validate();
    private async Task RunAsync()
    {
        busy = true; start.IsEnabled = false; stop.IsEnabled = true; options.IsEnabled = false;
        captureStop = new(); recentLines.Clear(); preview.Clear(); sessionDirectory = null;
        state.Text = "Preparing"; detail.Text = "Opening the enabled audio sources and creating transcript files…";
        await using var speech = new ModelWorkerClient();
        await using var diarizer = new ModelWorkerClient();
        try
        {
            settings = ReadSettings();
            string models = Path.Combine(AppContext.BaseDirectory, "Models");
            string? component = ComponentFor(settings.TranscriptModel);
            string speechPath = component != null ? OptionalComponents.FileFor(component, "Models/ggml-" + component + ".bin") : Path.Combine(models, "ggml-base.bin");
            if (component != null && !OptionalComponents.IsInstalled(component))
                throw new InvalidOperationException("This transcription model is not installed. Open Downloads to install it, or choose Whisper Base.");
            if (settings.LabelSpeakers && settings.SpeakerModel == SpeakerModel.Nemotron3 && !OptionalComponents.IsInstalled("nemotron"))
                throw new InvalidOperationException("Nemotron is not installed. Open Downloads to install it, or choose the standard speaker model.");
            if (settings.Acceleration == ModelAcceleration.NvidiaGpu && !OptionalComponents.IsInstalled("nvidia"))
                throw new InvalidOperationException("NVIDIA support is not installed. Open Downloads to install it, or choose Automatic / CPU.");
            detail.Text = "Loading transcription model… capture starts when the models are ready.";
            await speech.StartAsync("whisper", speechPath, settings.Acceleration, captureStop.Token);
            if (settings.LabelSpeakers)
            {
                detail.Text = "Loading speaker model…";
                bool nemo = settings.SpeakerModel == SpeakerModel.Nemotron3;
                await diarizer.StartAsync(nemo ? "nemotron" : "classic", nemo ? OptionalComponents.FileFor("nemotron", "Models/Nemotron-3-Diarization.q8_0.gguf") : Path.Combine(models, "Speakers"), settings.Acceleration, captureStop.Token);
            }
            captureStop.Token.ThrowIfCancellationRequested();
            modelLabel.Text = $"Transcription: {speech.Backend}" + (settings.LabelSpeakers ? $" · Speakers: {diarizer.Backend}" : "");
            if (settingsWritable) await store.SaveAsync(settings);
            session = new(new TranscriptCapture(), speech, settings.LabelSpeakers ? diarizer : null);
            session.LevelChanged += (slot, value) => Volatile.Write(ref levels[slot - 1], value);
            session.FilesCreated += (directory, files) => Dispatcher.BeginInvoke(() =>
            { sessionDirectory = directory; output.Text = string.Join(Environment.NewLine, files); state.Text = captureStop?.IsCancellationRequested == true ? "Finishing" : "Recording"; detail.Text = "Text is saved automatically. Stop & save finishes the captured audio."; });
            session.LinesWritten += lines => Dispatcher.BeginInvoke(() =>
            {
                recentLines.AddRange(lines);
                if (recentLines.Count > 200) recentLines.RemoveRange(0, recentLines.Count - 200);
                RenderPreview();
            });
            stopwatch.Restart(); timer.Start();
            var result = await Task.Run(() => session.RunAsync(settings, captureStop.Token));
            sessionDirectory = result.Directory;
            state.Text = result.Error == null ? "Saved" : "Stopped with an error";
            detail.Text = result.Error ?? $"Saved {result.Lines} transcript lines. You can start a new recording or return to Game profiles.";
            output.Text = string.Join(Environment.NewLine, result.Files);
        }
        catch (OperationCanceledException) when (captureStop.IsCancellationRequested) { state.Text = "Stopped"; detail.Text = "Model preparation cancelled."; }
        catch (Exception ex) { state.Text = "Could not complete transcription"; detail.Text = ex.Message; }
        finally
        {
            await diarizer.DisposeAsync(); await speech.DisposeAsync();
            timer.Stop(); stopwatch.Stop(); elapsed.Text = TranscriptFiles.FormatTime(stopwatch.Elapsed) + " · stopped";
            captureStop.Dispose(); captureStop = null; session = null; busy = false; stop.IsEnabled = false; start.IsEnabled = true; options.IsEnabled = true;
            foreach (var row in rows) row.Level.Value = 0;
        }
    }
    public async Task StopAndWaitAsync()
    {
        if (busy) { state.Text = "Finishing"; detail.Text = "Capture has stopped. Finishing buffered audio and saving the files…"; stop.IsEnabled = false; stopwatch.Stop(); captureStop?.Cancel(); }
        await activeTask;
    }
    public async Task ActivateAsync()
    {
        initialization ??= LoadAsync();
        await initialization;
        if (!busy)
        {
            int t = transcriptModel.SelectedIndex, s = speakerModel.SelectedIndex, a = acceleration.SelectedIndex;
            transcriptModel.ItemsSource = new[] { "Whisper Base · included", "Whisper Medium · " + (OptionalComponents.IsInstalled("medium") ? "installed" : "download required"), "Whisper Large-v3 · " + (OptionalComponents.IsInstalled("large-v3") ? "installed" : "download required") };
            speakerModel.ItemsSource = new[] { "Standard speakers · Classic (CPU)", "Nemotron-3 · " + (OptionalComponents.IsInstalled("nemotron") ? "installed" : "download required") };
            acceleration.ItemsSource = new[] { "Automatic · CPU unless NVIDIA support is installed", "CPU", "NVIDIA GPU · " + (OptionalComponents.IsInstalled("nvidia") ? "installed" : "download required") };
            transcriptModel.SelectedIndex = t; speakerModel.SelectedIndex = s; acceleration.SelectedIndex = a;
            modelLabel.Text = "Independent of Game profiles. Optional models are managed in Downloads.";
        }
        start.IsEnabled = !busy;
    }
    private void RenderPreview()
    {
        preview.Text = string.Join(Environment.NewLine, recentLines.Select(TranscriptFiles.Format));
        preview.ScrollToEnd();
    }
    private sealed class InputRow
    {
        public int Slot { get; }
        public Border Panel { get; }
        public ProgressBar Level { get; } = new() { Minimum = 0, Maximum = 1, Height = 4, Foreground = Brushes.MediumAquamarine, Background = new SolidColorBrush(Color.FromRgb(13, 20, 29)) };
        private readonly CheckBox enabled;
        private readonly TextBox name = new();
        private readonly ComboBox device = new();
        private TranscriptSource saved;
        public InputRow(int slot)
        {
            Slot = slot; saved = new() { Slot = slot };
            enabled = new() { Content = $"Use input {slot}", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            var content = new StackPanel(); var top = new DockPanel(); DockPanel.SetDock(enabled, Dock.Left); top.Children.Add(enabled); top.Children.Add(name);
            name.ToolTip = "Name shown beside this source's transcript lines"; device.ToolTip = "Choose a recording input or a speaker/headphone loopback";
            content.Children.Add(top); device.Margin = new Thickness(0, 6, 0, 6); content.Children.Add(device); content.Children.Add(Level);
            enabled.Click += (_, _) => Update();
            Panel = new() { Background = new SolidColorBrush(Color.FromRgb(25, 35, 49)), CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 9), Child = content };
        }
        public void Apply(TranscriptSource source) { saved = source; enabled.IsChecked = source.Enabled; name.Text = source.Name; Update(); }
        private void Update() { name.IsEnabled = device.IsEnabled = enabled.IsChecked == true; Panel.Opacity = enabled.IsChecked == true ? 1 : 0.65; }
        public void SetDevices(IReadOnlyList<AudioEndpointChoice> devices)
        {
            var prior = device.SelectedItem as AudioEndpointChoice;
            string? id = prior?.Id ?? saved.DeviceId;
            var kind = prior?.Kind ?? saved.Kind;
            var list = devices.ToList();
            var selected = list.FirstOrDefault(d => d.Id == id && d.Kind == kind);
            if (selected == null && id != null) { selected = new(id, "Unavailable device — choose another", kind); list.Add(selected); }
            selected ??= list.FirstOrDefault(d => d.Kind == kind && d.IsDefault) ?? list.FirstOrDefault(d => d.Kind == kind);
            device.ItemsSource = list; device.SelectedItem = selected;
        }
        public TranscriptSource Read() => new() { Slot = Slot, Enabled = enabled.IsChecked == true, Name = name.Text,
            Kind = (device.SelectedItem as AudioEndpointChoice)?.Kind ?? saved.Kind, DeviceId = (device.SelectedItem as AudioEndpointChoice)?.Id };
    }
}
