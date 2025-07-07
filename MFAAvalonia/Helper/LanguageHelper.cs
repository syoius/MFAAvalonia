using Avalonia.Collections;
using Avalonia.Markup.Xaml.MarkupExtensions;
using AvaloniaExtensions.Axaml.Markup;
using CommunityToolkit.Mvvm.ComponentModel;
using MFAAvalonia.Configuration;
using MFAAvalonia.ViewModels.Other;
using Newtonsoft.Json;
using SukiUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;

namespace MFAAvalonia.Helper;

public static class LanguageHelper
{
    public static event EventHandler<LanguageEventArgs>? LanguageChanged;

    public static readonly List<SupportedLanguage> SupportedLanguages =
    [
        new("zh-hans", "简体中文"),
        new("zh-hant", "繁體中文"),
        new("en-us", "English"),
    ];

    public static Dictionary<string, CultureInfo> Cultures { get; } = new() {};

    public static SupportedLanguage GetLanguage(string key)
    {
        return SupportedLanguages.FirstOrDefault(lang => lang.Key == key, SupportedLanguages[0]);
    }

    public static void ChangeLanguage(SupportedLanguage language)
    {
        // 设置应用程序的文化
        I18nManager.Instance.Culture = Cultures.TryGetValue(language.Key, out var culture)
            ? culture
            : Cultures[language.Key] = new CultureInfo(language.Key);
        _currentLanguage = language.Key;
        SukiTheme.GetInstance().Locale = I18nManager.Instance.Culture;
        LanguageChanged?.Invoke(null, new LanguageEventArgs(language));
    }

    public static void ChangeLanguage(string language)
    {
        I18nManager.Instance.Culture = Cultures.TryGetValue(language, out var culture)
            ? culture
            : Cultures[language] = new CultureInfo(language);
        _currentLanguage = language;
        SukiTheme.GetInstance().Locale = I18nManager.Instance.Culture;
        LanguageChanged?.Invoke(null, new LanguageEventArgs(GetLanguage(language)));
    }

    // 存储语言的字典
    private static readonly Dictionary<string, Dictionary<string, string>> Langs = new();
    private static string _currentLanguage = ConfigurationManager.Current.GetValue(ConfigurationKeys.CurrentLanguage, LanguageHelper.SupportedLanguages[0].Key, ["zh-hans", "zh-hant", "en-us"]);
    public static string CurrentLanguage => _currentLanguage;
    public static void Initialize()
    {
        LoggerHelper.Info("Initializing LanguageManager...");
        LoadLanguages();
    }

    private static void LoadLanguages()
    {
        var langPath = Path.Combine(AppContext.BaseDirectory, "lang");
        if (Directory.Exists(langPath))
        {
            var langFiles = Directory.GetFiles(langPath, "*.json");
            foreach (string langFile in langFiles)
            {
                var langCode = Path.GetFileNameWithoutExtension(langFile).ToLower();
                if (IsSimplifiedChinese(langCode))
                {
                    langCode = "zh-hans";
                }
                else if (IsTraditionalChinese(langCode))
                {
                    langCode = "zh-hant";
                }
                var jsonContent = File.ReadAllText(langFile);
                var langResources = JsonConvert.DeserializeObject<Dictionary<string, string>>(jsonContent);
                if (langResources is not null)
                    Langs[langCode] = langResources;
            }
        }
    }

    private static Dictionary<string, string> GetLocalizedStrings()
    {
        return Langs.TryGetValue(_currentLanguage,
            out var dict)
            ? dict
            : new Dictionary<string, string>();
    }

    public static string GetLocalizedString(string? key)
    {
        if (key == null)
            return string.Empty;
        return GetLocalizedStrings().GetValueOrDefault(key, key);
    }


    private static bool IsSimplifiedChinese(string langCode)
    {
        string[] simplifiedPrefixes =
        [
            "zh-hans",
            "zh-cn",
            "zh-sg"
        ];
        foreach (string prefix in simplifiedPrefixes)
        {
            if (langCode.StartsWith(prefix))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsTraditionalChinese(string langCode)
    {
        string[] traditionalPrefixes =
        [
            "zh-hant",
            "zh-tw",
            "zh-hk",
            "zh-mo"
        ];
        foreach (string prefix in traditionalPrefixes)
        {
            if (langCode.StartsWith(prefix))
            {
                return true;
            }
        }
        return false;
    }

    public class LanguageEventArgs(SupportedLanguage language) : EventArgs
    {
        public SupportedLanguage Value { get; set; } = language;
    }
}
