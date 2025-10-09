using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using MFAAvalonia.ViewModels.Pages;
using Microsoft.Extensions.DependencyInjection;
using Avalonia.Markup.Xaml;

namespace MFAAvalonia.Views.Pages;

public partial class CopilotView : UserControl
{
    public CopilotView()
    {
        // 兜底：在编译的 XAML 未刷新时（--no-build），仍确保 DataContext 正确
        try { DataContext = MFAAvalonia.App.Services.GetRequiredService<CopilotViewModel>(); } catch { /* fallback to XAML */ }
        InitializeComponent();
        this.AttachedToVisualTree += (_, __) => (DataContext as CopilotViewModel)?.Initialize();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private async void OnImportLocalJson(object? sender, RoutedEventArgs e)
    {
        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top == null) return;
            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择作业 JSON",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("JSON") { Patterns = new[] { "*.json" } }
                }
            });
            if (files.Count == 0) return;
            await (DataContext as CopilotViewModel)!.ImportLocalJsonAsync(files[0].TryGetLocalPath());
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }

    private async void OnImportMysteryCode(object? sender, RoutedEventArgs e)
    {
        await (DataContext as CopilotViewModel)!.ImportMysteryCodeAsync(string.Empty);
    }

    private async void OnRefresh(object? sender, RoutedEventArgs e)
    {
        await (DataContext as CopilotViewModel)!.RefreshAsync();
    }

    private async void OnLoadSelected(object? sender, RoutedEventArgs e)
    {
        await (DataContext as CopilotViewModel)!.LoadSelectedAsync();
    }

    private async void OnOpenCacheDir(object? sender, RoutedEventArgs e)
    {
        await (DataContext as CopilotViewModel)!.OpenCacheDirAsync();
    }

    private async void OnPreview(object? sender, RoutedEventArgs e)
    {
        await (DataContext as CopilotViewModel)!.PreviewSelectedAsync();
    }
}
