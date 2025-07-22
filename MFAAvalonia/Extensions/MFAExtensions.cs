using Avalonia.Media.Imaging;
using AvaloniaExtensions.Axaml.Markup;
using MaaFramework.Binding.Buffers;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using MFAAvalonia.ViewModels.Other;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using Bitmap = Avalonia.Media.Imaging.Bitmap;

namespace MFAAvalonia.Extensions;

public static class MFAExtensions
{
    public static string GetFallbackCommand()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "cmd.exe"
            : "/bin/bash";
    }

    public static Dictionary<TKey, MaaNode> MergeMaaNodes<TKey>(
        this IEnumerable<KeyValuePair<TKey, MaaNode>>? taskModels,
        IEnumerable<KeyValuePair<TKey, MaaNode>>? additionalModels) where TKey : notnull
    {

        if (additionalModels == null)
            return taskModels?.ToDictionary() ?? new Dictionary<TKey, MaaNode>();
        return taskModels?
                .Concat(additionalModels)
                .GroupBy(pair => pair.Key)
                .ToDictionary(
                    group => group.Key,
                    group =>
                    {
                        var mergedModel = group.First().Value;
                        foreach (var taskModel in group.Skip(1))
                        {
                            mergedModel.Merge(taskModel.Value);
                        }
                        return mergedModel;
                    }
                )
            ?? new Dictionary<TKey, MaaNode>();
    }

    public static Dictionary<TKey, JToken> MergeJTokens<TKey>(
        this IEnumerable<KeyValuePair<TKey, JToken>>? taskModels,
        IEnumerable<KeyValuePair<TKey, JToken>>? additionalModels) where TKey : notnull
    {

        if (additionalModels == null)
            return taskModels?.ToDictionary() ?? new Dictionary<TKey, JToken>();
        return taskModels?
                .Concat(additionalModels)
                .GroupBy(pair => pair.Key)
                .ToDictionary(
                    group => group.Key,
                    group =>
                    {
                        var mergedModel = group.First().Value;
                        foreach (var taskModel in group.Skip(1))
                        {
                            mergedModel.Merge(taskModel.Value);
                        }
                        return mergedModel;
                    }
                )
            ?? new Dictionary<TKey, JToken>();
    }

    public static JToken Merge(this JToken? target, JToken? source)
    {
        if (target == null) return source;
        if (source == null) return target;

        // 确保目标和源都是 JObject 类型
        if (target.Type != JTokenType.Object || source.Type != JTokenType.Object)
            return target;

        var targetObj = (JObject)target;
        var sourceObj = (JObject)source;

        // 遍历源对象的所有属性
        foreach (var property in sourceObj.Properties())
        {
            string propName = property.Name;
            JToken? targetProp = targetObj.Property(propName)?.Value;
            JToken sourceProp = property.Value;

            // 处理 recognition 相关合并逻辑
            if (propName == "recognition")
            {
                if (targetProp != null && targetProp.Type == JTokenType.Object && sourceProp.Type == JTokenType.Object)
                {
                    JObject targetRecognition = (JObject)targetProp;
                    JObject sourceRecognition = (JObject)sourceProp;

                    // 覆盖 type 属性
                    if (sourceRecognition.ContainsKey("type"))
                    {
                        targetRecognition["type"] = sourceRecognition["type"]?.DeepClone() ?? new JValue((string)null);
                    }

                    // 处理 recognition 内部的 param 属性，递归合并
                    if (sourceRecognition.ContainsKey("param") && targetRecognition.ContainsKey("param") && targetRecognition["param"]?.Type == JTokenType.Object && sourceRecognition["param"]?.Type == JTokenType.Object)
                    {
                        targetRecognition["param"] = Merge(targetRecognition["param"], sourceRecognition["param"]);
                    }
                    else if (sourceRecognition.ContainsKey("param") && targetRecognition["param"] == null)
                    {
                        targetRecognition["param"] = sourceRecognition["param"]?.DeepClone();
                    }

                    targetObj[propName] = targetRecognition;
                }
                else if (targetProp == null)
                {
                    targetObj[propName] = sourceProp.DeepClone();
                }
                continue;
            }

            // 处理 action 相关合并逻辑
            if (propName == "action")
            {
                if (targetProp != null && targetProp.Type == JTokenType.Object && sourceProp.Type == JTokenType.Object)
                {
                    JObject targetAction = (JObject)targetProp;
                    JObject sourceAction = (JObject)sourceProp;

                    // 覆盖 type 属性
                    if (sourceAction.ContainsKey("type"))
                    {
                        targetAction["type"] = sourceAction["type"]?.DeepClone() ?? new JValue((string)null);
                    }

                    // 处理 action 内部的 param 属性，递归合并
                    if (sourceAction.ContainsKey("param") && targetAction.ContainsKey("param") && targetAction["param"]?.Type == JTokenType.Object && sourceAction["param"]?.Type == JTokenType.Object)
                    {
                        targetAction["param"] = Merge(targetAction["param"], sourceAction["param"]);
                    }
                    else if (sourceAction.ContainsKey("param") && targetAction["param"] == null)
                    {
                        targetAction["param"] = sourceAction["param"]?.DeepClone();
                    }

                    targetObj[propName] = targetAction;
                }
                else if (targetProp == null)
                {
                    targetObj[propName] = sourceProp.DeepClone();
                }
                continue;
            }

            // 其他普通属性直接替换或添加
            targetObj[propName] = sourceProp.DeepClone();
        }

        return target;
    }

    public static string FormatWith(this string format, params object[] args)
    {
        return string.Format(format, args);
    }

    public static void AddRange<T>(this ICollection<T>? collection, IEnumerable<T> newItems)
    {
        if (collection == null)
            return;
        if (collection is List<T> objList)
        {
            objList.AddRange(newItems);
        }
        else
        {
            foreach (T newItem in newItems)
                collection.Add(newItem);
        }
    }

    public static string ToLocalization(this string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return string.Empty;

        return I18nManager.GetString(key) ?? key;
    }

    public static string ToLocalizationFormatted(this string? key, bool transformKey = true, params string[] args)
    {
        if (string.IsNullOrWhiteSpace(key)) return string.Empty;

        var localizedKey = key.ToLocalization();
        var processedArgs = Array.ConvertAll(args, a => a.ToLocalization() as object);

        try
        {
            return Regex.Unescape(localizedKey.FormatWith(processedArgs));
        }
        catch
        {
            return localizedKey.FormatWith(processedArgs);
        }
    }

    public static bool ContainsKey(this IEnumerable<LocalizationViewModel> settingViewModels, string key)
    {
        return settingViewModels.Any(vm => vm.ResourceKey == key);
    }

    public static bool ShouldSwitchButton(this List<MaaInterface.MaaInterfaceOptionCase>? cases, out int yes, out int no)
    {
        yes = -1;
        no = -1;

        if (cases == null || cases.Count != 2)
            return false;

        var yesItem = cases
            .Select((c, index) => new
            {
                c.Name,
                Index = index
            })
            .Where(x => x.Name?.Equals("yes", StringComparison.OrdinalIgnoreCase) == true).ToList();

        var noItem = cases
            .Select((c, index) => new
            {
                c.Name,
                Index = index
            })
            .Where(x => x.Name?.Equals("no", StringComparison.OrdinalIgnoreCase) == true).ToList();

        if (yesItem.Count == 0 || noItem.Count == 0)
            return false;

        yes = yesItem[0].Index;
        no = noItem[0].Index;

        return true;
    }

    public static void SafeCancel(this CancellationTokenSource? cts, bool useCancel = true)
    {
        if (cts == null || cts.IsCancellationRequested) return;

        try
        {
            if (useCancel) cts.Cancel();
            cts.Dispose();
        }
        catch (Exception e) { Console.WriteLine(e); }
    }

    /// <summary>
    /// 安全移动元素的扩展方法（泛型版本）
    /// </summary>
    /// <param name="targetIndex">目标位置索引应先于实际插入位置</param>
    /// <remarks>当移动方向为向后移动时，实际插入位置会比targetIndex大1[8](@ref)</remarks>
    public static void MoveTo<T>(this IList<T> list, int sourceIndex, int targetIndex) where T : class
    {
        ValidateIndexes(list, sourceIndex, targetIndex);
        if (sourceIndex == targetIndex) return;

        var item = list[sourceIndex];

        list.RemoveAt(sourceIndex);

        list.Insert(targetIndex > sourceIndex ? targetIndex - 1 : targetIndex, item);
    }

    /// <summary>
    /// 安全移动元素的扩展方法（非泛型版本）
    /// </summary>
    public static void MoveTo(this IList list, int sourceIndex, int targetIndex)
    {
        ValidateIndexes(list, sourceIndex, targetIndex);
        if (sourceIndex == targetIndex) return;

        var item = list[sourceIndex];

        list.RemoveAt(sourceIndex);

        list.Insert(targetIndex > sourceIndex ? targetIndex - 1 : targetIndex, item);
    }

    // 扩展方法：范围判断
    public static bool Between(this double value, double min, double max)
        => value >= min && value <= max;
    private static void ValidateIndexes(IList list, int source, int target)
    {
        if (source < 0 || source >= list.Count)
            throw new ArgumentOutOfRangeException(nameof(source), "源索引越界");
        if (target < 0 || target > list.Count)
            throw new ArgumentOutOfRangeException(nameof(target), "目标索引越界");
    }
    private static void ValidateIndexes<T>(IList<T> list, int source, int target)
    {
        if (source < 0 || source >= list.Count)
            throw new ArgumentOutOfRangeException(nameof(source), "源索引越界");
        if (target < 0 || target > list.Count)
            throw new ArgumentOutOfRangeException(nameof(target), "目标索引越界");
    }

    public static string GetName(this VersionChecker.VersionType type)
    {
        return type.ToString().ToLower();
    }

    public static VersionChecker.VersionType ToVersionType(this string version)
    {
        if (version.Contains("alpha", StringComparison.OrdinalIgnoreCase))
            return VersionChecker.VersionType.Alpha;
        if (version.Contains("beta", StringComparison.OrdinalIgnoreCase)) return VersionChecker.VersionType.Beta;
        return VersionChecker.VersionType.Stable;
    }

    public static VersionChecker.VersionType ToVersionType(this int version)
    {
        if (version == 0)
            return VersionChecker.VersionType.Alpha;
        if (version == 1) return VersionChecker.VersionType.Beta;
        return VersionChecker.VersionType.Stable;
    }

    public static Bitmap? ToBitmap(this MaaImageBuffer buffer)
    {
        if (!buffer.TryGetEncodedData(out Stream EncodedDataStream)) return null;

        try
        {
            EncodedDataStream.Seek(0, SeekOrigin.Begin);
            return new Bitmap(EncodedDataStream);
        }
        catch (ArgumentException ex)
        {
            LoggerHelper.Error($"解码失败: {ex.Message}");
            return null;
        }
    }
    public static System.Drawing.Bitmap? ToDrawingBitmap(this Bitmap? bitmap)
    {
        if (bitmap == null)
            return null;

        using var memory = new MemoryStream();

        bitmap.Save(memory);
        memory.Position = 0;
        
        return new System.Drawing.Bitmap(memory);
    }
    
    public static Bitmap? ToAvaloniaBitmap(this System.Drawing.Bitmap? bitmap)
    {
        if (bitmap == null)
            return null;
        var bitmapTmp = new System.Drawing.Bitmap(bitmap);
        var bd = bitmapTmp.LockBits(new Rectangle(0, 0, bitmapTmp.Width, bitmapTmp.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        var bitmap1 = new Bitmap(Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Premul,
            bd.Scan0,
            new Avalonia.PixelSize(bd.Width, bd.Height),
            new Avalonia.Vector(96, 96),
            bd.Stride);
        bitmapTmp.UnlockBits(bd);
        bitmapTmp.Dispose();
        return bitmap1;
    }
}
