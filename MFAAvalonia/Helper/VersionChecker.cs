using Avalonia.Controls;
using MFAAvalonia.Configuration;
using MFAAvalonia.Extensions;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using MFAAvalonia.Helper.Converters;
using MFAAvalonia.ViewModels.Windows;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Semver;
using SukiUI.Dialogs;
using SukiUI.Toasts;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MFAAvalonia.Helper;
#pragma warning  disable CS1998 // 此异步方法缺少 "await" 运算符，将以同步方式运行。
#pragma warning  disable CS4014 // 由于此调用不会等待，因此在此调用完成之前将会继续执行当前方法。
public static class VersionChecker
{
    private static readonly ConcurrentQueue<ValueType.MFATask> Queue = new();
    public static void Check()
    {
        var config = new
        {
            AutoUpdateResource = ConfigurationManager.Current.GetValue(ConfigurationKeys.EnableAutoUpdateResource, false),
            AutoUpdateMFA = ConfigurationManager.Current.GetValue(ConfigurationKeys.EnableAutoUpdateMFA, false),
            CheckVersion = ConfigurationManager.Current.GetValue(ConfigurationKeys.EnableCheckVersion, true),
        };

        if (config.AutoUpdateResource)
        {
            AddResourceUpdateTask(config.AutoUpdateMFA);
        }
        else if (config.CheckVersion)
        {
            AddResourceCheckTask();
        }

        if (config.AutoUpdateMFA)
        {
            AddMFAUpdateTask();
        }
        else if (config.CheckVersion)
        {
            AddMFACheckTask();
        }

        TaskManager.RunTaskAsync(async () => await ExecuteTasksAsync(),
            () => ToastNotification.Show("自动更新时发生错误！"), "启动检测");
    }
    public static void CheckMFAVersionAsync() => TaskManager.RunTaskAsync(() => CheckForMFAUpdates(Instances.VersionUpdateSettingsUserControlModel.DownloadSourceIndex == 0));
    public static void CheckResourceVersionAsync() => TaskManager.RunTaskAsync(() => CheckForResourceUpdates(Instances.VersionUpdateSettingsUserControlModel.DownloadSourceIndex == 0));
    public static void UpdateResourceAsync() => TaskManager.RunTaskAsync(() => UpdateResource(Instances.VersionUpdateSettingsUserControlModel.DownloadSourceIndex == 0));
    public static void UpdateMFAAsync() => TaskManager.RunTaskAsync(() => UpdateMFA(Instances.VersionUpdateSettingsUserControlModel.DownloadSourceIndex == 0));

    public static void UpdateMaaFwAsync() => TaskManager.RunTaskAsync(() => UpdateMaaFw());

    private static void AddResourceCheckTask()
    {
        Queue.Enqueue(new ValueType.MFATask
        {
            Action = async () => CheckForResourceUpdates(Instances.VersionUpdateSettingsUserControlModel.DownloadSourceIndex == 0),
            Name = "更新资源"
        });
    }

    private static void AddMFACheckTask()
    {
        Queue.Enqueue(new ValueType.MFATask
        {
            Action = async () => CheckForMFAUpdates(Instances.VersionUpdateSettingsUserControlModel.DownloadSourceIndex == 0),
            Name = "更新软件"
        });
    }

    private static void AddResourceUpdateTask(bool autoUpdateMFA)
    {
        Queue.Enqueue(new ValueType.MFATask
        {
            Action = async () => await UpdateResource(Instances.VersionUpdateSettingsUserControlModel.DownloadSourceIndex == 0, autoUpdateMFA, true),
            Name = "更新资源"
        });
    }

    private static SemaphoreSlim _queueLock = new(1, 1);

    private static void AddMFAUpdateTask()
    {
        Queue.Enqueue(new ValueType.MFATask
        {
            Action = async () => UpdateMFA(Instances.VersionUpdateSettingsUserControlModel.DownloadSourceIndex == 0),
            Name = "更新软件"
        });
    }

    public static void CheckForResourceUpdates(bool isGithub = true)
    {
        Instances.RootViewModel.SetUpdating(true);
        var url = MaaProcessor.Interface?.Url ?? string.Empty;

        string[] strings = [];
        try
        {
            if (isGithub)
            {
                if (string.IsNullOrWhiteSpace(url))
                {
                    ToastHelper.Info("CurrentResourcesNotSupportGitHub".ToLocalization());
                    Instances.RootViewModel.SetUpdating(false);
                    return;
                }
                strings = GetRepoFromUrl(url);
            }
            var resourceVersion = GetResourceVersion();
            if (string.IsNullOrWhiteSpace(resourceVersion))
            {
                Instances.RootViewModel.SetUpdating(false);
                return;
            }

            string latestVersion = string.Empty;
            if (isGithub)
                GetLatestVersionAndDownloadUrlFromGithub(out var downloadUrl, out latestVersion, strings[0], strings[1]);
            else
                GetDownloadUrlFromMirror(resourceVersion, GetResourceID(), CDK(), out _, out latestVersion, onlyCheck: true);

            if (string.IsNullOrWhiteSpace(latestVersion))
            {
                Instances.RootViewModel.SetUpdating(false);
                return;
            }

            if (IsNewVersionAvailable(latestVersion, resourceVersion))
            {
                DispatcherHelper.RunOnMainThread(() =>
                {
                    Instances.ToastManager.CreateToast().WithTitle("UpdateResource".ToLocalization())
                        .WithContent("ResourceOption".ToLocalization() + "NewVersionAvailableLatestVersion".ToLocalization() + latestVersion).Dismiss().After(TimeSpan.FromSeconds(6))
                        .WithActionButtonNormal("Later".ToLocalization(), _ => { }, true)
                        .WithActionButton("Update".ToLocalization(), _ => UpdateResourceAsync(), true).Queue();
                });
            }
            else
            {
                ToastHelper.Info("ResourcesAreLatestVersion".ToLocalization());
            }
            Instances.RootViewModel.SetUpdating(false);
        }
        catch (Exception ex)
        {
            Instances.RootViewModel.SetUpdating(false);
            if (ex.Message.Contains("resource not found"))
                ToastHelper.Error("CurrentResourcesNotSupportMirror".ToLocalization());
            else
                ToastHelper.Error("ErrorWhenCheck".ToLocalizationFormatted(true, "Resource"), ex.Message);
            LoggerHelper.Error(ex);
        }
    }

