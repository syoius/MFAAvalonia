using MFAAvalonia.Configuration;
using MFAAvalonia.Helper;
using MFAAvalonia.Helper.ValueType;
using MFAAvalonia.ViewModels.Other;
using MFAAvalonia.ViewModels.Pages;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace MFAAvalonia.Extensions.MaaFW;

/// <summary>
/// 任务加载器
/// </summary>
public class TaskLoader(MaaInterface? maaInterface, TaskQueueViewModel taskQueueViewModel)
{
    public const string NEW_SEPARATOR = "<|||>";
    public const string OLD_SEPARATOR = ":";


    /// <summary>
    /// 加载任务列表
    /// </summary>
    public void LoadTasks(
        List<MaaInterface.MaaInterfaceTask> tasks,
        ObservableCollection<DragItemViewModel> tasksSource,
        ref bool firstTask,
        IList<DragItemViewModel>? oldDrags = null)
    {
        // Keep interface definitions separate from editable task state, including Copilot's defaults.
        tasks = tasks.Select(task => task.Clone()).ToList();
        var instanceConfig = taskQueueViewModel.Processor.InstanceConfiguration;
        var currentTasks = instanceConfig.GetValue(ConfigurationKeys.CurrentTasks, new List<string>());
        if (currentTasks.Any(t => t.Contains(OLD_SEPARATOR) && !t.Contains(NEW_SEPARATOR)))
        {
            currentTasks = currentTasks
                .Select(item =>
                {
                    var parts = item.Split(OLD_SEPARATOR, 2);
                    return parts.Length == 2 ? $"{parts[0]}{NEW_SEPARATOR}{parts[1]}" : item;
                })
                .Distinct()
                .ToList();
        }
        // 如果传入了 oldDrags（用户当前的任务列表），优先使用它来保留用户的顺序和 check 状态
        // 只有当 oldDrags 为空时，才从配置中读取
        List<DragItemViewModel> drags;
        if (oldDrags != null && oldDrags.Count > 0)
        {
            drags = oldDrags.ToList();
        }
        else
        {
            var items = instanceConfig.GetValue(ConfigurationKeys.TaskItems, new List<MaaInterface.MaaInterfaceTask>()) ?? new List<MaaInterface.MaaInterfaceTask>();
            drags = items.Select(interfaceItem => new DragItemViewModel(interfaceItem) { OwnerViewModel = taskQueueViewModel }).ToList();
        }

        if (firstTask)
        {
            DispatcherHelper.RunOnMainThread(InitializeResources);
            firstTask = false;
        }

        var (updateList, removeList) = SynchronizeTaskItems(ref currentTasks, drags, tasks);
        instanceConfig.SetValue(ConfigurationKeys.CurrentTasks, currentTasks);
        
        updateList.RemoveAll(d => removeList.Contains(d));

        UpdateViewModels(updateList, tasks, tasksSource, reloadResourceOptions: oldDrags == null);
    }

    private void InitializeResources()
    {
        var instanceConfig = taskQueueViewModel.Processor.InstanceConfiguration;
        var allResources = maaInterface?.Resources.Values.Count > 0
            ? maaInterface.Resources.Values.ToList()
            :
            [
                new()
                {
                    Name = "Default",
                    Path = [MaaProcessor.ResourceBase]
                }
            ];

        // 获取当前控制器的名称
        var currentControllerName = GetCurrentControllerName();

        // 根据控制器过滤资源
        var filteredResources = FilterResourcesByController(allResources, currentControllerName);

        foreach (var resource in filteredResources)
        {
            resource.InitializeDisplayName();
        }
        taskQueueViewModel.CurrentResources = new ObservableCollection<MaaInterface.MaaInterfaceResource>(filteredResources);
        taskQueueViewModel.CurrentResource = instanceConfig.GetValue(ConfigurationKeys.Resource, string.Empty);
        if (taskQueueViewModel.CurrentResources.Count > 0 && taskQueueViewModel.CurrentResources.All(r => r.Name != taskQueueViewModel.CurrentResource))
            taskQueueViewModel.CurrentResource = taskQueueViewModel.CurrentResources[0].Name ?? "Default";
    }

