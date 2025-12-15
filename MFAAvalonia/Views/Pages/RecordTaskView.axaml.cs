using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using MFAAvalonia.ViewModels.Pages;

namespace MFAAvalonia.Views.Pages;

public partial class RecordTaskView : UserControl
{
    public RecordTaskView()
    {
        try { DataContext = MFAAvalonia.App.Services.GetRequiredService<RecordTaskViewModel>(); } catch { /* fallback to XAML */ }
        InitializeComponent();
        this.AttachedToVisualTree += (_, _) => (DataContext as RecordTaskViewModel)?.Initialize();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

