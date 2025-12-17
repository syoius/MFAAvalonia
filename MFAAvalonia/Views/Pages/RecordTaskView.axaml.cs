using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using MFAAvalonia.Helper;
using MFAAvalonia.ViewModels.Pages;
using System;

namespace MFAAvalonia.Views.Pages;

public partial class RecordTaskView : UserControl
{
    public RecordTaskView()
    {
        try { DataContext = MFAAvalonia.App.Services.GetRequiredService<RecordTaskViewModel>(); } catch { /* fallback to XAML */ }
        InitializeComponent();
        InitializeDeviceSelectorLayout();
        this.AttachedToVisualTree += (_, _) =>
        {
            (DataContext as RecordTaskViewModel)?.Initialize();
            Dispatcher.UIThread.Post(() => UpdateConnectionLayout(true), DispatcherPriority.Loaded);
        };
    }

    private int _currentLayoutMode = -1;
    private int _currentSelectorMode = -1;

    private void InitializeDeviceSelectorLayout()
    {
        var grid = ConnectionGrid ?? this.FindControl<Grid>("ConnectionGrid");
        var selectorPanel = DeviceSelectorPanel ?? this.FindControl<Grid>("DeviceSelectorPanel");
        var adbButton = AdbRadioButton ?? this.FindControl<RadioButton>("AdbRadioButton");
        var win32Button = Win32RadioButton ?? this.FindControl<RadioButton>("Win32RadioButton");

        if (grid != null)
            grid.SizeChanged += (_, _) => UpdateConnectionLayout();
        if (selectorPanel != null)
            selectorPanel.SizeChanged += (_, _) => UpdateDeviceSelectorLayout();
        if (adbButton != null)
            adbButton.PropertyChanged += (_, e) =>
            {
                if (e.Property.Name == "IsVisible") UpdateConnectionLayout();
            };
        if (win32Button != null)
            win32Button.PropertyChanged += (_, e) =>
            {
                if (e.Property.Name == "IsVisible") UpdateConnectionLayout();
            };

        UpdateConnectionLayout(true);
    }

    private void UpdateConnectionLayout(bool forceUpdate = false)
    {
        var grid = ConnectionGrid ?? this.FindControl<Grid>("ConnectionGrid");
        var selectorPanel = DeviceSelectorPanel ?? this.FindControl<Grid>("DeviceSelectorPanel");
        var adbButton = AdbRadioButton ?? this.FindControl<RadioButton>("AdbRadioButton");
        var win32Button = Win32RadioButton ?? this.FindControl<RadioButton>("Win32RadioButton");

        if (grid == null || selectorPanel == null || adbButton == null || win32Button == null)
            return;

        var totalWidth = grid.Bounds.Width;
        if (totalWidth <= 0) return;

        var adbWidth = adbButton.IsVisible ? adbButton.MinWidth + 8 : 0;
        var win32Width = win32Button.IsVisible ? win32Button.MinWidth + 8 : 0;
        var radioButtonsWidth = adbWidth + win32Width;
        var selectorMinWidth = selectorPanel.MinWidth;

        int newMode;
        if (totalWidth >= radioButtonsWidth + selectorMinWidth + 20)
            newMode = 0; // 一行
        else if (totalWidth >= selectorMinWidth + 20)
            newMode = 1; // 两行
        else
            newMode = 2; // 三行

        if (!forceUpdate && newMode == _currentLayoutMode) return;
        _currentLayoutMode = newMode;

        grid.ColumnDefinitions.Clear();
        grid.RowDefinitions.Clear();
        Grid.SetColumnSpan(selectorPanel, 1);

        switch (newMode)
        {
            case 0:
                if (adbButton.IsVisible)
                    grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                if (win32Button.IsVisible)
                    grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));

                var col = 0;
                if (adbButton.IsVisible)
                {
                    Grid.SetColumn(adbButton, col);
                    Grid.SetRow(adbButton, 0);
                    col++;
                }
                if (win32Button.IsVisible)
                {
                    Grid.SetColumn(win32Button, col);
                    Grid.SetRow(win32Button, 0);
                    col++;
                }
                Grid.SetColumn(selectorPanel, col);
                Grid.SetRow(selectorPanel, 0);
                break;

            case 1:
            case 2:
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var visibleCount = (adbButton.IsVisible ? 1 : 0) + (win32Button.IsVisible ? 1 : 0);
                for (var i = 0; i < Math.Max(visibleCount, 1); i++)
                    grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

                var c = 0;
                if (adbButton.IsVisible)
                {
                    Grid.SetColumn(adbButton, c++);
                    Grid.SetRow(adbButton, 0);
                }
                if (win32Button.IsVisible)
                {
                    Grid.SetColumn(win32Button, c);
                    Grid.SetRow(win32Button, 0);
                }
                Grid.SetColumn(selectorPanel, 0);
                Grid.SetColumnSpan(selectorPanel, Math.Max(visibleCount, 1));
                Grid.SetRow(selectorPanel, 1);
                break;
        }

        _currentSelectorMode = -1;
        UpdateDeviceSelectorLayout();
    }

    private void UpdateDeviceSelectorLayout()
    {
        var selectorPanel = DeviceSelectorPanel ?? this.FindControl<Grid>("DeviceSelectorPanel");
        var selectorLabel = DeviceSelectorLabel ?? this.FindControl<TextBlock>("DeviceSelectorLabel");
        var deviceCombo = DeviceComboBox ?? this.FindControl<ComboBox>("DeviceComboBox");

        if (selectorPanel == null || selectorLabel == null || deviceCombo == null)
            return;

        int newMode = _currentLayoutMode == 2 ? 1 : 0;

        if (newMode == _currentSelectorMode) return;
        _currentSelectorMode = newMode;

        selectorPanel.ColumnDefinitions.Clear();
        selectorPanel.RowDefinitions.Clear();

        switch (newMode)
        {
            case 0:
                selectorPanel.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                selectorPanel.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));

                Grid.SetColumn(selectorLabel, 0);
                Grid.SetRow(selectorLabel, 0);
                Grid.SetColumn(deviceCombo, 1);
                Grid.SetRow(deviceCombo, 0);

                selectorLabel.Margin = new Thickness(0, 2, 8, 0);
                deviceCombo.HorizontalAlignment = HorizontalAlignment.Stretch;
                break;

            case 1:
                selectorPanel.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                selectorPanel.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                selectorPanel.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));

                Grid.SetColumn(selectorLabel, 0);
                Grid.SetRow(selectorLabel, 0);
                Grid.SetColumn(deviceCombo, 0);
                Grid.SetRow(deviceCombo, 1);

                selectorLabel.Margin = new Thickness(5, 0, 0, 5);
                deviceCombo.HorizontalAlignment = HorizontalAlignment.Stretch;
                break;
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
