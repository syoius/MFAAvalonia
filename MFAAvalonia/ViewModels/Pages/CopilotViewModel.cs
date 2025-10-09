using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using MFAAvalonia.ViewModels.Other;
using MaaFramework.Binding;

namespace MFAAvalonia.ViewModels.Pages;

public partial class CopilotViewModel : ObservableObject
{
    private static string ResourceBase => MaaProcessor.ResourceBase;
    private static string PipelineDir => Path.Combine(ResourceBase, "pipeline");
    private static string CopilotActiveDir => Path.Combine(PipelineDir, "copilot");
    private static string CopilotCacheDir => Path.Combine(PipelineDir, "copilot-cache");

    [ObservableProperty]
    private ObservableCollection<CopilotFileItem> _files = new();

    [ObservableProperty]
    private CopilotFileItem? _selectedFile;

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private bool _hasSelection;

    [ObservableProperty]
    private string _secretCode = string.Empty;

    partial void OnSelectedFileChanged(CopilotFileItem? value)
    {
        HasSelection = value != null;
    }

    public void Initialize()
    {
        EnsureDirs();
        _ = RefreshAsync();
    }

    #region 右侧列（连接与日志）- 与 TaskQueue 右栏对齐
    // 说明：为复用 TaskQueue 的右侧列 UI，本 ViewModel 补齐必要的同名属性/命令，
    // 实现采取最小必要封装，直接转发到 Instances.TaskQueueViewModel，避免重复逻辑。

    // 显示控制（与主页一致）
    public int ShouldShow
    {
        get => Instances.TaskQueueViewModel.ShouldShow;
        set => Instances.TaskQueueViewModel.ShouldShow = value;
    }

    // 设备列表与当前设备
    public ObservableCollection<object> Devices
    {
        get => Instances.TaskQueueViewModel.Devices;
        set => Instances.TaskQueueViewModel.Devices = value;
    }

    public object? CurrentDevice
    {
        get => Instances.TaskQueueViewModel.CurrentDevice;
        set => Instances.TaskQueueViewModel.CurrentDevice = value;
    }

    // 控制器（ADB/Win32）
    public MaaControllerTypes CurrentController
    {
        get => Instances.TaskQueueViewModel.CurrentController;
        set => Instances.TaskQueueViewModel.CurrentController = value;
    }

    // 日志集合
    public ObservableCollection<LogItemViewModel> LogItemViewModels =>
        Instances.TaskQueueViewModel.LogItemViewModels;

    // 命令转发（与 TaskQueueViewModel 保持一致的名称，以满足 XAML 绑定）
    [RelayCommand]
    private void CustomAdb()
    {
        // 直接调用 TaskQueue 的同名方法，避免重复实现
        try { Instances.TaskQueueViewModel.CustomAdb(); }
        catch (Exception ex) { LoggerHelper.Error(ex); }
    }

    // 注意：本 ViewModel 已存在 [RelayCommand] RefreshAsync() → 生成 RefreshCommand。
    // 因此此处不再声明同名 Refresh() 命令以避免命名冲突。

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

