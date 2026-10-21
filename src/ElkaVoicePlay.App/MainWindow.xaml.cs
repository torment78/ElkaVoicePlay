using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ElkaVoicePlay.Audio;
using ElkaVoicePlay.Core;
using ElkaVoicePlay.Input;
using ElkaVoicePlay.Shared;
using ElkaVoicePlay.Speech;
using Forms = System.Windows.Forms;

namespace ElkaVoicePlay.App;

public partial class MainWindow : Window
{
    private readonly MicrophoneRecorder recorder = new();
    private readonly WhisperRecognizer speech = new();
    private readonly SettingsStore store = new(DataPaths.Settings);
    private readonly bool smokeTest;
    private readonly DispatcherTimer meterTimer;
    private AppSettings settings = new AppSettings().Validate();
    private DictationController? controller;
    private KeyboardHook? hook;
    private NotepadTextTarget? target;
    private ProfileTextTarget? profileTarget;
    private bool editingProfile, refreshingProfiles, transcriptMode;
    private TranscriptionView? transcriptionView;
    private bool switchingMode;
    private bool refreshingGameModels;
    private sealed record GameModelChoice(string Id, string Path, string Name, bool NeedsDownload)
    { public override string ToString() => Name; }
    private bool downloadsMode, transcriptionDownloadMode;
    private DownloadsView? downloadsView;
    private Forms.NotifyIcon? tray;
    private Forms.ToolStripMenuItem? trayEnabled;
    private bool loading, exiting, settingsWritable = true, previewHeld, keyboardSession;
    private float peak;
    private Task loadTask = Task.CompletedTask;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? inputTestCancellation;
    private CancellationTokenSource? commandCancellation;
    private Task commandTask = Task.CompletedTask;

    private sealed record LanguageOption(string Code, string Name)
    {
        public override string ToString() => Name;
    }
    private readonly LanguageOption[] languages =
    [
        new("auto", "Automatic"), new("en", "English"), new("is", "Icelandic"),
        new("de", "German"), new("fr", "French"), new("es", "Spanish"),
        new("da", "Danish"), new("no", "Norwegian"), new("sv", "Swedish"),
        new("pl", "Polish"), new("pt", "Portuguese"), new("it", "Italian"),
        new("ja", "Japanese"), new("ko", "Korean"), new("zh", "Chinese"), new("uk", "Ukrainian")
    ];

