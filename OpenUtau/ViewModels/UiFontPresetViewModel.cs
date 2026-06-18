using Avalonia.Media;
using OpenUtau.App.Fonts;
using ReactiveUI;

namespace OpenUtau.App.ViewModels;

public class UiFontPresetViewModel : ViewModelBase {
    public string Id { get; init; } = UiFontCatalog.ClassicPresetId;
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public FontFamily PreviewFontFamily { get; init; } = FontFamily.Default;

    public static UiFontPresetViewModel FromPreset(UiFontCatalog.Preset preset) {
        var displayName = ThemeManager.GetString(preset.NameResourceKey);
        if (displayName == preset.NameResourceKey) {
            displayName = preset.Id;
        }
        var description = preset.DescriptionResourceKey != null
            ? ThemeManager.GetString(preset.DescriptionResourceKey)
            : string.Empty;
        if (description == preset.DescriptionResourceKey) {
            description = string.Empty;
        }
        return new UiFontPresetViewModel {
            Id = preset.Id,
            DisplayName = displayName,
            Description = description,
            PreviewFontFamily = UiFontCatalog.CreateFontFamily(preset),
        };
    }
}
