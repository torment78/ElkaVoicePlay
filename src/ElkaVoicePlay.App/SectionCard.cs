using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ElkaVoicePlay.App;

public static class SectionCard
{
    public static Expander Create(string title, string summary, UIElement content, bool expanded = false)
    {
        var heading = new StackPanel();
        heading.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(new TextBlock { Text = summary, FontSize = 12, Opacity = 0.65, Margin = new Thickness(0, 3, 0, 0) });
        var card = new Expander { Header = heading, Content = content, IsExpanded = expanded, Margin = new Thickness(0, 0, 0, 10) };
        return card;
    }
}
