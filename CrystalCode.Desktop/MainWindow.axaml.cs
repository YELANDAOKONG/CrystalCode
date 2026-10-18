using Avalonia.Controls;
using Avalonia.Layout;

namespace CrystalCode.Desktop;

public sealed class MainWindow : Window
{
    public MainWindow()
    {
        Title = "CrystalCode";
        Width = 1_200;
        Height = 800;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Content = new TextBlock
        {
            Text = "CrystalCode",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 24
        };
    }
}