    public static void CheckForMFAUpdates(bool isGithub = true)
    {
        try
        {
            Instances.RootViewModel.SetUpdating(true);
            var localVersion = GetLocalVersion();
            string latestVersion = string.Empty;
            if (isGithub)
                GetLatestVersionAndDownloadUrlFromGithub(out var downloadUrl, out latestVersion);
            else
                GetDownloadUrlFromMirror(localVersion, "YuanMFA", CDK(), out _, out latestVersion, isUI: true, onlyCheck: true);

            if (IsNewVersionAvailable(latestVersion, localVersion))
            {
                DispatcherHelper.RunOnMainThread(() =>
                {
                    Instances.ToastManager.CreateToast().WithTitle("SoftwareUpdate".ToLocalization())
                        .WithContent("MFA" + "NewVersionAvailableLatestVersion".ToLocalization() + latestVersion).Dismiss().After(TimeSpan.FromSeconds(6))
                        .WithActionButtonNormal("Later".ToLocalization(), _ => { }, true)
                        .WithActionButton("Update".ToLocalization(), _ => UpdateMFAAsync(), true).Queue();
                });
            }
            else
            {
                ToastHelper.Info("MFAIsLatestVersion".ToLocalization());
            }

            Instances.RootViewModel.SetUpdating(false);
        }
        catch (Exception ex)
        {
            if (ex.Message.Contains("resource not found"))
                ToastHelper.Error("CurrentResourcesNotSupportMirror".ToLocalization());
            else
                ToastHelper.Error("ErrorWhenCheck".ToLocalizationFormatted(false, "MFA"), ex.Message);
            Instances.RootViewModel.SetUpdating(false);
            LoggerHelper.Error(ex);
        }
    }

    public async static Task UpdateResource(bool isGithub = true, bool closeDialog = false, bool noDialog = false, Action action = null)
    {
        Instances.RootViewModel.SetUpdating(true);
        ProgressBar? progress = null;
        TextBlock? textBlock = null;
        ISukiToast? sukiToast = null;
        DispatcherHelper.RunOnMainThread(() =>
        {
            progress = new ProgressBar
            {
                Value = 0,
                ShowProgressText = true
            };
            StackPanel stackPanel = new();
            var textBlock = new TextBlock()
            {
                Text = "GettingLatestResources".ToLocalization(),
            };
            stackPanel.Children.Add(textBlock);
            stackPanel.Children.Add(progress);
            sukiToast = Instances.ToastManager.CreateToast()
                .WithTitle("UpdateResource".ToLocalization())
                .WithContent(stackPanel).Queue();
        });


        var localVersion = MaaProcessor.Interface?.Version ?? string.Empty;

        if (string.IsNullOrWhiteSpace(localVersion))
        {
            Dismiss(sukiToast);
            ToastHelper.Warn("FailToGetCurrentVersionInfo".ToLocalization());
            Instances.RootViewModel.SetUpdating(false);
            return;
        }
        SetProgress(progress, 10);
        string[] strings = [];
        if (isGithub)
        {
            var url = MaaProcessor.Interface?.Url ?? string.Empty;
            if (string.IsNullOrWhiteSpace(url))
            {
                Dismiss(sukiToast);
                ToastHelper.Warn("CurrentResourcesNotSupportGitHub".ToLocalization());
                Instances.RootViewModel.SetUpdating(false);
                return;
            }
            strings = GetRepoFromUrl(url);
        }
        string latestVersion = string.Empty;
        string downloadUrl = string.Empty;
        try
        {
            if (isGithub)
                GetLatestVersionAndDownloadUrlFromGithub(out downloadUrl, out latestVersion, strings[0], strings[1]);
            else
                GetDownloadUrlFromMirror(localVersion, GetResourceID(), CDK(), out downloadUrl, out latestVersion);

        }
        catch (Exception ex)
        {
            Dismiss(sukiToast);
            ToastHelper.Warn($"{"FailToGetLatestVersionInfo".ToLocalization()}", ex.Message);
            Instances.RootViewModel.SetUpdating(false);
            LoggerHelper.Error(ex);
            return;
        }

        SetProgress(progress, 50);

        if (string.IsNullOrWhiteSpace(latestVersion))
        {
            Dismiss(sukiToast);
            ToastHelper.Warn("FailToGetLatestVersionInfo".ToLocalization());
            Instances.RootViewModel.SetUpdating(false);
            Instances.TaskQueueViewModel.ClearDownloadProgress();

            return;
        }

        if (!IsNewVersionAvailable(latestVersion, localVersion))
        {
            Dismiss(sukiToast);
            ToastHelper.Info("ResourcesAreLatestVersion".ToLocalization());
            Instances.RootViewModel.SetUpdating(false);
            Instances.TaskQueueViewModel.ClearDownloadProgress();
            action?.Invoke();
            return;
        }

        SetProgress(progress, 100);

        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            Dismiss(sukiToast);
            ToastHelper.Warn("FailToGetDownloadUrl".ToLocalization());
            Instances.RootViewModel.SetUpdating(false);
            Instances.TaskQueueViewModel.ClearDownloadProgress();
            return;
        }

        var tempPath = Path.Combine(AppContext.BaseDirectory, "temp_res");
        Directory.CreateDirectory(tempPath);

        var tempZipFilePath = Path.Combine(tempPath, $"resource_{latestVersion}.zip");
        SetText(textBlock, "Downloading".ToLocalization());
        SetProgress(progress, 0);
        if (!await DownloadFileAsync(downloadUrl, tempZipFilePath, progress, "GameResourceUpdated"))
        {
            Dismiss(sukiToast);
            ToastHelper.Warn("DownloadFailed".ToLocalization());
            return;
        }

        SetText(textBlock, "ApplyingUpdate".ToLocalization());
        SetProgress(progress, 0);

