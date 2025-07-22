using CommunityToolkit.Mvvm.ComponentModel;
using MaaFramework.Binding;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Views.Windows;
using MFAAvalonia.Helper;
using Serilog;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MFAAvalonia.Helper.ValueType;

public partial class MFATask : ObservableObject
{
    public enum MFATaskType
    {
        MFA,
        MAAFW
    }

    public enum MFATaskStatus
    {
        NOT_STARTED,
        STOPPED,
        SUCCEEDED,
        FAILED,
    }

    [ObservableProperty] private string? _name = string.Empty;
    [ObservableProperty] private MFATaskType _type = MFATaskType.MFA;
    [ObservableProperty] private int _count = 1;
    [ObservableProperty] private Func<Task> _action;
    // [ObservableProperty] private Dictionary<string, MaaNode> _tasks = new();
    [ObservableProperty] private bool _isUpdateRelated;

    public async Task<MFATaskStatus> Run(CancellationToken token)
    {
        try
        {
            for (int i = 0; i < Count; i++)
            {
                token.ThrowIfCancellationRequested();
                if (Type == MFATaskType.MAAFW)
                    RootView.AddLogByKey("TaskStart", null, true, Name ?? string.Empty);
                await Action();
            }
            return MFATaskStatus.SUCCEEDED;
        }
        catch (MaaJobStatusException)
        {
            LoggerHelper.Error($"Task {Name} failed to run");
            return MFATaskStatus.FAILED;
        }
        catch (OperationCanceledException)
        {
            return MFATaskStatus.STOPPED;
        }
        catch (Exception ex)
        {
            LoggerHelper.Error(ex);
            return MFATaskStatus.FAILED;
        }
    }
}
