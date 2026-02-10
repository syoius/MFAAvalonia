using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using MFAAvalonia.Helper.ValueType;
using Avalonia.Threading;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MFAAvalonia.ViewModels.Pages;

public partial class MonitorItemViewModel : ViewModelBase
{
    public MaaProcessor Processor { get; }
    private int _imageUpdateInProgress;

    [ObservableProperty]
    private Bitmap? _image;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCurrentTask))]
    [NotifyPropertyChangedFor(nameof(HasTaskProgress))]
    [NotifyPropertyChangedFor(nameof(TaskProgressPercent))]
    [NotifyPropertyChangedFor(nameof(TaskProgressText))]
    private bool _isRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCurrentTask))]
    private string _currentTaskName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTaskProgress))]
    [NotifyPropertyChangedFor(nameof(TaskProgressPercent))]
    [NotifyPropertyChangedFor(nameof(TaskProgressText))]
    private int _taskQueueRemaining;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTaskProgress))]
    [NotifyPropertyChangedFor(nameof(TaskProgressPercent))]
    [NotifyPropertyChangedFor(nameof(TaskProgressText))]
    private int _taskQueueTotal;

    [ObservableProperty]
    private bool _hasImage;

    public bool HasCurrentTask => IsRunning && !string.IsNullOrWhiteSpace(CurrentTaskName);
    public bool HasTaskProgress => IsRunning && TaskQueueTotal > 0;
    public double TaskProgressPercent =>
        HasTaskProgress ? Math.Clamp((TaskQueueTotal - TaskQueueRemaining) * 100.0 / TaskQueueTotal, 0, 100) : 0;
    public string TaskProgressText =>
        HasTaskProgress ? $"{TaskQueueTotal - TaskQueueRemaining}/{TaskQueueTotal}" : string.Empty;

    public MonitorItemViewModel(MaaProcessor processor)
    {
        Processor = processor;
        UpdateInfo();
    }

    public void UpdateInfo()
    {
        Name = MaaProcessorManager.Instance.GetInstanceName(Processor.InstanceId);
        IsConnected = Processor.ViewModel?.IsConnected ?? false;
        IsRunning = Processor.TaskQueue.Count > 0;
        TaskQueueRemaining = Processor.TaskQueue.CountWhere(task => task.Type == MFATask.MFATaskType.MAAFW);
        TaskQueueTotal = IsRunning ? Math.Max(Processor.MainTaskTotal, TaskQueueRemaining) : 0;
        CurrentTaskName = Processor.ViewModel?.CurrentTaskName ?? string.Empty;
    }

    public void UpdateImage(CancellationToken token)
    {
        if (token.IsCancellationRequested)
            return;

        if (Interlocked.Exchange(ref _imageUpdateInProgress, 1) == 1)
            return;

        try
        {
        if (!IsConnected)
        {
             ClearImage();
             return;
        }

        Bitmap? newImage = null;
        try 
        {
             newImage = Processor.GetLiveView(false); 
        }
        catch {}

        if (token.IsCancellationRequested)
        {
            newImage?.Dispose();
            return;
        }

        if (newImage != null)
        {
            DispatcherHelper.PostOnMainThread(() => 
            {
                if (token.IsCancellationRequested)
                {
                    newImage.Dispose();
                    return;
                }

                var old = Image;
                Image = newImage;
                old?.Dispose();
                HasImage = true;
            });
        }
        }
        finally
        {
            Interlocked.Exchange(ref _imageUpdateInProgress, 0);
        }
    }

    public void ClearImage()
    {
        DispatcherHelper.PostOnMainThread(() =>
        {
            var old = Image;
            Image = null;
            old?.Dispose();
            HasImage = false;
        });
    }

    [RelayCommand]
    private void Switch()
    {
        var tab = Instances.InstanceTabBarViewModel.Tabs.FirstOrDefault(t => t.Processor == Processor);
        if (tab != null)
        {
            Instances.InstanceTabBarViewModel.ActiveTab = tab;
        }
    }

    [RelayCommand]
    private void StartTask()
    {
        Processor.Start();
    }

    [RelayCommand]
    private void StopTask()
    {
        Processor.Stop(MFATask.MFATaskStatus.STOPPED);
    }

    [RelayCommand]
    private async Task Connect()
    {
        await Processor.ReconnectAsync();
    }

    public void Dispose()
    {
        ClearImage();
    }
}