        var tempExtractDir = Path.Combine(tempPath, $"resource_{latestVersion}_extracted");
        if (Directory.Exists(tempExtractDir)) Directory.Delete(tempExtractDir, true);
        if (!File.Exists(tempZipFilePath))
        {
            Dismiss(sukiToast);
            ToastHelper.Warn("DownloadFailed".ToLocalization());
            return;
        }

        ZipFile.ExtractToDirectory(tempZipFilePath, tempExtractDir);
        SetProgress(progress, 50);
        var originPath = tempExtractDir;
        var interfacePath = Path.Combine(tempExtractDir, "interface.json");
        var resourceDirPath = Path.Combine(tempExtractDir, "resource");

        var wpfDir = AppContext.BaseDirectory;
        var resourcePath = Path.Combine(wpfDir, "resource");
        if (!File.Exists(interfacePath))
        {
            originPath = Path.Combine(tempExtractDir, "assets");
            interfacePath = Path.Combine(tempExtractDir, "assets", "interface.json");
            resourceDirPath = Path.Combine(tempExtractDir, "assets", "resource");
        }

        if (isGithub)
        {
            if (Directory.Exists(resourcePath))
            {
                foreach (var rfile in Directory.EnumerateFiles(resourcePath, "*", SearchOption.AllDirectories))
                {
                    var fileName = Path.GetFileName(rfile);
                    if (fileName.Equals(AnnouncementViewModel.AnnouncementFileName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    try
                    {
                        File.SetAttributes(rfile, FileAttributes.Normal);
                        File.Delete(rfile);
                    }
                    catch (Exception ex)
                    {
                        LoggerHelper.Warning($"文件删除失败: {rfile} - {ex.Message}");
                    }
                }
            }
        }
        else
        {
            var changesPath = Path.Combine(tempExtractDir, "changes.json");
            if (File.Exists(changesPath))
            {
                var changes = await File.ReadAllTextAsync(changesPath);
                try
                {
                    var changesJson = JsonConvert.DeserializeObject<MirrorChangesJson>(changes);
                    if (changesJson?.Deleted != null)
                    {
                        var delPaths = changesJson.Deleted
                            .Select(del => Path.Combine(AppContext.BaseDirectory, del))
                            .Where(File.Exists);

                        foreach (var delPath in delPaths)
                        {
                            File.Delete(delPath);
                        }
                    }
                }
                catch (Exception e)
                {
                    LoggerHelper.Error(e);
                }
            }

        }
        var file = new FileInfo(interfacePath);
        if (file.Exists)
        {
            var targetPath = Path.Combine(wpfDir, "interface.json");
            file.CopyTo(targetPath, true);
        }

        SetProgress(progress, 60);

        var di = new DirectoryInfo(resourceDirPath);
        if (di.Exists)
        {
            DirectoryMerge(originPath, wpfDir);
        }

        SetProgress(progress, 70);

        File.Delete(tempZipFilePath);
        Directory.Delete(tempExtractDir, true);
        SetProgress(progress, 80);

        var newInterfacePath = Path.Combine(wpfDir, "interface.json");
        if (File.Exists(newInterfacePath))
        {
            var jsonContent = await File.ReadAllTextAsync(newInterfacePath);
            var settings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Ignore,
                DefaultValueHandling = DefaultValueHandling.Ignore
            };

            settings.Converters.Add(new MaaInterfaceSelectOptionConverter(true));

            var @interface = JsonConvert.DeserializeObject<MaaInterface>(jsonContent, settings);
            if (@interface != null)
            {
                @interface.Url = MaaProcessor.Interface?.Url;
                @interface.Version = latestVersion;
            }
            var updatedJsonContent = JsonConvert.SerializeObject(@interface, settings);

            await File.WriteAllTextAsync(newInterfacePath, updatedJsonContent);
        }

        SetProgress(progress, 100);

        SetText(textBlock, "UpdateCompleted".ToLocalization());
        // dialog?.SetRestartButtonVisibility(true);

        Instances.RootViewModel.SetUpdating(false);

        DispatcherHelper.RunOnMainThread(() =>
        {
            if (!noDialog)
            {
                Instances.DialogManager.CreateDialog().WithContent("GameResourceUpdated".ToLocalization()).WithActionButton("Yes".ToLocalization(), _ =>
                    {
                        Process.Start(Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty);
                        Instances.ShutdownApplication();
                        Instances.ApplicationLifetime.Shutdown();
                    }, dismissOnClick: true, "Flat", "Accent")
                    .WithActionButton("No".ToLocalization(), _ =>
                    {
                    }, dismissOnClick: true).TryShow();
            }
        });

        var tasks = Instances.TaskQueueViewModel.TaskItemViewModels;
        Instances.RootView.ClearTasks(() => MaaProcessor.Instance.InitializeData(dragItem: tasks));
        action?.Invoke();
    }

