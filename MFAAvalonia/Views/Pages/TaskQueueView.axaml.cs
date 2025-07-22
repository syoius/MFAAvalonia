using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using AvaloniaExtensions.Axaml.Markup;
using ExCSS;
using MFAAvalonia.Configuration;
using MFAAvalonia.Extensions;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using MFAAvalonia.Helper.Converters;
using MFAAvalonia.Helper.ValueType;
using MFAAvalonia.ViewModels.Pages;
using MFAAvalonia.ViewModels.UsersControls;
using MFAAvalonia.Views.UserControls;
using SukiUI;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Color = Avalonia.Media.Color;
using FontStyle = Avalonia.Media.FontStyle;
using FontWeight = Avalonia.Media.FontWeight;
using HorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using Point = Avalonia.Point;
using VerticalAlignment = Avalonia.Layout.VerticalAlignment;
using Avalonia.Threading;
using Avalonia.Xaml.Interactivity;
using MFAAvalonia.ViewModels.Other;
using Newtonsoft.Json.Linq;
using SukiUI.Extensions;

namespace MFAAvalonia.Views.Pages;

public partial class TaskQueueView : UserControl
{
    public TaskQueueView()
    {
        DataContext = Instances.TaskQueueViewModel;
        InitializeComponent();
        MaaProcessor.Instance.InitializeData();
        InitializeControllerUI();
    }

    #region UI

