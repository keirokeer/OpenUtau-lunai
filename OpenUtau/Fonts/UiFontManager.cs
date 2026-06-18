using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using OpenUtau.App.Controls;
using OpenUtau.App.ViewModels;
using OpenUtau.Core.Util;
using ReactiveUI;
using Serilog;

namespace OpenUtau.App.Fonts;

public static class UiFontManager {
    public const string ResourceKey = "ui.fontfamily";

    static readonly (string Key, double BaseSize)[] FontSizeSteps = [
        ("ui.fontsize.7.5", 7.5),
        ("ui.fontsize.8.5", 8.5),
        ("ui.fontsize.10", 10),
        ("ui.fontsize.11", 11),
        ("ui.fontsize.12", 12),
        ("ui.fontsize.13", 13),
        ("ui.fontsize.15", 15),
    ];

    public static void Apply(string? presetId = null) {
        if (Application.Current == null) {
            return;
        }
        presetId = UiFontCatalog.NormalizePresetId(presetId ?? Preferences.Default.UiFontPreset);
        if (!string.Equals(Preferences.Default.UiFontPreset, presetId, System.StringComparison.OrdinalIgnoreCase)) {
            Preferences.Default.UiFontPreset = presetId;
            Preferences.Save();
        }

        Application.Current.Resources[ResourceKey] = ResolveFontFamily(presetId);
        ApplyFontSizeResources(presetId);
        Log.Information("Applied UI font preset {Preset}", presetId);
        TextLayoutCache.Clear();
        MessageBus.Current.SendMessage(new NotesRefreshEvent());
        MessageBus.Current.SendMessage(new PianorollRefreshEvent("Part"));
    }

    public static double GetFontSizeScale() {
        return UiFontCatalog.UsesInterMetrics(Preferences.Default.UiFontPreset)
            ? UiFontCatalog.InterFontSizeScale
            : 1.0;
    }

    public static double ScaleFontSize(double size) {
        return size * GetFontSizeScale();
    }

    public static FontFamily GetUiFontFamily() {
        if (Application.Current != null
            && Application.Current.Resources.TryGetResource(ResourceKey, ThemeVariant.Default, out var resource)
            && resource is FontFamily fontFamily) {
            return fontFamily;
        }
        return FontFamily.Default;
    }

    static void ApplyFontSizeResources(string presetId) {
        if (Application.Current == null) {
            return;
        }
        var scale = UiFontCatalog.UsesInterMetrics(presetId)
            ? UiFontCatalog.InterFontSizeScale
            : 1.0;
        foreach (var (key, baseSize) in FontSizeSteps) {
            Application.Current.Resources[key] = baseSize * scale;
        }
        Application.Current.Resources["ui.fontsize.scale"] = scale;
    }

    static FontFamily ResolveFontFamily(string presetId) {
        if (string.Equals(presetId, UiFontCatalog.ClassicPresetId, System.StringComparison.OrdinalIgnoreCase)) {
            return ResolveClassicFontFamily();
        }
        var preset = UiFontCatalog.TryGetPreset(presetId);
        if (preset == null) {
            return ResolveClassicFontFamily();
        }
        return UiFontCatalog.CreateFontFamily(preset);
    }

    static FontFamily ResolveClassicFontFamily() {
        FontFamily? localeFont = null;
        if (Application.Current != null) {
            foreach (var dictionary in Application.Current.Resources.MergedDictionaries) {
                if (dictionary is IResourceDictionary resourceDictionary
                    && resourceDictionary.TryGetResource(ResourceKey, ThemeVariant.Default, out var resource)
                    && resource is FontFamily fontFamily) {
                    localeFont = fontFamily;
                }
            }
        }
        if (localeFont != null) {
            return localeFont;
        }
        return new FontFamily(UiFontCatalog.LocaleScriptFallbacks);
    }
}

public class UiFontChangedEvent { }
