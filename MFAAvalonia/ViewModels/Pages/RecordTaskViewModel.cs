using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaaFramework.Binding;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using MFAAvalonia.ViewModels.Other;
using MFAAvalonia.Views.Windows;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace MFAAvalonia.ViewModels.Pages;

public partial class RecordTaskViewModel : ObservableObject
{
    private static readonly IReadOnlyDictionary<string, FightActionTemplate> ActionTemplates =
        new Dictionary<string, FightActionTemplate>(StringComparer.Ordinal)
        {
            // 来源：MaaYuanCopilot_自己自定义一下吧.json（actions 内含 action + text_doc 的节点）
            // 说明：忽略“检测回合xx / 回合x行动x”的 actionKey，仅使用 text_doc 作为按钮名/录制 token。
            ["1A"] = FightActionTemplate.Click(target: [56, 1060, 5, 5]),
            ["1↑"] = FightActionTemplate.Swipe(begin: [77, 1060, 10, 1], end: [77, 670, 10, 1], durationMs: 800),
            ["1↓"] = FightActionTemplate.Swipe(begin: [73, 1060, 1, 1], end: [73, 1258, 1, 1], durationMs: 800),
            ["2A"] = FightActionTemplate.Click(target: [180, 1060, 5, 5]),
            ["2↑"] = FightActionTemplate.Swipe(begin: [220, 1060, 1, 1], end: [225, 668, 1, 1], durationMs: 800),
            ["2↓"] = FightActionTemplate.Swipe(begin: [221, 1060, 1, 1], end: [221, 1251, 1, 1], durationMs: 800),
            ["3A"] = FightActionTemplate.Click(target: [357, 1060, 5, 5]),
            ["3↑"] = FightActionTemplate.Swipe(begin: [357, 1060, 1, 1], end: [357, 714, 1, 1], durationMs: 800),
            ["3↓"] = FightActionTemplate.Swipe(begin: [357, 1060, 1, 1], end: [357, 1237, 1, 1], durationMs: 800),
            ["4A"] = FightActionTemplate.Click(target: [496, 1060, 5, 5]),
            ["4↑"] = FightActionTemplate.Swipe(begin: [496, 1060, 1, 1], end: [496, 679, 1, 1], durationMs: 800),
            ["4↓"] = FightActionTemplate.Swipe(begin: [496, 1060, 1, 1], end: [496, 1258, 1, 1], durationMs: 800),
            ["5A"] = FightActionTemplate.Click(target: [646, 1060, 5, 5]),
            ["5↑"] = FightActionTemplate.Swipe(begin: [646, 1060, 1, 1], end: [642, 700, 1, 1], durationMs: 800),
            ["5↓"] = FightActionTemplate.Swipe(begin: [646, 1060, 1, 1], end: [646, 1258, 1, 1], durationMs: 800),
            ["额外:左侧目标"] = FightActionTemplate.Click(target: [154, 648, 1, 1]),
            ["额外:右侧目标"] = FightActionTemplate.Click(target: [603, 413, 18, 21]),
            ["额外:吕布"] = FightActionTemplate.PipelineTask("录制_吕布切换形态"),
            ["额外:史子眇sp"] = FightActionTemplate.PipelineTask("录制_史子眇sp技能"),

            // 兼容旧显示名（不会出现在按钮列表中）
            ["1号位普攻"] = FightActionTemplate.Click(target: [56, 1060, 5, 5]),
            ["2号位普攻"] = FightActionTemplate.Click(target: [180, 1060, 5, 5]),
            ["3号位普攻"] = FightActionTemplate.Click(target: [357, 1060, 5, 5]),
            ["4号位普攻"] = FightActionTemplate.Click(target: [496, 1060, 5, 5]),
            ["5号位普攻"] = FightActionTemplate.Click(target: [646, 1060, 5, 5]),
            ["1号位下拉"] = FightActionTemplate.Swipe(begin: [73, 1060, 1, 1], end: [73, 1258, 1, 1], durationMs: 800),
            ["2号位下拉"] = FightActionTemplate.Swipe(begin: [221, 1060, 1, 1], end: [221, 1251, 1, 1], durationMs: 800),
        };

    private static readonly IReadOnlyList<string> SortedActionNames =
    [
        "1↑","2↑", "3↑", "4↑","5↑",
        "1A", "2A", "3A","4A", "5A",
        "1↓", "2↓","3↓", "4↓", "5↓",
        "额外:左侧目标", "额外:右侧目标","额外:吕布","额外:史子眇sp"
    ];

    private const string SimingExportApiUrl = "https://share.maayuan.top/simingapi/api/export";
    private static string CopilotCacheDir => Path.Combine(MaaProcessor.Resource, "copilot-cache");
    private static string RecordingsDir => Path.Combine(MaaProcessor.Resource, "recordings");
    private static string LegacyRecordingsDir => Path.Combine(CopilotCacheDir, "recordings");

    public ObservableCollection<RecordingFileItem> RecordingFiles { get; } = new();
    public ObservableCollection<RecordedStepItem> RecordedSteps { get; } = new();
    public ObservableCollection<RecordedRoundGroupItem> RecordedStepGroups { get; } = new();
    public ObservableCollection<RecordedRoundTableRowItem> RecordedRoundTableRows { get; } = new();
    public ObservableCollection<ActionButtonItem> AvailableActions { get; } = new();