    internal static List<MaaInterface.MaaInterfaceSelectOption> CreateResourceOptions(
        MaaInterface.MaaInterfaceResource resource,
        MaaInterface maaInterface,
        List<MaaInterface.MaaInterfaceSelectOption>? savedOptions)
    {
        if (resource.Option is not { Count: > 0 } optionNames) return [];

        // 收集所有子选项名称（这些选项不应该在顶级初始化）
        var subOptionNames = new HashSet<string>();
        foreach (var optionName in optionNames)
        {
            if (maaInterface.Option?.TryGetValue(optionName, out var interfaceOption) == true
                && interfaceOption.Cases != null)
            {
                foreach (var caseOption in interfaceOption.Cases)
                {
                    if (caseOption.Option != null)
                        subOptionNames.UnionWith(caseOption.Option);
                }
            }
        }

        return optionNames
            .Where(optionName => !subOptionNames.Contains(optionName))
            .Distinct()
            .Select(optionName =>
            {
                var savedOption = savedOptions?.FirstOrDefault(option => option.Name == optionName);
                if (savedOption != null) return CloneResourceOption(savedOption);

                var selectOption = new MaaInterface.MaaInterfaceSelectOption { Name = optionName };
                SetDefaultOptionValue(maaInterface, selectOption);
                return selectOption;
            }).ToList();
    }

    internal static MaaInterface.MaaInterfaceSelectOption CloneResourceOption(MaaInterface.MaaInterfaceSelectOption option)
    {
        return new MaaInterface.MaaInterfaceSelectOption
        {
            Name = option.Name,
            Index = option.Index,
            Data = option.Data != null ? new Dictionary<string, string?>(option.Data) : null,
            SubOptions = option.SubOptions?.Select(CloneResourceOption).ToList()
        };
    }

    internal static bool IsInlineResourceOption(MaaInterface.MaaInterfaceSelectOption option) =>
        !OperatingSystem.IsAndroid()
        && MaaProcessor.Interface?.Option?.TryGetValue(option.Name ?? string.Empty, out var definition) == true
        && definition.IsInput;

    /// <summary>
    /// 获取当前控制器的名称
    /// </summary>
    public string? GetCurrentControllerName()
    {
        return GetControllerName(taskQueueViewModel.CurrentController, maaInterface);
    }

    public static string? GetControllerName(MaaControllerTypes currentControllerType, MaaInterface? maaInterface)
    {
        var controllerTypeKey = currentControllerType.ToJsonKey();

        // 从 interface 的 controller 配置中查找匹配的控制器
        var controller = maaInterface?.Controller?.Find(c =>
            c.Type != null && c.Type.Equals(controllerTypeKey, StringComparison.OrdinalIgnoreCase));

        return controller?.Name;
    }

    /// <summary>
    /// 根据控制器过滤资源
    /// </summary>
    /// <param name="resources">所有资源列表</param>
    /// <param name="controllerName">当前控制器名称</param>
    /// <returns>过滤后的资源列表</returns>
    public static List<MaaInterface.MaaInterfaceResource> FilterResourcesByController(
        List<MaaInterface.MaaInterfaceResource> resources,
        string? controllerName)
    {
        return resources.Where(r =>
        {
            // 如果资源没有指定 controller，则支持所有控制器
            if (r.Controller == null || r.Controller.Count == 0)
                return true;

            // 如果当前控制器名称为空，则显示所有资源
            if (string.IsNullOrWhiteSpace(controllerName))
                return true;

            // 检查资源是否支持当前控制器
            return r.Controller.Any(c =>
                c.Equals(controllerName, StringComparison.OrdinalIgnoreCase));
        }).ToList();
    }

    private (List<DragItemViewModel> updateList, List<DragItemViewModel> removeList) SynchronizeTaskItems(
        ref List<string> currentTasks,
        IList<DragItemViewModel> drags,
        List<MaaInterface.MaaInterfaceTask> tasks)
    {
        var currentTaskSet = currentTasks;

        var removeList = new List<DragItemViewModel>();
        var updateList = new List<DragItemViewModel>();

        var taskDict = tasks
            .GroupBy(t => (t.Name, t.Entry))
            .ToDictionary(group => group.Key, group => group.Last());

        var taskByEntry = tasks
            .Where(t => !string.IsNullOrWhiteSpace(t.Entry))
            .GroupBy(t => t.Entry!)
            .ToDictionary(group => group.Key, group => group.Last());

        foreach (var oldItem in drags)
        {
            var key = (oldItem.InterfaceItem?.Name, oldItem.InterfaceItem?.Entry);

            if (taskDict.TryGetValue(key, out var exact))
            {
                UpdateExistingItem(oldItem, exact);
                updateList.Add(oldItem);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(oldItem.InterfaceItem?.Entry)
                && taskByEntry.TryGetValue(oldItem.InterfaceItem.Entry!, out var byEntry))
            {
                UpdateExistingItem(oldItem, byEntry, true);
                updateList.Add(oldItem);
                continue;
            }

            removeList.Add(oldItem);
        }

        var existingKeys = new HashSet<string>(
            updateList.Select(item => $"{item.InterfaceItem?.Name}{NEW_SEPARATOR}{item.InterfaceItem?.Entry}"));

        foreach (var task in tasks)
        {
            var historyKey = $"{task.Name}{NEW_SEPARATOR}{task.Entry}";
            var isNewTask = !currentTaskSet.Contains(historyKey);

            if (!isNewTask)
            {
                continue;
            }

            if (existingKeys.Contains(historyKey))
            {
                continue;
            }

            var newItem = new DragItemViewModel(task) { OwnerViewModel = taskQueueViewModel };
            if (task.Option != null)
            {
                task.Option.ForEach(option => SetDefaultOptionValue(maaInterface, option));
            }
            updateList.Add(newItem);
            existingKeys.Add(historyKey);
            currentTasks.Add(historyKey);
        }

        return (updateList, removeList);
    }