    public async static Task UpdateMFA(bool isGithub, bool noDialog = false)
    {
        Instances.RootViewModel.SetUpdating(true);
        ProgressBar? progress = null;
        TextBlock? textBlock = null;
        ISukiToast? sukiToast = null;

        // 初始化进度UI
        DispatcherHelper.RunOnMainThread(() =>
        {
            progress = new ProgressBar
            {
                Value = 0,
                ShowProgressText = true
            };
            textBlock = new TextBlock
            {
                Text = "GettingLatestSoftware".ToLocalization()
            };

            var stackPanel = new StackPanel();
            stackPanel.Children.Add(textBlock);
            stackPanel.Children.Add(progress);

            sukiToast = Instances.ToastManager.CreateToast()
                .WithTitle("SoftwareUpdate".ToLocalization())
                .WithContent(stackPanel)
                .Queue();
        });

        try
        {
            SetProgress(progress, 10);

            // 获取版本信息
            string downloadUrl, latestVersion;
            try
            {
                if (isGithub)
                    GetLatestVersionAndDownloadUrlFromGithub(out downloadUrl, out latestVersion);
                else
                    GetDownloadUrlFromMirror(GetLocalVersion(), "MFAAvalonia", CDK(), out downloadUrl, out latestVersion, isUI: true);
            }
            catch (Exception ex)
            {
                Dismiss(sukiToast);
                ToastHelper.Warn($"{"FailToGetLatestVersionInfo".ToLocalization()}", ex.Message);
                LoggerHelper.Error(ex);
                return;
            }

            // 版本验证
            SetProgress(progress, 50);
            if (string.IsNullOrWhiteSpace(latestVersion) || !IsNewVersionAvailable(latestVersion, GetLocalVersion()))
            {
                Dismiss(sukiToast);
                ToastHelper.Info("MFAIsLatestVersion".ToLocalization());
                return;
            }

            // 准备临时目录
            var tempPath = Path.Combine(AppContext.BaseDirectory, "temp_mfa");
            Directory.CreateDirectory(tempPath);

            // 下载更新包
            SetText(textBlock, "Downloading".ToLocalization());
            SetProgress(progress, 0);
            var tempZip = Path.Combine(tempPath, $"mfa_{latestVersion}.zip");
            if (!await DownloadWithRetry(downloadUrl, tempZip, progress, 3))
            {
                Dismiss(sukiToast);
                ToastHelper.Warn("DownloadFailed");
                return;
            }

            // 解压文件
            SetProgress(progress, 20);
            var extractDir = Path.Combine(tempPath, $"mfa_{latestVersion}_extracted");
            if (Directory.Exists(extractDir))
                Directory.Delete(extractDir, true);
            ZipFile.ExtractToDirectory(tempZip, extractDir);

            SetText(textBlock, "ApplyingUpdate".ToLocalization());
            // 执行安全更新
            SetProgress(progress, 40);
            var utf8Bytes = Encoding.UTF8.GetBytes(AppContext.BaseDirectory);
            var utf8BaseDirectory = Encoding.UTF8.GetString(utf8Bytes);
            var sourceBytes = Encoding.UTF8.GetBytes(extractDir);
            var sourceDirectory = Encoding.UTF8.GetString(sourceBytes);

            SetProgress(progress, 60);
            string updaterName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? "MFAUpdater.exe"
                : "MFAUpdater";
            // 构建完整路径
            string sourceUpdaterPath = Path.Combine(sourceDirectory, updaterName); // 源目录路径
            string targetUpdaterPath = Path.Combine(utf8BaseDirectory, updaterName); // 目标目录路径

            try
            {
                if (File.Exists(sourceUpdaterPath) && !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var chmodProcess = Process.Start("/bin/chmod", $"+x {sourceDirectory}");
                    await chmodProcess?.WaitForExitAsync();
                }
                File.Copy(sourceUpdaterPath, targetUpdaterPath, overwrite: true);
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    try
                    {
                        File.Copy(Path.Combine(sourceDirectory, "MFAUpdater.dll"), Path.Combine(utf8BaseDirectory, "MFAUpdater.dll"), overwrite: true);
                        LoggerHelper.Info($"成功复制更新器.dll到目标目录: {Path.Combine(utf8BaseDirectory, "MFAUpdater.dll")}");
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                    }
                }
                LoggerHelper.Info($"成功复制更新器到目标目录: {targetUpdaterPath}");

                // 验证源文件存在性
                if (!File.Exists(sourceUpdaterPath))
                {
                    LoggerHelper.Error($"更新器在源目录缺失: {sourceUpdaterPath}");
                    throw new FileNotFoundException("更新程序源文件未找到");
                }
            }
            catch (IOException ex)
            {
                LoggerHelper.Error($"文件操作失败: {ex.Message} (错误代码: {ex.HResult})");
                throw new InvalidOperationException("文件复制过程中发生I/O错误", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                LoggerHelper.Error($"权限不足: {ex.Message}");
                throw new SecurityException("文件访问权限被拒绝", ex);
            }

            SetProgress(progress, 100);

            await ApplySecureUpdate(sourceDirectory, utf8BaseDirectory, $"{Assembly.GetEntryAssembly().GetName().Name}.exe", Process.GetCurrentProcess().MainModule.ModuleName);

            Thread.Sleep(500);
        }
        finally
        {
            Instances.RootViewModel.SetUpdating(false);
            Dismiss(sukiToast);
        }
    }

    #region 增强型更新核心方法

    async private static Task<bool> DownloadWithRetry(string url, string savePath, ProgressBar progress, int retries)
    {
        for (int i = 0; i < retries; i++)
        {
            try
            {
                return await DownloadFileAsync(url, savePath, progress, "GameResourceUpdated");
            }
            catch (WebException ex) when (i < retries - 1)
            {
                LoggerHelper.Warning($"下载重试 ({i + 1}/{retries}): {ex.Status}");
                await Task.Delay(2000 * (i + 1));
            }
        }
        return false;
    }

    async private static Task ApplySecureUpdate(string source, string target, string oldName = "", string newName = "")
    {
        string updaterName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "MFAUpdater.exe"
            : "MFAUpdater";

        string updaterPath = Path.Combine(AppContext.BaseDirectory, updaterName);

        if (!File.Exists(updaterPath))
        {
            LoggerHelper.Error($"更新器在目录缺失: {updaterPath}");
            throw new FileNotFoundException("更新程序源文件未找到");
        }

        if (File.Exists(updaterPath) && !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var chmodProcess = Process.Start("/bin/chmod", $"+x {updaterPath}");
            await chmodProcess?.WaitForExitAsync();
        }

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var chmodProcess = Process.Start("/bin/chmod", $"+x {updaterPath}");
            await chmodProcess?.WaitForExitAsync();
        }

        var psi = new ProcessStartInfo
        {
            FileName = updaterName,
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
            Arguments = (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName)) ? $"{source} {target}" : $"{source} {target} {oldName} {newName}",
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        LoggerHelper.Info($"{AppContext.BaseDirectory}{updaterName} {source} {target} {oldName} {newName}");

