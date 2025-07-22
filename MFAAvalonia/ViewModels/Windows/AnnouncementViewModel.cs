using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MFAAvalonia.Configuration;
using MFAAvalonia.Extensions;
using MFAAvalonia.Helper;
using MFAAvalonia.Helper.ValueType;
using MFAAvalonia.Views.Windows;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace MFAAvalonia.ViewModels.Windows;

// 公告项数据结构
public class AnnouncementItem
{
    public string Title { get; set; }
    public string Content { get; set; }
    public string FilePath { get; set; }
    public DateTime LastModified { get; set; }
}

public partial class AnnouncementViewModel : ViewModelBase
{
    public static readonly string AnnouncementFolder = "Announcement";

    [ObservableProperty] private AvaloniaList<AnnouncementItem> _announcementItems = new();

    [ObservableProperty] private AnnouncementItem _selectedAnnouncement;

    [ObservableProperty] private bool _doNotRemindThisAnnouncementAgain = Convert.ToBoolean(
        GlobalConfiguration.GetValue(ConfigurationKeys.DoNotShowAnnouncementAgain, bool.FalseString));

    partial void OnDoNotRemindThisAnnouncementAgainChanged(bool value)
    {
        GlobalConfiguration.SetValue(ConfigurationKeys.DoNotShowAnnouncementAgain, value.ToString());
    }

    // 加载公告文件夹中的所有md文件
    public AnnouncementViewModel()
    {
        LoadAnnouncements();
    }

    private void LoadAnnouncements()
    {
        try
        {
            var resourcePath = Path.Combine(AppContext.BaseDirectory, "resource");
            var announcementDir = Path.Combine(resourcePath, AnnouncementFolder);

            if (!Directory.Exists(announcementDir))
            {
                LoggerHelper.Warning($"公告文件夹不存在: {announcementDir}");
                return;
            }

            // 获取所有md文件，按最后修改时间排序（最新的在前）
            var mdFiles = Directory.GetFiles(announcementDir, "*.md")
                .OrderBy(f => Path.GetFileName(f)[0])  // 按文件名的首字母升序排列
                .ToList();

            foreach (var mdFile in mdFiles)
            {
                try
                {
                    var content = File.ReadAllText(mdFile);
                    var lines = content.Split([Environment.NewLine], 2, StringSplitOptions.None);

                    if (lines.Length >= 2 && !string.IsNullOrWhiteSpace(lines[0]))
                    {
                        // 第一行为标题，其余为内容
                        var item = new AnnouncementItem
                        {
                            Title = lines[0].Trim(),
                            Content = lines[1],
                            FilePath = mdFile,
                            LastModified = File.GetLastWriteTime(mdFile)
                        };
                        AnnouncementItems.Add(item);
                    }
                }
                catch (Exception ex)
                {
                    LoggerHelper.Error($"读取公告文件失败: {mdFile}, 错误: {ex.Message}");
                }
            }

            // 默认选中第一个公告
            if (AnnouncementItems.Any())
            {
                SelectedAnnouncement = AnnouncementItems[0];
            }
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"加载公告文件夹失败: {ex.Message}");
        }
    }

    public static void CheckAnnouncement(bool forceShow = false)
    {
        var viewModel = new AnnouncementViewModel();
        if (forceShow)
        {
            if (!viewModel.AnnouncementItems.Any()) 
                ToastHelper.Warn("Warning".ToLocalization(),"AnnouncementEmpty".ToLocalization());
        }
        else if (viewModel.DoNotRemindThisAnnouncementAgain || !viewModel.AnnouncementItems.Any())
            return;

        try
        {
            var announcementView = new AnnouncementView
            {
                DataContext = viewModel
            };
            announcementView.Show();
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"显示公告窗口失败: {ex.Message}");
        }
    }
}
