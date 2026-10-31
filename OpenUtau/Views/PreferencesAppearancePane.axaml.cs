using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using OpenUtau.App.ViewModels;

namespace OpenUtau.App.Views {
    public partial class PreferencesAppearancePane : UserControl {
        public PreferencesAppearancePane() {
            InitializeComponent();
        }

        async void SelectUiFont(object? sender, RoutedEventArgs e) {
            if (DataContext is not PreferencesViewModel vm) {
                return;
            }
            var window = TopLevel.GetTopLevel(this) as Window;
            if (window == null) {
                return;
            }
            var path = await FilePicker.OpenFile(window, "prefs.appearance.customfont.select", FilePicker.FontFiles);
            if (!string.IsNullOrEmpty(path) && UiFontManager.IsSupportedFontPath(path)) {
                vm.SetUiFontPath(path);
            }
        }

        void ResetUiFont(object? sender, RoutedEventArgs e) {
            if (DataContext is PreferencesViewModel vm) {
                vm.ResetUiFont();
            }
        }
    }
}