        try
        {
            using var updaterProcess = Process.Start(psi);
            if (updaterProcess?.HasExited == false)
            {
                LoggerHelper.Info($"更新器已启动(PID:{updaterProcess.Id})");
            }
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"启动失败: {ex.Message}");
            throw;
        }
        finally
        {
            Environment.Exit(0);
        }
    }

    private static string CreateVersionBackup(string dir)
    {
        var backupPath = Path.Combine(AppContext.BaseDirectory, dir);

        Directory.CreateDirectory(backupPath);
        return backupPath;
    }

    async private static Task ReplaceFilesWithRetry(string sourceDir, string backupDir, int maxRetry = 3)
    {
        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDir, file);
            var targetPath = Path.Combine(AppContext.BaseDirectory, relativePath);
            var backupPath = Path.Combine(backupDir, relativePath);

            for (int i = 0; i < maxRetry; i++)
            {
                try
                {
                    if (File.Exists(targetPath))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(backupPath));
                        File.Move(targetPath, backupPath, overwrite: true);
                    }
                    File.Move(file, targetPath, overwrite: true);
                    break;
                }
                catch (IOException ex) when (i < maxRetry - 1)
                {
                    await Task.Delay(1000 * (i + 1));
                    LoggerHelper.Warning($"文件替换重试: {ex.Message}");
                }
            }
        }
    }

    #endregion

    public async static Task UpdateMaaFw()
    {
        Instances.RootViewModel.SetUpdating(true);
        ProgressBar? progress = null;
        TextBlock? textBlock = null;
        ISukiToast? sukiToast = null;

        // UI初始化（与原有逻辑保持一致）
        DispatcherHelper.RunOnMainThread(() =>
        {
            progress = new ProgressBar
            {
                Value = 0,
                ShowProgressText = true
            };
            textBlock = new TextBlock
            {
                Text = "GettingLatestMaaFW".ToLocalization()
            };
            var stackPanel = new StackPanel();
            stackPanel.Children.Add(textBlock);
            stackPanel.Children.Add(progress);
            sukiToast = Instances.ToastManager.CreateToast()
                .WithTitle("UpdateMaaFW".ToLocalization())
                .WithContent(stackPanel).Queue();
        });

        try
        {
            // 版本信息获取（保持原有逻辑）
            SetProgress(progress, 10);
            var resId = "MaaFramework";
            var currentVersion = MaaProcessor.Utility.Version;
            string downloadUrl = string.Empty, latestVersion = string.Empty;
            GetDownloadUrlFromMirror(currentVersion, resId, CDK(), out downloadUrl, out latestVersion, "MFA", true);

            // 版本校验（保持原有逻辑）
            SetProgress(progress, 50);
            if (!IsNewVersionAvailable(latestVersion, currentVersion))
            {
                Dismiss(sukiToast);
                ToastHelper.Info("MaaFwIsLatestVersion".ToLocalization());
                return;
            }

            // 下载与解压（优化为使用DownloadWithRetry）
            var tempPath = Path.Combine(AppContext.BaseDirectory, "temp_maafw");
            Directory.CreateDirectory(tempPath);
            var tempZip = Path.Combine(tempPath, $"maafw_{latestVersion}.zip");
            SetText(textBlock, "Downloading".ToLocalization());
            if (!await DownloadWithRetry(downloadUrl, tempZip, progress, 3))
            {
                Dismiss(sukiToast);
                ToastHelper.Warn("DownloadFailed");
                return;
            }

            SetText(textBlock, "ApplyingUpdate".ToLocalization());
            // 文件替换（复用ReplaceFilesWithRetry）
            SetProgress(progress, 0);
            var extractDir = Path.Combine(tempPath, $"maafw_{latestVersion}_extracted");
            if (Directory.Exists(extractDir))
                Directory.Delete(extractDir, true);
            ZipFile.ExtractToDirectory(tempZip, extractDir);
            SetProgress(progress, 20);

            var utf8Bytes = Encoding.UTF8.GetBytes(AppContext.BaseDirectory);
            var utf8BaseDirectory = Encoding.UTF8.GetString(utf8Bytes);
            var sourceBytes = Encoding.UTF8.GetBytes(Path.Combine(extractDir, "bin"));
            var sourceDirectory = Encoding.UTF8.GetString(utf8Bytes);
            SetProgress(progress, 100);

            // 清理与重启（复用ApplySecureUpdate）
            await ApplySecureUpdate(sourceDirectory, utf8BaseDirectory);
        }
        finally
        {
            Instances.RootViewModel.SetUpdating(false);
            Dismiss(sukiToast);
        }
    }

    async private static Task ExecuteTasksAsync()
    {
        try
        {
            while (Queue.TryDequeue(out var task))
            {
                await _queueLock.WaitAsync();
                LoggerHelper.Info($"开始执行任务: {task.Name}");
                await task.Action();
                LoggerHelper.Info($"任务完成: {task.Name}");
                _queueLock.Release();
            }
        }
        finally
        {
            Instances.RootViewModel.SetUpdating(false);
        }
    }


    public static void GetLatestVersionAndDownloadUrlFromGithub(out string url, out string latestVersion, string owner = "syoius", string repo = "MFAAvalonia")
    {
        url = string.Empty;
        latestVersion = string.Empty;

        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
            return;

        var releaseUrl = $"https://api.github.com/repos/{owner}/{repo}/releases";
        int page = 1;
        const int perPage = 5;
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.TryParseAdd("request");
        httpClient.DefaultRequestHeaders.Accept.TryParseAdd("application/json");

        while (page < 101)
        {
            var urlWithParams = $"{releaseUrl}?per_page={perPage}&page={page}";
            try
            {
                var response = httpClient.GetAsync(urlWithParams).Result;
                if (response.IsSuccessStatusCode)
                {
                    var read = response.Content.ReadAsStringAsync();
                    read.Wait();
                    string json = read.Result;
                    var tags = JArray.Parse(json);
                    if (tags.Count == 0)
                    {
                        break;
                    }
                    foreach (var tag in tags)
                    {
                        if ((bool)tag["prerelease"])
                        {
                            continue;
                        }
                        latestVersion = tag["tag_name"]?.ToString();
                        if (!string.IsNullOrEmpty(latestVersion))
                        {
                            url = GetDownloadUrlFromGitHubRelease(latestVersion, owner, repo);
                            return;
                        }
                    }
                }
                else if (response.StatusCode == HttpStatusCode.Forbidden && response.ReasonPhrase.Contains("403"))
                {
                    LoggerHelper.Error("GitHub API速率限制已超出，请稍后再试。");
                    throw new Exception("GitHub API速率限制已超出，请稍后再试。");
                }
                else
                {
                    LoggerHelper.Error($"请求GitHub时发生错误: {response.StatusCode} - {response.ReasonPhrase}");
                    throw new Exception($"请求GitHub时发生错误: {response.StatusCode} - {response.ReasonPhrase}");
                }
            }
            catch (Exception e)
            {
                LoggerHelper.Error($"处理GitHub响应时发生错误: {e.Message}");
                throw new Exception($"处理GitHub响应时发生错误: {e.Message}");
            }
            page++;
        }
    }

    private static string GetDownloadUrlFromGitHubRelease(string version, string owner, string repo)
    {
        // 获取当前运行环境信息
        var osPlatform = GetNormalizedOSPlatform();
        var cpuArch = GetNormalizedArchitecture();

        var releaseUrl = $"https://api.github.com/repos/{owner}/{repo}/releases/tags/{version}";
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.TryParseAdd("MFAComponentUpdater/1.0");
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        try
        {
            var response = httpClient.GetAsync(releaseUrl).Result;
            if (response.IsSuccessStatusCode)
            {
                var jsonResponse = response.Content.ReadAsStringAsync().Result;
                var releaseData = JObject.Parse(jsonResponse);

                if (releaseData["assets"] is JArray { Count: > 0 } assets)
                {
                    var orderedAssets = assets
                        .Select(asset => new
                        {
                            Url = asset["browser_download_url"]?.ToString(),
                            Name = asset["name"]?.ToString().ToLower()
                        })
                        .OrderByDescending(a => GetAssetPriority(a.Name, osPlatform, cpuArch))
                        .ToList();

                    return orderedAssets.FirstOrDefault()?.Url ?? string.Empty;
                }
            }
            else if (response.StatusCode == HttpStatusCode.Forbidden && response.ReasonPhrase.Contains("403"))
            {
                LoggerHelper.Error("GitHub API速率限制已超出，请稍后再试。");
                throw new Exception("GitHub API速率限制已超出，请稍后再试。");
            }
            else
            {
                LoggerHelper.Error($"请求GitHub时发生错误: {response.StatusCode} - {response.ReasonPhrase}");
                throw new Exception($"{response.StatusCode} - {response.ReasonPhrase}");
            }
        }
        catch (Exception e)
        {
            LoggerHelper.Error($"处理GitHub响应时发生错误: {e.Message}");
            throw new Exception($"{e.Message}");
        }
        return string.Empty;
    }

