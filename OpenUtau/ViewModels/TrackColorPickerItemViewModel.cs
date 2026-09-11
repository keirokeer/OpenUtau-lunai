using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace OpenUtau.App.ViewModels {
    public partial class TrackColorPickerItemViewModel : ViewModelBase {
        public TrackColor? Color { get; init; }
        public bool IsCreateTile { get; init; }
        public bool IsCustom => Color?.IsCustom ?? false;
        public bool IsDeletable => IsCustom;
        public string ToolTipName => IsCreateTile
            ? ThemeManager.GetString("prefs.appearance.customtrackcolor.create.short")
            : Color?.Name ?? string.Empty;

        [Reactive] public partial bool IsSelected { get; set; }

        public static TrackColorPickerItemViewModel FromColor(TrackColor color) => new() { Color = color };

        public static TrackColorPickerItemViewModel CreateAddTile() => new() { IsCreateTile = true };
    }
}