    private void GridSplitter_DragCompleted(object sender, VectorEventArgs e)
    {
        if (MainGrid == null)
        {
            LoggerHelper.Error("GridSplitter_DragCompleted: MainGrid is null");
            return;
        }

        // 强制在UI线程上执行
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                // 获取当前Grid的实际列宽
                var actualCol1Width = MainGrid.ColumnDefinitions[0].ActualWidth;
                // var actualCol2Width = MainGrid.ColumnDefinitions[2].ActualWidth;
                // var actualCol3Width = MainGrid.ColumnDefinitions[4].ActualWidth;

                // 获取当前列定义中的Width属性
                var col1Width = MainGrid.ColumnDefinitions[0].Width;
                var col2Width = MainGrid.ColumnDefinitions[2].Width;
                var col3Width = MainGrid.ColumnDefinitions[4].Width;

                // 更新ViewModel中的列宽值
                var viewModel = Instances.TaskQueueViewModel;
                if (viewModel != null)
                {
                    // 更新ViewModel中的列宽值
                    // 临时禁用回调以避免循环更新
                    viewModel.SuppressPropertyChangedCallbacks = true;

                    // 对于第一列，使用像素值
                    if (col1Width is { IsStar: true, Value: 0 } && actualCol1Width > 0)
                    {
                        // 如果是自动或星号但实际有宽度，使用像素值
                        viewModel.Column1Width = new GridLength(actualCol1Width, GridUnitType.Pixel);
                    }
                    else
                    {
                        viewModel.Column1Width = col1Width;
                    }

                    // 其他列保持原来的类型
                    viewModel.Column2Width = col2Width;
                    viewModel.Column3Width = col3Width;

                    viewModel.SuppressPropertyChangedCallbacks = false;

                    // 手动保存配置
                    viewModel.SaveColumnWidths();
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

    #endregion

    private void InitializeControllerUI()
    {
        connectionGrid.SizeChanged += (sender, e) =>
        {
            var actualWidth = connectionGrid.Bounds.Width;
            double totalMinWidth = connectionGrid.Children.Sum(c => c.MinWidth);
            if (actualWidth >= totalMinWidth)
            {
                // 左右布局模式
                connectionGrid.ColumnDefinitions.Clear();
                connectionGrid.ColumnDefinitions.AddRange([
                        new ColumnDefinition
                        {
                            Width = new GridLength(FirstButton.MinWidth, GridUnitType.Pixel)
                        },
                        new ColumnDefinition
                        {
                            Width = new GridLength(SecondButton.MinWidth, GridUnitType.Pixel)
                        },
                        new ColumnDefinition
                        {
                            Width = new GridLength(1, GridUnitType.Star)
                        }
                    ]
                );

                Grid.SetColumn(FirstButton, 0);
                Grid.SetColumn(SecondButton, 1);
                Grid.SetColumn(ControllerPanel, 2);
                Grid.SetRow(FirstButton, 0);
                Grid.SetRow(SecondButton, 0);
                Grid.SetRow(ControllerPanel, 0);
            }
            else if (actualWidth >= FirstButton.MinWidth + SecondButton.MinWidth)
            {
                // 上下布局模式（两行）
                connectionGrid.ColumnDefinitions.Clear();
                connectionGrid.RowDefinitions.Clear();

                // 创建两行结构
                connectionGrid.RowDefinitions.Add(new RowDefinition
                {
                    Height = GridLength.Auto
                });
                connectionGrid.RowDefinitions.Add(new RowDefinition
                {
                    Height = GridLength.Auto
                });

                // 定义两列等宽布局（网页4提到的Star单位）
                connectionGrid.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(1, GridUnitType.Star)
                });
                connectionGrid.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(1, GridUnitType.Star)
                });

                // 设置控件位置
                Grid.SetRow(FirstButton, 0);
                Grid.SetColumn(FirstButton, 0);
                Grid.SetRow(SecondButton, 0);
                Grid.SetColumn(SecondButton, 1);

                // 设置DockPanel跨两列
                Grid.SetRow(ControllerPanel, 1);
                Grid.SetColumnSpan(ControllerPanel, 2);
                Grid.SetColumn(ControllerPanel, 0);

                // 强制刷新布局（网页3提到的布局刷新机制）
                FirstButton.InvalidateMeasure();
                SecondButton.InvalidateMeasure();
            }
            else
            {
                // 三层布局模式
                connectionGrid.ColumnDefinitions.Clear();
                connectionGrid.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(1, GridUnitType.Star)
                });

                connectionGrid.RowDefinitions.Clear();
                connectionGrid.RowDefinitions.Add(new RowDefinition
                {
                    Height = GridLength.Auto
                });
                connectionGrid.RowDefinitions.Add(new RowDefinition
                {
                    Height = GridLength.Auto
                });
                connectionGrid.RowDefinitions.Add(new RowDefinition
                {
                    Height = GridLength.Auto
                });

                Grid.SetRow(FirstButton, 0);
                Grid.SetColumn(FirstButton, 0);
                Grid.SetRow(SecondButton, 1);
                Grid.SetColumn(SecondButton, 0);
                Grid.SetRow(ControllerPanel, 2);
                Grid.SetColumn(ControllerPanel, 0);
            }
        };
    }

    private void SelectingItemsControl_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox listBox && listBox.SelectedItem is DragItemViewModel itemViewModel)
        {
            // 如果是由Grid_Tapped触发的选择变更，则不重复设置
            if (DataContext is TaskQueueViewModel vm)
            {
                // 先将所有任务项的EnableSetting设置为false
                foreach (var item in vm.TaskItemViewModels)
                {
                    if (item != itemViewModel)
                    {
                        item.EnableSetting = false;
                    }
                }
                // 然后设置当前项为true
                itemViewModel.EnableSetting = true;
            }
        }
    }

    private void Grid_Tapped(object? sender, TappedEventArgs e)
    {
        // 如果点击源是CheckBox或TextBlock，让专门的事件处理程序来处理
        if (e.Source is CheckBox || e.Source is TextBlock)
        {
            return;
        }

        // 如果点击到Grid的其他区域，则选中该项（显示设置面板）
        if (sender is Grid grid && grid.DataContext is DragItemViewModel itemViewModel)
        {
            if (DataContext is TaskQueueViewModel vm)
            {
                // 先将所有任务项的EnableSetting设置为false
                foreach (var item in vm.TaskItemViewModels)
                {
                    if (item != itemViewModel)
                    {
                        item.EnableSetting = false;
                    }
                }
                // 然后设置当前项为true
                itemViewModel.EnableSetting = true;
                
                // 同时更新ListBox的选中项
                var listBox = this.FindControl<ListBox>("TaskListBox");
                if (listBox != null && listBox.SelectedItem != itemViewModel)
                {
                    listBox.SelectedItem = itemViewModel;
                }
            }
            
            e.Handled = true;
        }
    }
    
    private void CheckBox_Tapped(object? sender, TappedEventArgs e)
    {
        // 阻止事件冒泡，这样Grid_Tapped不会处理这个事件
        e.Handled = true;
        // CheckBox的勾选状态由Binding自动处理，不需要额外代码
    }
    
    private void TextBlock_Tapped(object? sender, TappedEventArgs e)
    {
        // 当点击任务名称时，显示该任务的设置
        if (sender is TextBlock textBlock && textBlock.DataContext is DragItemViewModel itemViewModel)
        {
            if (DataContext is TaskQueueViewModel vm)
            {
                // 先将所有任务项的EnableSetting设置为false
                foreach (var item in vm.TaskItemViewModels)
                {
                    if (item != itemViewModel)
                    {
                        item.EnableSetting = false;
                    }
                }
                // 然后设置当前项为true
                itemViewModel.EnableSetting = true;
                if (vm.Idle)
                {
                    itemViewModel.IsCheckedWithNull = true;
                }
                
                // 同时更新ListBox的选中项
                var listBox = this.FindControl<ListBox>("TaskListBox");
                if (listBox != null && listBox.SelectedItem != itemViewModel)
                {
                    listBox.SelectedItem = itemViewModel;
                }
            }
            
            e.Handled = true;
        }
    }


    private void Delete(object? sender, RoutedEventArgs e)
    {
        var menuItem = sender as MenuItem;
        if (menuItem.DataContext is DragItemViewModel taskItemViewModel && DataContext is TaskQueueViewModel vm)
        {
            int index = vm.TaskItemViewModels.IndexOf(taskItemViewModel);
            vm.TaskItemViewModels.RemoveAt(index);
            Instances.TaskQueueView.SetOption(taskItemViewModel, false);
            ConfigurationManager.Current.SetValue(ConfigurationKeys.TaskItems, vm.TaskItemViewModels.ToList().Select(model => model.InterfaceItem));
            vm.ShowSettings = false;
        }
    }
    
    private void Run(object? sender, RoutedEventArgs e)
    {
        var menuItem = sender as MenuItem;
        if (menuItem.DataContext is DragItemViewModel taskItemViewModel && DataContext is TaskQueueViewModel vm)
        {
            MaaProcessor.Instance.Start([taskItemViewModel]);
        }
    }
    
    #region 任务选项

    private static readonly ConcurrentDictionary<string, Control> CommonPanelCache = new();
    private static readonly ConcurrentDictionary<string, Control> AdvancedPanelCache = new();
    private static readonly ConcurrentDictionary<string, string> IntroductionsCache = new();
    private static readonly ConcurrentDictionary<string, bool> ShowCache = new();

    private void SetMarkDown(string markDown)
    {
        // 使用TaskQueueViewModel的增强方法
        if (DataContext is TaskQueueViewModel viewModel)
        {
            viewModel.SetMarkdownIntroduction(markDown);
        }
        Introduction.Markdown = markDown;
    }

    public void SetOption(DragItemViewModel dragItem, bool value, bool init = false)
    {
        if (!init)
            Instances.TaskQueueViewModel.IsCommon = true;
        var cacheKey = $"{dragItem.Name}_{dragItem.InterfaceItem?.Entry}_{dragItem.InterfaceItem?.GetHashCode()}";

        if (!value)
        {
            HideCurrentPanel(cacheKey);
            return;
        }

        HideAllPanels();
        var juggle = (dragItem.InterfaceItem?.Advanced == null || dragItem.InterfaceItem.Advanced.Count == 0) || (dragItem.InterfaceItem?.Option == null || dragItem.InterfaceItem.Option.Count == 0);
        Instances.TaskQueueViewModel.ShowSettings = ShowCache.GetOrAdd(cacheKey,
            !juggle);
        if (juggle)
        {
            var newPanel = CommonPanelCache.GetOrAdd(cacheKey, key =>
            {
                var p = new StackPanel();
                GeneratePanelContent(p, dragItem);
                CommonOptionSettings.Children.Add(p);
                return p;
            });
            newPanel.IsVisible = true;
        }
        else
        {
            if (!init)
            {
                var commonPanel = CommonPanelCache.GetOrAdd(cacheKey, key =>
                {
                    var p = new StackPanel();
                    GenerateCommonPanelContent(p, dragItem);
                    CommonOptionSettings.Children.Add(p);
                    return p;
                });
                commonPanel.IsVisible = true;
            }
            var advancedPanel = AdvancedPanelCache.GetOrAdd(cacheKey, key =>
            {
                var p = new StackPanel();
                GenerateAdvancedPanelContent(p, dragItem);
                AdvancedOptionSettings.Children.Add(p);
                return p;
            });
            if (!init)
            {
                advancedPanel.IsVisible = true;
            }
        }
        if (!init)
        {
            var newIntroduction = IntroductionsCache.GetOrAdd(cacheKey, key =>
            {
                var input = string.Empty;

                // 原始带标记的文本
                if (dragItem.InterfaceItem?.Document?.Count > 0)
                {
                    input = Regex.Unescape(string.Join("\\n", dragItem.InterfaceItem.Document));
                }
                input = LanguageHelper.GetLocalizedString(input);
                return ConvertCustomMarkup(input);
            });

            SetMarkDown(newIntroduction);
        }
    }


    private void GeneratePanelContent(StackPanel panel, DragItemViewModel dragItem)
    {

        AddRepeatOption(panel, dragItem);

        if (dragItem.InterfaceItem?.Option != null)
        {
            foreach (var option in dragItem.InterfaceItem.Option)
            {
                AddOption(panel, option, dragItem);
            }
        }

        if (dragItem.InterfaceItem?.Advanced != null)
        {
            foreach (var option in dragItem.InterfaceItem.Advanced)
            {
                AddAdvancedOption(panel, option);
            }
        }

    }

    private void GenerateCommonPanelContent(StackPanel panel, DragItemViewModel dragItem)
    {
        AddRepeatOption(panel, dragItem);

        if (dragItem.InterfaceItem?.Option != null)
        {
            foreach (var option in dragItem.InterfaceItem.Option)
            {
                AddOption(panel, option, dragItem);
            }
        }
    }

    private void GenerateAdvancedPanelContent(StackPanel panel, DragItemViewModel dragItem)
    {
        if (dragItem.InterfaceItem?.Advanced != null)
        {
            foreach (var option in dragItem.InterfaceItem.Advanced)
            {
                AddAdvancedOption(panel, option);
            }
        }
    }

    private void HideCurrentPanel(string key)
    {
        if (CommonPanelCache.TryGetValue(key, out var oldPanel))
        {
            oldPanel.IsVisible = false;
        }
        if (AdvancedPanelCache.TryGetValue(key, out var oldaPanel))
        {
            oldaPanel.IsVisible = false;
        }

        Introduction.Markdown = "";
    }

    private void HideAllPanels()
    {
        foreach (var panel in CommonPanelCache.Values)
        {
            panel.IsVisible = false;
        }

        Introduction.Markdown = "";
    }


    private void AddRepeatOption(Panel panel, DragItemViewModel source)
    {
        if (source.InterfaceItem is not { Repeatable: true }) return;
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition
                {
                    Width = new GridLength(7, GridUnitType.Star)
                },
                new ColumnDefinition
                {
                    Width = new GridLength(4, GridUnitType.Star)
                }
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
        numericUpDown.Bind(IsEnabledProperty, new Binding("Idle")
        {
            Source = Instances.RootViewModel
        });
        numericUpDown.ValueChanged += (_, _) =>
        {
            source.InterfaceItem.RepeatCount = Convert.ToInt32(numericUpDown.Value);
            SaveConfiguration();
        };
        Grid.SetColumn(numericUpDown, 1);
        grid.SizeChanged += (sender, e) =>
        {
            var currentGrid = sender as Grid;
            if (currentGrid == null) return;
            // 计算所有列的 MinWidth 总和
            double totalMinWidth = currentGrid.Children.Sum(c => c.MinWidth);
            double availableWidth = currentGrid.Bounds.Width - currentGrid.Margin.Left - currentGrid.Margin.Right;

            if (availableWidth < totalMinWidth)
            {
                // 切换为上下结构（两行）
                currentGrid.ColumnDefinitions.Clear();
                currentGrid.RowDefinitions.Clear();
                currentGrid.RowDefinitions.Add(new RowDefinition
                {
                    Height = GridLength.Auto
                });
                currentGrid.RowDefinitions.Add(new RowDefinition
                {
                    Height = GridLength.Auto
                });

                Grid.SetRow(textBlock, 0);
                Grid.SetRow(numericUpDown, 1);
                Grid.SetColumn(textBlock, 0);
                Grid.SetColumn(numericUpDown, 0);
            }
            else
            {
                // 恢复左右结构（两列）
                currentGrid.RowDefinitions.Clear();
                currentGrid.ColumnDefinitions.Clear();
                currentGrid.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(7, GridUnitType.Star)
                });
                currentGrid.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(4, GridUnitType.Star)
                });

                Grid.SetRow(textBlock, 0);
                Grid.SetRow(numericUpDown, 0);
                Grid.SetColumn(textBlock, 0);
                Grid.SetColumn(numericUpDown, 1);
            }
        };

        grid.Children.Add(numericUpDown);
        panel.Children.Add(grid);
    }


    private bool IsValidIntegerInput(string text)
    {
        // 空字符串或仅包含负号是允许的
        if (string.IsNullOrEmpty(text) || text == "-")
            return true;

        // 检查是否以负号开头，且负号只出现一次
        if (text.StartsWith("-") && (text.Length == 1 || (!char.IsDigit(text[1]) || text.LastIndexOf("-") != 0)))
            return false;

        // 检查是否只包含数字和可能的负号
        for (int i = 0; i < text.Length; i++)
        {
            if (i == 0 && text[i] == '-')
                continue; // 允许第一个字符是负号

            if (!char.IsDigit(text[i]))
                return false; // 其他字符必须是数字
        }

        return true;
    }
    private string FilterToInteger(string text)
    {
        // 1. 去除所有非数字和非负号字符
        string filtered = new string(text.Where(c => c == '-' || char.IsDigit(c)).ToArray());

        // 2. 处理负号位置和数量
        if (filtered.Contains('-'))
        {
            // 确保负号只出现在开头且只有一个
            if (filtered[0] != '-' || filtered.Count(c => c == '-') > 1)
            {
                filtered = filtered.Replace("-", "");
            }
        }

        // 3. 处理空字符串或仅负号的情况
        if (string.IsNullOrEmpty(filtered) || filtered == "-")
        {
            return filtered;
        }

        // 4. 去除前导零
        if (filtered.Length > 1 && filtered[0] == '0')
        {
            filtered = filtered.TrimStart('0');
        }

        return filtered;
    }

    private void AddAdvancedOption(Panel panel, MaaInterface.MaaInterfaceSelectAdvanced option)
    {
        if (MaaProcessor.Interface?.Advanced?.TryGetValue(option.Name, out var interfaceOption) != true) return;

        for (int i = 0; interfaceOption.Field != null && i < interfaceOption.Field.Count; i++)
        {
            var field = interfaceOption.Field[i];
            var type = i < (interfaceOption.Type?.Count ?? 0) ? (interfaceOption.Type?[i] ?? "string") : (interfaceOption.Type?.Count > 0 ? interfaceOption.Type[0] : "string");

            // 获取默认值（支持单值或列表）
            string defaultValue = string.Empty;
            if (option.Data.TryGetValue(field, out var value))
            {
                defaultValue = value;
            }
            else if (interfaceOption.Default != null && interfaceOption.Default.Count > i)
            {
                // 处理Default为单值或列表的情况
                var defaultToken = interfaceOption.Default[i];
                if (defaultToken is JArray arr)
                {
                    defaultValue = arr.Count > 0 ? arr[0].ToString() : string.Empty;
                }
                else
                {
                    defaultValue = defaultToken.ToString();
                }
            }

            var grid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition
                    {
                        Width = new GridLength(7, GridUnitType.Star)
                    },
                    new ColumnDefinition
                    {
                        Width = new GridLength(4, GridUnitType.Star)
                    }
                },
                Margin = new Thickness(8, 0, 5, 5)
            };

            // 创建AutoCompleteBox
            var autoCompleteBox = new AutoCompleteBox
            {
                MinWidth = 150,
                Margin = new Thickness(0, 5, 5, 5),
                Text = defaultValue,
                IsTextCompletionEnabled = true,
                FilterMode = AutoCompleteFilterMode.Custom,
                ItemFilter = (search, item) =>
                {
                    // 处理搜索文本为空的情况
                    if (string.IsNullOrEmpty(search))
                        return true;

                    // 处理项为空的情况
                    if (item == null)
                        return false;

                    // 确保项可以转换为字符串
                    var itemText = item.ToString();
                    if (string.IsNullOrEmpty(itemText))
                        return false;

                    // 执行包含匹配（不区分大小写）
                    return itemText.IndexOf(search, StringComparison.InvariantCultureIgnoreCase) >= 0;
                },
            };
      