    [ObservableProperty] private RecordingFileItem? _selectedRecording;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteRecordedStepCommand))]
    private bool _isRecording;
    [ObservableProperty] private bool _canSave;
    [ObservableProperty] private string _recordingName = string.Empty;
    [ObservableProperty] private string _status = string.Empty;

    // 修改跟踪相关属性
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private string? _currentLoadedFilePath;
    private string? _originalRecordingName;

    public bool IsNotRecording => !IsRecording;

    private readonly SemaphoreSlim _actionLock = new(1, 1);
    private readonly SemaphoreSlim _selectionLoadLock = new(1, 1);
    private RecordingOverlayView? _overlay;
    private readonly Dictionary<int, List<RecordedStepData>> _roundSteps = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PrevRoundCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextRoundCommand))]
    private int _currentRound = 1;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PrevRoundCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextRoundCommand))]
    private int _roundCount = 1;

    public string RoundDisplay => $"回合 {CurrentRound}/{RoundCount}";

    #region 右侧列（日志）- 与 CopilotView 右栏对齐
    // 说明：为复用 Copilot/TaskQueue 的右侧“日志”UI，本 ViewModel 补齐必要的同名属性/命令，
    // 直接转发到 Instances.TaskQueueViewModel，避免重复逻辑。

    public ObservableCollection<LogItemViewModel> LogItemViewModels =>
        Instances.TaskQueueViewModel.LogItemViewModels;

    [RelayCommand]
    private void Clear()
    {
        try { Instances.TaskQueueViewModel.ClearCommand?.Execute(null); }
        catch (Exception ex) { LoggerHelper.Error(ex); }
    }

    [RelayCommand]
    private void Export()
    {
        try { Instances.TaskQueueViewModel.ExportCommand?.Execute(null); }
        catch (Exception ex) { LoggerHelper.Error(ex); }
    }
    #endregion

    public RecordTaskViewModel()
    {
        RecordedSteps.CollectionChanged += (_, _) => UpdateCanSave();
        ResetRounds();
        UpdateCanSave();
    }

    partial void OnIsRecordingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNotRecording));

        if (value)
            SelectedRecording = null;
    }

    partial void OnSelectedRecordingChanged(RecordingFileItem? value)
    {
        if (IsRecording || value == null)
            return;

        _ = LoadSelectedRecordingAsync(value);
    }

    public void Initialize()
    {
        EnsureDirs();
        if (AvailableActions.Count == 0)
        {
            foreach (var token in SortedActionNames)
                AvailableActions.Add(new ActionButtonItem(displayName: GetActionDisplayName(token), token: token, command: ExecuteActionCommand));
        }
        if (string.IsNullOrWhiteSpace(RecordingName))
            RecordingName = $"录制作业-{DateTime.Now:yyyyMMdd-HHmmss}";
        _ = RefreshAsync();
    }

    private static string GetActionDisplayName(string token) =>
        token switch
        {
            "额外:左侧目标" => "左",
            "额外:右侧目标" => "右",
            "额外:吕布" => "吕布",
            "额外:史子眇sp" => "史SP",
            _ => token
        };

    private static void EnsureDirs()
    {
        Directory.CreateDirectory(RecordingsDir);
        if (!Directory.Exists(LegacyRecordingsDir))
            return;

        try
        {
            foreach (var source in Directory.EnumerateFiles(LegacyRecordingsDir, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(LegacyRecordingsDir, source);
                var dest = Path.Combine(RecordingsDir, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

                if (!File.Exists(dest))
                    File.Move(source, dest);
            }

            foreach (var dir in Directory.EnumerateDirectories(LegacyRecordingsDir, "*", SearchOption.AllDirectories)
                         .OrderByDescending(d => d.Length))
            {
                if (!Directory.EnumerateFileSystemEntries(dir).Any())
                    Directory.Delete(dir);
            }

            if (!Directory.EnumerateFileSystemEntries(LegacyRecordingsDir).Any())
                Directory.Delete(LegacyRecordingsDir);
        }
        catch (Exception ex)
        {
            LoggerHelper.Warning($"迁移 recordings 失败: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var selectedPath = SelectedRecording?.FullPath;
        try
        {
            EnsureDirs();
            var files = Directory.EnumerateFiles(RecordingsDir, "*.json", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(RecordingsDir, "*.jsonc", SearchOption.AllDirectories))
                .Select(p => new FileInfo(p))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Select(f => new RecordingFileItem(f.Name, f.FullName, f.LastWriteTimeUtc))
                .ToList();

            RecordingFiles.Clear();
            foreach (var item in files)
                RecordingFiles.Add(item);

            if (!IsRecording && !string.IsNullOrWhiteSpace(selectedPath))
            {
                var nextSelected = RecordingFiles.FirstOrDefault(f =>
                    string.Equals(f.FullPath, selectedPath, StringComparison.OrdinalIgnoreCase));
                if (nextSelected != null)
                    SelectedRecording = nextSelected;
            }
        }
        catch (Exception ex)
        {
            LoggerHelper.Error(ex);
            ToastHelper.Error("刷新录制作业列表失败");
        }

        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task DeleteSelectedRecordingAsync()
    {
        if (SelectedRecording == null)
            return;

        try
        {
            var path = SelectedRecording.FullPath;
            var name = SelectedRecording.Name;

            if (File.Exists(path))
            {
                File.Delete(path);
                ToastHelper.Success($"已删除：{name}");
            }

            // 清空当前加载状态
            if (string.Equals(CurrentLoadedFilePath, path, StringComparison.OrdinalIgnoreCase))
            {
                CurrentLoadedFilePath = null;
                _originalRecordingName = null;
                IsDirty = false;
                ResetRounds();
                RecordingName = $"录制作业-{DateTime.Now:yyyyMMdd-HHmmss}";
            }

            SelectedRecording = null;
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            LoggerHelper.Error(ex);
            ToastHelper.Error("删除失败");
        }
    }

    [RelayCommand]
    private async Task OpenRecordingsDirAsync()
    {
        try
        {
            EnsureDirs();
            using var p = new Process();
            if (OperatingSystem.IsWindows()) { p.StartInfo.FileName = "explorer"; p.StartInfo.Arguments = RecordingsDir; }
            else if (OperatingSystem.IsMacOS()) { p.StartInfo.FileName = "open"; p.StartInfo.Arguments = RecordingsDir; }
            else { p.StartInfo.FileName = "xdg-open"; p.StartInfo.Arguments = RecordingsDir; }
            p.Start();
        }
        catch (Exception ex)
        {
            LoggerHelper.Error(ex);
        }
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task StartRecordingAsync()
    {
        try
        {
            EnsureDirs();
            if (IsRecording && _overlay != null)
            {
                if (!_overlay.IsVisible)
                    _overlay.Show();
                _overlay.Activate();
                return;
            }

            ResetRounds();
            IsRecording = true;
            Status = "录制中";

            // 重置加载状态，开始新录制
            CurrentLoadedFilePath = null;
            _originalRecordingName = null;
            IsDirty = false;

            if (string.IsNullOrWhiteSpace(RecordingName))
                RecordingName = $"录制作业-{DateTime.Now:yyyyMMdd-HHmmss}";

            if (_overlay == null)
            {
                _overlay = new RecordingOverlayView
                {
                    DataContext = this,
                    Topmost = true,
                    ShowInTaskbar = false,
                };

                _overlay.Closed += (_, _) =>
                {
                    _overlay = null;
                    if (IsRecording)
                    {
                        IsRecording = false;
                        Status = "已结束";
                    }
                };
            }

            if (!_overlay.IsVisible)
                _overlay.Show();

            _overlay.Activate();
        }
        catch (Exception ex)
        {
            LoggerHelper.Error(ex);
            ToastHelper.Error("开始录制失败");
        }

        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task StopRecordingAsync()
    {
        try
        {
            IsRecording = false;
            Status = "已结束";

            if (_overlay != null)
            {
                _overlay.Close();
                _overlay = null;
            }
        }
        catch (Exception ex)
        {
            LoggerHelper.Error(ex);
        }

        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task ExecuteActionAsync(string? actionName)
    {
        if (string.IsNullOrWhiteSpace(actionName))
            return;

        if (!IsRecording)
        {
            ToastHelper.Warn("请先点击“开始录制”");
            return;
        }

        await _actionLock.WaitAsync();
        try
        {
            if (!ActionTemplates.TryGetValue(actionName, out var template))
            {
                ToastHelper.Warn($"未找到动作模板：{actionName}");
                return;
            }

            var triggeredAt = DateTimeOffset.Now;
            if (template.Kind == FightActionKind.RecordOnly)
            {
                AppendStep(actionName, triggeredAt);
                return;
            }

            var tasker = await MaaProcessor.Instance.GetTaskerAsync();
            if (tasker == null)
            {
                ToastHelper.Error("未连接到控制器/Agent");
                return;
            }

            await Task.Run(() =>
            {
                switch (template.Kind)
                {
                    case FightActionKind.Click:
                        var (cx, cy) = template.GetClickPoint();
                        tasker.Click(cx, cy);
                        break;
                    case FightActionKind.Swipe:
                        var args = template.GetSwipeArgs();
                        tasker.Swipe(args.StartX, args.StartY, args.EndX, args.EndY, args.DurationMs);
                        break;
                    case FightActionKind.PipelineTask:
                        // 直接调用已注册的 pipeline task
                        if (!string.IsNullOrWhiteSpace(template.TaskName))
                        {
                            var pipelineJob = tasker.AppendTask(template.TaskName);
                            pipelineJob.WaitFor(MaaJobStatus.Succeeded);
                        }
                        break;
                    default:
                        throw new NotSupportedException($"Unknown action kind: {template.Kind}");
                }
            });

            AppendStep(actionName, triggeredAt);
        }
        catch (Exception ex)
        {
            LoggerHelper.Error(ex);
            ToastHelper.Error("执行动作失败");
        }
        finally
        {
            _actionLock.Release();
        }
    }

    [RelayCommand]
    private async Task SaveRecordingAsync()
    {
        await TrySaveRecordingAsync(stopAfterSave: false);
    }

    [RelayCommand]
    private async Task SaveAndStopRecordingAsync()
    {
        await TrySaveRecordingAsync(stopAfterSave: true);
    }

    /// <summary>
    /// 保存对已加载录制文件的修改
    /// </summary>
    [RelayCommand]
    private async Task SaveModificationsAsync()
    {
        if (string.IsNullOrWhiteSpace(CurrentLoadedFilePath))
        {
            ToastHelper.Warn("没有加载的录制文件，无法保存修改");
            return;
        }

        try
        {
            var roundsPayload = BuildRoundsPayload();
            var exportRequest = BuildSimingExportRequest(RecordingName, roundsPayload);
            var requestJson = exportRequest.ToString(Formatting.Indented);

            await File.WriteAllTextAsync(CurrentLoadedFilePath, requestJson, new UTF8Encoding(false));

            _originalRecordingName = RecordingName;
            IsDirty = false;
            ToastHelper.Success("修改已保存");
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            LoggerHelper.Error(ex);
            ToastHelper.Error("保存修改失败");
        }
    }

    /// <summary>
    /// 放弃当前修改，重新加载原始文件
    /// </summary>
    [RelayCommand]
    private async Task DiscardModificationsAsync()
    {
        if (SelectedRecording == null)
        {
            ToastHelper.Warn("没有加载的录制文件");
            return;
        }

        try
        {
            // 重新加载原始文件
            await LoadSelectedRecordingAsync(SelectedRecording);
            ToastHelper.Info("已放弃修改");
        }
        catch (Exception ex)
        {
            LoggerHelper.Error(ex);
            ToastHelper.Error("放弃修改失败");
        }
    }

    /// <summary>
    /// 保存到作业列表（copilot-cache），不依赖录制状态
    /// </summary>
    [RelayCommand]
    private async Task SaveToCopilotCacheAsync()
    {
        if (GetTotalRecordedStepCount() == 0)
        {
            ToastHelper.Warn("没有任何录制步骤");
            return;
        }

        try
        {
            EnsureDirs();
            Directory.CreateDirectory(CopilotCacheDir);

            var baseName = string.IsNullOrWhiteSpace(RecordingName)
                ? $"录制作业-{DateTime.Now:yyyyMMdd-HHmmss}"
                : RecordingName;

            var roundsPayload = BuildRoundsPayload();
            var exportRequest = BuildSimingExportRequest(baseName, roundsPayload);

            // 调用 API 转换格式
            var result = await CallSimingExportAsync(exportRequest.ToString(Formatting.None));
            var jobJson = BuildCopilotCacheJobJson(baseName, result.Actions);

            var jobFileName = SanitizeJobFileName(result.FileName, baseName);
            var jobPath = UniquePath(Path.Combine(CopilotCacheDir, jobFileName));

            await File.WriteAllTextAsync(jobPath, jobJson.ToString(Formatting.Indented), new UTF8Encoding(false));

            ToastHelper.Success($"已保存到作业列表：{Path.GetFileName(jobPath)}");

            // 方案 A：主动刷新 CopilotViewModel
            try
            {
                var copilotVm = App.Services.GetRequiredService<CopilotViewModel>();
                await copilotVm.RefreshAsync();
            }
            catch (Exception ex)
            {
                LoggerHelper.Warning($"刷新作业列表失败: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            LoggerHelper.Error(ex);
            ToastHelper.Error("保存到作业列表失败");
        }
    }

    private async Task TrySaveRecordingAsync(bool stopAfterSave)
    {
        if (GetTotalRecordedStepCount() == 0)
        {
            ToastHelper.Warn("没有任何录制步骤");
            return;
        }

        try
        {
            EnsureDirs();

            var baseName = string.IsNullOrWhiteSpace(RecordingName)
                ? $"录制作业-{DateTime.Now:yyyyMMdd-HHmmss}"
                : RecordingName;

            var fileName = SanitizeFileName(baseName);
            if (!fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                fileName += ".json";

            var path = Path.Combine(RecordingsDir, fileName);

            var roundsPayload = BuildRoundsPayload();
            var exportRequest = BuildSimingExportRequest(baseName, roundsPayload);
            var requestJson = exportRequest.ToString(Formatting.Indented);

            // 1) 先保存“可直接 curl 调用 export 的请求体”到 recordings，便于复用/二次编辑
            await File.WriteAllTextAsync(path, requestJson, new UTF8Encoding(false));
            ToastHelper.Success($"已保存：{fileName}");
            await RefreshAsync();

            var saved = RecordingFiles.FirstOrDefault(f => string.Equals(f.FullPath, path, StringComparison.OrdinalIgnoreCase))
                        ?? RecordingFiles.FirstOrDefault(f => string.Equals(f.Name, fileName, StringComparison.OrdinalIgnoreCase));
            if (saved != null)
                SelectedRecording = saved;

            // 2) 将保存的 JSON 调用 export API，拿到 actions 结果并补齐为 copilot-cache 作业文件
            try
            {
                var result = await CallSimingExportAsync(exportRequest.ToString(Formatting.None));
                var jobJson = BuildCopilotCacheJobJson(baseName, result.Actions);

                var jobFileName = SanitizeJobFileName(result.FileName, baseName);
                var jobPath = UniquePath(Path.Combine(CopilotCacheDir, jobFileName));

                await File.WriteAllTextAsync(jobPath, jobJson.ToString(Formatting.Indented), new UTF8Encoding(false));
                ToastHelper.Success($"已生成作业：{Path.GetFileName(jobPath)}");
            }
            catch (Exception ex)
            {
                LoggerHelper.Error(ex);
                ToastHelper.Error("调用 export API 失败，已保留录制作业 JSON");
            }

            if (stopAfterSave)
                await StopRecordingAsync();
        }
        catch (Exception ex)
        {
            LoggerHelper.Error(ex);
            ToastHelper.Error("保存失败");
        }
    }

    [RelayCommand(CanExecute = nameof(CanPrevRound))]
    private void PrevRound()
    {
        SwitchRound(CurrentRound - 1);
    }

    private bool CanPrevRound => CurrentRound > 1;

    [RelayCommand(CanExecute = nameof(CanNextRound))]
    private void NextRound()
    {
        if (CurrentRound >= RoundCount)
        {
            var newRound = RoundCount + 1;
            EnsureRoundExists(newRound);
            RoundCount = newRound;
        }

        SwitchRound(CurrentRound + 1);
    }

    // 支持在最后一回合点击“下一回合”自动新增回合
    private bool CanNextRound => true;

    [RelayCommand]
    private void AddRound()
    {
        var newRound = RoundCount + 1;
        EnsureRoundExists(newRound);
        RoundCount = newRound;
        SwitchRound(newRound);
    }

    private void SwitchRound(int round)
    {
        if (round < 1 || round > RoundCount)
            return;

        CurrentRound = round;
        RefreshRecordedStepsForCurrentRound();
    }

    private void RefreshRecordedStepsForCurrentRound()
    {
        RecordedSteps.Clear();

        if (_roundSteps.TryGetValue(CurrentRound, out var steps) && steps.Count > 0)
        {
            for (var i = 0; i < steps.Count; i++)
            {
                var step = steps[i];
                RecordedSteps.Add(new RecordedStepItem(CurrentRound, i + 1, step.ActionName, step.TriggeredAt));
            }
        }

        RefreshRecordedStepGroups();
        UpdateCanSave();
    }

    private void RefreshRecordedStepGroups()
    {
        RecordedStepGroups.Clear();

        for (var round = 1; round <= RoundCount; round++)
        {
            if (!_roundSteps.TryGetValue(round, out var steps) || steps.Count == 0)
                continue;

            var group = new RecordedRoundGroupItem(round);
            for (var i = 0; i < steps.Count; i++)
            {
                var step = steps[i];
                group.Steps.Add(new RecordedStepItem(round, i + 1, step.ActionName, step.TriggeredAt));
            }

            RecordedStepGroups.Add(group);
        }

        RefreshRecordedRoundTableRows();
    }

    private void AppendStep(string actionName, DateTimeOffset triggeredAt)
    {
        // 兼容旧配置/旧录制：“+回合”不再作为动作记录，而是直接切到下一回合（必要时自动新增）
        if (string.Equals(actionName, "+回合", StringComparison.Ordinal))
        {
            NextRound();
            return;
        }

        EnsureRoundExists(CurrentRound);
        _roundSteps[CurrentRound].Add(new RecordedStepData(actionName, triggeredAt));
        RecordedSteps.Add(new RecordedStepItem(CurrentRound, RecordedSteps.Count + 1, actionName, triggeredAt));
        RefreshRecordedStepGroups();
        UpdateCanSave();
    }

    private void RefreshRecordedRoundTableRows()
    {
        RecordedRoundTableRows.Clear();

        for (var round = 1; round <= RoundCount; round++)
        {
            _roundSteps.TryGetValue(round, out var steps);
            steps ??= [];

            var slot1 = new List<RecordedRoundTablePillItem>();
            var slot2 = new List<RecordedRoundTablePillItem>();
            var slot3 = new List<RecordedRoundTablePillItem>();
            var slot4 = new List<RecordedRoundTablePillItem>();
            var slot5 = new List<RecordedRoundTablePillItem>();
            var extra = new List<RecordedRoundTablePillItem>();

            for (var i = 0; i < steps.Count; i++)
            {
                var index = i + 1;
                var actionName = steps[i].ActionName;
                if (TryParseSlotActionToken(actionName, out var slot, out var actionToken, out var kind))
                {
                    var pill = new RecordedRoundTablePillItem($"{index}{actionToken}", kind);
                    switch (slot)
                    {
                        case 1: slot1.Add(pill); break;
                        case 2: slot2.Add(pill); break;
                        case 3: slot3.Add(pill); break;
                        case 4: slot4.Add(pill); break;
                        case 5: slot5.Add(pill); break;
                        default: extra.Add(pill); break;
                    }

                    continue;
                }

                // 去掉 "额外:" 前缀，简化显示
                var displayName = actionName.StartsWith("额外:", StringComparison.Ordinal)
                    ? actionName[3..]
                    : actionName;
                extra.Add(new RecordedRoundTablePillItem($"{index}{displayName}", RecordedActionPillKind.Extra));
            }

            RecordedRoundTableRows.Add(new RecordedRoundTableRowItem(
                round: round,
                totalActions: steps.Count,
                slot1: slot1,
                slot2: slot2,
                slot3: slot3,
                slot4: slot4,
                slot5: slot5,
                extra: extra));
        }
    }

    private static bool TryParseSlotActionToken(
        string actionName,
        out int slot,
        out string actionToken,
        out RecordedActionPillKind kind)
    {
        slot = 0;
        actionToken = string.Empty;
        kind = RecordedActionPillKind.Other;

        if (string.IsNullOrWhiteSpace(actionName))
            return false;

        var ch0 = actionName[0];
        if (ch0 is < '1' or > '5')
            return false;

        slot = ch0 - '0';

        if (actionName.Length >= 2 && actionName[1] is 'A' or '↑' or '↓')
        {
            actionToken = actionName.Substring(1);
        }
        else if (actionName.Length >= 3 && actionName[1] == '号')
        {
            if (actionName.Contains("普攻", StringComparison.Ordinal))
                actionToken = "A";
            else if (actionName.Contains("上拉", StringComparison.Ordinal))
                actionToken = "↑";
            else if (actionName.Contains("下拉", StringComparison.Ordinal))
                actionToken = "↓";
            else
                actionToken = actionName;
        }
        else
        {
            actionToken = actionName.Length > 1 ? actionName.Substring(1) : actionName;
        }

        kind = actionToken switch
        {
            "A" => RecordedActionPillKind.Attack,
            "↑" => RecordedActionPillKind.Up,
            "↓" => RecordedActionPillKind.Down,
            _ => RecordedActionPillKind.Other
        };

        return true;
    }

    [RelayCommand(CanExecute = nameof(CanDeleteRecordedStep))]
    private void DeleteRecordedStep(RecordedStepItem? step)
    {
        if (step == null || step.Round < 1)
            return;

        if (!_roundSteps.TryGetValue(step.Round, out var steps) || steps.Count == 0)
            return;

        var index0 = step.Index - 1;
        if (index0 < 0 || index0 >= steps.Count)
            return;

        steps.RemoveAt(index0);

        // 标记为已修改（仅在加载已保存录制后）
        if (CurrentLoadedFilePath != null)
            IsDirty = true;

        if (step.Round == CurrentRound)
        {
            RefreshRecordedStepsForCurrentRound();
            return;
        }

        RefreshRecordedStepGroups();
        UpdateCanSave();
    }

    private bool CanDeleteRecordedStep(RecordedStepItem? step) =>
        step is { Round: >= 1 };

    private void ResetRounds()
    {
        _roundSteps.Clear();
        _roundSteps[1] = new List<RecordedStepData>();
        RoundCount = 1;
        CurrentRound = 1;
        RefreshRecordedStepsForCurrentRound();
    }

    private async Task LoadSelectedRecordingAsync(RecordingFileItem item)
    {
        await _selectionLoadLock.WaitAsync();
        try
        {
            if (IsRecording)
                return;

            if (!string.Equals(SelectedRecording?.FullPath, item.FullPath, StringComparison.OrdinalIgnoreCase))
                return;

            if (!File.Exists(item.FullPath))
                return;

            var json = await File.ReadAllTextAsync(item.FullPath);
            var payload = TryParseRoundsPayload(json);
            if (payload == null)
            {
                ToastHelper.Warn("无法解析录制作业内容");
                return;
            }

            // 尝试从 JSON 中提取作业名
            var loadedName = TryExtractLevelName(json) ?? Path.GetFileNameWithoutExtension(item.Name);

            var updatedAtUtc = DateTime.SpecifyKind(item.LastWriteTimeUtc, DateTimeKind.Utc);
            var triggeredAt = new DateTimeOffset(updatedAtUtc);
            LoadRoundsPayload(payload, triggeredAt);

            // 设置加载状态
            RecordingName = loadedName;
            _originalRecordingName = loadedName;
            CurrentLoadedFilePath = item.FullPath;
            IsDirty = false;
        }
        catch (Exception ex)
        {
            LoggerHelper.Error(ex);
            ToastHelper.Error("加载录制作业失败");
        }
        finally
        {
            _selectionLoadLock.Release();
        }
    }

    private static string? TryExtractLevelName(string json)
    {
        try
        {
            var root = JObject.Parse(json);
            if (root.TryGetValue("level_name", StringComparison.OrdinalIgnoreCase, out var nameToken))
            {
                var name = nameToken.Value<string>();
                if (!string.IsNullOrWhiteSpace(name))
                    return name;
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    private void LoadRoundsPayload(Dictionary<string, List<List<string>>> payload, DateTimeOffset triggeredAt)
    {
        _roundSteps.Clear();

        var maxRound = 1;
        foreach (var key in payload.Keys)
        {
            if (int.TryParse(key, out var round) && round > maxRound)
                maxRound = round;
        }

        if (maxRound < 1)
            maxRound = 1;

        RoundCount = maxRound;

        for (var round = 1; round <= maxRound; round++)
        {
            _roundSteps[round] = new List<RecordedStepData>();
            if (!payload.TryGetValue(round.ToString(), out var steps) || steps == null)
                continue;

            foreach (var stepTokens in steps)
            {
                var actionName = stepTokens?.FirstOrDefault();
                if (string.IsNullOrWhiteSpace(actionName))
                    continue;

                _roundSteps[round].Add(new RecordedStepData(FromSavedStepToken(actionName), triggeredAt));
            }
        }

        CurrentRound = 1;
        RefreshRecordedStepsForCurrentRound();
    }

    private static Dictionary<string, List<List<string>>>? TryParseRoundsPayload(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            var direct = JsonConvert.DeserializeObject<Dictionary<string, List<List<string>>>>(json);
            if (direct != null && direct.Count > 0)
                return direct;
        }
        catch
        {
            // ignore
        }

        try
        {
            var root = JObject.Parse(json);
            if (root.TryGetValue("actions", StringComparison.OrdinalIgnoreCase, out var actionsToken))
                return actionsToken.ToObject<Dictionary<string, List<List<string>>>>();
            if (root.TryGetValue("rounds", StringComparison.OrdinalIgnoreCase, out var roundsToken))
                return roundsToken.ToObject<Dictionary<string, List<List<string>>>>();
        }
        catch
        {
            // ignore
        }

        return null;
    }

    private void EnsureRoundExists(int round)
    {
        if (!_roundSteps.ContainsKey(round))
            _roundSteps[round] = new List<RecordedStepData>();
    }

    private int GetTotalRecordedStepCount() => _roundSteps.Values.Sum(static s => s.Count);

    private void UpdateCanSave()
    {
        CanSave = GetTotalRecordedStepCount() > 0;
        OnPropertyChanged(nameof(RoundDisplay));
    }

    partial void OnCurrentRoundChanged(int value)
    {
        OnPropertyChanged(nameof(RoundDisplay));
    }

    partial void OnRoundCountChanged(int value)
    {
        OnPropertyChanged(nameof(RoundDisplay));
    }

    partial void OnRecordingNameChanged(string value)
    {
        // 方案 A：作业名修改触发 IsDirty（仅在加载已保存录制后才检测）
        if (!IsRecording && CurrentLoadedFilePath != null && _originalRecordingName != null)
        {
            if (!string.Equals(value, _originalRecordingName, StringComparison.Ordinal))
            {
                IsDirty = true;
            }
        }
    }

    private Dictionary<string, List<List<string>>> BuildRoundsPayload()
    {
        var payload = new Dictionary<string, List<List<string>>>(StringComparer.Ordinal);

        for (var round = 1; round <= RoundCount; round++)
        {
            var items = new List<List<string>>();
            if (_roundSteps.TryGetValue(round, out var steps) && steps.Count > 0)
            {
                items.Capacity = steps.Count;
                foreach (var step in steps)
                    items.Add([ToSavedStepToken(step.ActionName)]);
            }

            payload[round.ToString()] = items;
        }

        return payload;
    }

    private static string ToSavedStepToken(string actionName)
    {
        if (string.IsNullOrWhiteSpace(actionName))
            return string.Empty;

        if (string.Equals(actionName, "+回合", StringComparison.Ordinal))
            return string.Empty;

        if (actionName.StartsWith("额外:", StringComparison.Ordinal))
            return actionName;

        // 统一录制 token：内部按钮为 1A/1↑/1↓，保存时转为 1普/1大/1下（更贴近 export 示例）
        if (actionName.Length == 2 && actionName[0] is >= '1' and <= '5')
        {
            var pos = actionName[0];
            return actionName[1] switch
            {
                'A' => $"{pos}普",
                '↑' => $"{pos}大",
                '↓' => $"{pos}下",
                _ => actionName
            };
        }

        var idx = actionName.IndexOf("号位", StringComparison.Ordinal);
        if (idx > 0)
        {
            var posText = actionName[..idx];
            if (int.TryParse(posText, out var pos) && pos is >= 1 and <= 5)
            {
                if (actionName.Contains("普攻", StringComparison.Ordinal))
                    return $"{pos}普";
                if (actionName.Contains("上拉", StringComparison.Ordinal))
                    return $"{pos}大";
                if (actionName.Contains("下拉", StringComparison.Ordinal))
                    return $"{pos}下";
                if (actionName.Contains("大招", StringComparison.Ordinal) || actionName.Contains("大", StringComparison.Ordinal))
                    return $"{pos}大";
            }
        }

        return actionName;
    }

    private static string FromSavedStepToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return string.Empty;

        if (token.StartsWith("额外:", StringComparison.Ordinal))
            return token;

        if (token.Length == 2 && token[0] is >= '1' and <= '5')
        {
            var pos = token[0];
            return token[1] switch
            {
                '普' => $"{pos}A",
                '大' => $"{pos}↑",
                '下' => $"{pos}↓",
                '上' => $"{pos}↑", // 兼容旧文件
                _ => token
            };
        }

        return token;
    }

    private static JObject BuildSimingExportRequest(string levelName, Dictionary<string, List<List<string>>> roundsPayload)
    {
        // 说明：除 actions 外，其余字段为 export API 所需的补齐项；默认值参考用户提供的 curl 示例
        return new JObject
        {
            ["level_name"] = levelName ?? string.Empty,
            ["level_type"] = string.Empty,
            ["level_recognition_name"] = string.Empty,
            ["difficulty"] = string.Empty,
            ["cave_type"] = string.Empty,
            ["lantai_nav"] = string.Empty,
            ["attack_delay"] = "3000",
            ["ult_delay"] = "5000",
            ["defense_delay"] = "3000",
            ["actions"] = JToken.FromObject(roundsPayload)
        };
    }

    private sealed record SimingExportResult(string FileName, JObject Actions);

    private static async Task<SimingExportResult> CallSimingExportAsync(string requestJson)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        using var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
        using var resp = await http.PostAsync(SimingExportApiUrl, content);
        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
        {
            var snippet = body.Length > 512 ? body[..512] + "..." : body;
            throw new HttpRequestException($"simingapi/export failed: {(int)resp.StatusCode} {resp.StatusCode}; body={snippet}");
        }

        var root = JObject.Parse(body);
        var contentStr = root.Value<string>("content");
        if (string.IsNullOrWhiteSpace(contentStr))
            throw new InvalidOperationException("simingapi/export 返回缺少 content");

        var actionsToken = JToken.Parse(contentStr);
        if (actionsToken is not JObject actionsObj)
            throw new InvalidOperationException("simingapi/export content 不是 JSON 对象");

        var filename = root.Value<string>("filename") ?? string.Empty;
        return new SimingExportResult(filename, actionsObj);
    }

    private static JObject BuildCopilotCacheJobJson(string title, JObject actions)
    {
        var safeTitle = string.IsNullOrWhiteSpace(title) ? "录制作业" : title.Trim();
        var stageName = Path.GetFileNameWithoutExtension(SanitizeFileName(safeTitle));

        return new JObject
        {
            ["version"] = 3,
            ["stage_name"] = stageName,
            ["difficulty"] = 0,
            ["level_meta"] = new JObject
            {
                ["stage_id"] = stageName,
                ["level_id"] = $"record/{stageName}",
                ["name"] = safeTitle,
                ["cat_one"] = "录制",
                ["cat_two"] = safeTitle,
                ["cat_three"] = "无",
                ["width"] = 0,
                ["height"] = 0
            },
            ["doc"] = new JObject
            {
                ["title"] = safeTitle,
                ["details"] = safeTitle
            },
            ["opers"] = new JArray(),
            ["actions"] = actions
        };
    }

    private static string SanitizeJobFileName(string apiFileName, string fallbackBaseName)
    {
        var name = apiFileName;
        if (string.IsNullOrWhiteSpace(name) || string.Equals(name.Trim(), ".json", StringComparison.OrdinalIgnoreCase))
        {
            name = fallbackBaseName;
        }

        name = SanitizeFileName(name);
        if (!name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            name += ".json";

        return name;
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path))
            return path;

        var dir = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(dir))
            dir = ".";

        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);

        for (var i = 1; i <= 999; i++)
        {
            var candidate = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(candidate))
                return candidate;
        }

        return Path.Combine(dir, $"{name}-{DateTime.Now:yyyyMMdd-HHmmssfff}{ext}");
    }

    private static string SanitizeFileName(string name)
    {
        var invalids = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
            sb.Append(invalids.Contains(ch) ? '_' : ch);

        var result = sb.ToString().Trim().Trim('.');
        return string.IsNullOrWhiteSpace(result) ? "recording.json" : result;
    }
}

internal readonly record struct RecordedStepData(string ActionName, DateTimeOffset TriggeredAt);

public sealed class RecordedRoundGroupItem
{
    public RecordedRoundGroupItem(int round)
    {
        Round = round;
        Steps = new ObservableCollection<RecordedStepItem>();
    }

    public int Round { get; }
    public ObservableCollection<RecordedStepItem> Steps { get; }
    public string Header => $"回合 {Round}";
}

public sealed class RecordingFileItem
{
    public RecordingFileItem(string name, string fullPath, DateTime lastWriteTimeUtc)
    {
        Name = name;
        FullPath = fullPath;
        LastWriteTimeUtc = lastWriteTimeUtc;
    }

    public string Name { get; }
    public string FullPath { get; }
    public DateTime LastWriteTimeUtc { get; }
    public string UpdatedAtLocal => LastWriteTimeUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
}

public sealed class RecordedStepItem
{
    public RecordedStepItem(int round, int index, string actionName, DateTimeOffset triggeredAt)
    {
        Round = round;
        Index = index;
        ActionName = actionName;
        TriggeredAt = triggeredAt;
    }

    public RecordedStepItem(int index, string actionName, DateTimeOffset triggeredAt)
        : this(round: 0, index: index, actionName: actionName, triggeredAt: triggeredAt)
    {
    }

    public int Round { get; }
    public int Index { get; }
    public string ActionName { get; }
    public DateTimeOffset TriggeredAt { get; }
    public string TimeLocal => TriggeredAt.ToLocalTime().ToString("HH:mm:ss.fff");
}

public enum RecordedActionPillKind
{
    Attack,
    Up,
    Down,
    Other,
    Extra
}

public sealed class RecordedRoundTableRowItem
{
    public RecordedRoundTableRowItem(
        int round,
        int totalActions,
        IReadOnlyList<RecordedRoundTablePillItem> slot1,
        IReadOnlyList<RecordedRoundTablePillItem> slot2,
        IReadOnlyList<RecordedRoundTablePillItem> slot3,
        IReadOnlyList<RecordedRoundTablePillItem> slot4,
        IReadOnlyList<RecordedRoundTablePillItem> slot5,
        IReadOnlyList<RecordedRoundTablePillItem> extra)
    {
        Round = round;
        TotalActions = totalActions;
        Slot1 = slot1;
        Slot2 = slot2;
        Slot3 = slot3;
        Slot4 = slot4;
        Slot5 = slot5;
        Extra = extra;
    }

    public int Round { get; }
    public int TotalActions { get; }
    public string RoundTitle => Round.ToString();
    public string Summary => $"共 {TotalActions} 个动作";

    public IReadOnlyList<RecordedRoundTablePillItem> Slot1 { get; }
    public IReadOnlyList<RecordedRoundTablePillItem> Slot2 { get; }
    public IReadOnlyList<RecordedRoundTablePillItem> Slot3 { get; }
    public IReadOnlyList<RecordedRoundTablePillItem> Slot4 { get; }
    public IReadOnlyList<RecordedRoundTablePillItem> Slot5 { get; }

    public IReadOnlyList<RecordedRoundTablePillItem> Extra { get; }
    public bool HasExtra => Extra.Count > 0;
}

public sealed class RecordedRoundTablePillItem
{
    private static readonly IBrush AttackBrush = new SolidColorBrush(Color.Parse("#F59E0B"));
    private static readonly IBrush AttackBg = new SolidColorBrush(Color.Parse("#14F59E0B"));
    private static readonly IBrush UpBrush = new SolidColorBrush(Color.Parse("#EF4444"));
    private static readonly IBrush UpBg = new SolidColorBrush(Color.Parse("#14EF4444"));
    private static readonly IBrush DownBrush = new SolidColorBrush(Color.Parse("#3B82F6"));
    private static readonly IBrush DownBg = new SolidColorBrush(Color.Parse("#143B82F6"));
    private static readonly IBrush OtherBrush = new SolidColorBrush(Color.Parse("#94A3B8"));
    private static readonly IBrush OtherBg = new SolidColorBrush(Color.Parse("#1494A3B8"));

    public RecordedRoundTablePillItem(string text, RecordedActionPillKind kind)
    {
        Text = text;
        Kind = kind;
    }

    public string Text { get; }
    public RecordedActionPillKind Kind { get; }

    public IBrush BorderBrush => Kind switch
    {
        RecordedActionPillKind.Attack => AttackBrush,
        RecordedActionPillKind.Up => UpBrush,
        RecordedActionPillKind.Down => DownBrush,
        _ => OtherBrush
    };

    public IBrush Background => Kind switch
    {
        RecordedActionPillKind.Attack => AttackBg,
        RecordedActionPillKind.Up => UpBg,
        RecordedActionPillKind.Down => DownBg,
        _ => OtherBg
    };

    public IBrush Foreground => BorderBrush;
}

public sealed class ActionButtonItem
{
    public ActionButtonItem(string displayName, string token, ICommand command)
    {
        DisplayName = displayName;
        Token = token;
        Command = command;
    }

    public string DisplayName { get; }
    public string Token { get; }
    public ICommand Command { get; }
    public object CommandParameter => Token;
}

internal enum FightActionKind
{
    Click,
    Swipe,
    RecordOnly,
    PipelineTask
}

internal sealed class FightActionTemplate
{
    private static readonly Random Rng = new();

    private FightActionTemplate(
        FightActionKind kind,
        int[]? target,
        int[]? begin,
        int[]? end,
        int durationMs,
        string? taskName = null)
    {
        Kind = kind;
        Target = target;
        Begin = begin;
        End = end;
        DurationMs = durationMs;
        TaskName = taskName;
    }

    public FightActionKind Kind { get; }
    public int[]? Target { get; }
    public int[]? Begin { get; }
    public int[]? End { get; }
    public int DurationMs { get; }
    public string? TaskName { get; }

    public static FightActionTemplate Click(int[] target) =>
        new(FightActionKind.Click, target: target, begin: null, end: null, durationMs: 0);

    public static FightActionTemplate Swipe(int[] begin, int[] end, int durationMs) =>
        new(FightActionKind.Swipe, target: null, begin: begin, end: end, durationMs: durationMs);

    public static FightActionTemplate RecordOnly() =>
        new(FightActionKind.RecordOnly, target: null, begin: null, end: null, durationMs: 0);

    public static FightActionTemplate PipelineTask(string taskName) =>
        new(FightActionKind.PipelineTask, target: null, begin: null, end: null, durationMs: 0,
            taskName: taskName);

    public (int X, int Y) GetClickPoint()
    {
        if (Target is not { Length: 4 })
            throw new InvalidOperationException("Click action requires target=[x,y,w,h].");

        var x = Target[0];
        var y = Target[1];
        var w = Math.Max(1, Target[2]);
        var h = Math.Max(1, Target[3]);
        var px = x + Rng.Next(0, w);
        var py = y + Rng.Next(0, h);
        return (px, py);
    }

    public (int StartX, int StartY, int EndX, int EndY, int DurationMs) GetSwipeArgs()
    {
        if (Begin is not { Length: 4 } || End is not { Length: 4 })
            throw new InvalidOperationException("Swipe action requires begin/end=[x,y,w,h].");

        var (sx, sy) = PickPointInRect(Begin);
        var (ex, ey) = PickPointInRect(End);
        return (sx, sy, ex, ey, DurationMs <= 0 ? 800 : DurationMs);
    }

    public void ApplyTo(MaaNode node)
    {
        switch (Kind)
        {
            case FightActionKind.Click:
                node.Target = Target;
                break;
            case FightActionKind.Swipe:
                node.Begin = Begin;
                node.End = End;
                node.Duration = (uint)(DurationMs <= 0 ? 800 : DurationMs);
                break;
            case FightActionKind.RecordOnly:
                break;
        }
    }

    private static (int X, int Y) PickPointInRect(int[] rect)
    {
        var x = rect[0];
        var y = rect[1];
        var w = Math.Max(1, rect[2]);
        var h = Math.Max(1, rect[3]);
        return (x + w / 2, y + h / 2);
    }
}
