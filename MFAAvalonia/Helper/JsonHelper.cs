using MFAAvalonia.Extensions;
using MFAAvalonia.Helper;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Reflection;

namespace MFAAvalonia.Helper;

public static class JsonHelper
{
    public static T LoadJson<T>(string filePath, T defaultValue = default, params JsonConverter[] converters)
    {

        try
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                LoggerHelper.Info($"自动创建配置目录：{directory}"); // 可选日志
            }

            if (!File.Exists(filePath)) return defaultValue;

            var settings = new JsonSerializerSettings();
            if (converters is { Length: > 0 })
            {
                settings.Converters.AddRange(converters);
            }

            var json = File.ReadAllText(filePath);
            return JsonConvert.DeserializeObject<T>(json, settings) ?? defaultValue;
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"配置加载失败：{Path.GetFileName(filePath)}", ex);
            return defaultValue;
        }
    }

    public static void SaveJson<T>(string filePath, T config, params JsonConverter[] converters)
    {
        try
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                LoggerHelper.Info($"自动创建配置目录：{directory}"); // 可选日志
            }

            if (!File.Exists(filePath))
                File.WriteAllText(filePath, "{}");

            var settings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Ignore,
                DefaultValueHandling = DefaultValueHandling.Ignore
            };
            if (converters is { Length: > 0 })
            {
                settings.Converters.AddRange(converters);
            }
            var json = JsonConvert.SerializeObject(config, settings);
            File.WriteAllText(filePath, json);
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"配置保存失败：{Path.GetFileName(filePath)}", ex);
        }
    }

    public static T LoadConfig<T>(string configName, T defaultValue = default, params JsonConverter[] converters)
    {

        var exeDir = Path.GetDirectoryName(AppContext.BaseDirectory);
        var configDir = Path.Combine(exeDir, "config");


        if (!Directory.Exists(configDir))
        {
            Directory.CreateDirectory(configDir);
        }
        var filePath = Path.Combine(configDir, $"{configName}.json");
        return LoadJson(filePath, defaultValue, converters);
    }

    public static void SaveConfig<T>(string configName, T config, params JsonConverter[] converters)
    {
        var exeDir = Path.GetDirectoryName(AppContext.BaseDirectory);
        var configDir = Path.Combine(exeDir, "config");


        if (!Directory.Exists(configDir))
        {
            Directory.CreateDirectory(configDir);
        }
        var filePath = Path.Combine(configDir, $"{configName}.json");
        SaveJson(filePath, config, converters);
    }
}