    private void UpdateExistingItem(DragItemViewModel oldItem, MaaInterface.MaaInterfaceTask newItem, bool updateName = false)
    {
        if (oldItem.InterfaceItem == null) return;
        if (updateName) oldItem.InterfaceItem.Name = newItem.Name;
        else if (oldItem.InterfaceItem.Name != newItem.Name) return;

        oldItem.InterfaceItem.Entry = newItem.Entry;
        oldItem.InterfaceItem.Label = newItem.Label;
        oldItem.InterfaceItem.PipelineOverride = newItem.PipelineOverride;
        oldItem.InterfaceItem.Description = newItem.Description;
        oldItem.InterfaceItem.Document = newItem.Document;
        oldItem.InterfaceItem.Repeatable = newItem.Repeatable;
        oldItem.InterfaceItem.Resource = newItem.Resource;
        oldItem.InterfaceItem.Controller = newItem.Controller;
        oldItem.InterfaceItem.Icon = newItem.Icon;

        // 更新图标
        oldItem.InterfaceItem.InitializeIcon();
        oldItem.ResolvedIcon = oldItem.InterfaceItem.ResolvedIcon;
        oldItem.HasIcon = oldItem.InterfaceItem.HasIcon;

        // 更新显示名称（保留自定义重命名/备注）
        oldItem.RefreshDisplayName();

        UpdateAdvancedOptions(oldItem, newItem);
        UpdateOptions(oldItem, newItem);

        // 更新 IsVisible 属性，确保设置图标的可见性正确
        oldItem.IsVisible = oldItem.InterfaceItem is { Advanced.Count: > 0 }
            || oldItem.InterfaceItem is { Option.Count: > 0 }
            || oldItem.InterfaceItem.Repeatable == true
            || !string.IsNullOrWhiteSpace(oldItem.InterfaceItem.Description)
            || oldItem.InterfaceItem.Document is { Count: > 0 };
    }


    private void UpdateAdvancedOptions(DragItemViewModel oldItem, MaaInterface.MaaInterfaceTask newItem)
    {
        if (newItem.Advanced != null)
        {
            var tempDict = oldItem.InterfaceItem?.Advanced?.ToDictionary(t => t.Name) ?? new Dictionary<string, MaaInterface.MaaInterfaceSelectAdvanced>();
            var advanceds = JsonConvert.DeserializeObject<List<MaaInterface.MaaInterfaceSelectAdvanced>>(JsonConvert.SerializeObject(newItem.Advanced));
            oldItem.InterfaceItem!.Advanced = advanceds?.Select(opt =>
            {
                if (tempDict.TryGetValue(opt.Name ?? string.Empty, out var existing)) opt.Data = existing.Data;
                return opt;
            }).ToList();
        }
        else oldItem.InterfaceItem!.Advanced = null;
    }

