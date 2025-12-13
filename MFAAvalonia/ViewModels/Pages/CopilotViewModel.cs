using System;
using System.Collections.Generic;
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
using MFAAvalonia.Helper.ValueType;
using MaaFramework.Binding;
using Avalonia;
using Avalonia.Threading;
using Avalonia.Controls.ApplicationLifetimes;
using System.Diagnostics;

namespace MFAAvalonia.ViewModels.Pages;

public partial class CopilotViewModel : ObservableObject
{
    private const string DefaultCopilotTaskName = "✨ 自动抄作业V3";
    // 主备域：当主域不可用时自动回退到备域
    private static readonly string SharePrimaryBase = "https://share.maayuan.top";
    private static readonly string ShareBackupBase = "https://share-backend.maayuan.fun:16666";

    private static string ResourceRoot => MaaProcessor.Resource; // 资源根目录（与 base/pipeline 同级）
    private static string ResourceBase => MaaProcessor.ResourceBase; // 简中基准资源，用于缓存与默认路径
    private static string PipelineDir => Path.Combine(ResourceBase, "pipeline"); // 基准 pipeline（用于兼容迁移等）

    private static string GetActiveResourceBase()
    {
        try
        {
            var name = Instances.TaskQueueViewModel.CurrentResource;
            var selected = Instances.TaskQueueViewModel.CurrentResources?.FirstOrDefault(r => r.Name == name);

            // 优先使用解析后的路径，避免出现占位符目录
            var resolved = selected?.ResolvedPath?.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p));
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                return resolved!;
            }

            // 回退到原始 Path，并标准化 {PROJECT_DIR} 等占位符
            var rawPath = selected?.Path?.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p));
            if (!string.IsNullOrWhiteSpace(rawPath))
            {
                var normalized = MaaInterface.ReplacePlaceholder(rawPath, AppContext.BaseDirectory);
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    return normalized;
                }
            }

            return ResourceBase;
        }
        catch
        {
            return ResourceBase;
        }
    }

    private static string ActivePipelineDir => Path.Combine(GetActiveResourceBase(), "pipeline");
    private static string CopilotActiveDir => Path.Combine(ActivePipelineDir, "copilot");
    // 迁移：缓存目录移至资源根，避免引擎扫描 pipeline/base 下的缓存导致解析冲突
    private static string CopilotCacheDir => Path.Combine(ResourceRoot, "copilot-cache");
    private static string LegacyBaseCopilotCacheDir => Path.Combine(ResourceBase, "copilot-cache");
    private static string LegacyPipelineCopilotCacheDir => Path.Combine(PipelineDir, "copilot-cache");

    [ObservableProperty]
    private ObservableCollection<CopilotTreeItem> _fileTree = new();

    [ObservableProperty]
    private CopilotTreeItem? _selectedNode;

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

    [ObservableProperty]
    private bool _canLike;

    partial void OnSelectedFileChanged(CopilotFileItem? value)
    {
        HasSelection = value != null;
        try { UpdateCanLikeFromSelection(); } catch { /* ignore */ }
    }

    partial void OnSelectedNodeChanged(CopilotTreeItem? value)
    {
        SelectedFile = value?.File;
    }

    public void Initialize()
    {
        EnsureDirs();
        _ = RefreshAsync();
        _ = UpdateActiveJobFromDiskAsync();
        try { UpdateCanLikeFromSelection(); } catch { /* ignore */ }
    }
    // 统一的主备域回退请求封装：按序尝试主域与备域，仅两者均失败时抛出异常
    private static async Task<string> GetStringWithFallbackAsync(HttpClient http, string relativePath)
    {
        // 确保相对路径以 '/'
        var path = relativePath.StartsWith('/') ? relativePath : "/" + relativePath;
        var urls = new[] { $"{SharePrimaryBase}{path}", $"{ShareBackupBase}{path}" };
        Exception? lastError = null;
        foreach (var url in urls)
        {
            try
            {
                using var resp = await http.GetAsync(url);
                if (resp.IsSuccessStatusCode)
                {
                    return await resp.Content.ReadAsStringAsync();
                }
                lastError = new HttpRequestException($"Request failed with status {(int)resp.StatusCode} {resp.StatusCode} for {url}");
            }
            catch (Exception ex)
            {
                lastError = ex;
                // 尝试下一个域名
            }
        }
        throw lastError ?? new Exception("Unknown error on fallback requests");
    }

    // 统一的主备域回退 POST：按序尝试主域与备域，任一成功（2xx）即返回
    private static async Task<bool> PostJsonWithFallbackAsync(HttpClient http, string relativePath, string json)
    {
        var path = relativePath.StartsWith('/') ? relativePath : "/" + relativePath;
        var urls = new[] { $"{SharePrimaryBase}{path}", $"{ShareBackupBase}{path}" };
        Exception? lastError = null;
        foreach (var url in urls)
        {
            try
            {
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var resp = await http.PostAsync(url, content);
                if (resp.IsSuccessStatusCode)
                {
                    return true;
                }
                lastError = new HttpRequestException($"Request failed with status {(int)resp.StatusCode} {resp.StatusCode} for {url}");
            }
            catch (Exception ex)
            {
                lastError = ex;
                // 尝试下一个域名
            }
        }
        if (lastError != null) throw lastError;
        return false;
    }

    // 选择可用的主/备域名，用于构造跳转链接（轻量探测）
    private static async Task<string> PickAvailableBaseAsync()
    {
        // 尝试快速探测主域是否可用，超时后回退到备域
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            using var resp = await http.GetAsync(SharePrimaryBase);
            if (resp.IsSuccessStatusCode)
            {
                return SharePrimaryBase;
            }
        }
        catch
        {
            // ignore and fallback
        }
        return ShareBackupBase;
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
        try { Instances.TaskQueueViewModel.CustomAdbCommand?.Execute(null); }
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

            MigrateLegacyCache(LegacyPipelineCopilotCacheDir, "pipeline");
            MigrateLegacyCache(LegacyBaseCopilotCacheDir, "ResourceBase");
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"创建目录失败: {ex}");
        }

        static void MigrateLegacyCache(string legacyDir, string originLabel)
        {
            if (!Directory.Exists(legacyDir))
            {
                return;
            }

            try
            {
                foreach (var file in Directory.EnumerateFiles(legacyDir, "*.json", SearchOption.TopDirectoryOnly))
                {
                    var dest = Path.Combine(CopilotCacheDir, Path.GetFileName(file));
                    if (!File.Exists(dest))
                    {
                        File.Move(file, dest);
                    }
                    else
                    {
                        var srcInfo = new FileInfo(file);
                        var dstInfo = new FileInfo(dest);
                        if (srcInfo.LastWriteTimeUtc > dstInfo.LastWriteTimeUtc)
                        {
                            File.Copy(file, dest, true);
                        }
                        File.Delete(file);
                    }
                }

                Directory.Delete(legacyDir, true);
                LoggerHelper.Info($"已迁移 copilot-cache（{originLabel}）至资源根目录，避免引擎解析缓存");
            }
            catch (Exception e)
            {
                LoggerHelper.Warning($"迁移 copilot-cache（{originLabel}）失败: {e.Message}");
            }
        }
    }

    /// <summary>
    /// 将主页任务选择固定为"✨ 自动抄作业V3"，并取消勾选其余任务。
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
    private Task ToggleCopilotTaskAsync()
    {
        try
        {
            if (Instances.RootViewModel.IsRunning)
            {
                Instances.TaskQueueViewModel.StopTask();
                return Task.CompletedTask;
            }

            var vm = Instances.TaskQueueViewModel;
            var items = vm.TaskItemViewModels;
            if (items == null || items.Count == 0)
            {
                ToastHelper.Error("默认任务列表为空，无法启动");
                return Task.CompletedTask;
            }

            var source = items.FirstOrDefault(i => string.Equals(i.Name, DefaultCopilotTaskName, StringComparison.OrdinalIgnoreCase))
                         ?? items.FirstOrDefault(i => string.Equals(i.InterfaceItem?.Name, DefaultCopilotTaskName, StringComparison.OrdinalIgnoreCase));
            if (source?.InterfaceItem == null)
            {
                ToastHelper.Error($"未找到默认任务：{DefaultCopilotTaskName}");
                return Task.CompletedTask;
            }

            var taskClone = source.Clone();
            var taskList = new List<DragItemViewModel> { taskClone };
            MaaProcessor.Instance.Start(taskList);
        }
        catch (Exception ex)
        {
            LoggerHelper.Error(ex);
            ToastHelper.Error("启动默认任务失败");
        }

        return Task.CompletedTask;
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        await Task.Run(() =>
        {
            try
            {
                EnsureDirs();
                int fileCount = 0;
                var nodes = BuildTree(CopilotCacheDir, ref fileCount);
                DispatcherHelper.RunOnMainThread(() =>
                {
                    FileTree.Clear();
                    foreach (var node in nodes) FileTree.Add(node);
                    Status = fileCount == 0 ? "缓存为空，先导入作业 JSON 或使用神秘代码。" : $"共 {fileCount} 个作业";
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

    private static List<CopilotTreeItem> BuildTree(string directory, ref int fileCount)
    {
        var result = new List<CopilotTreeItem>();
        if (!Directory.Exists(directory))
            return result;

        var subDirs = Directory.EnumerateDirectories(directory)
            .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase);

        foreach (var dir in subDirs)
        {
            var folder = new CopilotTreeItem
            {
                Name = Path.GetFileName(dir),
                FullPath = dir,
                IsFolder = true
            };
            var children = BuildTree(dir, ref fileCount);
            foreach (var child in children)
            {
                folder.Children.Add(child);
            }
            result.Add(folder);
        }

        var files = Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
            .Select(p => new FileInfo(p))
            .OrderByDescending(f => f.LastWriteTimeUtc);

        foreach (var file in files)
        {
            var item = CopilotFileItem.FromFileInfo(file);
            result.Add(new CopilotTreeItem
            {
                Name = Path.GetFileNameWithoutExtension(file.Name),
                FullPath = file.FullName,
                IsFolder = false,
                File = item
            });
            fileCount++;
        }

        return result;
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

    [RelayCommand]
    private async Task LikeAsync()
    {
        try
        {
            if (SelectedFile == null)
            {
                ToastHelper.Warn("请选择要点赞的作业");
                return;
            }

            // 从缓存 JSON 读取 id（神秘代码）
            JsonNode? root = null;
            try
            {
                var text = await File.ReadAllTextAsync(SelectedFile.FullPath, Encoding.UTF8);
                root = JsonNode.Parse(text);
            }
            catch
            {
                ToastHelper.Error("读取作业文件失败");
                return;
            }

            if (root is not JsonObject obj)
            {
                ToastHelper.Error("作业文件格式不正确");
                return;
            }

            // 兼容数字或字符串 id
            bool hasId = false;
            long idNum = 0;
            string? idStr = null;
            if (obj["id"] is JsonValue idVal)
            {
                if (idVal.TryGetValue<long>(out var asLong))
                {
                    hasId = true; idNum = asLong; idStr = null;
                }
                else if (idVal.TryGetValue<string>(out var asStr) && !string.IsNullOrWhiteSpace(asStr))
                {
                    hasId = true; idStr = asStr.Trim();
                    // 若是 maay:// 前缀，尽量提取数字部分
                    if (idStr.StartsWith("maay://", StringComparison.OrdinalIgnoreCase))
                    {
                        var tail = idStr.Substring("maay://".Length);
                        if (long.TryParse(tail, out var parsed))
                        {
                            idNum = parsed; idStr = null; // 优先数字
                        }
                    }
                }
            }

            if (!hasId)
            {
                ToastHelper.Warn("未找到作业 id（神秘代码）");
                return;
            }

            var payload = new JsonObject
            {
                ["rating"] = "Like"
            };
            if (idStr != null) payload["id"] = idStr;
            else payload["id"] = idNum;

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            var ok = await PostJsonWithFallbackAsync(http, "/api/copilot/rating", payload.ToJsonString());
            if (ok)
            {
                ToastHelper.Success("已点赞，感谢反馈！");
                // 点赞成功后，写入本地历史并禁用按钮
                string? key = idStr ?? idNum.ToString();
                try { LikeHistoryHelper.MarkLiked(key); } catch { /* ignore */ }
                CanLike = false;
            }
            else
            {
                ToastHelper.Warn("点赞失败，请稍后重试");
            }
        }
        catch (Exception ex)
        {
            LoggerHelper.Warning(ex);
            ToastHelper.Error("点赞失败");
        }
    }

    private void UpdateCanLikeFromSelection()
    {
        if (SelectedFile == null)
        {
            CanLike = false;
            return;
        }

        try
        {
            // 读取所选作业的 id，决定是否允许点赞
            var text = File.ReadAllText(SelectedFile.FullPath, Encoding.UTF8);
            var root = JsonNode.Parse(text) as JsonObject;
            if (root == null)
            {
                CanLike = HasSelection; // 无法解析则默认允许
                return;
            }

            long idNum = 0;
            string? idStr = null;
            if (root["id"] is JsonValue idVal)
            {
                if (idVal.TryGetValue<long>(out var asLong))
                {
                    idNum = asLong; idStr = null;
                }
                else if (idVal.TryGetValue<string>(out var asStr) && !string.IsNullOrWhiteSpace(asStr))
                {
                    idStr = asStr.Trim();
                    if (idStr.StartsWith("maay://", StringComparison.OrdinalIgnoreCase))
                    {
                        var tail = idStr.Substring("maay://".Length);
                        if (long.TryParse(tail, out var parsed))
                        {
                            idNum = parsed; idStr = null;
                        }
                    }
                }
            }

            string key = idStr ?? idNum.ToString();
            var liked = LikeHistoryHelper.HasLiked(key);
            CanLike = HasSelection && !liked;
        }
        catch
        {
            // 任何异常下不阻塞交互：保持默认允许点赞
            CanLike = HasSelection;
        }
    }

    [RelayCommand]
    private async Task OpenJobPageAsync()
    {
        try
        {
            if (SelectedFile == null)
            {
                ToastHelper.Warn("请选择要跳转的作业");
                return;
            }

            // 从缓存 JSON 读取 id（神秘代码）
            JsonNode? root = null;
            try
            {
                var text = await File.ReadAllTextAsync(SelectedFile.FullPath, Encoding.UTF8);
                root = JsonNode.Parse(text);
            }
            catch
            {
                ToastHelper.Error("读取作业文件失败");
                return;
            }

            if (root is not JsonObject obj)
            {
                ToastHelper.Error("作业文件格式不正确");
                return;
            }

            // 兼容数字或字符串 id
            bool hasId = false;
            long idNum = 0;
            string? idStr = null;
            if (obj["id"] is JsonValue idVal)
            {
                if (idVal.TryGetValue<long>(out var asLong))
                {
                    hasId = true; idNum = asLong; idStr = null;
                }
                else if (idVal.TryGetValue<string>(out var asStr) && !string.IsNullOrWhiteSpace(asStr))
                {
                    hasId = true; idStr = asStr.Trim();
                    if (idStr.StartsWith("maay://", StringComparison.OrdinalIgnoreCase))
                    {
                        var tail = idStr.Substring("maay://".Length);
                        if (long.TryParse(tail, out var parsed))
                        {
                            idNum = parsed; idStr = null;
                        }
                    }
                }
            }

            if (!hasId)
            {
                ToastHelper.Warn("未找到作业 id（神秘代码）");
                return;
            }

            var op = idStr ?? idNum.ToString();
            var baseUrl = await PickAvailableBaseAsync();
            var url = $"{baseUrl}/?op={Uri.EscapeDataString(op)}";

            try
            {
                // 调用系统默认浏览器打开
                var psi = new ProcessStartInfo(url)
                {
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                LoggerHelper.Error(ex);
                ToastHelper.Error("无法打开浏览器");
            }
        }
        catch (Exception ex)
        {
            LoggerHelper.Warning(ex);
            ToastHelper.Error("跳转失败");
        }
    }

            public async Task ImportMysteryCodeAsync(string code, bool skipClipboard = false, string? targetDirectory = null)
    {
        if (!skipClipboard)
        {
            var clipboardCode = await TryReadSecretCodeFromClipboardAsync();
            if (!string.IsNullOrWhiteSpace(clipboardCode))
            {
                code = clipboardCode;
                if (!string.Equals(SecretCode, clipboardCode, StringComparison.Ordinal))
                {
                    SecretCode = clipboardCode;
                }
            }
            else if (string.IsNullOrWhiteSpace(code))
            {
                code = SecretCode;
            }
        }
        else if (!string.IsNullOrWhiteSpace(code) && !string.Equals(SecretCode, code, StringComparison.Ordinal))
        {
            SecretCode = code;
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            ToastHelper.Warn("神秘代码缺失");
            return;
        }

        if (!TryExtractCodeId(code, out var id))
        {
            ToastHelper.Warn("神秘代码格式不正确");
            return;
        }

        EnsureDirs();
        var destinationDir = string.IsNullOrWhiteSpace(targetDirectory) ? CopilotCacheDir : targetDirectory;
        Directory.CreateDirectory(destinationDir);
        try
        {
            using var http = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
            var json = await GetStringWithFallbackAsync(http, $"/api/copilot/get/{id}");

            JsonNode? root;
            try { root = JsonNode.Parse(json); }
            catch
            {
                ToastHelper.Error("神秘代码解析失败");
                return;
            }

            if (root?["status_code"] is JsonValue statusValue && statusValue.TryGetValue<int>(out var statusCode) && statusCode != 200)
            {
                var message = root?["message"] is JsonValue msgValue && msgValue.TryGetValue<string>(out var msg)
                    ? msg
                    : "神秘代码导入失败";
                if (statusCode == 400)
                {
                    ToastHelper.Warn($"{message}\n请确认该作业代码是否仍然有效");
                }
                else
                {
                    ToastHelper.Warn(message);
                }
                return;
            }

            JsonNode? data = null;
            if (root != null)
            {
                if (root is JsonObject) data = root["data"] ?? root["Data"];
                else if (root is JsonArray arr && arr.Count > 0 && arr[0] is JsonObject obj0) data = obj0["data"] ?? obj0["Data"];
            }

            JsonNode? content = null;
            if (data != null)
            {
                if (data is JsonObject) content = data["content"] ?? data["Content"];
                else if (data is JsonArray dArr && dArr.Count > 0 && dArr[0] is JsonObject dObj0) content = dObj0["content"] ?? dObj0["Content"];
            }
            else
            {
                if (root is JsonObject) content = root["content"] ?? root["Content"];
            }

            if (content is JsonValue jv && jv.TryGetValue<string>(out var contentStr))
            {
                try { content = JsonNode.Parse(contentStr); }
                catch { /* ignore parse error */ }
            }

            if (content is JsonArray cArr && cArr.Count > 0)
            {
                content = cArr[0];
            }

            if (content is not JsonObject contentObj)
            {
                var snippet = json.Length > 512 ? json[..512] + "..." : json;
                LoggerHelper.Error($"神秘代码返回格式异常，未找到 content 字段: {snippet}");
                ToastHelper.Error("未找到有效的作业数据（content）");
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

            // 附加需求：在导入的 JSON 中增加 id（去除 maay:// 的纯数字）与 tags（来自响应的 data.tags）
            try
            {
                // 写入 id：优先以数字写入，失败则写入字符串
                if (!string.IsNullOrWhiteSpace(id))
                {
                    if (long.TryParse(id, out var idNum))
                        contentObj["id"] = idNum;
                    else
                        contentObj["id"] = id;
                }

                // 写入 tags：从 data 节点提取（兼容大小写与数组/对象情况）
                JsonNode? tagsNode = null;
                if (data != null)
                {
                    if (data is JsonObject dObj)
                        tagsNode = dObj["tags"] ?? dObj["Tags"];
                    else if (data is JsonArray dArr && dArr.Count > 0 && dArr[0] is JsonObject dObj0)
                        tagsNode = dObj0["tags"] ?? dObj0["Tags"];
                }

                if (tagsNode != null)
                {
                    // 直接挂载即可；如为数组则保持原样
                    contentObj["tags"] = tagsNode;
                }
            }
            catch { /* 忽略增强字段写入失败，保持导入主流程 */ }

            string fileName = string.IsNullOrWhiteSpace(title) ? $"{id}-{DateTime.Now:yyyyMMddHHmmss}.json" : title;
            fileName = SanitizeFileName(fileName);
            if (!fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) fileName += ".json";

            var prettyContent = contentObj.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });

            var mainPath = UniquePath(Path.Combine(destinationDir, fileName));
            await File.WriteAllTextAsync(mainPath, prettyContent, new UTF8Encoding(false));

            ToastHelper.Success($"已下载神秘代码导入：{Path.GetFileName(mainPath)}");
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            LoggerHelper.Error(ex);
            ToastHelper.Error("神秘代码导入失败");
        }
    }

    public async Task ImportMysterySetAsync(string code)
    {
        var clipboardCode = await TryReadSecretCodeFromClipboardAsync();
        if (!string.IsNullOrWhiteSpace(clipboardCode))
        {
            code = clipboardCode;
            if (!string.Equals(SecretCode, clipboardCode, StringComparison.Ordinal))
            {
                SecretCode = clipboardCode;
            }
        }
        else if (string.IsNullOrWhiteSpace(code))
        {
            code = SecretCode;
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            ToastHelper.Warn("作业集代码缺失");
            return;
        }

        if (!TryExtractCodeId(code, out var id))
        {
            ToastHelper.Warn("作业集代码格式不正确");
            return;
        }

        try
        {
            using var http = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
            var json = await GetStringWithFallbackAsync(http, $"/api/set/get?id={id}");

            JsonNode? root;
            try { root = JsonNode.Parse(json); }
            catch
            {
                ToastHelper.Error("作业集响应解析失败");
                return;
            }

            int status = 200;
            bool hasStatus = false;
            if (root?["status_code"] is JsonValue statusValue && statusValue.TryGetValue<int>(out var statusCode))
            {
                status = statusCode;
                hasStatus = true;
            }

            if (hasStatus && status != 200)
            {
                var message = root?["message"] is JsonValue msgValue && msgValue.TryGetValue<string>(out var msg)
                    ? msg
                    : "作业集下载失败";
                if (status == 400)
                {
                    ToastHelper.Warn($"{message}\n请确认这是作业集代码，并在右键菜单中导入作业集。");
                }
                else
                {
                    ToastHelper.Warn(message);
                }
                return;
            }

            if (root?["data"] is not JsonObject data)
            {
                ToastHelper.Error("作业集数据缺失");
                return;
            }

            var ids = data["copilot_ids"] as JsonArray;
            if (ids == null || ids.Count == 0)
            {
                ToastHelper.Warn("该作业集中没有任何作业");
                return;
            }

            var name = data["name"] is JsonValue nameValue && nameValue.TryGetValue<string>(out var setName)
                ? setName
                : id;

            EnsureDirs();
            var folderName = SanitizeDirectoryName(name);
            var targetDir = Path.Combine(CopilotCacheDir, folderName);
            Directory.CreateDirectory(targetDir);

            var processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            int success = 0;

            foreach (var item in ids)

            {

                if (item is JsonValue value)

                {

                    string? entryId = null;

                    if (value.TryGetValue<int>(out var intId))

                        entryId = intId.ToString();

                    else if (value.TryGetValue<string>(out var strId))

                        entryId = strId;



                    if (!string.IsNullOrWhiteSpace(entryId) && processed.Add(entryId))

                    {

                        try

                        {

                            await ImportMysteryCodeAsync($"maay://{entryId}", skipClipboard: true, targetDirectory: targetDir);

                            success++;

                        }

                        catch (Exception ex)

                        {

                            LoggerHelper.Warning($"导入作业集条目失败: {entryId} => {ex.Message}");

                        }

                    }

                }

            }



            if (success > 0)
            {
                ToastHelper.Success($"已导入作业集 {name} 中的 {success} 个作业");
            }
            else
            {
                ToastHelper.Warn("未能导入作业集中的任何作业");
            }
        }
        catch (HttpRequestException ex)
        {
            LoggerHelper.Error(ex);
            ToastHelper.Error("作业集下载失败，请检查网络");
        }
        catch (Exception ex)
        {
            LoggerHelper.Error(ex);
            ToastHelper.Error("作业集导入失败");
        }
    }



        // 替换文件夹名中的非法字符
    private static string SanitizeDirectoryName(string name)
    {
        var invalids = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            sb.Append(invalids.Contains(ch) ? '_' : ch);
        }
        var result = sb.ToString().Trim().Trim('.');
        return string.IsNullOrWhiteSpace(result) ? "copilot-set" : result;
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
            // 若缓存文件为"完整 content"，则仅将其中的 actions 写入引擎目录，避免不兼容的元字段（如 difficulty 数字）
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

    public async Task UnloadActiveJobAsync()
    {
        try
        {
            var hasActiveJob = Directory.Exists(CopilotActiveDir) &&
                               Directory.EnumerateFiles(CopilotActiveDir, "*.*", SearchOption.TopDirectoryOnly)
                                   .Any(file =>
                                   {
                                       var ext = Path.GetExtension(file);
                                       var name = Path.GetFileName(file);
                                       var isJobFile = ext.Equals(".json", StringComparison.OrdinalIgnoreCase) ||
                                                       ext.Equals(".jsonc", StringComparison.OrdinalIgnoreCase);
                                       return isJobFile &&
                                              !name.Equals("copilot_config.json", StringComparison.OrdinalIgnoreCase);
                                   });

            if (!hasActiveJob)
            {
                ToastHelper.Warn("当前没有激活的作业");
                return;
            }

            ClearCopilotActiveDir();
            var reloadOk = MaaProcessor.ReloadResources();
            await UpdateActiveJobFromDiskAsync();

            if (reloadOk)
            {
                ToastHelper.Success("已卸载当前作业");
            }
            else
            {
                ToastHelper.Warn("已清空作业，但资源刷新失败");
            }
        }
        catch (Exception ex)
        {
            LoggerHelper.Error(ex);
            ToastHelper.Error("卸载失败");
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


    private static async Task<string?> TryReadSecretCodeFromClipboardAsync()
    {
        try
        {
            var clipboard = await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (Instances.RootView?.Clipboard != null)
                    return Instances.RootView.Clipboard;

                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                    return desktop.MainWindow?.Clipboard;

                return null;
            });

            if (clipboard == null)
                return null;

            var text = await clipboard.GetTextAsync();
            return NormalizeSecretCode(text);
        }
        catch (Exception ex)
        {
            LoggerHelper.Warning($"Copilot: 读取剪贴板失败: {ex.Message}");
            return null;
        }
    }

    private static string? NormalizeSecretCode(string? raw)
    {
        const string Prefix = "maay://";
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var candidate = raw.Trim();
        if (!candidate.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            return null;

        var id = candidate.Substring(Prefix.Length).Trim('/');
        if (string.IsNullOrWhiteSpace(id)) return null;
        if (id.Any(ch => !char.IsDigit(ch)))
            return null;

        return $"{Prefix}{id}";
    }



    private static bool TryExtractCodeId(string code, out string id)
    {
        id = string.Empty;
        if (string.IsNullOrWhiteSpace(code))
            return false;

        string working = code;
        if (code.StartsWith("maay://", StringComparison.OrdinalIgnoreCase))
            working = code[6..];

        working = working.Trim('/');
        if (string.IsNullOrWhiteSpace(working))
            return false;

        if (working.Contains('/'))
            working = working.Split('/')[^1];

        if (string.IsNullOrWhiteSpace(working))
            return false;

        id = working;
        return true;
    }
    public async Task DeleteSelectedAsync()
    {
        var node = SelectedNode;
        if (node == null)
        {
            ToastHelper.Warn("请选择要删除的作业");
            return;
        }

        try
        {
            // 若将要删除的目标包含当前激活作业，则先卸载
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

                if (!string.IsNullOrWhiteSpace(activePath))
                {
                    var activeBase = Path.GetFileNameWithoutExtension(activePath);
                    bool targetIncludesActive = false;

                    if (node.IsFile && node.File != null)
                    {
                        var baseName = Path.GetFileNameWithoutExtension(node.File.Name);
                        targetIncludesActive = string.Equals(baseName, activeBase, StringComparison.OrdinalIgnoreCase);
                    }
                    else if (Directory.Exists(node.FullPath))
                    {
                        targetIncludesActive = Directory.EnumerateFiles(node.FullPath, "*.json", SearchOption.AllDirectories)
                            .Any(f => string.Equals(Path.GetFileNameWithoutExtension(f), activeBase, StringComparison.OrdinalIgnoreCase));
                    }

                    if (targetIncludesActive)
                    {
                        await UnloadActiveJobAsync();
                    }
                }
            }
            catch (Exception e)
            {
                // 卸载判定失败不应阻断删除流程，仅记录告警
                LoggerHelper.Warning($"删除前卸载判定失败: {e.Message}");
            }

            string successMessage;
            if (node.IsFile && node.File != null)
            {
                var path = node.File.FullPath;
                await Task.Run(() =>
                {
                    try
                    {
                        if (File.Exists(path)) File.Delete(path);
                    }
                    catch (Exception e)
                    {
                        throw new IOException($"删除失败: {path}", e);
                    }
                });
                successMessage = "删除成功";
            }
            else
            {
                var path = node.FullPath;
                await Task.Run(() =>
                {
                    try
                    {
                        if (Directory.Exists(path)) Directory.Delete(path, true);
                    }
                    catch (Exception e)
                    {
                        throw new IOException($"删除失败: {path}", e);
                    }
                });
                successMessage = "删除成功";
            }

            SelectedNode = null;
            await RefreshAsync();
            ToastHelper.Success(successMessage);
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
            string display = baseName;
            string? cacheCandidate = null;
            try
            {
                cacheCandidate = Directory.EnumerateFiles(CopilotCacheDir, baseName + ".json", SearchOption.AllDirectories).FirstOrDefault();
            }
            catch
            {
                cacheCandidate = null;
            }

            if (!string.IsNullOrWhiteSpace(cacheCandidate) && File.Exists(cacheCandidate))
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

public sealed class CopilotTreeItem
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public bool IsFolder { get; init; }
    public ObservableCollection<CopilotTreeItem> Children { get; } = new();
    public CopilotFileItem? File { get; init; }

    public bool IsFile => File != null;
    public string DisplayName => File?.DisplayName ?? Name;
    public string? SizeText => File?.SizeText;
    public string? ModifiedText => File?.ModifiedText;
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

            // Prefix display name by tags rules:
            // - If tags contain "如鸢" and NOT contain "代号鸢" → add 【如鸢】
            // - Else if NOT contain "如鸢" but contain "代号鸢" → add 【代号鸢】
            // - Otherwise no prefix
            if (node != null && node["tags"] is JsonArray tagsArr)
            {
                bool hasRuYuan = tagsArr.Any(it =>
                {
                    if (it is JsonValue v && v.TryGetValue<string>(out var s))
                    {
                        s = s?.Trim();
                        return !string.IsNullOrEmpty(s) && s.Contains("如鸢", StringComparison.Ordinal);
                    }
                    return false;
                });

                bool hasDaiHaoYuan = tagsArr.Any(it =>
                {
                    if (it is JsonValue v && v.TryGetValue<string>(out var s))
                    {
                        s = s?.Trim();
                        return !string.IsNullOrEmpty(s) && s.Contains("代号鸢", StringComparison.Ordinal);
                    }
                    return false;
                });

                if (hasRuYuan && !hasDaiHaoYuan)
                {
                    display = $"【如鸢】{display}";
                }
                else if (!hasRuYuan && hasDaiHaoYuan)
                {
                    display = $"【代号鸢】{display}";
                }
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
