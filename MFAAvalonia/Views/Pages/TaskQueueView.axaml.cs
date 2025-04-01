using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
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
using SukiUI.Controls;

namespace MFAAvalonia.Views.Pages;

public partial class TaskQueueView : UserControl
{
    public TaskQueueView()
    {
        DataContext = Instances.TaskQueueViewModel;
        InitializeComponent();
        MaaProcessor.Instance.InitializeData();
        // Introduction.TextArea.TextView.LineTransformers.Add(new RichTextLineTransformer());
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
        }
    }

    #region 任务选项

    private static readonly ConcurrentDictionary<string, StackPanel> CommonPanelCache = new();
    private static readonly ConcurrentDictionary<string, string> IntroductionsCache = new();
    private void SetMarkDown(string markDown)
    {
        // 使用TaskQueueViewModel的增强方法
        if (DataContext is TaskQueueViewModel viewModel)
        {
            viewModel.SetMarkdownIntroduction(markDown);
        }
        Introduction.Markdown = markDown;
    }

    public void SetOption(DragItemViewModel dragItem, bool value)
    {
        var cacheKey = $"{dragItem.Name}_{dragItem.InterfaceItem.GetHashCode()}";

        if (!value)
        {
            HideCurrentPanel(cacheKey);
            return;
        }

        var newPanel = CommonPanelCache.GetOrAdd(cacheKey, key =>
        {
            var p = new StackPanel();
            GeneratePanelContent(p, dragItem);
            CommonOptionSettings.Children.Add(p);
            return p;
        });

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
        if (newPanel.Children.Count == 0)
            CommonPanelCache.Remove(cacheKey, out _);
        newPanel.IsVisible = true;
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
    }

    private void HideCurrentPanel(string key)
    {
        if (CommonPanelCache.TryGetValue(key, out var oldPanel))
        {
            oldPanel.IsVisible = false;
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
                    Width = new GridLength(7, GridUnitType.Star),
                },
                new ColumnDefinition
                {
                    Width = new GridLength(4, GridUnitType.Star),
                    MinWidth = 146
                },
            },
            Margin = new Thickness(8, 0, 5, 5)
        };

        var textBlock = new TextBlock
        {
            FontSize = 14,
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
            Margin = new Thickness(13, 5, 5, 5),
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
        grid.Children.Add(numericUpDown);
        panel.Children.Add(grid);
    }

    private void AddOption(Panel panel, MaaInterface.MaaInterfaceSelectOption option, DragItemViewModel source)
    {
        if (MaaProcessor.Interface?.Option?.TryGetValue(option.Name ?? string.Empty, out var interfaceOption) != true) return;
        Control control = interfaceOption.Cases.ShouldSwitchButton(out var yes, out var no)
            ? CreateToggleControl(option, yes, no, source)
            : CreateComboControl(option, interfaceOption, source);

        panel.Children.Add(control);
    }

    private Grid CreateToggleControl(
        MaaInterface.MaaInterfaceSelectOption option,
        int yesValue,
        int noValue,
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

        Grid.SetColumn(textBlock, 0);
        Grid.SetColumn(button, 2);
        grid.Children.Add(textBlock);
        grid.Children.Add(button);

        return grid;
    }

    private Grid CreateComboControl(
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
                    Width = new GridLength(7, GridUnitType.Star),
                },
                new ColumnDefinition
                {
                    Width = new GridLength(4, GridUnitType.Star),
                    MinWidth = 180
                },
            },
            Margin = new Thickness(8, 0, 0, 5)
        };

        var combo = new ComboBox
        {
            DisplayMemberBinding = new Binding("Name"),
            Margin = new Thickness(5, 5, 2, 5),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            ItemsSource = interfaceOption.Cases?.Select(c => new
            {
                Name = LanguageHelper.GetLocalizedString(c.Name)
            }),
            SelectedIndex = option.Index ?? 0,
        };


        combo.Bind(IsEnabledProperty, new Binding("Idle")
        {
            Source = Instances.RootViewModel
        });

        // 直接设置ComboBox的属性
        combo.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        combo.Padding = new Thickness(2, 0, 0, 0);

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
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            Text = LanguageHelper.GetLocalizedString(option.Name),
        };
        textBlock.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("SukiLowText"));
        Grid.SetColumn(textBlock, 0);
        grid.Children.Add(combo);
        grid.Children.Add(textBlock);
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
