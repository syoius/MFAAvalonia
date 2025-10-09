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

            var data = root?["data"] ?? root?["Data"];
            var content = data?["content"] ?? data?["Content"];
            JsonNode? actionsNode = content?["actions"] ?? content?["Actions"];

            // 某些情况下 content 可能是字符串包裹的 JSON
            if (actionsNode == null && content is JsonValue jv && jv.TryGetValue<string>(out var contentStr))
            {
                try
                {
                    var croot = JsonNode.Parse(contentStr);
                    actionsNode = croot?["actions"] ?? croot?["Actions"];
                }
                catch { /* ignore */ }
            }

            if (actionsNode == null)
            {
                ToastHelper.Error("未找到 actions 字段");
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
