using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using MFAAvalonia.Configuration;
using MFAAvalonia.Views.Windows;
using MFAAvalonia.Helper;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace MFAAvalonia;

sealed class Program
{
    public static Dictionary<string, string> ParseArguments(string[] args)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < args.Length; i++)
        {
            // 识别以 "-" 或 "--" 开头的键
            if (args[i].StartsWith("-"))
            {
                string key = args[i].TrimStart('-').ToLower();
                // 检查下一个元素是否为值（非键）
                if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                {
                    parameters[key] = args[i + 1];
                    i++; // 跳过已处理的值
                }
                else
                {
                    parameters[key] = ""; // 标记无值的键
                }
            }
        }
        return parameters;
    }
    
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    public static Dictionary<string, string> Args { get; private set; } = new();
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            var parsedArgs = ParseArguments(args);
            LoggerHelper.Info("解析参数：" + JsonConvert.SerializeObject(parsedArgs, Formatting.Indented));
            Args = parsedArgs;
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args, ShutdownMode.OnMainWindowClose);
        }
        catch (Exception e)
        {
            LoggerHelper.Error($"总异常捕获：{e}");
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
    }
}
