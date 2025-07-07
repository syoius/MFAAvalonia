using System.Diagnostics;
using System.Runtime.InteropServices;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

public class Program
{
    private const int InitDelay = 2500;
    static StringBuilder LogBuilder = new();
    static void Main(string[] args)
    {
        Log("MFAUpdater Version:v" + Assembly.GetExecutingAssembly().GetName().Version);
        Log("Command Line Arguments: " + string.Join(",", args));
        Thread.Sleep(InitDelay); // 网页[6]启动延迟优化
       
        try
        {
            ValidateArguments(args); // 参数验证模块化
            HandleFileOperations(args); // 主操作封装
        }
        finally
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "logs", "updater_log.txt"), LogBuilder.ToString()); // 日志输出
        }
    }

    private static void Log(object message)
    {
        Console.WriteLine(message);
        LogBuilder.Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}");
        LogBuilder.AppendLine();
    }

    private static void ValidateArguments(string[] args)
    {
        if (args.Length is not (2 or 3 or 4)) // C# 9模式匹配
        {
            Log("""
                用法: 
                MFAUpdater [源路径] [目标路径] [原程序] [新程序名称] 
                或 
                MFAUpdater [源路径] [目标路径]
                """);

            Environment.Exit(1); // 标准化退出码
        }
    }

    private static void HandleFileOperations(string[] args)
    {
        try
        {

            var source = Path.GetFullPath(args[0].Replace("\\\"", "\"").Replace("\"", "")).Replace('\\', Path.DirectorySeparatorChar);
            var dest = Path.GetFullPath(args[1].Replace("\\\"", "\"").Replace("\"", "")).Replace('\\', Path.DirectorySeparatorChar);

            if (File.Exists(source))
                HandleFileTransfer(source, dest);
            else if (Directory.Exists(source))
                HandleDirectoryTransfer(source, dest);
            else
                throw new FileNotFoundException($"路径不存在: {source}");

            if (args.Length == 4)
                HandleAppRename(source, dest, args[2].Replace("\\\"", "\"").Replace("\"", ""), args[3].Replace("\\\"", "\"").Replace("\"", "")); // 重命名与启动分离
            if (args.Length == 3)
                StartCrossPlatformProcess(args[2].Replace("\\\"", "\"").Replace("\"", ""));
            HandleDeleteDirectoryTransfer(source);
        }
        catch (Exception ex)
        {
            HandlePlatformSpecificErrors(ex); // 跨平台错误处理
            Environment.Exit(ex.HResult); // 返回错误码
        }
    }

    private static void HandleFileTransfer(string source, string dest)
    {
        try
        {
            var destFile = Path.GetFileName(dest);
            if (destFile.Contains("MFAAvalonia.dll", StringComparison.OrdinalIgnoreCase) || !destFile.Contains("MFAUpdater", StringComparison.OrdinalIgnoreCase) && !destFile.Contains("MFAAvalonia", StringComparison.OrdinalIgnoreCase))
            {
                Log($"From {source} to {dest}");
                File.Copy(source, dest, true);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }

        SetUnixPermissions(dest); // 网页[6][7]权限设置
    }

    private static void HandleDirectoryTransfer(string source, string dest)
    {
        Directory.CreateDirectory(dest);

        // 递归复制优化（网页[1]目录处理）
        foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(dir.Replace(source, dest));

        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var destFile = file.Replace(source, dest);
            HandleFileTransfer(file, destFile);
        }

        Log($"目录迁移完成: {dest}");
    }

    private static void HandleDeleteDirectoryTransfer(string source)
    {
        try
        {
            Directory.Delete(source, true);
            Log($"源目录{source}删除完成");
        }
        catch (Exception e)
        {
            Log($"源目录{source}删除失败: {e}");
        }

    }

    private static void HandleAppRename(string source, string dest, string oldName, string newName)
    {
        var oldPath = Path.Combine(source, oldName);
        var newPath = Path.Combine(dest, newName);

        File.Move(oldPath, newPath, true);

        SetUnixPermissions(newPath); // 网页[6]可执行权限

        StartCrossPlatformProcess(newName); // 统一启动入口
    }

    private static void SetUnixPermissions(string path)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) // 网页[1][7]平台检测
        {
            try
            {
                using var chmod = Process.Start(new ProcessStartInfo
                {
                    FileName = "chmod",
                    Arguments = $"+rwx \"{path}\"",
                    UseShellExecute = false
                });
                chmod?.WaitForExit();
            }
            catch
            {
                Log($"权限设置失败，请手动执行: sudo chmod 755 \"{path}\"");
            }
        }
    }

    private static void StartCrossPlatformProcess(string appName)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
                FileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? appName : $"./{appName}"
            };

            using var process = Process.Start(startInfo);
            Log($"进程已启动 [PID:{process?.Id}]");
        }
        catch (Exception ex)
        {
            Log($"启动失败: {ex.Message}");
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                Log($"尝试: chmod +x {appName} && ./{appName}");
        }
    }


    private static void HandlePlatformSpecificErrors(Exception ex)
    {
        Log($"错误: {ex}");
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && ex is UnauthorizedAccessException)
        {
            Log("""
                Linux/macOS 权限问题解决方案:
                1. 使用 sudo 重新运行
                2. 检查文件所有权: ls -l
                3. 手动设置权限: chmod -R 755 [目标目录]
                """);
        }
    }
}
