using System.Globalization;
using System.Windows;

namespace TodoDesktopApp.Services;

public static class LocalizationService
{
    public const string DefaultLanguage = "zh-CN";

    private static readonly IReadOnlyList<LanguageOption> SupportedLanguages =
    [
        new("zh-CN", "简体中文"),
        new("en-US", "English")
    ];

    public static event EventHandler? LanguageChanged;

    public static IReadOnlyList<LanguageOption> Languages => SupportedLanguages;

    public static string CurrentLanguage { get; private set; } = DefaultLanguage;

    public static void ApplyLanguage(string? language)
    {
        var normalizedLanguage = NormalizeLanguage(language);
        var culture = CultureInfo.GetCultureInfo(normalizedLanguage);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;

        var resources = System.Windows.Application.Current.Resources.MergedDictionaries;
        for (var index = resources.Count - 1; index >= 0; index--)
        {
            if (IsLocalizationDictionary(resources[index]))
            {
                resources.RemoveAt(index);
            }
        }

        resources.Insert(0, new ResourceDictionary
        {
            Source = new Uri($"Localization/Strings.{normalizedLanguage}.xaml", UriKind.Relative)
        });

        CurrentLanguage = normalizedLanguage;
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    public static string Text(string key)
    {
        return System.Windows.Application.Current.TryFindResource(key) as string ?? key;
    }

    public static string Format(string key, params object?[] args)
    {
        return string.Format(CultureInfo.CurrentCulture, Text(key), args);
    }

    public static string NormalizeLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return DefaultLanguage;
        }

        return SupportedLanguages.Any(option => option.Key == language)
            ? language
            : DefaultLanguage;
    }

    private static bool IsLocalizationDictionary(ResourceDictionary dictionary)
    {
        return dictionary.Source?.OriginalString.StartsWith("Localization/Strings.", StringComparison.OrdinalIgnoreCase) == true;
    }
}

public sealed record LanguageOption(string Key, string DisplayName);
