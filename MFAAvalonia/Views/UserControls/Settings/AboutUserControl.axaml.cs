using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using AvaloniaEdit.Highlighting;
using MFAAvalonia.Helper;

namespace MFAAvalonia.Views.UserControls.Settings;

public partial class AboutUserControl : UserControl
{
    public AboutUserControl()
    {
        InitializeComponent();

    }
    private void Button_OnClick(object? sender, RoutedEventArgs e)
    {
        FileLogExporter.CompressRecentLogs(Instances.RootView.StorageProvider);
    }
}