// 标准化操作系统标识
    private static string GetNormalizedOSPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return "win";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return "macos";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return "linux";
        return "unknown";
    }

// 标准化硬件架构标识
    private static string GetNormalizedArchitecture()
    {
        var arch = RuntimeInformation.ProcessArchitecture.ToString().ToLower();
        return arch switch
        {
            "x64" => "x86_64", // 统一x64和amd64标识
            "arm64" => "arm64",
            _ => "unknown"
        };
    }

// 资源优先级评分算法
    private static int GetAssetPriority(string fileName, string targetOS, string targetArch)
    {
        if (string.IsNullOrEmpty(fileName)) return 0;

        var patterns = new Dictionary<string, int>
        {
            // 完全匹配最高优先级（如：win-x86_64）
            {
                $"{targetOS}-{targetArch}", 100
            },

            // 次优匹配（如：win-amd64或win-x64）
            {
                $"{targetOS}-(amd64|x64)", 80
            },
            {
                $"{targetOS}", 60
            }, // 仅匹配操作系统
            {
                $"{targetArch}", 40
            } // 仅匹配架构
        };

        foreach (var pattern in patterns)
        {
            if (Regex.IsMatch(fileName, pattern.Key, RegexOptions.IgnoreCase))
                return pattern.Value;
        }
        return 0;
    }

    private static void GetDownloadUrlFromMirror(string version,
        string resId,
        string cdk,
        out string url,
        out string latestVersion,
        string userAgent = "YuanMFA",
        bool isUI = false,
        bool onlyCheck = false)
    {

        if (string.IsNullOrWhiteSpace(resId))
        {
            throw new Exception("CurrentResourcesNotSupportMirror".ToLocalization());
        }
        if (string.IsNullOrWhiteSpace(cdk) && !onlyCheck)
        {
            throw new Exception("MirrorCdkInvalid".ToLocalization());
        }
        var cdkD = onlyCheck ? string.Empty : $"cdk={cdk}&";
        var multiplatform = MaaProcessor.Interface?.Multiplatform == true;
        var os = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win" :
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux" :
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macos" : "unknown";

        var arch = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "x86_64",
            Architecture.Arm64 => "arm64",
            _ => "unknown"
        };

        var multiplatformString = multiplatform ? $"os={os}&arch={arch}&" : "";
        var releaseUrl = isUI
            ? $"https://mirrorchyan.com/api/resources/{resId}/latest?current_version={version}&{cdkD}os={os}&arch={arch}"
            : $"https://mirrorchyan.com/api/resources/{resId}/latest?current_version={version}&{cdkD}{multiplatformString}user_agent={userAgent}";
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.TryParseAdd("request");
        httpClient.DefaultRequestHeaders.Accept.TryParseAdd("application/json");

        try
        {

            var response = httpClient.GetAsync(releaseUrl).Result;
            var jsonResponse = response.Content.ReadAsStringAsync().Result;
            var responseData = JObject.Parse(jsonResponse);
            Exception? exception = null;
            // 处理 HTTP 状态码
            if (!response.IsSuccessStatusCode)
            {
                exception = HandleHttpError(response.StatusCode, responseData);
            }

            // 处理业务错误码
            var responseCode = (int)responseData["code"]!;
            if (responseCode != 0)
            {
                HandleBusinessError(responseCode, responseData);
            }

            // 成功处理
            var data = responseData["data"]!;
            if (!onlyCheck && !isUI && data != null)
            {
                SaveAnnouncement(data, "release_note");
            }

            url = data["url"]?.ToString() ?? string.Empty;
            latestVersion = data["version_name"]?.ToString() ?? string.Empty;
            if (exception != null)
                throw exception;
        }
        catch (AggregateException ex) when (ex.InnerException is HttpRequestException httpEx)
        {
            throw new Exception($"NetworkError: {httpEx.Message}".ToLocalization());
        }
        catch (Exception ex)
        {
            throw new Exception(ex.Message);
        }
    }

    #region 错误处理逻辑

    private static Exception HandleHttpError(HttpStatusCode statusCode, JObject responseData)
    {
        var errorMsg = responseData["msg"]?.ToString() ?? "UnknownError".ToLocalization();

        switch (statusCode)
        {
            case HttpStatusCode.BadRequest: // 400
                return new Exception($"InvalidRequest: {errorMsg}".ToLocalization());

            case HttpStatusCode.Forbidden: // 403
                return new Exception($"AccessDenied: {errorMsg}".ToLocalization());

            case HttpStatusCode.NotFound: // 404
                return new Exception($"ResourceNotFound: {errorMsg}".ToLocalization());

            default:
                return new Exception($"ServerError: [{(int)statusCode}] {errorMsg}".ToLocalization());
        }
    }

    private static void HandleBusinessError(int code, JObject responseData)
    {
        var errorMsg = responseData["msg"]?.ToString() ?? "UndefinedError".ToLocalization();

        switch (code)
        {
            // 参数错误系列 (400)
            case 1001:
                throw new Exception($"InvalidParams: {errorMsg}".ToLocalization());

            // CDK 相关错误 (403)
            case 7001:
                throw new Exception("MirrorCdkExpired".ToLocalization());
            case 7002:
                throw new Exception("MirrorCdkInvalid".ToLocalization());
            case 7003:
                throw new Exception("MirrorUseLimitReached".ToLocalization());
            case 7004:
                throw new Exception("MirrorCdkMismatch".ToLocalization());

            // 资源相关错误 (404)
            case 8001:
                throw new Exception("CurrentResourcesNotSupportMirror".ToLocalization());

            // 参数校验错误 (400)
            case 8002:
                throw new Exception($"InvalidOS: {errorMsg}".ToLocalization());
            case 8003:
                throw new Exception($"InvalidArch: {errorMsg}".ToLocalization());
            case 8004:
                throw new Exception($"InvalidChannel: {errorMsg}".ToLocalization());

            // 未分类错误
            case 1:
                throw new Exception($"BusinessError: {errorMsg}".ToLocalization());

            default:
                throw new Exception($"UnknownErrorCode: [{code}] {errorMsg}".ToLocalization());
        }
    }

    #endregion

    private static string GetLocalVersion()
    {
        return Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "DEBUG";
    }

    private static string GetResourceVersion()
    {
        return Instances.VersionUpdateSettingsUserControlModel.ResourceVersion;
    }

    private static string GetResourceID()
    {
        return MaaProcessor.Interface?.RID ?? string.Empty;
    }

    private static bool IsNewVersionAvailable(string latestVersion, string localVersion)
    {
        try
        {
            var normalizedLatest = ParseAndNormalizeVersion(latestVersion);
            var normalizedLocal = ParseAndNormalizeVersion(localVersion);
            return normalizedLatest.ComparePrecedenceTo(normalizedLocal) > 0;
        }
        catch (Exception ex)
        {
            LoggerHelper.Error(ex);
            return false;
        }
    }

    private static SemVersion ParseAndNormalizeVersion(string version)
    {
        // 移除v前缀和前后空格
        var sanitized = version.Trim().TrimStart('v', 'V');

        // 分离构建元数据和预发布标签
        var buildSeparatorIndex = sanitized.IndexOf('+');
        var prereleaseSeparatorIndex = sanitized.IndexOf('-');

        var versionPart = sanitized;
        var suffix = "";

        // 提取基础版本部分
        if (prereleaseSeparatorIndex > 0 || buildSeparatorIndex > 0)
        {
            var firstSpecialIndex = new[]
                {
                    prereleaseSeparatorIndex,
                    buildSeparatorIndex
                }
                .Where(i => i > 0)
                .DefaultIfEmpty(-1)
                .Min();

            if (firstSpecialIndex > 0)
            {
                versionPart = sanitized.Substring(0, firstSpecialIndex);
                suffix = sanitized.Substring(firstSpecialIndex);
            }
        }

        // 分割版本号组件
        var versionComponents = versionPart.Split('.')
            .Select(s => int.TryParse(s, out var num) ? num : 0)
            .ToList();

        // 标准化为三位版本号
        while (versionComponents.Count < 3)
        {
            versionComponents.Add(0);
        }

        if (versionComponents.Count > 3)
        {
            versionComponents = versionComponents.Take(3).ToList();
        }

        // 重组标准化版本字符串
        var normalized = $"{versionComponents[0]}.{versionComponents[1]}.{versionComponents[2]}{suffix}";

        return SemVersion.Parse(normalized, SemVersionStyles.AllowV);
    }

    async private static Task<bool> DownloadFileAsync(string url, string filePath, ProgressBar? progressBar, string key)
    {
        try
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("request");
            httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/json");

            using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var startTime = DateTime.Now;
            long totalBytesRead = 0;
            long bytesPerSecond = 0;
            long? totalBytes = response.Content.Headers.ContentLength;

            await using var contentStream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write);

            var buffer = new byte[8192];
            var stopwatch = Stopwatch.StartNew();
            var lastSpeedUpdateTime = startTime;
            long lastTotalBytesRead = 0;

            while (true)
            {
                var bytesRead = await contentStream.ReadAsync(buffer);
                if (bytesRead == 0) break;

                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));

                totalBytesRead += bytesRead;
                var currentTime = DateTime.Now;


                var timeSinceLastUpdate = currentTime - lastSpeedUpdateTime;
                if (timeSinceLastUpdate.TotalSeconds >= 1)
                {
                    bytesPerSecond = (long)((totalBytesRead - lastTotalBytesRead) / timeSinceLastUpdate.TotalSeconds);
                    lastTotalBytesRead = totalBytesRead;
                    lastSpeedUpdateTime = currentTime;
                }


                double progressPercentage;
                if (totalBytes.HasValue && totalBytes.Value > 0)
                {
                    progressPercentage = Math.Min((double)totalBytesRead / totalBytes.Value * 100, 100);
                }
                else
                {
                    if (bytesPerSecond > 0)
                    {
                        double estimatedTotal = totalBytesRead + bytesPerSecond * 5;
                        progressPercentage = Math.Min((double)totalBytesRead / estimatedTotal * 100, 99);
                    }
                    else
                    {
                        progressPercentage = Math.Min((currentTime - startTime).TotalSeconds / 30 * 100, 99);
                    }
                }

                SetProgress(progressBar, progressPercentage);
                if (stopwatch.ElapsedMilliseconds >= 100)
                {
                    // DispatcherHelper.RunOnMainThread(() =>
                    //     Instances.TaskQueueViewModel.OutputDownloadProgress(
                    //         totalBytesRead,
                    //         totalBytes ?? 0,
                    //         (int)bytesPerSecond,
                    //         (currentTime - startTime).TotalSeconds));
                    stopwatch.Restart();
                }
            }

            SetProgress(progressBar, 100);
            DispatcherHelper.RunOnMainThread(() =>
                Instances.TaskQueueViewModel.OutputDownloadProgress(
                    totalBytesRead,
                    totalBytes ?? totalBytesRead,
                    (int)bytesPerSecond,
                    (DateTime.Now - startTime).TotalSeconds
                ));

            return true;
        }
        catch (HttpRequestException httpEx)
        {
            LoggerHelper.Error($"HTTP请求失败: {httpEx.Message}");
            return false;
        }
        catch (IOException ioEx)
        {
            LoggerHelper.Error($"文件操作失败: {ioEx.Message}");
            return false;
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"未知错误: {ex.Message}");
            return false;
        }
    }

    public class MirrorChangesJson
    {
        [JsonProperty("modified")] public List<string>? Modified;
        [JsonProperty("deleted")] public List<string>? Deleted;
        [JsonProperty("added")] public List<string>? Added;
        [JsonExtensionData]
        public Dictionary<string, object>? AdditionalData { get; set; } = new();
    }

    private static string[] GetRepoFromUrl(string githubUrl)
    {
        var pattern = @"^https://github\.com/(?<owner>[^/]+)/(?<repo>[^/]+)$";
        var match = Regex.Match(githubUrl, pattern);

        if (match.Success)
        {
            string owner = match.Groups["owner"].Value;
            string repo = match.Groups["repo"].Value;

            return
            [
                owner,
                repo
            ];
        }

        throw new FormatException("输入的 GitHub URL 格式不正确: " + githubUrl);
    }

    private static string CDK()
    {
        return Instances.VersionUpdateSettingsUserControlModel.CdkPassword;
    }
    private static void SetText(TextBlock? block, string text)
    {
        if (block == null)
            return;
        DispatcherHelper.RunOnMainThread(() => block.Text = text);
    }
    private static void SetProgress(ProgressBar? bar, double percentage)
    {
        if (bar == null)
            return;
        DispatcherHelper.RunOnMainThread(() => bar.Value = percentage);
    }
    private static void Dismiss(ISukiToast? toast)
    {
        if (toast == null)
            return;
        DispatcherHelper.RunOnMainThread(() => Instances.ToastManager.Dismiss(toast));
    }

    private static void CopyFolder(string sourceFolder, string destinationFolder)
    {
        if (!Directory.Exists(destinationFolder))
        {
            Directory.CreateDirectory(destinationFolder);
        }
        var files = Directory.GetFiles(sourceFolder);
        foreach (string file in files)
        {
            string fileName = Path.GetFileName(file);
            string destinationFile = Path.Combine(destinationFolder, fileName);
            File.Copy(file, destinationFile, true);
        }
        var subDirectories = Directory.GetDirectories(sourceFolder);
        foreach (string subDirectory in subDirectories)
        {
            string subDirectoryName = Path.GetFileName(subDirectory);
            string destinationSubDirectory = Path.Combine(destinationFolder, subDirectoryName);
            CopyFolder(subDirectory, destinationSubDirectory);
        }
    }
