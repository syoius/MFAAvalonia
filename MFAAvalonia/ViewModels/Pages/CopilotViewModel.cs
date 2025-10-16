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
    private const string DefaultCopilotTaskName = "✨ 自动抄作业V3";

    private static string ResourceBase => MaaProcessor.ResourceBase; // 简中基准资源，用于缓存与默认路径
    private static string PipelineDir => Path.Combine(ResourceBase, "pipeline"); // 基准 pipeline（用于兼容迁移等）

    private static string GetActiveResourceBase()
    {
        try
        {
            var name = Instances.TaskQueueViewModel.CurrentResource;
            var selected = Instances.TaskQueueViewModel.CurrentResources?.FirstOrDefault(r => r.Name == name);
            var path = selected?.Path?.FirstOrDefault();
            return string.IsNullOrWhiteSpace(path) ? ResourceBase : path;
        }
        catch
        {
            return ResourceBase;
        }
    }

    private static string ActivePipelineDir => Path.Combine(GetActiveResourceBase(), "pipeline");
    private static string CopilotActiveDir => Path.Combine(ActivePipelineDir, "copilot");
    // 迁移：缓存目录移至 ResourceBase 根，避免引擎扫描 pipeline 下的缓存导致解析冲突
    private static string CopilotCacheDir => Path.Combine(ResourceBase, "copilot-cache");
    private static string OldCopilotCacheDir => Path.Combine(PipelineDir, "copilot-cache");

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

    [ObservableProperty]
    private string _activeJob = string.Empty;

    partial void OnSelectedFileChanged(CopilotFileItem? value)
    {
        HasSelection = value != null;
    }

    public void Initialize()
    {
        EnsureDirs();
        // 优先确保资源与任务源已加载（避免首次进入时任务列表为空）
        try { MaaProcessor.ReloadResources(); } catch { /* ignore */ }
        _ = RefreshAsync();
        _ = EnsureDefaultTaskSelectedAsync();
        _ = UpdateActiveJobFromDiskAsync();
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

    // GridSplitter 拖拽指令（与 TaskQueue 保持一致命名，供 XAML behaviors 绑定）
    [RelayCommand]
    public void GridSplitterDragStarted(string splitterName)
    {
        try { Instances.TaskQueueViewModel.GridSplitterDragStartedCommand?.Execute(splitterName); }
        catch (Exception ex) { LoggerHelper.Error(ex); }
    }

    [RelayCommand]
    public void GridSplitterDragCompleted(string splitterName)
    {
        try { Instances.TaskQueueViewModel.GridSplitterDragCompletedCommand?.Execute(splitterName); }
        catch (Exception ex) { LoggerHelper.Error(ex); }
    }
    #endregion

    private static void EnsureDirs()
    {
        try
        {
            Directory.CreateDirectory(ActivePipelineDir);
            Directory.CreateDirectory(CopilotActiveDir);
            Directory.CreateDirectory(CopilotCacheDir);

            // 迁移：将 pipeline/copilot-cache 挪到 resource/base/copilot-cache，避免被底层引擎当作 pipeline 解析
            if (Directory.Exists(OldCopilotCacheDir))
            {
                try
                {
                    foreach (var file in Directory.EnumerateFiles(OldCopilotCacheDir, "*.json", SearchOption.TopDirectoryOnly))
                    {
                        var dest = Path.Combine(CopilotCacheDir, Path.GetFileName(file));
                        if (!File.Exists(dest))
                        {
                            File.Move(file, dest);
                        }
                        else
                        {
                            // 已存在则保留较新的
                            var srcInfo = new FileInfo(file);
                            var dstInfo = new FileInfo(dest);
                            if (srcInfo.LastWriteTimeUtc > dstInfo.LastWriteTimeUtc)
                            {
                                File.Copy(file, dest, true);
                            }
                            File.Delete(file);
                        }
                    }
                    // 移除旧目录
                    Directory.Delete(OldCopilotCacheDir, true);
                    LoggerHelper.Info("已迁移 copilot-cache 至 ResourceBase，避免引擎解析缓存");
                }
                catch (Exception e)
                {
                    LoggerHelper.Warning($"迁移 copilot-cache 失败: {e.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"创建目录失败: {ex}");
        }
    }

    /// <summary>
    /// 将主页任务选择固定为“✨ 自动抄作业V3”，并取消勾选其余任务。
    /// </summary>
    private async Task EnsureDefaultTaskSelectedAsync()
    {
        try
        {
            // 若任务正在运行，避免修改选择
            if (Instances.RootViewModel.IsRunning)
                return;

            // 确保任务源存在
            MaaProcessor.Instance.InitializeData();

            var vm = Instances.TaskQueueViewModel;
            var items = vm.TaskItemViewModels;
            if (items == null || items.Count == 0)
                return;

            // 先全部取消勾选
            foreach (var it in items)
                it.IsCheckedWithNull = false;

            // 按名称匹配（兼容已本地化的名称）
            var target = items.FirstOrDefault(i => string.Equals(i.Name, DefaultCopilotTaskName, StringComparison.OrdinalIgnoreCase))
                         ?? items.FirstOrDefault(i => string.Equals(i.InterfaceItem?.Name, DefaultCopilotTaskName, StringComparison.OrdinalIgnoreCase));

            if (target != null)
            {
                target.IsCheckedWithNull = true; // 三态为 true
                // 默认展开设置（若在主页查看），这里不强依赖面板，仅保证配置为该任务
                vm.ShowSettings = false;
                await Task.CompletedTask;
                return;
            }

            // 未命中则写日志但不抛异常，避免阻塞界面
            LoggerHelper.Warning($"Copilot: 未找到默认任务 '{DefaultCopilotTaskName}'");
        }
        catch (Exception ex)
        {
            LoggerHelper.Warning($"设置默认任务失败: {ex.Message}");
        }

        await Task.CompletedTask;
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
                _ = UpdateActiveJobFromDiskAsync();
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
            var id = code.StartsWith("maay://", StringComparison.OrdinalIgnoreCase) ? code[6..] : code;
            id = id.Trim('/');
            if (id.Contains('/')) id = id.Split('/')[^1];
            var url = $"https://share.maayuan.top/api/copilot/get/{id}";
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

            // 提取 doc.title / doc.details / level_meta，并以 title 命名保存完整 content 为主作业 JSON
            if (content is not JsonObject contentObj)
            {
                var snippet = json.Length > 512 ? json[..512] + "..." : json;
                LoggerHelper.Error($"神秘代码返回格式不符，未找到 content 对象：\n{snippet}");
                ToastHelper.Error("未找到可用的作业数据（content）");
                return;
            }

            string? title = null;
            string? details = null;
            JsonNode? levelMeta = null;
            if (contentObj["doc"] is JsonObject docObj)
            {
                if (docObj["title"] is JsonValue t && t.TryGetValue<string>(out var tStr)) title = tStr;
                if (docObj["details"] is JsonValue d && d.TryGetValue<string>(out var dStr)) details = dStr;
            }
            levelMeta = contentObj["level_meta"] ?? contentObj["levelMeta"];

            // 规范化文件名：保留中文与空格，替换非法字符
            string fileName = string.IsNullOrWhiteSpace(title) ? $"{id}-{DateTime.Now:yyyyMMddHHmmss}.json" : title;
            fileName = SanitizeFileName(fileName);
            if (!fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) fileName += ".json";

            var prettyContent = contentObj.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });

            var mainPath = UniquePath(Path.Combine(CopilotCacheDir, fileName));
            await File.WriteAllTextAsync(mainPath, prettyContent, new UTF8Encoding(false));

            // 不再额外写入 info 缓存文件（id/details/level_meta），避免重复与冗余。

            ToastHelper.Success($"已从神秘代码导入：{Path.GetFileName(mainPath)}");
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            LoggerHelper.Error(ex);
            ToastHelper.Error("神秘代码导入失败");
        }
    }

    // 替换文件名中的非法字符
    private static string SanitizeFileName(string name)
    {
        var invalids = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            sb.Append(invalids.Contains(ch) ? '_' : ch);
        }
        // 去除首尾空白与点
        var result = sb.ToString().Trim().Trim('.');
        return string.IsNullOrWhiteSpace(result) ? "job.json" : result;
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
            // 若缓存文件为“完整 content”，则仅将其中的 actions 写入引擎目录，避免不兼容的元字段（如 difficulty 数字）
            try
            {
                var raw = await File.ReadAllTextAsync(SelectedFile.FullPath, Encoding.UTF8);
                JsonNode? node = null;
                try { node = JsonNode.Parse(raw); } catch { node = null; }
                if (node is JsonObject rootObj && rootObj["actions"] is JsonObject actionsObj)
                {
                    var prettyActions = actionsObj.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
                    await File.WriteAllTextAsync(dest, prettyActions, new UTF8Encoding(false));
                }
                else
                {
                    // 兼容旧文件（直接为 actions 映射或其他合法格式）
                    await File.WriteAllTextAsync(dest, raw, new UTF8Encoding(false));
                }
            }
            catch
            {
                // 回退为直接复制
                File.Copy(SelectedFile.FullPath, dest, true);
            }
            // 重载资源
            var ok = MaaProcessor.ReloadResources();
            if (ok) ToastHelper.Success("已加载到资源并刷新");
            else ToastHelper.Error("资源重载失败");

            // 更新当前激活文本
            ActiveJob = SelectedFile.DisplayName;
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

    public async Task DeleteSelectedAsync()
    {
        if (SelectedFile == null)
        {
            ToastHelper.Warn("请选择要删除的作业");
            return;
        }

        try
        {
            var path = SelectedFile.FullPath;
            await Task.Run(() =>
            {
                try
                {
                    if (File.Exists(path)) File.Delete(path);
                }
                catch (Exception e)
                {
                    throw new IOException($"删除文件失败: {path}", e);
                }
            });

            await RefreshAsync();
            ToastHelper.Success("删除完成");
        }
        catch (Exception ex)
        {
            LoggerHelper.Error(ex);
            ToastHelper.Error("删除失败");
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

// 读取资源目录当前激活的作业文件名，转为展示名
partial class CopilotViewModel
{
    private async Task UpdateActiveJobFromDiskAsync()
    {
        try
        {
            string? activePath = null;
            if (Directory.Exists(CopilotActiveDir))
            {
                activePath = Directory.EnumerateFiles(CopilotActiveDir, "*.json", SearchOption.TopDirectoryOnly)
                    .Select(p => new FileInfo(p))
                    .Where(f => !string.Equals(f.Name, "copilot_config.json", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .FirstOrDefault()?.FullName;
            }

            if (string.IsNullOrWhiteSpace(activePath))
            {
                DispatcherHelper.RunOnMainThread(() => ActiveJob = "未加载");
                return;
            }

            var baseName = Path.GetFileNameWithoutExtension(activePath);
            var cacheCandidate = Path.Combine(CopilotCacheDir, baseName + ".json");
            string display = baseName;
            if (File.Exists(cacheCandidate))
            {
                try
                {
                    using var sr = new StreamReader(cacheCandidate, Encoding.UTF8, true);
                    var text = await sr.ReadToEndAsync();
                    var node = JsonNode.Parse(text) as JsonObject;
                    if (node != null && node["level_meta"] is JsonObject lm && lm["game"] is JsonValue gv && gv.TryGetValue<string>(out var game) && !string.IsNullOrWhiteSpace(game))
                    {
                        display = $"{game}-{baseName}";
                    }
                }
                catch { /* ignore */ }
            }

            DispatcherHelper.RunOnMainThread(() => ActiveJob = display);
        }
        catch
        {
            // ignore
        }
    }
}

public sealed class CopilotFileItem
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public required long Size { get; init; }
    public required DateTime Modified { get; init; }
    public required string DisplayName { get; init; }

    public string SizeText => FormatSize(Size);
    public string ModifiedText => Modified.ToString("yyyy-MM-dd HH:mm:ss");

    public static CopilotFileItem FromFileInfo(FileInfo f)
    {
        var baseName = Path.GetFileNameWithoutExtension(f.Name);
        string display = baseName;
        try
        {
            using var sr = new StreamReader(f.FullName, Encoding.UTF8, true);
            var text = sr.ReadToEnd();
            var node = JsonNode.Parse(text) as JsonObject;
            if (node != null && node["level_meta"] is JsonObject lm && lm["game"] is JsonValue gv && gv.TryGetValue<string>(out var game) && !string.IsNullOrWhiteSpace(game))
            {
                display = $"{game}-{baseName}";
            }
        }
        catch { /* ignore parse errors */ }

        return new CopilotFileItem
        {
            Name = f.Name,
            FullPath = f.FullName,
            Size = f.Length,
            Modified = f.LastWriteTime,
            DisplayName = display
        };
    }

    private static string FormatSize(long size)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double v = size; int i = 0;
        while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
        return $"{v:F1} {units[i]}";
    }
}