    public MainWindow(bool smokeTest)
    {
        this.smokeTest = smokeTest;
        InitializeComponent();
        VersionLabel.Text = $"ElkaSoft · {typeof(MainWindow).Assembly.GetName().Version?.ToString(3)} preview · speech stays on this computer";
        recorder.LevelChanged += value => Volatile.Write(ref peak, value);
        meterTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromMilliseconds(60) };
        meterTimer.Tick += (_, _) => LevelMeter.Value = Volatile.Read(ref peak);
        IsVisibleChanged += (_, _) => UpdateMeterActivity();
        StateChanged += (_, _) => UpdateMeterActivity();
        Activated += (_, _) => ConfigureHook();
        Deactivated += (_, _) => ConfigureHook();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            try { settings = await store.LoadAsync(); }
            catch (Exception ex)
            {
                settingsWritable = false;
                SetStatus("Settings need attention", $"{ex.Message} Defaults are active; the existing settings file will be preserved.", true);
            }
            LanguageBox.ItemsSource = languages;
            LanguageBox.SelectedItem = languages.FirstOrDefault(l => l.Code == settings.Language) ?? languages[0];
            RefreshProfiles();
            TrayCheck.IsChecked = settings.CloseToTray;
            MaxSecondsBox.Text = settings.MaximumRecordingSeconds.ToString();
            MaxLengthBox.Text = settings.MaximumMessageLength.ToString();
            ModelPathBox.Text = settings.ModelPath;
            RefreshGameModels();
            await RefreshDevicesAsync();
            target = new NotepadTextTarget();
            profileTarget = new(() => settings, target);
            profileTarget.StatusChanged += status => Dispatcher.BeginInvoke(() => ApplyStatus(status));
            controller = new(recorder, speech, profileTarget);
            controller.StatusChanged += status => Dispatcher.BeginInvoke(() => ApplyStatus(status));
            hook = new KeyboardHook();
            // Hook notifications are queued from a dedicated thread; WPF never blocks the native hook.
            hook.HoldChanged += down => Dispatcher.BeginInvoke(() =>
            {
                if (down)
                {
                    if (!transcriptMode && !loading && !exiting && !editingProfile && !IsActive && commandCancellation == null && EnabledCheck.IsChecked == true)
                        keyboardSession = controller.Begin(settings);
                }
                else if (keyboardSession) { controller.Release(); keyboardSession = false; }
            });
            hook.CancelRequested += () => Dispatcher.BeginInvoke(CancelActiveWork);
            hook.CommandRequested += request => Dispatcher.BeginInvoke(() =>
            { if (commandCancellation == null) commandTask = RunChatCommandAsync(request); });
            hook.Failed += error => Dispatcher.BeginInvoke(() => SetStatus("Input controls unavailable", error, true));
            CreateTray();
            UpdateInstructions();
            if (!smokeTest) await LoadModelAsync();
            RefreshAvailability();
            if (smokeTest) { await Task.Delay(2000); await ExitAsync(); }
        }
        catch (Exception ex)
        {
            SetStatus("Startup error", ex.Message, true);
            if (smokeTest) { System.Windows.Application.Current.Shutdown(1); }
        }
    }

    private void CreateTray()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open Elka VoicePlay", null, (_, _) => ShowWindow());
        trayEnabled = new Forms.ToolStripMenuItem("Enabled") { Checked = true };
        trayEnabled.Click += (_, _) => { EnabledCheck.IsChecked = !EnabledCheck.IsChecked; OnEnabledChanged(this, new RoutedEventArgs()); };
        menu.Items.Add(trayEnabled);
        menu.Items.Add("Cancel recording / transcription", null, (_, _) => CancelActiveWork());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, async (_, _) => await ExitAsync());
        tray = new Forms.NotifyIcon { Icon = new System.Drawing.Icon(System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/ElkaVoicePlay;component/Assets/ElkaVoicePlay.ico")).Stream), Visible = true, Text = "Elka VoicePlay", ContextMenuStrip = menu };
        tray.DoubleClick += (_, _) => ShowWindow();
    }

    private void ShowWindow() { Show(); WindowState = WindowState.Normal; Activate(); }
    private async void OnTranscribeAudio(object sender, RoutedEventArgs e)
    {
        if (loading || exiting || switchingMode || controller?.IsBusy == true || commandCancellation != null || hook?.IsHeld == true || transcriptMode && !downloadsMode || downloadsView?.IsBusy == true) return;
        transcriptMode = true; switchingMode = true; hook?.CancelPendingGesture();
        try
        {
            if (!TranscriptionModule.IsInstalled())
            {
                ShowDownloads(TranscriptionModule.Id);
                return;
            }
            transcriptionView ??= new TranscriptionView(settings.Language, () => OnDownloads(this, new RoutedEventArgs()));
            downloadsMode = false; transcriptionDownloadMode = false;
            TranscriptHost.Content = transcriptionView;
            GamePage.Visibility = Visibility.Collapsed; TranscriptHost.Visibility = Visibility.Visible;
            RefreshAvailability();
            await transcriptionView.ActivateAsync();
        }
        finally { switchingMode = false; RefreshAvailability(); }
    }
    private async void OnGameProfiles(object sender, RoutedEventArgs e)
    {
        if (!transcriptMode || switchingMode || exiting || downloadsView?.IsBusy == true) return;
        switchingMode = true; RefreshAvailability();
        try
        {
            if (transcriptionView != null) await transcriptionView.StopAndWaitAsync();
        if (downloadsView != null) await downloadsView.StopAndWaitAsync();
            if (exiting) return;
            TranscriptHost.Visibility = Visibility.Collapsed; GamePage.Visibility = Visibility.Visible;
            transcriptMode = false; downloadsMode = false; transcriptionDownloadMode = false;
            RefreshGameModels();
        }
        finally { switchingMode = false; RefreshAvailability(); }
    }
    private async void OnDownloads(object sender, RoutedEventArgs e)
    {
        if (downloadsMode && !transcriptionDownloadMode || downloadsView?.IsBusy == true || switchingMode || loading || exiting || controller?.IsBusy == true || commandCancellation != null || hook?.IsHeld == true) return;
        switchingMode = true; transcriptMode = true; hook?.CancelPendingGesture(); RefreshAvailability();
        try
        {
            if (transcriptionView != null) await transcriptionView.StopAndWaitAsync();
        if (downloadsView != null) await downloadsView.StopAndWaitAsync();
            ShowDownloads();
        }
        finally { switchingMode = false; RefreshAvailability(); }
    }
    private void ShowDownloads(string? componentId = null)
    {
        downloadsView = new DownloadsView(componentId);
        downloadsView.BusyChanged += RefreshAvailability;
        downloadsView.Installed += () =>
        {
            if (transcriptionDownloadMode && !exiting && TranscriptionModule.IsInstalled()) OnTranscribeAudio(this, new RoutedEventArgs());
        };
        downloadsMode = true; transcriptionDownloadMode = componentId == TranscriptionModule.Id;
        TranscriptHost.Content = downloadsView; GamePage.Visibility = Visibility.Collapsed; TranscriptHost.Visibility = Visibility.Visible;
    }
    private void SetStatus(string title, string detail, bool error = false)
    {
        StateLabel.Text = title;
        StateLabel.Foreground = new SolidColorBrush(error ? System.Windows.Media.Color.FromRgb(255, 164, 152) : System.Windows.Media.Color.FromRgb(105, 199, 178));
        StatusLabel.Text = detail;
        if (tray != null) tray.Text = ("Elka VoicePlay · " + title)[..Math.Min(63, 17 + title.Length)];
    }

    private void ApplyStatus(SessionStatus status)
    {
        recording = status.State == SessionState.Recording;
        UpdateMeterActivity();
        if (status.Text != null) TranscriptBox.Text = status.Text;
        SetStatus(status.IsError ? "Attention" : status.State == SessionState.Idle ? "Ready" : status.State.ToString(), status.Message, status.IsError);
        RefreshAvailability();
    }

    private void RefreshAvailability()
    {
        bool busy = transcriptMode || loading || controller?.IsBusy == true || commandCancellation != null || exiting;
        SettingsPanel.IsEnabled = !busy;
        TranscribeAudioButton.IsEnabled = (!busy || downloadsMode && !transcriptionDownloadMode) && !switchingMode && !exiting && downloadsView?.IsBusy != true;
        DownloadsButton.IsEnabled = (!downloadsMode || transcriptionDownloadMode) && downloadsView?.IsBusy != true && !loading && !switchingMode && !exiting && controller?.IsBusy != true && commandCancellation == null;
        GameProfilesButton.IsEnabled = transcriptMode && !switchingMode && !exiting && downloadsView?.IsBusy != true;
        PreviewButton.IsEnabled = !loading && !exiting && speech.IsReady && EnabledCheck.IsChecked == true && (previewHeld || !busy);
        CancelButton.IsEnabled = busy && !exiting;
        TestInputButton.IsEnabled = !busy && target != null && EnabledCheck.IsChecked == true;
        ConfigureHook();
    }

    private bool recording;
    private void UpdateMeterActivity()
    {
        if (meterTimer == null) return;
        if (recording && IsVisible && WindowState != WindowState.Minimized && !exiting) meterTimer.Start();
        else { meterTimer.Stop(); LevelMeter.Value = 0; }
    }
    private void ConfigureHook() => hook?.Configure(HookConfiguration.ForSettings(settings, EnabledCheck.IsChecked == true && !transcriptMode,
        !loading && !exiting && !editingProfile && !IsActive && speech.IsReady && controller?.IsBusy != true && commandCancellation == null));

    private async Task RefreshDevicesAsync()
    {
        var selected = (MicrophoneBox.SelectedItem as MicrophoneInfo)?.Id ?? settings.MicrophoneId;
        try
        {
            var devices = await Task.Run(MicrophoneRecorder.Enumerate);
            var list = devices.ToList();
            if (selected != null && !list.Any(d => d.Id == selected)) list.Add(new(selected, "Unavailable microphone — choose another"));
            MicrophoneBox.ItemsSource = list;
            MicrophoneBox.SelectedItem = list.FirstOrDefault(d => d.Id == selected) ?? list[0];
        }
        catch (Exception ex) { SetStatus("Microphone unavailable", ex.Message, true); }
    }
    private async void OnRefreshDevices(object sender, RoutedEventArgs e) => await RefreshDevicesAsync();

    private void OnBrowseModel(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Whisper ggml model (*.bin)|*.bin", Title = "Choose a local Whisper model" };
        if (Directory.Exists(DataPaths.Models)) dialog.InitialDirectory = DataPaths.Models;
        if (dialog.ShowDialog(this) == true) { ModelPathBox.Text = dialog.FileName; RefreshGameModels(); }
    }
    private void RefreshGameModels()
    {
        string requested = ModelPathBox.Text;
        string basePath = Path.GetFileName(requested).Equals("ggml-base.bin", StringComparison.OrdinalIgnoreCase) ? requested : Path.Combine(DataPaths.Models, "ggml-base.bin");
        var choices = new List<GameModelChoice> { new("base", basePath, "Whisper Base · included · fastest", false) };
        foreach (string id in new[] { "medium", "large-v3" })
        {
            bool missing = !OptionalComponents.IsInstalled(id);
            choices.Add(new(id, OptionalComponents.FileFor(id, "Models/ggml-" + id + ".bin"),
                OptionalComponents.Get(id).Name + (missing ? " · download required" : " · installed"), missing));
        }
        var selected = choices.FirstOrDefault(c => string.Equals(c.Path, requested, StringComparison.OrdinalIgnoreCase));
        if (selected == null) { selected = new("custom", requested, "Custom · " + Path.GetFileName(requested), false); choices.Add(selected); }
        refreshingGameModels = true; GameModelBox.ItemsSource = choices; GameModelBox.SelectedItem = selected; refreshingGameModels = false;
        LoadGameModelButton.Content = selected.NeedsDownload ? "Download" : "Load";
    }
    private void OnGameModelSelected(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (refreshingGameModels || GameModelBox.SelectedItem is not GameModelChoice choice) return;
        ModelPathBox.Text = choice.Path; LoadGameModelButton.Content = choice.NeedsDownload ? "Download" : "Load";
        ModelLabel.Text = choice.NeedsDownload ? "Install this model from Downloads, then return here and click Load." :
            "Click Load to use this model for all games. Larger models take more memory and CPU time.";
    }
    private async void OnLoadModel(object sender, RoutedEventArgs e)
    {
        if (GameModelBox.SelectedItem is GameModelChoice { NeedsDownload: true }) { OnDownloads(sender,e); return; }
        await LoadModelAsync();
    }
    private async Task LoadModelAsync()
    {
        if (loading || controller?.IsBusy == true) return;
        loading = true;
        RefreshAvailability();
        SetStatus("Preparing model", "On first launch, the included Base model is prepared in your ElkaSoft app-data folder.");
        try
        {
            loadTask = PrepareAndLoadModelAsync(ModelPathBox.Text.Trim(), lifetime.Token);
            await loadTask;
            settings = settings with { ModelPath = speech.LoadedPath! };
            if (settingsWritable) await store.SaveAsync(settings);
            ModelLabel.Text = Path.GetFileName(speech.LoadedPath) + " · loaded for all games · CPU · offline";
            RefreshGameModels();
            SetStatus("Ready", "Select a profile, focus its app, hold the talk button and speak.");
        }
        catch (OperationCanceledException) { SetStatus("Cancelled", "Model loading cancelled."); }
        catch (Exception ex) { SetStatus("Model error", ex.Message, true); ModelLabel.Text = speech.IsReady ? "Previous model is still loaded" : "No model loaded"; }
        finally { loading = false; RefreshAvailability(); }
    }
    private async Task PrepareAndLoadModelAsync(string requestedPath, CancellationToken token)
    {
        string path = await BundledModelSetup.ResolveAsync(requestedPath, DataPaths.Models, AppContext.BaseDirectory, token);
        token.ThrowIfCancellationRequested();
        ModelPathBox.Text = path;
        SetStatus("Loading model", "The model will stay in memory for subsequent recordings.");
        await speech.LoadAsync(path, token);
    }

    private async void OnApplySettings(object sender, RoutedEventArgs e)
    {
        try
        {
            if (hook?.IsHeld == true) throw new InvalidOperationException("Release the hotkey before changing settings.");
            if (!int.TryParse(MaxSecondsBox.Text, out var seconds) || seconds is < 1 or > 120) throw new InvalidOperationException("Recording limit must be 1–120 seconds.");
            if (!int.TryParse(MaxLengthBox.Text, out var length) || length is < 1 or > 4000) throw new InvalidOperationException("Message limit must be 1–4000 characters.");
            settings = settings with
            {
                MicrophoneId = (MicrophoneBox.SelectedItem as MicrophoneInfo)?.Id,
                Language = (LanguageBox.SelectedItem as LanguageOption)?.Code ?? "auto",
                MaximumRecordingSeconds = seconds, MaximumMessageLength = length,
                CloseToTray = TrayCheck.IsChecked == true
            };
            if (settingsWritable) await store.SaveAsync(settings);
            UpdateInstructions();
            SetStatus(speech.IsReady ? "Ready" : "Model needed", settingsWritable ? "Settings applied and saved." : "Settings applied for this session. The unreadable settings file was preserved.");
        }
        catch (Exception ex) { SetStatus("Settings error", ex.Message, true); }
    }

    private void UpdateInstructions()
    {
        var profile = settings.Profiles?.FirstOrDefault(p => p.Id == settings.SelectedProfileId);
        if (profile == null) return;
        TriggerLabel.Text = profile.Trigger.ToString();
        InstructionsLabel.Text = $"{profile.Name} · hold {profile.Trigger}, speak, then release.";
        if (profile.ChatCommand.Enabled)
            InstructionsLabel.Text += profile.ChatCommand.UseSeparateButton
                ? $" Press {profile.ChatCommand.Button} for the chat command."
                : $" Double-tap {profile.Trigger} within {profile.ChatCommand.DoubleTapWindowMs} ms for the chat command.";
        if (profile.BlockKeyboardWhileSending && profile.Delivery == TextDelivery.Unicode)
            InstructionsLabel.Text += " Keyboard blocked while sending; Esc cancels.";
        ActiveProfileLabel.Text = "Target checked only when text is ready to send.";
        ConfigureHook();
    }

    private void RefreshProfiles()
    {
        refreshingProfiles = true;
        ProfileBox.ItemsSource = settings.Profiles;
        ProfileBox.SelectedItem = settings.Profiles?.FirstOrDefault(p => p.Id == settings.SelectedProfileId);
        AutomaticProfilesCheck.IsChecked = settings.AutomaticProfiles;
        refreshingProfiles = false;
        UpdateInstructions();
    }
    private async void OnProfileSelected(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (refreshingProfiles || ProfileBox.SelectedItem is not GameProfile profile) return;
        settings = settings with { SelectedProfileId = profile.Id }; UpdateInstructions();
        await SaveProfileSettings();
    }
    private async void OnAutomaticProfiles(object sender, RoutedEventArgs e)
    {
        if (refreshingProfiles) return;
        settings = settings with { AutomaticProfiles = AutomaticProfilesCheck.IsChecked == true };
        ConfigureHook(); await SaveProfileSettings();
    }
    private async Task<bool> SaveProfileSettings()
    {
        try { if (!settingsWritable) return false; await store.SaveAsync(settings); return true; }
        catch (Exception ex) { SetStatus("Settings error", ex.Message, true); return false; }
    }
    private async void OnEditProfile(object sender, RoutedEventArgs e) => await EditProfile(ProfileBox.SelectedItem as GameProfile);
    private async void OnNewProfile(object sender, RoutedEventArgs e) => await EditProfile(new GameProfile());
    private async Task EditProfile(GameProfile? profile)
    {
        if (profile == null || hook == null || hook.IsHeld || controller?.IsBusy == true || commandCancellation != null) return;
        editingProfile = true; ConfigureHook();
        try
        {
            var dialog = new ProfileEditorWindow(profile, hook) { Owner = this };
            if (dialog.ShowDialog() == true && dialog.Result is { } result)
            {
                var profiles = settings.Profiles!.ToList();
                int index = profiles.FindIndex(p => p.Id == result.Id);
                if (index < 0) profiles.Add(result); else profiles[index] = result;
                settings = (settings with { Profiles = profiles.ToArray(), SelectedProfileId = result.Id }).Validate();
                RefreshProfiles(); bool saved = await SaveProfileSettings();
                SetStatus(saved ? "Profile saved" : "Profile applied for this session", $"{result.Name} · hold {result.Trigger} to talk.");
            }
        }
        finally { editingProfile = false; ConfigureHook(); }
    }
    private async void OnDeleteProfile(object sender, RoutedEventArgs e)
    {
        if (settings.Profiles?.Length <= 1 || ProfileBox.SelectedItem is not GameProfile profile) return;
        if (System.Windows.MessageBox.Show(this, $"Delete profile '{profile.Name}'?", "Delete profile", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        settings = (settings with { Profiles = settings.Profiles!.Where(p => p.Id != profile.Id).ToArray() }).Validate();
        RefreshProfiles(); await SaveProfileSettings();
    }
    private void OnEnabledChanged(object sender, RoutedEventArgs e)
    {
        if (hook == null) return;
        bool enabled = EnabledCheck.IsChecked == true;
        if (!enabled) CancelActiveWork();
        if (trayEnabled != null) trayEnabled.Checked = enabled;
        SetStatus(enabled ? "Ready" : "Disabled", enabled ? "Hold-to-talk is enabled." : "Hold-to-talk is disabled.");
        RefreshAvailability();
    }
    private void OnPreviewDown(object sender, MouseButtonEventArgs e)
    {
        if (controller?.Begin(settings, previewOnly: true) == true)
        {
            previewHeld = true;
            PreviewButton.CaptureMouse();
            e.Handled = true;
        }
    }
    private void OnPreviewUp(object sender, MouseButtonEventArgs e)
    {
        if (!previewHeld) return;
        previewHeld = false;
        controller?.Release();
        PreviewButton.ReleaseMouseCapture();
        e.Handled = true;
        RefreshAvailability();
    }
    private void OnPreviewLostCapture(object sender, MouseEventArgs e)
    {
        if (previewHeld) { previewHeld = false; controller?.Cancel(); }
    }
    private async void CancelActiveWork()
    {
        hook?.CancelPendingGesture();
        controller?.Cancel(); inputTestCancellation?.Cancel(); commandCancellation?.Cancel();
        if (transcriptMode && transcriptionView != null) await transcriptionView.StopAndWaitAsync();
    }
    private async Task RunChatCommandAsync(ChatCommandRequest request)
    {
        if (transcriptMode || loading || exiting || editingProfile || IsActive || EnabledCheck.IsChecked != true ||
            controller?.IsBusy == true || commandCancellation != null || profileTarget == null) return;
        commandCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var token = commandCancellation.Token;
        RefreshAvailability();
        SetStatus("Chat command", "Checking the destination and running its configured buttons…");
        try
        {
            string profile = await Task.Run(() => profileTarget.RunCommandAsync(request, token), token);
            SetStatus("Ready", $"{profile} · chat command completed.");
        }
        catch (OperationCanceledException) { SetStatus("Cancelled", "Chat command stopped. Check the chat before retrying."); }
        catch (Exception ex) { SetStatus("Attention", ex.Message, true); }
        finally { commandCancellation.Dispose(); commandCancellation = null; RefreshAvailability(); }
    }
    private void OnCancel(object sender, RoutedEventArgs e) => CancelActiveWork();
    private void OnTestChat(object sender, RoutedEventArgs e)
    {
        if (loading || controller?.IsBusy == true || commandCancellation != null || hook?.IsHeld == true) return;
        editingProfile = true; ConfigureHook();
        try { new ChatTestWindow((ProfileBox.SelectedItem as GameProfile) ?? GameProfile.WarDogs()) { Owner = this }.ShowDialog(); }
        finally { editingProfile = false; ConfigureHook(); }
    }

    private async void OnTestInput(object sender, RoutedEventArgs e)
    {
        if (loading || controller?.IsBusy == true || commandCancellation != null || target == null) return;
        loading = true;
        inputTestCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        RefreshAvailability();
        try
        {
            loadTask = RunInputTestAsync(inputTestCancellation.Token);
            await loadTask;
        }
        catch (OperationCanceledException) { SetStatus("Cancelled", "Input test cancelled."); }
        catch (Exception ex) { SetStatus("Input test failed", ex.Message, true); }
        finally { inputTestCancellation.Dispose(); inputTestCancellation = null; loading = false; RefreshAvailability(); }
    }

    private async Task RunInputTestAsync(CancellationToken token)
    {
        const string sample = "Now we're talking about other stuff so that is so. Þú ert á Íslandi — æ ö ü. 😀";
        TranscriptBox.Text = sample;
        for (int seconds = 10; seconds > 0; seconds--)
        {
            SetStatus("Input test", $"Click inside a blank Notepad document. Inserting test text in {seconds} seconds… Esc cancels.");
            await Task.Delay(1000, token);
        }
        var destination = target!.Capture() ?? throw new InvalidOperationException("No focused Notepad document. Click inside its editor and retry.");
        await Task.Run(() => target.InsertAsync(destination, sample, token), token);
        SetStatus("Ready", "Test text inserted into Notepad. Compare it with the sample below.");
    }
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (exiting) return;
        e.Cancel = true;
        if (transcriptionView != null) await transcriptionView.StopAndWaitAsync();
        if (downloadsView != null) await downloadsView.StopAndWaitAsync();
        if (settings.CloseToTray && tray != null) Hide();
        else await ExitAsync();
    }

    private async Task ExitAsync()
    {
        if (exiting) return;
        exiting = true;
        hook?.Dispose();
        controller?.Cancel();
        lifetime.Cancel();
        RefreshAvailability();
        if (controller != null) await controller.Completion;
        await commandTask;
        try { await loadTask; } catch (Exception) { /* Already displayed by LoadModelAsync. */ }
        if (transcriptionView != null) await transcriptionView.StopAndWaitAsync();
        if (downloadsView != null) await downloadsView.StopAndWaitAsync();
        await speech.DisposeAsync();
        target?.Dispose();
        profileTarget?.Dispose();
        meterTimer.Stop();
        tray?.Dispose();
        lifetime.Dispose();
        System.Windows.Application.Current.Shutdown();
    }
}