// 修改 DirectoryMerge 方法中的文件复制逻辑
    private static void DirectoryMerge(string sourceDirName, string destDirName)
    {
        DirectoryInfo dir = new DirectoryInfo(sourceDirName);
        DirectoryInfo[] dirs = dir.GetDirectories();

        if (!dir.Exists)
        {
            throw new DirectoryNotFoundException("Source directory does not exist or could not be found: " + sourceDirName);
        }

        if (!Directory.Exists(destDirName))
        {
            Directory.CreateDirectory(destDirName);
        }
        foreach (FileInfo file in dir.GetFiles())
        {
            string tempPath = Path.Combine(destDirName, file.Name);
            try
            {
                file.CopyTo(tempPath, true);
            }
            catch (IOException)
            {
            }
        }
        foreach (DirectoryInfo subDir in dirs)
        {
            string tempPath = Path.Combine(destDirName, subDir.Name);
            DirectoryMerge(subDir.FullName, tempPath);
        }
    }
    private static void SaveAnnouncement(JToken? releaseData, string from)
    {
        try
        {
            var bodyContent = releaseData?[from]?.ToString();
            if (!string.IsNullOrWhiteSpace(bodyContent) && bodyContent != "placeholder")
            {
                var resourceDirectory = Path.Combine(AppContext.BaseDirectory, "resource");
                Directory.CreateDirectory(resourceDirectory);
                var filePath = Path.Combine(resourceDirectory, AnnouncementViewModel.AnnouncementFileName);
                File.WriteAllText(filePath, bodyContent);
                LoggerHelper.Info($"{AnnouncementViewModel.AnnouncementFileName} saved successfully.");
                GlobalConfiguration.SetValue(ConfigurationKeys.DoNotShowAgain, bool.FalseString);
            }
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"Error saving {AnnouncementViewModel.AnnouncementFileName}: {ex.Message}");
        }
    }
}
