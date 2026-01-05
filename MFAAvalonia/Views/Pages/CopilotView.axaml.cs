using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using MFAAvalonia.ViewModels.Pages;
using Microsoft.Extensions.DependencyInjection;
using Avalonia.Markup.Xaml;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.Input;
using MFAAvalonia.Helper;
using MFAAvalonia.Helper.ValueType;
using MFAAvalonia.ViewModels.Other;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Data;
using Avalonia.Media;
using MFAAvalonia.Configuration;
using MFAAvalonia.Views.UserControls;
using Avalonia.Controls.Primitives;
using Avalonia.Xaml.Interactivity;
using Avalonia.Data.Converters;
using System.Text.RegularExpressions;
using System.Text;
using System.IO;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Extensions;
using Avalonia;
using AvaloniaExtensions.Axaml.Markup;
using Lang.Avalonia.MarkupExtensions;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Controls.Templates;
using SukiUI.MessageBox;
using SukiUI.Controls;

namespace MFAAvalonia.Views.Pages;

public partial class CopilotView : UserControl
{
    private const string DefaultCopilotIntro =
        "左上角导入作业文件，点击作业列表中的候选文件将自动激活该作业，\n" +
        "激活想抄的作业后即可点击开始任务。\n\n" +
        "不勾选“战斗中开始抄作业”时，需要在想打的关卡的编队界面（页面中有“进入战斗”按钮）处启动任务。\n\n" +
        "在“战斗中开始抄作业”需要展开战斗中手动/自动和倍速的那个面板。";
    private bool _isSelectionRefreshBusy;
    private bool _mysteryImportInProgress;
    private DateTime _lastMysteryImportUtc = DateTime.MinValue;
    private static readonly TimeSpan MysteryImportCooldown = TimeSpan.FromMilliseconds(500);
    // 用于区分最近一次指针按下是否为右键，用来抑制右键导致的 SelectionChanged 触发加载
    private bool _lastPointerWasRightClick;
    private DateTime _lastPointerEventTimeUtc = DateTime.MinValue;
    private int _currentLayoutMode = -1;
    private int _currentSelectorMode = -1;
    public CopilotView()
    {
        // 兜底：在编译的 XAML 未刷新时（--no-build），仍确保 DataContext 正确
        try { DataContext = MFAAvalonia.App.Services.GetRequiredService<CopilotViewModel>(); } catch { /* fallback to XAML */ }
        InitializeComponent();
        InitializeDeviceSelectorLayout();
        this.AttachedToVisualTree += (_, __) =>
        {
            (DataContext as CopilotViewModel)?.Initialize();
            // 稍后渲染默认任务的设置与说明
            Dispatcher.UIThread.Post(async () => await RenderDefaultTaskSettingsAsync(), DispatcherPriority.Background);
            // 选中即预览并重载
            TryHookSelectionChanged();
            TryHookPointerPressedForTree();
        };
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private async void OnImportLocalJson(object? sender, RoutedEventArgs e)
    {
        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top == null) return;
            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择作业 JSON",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("JSON") { Patterns = new[] { "*.json" } }
                }
            });
            if (files.Count == 0) return;
            await (DataContext as CopilotViewModel)!.ImportLocalJsonAsync(files[0].TryGetLocalPath());
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }

    private async void OnImportMysteryCode(object? sender, RoutedEventArgs e) =>
        await TryRunMysteryImportAsync(vm => vm.ImportMysteryCodeAsync(string.Empty));

    private async void OnImportMysteryCodePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Avalonia.Visual visual && e.GetCurrentPoint(visual).Properties.IsRightButtonPressed)
        {
            e.Handled = true;
            await TryRunMysteryImportAsync(vm => vm.ImportMysterySetAsync(string.Empty));
        }
    }

    private async void OnRefresh(object? sender, RoutedEventArgs e)
    {
        await (DataContext as CopilotViewModel)!.RefreshAsync();
    }

    private async void OnLoadSelected(object? sender, RoutedEventArgs e)
    {
        await (DataContext as CopilotViewModel)!.LoadSelectedAsync();
        // 加载完成后，尝试用所选作业的 details 渲染“任务说明”
        try { await RenderSelectedTaskDetailsAsync(); } catch { /* ignore */ }
    }

    private async void OnUnloadActiveJob(object? sender, RoutedEventArgs e)
    {
        try
        {
            var vm = DataContext as CopilotViewModel;
            if (vm == null) return;

            await vm.UnloadActiveJobAsync();
        }
        catch (Exception ex)
        {
            LoggerHelper.Error(ex);
            ToastHelper.Error("卸载失败");
        }
    }

    private async void OnOpenCacheDir(object? sender, RoutedEventArgs e)
    {
        await (DataContext as CopilotViewModel)!.OpenCacheDirAsync();
    }

    private async void OnPreview(object? sender, RoutedEventArgs e)
    {
        await (DataContext as CopilotViewModel)!.PreviewSelectedAsync();
    }

    private async void OnDeleteSelectedJobs(object? sender, RoutedEventArgs e)
    {
        try
        {
            var vm = DataContext as CopilotViewModel;
            if (sender is MenuItem menuItem && vm != null)
            {
                switch (menuItem.DataContext)
                {
                    case CopilotTreeItem treeItem:
                        vm.SelectedNode = treeItem;
                        break;
                    case CopilotFileItem fileItem:
                        vm.SelectedFile = fileItem;
                        break;
                }
            }

            if (vm?.SelectedNode == null)
            {
                ToastHelper.Warn("请选择要删除的作业或文件夹");
                return;
            }

            var target = vm.SelectedNode;
            var confirmText = target.IsFile
                ? "确认删除选中的作业？此操作不可恢复"
                : "确认删除选中的文件夹？将一并删除其中的所有作业";

            var result = await SukiMessageBox.ShowDialog(new SukiMessageBoxHost
            {
                Content = confirmText,
                ActionButtonsPreset = SukiMessageBoxButtons.YesNo,
                IconPreset = SukiMessageBoxIcons.Warning,
            }, new SukiMessageBoxOptions
            {
                Title = "删除确认",
            });

            if (result is not SukiMessageBoxResult.Yes)
                return;

            await vm.DeleteSelectedAsync();
        }
        catch (Exception ex)
        {
            LoggerHelper.Error(ex);
            ToastHelper.Error("删除失败");
        }
    }

    private async void OnOpenShareSite(object? sender, RoutedEventArgs e)
    {
        try
        {
            // 主域与备用域
            var primary = "https://share.maayuan.top/";
            var backup = "https://share.maayuan.fun:16666/";
            // 快速探测主域可用性（短超时，避免阻塞）
            var openUrl = primary;
            try
            {
                using var http = new System.Net.Http.HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(2)
                };
                using var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, primary);
                using var resp = await http.SendAsync(req, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
                if (!resp.IsSuccessStatusCode)
                {
                    openUrl = backup;
                }
            }
            catch
            {
                // 网络异常/超时：切换备用域
                openUrl = backup;
            }

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = openUrl,
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            MFAAvalonia.Helper.ToastHelper.Error("无法打开浏览器");
        }
        await Task.CompletedTask;
    }

    private void TryHookSelectionChanged()
    {
        try
        {
            var tree = this.FindControl<TreeView>("CopilotTree");
            if (tree == null) return;
            tree.SelectionChanged -= OnListSelectionChanged;
            tree.SelectionChanged += OnListSelectionChanged;
        }
        catch { /* ignore */ }
    }
    private void TryHookPointerPressedForTree()
    {
        try
        {
            var tree = this.FindControl<TreeView>("CopilotTree");
            if (tree == null) return;
            // 捕获指针按下以判断是否为右键，从而在 SelectionChanged 中抑制自动加载
            tree.AddHandler(InputElement.PointerPressedEvent, OnTreePointerPressed, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
        }
        catch { /* ignore */ }
    }
    private void OnTreePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        try
        {
            var visual = sender as Visual ?? this;
            var point = e.GetCurrentPoint(visual);
            _lastPointerWasRightClick = point.Properties.IsRightButtonPressed;
            _lastPointerEventTimeUtc = DateTime.UtcNow;
            // 不要设置 e.Handled，保持 ContextMenu 正常弹出
        }
        catch { /* ignore */ }
    }

    private async void OnListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isSelectionRefreshBusy) return;
                // 如果最近一次指针事件为右键（常见于右键打开上下文菜单时改变选中项），则不触发自动加载
        // 允许一个很短的时间窗口来判断该次 SelectionChanged 是否由右键导致
        if (_lastPointerWasRightClick && (DateTime.UtcNow - _lastPointerEventTimeUtc) < TimeSpan.FromSeconds(1))
        {
            // 重置标记，避免影响后续左键行为
            _lastPointerWasRightClick = false;
            return;
        }
        var tree = sender as TreeView ?? this.FindControl<TreeView>("CopilotTree");
        var vm = DataContext as CopilotViewModel;
        var node = e.AddedItems?.OfType<CopilotTreeItem>().FirstOrDefault() ?? tree?.SelectedItem as CopilotTreeItem;
        if (vm != null && node != null)
        {
            vm.SelectedNode = node;
        }

        // Clear visual selection immediately to avoid highlight flashes
        if (tree != null)
        {
            try { tree.SelectedItem = null; } catch { /* ignore */ }
        }

        if (vm?.SelectedFile == null)
            return;

        _isSelectionRefreshBusy = true;
        try
        {
            try { await RenderSelectedTaskDetailsAsync(); } catch { }
            try { await vm.LoadSelectedAsync(); } catch { }
        }
        finally
        {
            _isSelectionRefreshBusy = false;
        }
    }

    /// <summary>
    /// 对齐主页“连接”区域的动态布局逻辑，避免控件在不同宽度下重叠。
    /// </summary>
    private async Task RenderDefaultTaskSettingsAsync()
    {
        // 等待 Task 列表就绪
        for (int i = 0; i < 20; i++)
        {
            var items = Instances.TaskQueueViewModel.TaskItemViewModels;
            if (items != null && items.Count > 0)
                break;
            await Task.Delay(100);
        }

        try
        {
            var items = Instances.TaskQueueViewModel.TaskItemViewModels;
            if (items == null || items.Count == 0) return;

            // 仅获取“✨ 自动抄作业V3”
            var name = "✨ 自动抄作业V3";
            var dragItem = items.FirstOrDefault(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase))
                           ?? items.FirstOrDefault(i => string.Equals(i.InterfaceItem?.Name, name, StringComparison.OrdinalIgnoreCase));
            if (dragItem == null)
            {
                LoggerHelper.Warning($"Copilot: 未找到默认任务 '{name}' 用于渲染设置");
                return;
            }

            // 获取面板控件（容错）
            var commonPanel = CopilotCommonOptionSettings ?? this.FindControl<StackPanel>("CopilotCommonOptionSettings");
            var advancedPanel = CopilotAdvancedOptionSettings ?? this.FindControl<StackPanel>("CopilotAdvancedOptionSettings");
            var introView = CopilotIntroduction ?? this.FindControl<Markdown.Avalonia.MarkdownScrollViewer>("CopilotIntroduction");
            if (commonPanel == null || advancedPanel == null || introView == null)
            {
                LoggerHelper.Warning("Copilot: 任务设置容器未找到，跳过渲染");
                return;
            }

            // 清空面板
            commonPanel.Children.Clear();
            advancedPanel.Children.Clear();

            // 渲染通用与高级设置
            CopilotAddRepeatOption(commonPanel, dragItem);
            if (dragItem.InterfaceItem?.Option != null)
            {
                foreach (var option in dragItem.InterfaceItem.Option)
                {
                    CopilotAddOption(commonPanel, option, dragItem);
                }
            }
            if (dragItem.InterfaceItem?.Advanced != null)
            {
                foreach (var option in dragItem.InterfaceItem.Advanced)
                {
                    CopilotAddAdvancedOption(advancedPanel, option);
                }
            }

            // 是否显示“通用/高级”切换
            var hasAny = (dragItem.InterfaceItem?.Advanced?.Count > 0) == true || (dragItem.InterfaceItem?.Option?.Count > 0) == true || dragItem.InterfaceItem?.Repeatable == true;
            Instances.TaskQueueViewModel.ShowSettings = hasAny;
            Instances.TaskQueueViewModel.IsCommon = true;

            // 渲染说明：首次进入强制显示默认说明，不展示默认任务的 doc
            introView.Markdown = DefaultCopilotIntro;
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"Copilot 渲染任务设置失败: {ex.Message}");
        }
    }

    private async Task RenderSelectedTaskDetailsAsync()
    {
        var vm = DataContext as CopilotViewModel;
        if (vm?.SelectedFile == null) return;
        try
        {
            var introView = CopilotIntroduction ?? this.FindControl<Markdown.Avalonia.MarkdownScrollViewer>("CopilotIntroduction");
            if (introView == null) return;

            // 读取所选作业文件，解析关卡/密探/详情
            string details = string.Empty;
            string stageLine = string.Empty;
            string opersLine = string.Empty;
            using (var sr = new StreamReader(vm.SelectedFile.FullPath, Encoding.UTF8, true))
            {
                var text = await sr.ReadToEndAsync();
                try
                {
                    var node = System.Text.Json.Nodes.JsonNode.Parse(text) as System.Text.Json.Nodes.JsonObject;
                    if (node != null)
                    {
                        // details
                        if (node["doc"] is System.Text.Json.Nodes.JsonObject doc && doc["details"] is System.Text.Json.Nodes.JsonValue dv && dv.TryGetValue<string>(out var dstr))
                            details = dstr ?? string.Empty;

                        // 关卡行：关卡:{game}-{cat_one}-{cat_two}
                        string game = string.Empty, cat1 = string.Empty, cat2 = string.Empty;
                        if (node["level_meta"] is System.Text.Json.Nodes.JsonObject lm)
                        {
                            if (lm["game"] is System.Text.Json.Nodes.JsonValue gv && gv.TryGetValue<string>(out var g)) game = g ?? string.Empty;
                            if (lm["cat_one"] is System.Text.Json.Nodes.JsonValue c1v && c1v.TryGetValue<string>(out var c1)) cat1 = c1 ?? string.Empty;
                            if (lm["cat_two"] is System.Text.Json.Nodes.JsonValue c2v && c2v.TryGetValue<string>(out var c2)) cat2 = c2 ?? string.Empty;
                        }
                        stageLine = $"关卡:{game}-{cat1}-{cat2}".TrimEnd('-');

                        // 密探行：密探:{opers}
                        if (node["opers"] is System.Text.Json.Nodes.JsonArray opersArr)
                        {
                            var names = new System.Collections.Generic.List<string>();
                            foreach (var it in opersArr)
                            {
                                if (it is System.Text.Json.Nodes.JsonObject o && o["name"] is System.Text.Json.Nodes.JsonValue nv && nv.TryGetValue<string>(out var name) && !string.IsNullOrWhiteSpace(name))
                                    names.Add(name);
                            }
                            opersLine = names.Count > 0 ? $"密探:{string.Join("、", names)}" : string.Empty;
                        }
                    }
                }
                catch { /* ignore parse error */ }
            }

            // 当没有 details 时，仅显示默认说明；否则显示前缀 + 详情
            bool useDefault = string.IsNullOrWhiteSpace(details);
            if (useDefault) details = DefaultCopilotIntro;

            var sb = new StringBuilder();
            if (!useDefault)
            {
                if (!string.IsNullOrWhiteSpace(stageLine)) sb.Append(stageLine).Append("  \n\n");
                if (!string.IsNullOrWhiteSpace(opersLine)) sb.Append(opersLine).Append("  \n\n");
            }
            if (!string.IsNullOrWhiteSpace(details)) sb.Append(details);
            introView.Markdown = sb.ToString();
        }
        catch (Exception ex)
        {
            LoggerHelper.Warning($"渲染作业说明失败：{ex.Message}");
        }
    }

    private void CopilotSaveConfiguration()
    {
        ConfigurationManager.Current.SetValue(ConfigurationKeys.TaskItems,
            Instances.TaskQueueViewModel.TaskItemViewModels.Select(m => m.InterfaceItem));
    }

    private void CopilotAddRepeatOption(Panel panel, DragItemViewModel source)
    {
        if (source.InterfaceItem is not { Repeatable: true }) return;
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(7, GridUnitType.Star) },
                new ColumnDefinition { Width = new GridLength(4, GridUnitType.Star) }
            },
            Margin = new Thickness(8, 0, 5, 5)
        };

        var textBlock = new TextBlock
        {
            FontSize = 14,
            MinWidth = 180,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        Grid.SetColumn(textBlock, 0);
        textBlock.Bind(TextBlock.TextProperty, new I18nBinding("RepeatOption"));
        textBlock.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("SukiLowText"));
        grid.Children.Add(textBlock);
        var numericUpDown = new NumericUpDown
        {
            Value = source.InterfaceItem.RepeatCount ?? 1,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 150,
            Margin = new Thickness(0, 5, 5, 5),
            Increment = 1,
            Minimum = -1,
        };
        numericUpDown.Bind(IsEnabledProperty, new Binding("Idle") { Source = Instances.RootViewModel });
        numericUpDown.ValueChanged += (_, _) =>
        {
            source.InterfaceItem.RepeatCount = Convert.ToInt32(numericUpDown.Value);
            CopilotSaveConfiguration();
        };
        Grid.SetColumn(numericUpDown, 1);
        grid.SizeChanged += (sender, e) =>
        {
            if (sender is not Grid currentGrid) return;
            double totalMinWidth = currentGrid.Children.Sum(c => c.MinWidth);
            double availableWidth = currentGrid.Bounds.Width - currentGrid.Margin.Left - currentGrid.Margin.Right;
            if (availableWidth < totalMinWidth)
            {
                currentGrid.ColumnDefinitions.Clear();
                currentGrid.RowDefinitions.Clear();
                currentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                currentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid.SetRow(textBlock, 0);
                Grid.SetRow(numericUpDown, 1);
                Grid.SetColumn(textBlock, 0);
                Grid.SetColumn(numericUpDown, 0);
            }
            else
            {
                currentGrid.RowDefinitions.Clear();
                currentGrid.ColumnDefinitions.Clear();
                currentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(7, GridUnitType.Star) });
                currentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4, GridUnitType.Star) });
                Grid.SetRow(textBlock, 0);
                Grid.SetRow(numericUpDown, 0);
                Grid.SetColumn(textBlock, 0);
                Grid.SetColumn(numericUpDown, 1);
            }
        };
        grid.Children.Add(numericUpDown);
        panel.Children.Add(grid);
    }

    private void CopilotAddAdvancedOption(Panel panel, MaaInterface.MaaInterfaceSelectAdvanced option)
    {
        if (MaaProcessor.Interface?.Advanced?.TryGetValue(option.Name, out var interfaceOption) != true) return;
        for (int i = 0; interfaceOption.Field != null && i < interfaceOption.Field.Count; i++)
        {
            var field = interfaceOption.Field[i];
            var type = i < (interfaceOption.Type?.Count ?? 0) ? (interfaceOption.Type?[i] ?? "string") : (interfaceOption.Type?.Count > 0 ? interfaceOption.Type[0] : "string");
            string defaultValue = string.Empty;
            if (option.Data.TryGetValue(field, out var value)) defaultValue = value;
            else if (interfaceOption.Default != null && interfaceOption.Default.Count > i)
            {
                var defaultToken = interfaceOption.Default[i];
                if (defaultToken is Newtonsoft.Json.Linq.JArray arr)
                    defaultValue = arr.Count > 0 ? arr[0].ToString() : string.Empty;
                else defaultValue = defaultToken.ToString();
            }

            var grid = new Grid
            {
                ColumnDefinitions = { new ColumnDefinition { Width = new GridLength(7, GridUnitType.Star) }, new ColumnDefinition { Width = new GridLength(4, GridUnitType.Star) } },
                Margin = new Thickness(8, 0, 5, 5)
            };

            var autoCompleteBox = new AutoCompleteBox
            {
                MinWidth = 150,
                Margin = new Thickness(0, 5, 5, 5),
                Text = defaultValue,
                IsTextCompletionEnabled = true,
                FilterMode = AutoCompleteFilterMode.Custom,
                ItemFilter = (search, item) =>
                {
                    if (string.IsNullOrEmpty(search)) return true;
                    var itemText = item?.ToString() ?? string.Empty;
                    return itemText.IndexOf(search, StringComparison.InvariantCultureIgnoreCase) >= 0;
                },
            };
            autoCompleteBox.Bind(IsEnabledProperty, new Binding("Idle") { Source = Instances.RootViewModel });
            var completionItems = new List<string>();
            if (interfaceOption.Default != null && interfaceOption.Default.Count > i)
            {
                var defaultToken = interfaceOption.Default[i];
                if (defaultToken is Newtonsoft.Json.Linq.JArray arr)
                    completionItems = arr.Select(it => it.ToString()).ToList();
                else
                {
                    completionItems.Add(defaultToken.ToString());
                    completionItems.Add(string.Empty);
                }
                autoCompleteBox.ItemsSource = completionItems;
            }
            autoCompleteBox.TextChanged += (_, _) =>
            {
                if (type.ToLower() == "int")
                {
                    if (!IsValidIntegerInput(autoCompleteBox.Text))
                    {
                        autoCompleteBox.Text = FilterToInteger(autoCompleteBox.Text);
                        if (autoCompleteBox.CaretIndex > autoCompleteBox.Text.Length)
                            autoCompleteBox.CaretIndex = autoCompleteBox.Text.Length;
                    }
                }
                option.Data[field] = autoCompleteBox.Text;
                option.PipelineOverride = interfaceOption.GenerateProcessedPipeline(option.Data);
                CopilotSaveConfiguration();
            };
            autoCompleteBox.SelectionChanged += (_, _) =>
            {
                if (autoCompleteBox.SelectedItem is string selectedText)
                {
                    autoCompleteBox.Text = selectedText;
                    option.Data[field] = selectedText;
                    option.PipelineOverride = interfaceOption.GenerateProcessedPipeline(option.Data);
                    CopilotSaveConfiguration();
                }
            };
            Grid.SetColumn(autoCompleteBox, 1);

            var textBlock = new TextBlock
            {
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                Text = LanguageHelper.GetLocalizedString(field),
            };
            textBlock.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("SukiLowText"));
            var stackPanel = new StackPanel { MinWidth = 180, Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
            Grid.SetColumn(stackPanel, 0);
            stackPanel.Children.Add(textBlock);
            if (interfaceOption.Document is { Count: > 0 } && i < interfaceOption.Document.Count)
            {
                var doc = interfaceOption.Document[i];
                var input = doc;
                try { input = Regex.Unescape(doc); } catch { }
                var docBlock = new TooltipBlock { TooltipText = input };
                stackPanel.Children.Add(docBlock);
            }
            grid.Children.Add(autoCompleteBox);
            grid.Children.Add(stackPanel);
            grid.SizeChanged += (sender, e) =>
            {
                if (sender is not Grid currentGrid) return;
                var totalMinWidth = currentGrid.Children.Sum(c => c.MinWidth);
                var availableWidth = currentGrid.Bounds.Width;
                if (availableWidth < totalMinWidth)
                {
                    currentGrid.ColumnDefinitions.Clear();
                    currentGrid.RowDefinitions.Clear();
                    currentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    currentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    Grid.SetRow(stackPanel, 0);
                    Grid.SetRow(autoCompleteBox, 1);
                    Grid.SetColumn(stackPanel, 0);
                    Grid.SetColumn(autoCompleteBox, 0);
                }
                else
                {
                    currentGrid.RowDefinitions.Clear();
                    currentGrid.ColumnDefinitions.Clear();
                    currentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(7, GridUnitType.Star) });
                    currentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4, GridUnitType.Star) });
                    Grid.SetRow(stackPanel, 0);
                    Grid.SetRow(autoCompleteBox, 0);
                    Grid.SetColumn(stackPanel, 0);
                    Grid.SetColumn(autoCompleteBox, 1);
                }
            };
            panel.Children.Add(grid);
        }
    }

    private void CopilotAddOption(Panel panel, MaaInterface.MaaInterfaceSelectOption option, DragItemViewModel source)
    {
        if (MaaProcessor.Interface?.Option?.TryGetValue(option.Name ?? string.Empty, out var interfaceOption) != true) return;
        Control control = interfaceOption.Cases.ShouldSwitchButton(out var yes, out var no)
            ? CopilotCreateToggleControl(option, yes, no, interfaceOption, source)
            : CopilotCreateComboBoxControl(option, interfaceOption, source);
        panel.Children.Add(control);
    }

    private Grid CopilotCreateToggleControl(MaaInterface.MaaInterfaceSelectOption option, int yesValue, int noValue, MaaInterface.MaaInterfaceOption interfaceOption, DragItemViewModel source)
    {
        // 如果 option.Index 为 null，根据当前状态初始化
        // 开关默认显示为关闭状态（当 Index 为 null 时），所以应该设置为 noValue
        if (option.Index == null)
        {
            option.Index = noValue;
            CopilotSaveConfiguration();
        }
        
        var button = new ToggleSwitch
        {
            IsChecked = option.Index == yesValue,
            Classes = { "Switch" },
            MaxHeight = 60,
            MaxWidth = 100,
            HorizontalAlignment = HorizontalAlignment.Right,
            Tag = option.Name,
            VerticalAlignment = VerticalAlignment.Center
        };
        button.Bind(IsEnabledProperty, new Binding("Idle") { Source = Instances.RootViewModel });
        button.IsCheckedChanged += (_, _) => { option.Index = button.IsChecked == true ? yesValue : noValue; CopilotSaveConfiguration(); };
        button.SetValue(ToolTip.TipProperty, LanguageHelper.GetLocalizedString(option.Name));
        var textBlock = new TextBlock
        {
            Text = LanguageHelper.GetLocalizedString(option.Name),
            Margin = new Thickness(8, 0, 5, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        var grid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }, new ColumnDefinition { Width = GridLength.Auto } },
            Margin = new Thickness(0, 0, 0, 5)
        };
        var stackPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        stackPanel.Children.Add(textBlock);
        if (interfaceOption.Document is { Count: > 0 })
        {
            var input = Regex.Unescape(string.Join("\\n", interfaceOption.Document));
            var docBlock = new TooltipBlock { TooltipText = input };
            stackPanel.Children.Add(docBlock);
        }
        Grid.SetColumn(stackPanel, 0);
        Grid.SetColumn(button, 2);
        grid.Children.Add(stackPanel);
        grid.Children.Add(button);
        return grid;
    }

    private Grid CopilotCreateComboBoxControl(MaaInterface.MaaInterfaceSelectOption option, MaaInterface.MaaInterfaceOption interfaceOption, DragItemViewModel source)
    {
        // 如果 option.Index 为 null，初始化为默认值 0
        if (option.Index == null)
        {
            option.Index = 0;
            CopilotSaveConfiguration();
        }
        
        var grid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition { Width = new GridLength(7, GridUnitType.Star) }, new ColumnDefinition { Width = new GridLength(4, GridUnitType.Star), MinWidth = 180 } },
            Margin = new Thickness(8, 0, 5, 5)
        };
        var combo = new ComboBox
        {
            MinWidth = 170,
            Classes = { "LimitWidth" },
            Margin = new Thickness(0, 5, 5, 5),
            ItemsSource = interfaceOption.Cases?.Select(caseOption => new LocalizationViewModel { Name = caseOption.Name ?? "" }).ToList(),
            ItemTemplate = new FuncDataTemplate<LocalizationViewModel>((optionCase, b) =>
            {
                var data = new TextBlock { Text = optionCase?.Name ?? string.Empty, TextTrimming = TextTrimming.WordEllipsis, TextWrapping = TextWrapping.NoWrap };
                ToolTip.SetTip(data, optionCase?.Name ?? string.Empty);
                ToolTip.SetShowDelay(data, 100);
                return data;
            }),
            SelectionBoxItemTemplate = new FuncDataTemplate<LocalizationViewModel>((optionCase, b) =>
            {
                var data = new TextBlock { Text = optionCase?.Name ?? string.Empty, TextTrimming = TextTrimming.WordEllipsis, TextWrapping = TextWrapping.NoWrap };
                ToolTip.SetTip(data, optionCase?.Name ?? string.Empty);
                ToolTip.SetShowDelay(data, 100);
                return data;
            }),
            SelectedIndex = option.Index ?? 0,
        };
        combo.Bind(IsEnabledProperty, new Binding("Idle") { Source = Instances.RootViewModel });
        combo.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        combo.Padding = new Thickness(2, 0, 2, 0);
        combo.SelectionChanged += (_, _) => { option.Index = combo.SelectedIndex; CopilotSaveConfiguration(); };
        ComboBoxExtensions.SetDisableNavigationOnLostFocus(combo, true);
        Grid.SetColumn(combo, 1);
        var textBlock = new TextBlock
        {
            FontSize = 14,
            MinWidth = 150,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            Text = LanguageHelper.GetLocalizedString(option.Name),
        };
        textBlock.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("SukiLowText"));
        var stackPanel = new StackPanel { Orientation = Orientation.Horizontal, MinWidth = 180, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        Grid.SetColumn(stackPanel, 0);
        stackPanel.Children.Add(textBlock);
        if (interfaceOption.Document is { Count: > 0 })
        {
            var input = Regex.Unescape(string.Join("\\n", interfaceOption.Document));
            var docBlock = new TooltipBlock { TooltipText = input };
            stackPanel.Children.Add(docBlock);
        }
        grid.Children.Add(combo);
        grid.Children.Add(stackPanel);
        grid.SizeChanged += (sender, e) =>
        {
            if (sender is not Grid currentGrid) return;
            var totalMinWidth = currentGrid.Children.Sum(c => c.MinWidth);
            var availableWidth = currentGrid.Bounds.Width;
            if (availableWidth < totalMinWidth)
            {
                currentGrid.ColumnDefinitions.Clear();
                currentGrid.RowDefinitions.Clear();
                currentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                currentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid.SetRow(stackPanel, 0);
                Grid.SetRow(combo, 1);
                Grid.SetColumn(stackPanel, 0);
                Grid.SetColumn(combo, 0);
            }
            else
            {
                currentGrid.RowDefinitions.Clear();
                currentGrid.ColumnDefinitions.Clear();
                currentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(7, GridUnitType.Star) });
                currentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4, GridUnitType.Star) });
                Grid.SetRow(stackPanel, 0);
                Grid.SetRow(combo, 0);
                Grid.SetColumn(stackPanel, 0);
                Grid.SetColumn(combo, 1);
            }
        };
        return grid;
    }

    private bool IsValidIntegerInput(string text)
    {
        if (string.IsNullOrEmpty(text) || text == "-") return true;
        if (text.StartsWith("-") && (text.Length == 1 || (!char.IsDigit(text[1]) || text.LastIndexOf("-") != 0))) return false;
        for (int i = 0; i < text.Length; i++) { if (i == 0 && text[i] == '-') continue; if (!char.IsDigit(text[i])) return false; }
        return true;
    }
    private string FilterToInteger(string text)
    {
        string filtered = new string(text.Where(c => c == '-' || char.IsDigit(c)).ToArray());
        if (filtered.Contains('-')) { if (filtered[0] != '-' || filtered.Count(c => c == '-') > 1) filtered = filtered.Replace("-", ""); }
        if (string.IsNullOrEmpty(filtered) || filtered == "-") return filtered;
        if (filtered.Length > 1 && filtered[0] == '0') filtered = filtered.TrimStart('0');
        return filtered;
    }

    private void InitializeDeviceSelectorLayout()
    {
        var grid = ConnectionGrid ?? this.FindControl<Grid>("ConnectionGrid");
        var selectorPanel = DeviceSelectorPanel ?? this.FindControl<Grid>("DeviceSelectorPanel");
        var adbButton = AdbRadioButton ?? this.FindControl<RadioButton>("AdbRadioButton");
        var win32Button = Win32RadioButton ?? this.FindControl<RadioButton>("Win32RadioButton");

        if (grid != null)
            grid.SizeChanged += (_, _) => UpdateConnectionLayout();
        if (selectorPanel != null)
            selectorPanel.SizeChanged += (_, _) => UpdateDeviceSelectorLayout();
        if (adbButton != null)
            adbButton.PropertyChanged += (_, e) =>
            {
                if (e.Property.Name == "IsVisible") UpdateConnectionLayout();
            };
        if (win32Button != null)
            win32Button.PropertyChanged += (_, e) =>
            {
                if (e.Property.Name == "IsVisible") UpdateConnectionLayout();
            };

        UpdateConnectionLayout(true);
    }

    private void UpdateConnectionLayout(bool forceUpdate = false)
    {
        var grid = ConnectionGrid ?? this.FindControl<Grid>("ConnectionGrid");
        var selectorPanel = DeviceSelectorPanel ?? this.FindControl<Grid>("DeviceSelectorPanel");
        var adbButton = AdbRadioButton ?? this.FindControl<RadioButton>("AdbRadioButton");
        var win32Button = Win32RadioButton ?? this.FindControl<RadioButton>("Win32RadioButton");

        if (grid == null || selectorPanel == null || adbButton == null || win32Button == null)
            return;

        var totalWidth = grid.Bounds.Width;
        if (totalWidth <= 0) return;

        var adbWidth = adbButton.IsVisible ? adbButton.MinWidth + 8 : 0;
        var win32Width = win32Button.IsVisible ? win32Button.MinWidth + 8 : 0;
        var radioButtonsWidth = adbWidth + win32Width;
        var selectorMinWidth = selectorPanel.MinWidth;

        int newMode;
        if (totalWidth >= radioButtonsWidth + selectorMinWidth + 20)
            newMode = 0; // 一行
        else if (totalWidth >= selectorMinWidth + 20)
            newMode = 1; // 两行
        else
            newMode = 2; // 三行

        if (!forceUpdate && newMode == _currentLayoutMode) return;
        _currentLayoutMode = newMode;

        grid.ColumnDefinitions.Clear();
        grid.RowDefinitions.Clear();
        Grid.SetColumnSpan(selectorPanel, 1);

        switch (newMode)
        {
            case 0:
                if (adbButton.IsVisible)
                    grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                if (win32Button.IsVisible)
                    grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));

                var col = 0;
                if (adbButton.IsVisible)
                {
                    Grid.SetColumn(adbButton, col);
                    Grid.SetRow(adbButton, 0);
                    col++;
                }
                if (win32Button.IsVisible)
                {
                    Grid.SetColumn(win32Button, col);
                    Grid.SetRow(win32Button, 0);
                    col++;
                }
                Grid.SetColumn(selectorPanel, col);
                Grid.SetRow(selectorPanel, 0);
                break;

            case 1:
            case 2:
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var visibleCount = (adbButton.IsVisible ? 1 : 0) + (win32Button.IsVisible ? 1 : 0);
                for (var i = 0; i < Math.Max(visibleCount, 1); i++)
                    grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

                var c = 0;
                if (adbButton.IsVisible)
                {
                    Grid.SetColumn(adbButton, c++);
                    Grid.SetRow(adbButton, 0);
                }
                if (win32Button.IsVisible)
                {
                    Grid.SetColumn(win32Button, c);
                    Grid.SetRow(win32Button, 0);
                }
                Grid.SetColumn(selectorPanel, 0);
                Grid.SetColumnSpan(selectorPanel, Math.Max(visibleCount, 1));
                Grid.SetRow(selectorPanel, 1);
                break;
        }

        _currentSelectorMode = -1;
        UpdateDeviceSelectorLayout();
    }

    private void UpdateDeviceSelectorLayout()
    {
        var selectorPanel = DeviceSelectorPanel ?? this.FindControl<Grid>("DeviceSelectorPanel");
        var selectorLabel = DeviceSelectorLabel ?? this.FindControl<TextBlock>("DeviceSelectorLabel");
        var deviceCombo = DeviceComboBox ?? this.FindControl<ComboBox>("DeviceComboBox");

        if (selectorPanel == null || selectorLabel == null || deviceCombo == null)
            return;

        int newMode = _currentLayoutMode == 2 ? 1 : 0;

        if (newMode == _currentSelectorMode) return;
        _currentSelectorMode = newMode;

        selectorPanel.ColumnDefinitions.Clear();
        selectorPanel.RowDefinitions.Clear();

        switch (newMode)
        {
            case 0:
                selectorPanel.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                selectorPanel.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));

                Grid.SetColumn(selectorLabel, 0);
                Grid.SetRow(selectorLabel, 0);
                Grid.SetColumn(deviceCombo, 1);
                Grid.SetRow(deviceCombo, 0);

                selectorLabel.Margin = new Thickness(0, 2, 8, 0);
                deviceCombo.HorizontalAlignment = HorizontalAlignment.Stretch;
                break;

            case 1:
                selectorPanel.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                selectorPanel.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                selectorPanel.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));

                Grid.SetColumn(selectorLabel, 0);
                Grid.SetRow(selectorLabel, 0);
                Grid.SetColumn(deviceCombo, 0);
                Grid.SetRow(deviceCombo, 1);

                selectorLabel.Margin = new Thickness(5, 0, 0, 5);
                deviceCombo.HorizontalAlignment = HorizontalAlignment.Stretch;
                break;
        }
    }

    // 与主页相同语义：在分隔条拖拽结束时写回列宽并持久化
    private void GridSplitter_DragCompleted(object? sender, VectorEventArgs e)
    {
        if (MainGrid == null)
        {
            LoggerHelper.Error("GridSplitter_DragCompleted: MainGrid is null");
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                var actualCol1Width = MainGrid.ColumnDefinitions[0].ActualWidth;
                var col1Width = MainGrid.ColumnDefinitions[0].Width;
                var col2Width = MainGrid.ColumnDefinitions[2].Width;
                var col3Width = MainGrid.ColumnDefinitions[4].Width;

                var vm = MFAAvalonia.Helper.Instances.TaskQueueViewModel;
                if (vm != null)
                {
                    vm.SuppressPropertyChangedCallbacks = true;

                    if (col1Width is { IsStar: true, Value: 0 } && actualCol1Width > 0)
                        vm.Column1Width = new GridLength(actualCol1Width, GridUnitType.Pixel);
                    else
                        vm.Column1Width = col1Width;

                    vm.Column2Width = col2Width;
                    vm.Column3Width = col3Width;

                    vm.SuppressPropertyChangedCallbacks = false;
                    vm.SaveColumnWidths();
                }
                else
                {
                    LoggerHelper.Error("GridSplitter_DragCompleted: ViewModel is null");
                }
            }
            catch (Exception ex)
            {
                LoggerHelper.Error($"更新列宽失败: {ex.Message}");
            }
        });
    }

    private async Task<bool> TryRunMysteryImportAsync(Func<CopilotViewModel, Task> action)
    {
        if (DataContext is not CopilotViewModel vm)
        {
            return false;
        }

        var now = DateTime.UtcNow;
        if (_mysteryImportInProgress)
        {
            return false;
        }

        if (now - _lastMysteryImportUtc < MysteryImportCooldown)
        {
            return false;
        }

        _mysteryImportInProgress = true;
        _lastMysteryImportUtc = now;

        try
        {
            await action(vm);
            return true;
        }
        finally
        {
            _mysteryImportInProgress = false;
            _lastMysteryImportUtc = DateTime.UtcNow;
        }
    }
}
