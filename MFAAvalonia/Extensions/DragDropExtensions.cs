﻿using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit.Utils;
using MFAAvalonia.Helper;
using MFAAvalonia.ViewModels.Pages;
using MFAAvalonia.Views.UserControls;
using Microsoft.VisualBasic;
using SukiUI;
using SukiUI.Controls;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace MFAAvalonia.Extensions;

#pragma warning disable CS4014 // 异步方法没有等待 
public class DragDropExtensions
{
    // 定义附加属性：是否启用拖放功能
    public static readonly AttachedProperty<bool> EnableFileDragDropProperty =
        AvaloniaProperty.RegisterAttached<TextBox, bool>(
            "EnableDragDrop",
            typeof(DragDropExtensions),
            defaultValue: false,
            inherits: false);

    // 获取附加属性值
    public static bool GetEnableFileDragDrop(TextBox textBox) =>
        textBox.GetValue(EnableFileDragDropProperty);

    // 设置附加属性值
    public static void SetEnableFileDragDrop(TextBox textBox, bool value) =>
        textBox.SetValue(EnableFileDragDropProperty, value);

    // 当附加属性值变化时触发
    private static void OnEnableDragDropChanged(AvaloniaPropertyChangedEventArgs<bool> args)
    {
        if (args.Sender is TextBox textBox)
        {
            if (args.NewValue.Value)
            {
                textBox.AddHandler(DragDrop.DragOverEvent, File_DragOver);
                textBox.AddHandler(DragDrop.DropEvent, File_Drop);
            }
            else
            {
                textBox.RemoveHandler(DragDrop.DragOverEvent, File_DragOver);
                textBox.RemoveHandler(DragDrop.DropEvent, File_Drop);
            }
        }
    }