// 绑定启用状态
            autoCompleteBox.Bind(IsEnabledProperty, new Binding("Idle")
            {
                Source = Instances.RootViewModel
            });
            var completionItems = new List<string>();
// 生成补全列表（从Default获取）
            if (interfaceOption.Default != null && interfaceOption.Default.Count > i)
            {
                var defaultToken = interfaceOption.Default[i];
               

                if (defaultToken is JArray arr)
                {
                    completionItems = arr.Select(item => item.ToString()).ToList();
                }
                else
                {
                    completionItems.Add(defaultToken.ToString());
                    completionItems.Add(string.Empty);
                }

                autoCompleteBox.ItemsSource = completionItems;
            }
            if (completionItems.Count > 0 && !string.IsNullOrEmpty(completionItems[0]))
            {
                var behavior = new AutoCompleteBehavior();
                Interaction.GetBehaviors(autoCompleteBox).Add(behavior);
            }
// 文本变化事件 - 修改此处以确保文本清空时下拉框保持打开
            autoCompleteBox.TextChanged += (_, _) =>
            {
                if (type.ToLower() == "int")
                {
                    if (!IsValidIntegerInput(autoCompleteBox.Text))
                    {
                        autoCompleteBox.Text = FilterToInteger(autoCompleteBox.Text);
                        // 保持光标位置
                        if (autoCompleteBox.CaretIndex > autoCompleteBox.Text.Length)
                        {
                            autoCompleteBox.CaretIndex = autoCompleteBox.Text.Length;
                        }
                    }
                }

                option.Data[field] = autoCompleteBox.Text;
                option.PipelineOverride = interfaceOption.GenerateProcessedPipeline(option.Data);
                SaveConfiguration();
            };
            option.Data[field] = autoCompleteBox.Text;
            option.PipelineOverride = interfaceOption.GenerateProcessedPipeline(option.Data);
            SaveConfiguration();
