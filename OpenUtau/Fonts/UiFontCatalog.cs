using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;

namespace OpenUtau.App.Fonts;

public static class UiFontCatalog {
    public const string ClassicPresetId = "system";
    public const string InterPresetId = "inter";
    public const string BundledAssemblyName = "OpenUtau";

    /// <summary>Inter reads larger than Segoe UI at the same pt size; ~4% smaller than classic metrics.</summary>
    public const double InterFontSizeScale = 11.5 / 12.0;

    public const string LocaleScriptFallbacks =
        "Segoe UI, Meiryo UI, \"Microsoft YaHei UI\", \"PingFang SC\", \"Noto Sans Thai\", sans-serif";

    public sealed record Preset(
        string Id,
        string NameResourceKey,
        string? BundledFolder,
        string? BundledFamilyName,
        string? DescriptionResourceKey = null);

    public static readonly IReadOnlyList<Preset> Presets = [
        new(ClassicPresetId,
            "prefs.appearance.font.classic",
            null,
            null,
            "prefs.appearance.font.classic.description"),
        new(InterPresetId,
            "prefs.appearance.font.inter",
            "Inter",
            "Inter",
            "prefs.appearance.font.inter.description"),
    ];

    public static readonly IReadOnlyDictionary<string, string> LegacyPresetIds = new Dictionary<string, string> {
        ["noto-sans"] = ClassicPresetId,
        ["source-sans-3"] = ClassicPresetId,
        ["roboto"] = ClassicPresetId,
        ["segoe-ui"] = ClassicPresetId,
        ["cascadia"] = ClassicPresetId,
        ["bahnschrift"] = ClassicPresetId,
        ["ibm-plex-sans"] = ClassicPresetId,
        ["dm-sans"] = InterPresetId,
        ["nunito-sans"] = InterPresetId,
    };

    public static string NormalizePresetId(string? presetId) {
        if (string.IsNullOrWhiteSpace(presetId)) {
            return ClassicPresetId;
        }
        if (LegacyPresetIds.TryGetValue(presetId, out var mapped)) {
            return mapped;
        }
        return IsValidPreset(presetId) ? presetId : ClassicPresetId;
    }

    public static bool IsValidPreset(string? presetId) {
        return !string.IsNullOrWhiteSpace(presetId)
            && Presets.Any(preset => string.Equals(preset.Id, presetId, System.StringComparison.OrdinalIgnoreCase));
    }

    public static bool UsesInterMetrics(string? presetId) {
        return string.Equals(NormalizePresetId(presetId), InterPresetId, System.StringComparison.OrdinalIgnoreCase);
    }

    public static Preset? TryGetPreset(string? presetId) {
        presetId = NormalizePresetId(presetId);
        return Presets.FirstOrDefault(preset => string.Equals(preset.Id, presetId, System.StringComparison.OrdinalIgnoreCase));
    }

    public static FontFamily CreateFontFamily(Preset preset) {
        if (!string.IsNullOrWhiteSpace(preset.BundledFolder)) {
            return new FontFamily(BuildBundledSpec(preset.BundledFolder!, preset.BundledFamilyName!));
        }
        return new FontFamily(LocaleScriptFallbacks);
    }

    public static string BuildBundledSpec(string folder, string familyName) {
        return $"avares://{BundledAssemblyName}/Assets/Fonts/{folder}#{familyName}, {LocaleScriptFallbacks}";
    }
}
