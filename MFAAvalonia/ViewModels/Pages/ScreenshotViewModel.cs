using Avalonia.Media;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MFAAvalonia.Configuration;
using MFAAvalonia.Extensions;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using MFAAvalonia.Helper.ValueType;
using System;
using System.Drawing;
using System.IO;
using Bitmap = Avalonia.Media.Imaging.Bitmap;

namespace MFAAvalonia.ViewModels.Pages;

public partial class ScreenshotViewModel : ViewModelBase
{
    [ObservableProperty] private Bitmap? _screenshotImage;
    [ObservableProperty] private string _taskName = string.Empty;
    [RelayCommand]
    private void Screenshot()
    {
        // if (MaaProcessor.Instance.MaaTasker == null)
        // {
        //     ToastHelper.Warn("Warning".ToLocalization(), (Instances.TaskQueueViewModel.CurrentController == MaaControllerTypes.Adb
        //         ? "Emulator".ToLocalization()
        //         : "Window".ToLocalization()) + "Unconnected".ToLocalization() + "!");
        //     return;
        // }
        try
        {
            if (MaaProcessor.Instance.MaaTasker is not { IsInitialized: true })
            {
                ToastHelper.Info("Tip".ToLocalization(), "ConnectingTo".ToLocalizationFormatted(true, Instances.TaskQueueViewModel.CurrentController == MaaControllerTypes.Adb ? "Emulator" : "Window"));
                MaaProcessor.Instance.TaskQueue.Enqueue(new MFATask
                {
                    Name = "截图前启动",
                    Type = MFATask.MFATaskType.MFA,
                    Action = async () => await MaaProcessor.Instance.TestConnecting(),
                });
                MaaProcessor.Instance.TaskQueue.Enqueue(new MFATask
                {
                    Name = "截图任务",
                    Type = MFATask.MFATaskType.MFA,
                    Action = async () => await TaskManager.RunTaskAsync(() =>
                    {
                        var bitmap = MaaProcessor.Instance.GetBitmapImage(false);
                        if (bitmap == null)
                            ToastHelper.Warn("ScreenshotFailed".ToLocalization());

                        DispatcherHelper.PostOnMainThread((() =>
                        {
                            ScreenshotImage = bitmap;
                            TaskName = string.Empty;
                        }));
                    }),
                });
                MaaProcessor.Instance.Start(true, checkUpdate: false);

            }
            else
                TaskManager.RunTaskAsync(() =>
                {
                    var bitmap = MaaProcessor.Instance.GetBitmapImage();
                    if (bitmap == null)
                        ToastHelper.Warn("ScreenshotFailed".ToLocalization());

                    DispatcherHelper.PostOnMainThread((() =>
                    {
                        ScreenshotImage = bitmap;
                        TaskName = string.Empty;
                    }));
                });
        }
        catch (Exception e)
        {
            LoggerHelper.Error(e);
        }
    }

    [RelayCommand]
    private void SaveScreenshot()
    {
        if (ScreenshotImage == null)
        {
            ToastHelper.Warn("Warning".ToLocalization(), "ScreenshotEmpty".ToLocalization());
            return;
        }
        var options = new FilePickerSaveOptions
        {
            Title = "SaveScreenshot".ToLocalization(),
            FileTypeChoices =
            [
                new FilePickerFileType("PNG")
                {
                    Patterns = ["*.png"]
                }
            ]
        };

        if (Instances.RootView.StorageProvider.SaveFilePickerAsync(options).Result is { } result && result.TryGetLocalPath() is { } path)
        {
            using var stream = File.Create(path);
            ScreenshotImage.Save(stream);
        }
    }
}
