using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ElkaVoicePlay.App;
using ElkaVoicePlay.Shared;
using ElkaVoicePlay.Speech;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var app = new App();
            app.InitializeComponent();
            bool profile = args.Contains("--profile");
            var sampleProfile = GameProfile.WarDogs();
            if (args.Contains("--command")) sampleProfile = sampleProfile with
            { ChatCommand = new() { Enabled = true, UseSeparateButton = args.Contains("--separate") } };
            bool transcript = args.Contains("--transcript");
            bool downloads = args.Contains("--downloads");
            Window window = downloads ? new Window { Content = new DownloadsView() } : transcript ? new Window { Content = new TranscriptionView("en") } : profile ? new ProfileEditorWindow(sampleProfile, null) : new MainWindow(true);
            int width = transcript || downloads ? 900 : profile ? 744 : 900, height = profile ? 780 : 900;
            // Do not Show(): this test never installs hooks, opens a microphone or accesses another app.
            var content = (FrameworkElement)window.Content;
            window.Content = null;
            var root = new Border { Background = new SolidColorBrush(Color.FromRgb(16, 23, 32)), Child = content };
            TextElement.SetForeground(root, new SolidColorBrush(Color.FromRgb(229, 235, 243)));
            TextElement.SetFontFamily(root, new FontFamily("Segoe UI"));
            TextElement.SetFontSize(root, 14);
            root.Measure(new Size(width, height));
            root.Arrange(new Rect(0, 0, width, height));
            root.UpdateLayout();
            if (args.Contains("--expand"))
            {
                static IEnumerable<DependencyObject> Descendants(DependencyObject node)
                {
                    for (int i=0; i<VisualTreeHelper.GetChildrenCount(node); i++)
                    { var child=VisualTreeHelper.GetChild(node,i); yield return child; foreach(var d in Descendants(child)) yield return d; }
                }
                var expander = Descendants(root).OfType<Expander>().FirstOrDefault(e => e.Header is StackPanel h && h.Children.OfType<TextBlock>().Any(t => t.Text == (profile ? "Chat sequence" : "Models & speakers")));
                if (expander != null) expander.IsExpanded = true;
                root.UpdateLayout();
            }
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(root);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            string path = Path.GetFullPath(args.LastOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal)) ?? "ui-layout.png");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var output = File.Create(path);
            encoder.Save(output);
            Console.WriteLine("WPF resource construction and layout render passed: " + path);
            app.Shutdown();
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