// 选择项变化事件
            autoCompleteBox.SelectionChanged += (_, _) =>
            {
                if (autoCompleteBox.SelectedItem is string selectedText)
                {
                    autoCompleteBox.Text = selectedText;
                    option.Data[field] = selectedText;
                    option.PipelineOverride = interfaceOption.GenerateProcessedPipeline(option.Data);
                    SaveConfiguration();
                }
            };

            Grid.SetColumn(autoCompleteBox, 1);

            // 标签部分（保持不变）
            var textBlock = new TextBlock
            {
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                Text = LanguageHelper.GetLocalizedString(field),
            };
            textBlock.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("SukiLowText"));

            var stackPanel = new StackPanel
            {
                MinWidth = 180,
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            Grid.SetColumn(stackPanel, 0);
            stackPanel.Children.Add(textBlock);

            // 文档提示部分（保持不变）
            if (interfaceOption.Document is { Count: > 0 } && i < interfaceOption.Document.Count)
            {
                var doc = interfaceOption.Document[i];
                var input = doc;
                try
                {
                    input = Regex.Unescape(doc);
                }
                catch (Exception)
                {
                }
                var docBlock = new TooltipBlock
                {
                    TooltipText = input
                };
                stackPanel.Children.Add(docBlock);
            }

            // 布局逻辑（保持不变）
            grid.Children.Add(autoCompleteBox);
            grid.Children.Add(stackPanel);
            grid.SizeChanged += (sender, e) =>
            {
                var currentGrid = sender as Grid;
                if (currentGrid == null) return;

                var totalMinWidth = currentGrid.Children.Sum(c => c.MinWidth);
                var availableWidth = currentGrid.Bounds.Width;
                if (availableWidth < totalMinWidth)
                {
                    currentGrid.ColumnDefinitions.Clear();
                    currentGrid.RowDefinitions.Clear();
                    currentGrid.RowDefinitions.Add(new RowDefinition
                    {
                        Height = GridLength.Auto
                    });
                    currentGrid.RowDefinitions.Add(new RowDefinition
                    {
                        Height = GridLength.Auto
                    });
                    Grid.SetRow(stackPanel, 0);
                    Grid.SetRow(autoCompleteBox, 1);
                    Grid.SetColumn(stackPanel, 0);
                    Grid.SetColumn(autoCompleteBox, 0);
                }
                else
                {
                    currentGrid.RowDefinitions.Clear();
                    currentGrid.ColumnDefinitions.Clear();
                    currentGrid.ColumnDefinitions.Add(new ColumnDefinition
                    {
                        Width = new GridLength(7, GridUnitType.Star)
                    });
                    currentGrid.ColumnDefinitions.Add(new ColumnDefinition
                    {
                        Width = new GridLength(4, GridUnitType.Star)
                    });
                    Grid.SetRow(stackPanel, 0);
                    Grid.SetRow(autoCompleteBox, 0);
                    Grid.SetColumn(stackPanel, 0);
                    Grid.SetColumn(autoCompleteBox, 1);
                }
            };

            panel.Children.Add(grid);
        }
    }
    private void AddOption(Panel panel, MaaInterface.MaaInterfaceSelectOption option, DragItemViewModel source)
    {
        if (MaaProcessor.Interface?.Option?.TryGetValue(option.Name ?? string.Empty, out var interfaceOption) != true) return;
        Control control = interfaceOption.Cases.ShouldSwitchButton(out var yes, out var no)
            ? CreateToggleControl(option, yes, no, interfaceOption, source)
            : CreateComboBoxControl(option, interfaceOption, source);

        panel.Children.Add(control);
    }

    private Grid CreateToggleControl(
        MaaInterface.MaaInterfaceSelectOption option,
        int yesValue,
        int noValue,
        MaaInterface.MaaInterfaceOption interfaceOption,
        DragItemViewModel source
    )
    {
        var button = new ToggleSwitch
        {
            IsChecked = option.Index == yesValue,
            Classes =
            {
                "Switch"
            },
            MaxHeight = 60,
            MaxWidth = 100,
            HorizontalAlignment = HorizontalAlignment.Right,
            Tag = option.Name,
            VerticalAlignment = VerticalAlignment.Center
        };

        button.Bind(IsEnabledProperty, new Binding("Idle")
        {
            Source = Instances.RootViewModel
        });


        button.IsCheckedChanged += (_, _) =>
        {
            option.Index = button.IsChecked == true ? yesValue : noValue;
            SaveConfiguration();
        };

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
            ColumnDefinitions =
            {
                new ColumnDefinition
                {
                    Width = GridLength.Auto
                },
                new ColumnDefinition
                {
                    Width = new GridLength(1, GridUnitType.Star)
                },
                new ColumnDefinition
                {
                    Width = GridLength.Auto
                }
            },
            Margin = new Thickness(0, 0, 0, 5)
        };
        var stackPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        stackPanel.Children.Add(textBlock);
        if (interfaceOption.Document is { Count: > 0 })
        {
            var input = Regex.Unescape(string.Join("\\n", interfaceOption.Document));

            var docBlock = new TooltipBlock
            {
                TooltipText = input
            };
            stackPanel.Children.Add(docBlock);
        }
        Grid.SetColumn(stackPanel, 0);
        Grid.SetColumn(button, 2);
        grid.Children.Add(stackPanel);
        grid.Children.Add(button);

        return grid;
    }

    private Grid CreateComboBoxControl(
        MaaInterface.MaaInterfaceSelectOption option,
        MaaInterface.MaaInterfaceOption interfaceOption,
        DragItemViewModel source)
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition
                {
                    Width = new GridLength(7, GridUnitType.Star)
                },
                new ColumnDefinition
                {
                    Width = new GridLength(4, GridUnitType.Star),
                    MinWidth = 180
                },
            },
            Margin = new Thickness(8, 0, 5, 5)
        };

        var combo = new ComboBox
        {
            MinWidth = 170,
            Classes =
            {
                "LimitWidth"
            },
            Margin = new Thickness(0, 5, 5, 5),
            ItemsSource = interfaceOption.Cases.Select(caseOption => new LocalizationViewModel
            {
                Name = caseOption.Name,
            }).ToList(),
            ItemTemplate = new FuncDataTemplate<LocalizationViewModel>((optionCase, b) =>
            {

                var data =
                    new TextBlock
                    {
                        Text = optionCase?.Name ?? string.Empty,
                        TextTrimming = TextTrimming.WordEllipsis,
                        TextWrapping = TextWrapping.NoWrap
                    };
                ToolTip.SetTip(data, optionCase?.Name ?? string.Empty);
                ToolTip.SetShowDelay(data, 100);
                return data;
            }),
            SelectionBoxItemTemplate = new FuncDataTemplate<LocalizationViewModel>((optionCase, b) =>
            {

                var data =
                    new TextBlock
                    {
                        Text = optionCase?.Name ?? string.Empty,
                        TextTrimming = TextTrimming.WordEllipsis,
                        TextWrapping = TextWrapping.NoWrap
                    };
                ToolTip.SetTip(data, optionCase?.Name ?? string.Empty);
                ToolTip.SetShowDelay(data, 100);
                return data;
            }),
            SelectedIndex = option.Index ?? 0,
        };


        combo.Bind(IsEnabledProperty, new Binding("Idle")
        {
            Source = Instances.RootViewModel
        });

        // 直接设置ComboBox的属性
        combo.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        combo.Padding = new Thickness(2, 0, 2, 0);

        combo.SelectionChanged += (_, _) =>
        {
            option.Index = combo.SelectedIndex;
            SaveConfiguration();
        };

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

        var stackPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            MinWidth = 180,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        Grid.SetColumn(stackPanel, 0);
        stackPanel.Children.Add(textBlock);
        if (interfaceOption.Document is { Count: > 0 })
        {
            var input = Regex.Unescape(string.Join("\\n", interfaceOption.Document));

            var docBlock = new TooltipBlock
            {
                TooltipText = input
            };
            stackPanel.Children.Add(docBlock);
        }
        grid.Children.Add(combo);
        grid.Children.Add(stackPanel);
        grid.SizeChanged += (sender, e) =>
        {
            var currentGrid = sender as Grid;

            if (currentGrid == null) return;

            // 计算所有列的 MinWidth 总和
            var totalMinWidth = currentGrid.Children.Sum(c => c.MinWidth);
            var availableWidth = currentGrid.Bounds.Width;
            if (availableWidth < totalMinWidth)
            {
                // 切换为上下结构（两行）
                currentGrid.ColumnDefinitions.Clear();
                currentGrid.RowDefinitions.Clear();
                currentGrid.RowDefinitions.Add(new RowDefinition
                {
                    Height = GridLength.Auto
                });
                currentGrid.RowDefinitions.Add(new RowDefinition
                {
                    Height = GridLength.Auto
                });

                Grid.SetRow(stackPanel, 0);
                Grid.SetRow(combo, 1);
                Grid.SetColumn(stackPanel, 0);
                Grid.SetColumn(combo, 0);
            }
            else
            {
                // 恢复左右结构（两列）
                currentGrid.RowDefinitions.Clear();
                currentGrid.ColumnDefinitions.Clear();
                currentGrid.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(7, GridUnitType.Star)
                });
                currentGrid.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(4, GridUnitType.Star)
                });

                Grid.SetRow(stackPanel, 0);
                Grid.SetRow(combo, 0);
                Grid.SetColumn(stackPanel, 0);
                Grid.SetColumn(combo, 1);
            }
        };
        return grid;
    }


    private void SaveConfiguration()
    {
        ConfigurationManager.Current.SetValue(ConfigurationKeys.TaskItems,
            Instances.TaskQueueViewModel.TaskItemViewModels.Select(m => m.InterfaceItem));
    }
    public static string ConvertCustomMarkup(string input, string outputFormat = "markdown")
    {
        // 预处理换行符
        input = input.Replace(@"\n", "\n");

        // 确保换行被正确处理
        if (input.Contains("\n"))
        {
            // 先处理双换行为段落
            input = Regex.Replace(input, @"(\n\s*\n)", "\n\n");
        }

        // 定义替换规则字典
        var replacementRules = new Dictionary<string, Dictionary<string, string>>
        {
            // 颜色标记 [color:red]
            {
                @"\[color:(.*?)\]", new Dictionary<string, string>
                {
                    {
                        "markdown", "%{color:$1}"
                    },
                    {
                        "html", "<span style='color: $1;'>"
                    }
                }
            },
            // 字号标记 [size:20]
            {
                @"\[size:(\d+)\]", new Dictionary<string, string>
                {
                    {
                        "markdown", ""
                    },
                    {
                        "html", "<span style='font-size: $1px;'>"
                    }
                }
            },
            // 对齐标记 [align:center]
            {
                @"\[align:(left|center|right)\]", new Dictionary<string, string>
                {
                    {
                        "markdown", "$1" switch { "center" => "p=.", "right" => "p>.", _ => "p<." }
                    },
                    {
                        "html", "<div style='text-align: $1;'>"
                    }
                }
            },
            {
                @"\[/(color)\]", new Dictionary<string, string>
                {
                    {
                        "markdown", "%"
                    },
                    {
                        "html", "$1" switch { "align" => "</div>", _ => "</span>" }
                    }
                }
            },
            {
                @"\[/(align)\]", new Dictionary<string, string>
                {
                    {
                        "markdown", ""
                    },
                    {
                        "html", "$1" switch { "align" => "</div>", _ => "</span>" }
                    }
                }
            },
            // 关闭标记 [/color] [/size] [/align]
            {
                @"\[/(size)\]", new Dictionary<string, string>
                {
                    {
                        "markdown", ""
                    },
                    {
                        "html", "$1" switch { "align" => "</div>", _ => "</span>" }
                    }
                }
            },
            // 粗体、斜体等简单标记
            {
                @"\[(b|i|u|s)\]", new Dictionary<string, string>
                {
                    {
                        "markdown", "$1" switch
                        {
                            "b" => "**", "i" => "*", "u" => "<u>", "s" => "~~", _ => ""
                        }
                    },
                    {
                        "html", "$1" switch
                        {
                            "b" => "<strong>", "i" => "<em>", "u" => "<u>", "s" => "<s>", _ => ""
                        }
                    }
                }
            },
            {
                @"\[/(b|i|u|s)\]", new Dictionary<string, string>
                {
                    {
                        "markdown", "$1" switch
                        {
                            "b" => "**", "i" => "*", "u" => "</u>", "s" => "~~", _ => ""
                        }
                    },
                    {
                        "html", "$1" switch
                        {
                            "b" => "</strong>", "i" => "</em>", "u" => "</u>", "s" => "</s>", _ => ""
                        }
                    }
                }
            }
        };

        // 执行正则替换
        foreach (var rule in replacementRules)
        {
            input = Regex.Replace(
                input,
                rule.Key,
                m => rule.Value[outputFormat].Replace("$1", m.Groups[1].Value),
                RegexOptions.IgnoreCase
            );
        }

        // 处理换行符
        input = outputFormat switch
        {
            "markdown" => input.Replace("\n", "  \n"), // Markdown换行需两个空格
            "html" => input.Replace("\n", "<br/>"), // HTML换行用<br/>
            _ => input
        };

        return input;
    }
    // private static List<TextStyleMetadata> _currentStyles = new();
    //
    // private class RichTextLineTransformer : DocumentColorizingTransformer
    // {
    //     protected override void ColorizeLine(DocumentLine line)
    //     {
    //         _currentStyles = _currentStyles.OrderByDescending(s => s.EndOffset).ToList();
    //         int lineStart = line.Offset;
    //         int lineEnd = line.Offset + line.Length;
    //
    //         foreach (var style in _currentStyles)
    //         {
    //             if (style.EndOffset <= lineStart || style.StartOffset >= lineEnd)
    //                 continue;
    //
    //             int start = Math.Max(style.StartOffset, lineStart);
    //             int end = Math.Min(style.EndOffset, lineEnd);
    //             ApplyStyle(start, end, style.Tag, style.Value);
    //         }
    //     }
    //
    //
    //     /// <summary>
    //     /// 应用样式到指定范围的文本
    //     /// </summary>
    //     /// <param name="startOffset">起始偏移量</param>
    //     /// <param name="endOffset">结束偏移量</param>
    //     /// <param name="tag">标记名称</param>
    //     /// <param name="value">标记值</param>
    //     private void ApplyStyle(int startOffset, int endOffset, string tag, string value)
    //     {
    //         switch (tag)
    //         {
    //             case "color":
    //                 ChangeLinePart(startOffset, endOffset, element => element.TextRunProperties.SetForegroundBrush(new SolidColorBrush(Color.Parse(value))));
    //                 break;
    //             case "size":
    //                 if (double.TryParse(value, out var size))
    //                 {
    //                     ChangeLinePart(startOffset, endOffset, element => element.TextRunProperties.SetFontRenderingEmSize(size));
    //                 }
    //                 break;
    //             case "b":
    //                 ChangeLinePart(startOffset, endOffset, element =>
    //                 {
    //                     var typeface = new Typeface(
    //                         element.TextRunProperties.Typeface.FontFamily,
    //                         element.TextRunProperties.Typeface.Style, FontWeight.Bold, // 设置粗体
    //                         element.TextRunProperties.Typeface.Stretch
    //                     );
    //                     element.TextRunProperties.SetTypeface(typeface);
    //                 });
    //                 break;
    //             case "i":
    //                 ChangeLinePart(startOffset, endOffset, element =>
    //                 {
    //                     var typeface = new Typeface(
    //                         element.TextRunProperties.Typeface.FontFamily,
    //                         FontStyle.Italic, // 设置斜体
    //                         element.TextRunProperties.Typeface.Weight,
    //                         element.TextRunProperties.Typeface.Stretch
    //                     );
    //                     element.TextRunProperties.SetTypeface(typeface);
    //                 });
    //                 break;
    //             case "u":
    //                 ChangeLinePart(startOffset, endOffset, element => element.TextRunProperties.SetTextDecorations(TextDecorations.Underline));
    //                 break;
    //             case "s":
    //                 ChangeLinePart(startOffset, endOffset, element => element.TextRunProperties.SetTextDecorations(TextDecorations.Strikethrough));
    //                 break;
    //         }
    //     }
    // }
    //
    // public class TextStyleMetadata
    // {
    //     public int StartOffset { get; set; }
    //     public int EndOffset { get; set; }
    //     public string Tag { get; set; }
    //     public string Value { get; set; }
    //
    //     // 新增字段存储标签部分的长度
    //     public int OriginalLength { get; set; }
    // }
    //
    // private (string CleanText, List<TextStyleMetadata> Styles) ProcessRichTextTags(string input)
    // {
    //     var styles = new List<TextStyleMetadata>();
    //     var cleanText = new StringBuilder();
    //     ProcessNestedContent(input, cleanText, styles, new Stack<(string Tag, string Value, int CleanStart)>());
    //     return (cleanText.ToString(), styles);
    // }
    //
    // private void ProcessNestedContent(string input, StringBuilder cleanText, List<TextStyleMetadata> styles, Stack<(string Tag, string Value, int CleanStart)> stack)
    // {
    //     var matches = Regex.Matches(input, @"\[(?<tag>[^\]]+):?(?<value>[^\]]*)\](?<content>.*?)\[/\k<tag>\]");
    //     int lastPos = 0;
    //
    //     foreach (Match match in matches.Cast<Match>())
    //     {
    //         // 添加非标签内容
    //         if (match.Index > lastPos)
    //         {
    //             cleanText.Append(input.Substring(lastPos, match.Index - lastPos));
    //         }
    //
    //         string tag = match.Groups["tag"].Value.ToLower();
    //         string value = match.Groups["value"].Value;
    //         string content = match.Groups["content"].Value;
    //
    //         // 记录开始位置
    //         int contentStart = cleanText.Length;
    //         stack.Push((tag, value, contentStart));
    //
    //         // 递归解析嵌套内容
    //         var nestedCleanText = new StringBuilder();
    //         ProcessNestedContent(content, nestedCleanText, styles, new Stack<(string Tag, string Value, int CleanStart)>(stack));
    //         cleanText.Append(nestedCleanText);
    //
    //         // 记录样式元数据
    //         if (stack.Count > 0 && stack.Peek().Tag == tag)
    //         {
    //             var (openTag, openValue, cleanStart) = stack.Pop();
    //             styles.Add(new TextStyleMetadata
    //             {
    //                 StartOffset = cleanStart,
    //                 EndOffset = cleanText.Length,
    //                 Tag = openTag,
    //                 Value = openValue
    //             });
    //         }
    //         lastPos = match.Index + match.Length;
    //     }
    //
    //     // 添加剩余文本
    //     if (lastPos < input.Length)
    //     {
    //         cleanText.Append(input.Substring(lastPos));
    //     }
    // }
    //
    // // 使用 MatchEvaluator 的独立方法
    //

    #endregion
}
