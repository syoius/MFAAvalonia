using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using AvaloniaEdit.Utils;
using System;

namespace MFAAvalonia.Extensions;

public static class ComboBoxExtensions
{
    public static readonly AttachedProperty<bool> DisableNavigationOnLostFocusProperty =
        AvaloniaProperty.RegisterAttached<ComboBox, bool>(
            "DisableNavigationOnLostFocus",
            typeof(DragDropExtensions),
            defaultValue: false);
    
    public static bool GetDisableNavigationOnLostFocus(ComboBox box) =>
        box.GetValue(DisableNavigationOnLostFocusProperty);

    public static void SetDisableNavigationOnLostFocus(ComboBox box, bool value) =>
        box.SetValue(DisableNavigationOnLostFocusProperty, value);

    private static void OnDisableNavigationOnLostFocusChanged(AvaloniaPropertyChangedEventArgs<bool> args)
    {
        if (args.Sender is ComboBox comboBox)
        {

            if (args.NewValue.Value)
            {
                comboBox.AddHandler(
                    InputElement.KeyDownEvent,
                    (EventHandler<KeyEventArgs>)HandleKeyDown,
                    RoutingStrategies.Tunnel); 
                comboBox.AddHandler(
                    InputElement.PointerWheelChangedEvent,
                    (EventHandler<PointerWheelEventArgs>)HandlePointerWheel,
                    RoutingStrategies.Tunnel);
            }
            else
            {
                comboBox.RemoveHandler(InputElement.KeyDownEvent, (EventHandler<KeyEventArgs>)HandleKeyDown);
                comboBox.RemoveHandler(InputElement.PointerWheelChangedEvent, (EventHandler<PointerWheelEventArgs>)HandlePointerWheel);
            }
        }
    }


    private static bool IsInputAllowed(ComboBox comboBox) =>
        comboBox.IsDropDownOpen;

    private static void HandleKeyDown(object sender, KeyEventArgs e)
    {
        var comboBox = (ComboBox)sender;
        if (!IsInputAllowed(comboBox) && (e.Key == Key.Up || e.Key == Key.Down))
        {
            e.Handled = true;
        }
    }

    private static void HandlePointerWheel(object sender, PointerWheelEventArgs e)
    {
        var comboBox = (ComboBox)sender;
        if (!IsInputAllowed(comboBox))
        {
            e.Handled = true;
        }
    }


    private static void HandleDropDownOpened(object sender, EventArgs e) => UpdateInputState((ComboBox)sender);
    private static void HandleDropDownClosed(object sender, EventArgs e) => UpdateInputState((ComboBox)sender);

    // 更新输入拦截状态
    private static void UpdateInputState(ComboBox comboBox)
    {

    }


    static ComboBoxExtensions()
    {
        DisableNavigationOnLostFocusProperty.Changed.Subscribe(OnDisableNavigationOnLostFocusChanged);
    }
}
