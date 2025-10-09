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
using Avalonia.Input;
using MFAAvalonia.Helper;

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
        void ApplyLayout()
        {
            var grid = connectionGrid ?? this.FindControl<Grid>("connectionGrid");
            var first = FirstButton ?? this.FindControl<RadioButton>("FirstButton");
            var second = SecondButton ?? this.FindControl<RadioButton>("SecondButton");
            var panel = ControllerPanel ?? this.FindControl<DockPanel>("ControllerPanel");

            if (grid is null || first is null || second is null || panel is null)
                return;

            var actualWidth = grid.Bounds.Width;
            double totalMinWidth = first.MinWidth + second.MinWidth + panel.MinWidth;

            if (actualWidth >= totalMinWidth)
            {
                // 左右三列
                grid.RowDefinitions.Clear();
                grid.ColumnDefinitions.Clear();
                grid.ColumnDefinitions.AddRange(new[]
                {
                    new ColumnDefinition { Width = new GridLength(first.MinWidth, GridUnitType.Pixel) },
                    new ColumnDefinition { Width = new GridLength(second.MinWidth, GridUnitType.Pixel) },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
                });

                Grid.SetColumn(first, 0);
                Grid.SetRow(first, 0);
                Grid.SetColumn(second, 1);
                Grid.SetRow(second, 0);
                Grid.SetColumn(panel, 2);
                Grid.SetRow(panel, 0);
            }
            else if (actualWidth >= (first.MinWidth + second.MinWidth))
            {
                // 按钮并排，设备选择换行
                grid.RowDefinitions.Clear();
                grid.ColumnDefinitions.Clear();
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                Grid.SetRow(first, 0);
                Grid.SetColumn(first, 0);
                Grid.SetRow(second, 0);
                Grid.SetColumn(second, 1);
                Grid.SetRow(panel, 1);
                Grid.SetColumn(panel, 0);
                Grid.SetColumnSpan(panel, 2);

                first.InvalidateMeasure();
                second.InvalidateMeasure();
            }
            else
            {
                // 三行堆叠
                grid.ColumnDefinitions.Clear();
                grid.RowDefinitions.Clear();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                Grid.SetRow(first, 0);
                Grid.SetColumn(first, 0);
                Grid.SetRow(second, 1);
                Grid.SetColumn(second, 0);
                Grid.SetRow(panel, 2);
                Grid.SetColumn(panel, 0);
            }

            Dispatcher.UIThread.Post(() =>
            {
                grid.InvalidateMeasure();
                grid.InvalidateArrange();
            }, DispatcherPriority.Background);
        }

        // 监听尺寸变化
        {
            var grid = connectionGrid ?? this.FindControl<Grid>("connectionGrid");
            if (grid != null)
                grid.SizeChanged += (_, __) => ApplyLayout();
        }

        // 初始应用一次，避免初次显示重叠
        Dispatcher.UIThread.Post(ApplyLayout, DispatcherPriority.Background);
    }

    // 与主页相同语义：在分隔条拖拽结束时写回列宽并持久化
    private void GridSplitter_DragCompleted(object? sender, VectorEventArgs e)
    {
        if (MainGrid == null)
        {
            LoggerHelper.Error("GridSplitter_DragCompleted: MainGrid is null");
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                var actualCol1Width = MainGrid.ColumnDefinitions[0].ActualWidth;
                var col1Width = MainGrid.ColumnDefinitions[0].Width;
                var col2Width = MainGrid.ColumnDefinitions[2].Width;
                var col3Width = MainGrid.ColumnDefinitions[4].Width;

                var vm = MFAAvalonia.Helper.Instances.TaskQueueViewModel;
                if (vm != null)
                {
                    vm.SuppressPropertyChangedCallbacks = true;

                    if (col1Width is { IsStar: true, Value: 0 } && actualCol1Width > 0)
                        vm.Column1Width = new GridLength(actualCol1Width, GridUnitType.Pixel);
                    else
                        vm.Column1Width = col1Width;

                    vm.Column2Width = col2Width;
                    vm.Column3Width = col3Width;

                    vm.SuppressPropertyChangedCallbacks = false;
                    vm.SaveColumnWidths();
                }
                else
                {
                    LoggerHelper.Error("GridSplitter_DragCompleted: ViewModel is null");
                }
            }
            catch (Exception ex)
            {
                LoggerHelper.Error($"更新列宽失败: {ex.Message}");
            }
        });
    }
}
