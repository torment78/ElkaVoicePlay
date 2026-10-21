using System.Windows;

namespace ElkaVoicePlay.App;

public partial class App : System.Windows.Application
{
    private Mutex? singleInstance;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        singleInstance = new Mutex(true, "Local\\ElkaSoft.ElkaVoicePlay", out bool created);
        if (!created)
        {
            System.Windows.MessageBox.Show("Elka VoicePlay is already running. Open it from the system tray.", "Elka VoicePlay");
            Shutdown();
            return;
        }
        try
        {
            ElkaVoicePlay.Core.LegacySettingsMigration.Import(
                System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ElkaSoftware", "ElkaVoiceType"),
                ElkaVoicePlay.Shared.DataPaths.Root);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show("Your previous settings could not be imported. They are unchanged. " + ex.Message, "Elka VoicePlay");
            Shutdown(1); return;
        }
        var window = new MainWindow(e.Args.Contains("--smoke-test"));
        MainWindow = window;
        window.Show();
    }
    protected override void OnExit(ExitEventArgs e) { singleInstance?.Dispose(); base.OnExit(e); }
}
