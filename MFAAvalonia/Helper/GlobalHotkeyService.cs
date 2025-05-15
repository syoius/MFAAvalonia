using Avalonia.Input;
using Avalonia.Threading;
using MFAAvalonia.Extensions;
using SharpHook;
using SharpHook.Native;
using System;
using System.Collections.Concurrent;
using System.Windows.Input;

namespace MFAAvalonia.Helper;

public static class GlobalHotkeyService
{
    // 线程安全的热键存储（Key: 组合键标识，Value: 关联命令）
    private static readonly ConcurrentDictionary<(KeyCode, ModifierMask), ICommand> _commands = new();
    private static TaskPoolGlobalHook? _hook;

    /// <summary>
    /// 初始化全局钩子服务
    /// </summary>
    public static void Initialize()
    {
        if (_hook != null) return;

        try
        {
            _hook = new TaskPoolGlobalHook();
            _hook.KeyPressed += HandleKeyEvent;
            _hook.RunAsync(); // 启动后台监听线程
        }
        catch (Exception e)
        {
            LoggerHelper.Error(e);
            ToastHelper.Error("GlobalHotkeyServiceError".ToLocalization());
        }
    }

    /// <summary>
    /// 注册全局热键（跨平台）
    /// </summary>
    /// <param name="gesture">热键手势（需转换为SharpHook的按键标识）</param>
    /// <param name="command">关联命令</param>
    public static bool Register(KeyGesture? gesture, ICommand command)
    {
        if (gesture == null || command == null)
            return true;
        var (keyCode, modifiers) = ConvertGesture(gesture);
        return _commands.TryAdd((keyCode, modifiers), command);
    }

    /// <summary>
    /// 注销全局热键
    /// </summary>
    public static void Unregister(KeyGesture? gesture)
    {
        if (gesture == null) return;

        var (keyCode, modifiers) = ConvertGesture(gesture);
        _commands.TryRemove((keyCode, modifiers), out _);
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public static void Shutdown()
    {
        _hook?.Dispose();
        _commands.Clear();
    }

    // 转换Avalonia手势到SharpHook标识
    private static (KeyCode, ModifierMask) ConvertGesture(KeyGesture gesture)
    {
        var keyCode = Enum.TryParse(typeof(KeyCode), $"Vc{gesture.Key.ToString()}", out var key) ? (KeyCode)key : KeyCode.VcEscape;
        var modifiers = gesture.KeyModifiers switch
        {
            KeyModifiers.Control => ModifierMask.LeftCtrl,
            KeyModifiers.Alt => ModifierMask.LeftAlt,
            KeyModifiers.Shift => ModifierMask.LeftShift,
            KeyModifiers.Meta => ModifierMask.LeftMeta,
            _ => ModifierMask.None
        };
        return (keyCode, modifiers);
    }

    // 处理全局按键事件
// 修改 HandleKeyEvent 方法
    private static void HandleKeyEvent(object? sender, KeyboardHookEventArgs e)
    {
        // 从 UioHookEvent 中获取修饰键状态
        var modifiers = e.RawEvent.Mask; // 直接访问原始事件数据
        var keyCode = e.Data.KeyCode;

        if (_commands.TryGetValue((keyCode, modifiers), out var command) && command.CanExecute(null))
        {
            Dispatcher.UIThread.Post(() => command.Execute(null));
        }
    }
}
