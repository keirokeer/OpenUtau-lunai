using System;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenUtau.App.Controls;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using OpenUtau.Core.Util;
using ReactiveUI;

namespace OpenUtau.App.Views {
    public partial class ExpressionDefaultsPane : UserControl {
        int scrollStyleApplyGeneration;

        ExpressionDefaultsViewModel? Vm => DataContext as ExpressionDefaultsViewModel;

        public ExpressionDefaultsPane() {
            InitializeComponent();
            AttachedToVisualTree += (_, _) => {
                ClosePanelButton.IsVisible = IsHostedInPianoRollDock();
                ScheduleApplyScrollStyle();
                Vm?.RefreshStyles();
            };
            DetachedFromVisualTree += (_, _) => {
                scrollStyleApplyGeneration++;
            };
            MessageBus.Current.Listen<ScrollbarsStyleChangedEvent>()
                .Subscribe(_ => ScheduleApplyScrollStyle());
        }

        void OnProjectModePressed(object? sender, PointerPressedEventArgs e) {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) {
                return;
            }
            if (Vm != null) {
                Vm.IsTrackMode = false;
            }
            e.Handled = true;
        }

        void OnTrackModePressed(object? sender, PointerPressedEventArgs e) {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) {
                return;
            }
            if (Vm != null && Vm.CanUseTrackMode) {
                Vm.IsTrackMode = true;
            }
            e.Handled = true;
        }

        void OnClearTrackOverridesClick(object? sender, RoutedEventArgs e) {
            Vm?.ClearAllTrackOverrides();
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

        bool IsHostedInPianoRollDock() {
            return this.GetVisualAncestors().OfType<PianoRoll>().Any();
        }

        Window? GetOwnerWindow() => TopLevel.GetTopLevel(this) as Window;

        void OnCloseDockedPanel(object? sender, RoutedEventArgs e) {
            var pianoRoll = this.GetVisualAncestors().OfType<PianoRoll>().FirstOrDefault();
            if (pianoRoll?.ViewModel != null) {
                pianoRoll.ViewModel.ShowExpressionDefaultsPanel = false;
            }
        }

        void OnParameterNamePressed(object? sender, PointerPressedEventArgs e) {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) {
                return;
            }
            if (sender is not Control { DataContext: ExpressionDefaultItem item }) {
                return;
            }
            var pianoRoll = this.GetVisualAncestors().OfType<PianoRoll>().FirstOrDefault();
            if (pianoRoll?.ViewModel == null) {
                return;
            }
            pianoRoll.ViewModel.NotesViewModel.ShowExpressions = true;
            int selectorIndex = DocManager.Inst.Project.expPrimary;
            DocManager.Inst.ExecuteCmd(new SelectExpressionNotification(item.Abbr, selectorIndex, true));
            e.Handled = true;
        }

        void OnStyleChipPointerPressed(object? sender, PointerPressedEventArgs e) {
            if (e.Source is Button) {
                return;
            }
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) {
                return;
            }
            if (sender is not Border { DataContext: ExpressionStyleItemViewModel item } || Vm == null) {
                return;
            }
            Vm.ApplyStyle(item.Style);
            e.Handled = true;
        }

        async void OnSaveStyleClick(object? sender, RoutedEventArgs e) {
            var vm = Vm;
            var owner = GetOwnerWindow();
            if (vm == null || owner == null || !vm.CanSaveStyle) {
                return;
            }
            var dialog = new SaveExpressionStyleDialog();
            vm.PrepareSaveDialog(dialog.ViewModel);
            dialog.onFinish = style => {
                if (!ExpressionStyleStore.Exists(style.Name)) {
                    if (!vm.TrySaveStyle(style, overwrite: false)) {
                        dialog.ViewModel.SetError("workspace.panel.expressions.styles.exists");
                        return false;
                    }
                    return true;
                }
                _ = ConfirmOverwriteAndSaveAsync(owner, vm, dialog, style);
                return false;
            };
            await dialog.ShowDialog(owner);
        }

        async Task ConfirmOverwriteAndSaveAsync(
            Window owner,
            ExpressionDefaultsViewModel vm,
            SaveExpressionStyleDialog dialog,
            ExpressionStyleYaml style) {
            var result = await MessageBox.Show(
                owner,
                ThemeManager.GetString("workspace.panel.expressions.styles.overwrite"),
                ThemeManager.GetString("workspace.panel.expressions.styles.save.title"),
                MessageBox.MessageBoxButtons.YesNo);
            if (result != MessageBox.MessageBoxResult.Yes) {
                return;
            }
            if (vm.TrySaveStyle(style, overwrite: true)) {
                dialog.Close();
            } else {
                dialog.ViewModel.SetError("workspace.panel.expressions.styles.exists");
            }
        }

        async void OnStyleDeleteClick(object? sender, RoutedEventArgs e) {
            if (sender is not Button { DataContext: ExpressionStyleItemViewModel item } || Vm == null) {
                return;
            }
            e.Handled = true;
            var owner = GetOwnerWindow();
            if (owner == null) {
                return;
            }
            var result = await MessageBox.Show(
                owner,
                ThemeManager.GetString("workspace.panel.expressions.styles.delete.message"),
                ThemeManager.GetString("workspace.panel.expressions.styles.delete"),
                MessageBox.MessageBoxButtons.YesNo);
            if (result != MessageBox.MessageBoxResult.Yes) {
                return;
            }
            Vm.TryDeleteStyle(item.Name);
        }
    }
}
