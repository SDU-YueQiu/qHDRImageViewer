using System.Text;
using HdrImageViewer.Services;
using Vortice.DXGI;

namespace HdrImageViewer.Settings;

/// <summary>
/// Builds the read-only diagnostics block shown on the advanced settings page.
/// The GPU adapter name is best-effort: when DXGI is unavailable the line says so
/// instead of failing the page.
/// </summary>
internal static class SettingsDiagnostics
{
    public static string GetText()
    {
        var version = typeof(SettingsDiagnostics).Assembly.GetName().Version?.ToString() ?? string.Empty;
        var settingsPath = AppSettingsService.SettingsFilePath;
        var language = Localization.GetAppliedLanguageOverride();
        var languageText = string.IsNullOrWhiteSpace(language)
            ? Localization.GetString("SettingsLanguageSystem")
            : language;
        var theme = Localization.GetString(AppSettingsService.Current.Theme switch
        {
            AppTheme.Light => "SettingsThemeLight",
            AppTheme.Dark => "SettingsThemeDark",
            _ => "SettingsThemeSystem",
        });
        var gpu = TryGetGpuAdapterName() ?? Localization.GetString("SettingsDiagnosticsUnavailable");

        var builder = new StringBuilder();
        builder.AppendLine(Localization.GetString("SettingsDiagnosticsVersionFormat", version));
        builder.AppendLine(Localization.GetString("SettingsDiagnosticsPathFormat", settingsPath));
        builder.AppendLine(Localization.GetString("SettingsDiagnosticsLanguageFormat", languageText));
        builder.AppendLine(Localization.GetString("SettingsDiagnosticsThemeFormat", theme));
        builder.Append(Localization.GetString("SettingsDiagnosticsGpuFormat", gpu));
        return builder.ToString();
    }

    private static string? TryGetGpuAdapterName()
    {
        try
        {
            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
            var result = factory.EnumAdapters1(0u, out var adapter);
            using (adapter)
            {
                if (result.Failure || adapter is null)
                {
                    return null;
                }

                var description = adapter.Description1.Description;
                if (string.IsNullOrWhiteSpace(description))
                {
                    return null;
                }

                return description.TrimEnd('\0').Trim();
            }
        }
        catch
        {
            return null;
        }
    }
}
