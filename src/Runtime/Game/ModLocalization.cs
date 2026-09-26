using STS2RitsuLib;
using STS2RitsuLib.Utils;

namespace Notes.Game;

/// <summary>Mod strings via RitsuLib I18N (embedded zhs/eng tables), with a
/// literal fallback so the UI never shows a missing key.</summary>
internal static class ModLocalization
{
    private static I18N? _i18n;

    public static void Initialize()
    {
        _i18n ??= RitsuLibFramework.CreateModLocalizationWithFallback(
            modId: Entry.ModId,
            instanceName: "Notes",
            resourceFolders: new[] { "Notes.Localization" },
            fallbackLanguage: "eng");
    }

    public static string T(string key, string fallback) => _i18n?.Get(key, fallback) ?? fallback;

    public static bool IsChinese =>
        string.Equals(I18N.ResolveCurrentLanguageCode(), "zhs", StringComparison.OrdinalIgnoreCase);
}
