using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using MFAAvalonia.Views.Windows;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
            ["1普"] = FightActionTemplate.Click(target: [56, 1060, 5, 5]),
            ["1大"] = FightActionTemplate.Swipe(begin: [77, 1060, 10, 1], end: [77, 670, 10, 1], durationMs: 800),
            ["1下"] = FightActionTemplate.Swipe(begin: [73, 1060, 1, 1], end: [73, 1258, 1, 1], durationMs: 800),
            ["2普"] = FightActionTemplate.Click(target: [180, 1060, 5, 5]),
            ["2大"] = FightActionTemplate.Swipe(begin: [220, 1060, 1, 1], end: [225, 668, 1, 1], durationMs: 800),
            ["2下"] = FightActionTemplate.Swipe(begin: [221, 1060, 1, 1], end: [221, 1251, 1, 1], durationMs: 800),
            ["3普"] = FightActionTemplate.Click(target: [357, 1060, 5, 5]),
            ["3大"] = FightActionTemplate.Swipe(begin: [357, 1060, 1, 1], end: [357, 714, 1, 1], durationMs: 800),
            ["3下"] = FightActionTemplate.Swipe(begin: [357, 1060, 1, 1], end: [357, 1237, 1, 1], durationMs: 800),
            ["4普"] = FightActionTemplate.Click(target: [496, 1060, 5, 5]),
            ["4大"] = FightActionTemplate.Swipe(begin: [496, 1060, 1, 1], end: [496, 679, 1, 1], durationMs: 800),
            ["4下"] = FightActionTemplate.Swipe(begin: [496, 1060, 1, 1], end: [496, 1258, 1, 1], durationMs: 800),
            ["5普"] = FightActionTemplate.Click(target: [646, 1060, 5, 5]),
            ["5大"] = FightActionTemplate.Swipe(begin: [646, 1060, 1, 1], end: [642, 700, 1, 1], durationMs: 800),
            ["5下"] = FightActionTemplate.Swipe(begin: [646, 1060, 1, 1], end: [646, 1258, 1, 1], durationMs: 800),
            ["左侧目标"] = FightActionTemplate.Click(target: [154, 648, 1, 1]),
            ["右侧目标"] = FightActionTemplate.Click(target: [603, 413, 18, 21]),
            ["吕布"] = FightActionTemplate.RecordOnly(),
            ["额外:史子眇sp"] = FightActionTemplate.RecordOnly(),

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
        "1A", "2A", "3A",
        "4A", "5A", "1↑",
        "2↑", "3↑", "4↑",
        "5↑", "1↓", "2↓",
        "3↓", "4↓", "5↓",
        "左侧目标", "右侧目标",
        "吕布",
        "额外:史子眇sp"
    ];

    private static string RecordingsDir => Path.Combine(MaaProcessor.Resource, "copilot-cache", "recordings");

    public ObservableCollection<RecordingFileItem> RecordingFiles { get; } = new();
    public ObservableCollection<RecordedStepItem> RecordedSteps { get; } = new();
    public ObservableCollection<ActionButtonItem> AvailableActions { get; } = new();

    [ObservableProperty] private RecordingFileItem? _selectedRecording;
    [ObservableProperty] private bool _isRecording;
    [ObservableProperty] private bool _canSave;
    [ObservableProperty] private string _recordingName = string.Empty;
    [ObservableProperty] private string _status = string.Empty;

    private readonly SemaphoreSlim _actionLock = new(1, 1);
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

    public RecordTaskViewModel()
    {
        RecordedSteps.CollectionChanged += (_, _) => UpdateCanSave();
        ResetRounds();
        UpdateCanSave();
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
            "左侧目标" => "左",
            "右侧目标" => "右",
            "额外:史子眇sp" => "史SP",
            _ => token
        };

    private static void EnsureDirs()
    {
        Directory.CreateDirectory(RecordingsDir);
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
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
        }
        catch (Exception ex)
        {
            LoggerHelper.Error(ex);
            ToastHelper.Error("刷新录制作业列表失败");
        }

        await Task.CompletedTask;
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

            var payload = BuildRoundsPayload();
            var json = JsonConvert.SerializeObject(
                payload,
                new JsonSerializerSettings
                {
                    Formatting = Formatting.Indented,
                    NullValueHandling = NullValueHandling.Ignore,
                    DefaultValueHandling = DefaultValueHandling.Ignore
                });

            await File.WriteAllTextAsync(path, json, new UTF8Encoding(false));
            ToastHelper.Success($"已保存：{fileName}");
            await RefreshAsync();
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
        SwitchRound(CurrentRound + 1);
    }

    private bool CanNextRound => CurrentRound < RoundCount;

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

        if (!_roundSteps.TryGetValue(CurrentRound, out var steps) || steps.Count == 0)
        {
            UpdateCanSave();
            return;
        }

        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            RecordedSteps.Add(new RecordedStepItem(i + 1, step.ActionName, step.TriggeredAt));
        }

        UpdateCanSave();
    }

    private void AppendStep(string actionName, DateTimeOffset triggeredAt)
    {
        EnsureRoundExists(CurrentRound);
        _roundSteps[CurrentRound].Add(new RecordedStepData(actionName, triggeredAt));
        RecordedSteps.Add(new RecordedStepItem(RecordedSteps.Count + 1, actionName, triggeredAt));
        UpdateCanSave();
    }

    private void ResetRounds()
    {
        _roundSteps.Clear();
        _roundSteps[1] = new List<RecordedStepData>();
        RoundCount = 1;
        CurrentRound = 1;
        RefreshRecordedStepsForCurrentRound();
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

        if (actionName.StartsWith("额外:", StringComparison.Ordinal))
            return actionName;

        var idx = actionName.IndexOf("号位", StringComparison.Ordinal);
        if (idx > 0)
        {
            var posText = actionName[..idx];
            if (int.TryParse(posText, out var pos) && pos is >= 1 and <= 5)
            {
                if (actionName.Contains("普攻", StringComparison.Ordinal))
                    return $"{pos}普";
                if (actionName.Contains("上拉", StringComparison.Ordinal))
                    return $"{pos}上";
                if (actionName.Contains("下拉", StringComparison.Ordinal))
                    return $"{pos}下";
                if (actionName.Contains("大招", StringComparison.Ordinal) || actionName.Contains("大", StringComparison.Ordinal))
                    return $"{pos}大";
            }
        }

        return actionName;
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
    public RecordedStepItem(int index, string actionName, DateTimeOffset triggeredAt)
    {
        Index = index;
        ActionName = actionName;
        TriggeredAt = triggeredAt;
    }

    public int Index { get; }
    public string ActionName { get; }
    public DateTimeOffset TriggeredAt { get; }
    public string TimeLocal => TriggeredAt.ToLocalTime().ToString("HH:mm:ss.fff");
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
    RecordOnly
}

internal sealed class FightActionTemplate
{
    private static readonly Random Rng = new();

    private FightActionTemplate(
        FightActionKind kind,
        int[]? target,
        int[]? begin,
        int[]? end,
        int durationMs)
    {
        Kind = kind;
        Target = target;
        Begin = begin;
        End = end;
        DurationMs = durationMs;
    }

    public FightActionKind Kind { get; }
    public int[]? Target { get; }
    public int[]? Begin { get; }
    public int[]? End { get; }
    public int DurationMs { get; }

    public static FightActionTemplate Click(int[] target) =>
        new(FightActionKind.Click, target: target, begin: null, end: null, durationMs: 0);

    public static FightActionTemplate Swipe(int[] begin, int[] end, int durationMs) =>
        new(FightActionKind.Swipe, target: null, begin: begin, end: end, durationMs: durationMs);

    public static FightActionTemplate RecordOnly() =>
        new(FightActionKind.RecordOnly, target: null, begin: null, end: null, durationMs: 0);

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
