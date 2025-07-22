using Avalonia;
using Avalonia.Threading;
using System;

namespace MFAAvalonia.Helper;

public static class DispatcherHelper
{
    public static void RunOnMainThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        Dispatcher.UIThread.Invoke(action);

    }

    public static T RunOnMainThread<T>(Func<T> func)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            return func();
        }

        return Dispatcher.UIThread.Invoke(func);

    }

    public static void PostOnMainThread(Action func)
    {
        Dispatcher.UIThread.Post(func);
    }
}