    // 拖放事件处理：拖拽经过时
    private static void File_DragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
    }

    // 拖放事件处理：文件拖放时
    private static void File_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.Contains(DataFormats.Files))
        {
            return;
        }

        var storageItems = e.Data.GetFiles()?.ToList();
        if (storageItems?.Count > 0 && sender is TextBox textBox)
        {
            var firstFile = storageItems[0].TryGetLocalPath();
            textBox.Text = firstFile ?? string.Empty;
        }
    }

    public static readonly AttachedProperty<bool> EnableDragDropProperty =
        AvaloniaProperty.RegisterAttached<ListBox, bool>(
            "EnableDragDrop",
            typeof(DragDropExtensions),
            defaultValue: false);

    // 存储当前拖拽项的私有属性
    private static readonly AttachedProperty<Views.UserControls.DragAdorner?> DragAdornerProperty =
        AvaloniaProperty.RegisterAttached<ListBox, Views.UserControls.DragAdorner?>(
            "DragAdorner",
            typeof(DragDropExtensions));

    public static readonly AttachedProperty<bool> EnableAnimationProperty =
        AvaloniaProperty.RegisterAttached<ListBox, bool>(
            "EnableAnimation",
            typeof(DragDropExtensions),
            defaultValue: false);

    public static readonly AttachedProperty<double> AnimationDurationProperty =
        AvaloniaProperty.RegisterAttached<ListBox, double>(
            "AnimationDuration",
            typeof(DragDropExtensions),
            defaultValue: 400.0);

    public static readonly AttachedProperty<int> HoldDurationMillisecondsProperty =
        AvaloniaProperty.RegisterAttached<ListBox, int>(
            "HoldDurationMilliseconds",
            typeof(DragDropExtensions),
            defaultValue: 200);

    private static readonly AttachedProperty<DateTime?> PressedTimeProperty =
        AvaloniaProperty.RegisterAttached<ListBox, DateTime?>(
            "PressedTime",
            typeof(DragDropExtensions));

    static DragDropExtensions()
    {
        EnableFileDragDropProperty.Changed.Subscribe(OnEnableDragDropChanged);
        EnableDragDropProperty.Changed.Subscribe(args =>
        {
            if (args.Sender is ListBox listBox)
            {
                if (args.NewValue.Value)
                {
                    EnableDragDrop(listBox);
                }
                else
                {
                    DisableDragDrop(listBox);
                }
            }
        });
        LimitDragDropProperty.Changed.Subscribe(OnLimitDragDropChanged);
    }
    public static bool GetEnableAnimation(ListBox element) =>
        element.GetValue(EnableAnimationProperty);

    public static void SetEnableAnimation(ListBox element, bool value) =>
        element.SetValue(EnableAnimationProperty, value);

    public static bool GetEnableDragDrop(ListBox element) =>
        element.GetValue(EnableDragDropProperty);

    public static void SetEnableDragDrop(ListBox element, bool value) =>
        element.SetValue(EnableDragDropProperty, value);


    public static double GetAnimationDuration(ListBox element) =>
        element.GetValue(AnimationDurationProperty);

    public static void SetAnimationDuration(ListBox element, double value) =>
        element.SetValue(AnimationDurationProperty, value switch
        {
            >= 150 => value,
            _ => throw new ArgumentOutOfRangeException($"AnimationDuration must be greater than or equal to 150, but was {value}")
        });

    public static int GetHoldDurationMilliseconds(ListBox element) =>
        element.GetValue(HoldDurationMillisecondsProperty);
    
    public static void SetHoldDurationMilliseconds(ListBox element, int value) =>
        element.SetValue(HoldDurationMillisecondsProperty, value switch
        {
            >= 0 => value,
            _ => throw new ArgumentOutOfRangeException($"HoldDurationMilliseconds must be greater than or equal to 0, but was {value}")
        });
    private static DateTime? GetPressedTime(ListBox element) =>
        element.GetValue(PressedTimeProperty);
    
    private static void SetPressedTime(ListBox element, DateTime? value) =>
        element.SetValue(PressedTimeProperty, value);
    
    public static readonly AttachedProperty<Point?> PressedPositionProperty =
        AvaloniaProperty.RegisterAttached<ListBox, Point?>("PressedPosition", typeof(DragDropExtensions), null);

    public static readonly AttachedProperty<int> DragStartThresholdProperty =
        AvaloniaProperty.RegisterAttached<ListBox, int>("DragStartThreshold", typeof(DragDropExtensions), 10); // 默认10像素阈值

    private static Point? GetPressedPosition(ListBox element) =>
        element.GetValue(PressedPositionProperty);

    private static void SetPressedPosition(ListBox element, Point? value) =>
        element.SetValue(PressedPositionProperty, value);

    public static int GetDragStartThreshold(ListBox element) =>
        element.GetValue(DragStartThresholdProperty);

    public static void SetDragStartThreshold(ListBox element, int value) =>
        element.SetValue(DragStartThresholdProperty, value switch
        {
            >= 0 => value,
            _ => throw new ArgumentOutOfRangeException($"DragStartThreshold must be non-negative, but was {value}")
        });

    private static void EnableDragDrop(ListBox listBox)
    {
        if (listBox.Parent is not AdornerLayer adornerLayer)
        {
            adornerLayer = new AdornerLayer();
            if (listBox.Parent is Panel panel)
            {
                panel.Children.Remove(listBox);
                panel.Children.Add(adornerLayer);

            }
            else if (listBox.Parent is ContentControl control)
            {
                control.Content = null;
                control.Content = adornerLayer;
            }
            else if (listBox.Parent is Border border)
            {
                border.Child = null;
                border.Child = adornerLayer;
            }

            AdornerLayer.SetAdornedElement(adornerLayer, listBox);

        }
        adornerLayer.Children.Add(listBox);
        listBox.AddHandler(ListBox.PointerExitedEvent, OnPointerExited);
        listBox.AddHandler(ListBox.PointerReleasedEvent, OnPointerReleased);
        listBox.AddHandler(ListBox.PointerMovedEvent, OnPointerMoved);
        listBox.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        listBox.AddHandler(DragDrop.DropEvent, OnDrop);
        listBox.AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
    }

    private static void DisableDragDrop(ListBox listBox)
    {
        listBox.RemoveHandler(ListBox.PointerExitedEvent, OnPointerExited);
        listBox.RemoveHandler(ListBox.PointerReleasedEvent, OnPointerReleased);
        listBox.RemoveHandler(ListBox.PointerMovedEvent, OnPointerMoved);
        listBox.RemoveHandler(DragDrop.DragOverEvent, OnDragOver);
        listBox.RemoveHandler(DragDrop.DropEvent, OnDrop);
        listBox.RemoveHandler(DragDrop.DragLeaveEvent, OnDragLeave);
    }
    public static readonly AttachedProperty<bool> LimitDragDropProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>(
            "LimitDragDrop",
            typeof(DragDropExtensions),
            defaultValue: false);
    public static void SetLimitDragDrop(Control element, bool value) =>
        element.SetValue(LimitDragDropProperty, value);
    public static bool GetLimitDragDrop(Control element) => element.GetValue(LimitDragDropProperty);
    private static void OnLimitDragDropChanged(AvaloniaPropertyChangedEventArgs<bool> args)
    {
        if (args.Sender is Control textBox)
        {
            if (args.NewValue.Value)
            {
                textBox.AddHandler(InputElement.PointerExitedEvent, OnPointerExited);
                textBox.AddHandler(InputElement.PointerReleasedEvent, OnPointerReleased);
            }
            else
            {
                textBox.RemoveHandler(InputElement.PointerExitedEvent, OnPointerExited);
                textBox.RemoveHandler(InputElement.PointerReleasedEvent, OnPointerReleased);
            }
        }
    }

    private static void OnPointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is ListBox listBox)
        {
            ClearDragState(listBox);
        }
        else if (sender is Control { Parent: ListBox lb })
        {
            ClearDragState(lb);
        }
    }

    private static void OnPointerReleased(object? sender, PointerEventArgs e)
    {
        if (sender is ListBox listBox)
        {
            ClearDragState(listBox);
        }
        else if (sender is Control { Parent: ListBox lb })
        {
            ClearDragState(lb);
        }
    }

    private static void ClearDragState(ListBox listBox)
    {
        SetPressedPosition(listBox, null);
    }

    private static void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is not ListBox listBox || !e.GetCurrentPoint(listBox).Properties.IsLeftButtonPressed)
            return;

        var pressedPosition = GetPressedPosition(listBox);
        var currentPosition = e.GetPosition(listBox);
        if (pressedPosition == null)
        {
            SetPressedPosition(listBox, currentPosition);
            pressedPosition = GetPressedPosition(listBox);
        }

        var deltaX = currentPosition.X - pressedPosition.Value.X;
        var deltaY = currentPosition.Y - pressedPosition.Value.Y;
        var distanceSquared = deltaX * deltaX + deltaY * deltaY;
        var threshold = GetDragStartThreshold(listBox);
        if (distanceSquared < threshold * threshold)
        {
            return;
        }
        // 清除按下位置
        SetPressedPosition(listBox, currentPosition);
        // 获取鼠标点击位置的项目索引
        var position = e.GetPosition(listBox);
        var sourceItem = GetSourceIndex(listBox, position, -1);
        if (sourceItem == -1) return;

        // 设置选中项并开始拖拽
        var data = new DataObject();
        listBox.SelectedIndex = Math.Clamp(sourceItem, 0, listBox.Items.Count - 1);
        data.Set(DataFormats.Text, sourceItem.ToString());

        DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
    }

    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        if (sender is not ListBox listBox || listBox.ItemsSource is not IList items || e.Data.Get(DataFormats.Text) is not string sourceIndexStr || !int.TryParse(sourceIndexStr, out int sourceIndex))
            return;
        var position = e.GetPosition(listBox);
        var targetIndex = GetTargetIndex(listBox, position);

        if (targetIndex == -1 || sourceIndex == -1) return;

        UpdateAdorner(listBox, targetIndex, items.Count);

        e.DragEffects = DragDropEffects.Move;
        e.Handled = true;
    }

    private static void OnDrop(object? sender, DragEventArgs e)
    {
        if (sender is not ListBox listBox || listBox.ItemsSource is not IList items || e.Data.Get(DataFormats.Text) is not string sourceIndexStr || !int.TryParse(sourceIndexStr, out int sourceIndex))
            return;
        var position = e.GetPosition(listBox);
        var targetIndex = GetTargetIndex(listBox, position);

        if (sourceIndex >= 0 && targetIndex >= 0 && sourceIndex != targetIndex)
        {
            if (GetEnableAnimation(listBox))
            {
                MoveWithAnimation(listBox, items, sourceIndex, targetIndex);
            }
            else
            {
                items.MoveTo(sourceIndex, targetIndex);
            }
        }
        ClearAdorner(listBox);
    }

    async private static Task AnimateItemMovement(ListBoxItem item, double startY, double endY, double duration = 400)
    {
        var completionSource = new TaskCompletionSource<bool>();
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(duration),
            Easing = new CubicEaseOut(),
            Children =
            {
                new KeyFrame
                {
                    KeyTime = TimeSpan.Zero,
                    Setters =
                    {
                        new Setter(TranslateTransform.YProperty, startY)
                    }
                },
                new KeyFrame
                {
                    KeyTime = TimeSpan.FromMilliseconds(duration - 3),
                    Setters =
                    {
                        new Setter(TranslateTransform.YProperty, endY)
                    }
                },
                new KeyFrame
                {
                    KeyTime = TimeSpan.FromMilliseconds(duration),
                    Setters =
                    {
                        new Setter(TranslateTransform.YProperty, endY)
                    }
                }
            }
        };
        animation.RunAsync(item).ContinueWith(t =>
        {
            completionSource.SetResult(true);
        });

        await completionSource.Task;
    }

    async private static Task MoveWithAnimation(ListBox listBox, IList items, int sourceIndex, int targetIndex)
    {
        var affectedItems = GetAffectedItems(listBox, sourceIndex, targetIndex);
        await RunPreUpdateAnimations(listBox, affectedItems, () =>
        {
            items.MoveTo(sourceIndex, targetIndex);
        });
    }

    private class ListBoxItemAndIndex(ListBoxItem? item, int sourceIndex, int targetIndex)
    {
        public ListBoxItem? ListBoxItem => item;
        public int SourceIndex => sourceIndex;
        public int TargetIndex => targetIndex;
    }

    private static IEnumerable<ListBoxItemAndIndex> GetAffectedItems(
        ListBox listBox,
        int sourceIndex,
        int targetIndex)
    {
        listBox.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var start = Math.Min(sourceIndex, targetIndex);
        var end = Math.Max(sourceIndex, targetIndex);

        var increment = sourceIndex < targetIndex ? -1 : +1;

        var affectedItems = new List<ListBoxItemAndIndex>();
        if (sourceIndex < targetIndex)
            targetIndex--;

        affectedItems.Add(new ListBoxItemAndIndex(listBox.ContainerFromIndex(sourceIndex) as ListBoxItem, sourceIndex, targetIndex));
        foreach (var i in Enumerable.Range(start, end - start))
        {
            if (i != sourceIndex)
                affectedItems.Add(new ListBoxItemAndIndex(listBox.ContainerFromIndex(i) as ListBoxItem, i, i + increment));
        }
        return affectedItems;
    }

    async private static Task RunPreUpdateAnimations(ListBox listBox,
        IEnumerable<ListBoxItemAndIndex> items,
        Action action)
    {
        var animations = items.Select(item =>
        {
            var startY = listBox.ContainerFromIndex(item.SourceIndex).Bounds.Y;
            var endY = listBox.ContainerFromIndex(item.TargetIndex).Bounds.Y;

            return AnimateItemMovement(item.ListBoxItem, 0, endY - startY, GetAnimationDuration(listBox));
        }).ToList();
        var delayTask = Task.Delay((int)GetAnimationDuration(listBox) - 2).ContinueWith(_ => action());
        animations.Add(delayTask);
        await Task.WhenAll(animations);
    }

    private static void OnDragLeave(object? sender, DragEventArgs e)
    {
        if (sender is ListBox listBox)
        {
            ClearAdorner(listBox);
        }
    }

    private static int GetSourceIndex(ListBox listBox, Point position, int defaultValue)
    {
        // 获取滚动视图和相关项
        var items = listBox.GetVisualDescendants()
            .OfType<ListBoxItem>()
            .ToList();

        if (items.Count == 0) return defaultValue;

        // 执行命中测试，使用原始位置
        var hitControl = listBox.InputHitTest(position) as Visual;
        if (hitControl == null) return defaultValue;

        // 查找命中的ListBoxItem
        var hitItem = hitControl
            .GetVisualAncestors()
            .OfType<ListBoxItem>()
            .FirstOrDefault();

        if (hitItem != null)
        {
            // 返回命中项的索引
            return listBox.IndexFromContainer(hitItem);
        }

        return defaultValue;
    }

    private static int GetTargetIndex(ListBox listBox, Point position)
    {
        // 获取滚动视图和可见项
        var scrollViewer = listBox.GetVisualDescendants()
            .OfType<ScrollViewer>()
            .FirstOrDefault();
        var items = listBox.GetVisualDescendants()
            .OfType<ListBoxItem>()
            .ToList();

        if (items.Count == 0) return -1;

        // 获取滚动偏移
        var scrollOffset = scrollViewer?.Offset ?? new Vector(0, 0);

        // 执行命中测试 - 使用原始位置
        var hitControl = listBox.InputHitTest(position) as Visual;

        // 如果命中测试没有结果，检查是否拖到了列表底部
        var lastItem = items.LastOrDefault();
        if (hitControl == null && lastItem != null)
        {
            var lastItemBottom = lastItem.Bounds.Y + lastItem.Bounds.Height;
            if (position.Y > lastItemBottom - scrollOffset.Y)
            {
                return listBox.ItemCount;
            }
            return -1;
        }

        // 查找命中的ListBoxItem
        var hitItem = hitControl?
            .GetVisualAncestors()
            .OfType<ListBoxItem>()
            .FirstOrDefault();

        if (hitItem != null)
        {
            // 获取目标索引
            var targetIndex = listBox.IndexFromContainer(hitItem);
            var itemBounds = hitItem.Bounds;

            // 计算项目中心点，考虑滚动偏移
            double itemY = itemBounds.Y - scrollOffset.Y;
            double itemCenter = itemY + itemBounds.Height / 2;

            // 根据位置是在项目上半部分还是下半部分确定目标索引
            var result = (position.Y > itemCenter) ? targetIndex + 1 : targetIndex;
            return result;
        }

        return -1;
    }


    // 修改装饰器更新逻辑
    private static void UpdateAdorner(ListBox listBox, int index, int count)
    {
        var adornerLayer = AdornerLayer.GetAdornerLayer(listBox);
        if (adornerLayer == null) return;

        // 处理特殊情况：拖到列表末尾
        var end = index == count;
        var displayIndex = end ? index - 1 : index;

        // 确保索引有效
        if (displayIndex < 0 || displayIndex >= listBox.ItemCount) return;

        // 获取容器和位置
        var container = listBox.ContainerFromIndex(displayIndex);
        if (container == null) return;

        // 获取容器相对于adornerLayer的实际位置
        var absolutePos = GetAbsolutePosition(container, adornerLayer);

        // 如果是拖到末尾，调整位置到容器底部
        if (end && container != null)
            absolutePos += new Point(0, container.Bounds.Height);

        // 计算ListBox相对于adornerLayer的左侧位置，考虑可能的菜单展开
        var listBoxLeftPos = GetAbsolutePosition(listBox, adornerLayer);

        // 获取ListBox的实际可见宽度
        double effectiveWidth = GetListBoxEffectiveWidth(listBox);

        // 创建或更新adorner
        if (listBox.GetValue(DragAdornerProperty) is not DragAdorner adorner)
        {
            adorner = new DragAdorner(
                listBoxLeftPos.X, // 使用ListBox的左侧实际位置
                effectiveWidth,
                SukiTheme.GetInstance().ActiveColorTheme.PrimaryBrush
            );
            listBox.SetValue(DragAdornerProperty, adorner);
        }
        else
        {
            // 确保已有adorner的X位置和宽度也会更新
            adorner.UpdateXPosition(listBoxLeftPos.X);
            adorner.UpdateWidth(effectiveWidth);
        }

        // 确保adorner已添加到层
        if (!adornerLayer.Children.Contains(adorner))
            adornerLayer.Children.Add(adorner);

        // 更新Y位置
        adorner.UpdatePosition(absolutePos.Y, index == 0, end);
    }

    // 获取ListBox的实际可见宽度
    private static double GetListBoxEffectiveWidth(ListBox listBox)
    {
        // 尝试获取滚动视图
        var scrollViewer = listBox.GetVisualDescendants()
            .OfType<ScrollViewer>()
            .FirstOrDefault();

        if (scrollViewer != null)
        {
            // 尝试获取内容宽度
            var content = scrollViewer.Content as Visual;
            if (content != null)
            {
                return Math.Min(listBox.Bounds.Width, content.Bounds.Width);
            }
        }

        // 退回到ListBox的宽度，减去边距
        return listBox.Bounds.Width - 20; // 减去一些边距以获得更好的视觉效果
    }

    private static Point GetAbsolutePosition(Control item, Visual relativeTo)
    {
        if (item == null) return new Point(0, 0);

        try
        {
            // 获取item相对于relativeTo(adornerLayer)的绝对位置
            // TranslatePoint会计算所有中间元素的偏移，包括侧边菜单引起的偏移
            var position = item.TranslatePoint(new Point(0, 0), relativeTo) ?? new Point(0, 0);
            return position;
        }
        catch
        {
            // 如果有任何异常，返回默认位置
            return new Point(0, 0);
        }
    }

    private static void ClearAdorner(ListBox listBox)
    {
        var adornerLayer = AdornerLayer.GetAdornerLayer(listBox);
        if (listBox.GetValue(DragAdornerProperty) is Views.UserControls.DragAdorner adorner)
        {
            adornerLayer?.Children.Remove(adorner);
        }
    }
}
