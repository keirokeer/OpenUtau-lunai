using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenUtau.App;
using OpenUtau.App.Controls;
using OpenUtau.App.ViewModels;
using OpenUtau.Core.Util;
using ReactiveUI;

namespace OpenUtau.App.Views {
    public partial class AppearancePreferencesPane : UserControl {
        PreferencesViewModel ViewModel => EnsureViewModel();
        int scrollStyleApplyGeneration;

        public AppearancePreferencesPane() {
            InitializeComponent();
            AttachedToVisualTree += (_, _) => {
                ClosePanelButton.IsVisible = IsHostedInPianoRollDock();
                ScheduleApplyScrollStyle();
            };
            DetachedFromVisualTree += (_, _) => {
                scrollStyleApplyGeneration++;
            };
            MessageBus.Current.Listen<ScrollbarsStyleChangedEvent>()
                .Subscribe(_ => ScheduleApplyScrollStyle());
        }

        void ScheduleApplyScrollStyle() {
            if (!WorkspaceScrollbarHelper.IsInVisualTree(this)) {
                return;
            }
            int generation = ++scrollStyleApplyGeneration;
            Dispatcher.UIThread.Post(() => {
                if (generation != scrollStyleApplyGeneration || !WorkspaceScrollbarHelper.IsInVisualTree(this)) {
                    return;
                }
                ApplyScrollStyle();
            }, DispatcherPriority.Loaded);
        }

        void ApplyScrollStyle() {
            if (!WorkspaceScrollbarHelper.IsInVisualTree(this)) {
                return;
            }
            WorkspaceScrollbarHelper.ApplyScrollViewer(ContentScroll, WorkspaceScrollbarHelper.UseClassicScrollbars);
        }

        PreferencesViewModel EnsureViewModel() {
            if (DataContext is PreferencesViewModel vm) {
                return vm;
            }
            var prefs = new PreferencesViewModel();
            DataContext = prefs;
            return prefs;
        }

        bool IsHostedInPianoRollDock() {
            return this.GetVisualAncestors().OfType<PianoRoll>().Any();
        }

        void OnCloseDockedPanel(object? sender, RoutedEventArgs e) {
            var pianoRoll = this.GetVisualAncestors().OfType<PianoRoll>().FirstOrDefault();
            if (pianoRoll?.ViewModel != null) {
                pianoRoll.ViewModel.ShowAppearancePanel = false;
            }
        }

        async void SelectUiFont(object? sender, RoutedEventArgs e) {
            var path = await FilePicker.OpenFile(
                TopLevel.GetTopLevel(this) as Window ?? throw new InvalidOperationException(),
                "prefs.appearance.customfont.select",
                FilePicker.FontFiles);
            if (!string.IsNullOrEmpty(path) && UiFontManager.IsSupportedFontPath(path)) {
                ViewModel.SetUiFontPath(path);
            }
        }

        void ResetUiFont(object? sender, RoutedEventArgs e) {
            ViewModel.ResetUiFont();
        }
    }
}
