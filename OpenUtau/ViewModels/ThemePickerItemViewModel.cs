using Avalonia.Media;
using OpenUtau.Colors;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace OpenUtau.App.ViewModels {
    public partial class ThemePickerItemViewModel : ViewModelBase {
        public string Name { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public string Author { get; init; } = string.Empty;
        public bool HasAuthor => !string.IsNullOrWhiteSpace(Author);
        public bool IsBuiltIn { get; init; }
        public bool IsPackageTheme { get; init; }
        public bool IsHubTheme { get; init; }
        public bool IsCreateTile { get; init; }
        public bool IsImportTile { get; init; }
        public bool IsActionTile => IsCreateTile || IsImportTile;
        public bool IsEditable => !IsBuiltIn && !IsPackageTheme && !IsHubTheme && !IsActionTile;
        [Reactive] public partial bool IsSelected { get; set; }
        public IBrush BackgroundBrush { get; init; } = Brushes.Transparent;
        public IBrush CanvasBrush { get; init; } = Brushes.Transparent;
        public IBrush CardBrush { get; init; } = Brushes.Transparent;
        public IBrush TrackAltBrush { get; init; } = Brushes.Transparent;
        public IBrush NoteBrush { get; init; } = Brushes.Transparent;
        public IBrush AccentBrush { get; init; } = Brushes.Transparent;

        public static ThemePickerItemViewModel FromThemeName(string name) {
            var colors = ThemePreviewFactory.GetPreviewColors(name);
            var displayName = GetDisplayName(name);
            return new ThemePickerItemViewModel {
                Name = name,
                DisplayName = displayName,
                Author = CustomTheme.TryGetAuthor(name) ?? string.Empty,
                IsBuiltIn = BuiltInThemeLoader.IsBuiltInTheme(name),
                IsPackageTheme = CustomTheme.IsPackageTheme(name),
                IsHubTheme = CustomTheme.IsHubTheme(name),
                BackgroundBrush = colors.Background,
                CanvasBrush = colors.Canvas,
                CardBrush = colors.Card,
                TrackAltBrush = colors.TrackAlt,
                NoteBrush = colors.Note,
                AccentBrush = colors.Accent,
            };
        }

        public static ThemePickerItemViewModel CreateAddTile() {
            return new ThemePickerItemViewModel {
                IsCreateTile = true,
                DisplayName = ThemeManager.GetString("prefs.appearance.customtheme.create.short"),
            };
        }

        public static ThemePickerItemViewModel CreateImportTile() {
            return new ThemePickerItemViewModel {
                IsImportTile = true,
                DisplayName = ThemeManager.GetString("prefs.appearance.customtheme.import.short"),
            };
        }

        static string GetDisplayName(string name) {
            var key = $"prefs.appearance.theme.name.{name.ToLowerInvariant()}";
            var localized = ThemeManager.GetString(key);
            return localized == key ? name : localized;
        }
    }
}
