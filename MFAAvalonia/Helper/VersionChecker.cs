using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using MFAAvalonia.Configuration;
using MFAAvalonia.Extensions;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper.Converters;
using MFAAvalonia.ViewModels.UsersControls.Settings;
using MFAAvalonia.ViewModels.Windows;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Semver;
using SukiUI.Dialogs;
using SukiUI.Enums;
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
using System.Net.Security;
using System.Net.Sockets;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Authentication;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MFAAvalonia.Helper;
#pragma warning  disable CS1998 // 此异步方法缺少 "await" 运算符，将以同步方式运行。
#pragma warning  disable CS4014 // 由于此调用不会等待，因此在此调用完成之前将会继续执行当前方法。
public static class VersionChecker
{
    public enum VersionType
    {
        Alpha,
        Beta,
        Stable
    }

    private static readonly ConcurrentQueue<ValueType.MFATask> Queue = new();
    public static void Check()
    {
        var config = new
        {
            AutoUpdateResource = ConfigurationManager.Current.GetValue(ConfigurationKeys.EnableAutoUpdateResource, false),
            AutoUpdateMFA = ConfigurationManager.Current.GetValue(ConfigurationKeys.EnableAutoUpdateMFA, false),
            CheckVersion = ConfigurationManager.Current.GetValue(ConfigurationKeys.EnableCheckVersion, true),
        };

        if (config.AutoUpdateResource && !GetResourceVersion().Contains("debug", StringComparison.OrdinalIgnoreCase))
        {
            AddResourceUpdateTask(config.AutoUpdateMFA);
        }
        else if (config.CheckVersion && !GetResourceVersion().Contains("debug", StringComparison.OrdinalIgnoreCase))
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

    public static void CheckMinVersion()
    {
        if (IsNewVersionAvailable(GetMinVersion(), GetLocalVersion()))
        {
            Instances.DialogManager.CreateDialog().OfType(NotificationType.Warning).WithContent("UiVersionBelowResourceRequirement".ToLocalizationFormatted(false, GetLocalVersion(), GetMinVersion()))
                .WithActionButton("Ok".ToLocalization(), dialog => { }, true).TryShow();
        }
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
                GetLatestVersionAndDownloadUrlFromGithub(out var downloadUrl, out latestVersion, strings[0], strings[1], true, currentVersion: resourceVersion);
            else
                GetDownloadUrlFromMirror(resourceVersion, GetResourceID(), CDK(), out _, out latestVersion, onlyCheck: true, currentVersion: resourceVersion);

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
                        .WithActionButton("Later".ToLocalization(), _ => { }, true, SukiButtonStyles.Basic)
                        .WithActionButton("Update".ToLocalization(), _ => UpdateResourceAsync(), true).Queue();
                });
                DispatcherHelper.RunOnMainThread(ChangelogViewModel.CheckReleaseNote);
            }
            else
            {
                DispatcherHelper.RunOnMainThread(ChangelogViewModel.CheckChangelog);
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

            if (IsNewVersionAvailable(latestVersion, GetMaxVersion()))
                latestVersion = GetMaxVersion();
            if (IsNewVersionAvailable(latestVersion, localVersion))
            {
                DispatcherHelper.RunOnMainThread(() =>
                {
                    Instances.ToastManager.CreateToast().WithTitle("SoftwareUpdate".ToLocalization())
                        .WithContent("MFA" + "NewVersionAvailableLatestVersion".ToLocalization() + latestVersion).Dismiss().After(TimeSpan.FromSeconds(6))
                        .WithActionButton("Later".ToLocalization(), _ => { }, true, SukiButtonStyles.Basic)
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
            textBlock = new TextBlock
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
                GetLatestVersionAndDownloadUrlFromGithub(out downloadUrl, out latestVersion, strings[0], strings[1], currentVersion: localVersion);
            else
                GetDownloadUrlFromMirror(localVersion, GetResourceID(), CDK(), out downloadUrl, out latestVersion, currentVersion: localVersion);

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
        string fileExtension = GetFileExtensionFromUrl(downloadUrl);
        if (string.IsNullOrEmpty(fileExtension))
        {
            fileExtension = ".zip";
        }
        var tempZipFilePath = Path.Combine(tempPath, $"resource_{latestVersion}{fileExtension}");

        SetText(textBlock, "Downloading".ToLocalization());
        SetProgress(progress, 0);
        (var downloadStatus, tempZipFilePath) = await DownloadFileAsync(downloadUrl, tempZipFilePath, progress);
        LoggerHelper.Info(tempZipFilePath);
        if (!downloadStatus)
        {
            Dismiss(sukiToast);
            ToastHelper.Warn("DownloadFailed".ToLocalization());
            Instances.RootViewModel.SetUpdating(false);
            return;
        }

        SetText(textBlock, "Extracting".ToLocalization());
        SetProgress(progress, 0);

        var tempExtractDir = Path.Combine(tempPath, $"resource_{latestVersion}_extracted");
        if (Directory.Exists(tempExtractDir)) Directory.Delete(tempExtractDir, true);
        if (!File.Exists(tempZipFilePath))
        {
            Dismiss(sukiToast);
            ToastHelper.Warn("DownloadFailed".ToLocalization());
            Instances.RootViewModel.SetUpdating(false);
            return;
        }

        await UniversalExtractor.ExtractAsync(tempZipFilePath, tempExtractDir, progress);
        SetText(textBlock, "ApplyingUpdate".ToLocalization());
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
                    if (fileName.Equals(ChangelogViewModel.ChangelogFileName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    try
                    {
                        File.SetAttributes(rfile, FileAttributes.Normal);
                        LoggerHelper.Info("Deleting file: " + rfile);
                        File.Delete(rfile);
                    }
                    catch (Exception ex)
                    {
                        LoggerHelper.Error($"文件删除失败: {rfile}", ex);
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
                            try
                            {
                                if (!Path.GetFileName(delPath).Contains("MFAUpdater")
                                    && !Path.GetFileName(delPath).Contains("MFAAvalonia")
                                    && !Path.GetFileName(delPath).Contains(Process.GetCurrentProcess().MainModule?.ModuleName ?? string.Empty))
                                {
                                    LoggerHelper.Info("Deleting file: " + delPath);
                                    File.Delete(delPath);
                                }
                            }
                            catch (Exception e)
                            {
                                LoggerHelper.Error("Failed to delete the file: " + e);
                            }
                        }
                    }
                    if (changesJson?.Modified != null)
                    {
                        var delPaths = changesJson.Modified
                            .Select(del => Path.Combine(AppContext.BaseDirectory, del))
                            .Where(File.Exists);

                        foreach (var delPath in delPaths)
                        {
                            try
                            {
                                if (!Path.GetFileName(delPath).Contains("MFAUpdater")
                                    && !Path.GetFileName(delPath).Contains("MFAAvalonia")
                                    && !Path.GetFileName(delPath).Contains(Process.GetCurrentProcess().MainModule?.ModuleName ?? string.Empty))
                                {
                                    LoggerHelper.Info("Deleting file: " + delPath);
                                    File.Delete(delPath);
                                }
                            }
                            catch (Exception e)
                            {
                                LoggerHelper.Error("Failed to delete the file: " + e);
                            }

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

        var di = new DirectoryInfo(originPath);
        if (di.Exists)
        {
            DirectoryMerge(originPath, wpfDir, false, true);
        }

        SetProgress(progress, 70);

        // 检查是否存在config文件夹，如果存在，安排程序关闭后更新
        var sourceConfigDir = Path.Combine(tempExtractDir, "config");
        if (Directory.Exists(sourceConfigDir))
        {
            var configBackupDir = Path.Combine(AppContext.BaseDirectory, "backup_config");
            Directory.CreateDirectory(configBackupDir);
            
            // 将配置文件复制到备份目录
            DirectoryMerge(sourceConfigDir, configBackupDir);
            
            // 创建更新配置的脚本
            var updaterScriptPath = Path.Combine(AppContext.BaseDirectory, RuntimeInformation.IsOSPlatform(OSPlatform.Windows) 
                ? "update_config.bat" 
                : "update_config.sh");
                
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    // Windows批处理脚本
                    string batchContent = $@"@echo off
timeout /t 1 /nobreak > nul
echo 正在更新配置文件...
xcopy ""{configBackupDir}\*.*"" ""{Path.Combine(AppContext.BaseDirectory, "config")}"" /E /Y /I
echo 配置更新完成!
rmdir /S /Q ""{configBackupDir}""
del ""%~f0""
";
                    File.WriteAllText(updaterScriptPath, batchContent);
                }
                else
                {
                    // Unix Shell脚本
                    string shContent = $@"#!/bin/bash
sleep 1
echo ""正在更新配置文件...""
mkdir -p ""{Path.Combine(AppContext.BaseDirectory, "config")}""
cp -rf ""{configBackupDir}""/* ""{Path.Combine(AppContext.BaseDirectory, "config")}""/ 
echo ""配置更新完成!""
rm -rf ""{configBackupDir}""
rm $0
";
                    File.WriteAllText(updaterScriptPath, shContent);
                    // 设置执行权限
                    var chmodProcess = Process.Start("/bin/chmod", $"+x {updaterScriptPath}");
                    chmodProcess?.WaitForExitAsync();
                }
                
                // 通知用户需要重启应用以完成配置更新
                SetText(textBlock, "配置文件将在应用重启后更新");
                LoggerHelper.Info("已安排配置文件在程序重启后更新");
                
                // 更新完成后在对话框中显示重启按钮
                DispatcherHelper.RunOnMainThread(() =>
                {
                    if (!noDialog)
                    {
                        Instances.DialogManager.CreateDialog().WithContent("GameResourceUpdated".ToLocalization() + 
                        "\n配置文件将在重启后更新").WithActionButton("Yes".ToLocalization(), _ =>
                        {
                            // 在程序退出前启动配置更新脚本
                            try
                            {
                                var psi = new ProcessStartInfo
                                {
                                    FileName = updaterScriptPath,
                                    UseShellExecute = true,
                                    CreateNoWindow = false,
                                    WindowStyle = ProcessWindowStyle.Hidden
                                };
                                Process.Start(psi);
                                LoggerHelper.Info("已启动配置文件更新器");
                            }
                            catch (Exception ex)
                            {
                                LoggerHelper.Error($"启动配置文件更新器失败: {ex.Message}");
                            }
                            
                            Process.Start(Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty);
                            Instances.ShutdownApplication();
                            Instances.ApplicationLifetime.Shutdown();
                        }, dismissOnClick: true, "Flat", "Accent")
                        .WithActionButton("No".ToLocalization(), _ =>
                        {
                        }, dismissOnClick: true).TryShow();
                    }
                });
            }
            catch (Exception ex)
            {
                LoggerHelper.Error($"准备配置更新器失败: {ex.Message}");
            }
        }

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
            settings.Converters.Add(new MaaInterfaceSelectAdvancedConverter(true));

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
                    GetDownloadUrlFromMirror(GetLocalVersion(), "YuanMFA", CDK(), out downloadUrl, out latestVersion, isUI: true);
            }
            catch (Exception ex)
            {
                Dismiss(sukiToast);
                ToastHelper.Warn($"{"FailToGetLatestVersionInfo".ToLocalization()}", ex.Message);
                LoggerHelper.Error(ex);
                Instances.RootViewModel.SetUpdating(false);
                return;
            }

            // 版本验证
            SetProgress(progress, 50);
            if (IsNewVersionAvailable(latestVersion, GetMaxVersion()))
            {
                latestVersion = GetMaxVersion();
                if (isGithub)
                    GetLatestVersionAndDownloadUrlFromGithub(out downloadUrl, out _, targetVersion: latestVersion);
            }

            if (!IsNewVersionAvailable(latestVersion, GetLocalVersion()))
            {
                Dismiss(sukiToast);
                ToastHelper.Info("MFAIsLatestVersion".ToLocalization());
                Instances.RootViewModel.SetUpdating(false);
                return;
            }

            // 准备临时目录
            var tempPath = Path.Combine(AppContext.BaseDirectory, "temp_mfa");
            Directory.CreateDirectory(tempPath);

            // 下载更新包
            SetText(textBlock, "Downloading".ToLocalization());
            SetProgress(progress, 0);
            var tempZip = Path.Combine(tempPath, $"mfa_{latestVersion}.zip");
            (var downloadStatus, tempZip) = await DownloadWithRetry(downloadUrl, tempZip, progress, 3);
            if (!downloadStatus)
            {
                Dismiss(sukiToast);
                ToastHelper.Warn("DownloadFailed".ToLocalization());
                Instances.RootViewModel.SetUpdating(false);
                return;
            }

            // 解压文件
            SetProgress(progress, 20);
            var extractDir = Path.Combine(tempPath, $"mfa_{latestVersion}_extracted");
            if (Directory.Exists(extractDir))
                Directory.Delete(extractDir, true);
            SetText(textBlock, "Extracting".ToLocalization());
            await UniversalExtractor.ExtractAsync(tempZip, extractDir, progress);

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
            bool update = true;
            try
            {
                if (File.Exists(targetUpdaterPath) && File.Exists(sourceUpdaterPath))
                {

                    var targetVersionInfo = FileVersionInfo.GetVersionInfo(targetUpdaterPath);
                    var sourceVersionInfo = FileVersionInfo.GetVersionInfo(sourceUpdaterPath);
                    var targetVersion = targetVersionInfo.FileVersion; // 或 ProductVersion
                    var sourceVersion = sourceVersionInfo.FileVersion;

                    // 使用Version类比较版本
                    var vTarget = new Version(targetVersion);
                    var vSource = new Version(sourceVersion);

                    int result = vTarget.CompareTo(vSource);
                    if (result < 0)
                    {
                        if (File.Exists(sourceUpdaterPath) && !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                        {
                            var chmodProcess = Process.Start("/bin/chmod", $"+x {sourceDirectory}");
                            await chmodProcess?.WaitForExitAsync();
                        }
                    }

                }

                // 验证源文件存在性
                if (!File.Exists(sourceUpdaterPath))
                {
                    LoggerHelper.Error($"更新器在源目录缺失: {sourceUpdaterPath}");
                    update = false;
                }
            }
            catch (IOException ex)
            {
                update = false;
                LoggerHelper.Error($"文件操作失败: {ex.Message} (错误代码: {ex.HResult})");
                throw new InvalidOperationException("文件复制过程中发生I/O错误", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                update = false;
                LoggerHelper.Error($"权限不足: {ex.Message}");
                throw new SecurityException("文件访问权限被拒绝", ex);
            }
            catch (Exception ex)
            {
                update = true;
                LoggerHelper.Error($"操作失败: {ex.Message} (具体: {ex})");
            }
            if (update)
            {
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
                        LoggerHelper.Error(e);
                    }
                }
                LoggerHelper.Info($"成功复制更新器到目标目录: {targetUpdaterPath}");
            }
            SetProgress(progress, 100);

            await ApplySecureUpdate(sourceDirectory, utf8BaseDirectory, $"{Assembly.GetEntryAssembly().GetName().Name}{(RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : "")}",
                Process.GetCurrentProcess().MainModule.ModuleName);

            Thread.Sleep(500);
        }
        finally
        {
            Instances.RootViewModel.SetUpdating(false);
            Dismiss(sukiToast);
        }
    }

    #region 增强型更新核心方法

    async private static Task<(bool, string)> DownloadWithRetry(string url, string savePath, ProgressBar? progress, int retries)
    {
        for (int i = 0; i < retries; i++)
        {
            try
            {
                return await DownloadFileAsync(url, savePath, progress);
            }
            catch (WebException ex) when (i < retries - 1)
            {
                LoggerHelper.Warning($"下载重试 ({i + 1}/{retries}): {ex.Status}");
                await Task.Delay(2000 * (i + 1));
            }
        }
        return (false, savePath);
    }
    private static string BuildArguments(string source, string target, string oldName, string newName)
    {
        var args = new List<string>
        {
            EscapeArgument(source),
            EscapeArgument(target)
        };

        if (!string.IsNullOrWhiteSpace(oldName))
            args.Add(EscapeArgument(oldName));

        if (!string.IsNullOrWhiteSpace(newName))
            args.Add(EscapeArgument(newName));

        return string.Join(" ", args);
    }

// 处理含空格的参数
    private static string EscapeArgument(string arg) => $"\"{arg.Replace("\"", "\\\"")}\"";

    async private static Task ApplySecureUpdate(string source, string target, string oldName = "", string newName = "")
    {
        source = Path.GetFullPath(source).Replace('\\', Path.DirectorySeparatorChar);
        target = Path.GetFullPath(target).Replace('\\', Path.DirectorySeparatorChar);

        target = target.TrimEnd('\\', '/');
        source = source.TrimEnd('\\', '/');

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
            WorkingDirectory = AppContext.BaseDirectory,
            FileName = updaterName,
            Arguments = BuildArguments(source, target, oldName, newName),
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };


        LoggerHelper.Info($"{Path.Combine(AppContext.BaseDirectory, updaterName)} {BuildArguments(source, target, oldName, newName)}");

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
            try
            {
                GetDownloadUrlFromMirror(currentVersion, resId, CDK(), out downloadUrl, out latestVersion, "MFA", true);
            }
            catch (Exception ex)
            {
                Dismiss(sukiToast);
                ToastHelper.Warn($"{"FailToGetLatestVersionInfo".ToLocalization()}", ex.Message);
                LoggerHelper.Error(ex);
                Instances.RootViewModel.SetUpdating(false);
                return;
            }
            // 版本校验（保持原有逻辑）
            SetProgress(progress, 50);
            if (!IsNewVersionAvailable(latestVersion, currentVersion))
            {
                Dismiss(sukiToast);
                ToastHelper.Info("MaaFwIsLatestVersion".ToLocalization());
                Instances.RootViewModel.SetUpdating(false);
                return;
            }

            // 下载与解压（优化为使用DownloadWithRetry）
            var tempPath = Path.Combine(AppContext.BaseDirectory, "temp_maafw");
            Directory.CreateDirectory(tempPath);
            var tempZip = Path.Combine(tempPath, $"maafw_{latestVersion}.zip");
            SetText(textBlock, "Downloading".ToLocalization());
            (var downloadStatus, tempZip) = await DownloadWithRetry(downloadUrl, tempZip, progress, 3);
            if (!downloadStatus)
            {
                Dismiss(sukiToast);
                ToastHelper.Warn("DownloadFailed".ToLocalization());
                Instances.RootViewModel.SetUpdating(false);
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
            var sourceDirectory = Encoding.UTF8.GetString(sourceBytes);
            SetProgress(progress, 100);

            // 清理与重启（复用ApplySecureUpdate）
            await ApplySecureUpdate(sourceDirectory, utf8BaseDirectory, Process.GetCurrentProcess().MainModule.ModuleName);
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


    public static void GetLatestVersionAndDownloadUrlFromGithub(out string url,
        out string latestVersion,
        string owner = "syoius",
        string repo = "MFAAvalonia",
        bool onlyCheck = false,
        string targetVersion = "",
        string currentVersion = "v0.0.0")
    {
        var versionType = repo.Equals("MFAAvalonia", StringComparison.OrdinalIgnoreCase)
            ? Instances.VersionUpdateSettingsUserControlModel.UIUpdateChannelIndex.ToVersionType()
            : Instances.VersionUpdateSettingsUserControlModel.ResourceUpdateChannelIndex.ToVersionType();
        url = string.Empty;
        latestVersion = string.Empty;

        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
            return;

        var releaseUrl = $"https://api.github.com/repos/{owner}/{repo}/releases";
        int page = 1;
        const int perPage = 5;
        using var httpClient = CreateHttpClientWithProxy();

        if (!string.IsNullOrWhiteSpace(Instances.VersionUpdateSettingsUserControlModel.GitHubToken))
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                Instances.VersionUpdateSettingsUserControlModel.GitHubToken);
        }

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
                        if ((bool)tag["prerelease"] && versionType == VersionType.Stable)
                        {
                            continue;
                        }
                        var isAlpha = latestVersion.Contains("alpha", StringComparison.OrdinalIgnoreCase);
                        var isBeta = latestVersion.Contains("beta", StringComparison.OrdinalIgnoreCase);

                        if (isAlpha && versionType != VersionType.Alpha || isBeta && versionType != VersionType.Beta && versionType != VersionType.Alpha)
                        {
                            continue;
                        }
                        latestVersion = tag["tag_name"]?.ToString() ?? string.Empty;
                        if (!string.IsNullOrEmpty(targetVersion) && latestVersion.Trim().Equals(targetVersion.Trim(), StringComparison.OrdinalIgnoreCase))
                        {
                            if (IsNewVersionAvailable(latestVersion, currentVersion))
                            {
                                if (onlyCheck && repo != "MFAAvalonia")
                                    SaveRelease(tag, "body");
                                if (!onlyCheck && repo != "MFAAvalonia")
                                    SaveChangelog(tag, "body");
                            }
                            url = GetDownloadUrlFromGitHubRelease(latestVersion, owner, repo);
                            return;
                        }
                        if (string.IsNullOrEmpty(targetVersion) && !string.IsNullOrEmpty(latestVersion))
                        {
                            if (IsNewVersionAvailable(latestVersion, currentVersion))
                            {
                                if (onlyCheck && repo != "MFAAvalonia")
                                    SaveRelease(tag, "body");
                                if (!onlyCheck && repo != "MFAAvalonia")
                                    SaveChangelog(tag, "body");
                            }
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
        using var httpClient = CreateHttpClientWithProxy();
        httpClient.DefaultRequestHeaders.UserAgent.TryParseAdd("MFAComponentUpdater/1.0");
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        if (!string.IsNullOrWhiteSpace(Instances.VersionUpdateSettingsUserControlModel.GitHubToken))
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                Instances.VersionUpdateSettingsUserControlModel.GitHubToken);
        }

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
        bool onlyCheck = false,
        string currentVersion = "v0.0.0"
    )
    {
        var versionType = isUI ? Instances.VersionUpdateSettingsUserControlModel.UIUpdateChannelIndex.ToVersionType() : Instances.VersionUpdateSettingsUserControlModel.ResourceUpdateChannelIndex.ToVersionType();
        if (string.IsNullOrWhiteSpace(resId))
        {
            throw new Exception("CurrentResourcesNotSupportMirror".ToLocalization());
        }
        if (string.IsNullOrWhiteSpace(cdk) && !onlyCheck)
        {
            throw new Exception("MirrorCdkEmpty".ToLocalization());
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
        var channel = versionType.GetName();
        var multiplatformString = multiplatform ? $"os={os}&arch={arch}&" : "";
        var releaseUrl = isUI
            ? $"https://mirrorchyan.com/api/resources/{resId}/latest?channel={channel}&current_version={version}&{cdkD}os={os}&arch={arch}"
            : $"https://mirrorchyan.com/api/resources/{resId}/latest?channel={channel}&current_version={version}&{cdkD}{multiplatformString}user_agent={userAgent}";
        using var httpClient = CreateHttpClientWithProxy();
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


            url = data["url"]?.ToString() ?? string.Empty;
            latestVersion = data["version_name"]?.ToString() ?? string.Empty;

            if (IsNewVersionAvailable(latestVersion, currentVersion))
            {
                if (onlyCheck && !isUI && data != null)
                {
                    SaveRelease(data, "release_note");
                }
                if (!onlyCheck && !isUI && data != null)
                {
                    SaveChangelog(data, "release_note");
                }
            }
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
        return RootViewModel.Version;
    }

    private static string GetResourceVersion()
    {
        return Instances.VersionUpdateSettingsUserControlModel.ResourceVersion;
    }

    private static string GetResourceID()
    {
        return MaaProcessor.Interface?.RID ?? string.Empty;
    }

    private static string GetMaxVersion()
    {
        return MaaProcessor.Interface?.MFAMaxVersion ?? string.Empty;
    }
    private static string GetMinVersion()
    {
        return MaaProcessor.Interface?.MFAMinVersion ?? string.Empty;
    }

    private static bool IsNewVersionAvailable(string latestVersion, string localVersion)
    {
        if (string.IsNullOrEmpty(latestVersion) || string.IsNullOrEmpty(localVersion))
            return false;
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
        if (!version.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            version = $"v{version}";
        var pattern = @"^[vV]?(?<major>\d+)(\.(?<minor>\d+))?(\.(?<patch>\d+))?(?:-(?<prerelease>[0-9a-zA-Z\-\.]+))?(?:\+(?<build>[0-9a-zA-Z\-\.]+))?$";
        var match = Regex.Match(version.Trim(), pattern);

        var major = match.Groups["major"].Success ? int.Parse(match.Groups["major"].Value) : 0;
        var minor = match.Groups["minor"].Success ? int.Parse(match.Groups["minor"].Value) : 0;
        var patch = match.Groups["patch"].Success ? int.Parse(match.Groups["patch"].Value) : 0;
        var prerelease = match.Groups["prerelease"].Success
            ? match.Groups["prerelease"].Value.Split('.')
            : null;

        var build = match.Groups["build"].Success
            ? match.Groups["build"].Value.Split('.')
            : null;

        return new SemVersion(new BigInteger(major), new BigInteger(minor), new BigInteger(patch), prerelease, build);
    }

    async private static Task<(bool, string)> DownloadFileAsync(string url, string filePath, ProgressBar? progressBar)
    {
        try
        {
            using var httpClient = CreateHttpClientWithProxy();
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("request");
            httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/json");

            using var headResponse = await httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Head, url));
            headResponse.EnsureSuccessStatusCode();

            string? suggestedFileName = null;
            if (headResponse.Content.Headers.ContentDisposition != null)
            {
                suggestedFileName = ParseFileNameFromContentDisposition(
                    headResponse.Content.Headers.ContentDisposition.ToString());
            }

            if (!string.IsNullOrEmpty(suggestedFileName))
            {
                string dir = Path.GetDirectoryName(filePath)!;
                string newFileName = Path.GetFileNameWithoutExtension(filePath) + Path.GetExtension(suggestedFileName);
                filePath = Path.Combine(dir, newFileName);
            }

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

            return (true, filePath);
        }
        catch (HttpRequestException httpEx)
        {
            LoggerHelper.Error($"HTTP请求失败: {httpEx.Message}");
            return (false, filePath);
        }
        catch (IOException ioEx)
        {
            LoggerHelper.Error($"文件操作失败: {ioEx.Message}");
            return (false, filePath);
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"未知错误: {ex.Message}");
            return (false, filePath);
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
    private static void DirectoryMerge(string sourceDirName, string destDirName, bool overwriteMFA = true, bool saveAnnouncement = false)
    {
        var dir = new DirectoryInfo(sourceDirName);
        var dirs = dir.GetDirectories();

        if (!dir.Exists)
        {
            throw new DirectoryNotFoundException("Source directory does not exist or could not be found: " + sourceDirName);
        }

        if (!Directory.Exists(destDirName))
        {
            Directory.CreateDirectory(destDirName);
        }

        foreach (var file in dir.GetFiles())
        {
            var tempPath = Path.Combine(destDirName, file.Name);
            try
            {
                if (overwriteMFA
                    || !Path.GetFileName(tempPath).Contains("MFAUpdater") && !Path.GetFileName(tempPath).Contains("MFAAvalonia") && !Path.GetFileName(tempPath).Contains(Process.GetCurrentProcess().MainModule?.ModuleName ?? string.Empty))
                {
                    if (saveAnnouncement && tempPath.Contains(AnnouncementViewModel.AnnouncementFolder))
                    {
                        GlobalConfiguration.SetValue(ConfigurationKeys.DoNotShowAnnouncementAgain, bool.FalseString);
                    }
                    LoggerHelper.Info("Copying file: " + tempPath);
                    file.CopyTo(tempPath, true);
                }
            }
            catch (IOException exception)
            {
                LoggerHelper.Error(exception);
            }
            catch (Exception exception)
            {
                LoggerHelper.Error(exception);
            }
        }
        foreach (var subDir in dirs)
        {
            string tempPath = Path.Combine(destDirName, subDir.Name);
            DirectoryMerge(subDir.FullName, tempPath);
        }
    }
    private static void SaveRelease(JToken? releaseData, string from)
    {
        try
        {
            var bodyContent = releaseData?[from]?.ToString();
            if (!string.IsNullOrWhiteSpace(bodyContent) && bodyContent != "placeholder")
            {
                var resourceDirectory = Path.Combine(AppContext.BaseDirectory, "resource");
                Directory.CreateDirectory(resourceDirectory);
                var filePath = Path.Combine(resourceDirectory, ChangelogViewModel.ReleaseFileName);
                File.WriteAllText(filePath, bodyContent);
                LoggerHelper.Info($"{ChangelogViewModel.ReleaseFileName} saved successfully.");
            }
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"Error saving {ChangelogViewModel.ReleaseFileName}: {ex.Message}");
        }
    }

    private static void SaveChangelog(JToken? releaseData, string from)
    {
        try
        {
            var bodyContent = releaseData?[from]?.ToString();
            if (!string.IsNullOrWhiteSpace(bodyContent) && bodyContent != "placeholder")
            {
                var resourceDirectory = Path.Combine(AppContext.BaseDirectory, "resource");
                Directory.CreateDirectory(resourceDirectory);
                var announcementDir = Path.Combine(resourceDirectory, "Announcement");
                Directory.CreateDirectory(announcementDir);
                var filePath = Path.Combine(announcementDir, ChangelogViewModel.ChangelogFileName);
                File.WriteAllText(filePath, bodyContent);
                LoggerHelper.Info($"{ChangelogViewModel.ChangelogFileName} saved successfully.");
                GlobalConfiguration.SetValue(ConfigurationKeys.DoNotShowChangelogAgain, bool.FalseString);
            }
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"Error saving {ChangelogViewModel.ChangelogFileName}: {ex.Message}");
        }
    }


    public static HttpClient CreateHttpClientWithProxy()
    {
        // 检查是否禁用SSL
        bool disableSSL = File.Exists(Path.Combine(AppContext.BaseDirectory, "NO_SSL"));
        LoggerHelper.Info($"SSL验证状态: {(disableSSL ? "已禁用" : "已启用")}");

        // 获取应用内代理设置
        var appProxyAddress = Instances.VersionUpdateSettingsUserControlModel.ProxyAddress;
        var appProxyType = Instances.VersionUpdateSettingsUserControlModel.ProxyType;
        NetworkCredential? credentials = null;

        // 检测系统代理 (新增)
        WebProxy systemProxy = null;
        try
        {
            // 获取系统默认代理
            systemProxy = (WebProxy)WebRequest.GetSystemWebProxy();
            if (systemProxy != null && !string.IsNullOrWhiteSpace(systemProxy.Address?.AbsoluteUri))
            {
                LoggerHelper.Info($"检测到系统代理: {systemProxy.Address}");
            }
        }
        catch (Exception ex)
        {
            LoggerHelper.Warning($"系统代理检测失败: {ex.Message}");
        }

        // 优先使用应用内代理，若无则检查系统代理
        string effectiveProxyAddress = string.IsNullOrWhiteSpace(appProxyAddress)
            ? (systemProxy?.Address?.AbsoluteUri ?? "")
            : appProxyAddress;

        // 判断是否需要使用代理
        var useProxy = !string.IsNullOrWhiteSpace(effectiveProxyAddress);

        var handler = new HttpClientHandler
        {
            // 优化SSL验证逻辑，启用时进行基本证书检查
            ServerCertificateCustomValidationCallback = disableSSL
                ? (_, _, _, _) => 
                {
                    LoggerHelper.Info("SSL验证已禁用");
                    return true;
                }
                : (sender, cert, chain, errors) =>
                {
                    // 简单证书验证，可根据需求扩展
                    bool isValid = errors == SslPolicyErrors.None;
                    if (!isValid)
                    {
                        LoggerHelper.Warning($"SSL证书验证失败: {errors}");
                        // 这里可以添加特定证书信任逻辑
                    }
                    return isValid;
                },
            UseCookies = false,
            // 增加连接超时设置
            SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls11 | SslProtocols.Tls // 明确支持的SSL协议
        };

        if (useProxy)
        {
            try
            {
                // 解析代理地址 (支持应用内代理和系统代理)
                string proxyToUse = appProxyAddress;
                VersionUpdateSettingsUserControlModel.UpdateProxyType proxyTypeToUse = appProxyType;

                // 如果使用系统代理，设置为HTTP代理
                if (string.IsNullOrWhiteSpace(appProxyAddress) && systemProxy?.Address != null)
                {
                    proxyToUse = systemProxy.Address.AbsoluteUri;
                    proxyTypeToUse = VersionUpdateSettingsUserControlModel.UpdateProxyType.Http;

                    // 尝试获取系统代理认证信息 (新增)
                    if (systemProxy.Credentials != null && systemProxy.Credentials is NetworkCredential sysCreds)
                    {
                        credentials = sysCreds;
                        LoggerHelper.Info("使用系统代理认证信息");
                    }
                }

                // 解析代理地址和认证信息
                var userHostParts = proxyToUse.Split('@');
                string endpointPart;

                if (userHostParts.Length == 2)
                {
                    var credentialsPart = userHostParts[0];
                    endpointPart = userHostParts[1];
                    var creds = credentialsPart.Split(':');
                    if (creds.Length == 2)
                    {
                        credentials = new NetworkCredential(creds[0], creds[1]);
                    }
                    else
                    {
                        throw new FormatException("认证信息格式错误，应为 '<username>:<password>'");
                    }
                }
                else
                {
                    endpointPart = userHostParts[0];
                }

                var hostParts = endpointPart.Split(':');
                if (hostParts.Length != 2)
                    throw new FormatException("主机部分格式错误，应为 '<host>:<port>'");

                // 配置代理
                switch (proxyTypeToUse)
                {
                    case VersionUpdateSettingsUserControlModel.UpdateProxyType.Socks5:
                        handler.Proxy = new WebProxy($"socks5://{proxyToUse}", false, null, credentials);
                        break;
                    default:
                        handler.Proxy = new WebProxy($"http://{proxyToUse}", false, null, credentials);
                        break;
                }
                handler.UseProxy = true;
            }
            catch (Exception ex)
            {
                LoggerHelper.Error($"代理配置失败: {ex.Message}");
                handler.UseProxy = false;
            }
        }

        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30),
            DefaultRequestVersion = HttpVersion.Version11
        };
    }
    /// <summary>
    /// 从URL中提取文件扩展名
    /// </summary>
    private static string GetFileExtensionFromUrl(string url)
    {
        try
        {
            // 解析URL路径部分
            Uri uri = new Uri(url);
            string path = Uri.UnescapeDataString(uri.LocalPath);

            // 提取扩展名（自动处理带查询参数的情况）
            return Path.GetExtension(path);
        }
        catch (Exception ex)
        {
            LoggerHelper.Warning($"解析URL扩展名失败: {ex.Message}");
            return string.Empty;
        }
    }

    /// <summary>
    /// 从Content-Disposition头解析文件名（可选增强）
    /// </summary>
    private static string? ParseFileNameFromContentDisposition(string contentDisposition)
    {
        // 示例格式: "attachment; filename=resource.tar.gz"
        const string filenamePrefix = "filename=";
        int index = contentDisposition.IndexOf(filenamePrefix, StringComparison.OrdinalIgnoreCase);
        if (index >= 0)
        {
            string filename = contentDisposition.Substring(index + filenamePrefix.Length);
            // 移除引号
            if (filename.StartsWith("\"") && filename.EndsWith("\""))
            {
                filename = filename[1..^1];
            }
            return filename;
        }
        return null;
    }
}