    private static void EnsureDirs()
    {
        try
        {
            Directory.CreateDirectory(PipelineDir);
            Directory.CreateDirectory(CopilotActiveDir);
            Directory.CreateDirectory(CopilotCacheDir);
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"创建目录失败: {ex}");
        }
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        await Task.Run(() =>
        {
            try
            {
                EnsureDirs();
                var items = Directory.EnumerateFiles(CopilotCacheDir, "*.json", SearchOption.TopDirectoryOnly)
                    .Select(p => new FileInfo(p))
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .Select(f => CopilotFileItem.FromFileInfo(f))
                    .ToList();
                DispatcherHelper.RunOnMainThread(() =>
                {
                    Files.Clear();
                    foreach (var i in items) Files.Add(i);
                    Status = Files.Count == 0 ? "缓存为空，先导入作业 JSON 或使用神秘代码。" : $"共 {Files.Count} 个作业";
                });
            }
            catch (Exception ex)
            {
                LoggerHelper.Error(ex);
                DispatcherHelper.RunOnMainThread(() => Status = "扫描缓存失败");
            }
        });
    }

    public async Task ImportLocalJsonAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        EnsureDirs();
        try
        {
            var name = Path.GetFileName(path);
            var dest = UniquePath(Path.Combine(CopilotCacheDir, name));
            File.Copy(path, dest, false);
            ToastHelper.Success($"已导入：{Path.GetFileName(dest)}");
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            LoggerHelper.Error(ex);
            ToastHelper.Error("本地导入失败");
        }
    }

    public async Task ImportMysteryCodeAsync(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) code = SecretCode;
        if (string.IsNullOrWhiteSpace(code)) { ToastHelper.Warn("请输入神秘代码"); return; }
        EnsureDirs();
        try
        {
            var id = code.StartsWith("maa://", StringComparison.OrdinalIgnoreCase) ? code[6..] : code;
            id = id.Trim('/');
            if (id.Contains('/')) id = id.Split('/')[^1];
            var url = $"https://share-backend.maayuan.fun/copilot/get/{id}";
            using var http = new HttpClient();
            var json = await http.GetStringAsync(url);

            // 解析 data -> content -> actions
            JsonNode? root;
            try { root = JsonNode.Parse(json); }
            catch { ToastHelper.Error("返回体解析失败"); return; }

            // 安全获取 data
            JsonNode? data = null;
            if (root != null)
            {
                if (root is JsonObject) data = root["data"] ?? root["Data"];
                else if (root is JsonArray arr && arr.Count > 0 && arr[0] is JsonObject obj0) data = obj0["data"] ?? obj0["Data"];
            }

            // 安全获取 content
            JsonNode? content = null;
            if (data != null)
            {
                if (data is JsonObject) content = data["content"] ?? data["Content"];
                else if (data is JsonArray dArr && dArr.Count > 0 && dArr[0] is JsonObject dObj0) content = dObj0["content"] ?? dObj0["Content"];
            }
            else
            {
                // 某些后端可能直接返回 content 顶层
                if (root is JsonObject) content = root["content"] ?? root["Content"];
            }

            // 如果 content 是字符串，尝试再次解析
            if (content is JsonValue jv && jv.TryGetValue<string>(out var contentStr))
            {
                try { content = JsonNode.Parse(contentStr); }
                catch { /* ignore parse error */ }
            }

            // 如果 content 是数组，取第一个对象
            if (content is JsonArray cArr && cArr.Count > 0)
            {
                content = cArr[0];
            }

            // 获取 actions 节点
            JsonNode? actionsNode = null;
            if (content is JsonObject cObj)
            {
                actionsNode = cObj["actions"] ?? cObj["Actions"];
            }

            // 兜底：若没有 actions，但 content 本身是一个对象，且看起来就是作业 JSON，则直接使用 content
            if (actionsNode == null && content is JsonObject fallbackObj)
            {
                actionsNode = fallbackObj;
            }

            if (actionsNode == null)
            {
                var snippet = json.Length > 512 ? json[..512] + "..." : json;
                LoggerHelper.Error($"神秘代码返回格式不符，未找到 actions：\n{snippet}");
                ToastHelper.Error("未找到可用的作业数据（缺少 actions）");
                return;
            }

            var pretty = actionsNode.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            var file = Path.Combine(CopilotCacheDir, $"{id}-{DateTime.Now:yyyyMMddHHmmss}.json");
            await File.WriteAllTextAsync(file, pretty, new UTF8Encoding(false));
            ToastHelper.Success($"已从神秘代码导入：{Path.GetFileName(file)}");
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            LoggerHelper.Error(ex);
            ToastHelper.Error("神秘代码导入失败");
        }
    }

    public async Task LoadSelectedAsync()
    {
        if (SelectedFile == null) { ToastHelper.Warn("请选择要加载的作业"); return; }
        if (Instances.RootViewModel.IsRunning)
        {
            ToastHelper.Warn("任务运行中，停止后再加载");
            return;
        }
        EnsureDirs();
        try
        {
            // 清理 copilot 目录：仅保留 copilot_config.json，其余 *.json/*.jsonc 删除
            ClearCopilotActiveDir();
            var dest = Path.Combine(CopilotActiveDir, SelectedFile.Name);
            File.Copy(SelectedFile.FullPath, dest, true);
            // 重载资源
            var ok = MaaProcessor.ReloadResources();
            if (ok) ToastHelper.Success("已加载到资源并刷新");
            else ToastHelper.Error("资源重载失败");
        }
        catch (Exception ex)
        {
            LoggerHelper.Error(ex);
            ToastHelper.Error("加载失败");
        }
    }

    private static void ClearCopilotActiveDir()
    {
        try
        {
            if (!Directory.Exists(CopilotActiveDir)) return;
            foreach (var file in Directory.EnumerateFiles(CopilotActiveDir, "*.*", SearchOption.TopDirectoryOnly))
            {
                var ext = Path.GetExtension(file);
                var name = Path.GetFileName(file);
                if (ext.Equals(".json", StringComparison.OrdinalIgnoreCase) || ext.Equals(".jsonc", StringComparison.OrdinalIgnoreCase))
                {
                    if (!name.Equals("copilot_config.json", StringComparison.OrdinalIgnoreCase))
                    {
                        try { File.Delete(file); }
                        catch (Exception e) { LoggerHelper.Warning($"删除旧作业失败: {file} => {e.Message}"); }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LoggerHelper.Warning(ex);
        }
    }

    public async Task OpenCacheDirAsync()
    {
        try
        {
            EnsureDirs();
            using var p = new System.Diagnostics.Process();
            if (OperatingSystem.IsWindows()) { p.StartInfo.FileName = "explorer"; p.StartInfo.Arguments = CopilotCacheDir; }
            else if (OperatingSystem.IsMacOS()) { p.StartInfo.FileName = "open"; p.StartInfo.Arguments = CopilotCacheDir; }
            else { p.StartInfo.FileName = "xdg-open"; p.StartInfo.Arguments = CopilotCacheDir; }
            p.Start();
        }
        catch (Exception ex)
        {
            LoggerHelper.Error(ex);
        }
        await Task.CompletedTask;
    }

    public async Task PreviewSelectedAsync()
    {
        if (SelectedFile == null) return;
        try
        {
            var lines = new StringBuilder();
            using var sr = new StreamReader(SelectedFile.FullPath, Encoding.UTF8, true);
            int count = 0; string? line;
            while (count < 200 && (line = await sr.ReadLineAsync()) != null)
            {
                lines.AppendLine(line);
                count++;
            }
            LoggerHelper.Info(lines.ToString());
            ToastHelper.Info("已在日志中输出前200行");
        }
        catch (Exception ex)
        {
            LoggerHelper.Error(ex);
        }
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        int i = 1;
        string candidate;
        do { candidate = Path.Combine(dir, $"{name}({i++}){ext}"); } while (File.Exists(candidate));
        return candidate;
    }
}

public sealed class CopilotFileItem
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public required long Size { get; init; }
    public required DateTime Modified { get; init; }

    public string SizeText => FormatSize(Size);
    public string ModifiedText => Modified.ToString("yyyy-MM-dd HH:mm:ss");

    public static CopilotFileItem FromFileInfo(FileInfo f) => new()
    {
        Name = f.Name,
        FullPath = f.FullName,
        Size = f.Length,
        Modified = f.LastWriteTime
    };

    private static string FormatSize(long size)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double v = size; int i = 0;
        while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
        return $"{v:F1} {units[i]}";
    }
}
