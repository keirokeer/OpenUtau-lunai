using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reactive.Linq;
using Avalonia.Media;
using OpenUtau.Colors;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace OpenUtau.App.ViewModels;

public partial class ThemeEditorStateChangedEvent { }

public partial class ThemeEditorSavedEvent { }

public partial class OpenDockedThemeEditorEvent {
    public required string Path { get; init; }
}

public partial class CloseDockedThemeEditorEvent { }

public static class ThemeEditorDockState {
    public static bool IsOpen { get; private set; }

    public static void SetOpen(bool open) {
        if (IsOpen == open) {
            return;
        }
        IsOpen = open;
        MessageBus.Current.SendMessage(new ThemeEditorStateChangedEvent());
    }
}

public partial class ThemeEditorViewModel : ViewModelBase {
    readonly string customThemePath;
    readonly string themeName;

    [Reactive] public partial bool IsDarkMode { get; set; }
    public ObservableCollection<ThemeColorSectionViewModel> Sections { get; } = [];

    public ThemeEditorViewModel(string customThemePath) {
        this.customThemePath = customThemePath;
        var themeYaml = ThemeYaml.LoadFromFile(customThemePath);
        themeName = themeYaml.Name;
        IsDarkMode = themeYaml.IsDarkMode;

        foreach (var section in ThemeColorCatalog.Sections) {
            var fields = new List<ThemeColorFieldViewModel>();
            foreach (var key in section.Keys) {
                var field = new ThemeColorFieldViewModel(
                    key,
                    ThemeColorStorage.ParseOrDefault(themeYaml.GetColor(key), Color.Parse("#000000")));
                field.WhenAnyValue(viewModel => viewModel.Color)
                    .Skip(1)
                    .Subscribe((Color _) => ApplyPreview());
                fields.Add(field);
            }
            Sections.Add(new ThemeColorSectionViewModel(section.Title, fields));
        }

        this.WhenAnyValue(viewModel => viewModel.IsDarkMode)
            .Skip(1)
            .Subscribe(_ => ApplyPreview());

        ApplyPreview();
    }

    ThemeYaml BuildCurrentYaml() {
        var yaml = ThemeYaml.LoadFromFile(customThemePath);
        yaml.Name = themeName;
        yaml.IsDarkMode = IsDarkMode;
        foreach (var section in Sections) {
            foreach (var field in section.Fields) {
                yaml.SetColor(field.Key, ThemeColorStorage.ToStorageString(field.Color));
            }
        }
        var defaults = BuiltInThemeLoader.CreateFromBuiltIn(IsDarkMode ? "Dark" : "Light", themeName);
        yaml.FillMissingFrom(defaults);
        ThemePaletteNormalizer.Normalize(yaml);
        return yaml;
    }

    void ApplyPreview() {
        ThemeApplicator.Apply(BuildCurrentYaml());
    }

    public void Save() {
        BuildCurrentYaml().SaveToFile(customThemePath);
    }
}
