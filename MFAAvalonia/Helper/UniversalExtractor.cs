using Avalonia.Controls;
using SharpCompress.Archives;
using SharpCompress.Archives.Tar;
using SharpCompress.Common;
using System.Linq;
using System.Threading.Tasks;

namespace MFAAvalonia.Helper;

using System;
using System.IO;

public class UniversalExtractor
{
    public static void Extract(string compressedFilePath, string destinationDirectory)
    {
        // 创建目标目录（如果不存在）
        Directory.CreateDirectory(destinationDirectory);

        // 根据文件扩展名选择解压方法
        string fileExtension = Path.GetExtension(compressedFilePath).ToLowerInvariant();

        switch (fileExtension)
        {
            case ".zip":
                ExtractZip(compressedFilePath, destinationDirectory);
                break;
            case ".gz":
            case ".tgz":
                ExtractTgz(compressedFilePath, destinationDirectory);
                break;
            case ".tar":
                ExtractTar(compressedFilePath, destinationDirectory);
                break;
            case ".rar":
                ExtractRar(compressedFilePath, destinationDirectory);
                break;
            default:
                throw new NotSupportedException($"不支持的压缩格式: {fileExtension}");
        }
    }

    // 解压ZIP文件
    private static void ExtractZip(string zipFilePath, string destinationDirectory)
    {
        using (var archive = ArchiveFactory.Open(zipFilePath))
        {
            foreach (var entry in archive.Entries)
            {
                if (!entry.IsDirectory)
                {
                    entry.WriteToDirectory(destinationDirectory, new ExtractionOptions
                    {
                        ExtractFullPath = true,
                        Overwrite = true
                    });
                }
            }
        }
    }

    // 解压TGZ文件
    private static void ExtractTgz(string tgzFilePath, string destinationDirectory)
    {
        using (var archive = TarArchive.Open(tgzFilePath))
        {
            foreach (var entry in archive.Entries)
            {
                if (!entry.IsDirectory)
                {
                    entry.WriteToDirectory(destinationDirectory, new ExtractionOptions
                    {
                        ExtractFullPath = true,
                        Overwrite = true
                    });
                }
            }
        }
    }

    // 解压TAR文件
    private static void ExtractTar(string tarFilePath, string destinationDirectory)
    {
        using (var archive = TarArchive.Open(tarFilePath))
        {
            foreach (var entry in archive.Entries)
            {
                if (!entry.IsDirectory)
                {
                    entry.WriteToDirectory(destinationDirectory, new ExtractionOptions
                    {
                        ExtractFullPath = true,
                        Overwrite = true
                    });
                }
            }
        }
    }

    // 解压RAR文件
    private static void ExtractRar(string rarFilePath, string destinationDirectory)
    {
        using (var archive = ArchiveFactory.Open(rarFilePath))
        {
            foreach (var entry in archive.Entries)
            {
                if (!entry.IsDirectory)
                {
                    entry.WriteToDirectory(destinationDirectory, new ExtractionOptions
                    {
                        ExtractFullPath = true,
                        Overwrite = true
                    });
                }
            }
        }
    }


    public async static Task<bool> ExtractAsync(string compressedFilePath, string destinationDirectory, ProgressBar? progressBar = null)
    {
        try
        {
            // 创建目标目录
            Directory.CreateDirectory(destinationDirectory);

            var fileExtension = Path.GetExtension(compressedFilePath).ToLowerInvariant();
            switch (fileExtension)
            {
                case ".zip":
                    await ExtractZipAsync(compressedFilePath, destinationDirectory, progressBar);
                    break;
                case ".gz":
                case ".tgz":
                    await ExtractTarGzAsync(compressedFilePath, destinationDirectory, progressBar);
                    break;
                case ".tar":
                    await ExtractTarAsync(compressedFilePath, destinationDirectory, progressBar);
                    break;
                case ".rar":
                    await ExtractRarAsync(compressedFilePath, destinationDirectory, progressBar);
                    break;
                default:
                    throw new NotSupportedException($"不支持的压缩格式: {fileExtension}");
            }
            return true;
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"解压失败: {ex.Message}");
            return false;
        }
    }

    // 异步解压ZIP文件（带进度）
    async private static Task ExtractZipAsync(string zipFilePath, string destinationDirectory, ProgressBar? progressBar = null)
    {
        using (var archive = ArchiveFactory.Open(zipFilePath))
        {
            var totalEntries = archive.Entries.Count();
            int processedEntries = 0;

            foreach (var entry in archive.Entries)
            {
                if (!entry.IsDirectory)
                {
                    entry.WriteToDirectory(destinationDirectory, new ExtractionOptions
                    {
                        ExtractFullPath = true,
                        Overwrite = true
                    });
                }

                processedEntries++;
                double progress = (double)processedEntries / totalEntries * 100;
                SetProgress(progressBar, progress);

                // 允许UI线程更新
                await Task.Yield();
            }
        }
    }

    // 异步解压TarGz文件（带进度）
    async private static Task ExtractTarGzAsync(string tgzFilePath, string destinationDirectory, ProgressBar? progressBar = null)
    {
        using (var archive = TarArchive.Open(tgzFilePath))
        {
            var totalEntries = archive.Entries.Count;
            int processedEntries = 0;

            foreach (var entry in archive.Entries)
            {
                if (!entry.IsDirectory)
                {
                    entry.WriteToDirectory(destinationDirectory, new ExtractionOptions
                    {
                        ExtractFullPath = true,
                        Overwrite = true
                    });
                }

                processedEntries++;
                double progress = (double)processedEntries / totalEntries * 100;
                SetProgress(progressBar, progress);

                // 允许UI线程更新
                await Task.Yield();
            }
        }
    }

    // 异步解压Tar文件（带进度）
    private static async Task ExtractTarAsync(string tarFilePath, string destinationDirectory, ProgressBar? progressBar = null)
    {
        using (var archive = TarArchive.Open(tarFilePath))
        {
            var totalEntries = archive.Entries.Count;
            int processedEntries = 0;

            foreach (var entry in archive.Entries)
            {
                if (!entry.IsDirectory)
                {
                    entry.WriteToDirectory(destinationDirectory, new ExtractionOptions
                    {
                        ExtractFullPath = true,
                        Overwrite = true
                    });
                }

                processedEntries++;
                double progress = (double)processedEntries / totalEntries * 100;
                SetProgress(progressBar, progress);

                // 允许UI线程更新
                await Task.Yield();
            }
        }
    }

    // 异步解压RAR文件（带进度）
    private static async Task ExtractRarAsync(string rarFilePath, string destinationDirectory, ProgressBar? progressBar = null)
    {
        using (var archive = ArchiveFactory.Open(rarFilePath))
        {
            var totalEntries = archive.Entries.Count();
            int processedEntries = 0;

            foreach (var entry in archive.Entries)
            {
                if (!entry.IsDirectory)
                {
                    entry.WriteToDirectory(destinationDirectory, new ExtractionOptions
                    {
                        ExtractFullPath = true,
                        Overwrite = true
                    });
                }

                processedEntries++;
                double progress = (double)processedEntries / totalEntries * 100;
                SetProgress(progressBar, progress);

                // 允许UI线程更新
                await Task.Yield();
            }
        }
    }

    // 设置进度条（与你现有代码保持一致）
    private static void SetProgress(ProgressBar? progressBar, double value)
    {
        if (progressBar != null)
        {
            DispatcherHelper.RunOnMainThread(() =>
            {
                progressBar.Value = value;
            });
        }
    }
}
