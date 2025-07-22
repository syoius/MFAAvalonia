using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using AvaloniaEdit.Highlighting;
using MFAAvalonia.Helper;
using MFAAvalonia.ViewModels.Windows;

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
    
    private void DisplayAnnouncement(object? sender, RoutedEventArgs e)
    {
       AnnouncementViewModel.CheckAnnouncement(true);
    }
}