    private void UpdateOptions(DragItemViewModel oldItem, MaaInterface.MaaInterfaceTask newItem)
    {
        if (newItem.Option != null)
        {
            var existingDict = oldItem.InterfaceItem?.Option?.ToDictionary(t => t.Name ?? string.Empty)
                ?? new Dictionary<string, MaaInterface.MaaInterfaceSelectOption>();

            var newOptions = new List<MaaInterface.MaaInterfaceSelectOption>();

            foreach (var newOpt in newItem.Option)
            {
                var optName = newOpt.Name ?? string.Empty;

                if (existingDict.TryGetValue(optName, out var existing))
                {
                    // 保留原有对象，只更新必要的属性（如果interface 定义变了需要调整）
                    // 这样 UI 控件的事件处理器仍然引用同一个对象，用户的修改能正确反映
                    if ((maaInterface?.Option?.TryGetValue(optName, out var io) ?? false) && io.Cases is { Count: > 0 })
                    {
                        // 只有当 Index 超出范围时才调整
                        if (existing.Index.HasValue && existing.Index.Value >= io.Cases.Count)
                        {
                            existing.Index = io.Cases.Count - 1;
                        }
                    }
                    newOptions.Add(existing);
                }
                else
                {
                    // 新增的选项，创建新对象并设置默认值
                    var opt = new MaaInterface.MaaInterfaceSelectOption
                    {
                        Name = newOpt.Name,
                        Index = newOpt.Index,
                        Data = newOpt.Data != null ? new Dictionary<string, string?>(newOpt.Data) : null
                    };
                    SetDefaultOptionValue(maaInterface, opt);
                    newOptions.Add(opt);
                }
            }

            oldItem.InterfaceItem!.Option = newOptions;
        }
        else oldItem.InterfaceItem!.Option = null;
    }

    private List<MaaInterface.MaaInterfaceSelectOption> MergeSubOptions(List<MaaInterface.MaaInterfaceSelectOption> existingSubOptions)
    {
        return existingSubOptions.Select(subOpt =>
        {
            var newSubOpt = new MaaInterface.MaaInterfaceSelectOption
            {
                Name = subOpt.Name,
                Index = subOpt.Index,
                Data = subOpt.Data?.Count > 0 ? new Dictionary<string, string?>(subOpt.Data) : null
            };
            if ((maaInterface?.Option?.TryGetValue(subOpt.Name ?? string.Empty, out var sio) ?? false) && sio.Cases is { Count: > 0 })
                newSubOpt.Index = Math.Min(subOpt.Index ?? 0, sio.Cases.Count - 1);
            if (subOpt.SubOptions?.Count > 0) newSubOpt.SubOptions = MergeSubOptions(subOpt.SubOptions);
            return newSubOpt;
        }).ToList();
    }

    public static void SetDefaultOptionValue(MaaInterface? @interface, MaaInterface.MaaInterfaceSelectOption option)
    {
        if (!(@interface?.Option?.TryGetValue(option.Name ?? string.Empty, out var io) ?? false)) return;
        var defaultIndex = io.Cases?.FindIndex(c => c.Name == io.DefaultCase) ?? -1;
        if (defaultIndex != -1) option.Index = defaultIndex;
        if (io.IsInput && io.Inputs != null)
        {
            option.Data ??= new Dictionary<string, string?>();
            foreach (var input in io.Inputs)
                if (!string.IsNullOrEmpty(input.Name) && !option.Data.ContainsKey(input.Name))
                    option.Data[input.Name] = input.Default ?? string.Empty;
        }

    }

    private void UpdateViewModels(IList<DragItemViewModel> drags, List<MaaInterface.MaaInterfaceTask> tasks,
        ObservableCollection<DragItemViewModel> tasksSource, bool reloadResourceOptions)
    {
        var newItems = tasks.Select(t => new DragItemViewModel(t) { OwnerViewModel = taskQueueViewModel }).ToList();
        if (drags.Count == 0)
        {
            foreach (var item in newItems)
                item.InterfaceItem?.Option?.ForEach(option => SetDefaultOptionValue(maaInterface, option));
        }

        // UI 线程更新集合，确保 TaskList 刷新
        DispatcherHelper.RunOnMainThread(() =>
        {
            if (reloadResourceOptions)
                taskQueueViewModel.ResetResourceOptions();

            tasksSource.Clear();
            foreach (var item in newItems) tasksSource.Add(item);

            var resourceItem = taskQueueViewModel.TaskItemViewModels.FirstOrDefault(item => item.IsResourceOptionItem);
            taskQueueViewModel.TaskItemViewModels.Clear();
            if (resourceItem != null)
                taskQueueViewModel.TaskItemViewModels.Add(resourceItem);
            foreach (var item in (drags.Count > 0 ? drags : newItems).Where(item => !item.IsResourceOptionItem))
                taskQueueViewModel.TaskItemViewModels.Add(item);

            taskQueueViewModel.UpdateTasksForResource(taskQueueViewModel.CurrentResource);
        });
    }
}
