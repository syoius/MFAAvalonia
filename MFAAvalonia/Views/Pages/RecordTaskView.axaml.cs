using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia;
using Avalonia.Threading;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using MFAAvalonia.Configuration;
using MFAAvalonia.Helper;
using MFAAvalonia.ViewModels.Pages;
using System;
using System.ComponentModel;

namespace MFAAvalonia.Views.Pages;

public partial class RecordTaskView : UserControl
{
    private const double TopToolbarCompactWidthThreshold = 980;
    private bool _isTopToolbarCompact;

    public RecordTaskView()
    {
        try { DataContext = MFAAvalonia.App.Services.GetRequiredService<RecordTaskViewModel>(); } catch { /* fallback to XAML */ }
        InitializeComponent();
        Loaded += OnTopToolbarLoaded;
        Unloaded += OnTopToolbarUnloaded;
        ApplyStoredColumnWidths();
        this.AttachedToVisualTree += (_, _) =>
        {
            (DataContext as RecordTaskViewModel)?.Initialize();
        };
    }

    private void ApplyStoredColumnWidths()
    {
        var grid = MainGrid ?? this.FindControl<Grid>("MainGrid");
        if (grid == null || grid.ColumnDefinitions.Count < 5)
        {
            return;
        }

        grid.ColumnDefinitions[0].Width = GridLengthStorage.Load(ConfigurationKeys.TaskQueueColumn1Width, new GridLength(1, GridUnitType.Star));
        grid.ColumnDefinitions[2].Width = GridLengthStorage.Load(ConfigurationKeys.TaskQueueColumn2Width, new GridLength(1, GridUnitType.Star));
        grid.ColumnDefinitions[4].Width = GridLengthStorage.Load(ConfigurationKeys.TaskQueueColumn3Width, new GridLength(1, GridUnitType.Star));
    }

    private void OnTopToolbarLoaded(object? sender, RoutedEventArgs e)
    {
        if (TopToolbar == null)
        {
            return;
        }

        TopToolbar.SizeChanged += OnTopToolbarSizeChanged;
        Dispatcher.UIThread.Post(() => UpdateTopToolbarLayout(true), DispatcherPriority.Render);

        if (Instances.TaskQueueViewModel != null)
        {
            Instances.TaskQueueViewModel.PropertyChanged -= OnTaskQueueViewModelPropertyChanged;
            Instances.TaskQueueViewModel.PropertyChanged += OnTaskQueueViewModelPropertyChanged;
        }
    }

    private void OnTopToolbarUnloaded(object? sender, RoutedEventArgs e)
    {
        if (TopToolbar == null)
        {
            return;
        }

        TopToolbar.SizeChanged -= OnTopToolbarSizeChanged;

        if (Instances.TaskQueueViewModel != null)
        {
            Instances.TaskQueueViewModel.PropertyChanged -= OnTaskQueueViewModelPropertyChanged;
        }
    }

    private void OnTopToolbarSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        UpdateTopToolbarLayout();
    }

    private void OnTaskQueueViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TaskQueueViewModel.CurrentController))
        {
            UpdateDeviceColumns();
        }
    }

    private void UpdateTopToolbarLayout(bool force = false)
    {
        if (TopToolbarWide == null || TopToolbarCompact == null)
        {
            return;
        }

        var width = TopToolbar.Bounds.Width;
        if (width <= 0)
        {
            return;
        }

        var shouldCompact = width < TopToolbarCompactWidthThreshold;
        if (!force && shouldCompact == _isTopToolbarCompact)
        {
            return;
        }

        _isTopToolbarCompact = shouldCompact;
        TopToolbarWide.IsVisible = !shouldCompact;
        TopToolbarWide.IsHitTestVisible = !shouldCompact;
        TopToolbarCompact.IsVisible = shouldCompact;
        TopToolbarCompact.IsHitTestVisible = shouldCompact;

        UpdateDeviceColumns();
    }

    private void UpdateDeviceColumns()
    {
        var deviceVisible = DeviceSelectorPanel?.IsVisible == true || DeviceSelectorPanelCompact?.IsVisible == true;

        if (TopToolbarWide?.ColumnDefinitions.Count >= 7)
        {
            TopToolbarWide.ColumnDefinitions[5].Width = deviceVisible ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            TopToolbarWide.ColumnDefinitions[4].Width = deviceVisible ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
        }

        if (TopToolbarCompactRow2?.ColumnDefinitions.Count >= 3)
        {
            TopToolbarCompactRow2.ColumnDefinitions[1].Width = deviceVisible ? GridLength.Auto : new GridLength(0);
            TopToolbarCompactRow2.ColumnDefinitions[2].Width = deviceVisible ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            TopToolbarCompactRow2.ColumnDefinitions[0].Width = deviceVisible ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
        }

        if (Spliter2Compact != null)
        {
            Spliter2Compact.IsVisible = deviceVisible;
        }
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

                if (col1Width is { IsStar: true, Value: 0 } && actualCol1Width > 0)
                {
                    col1Width = new GridLength(actualCol1Width, GridUnitType.Pixel);
                    MainGrid.ColumnDefinitions[0].Width = col1Width;
                }

                GridLengthStorage.Save(ConfigurationKeys.TaskQueueColumn1Width, col1Width);
                GridLengthStorage.Save(ConfigurationKeys.TaskQueueColumn2Width, col2Width);
                GridLengthStorage.Save(ConfigurationKeys.TaskQueueColumn3Width, col3Width);
            }
            catch (Exception ex)
            {
                LoggerHelper.Error($"更新列宽失败: {ex.Message}");
            }
        });
    }

    // 删除选中的录制文件
    private void OnDeleteSelectedRecording(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is RecordTaskViewModel vm)
        {
            vm.DeleteSelectedRecordingCommand.Execute(null);
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
