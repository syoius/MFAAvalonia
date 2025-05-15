using Avalonia.Collections;
using MFAAvalonia.Helper;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MFAAvalonia.Configuration;

public static class ConfigurationManager
{
    private static readonly string _configDir = Path.Combine(
        AppContext.BaseDirectory,
        "config");
    public static readonly MFAConfiguration Maa = new("Maa", "maa_option", new Dictionary<string, object>());
    public static MFAConfiguration Current = new("Default", "config", new Dictionary<string, object>());

    public static AvaloniaList<MFAConfiguration> Configs { get; } = LoadConfigurations();

    public static string ConfigName { get; set; }
    public static string GetCurrentConfiguration() => ConfigName;

    public static string GetActualConfiguration()
    {
        if (ConfigName.Equals("Default", StringComparison.OrdinalIgnoreCase))
            return "config";
        return GetCurrentConfiguration();
    }

    public static void Initialize()
    {
        LoggerHelper.Info("Current Configuration: " + GetCurrentConfiguration());
    }

    public static void SetDefaultConfig(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;
        GlobalConfiguration.SetValue(ConfigurationKeys.DefaultConfig, name);
    }

    public static string GetDefaultConfig()
    {
        return GlobalConfiguration.GetValue(ConfigurationKeys.DefaultConfig, "Default");
    }

    private static AvaloniaList<MFAConfiguration> LoadConfigurations()
    {
        LoggerHelper.Info("Loading Configurations...");
        ConfigName = GetDefaultConfig();

        var collection = new AvaloniaList<MFAConfiguration>();

        var defaultConfigPath = Path.Combine(_configDir, "config.json");
        if (!Directory.Exists(_configDir))
            Directory.CreateDirectory(_configDir);
        if (!File.Exists(defaultConfigPath))
            File.WriteAllText(defaultConfigPath, "{}");
        collection.Add(Current.SetConfig(JsonHelper.LoadConfig("config", new Dictionary<string, object>())));
        foreach (var file in Directory.EnumerateFiles(_configDir, "*.json"))
        {
            var fileName = Path.GetFileNameWithoutExtension(file);
            if (fileName == "maa_option" || fileName == "config") continue;
            var configs = JsonHelper.LoadConfig(fileName, new Dictionary<string, object>());

            var config = new MFAConfiguration(fileName, fileName, configs);

            collection.Add(config);
        }

        Maa.SetConfig(JsonHelper.LoadConfig("maa_option", new Dictionary<string, object>()));
       
        if (Program.Args.TryGetValue("c", out var param) && !string.IsNullOrEmpty(param))
        {
            if (collection.Any(c => c.Name == param))
                ConfigName = param;
        }
        Current = collection.FirstOrDefault(c
                => !string.IsNullOrWhiteSpace(c.Name)
                && c.Name.Equals(ConfigName, StringComparison.OrdinalIgnoreCase))
            ?? Current;

        return collection;
    }

    public static void SaveConfiguration(string configName)
    {
        var config = Configs.FirstOrDefault(c => c.Name == configName);
        if (config != null)
        {
            JsonHelper.SaveConfig(config.FileName, config.Config);
        }
    }

    public static MFAConfiguration Add(string name)
    {
        var configPath = Path.Combine(AppContext.BaseDirectory, "config");
        var newConfigPath = Path.Combine(configPath, $"{name}.json");
        var newConfig = new MFAConfiguration(name.Equals("config", StringComparison.OrdinalIgnoreCase) ? "Default" : name, name, new Dictionary<string, object>());
        Configs.Add(newConfig);
        return newConfig;
    }
}
