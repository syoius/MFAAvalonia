using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Diagnostics;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.GestureRecognizers;
using Avalonia.Interactivity;
using Avalonia.Layout;
using System;

namespace MFAAvalonia.Extensions;

public static class ScrollViewerExtensions
{
    // 滚动方向控制
    public static readonly AttachedProperty<PanningMode> PanningModeProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, PanningMode>(
            "PanningMode", typeof(ScrollViewerExtensions), PanningMode.Both);

    // 自动滚动控制
    public static readonly AttachedProperty<bool> AutoScrollProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, bool>(
            "AutoScroll", typeof(ScrollViewerExtensions), false);

    static ScrollViewerExtensions()
    {
        PanningModeProperty.Changed.AddClassHandler<ScrollViewer>(
          OnPanningModeChanged);

        AutoScrollProperty.Changed.AddClassHandler<ScrollViewer>(
            OnAutoScrollChanged);
    }

    
    #region 属性设置器

    public static void SetPanningMode(ScrollViewer element, PanningMode value) =>
        element.SetValue(PanningModeProperty, value);

    public static PanningMode GetPanningMode(ScrollViewer element) =>
        element.GetValue(PanningModeProperty);

    public static void SetAutoScroll(ScrollViewer element, bool value) =>
        element.SetValue(AutoScrollProperty, value);

    public static bool GetAutoScroll(ScrollViewer element) =>
        element.GetValue(AutoScrollProperty);

    #endregion

    #region 逻辑处理

    private static void OnPanningModeChanged(ScrollViewer scrollViewer, AvaloniaPropertyChangedEventArgs args)
    {
        scrollViewer.HorizontalScrollBarVisibility = args.NewValue switch
        {
            PanningMode.VerticalOnly => ScrollBarVisibility.Disabled,
            PanningMode.HorizontalOnly => ScrollBarVisibility.Auto,
            _ => ScrollBarVisibility.Auto
        };

        scrollViewer.VerticalScrollBarVisibility = args.NewValue switch
        {
            PanningMode.HorizontalOnly => ScrollBarVisibility.Disabled,
            PanningMode.VerticalOnly => ScrollBarVisibility.Auto,
            _ => ScrollBarVisibility.Auto
        };

    }

    private static void OnAutoScrollChanged(ScrollViewer scrollViewer, AvaloniaPropertyChangedEventArgs args)
    {
        var isAutoScroll = (bool)args.NewValue;
    
        // 移除旧事件监听

        if (isAutoScroll)
        {
            // 监听布局变化
            scrollViewer.LayoutUpdated += OnLayoutUpdated;
        
            // 监听用户手动滚动
            scrollViewer.PropertyChanged += OnScrollViewerPropertyChanged;
        
            // 初始滚动到底部
            scrollViewer.ScrollToEnd();
        }
        else
        {
            scrollViewer.LayoutUpdated -= OnLayoutUpdated;
            scrollViewer.PropertyChanged -= OnScrollViewerPropertyChanged;
        }

        void OnLayoutUpdated(object sender, EventArgs e)
        {
            if (scrollViewer.Offset.Y >= scrollViewer.ScrollBarMaximum.Y - 10)
            {
                scrollViewer.ScrollToEnd();
            }
        }

        void OnScrollViewerPropertyChanged(object sender, 
            AvaloniaPropertyChangedEventArgs e)
        {
            // 用户手动滚动时停止自动滚动
            if (e.Property == ScrollViewer.OffsetProperty)
            {
                scrollViewer.SetValue(AutoScrollProperty, false);
            }
        }
    }
    #endregion
}

public enum PanningMode
{
    VerticalOnly,
    HorizontalOnly,
    Both
}
