using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using MFAAvalonia.ViewModels.Pages;
using Microsoft.Extensions.DependencyInjection;
using Avalonia.Markup.Xaml;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;

namespace MFAAvalonia.Views.Pages;

public partial class CopilotView : UserControl
{
    public CopilotView()
    {
        // 兜底：在编译的 XAML 未刷新时（--no-build），仍确保 DataContext 正确
        try { DataContext = MFAAvalonia.App.Services.GetRequiredService<CopilotViewModel>(); } catch { /* fallback to XAML */ }
        InitializeComponent();
        InitializeControllerUI();
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

    /// <summary>
    /// 对齐主页“连接”区域的动态布局逻辑，避免控件在不同宽度下重叠。
    /// </summary>
    private void InitializeControllerUI()
    {
        if (connectionGrid is null || FirstButton is null || SecondButton is null || ControllerPanel is null)
            return;

        connectionGrid.SizeChanged += (_, __) =>
        {
            var actualWidth = connectionGrid.Bounds.Width;
            double totalMinWidth = FirstButton.MinWidth + SecondButton.MinWidth + ControllerPanel.MinWidth;

            if (actualWidth >= totalMinWidth)
            {
                // 左右三列
                connectionGrid.RowDefinitions.Clear();
                connectionGrid.ColumnDefinitions.Clear();
                connectionGrid.ColumnDefinitions.AddRange(new[]
                {
                    new ColumnDefinition { Width = new GridLength(FirstButton.MinWidth, GridUnitType.Pixel) },
                    new ColumnDefinition { Width = new GridLength(SecondButton.MinWidth, GridUnitType.Pixel) },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
                });

                Grid.SetColumn(FirstButton, 0);
                Grid.SetRow(FirstButton, 0);
                Grid.SetColumn(SecondButton, 1);
                Grid.SetRow(SecondButton, 0);
                Grid.SetColumn(ControllerPanel, 2);
                Grid.SetRow(ControllerPanel, 0);
            }
            else if (actualWidth >= (FirstButton.MinWidth + SecondButton.MinWidth))
            {
                // 按钮并排，设备选择换行
                connectionGrid.RowDefinitions.Clear();
                connectionGrid.ColumnDefinitions.Clear();
                connectionGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                connectionGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                connectionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                connectionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                Grid.SetRow(FirstButton, 0);
                Grid.SetColumn(FirstButton, 0);
                Grid.SetRow(SecondButton, 0);
                Grid.SetColumn(SecondButton, 1);
                Grid.SetRow(ControllerPanel, 1);
                Grid.SetColumn(ControllerPanel, 0);
                Grid.SetColumnSpan(ControllerPanel, 2);

                FirstButton.InvalidateMeasure();
                SecondButton.InvalidateMeasure();
            }
            else
            {
                // 三行堆叠
                connectionGrid.ColumnDefinitions.Clear();
                connectionGrid.RowDefinitions.Clear();
                connectionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                connectionGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                connectionGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                connectionGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                Grid.SetRow(FirstButton, 0);
                Grid.SetColumn(FirstButton, 0);
                Grid.SetRow(SecondButton, 1);
                Grid.SetColumn(SecondButton, 0);
                Grid.SetRow(ControllerPanel, 2);
                Grid.SetColumn(ControllerPanel, 0);
            }

            // 保证 UI 在主线程刷新
            Dispatcher.UIThread.Post(() =>
            {
                connectionGrid.InvalidateMeasure();
                connectionGrid.InvalidateArrange();
            }, DispatcherPriority.Background);
        };
    }
}
