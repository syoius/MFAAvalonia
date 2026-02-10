using CommunityToolkit.Mvvm.ComponentModel;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using Avalonia.Threading;

namespace MFAAvalonia.ViewModels.Pages;

public partial class MonitorViewModel : ViewModelBase, IDisposable
{
    public ObservableCollection<MonitorItemViewModel> Items { get; } = new();
    private readonly DispatcherTimer _timer;
    private readonly object _updateLock = new();
    private CancellationTokenSource _updateCts = new();
    private int _isActive;
    private int _updateAllInProgress;

    public MonitorViewModel()
    {
        RefreshItems();
        
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1.5) 
        };
        _timer.Tick += OnTimerTick;
        
        MaaProcessor.Processors.CollectionChanged += Processors_CollectionChanged;
    }

    public void Activate()
    {
        if (Interlocked.Exchange(ref _isActive, 1) == 1)
            return;

        ResetUpdateCts();
        RefreshItems();
        _timer.Start();
    }

    public void Deactivate()
    {
        if (Interlocked.Exchange(ref _isActive, 0) == 0)
            return;

        _timer.Stop();
        ResetUpdateCts();

        foreach (var item in Items)
        {
            item.ClearImage();
        }
    }

    private void Processors_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
       RefreshItems();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        UpdateAll();
    }

    private void RefreshItems()
    {
        DispatcherHelper.PostOnMainThread(() =>
        {
            var processors = MaaProcessor.Processors.ToList();
            
            var toRemove = Items.Where(i => !processors.Contains(i.Processor)).ToList();
            foreach(var item in toRemove)
            {
                item.Dispose();
                Items.Remove(item);
            }

            foreach(var p in processors)
            {
                if (!Items.Any(i => i.Processor == p))
                {
                    Items.Add(new MonitorItemViewModel(p));
                }
            }
        });
    }

    private void UpdateAll()
    {
        if (Interlocked.Exchange(ref _updateAllInProgress, 1) == 1)
            return;

        foreach(var item in Items)
        {
            item.UpdateInfo();
        }

        try
        {
            if (Interlocked.CompareExchange(ref _isActive, 0, 0) == 0)
                return;

            var token = GetUpdateToken();

            foreach (var item in Items)
            {
                if (token.IsCancellationRequested)
                    break;

                var taskVm = item.Processor.ViewModel;
                if (taskVm is { EnableLiveView: false })
                {
                    item.ClearImage();
                    continue;
                }

                if (item.IsConnected)
                {
                    // Run image update in background (guarded in item)
                    System.Threading.Tasks.Task.Run(() => item.UpdateImage(token), token);
                }
                else
                {
                    item.ClearImage();
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _updateAllInProgress, 0);
        }
    }

    private CancellationToken GetUpdateToken()
    {
        lock (_updateLock)
        {
            return _updateCts.Token;
        }
    }

    private void ResetUpdateCts()
    {
        lock (_updateLock)
        {
            _updateCts.Cancel();
            _updateCts.Dispose();
            _updateCts = new CancellationTokenSource();
        }
    }

    public void Dispose()
    {
        MaaProcessor.Processors.CollectionChanged -= Processors_CollectionChanged;
        _timer.Tick -= OnTimerTick;
        _timer.Stop();
        foreach(var item in Items) item.Dispose();
        ResetUpdateCts();
        GC.SuppressFinalize(this);
    }
}
