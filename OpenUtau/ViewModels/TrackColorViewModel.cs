using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using OpenUtau.App.Views;
using OpenUtau.Colors;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace OpenUtau.App.ViewModels {
    public partial class TrackColorViewModel : ViewModelBase {
        readonly UTrack track;
        TrackColorPickerItemViewModel? previousSelection;

        public ObservableCollection<TrackColorPickerItemViewModel> Items { get; } = new();
        [Reactive] public partial TrackColorPickerItemViewModel? SelectedItem { get; set; }

        public TrackColorViewModel(UTrack track) {
            this.track = track;
            RefreshItems();
            SelectedItem = TrackColorPickerOperations.FindItem(Items, track.TrackColor)
                ?? TrackColorPickerOperations.FindItem(Items, "Blue")
                ?? Items.FirstOrDefault(item => item.Color != null);
            previousSelection = SelectedItem;

            this.WhenAnyValue(vm => vm.SelectedItem)
                .Subscribe(item => {
                    if (item?.IsCreateTile == true) {
                        var restore = previousSelection ?? Items.FirstOrDefault(i => i.Color != null);
                        SelectedItem = restore;
                        OpenCreateDialog();
                        return;
                    }
                    if (item?.Color != null) {
                        previousSelection = item;
                    }
                });
        }

        public TrackColor SelectedColor =>
            SelectedItem?.Color ?? ThemeManager.GetTrackColor(track.TrackColor);

        public void RefreshItems() {
            string? selectedName = SelectedItem?.Color?.Name ?? track.TrackColor;
            TrackColorPickerOperations.PopulateItems(Items);
            SelectedItem = TrackColorPickerOperations.FindItem(Items, selectedName)
                ?? TrackColorPickerOperations.FindItem(Items, "Blue")
                ?? Items.FirstOrDefault(item => item.Color != null);
            previousSelection = SelectedItem;
        }

        public void OpenCreateDialog(Window? owner = null) {
            TrackColorPickerOperations.OpenCreateDialog(owner, created => {
                RefreshItems();
                SelectedItem = TrackColorPickerOperations.FindItem(Items, created.Name);
                previousSelection = SelectedItem;
            });
        }

        public void EditColorAsync(TrackColor color, Window owner) {
            TrackColorPickerOperations.OpenEditDialog(owner, color, updated => {
                string previousName = color.Name;
                RefreshItems();
                if (string.Equals(SelectedItem?.Color?.Name, previousName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(track.TrackColor, previousName, StringComparison.OrdinalIgnoreCase)) {
                    SelectedItem = TrackColorPickerOperations.FindItem(Items, updated.Name);
                }
                previousSelection = SelectedItem;
            });
        }

        public async System.Threading.Tasks.Task DeleteColorAsync(TrackColor color, Window owner) {
            if (!await TrackColorPickerOperations.ConfirmAndDeleteAsync(owner, color)) {
                return;
            }

            string deletedName = color.Name;
            RefreshItems();
            if (string.Equals(SelectedItem?.Color?.Name, deletedName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(track.TrackColor, deletedName, StringComparison.OrdinalIgnoreCase)) {
                SelectedItem = TrackColorPickerOperations.FindItem(Items, "Blue");
            }
            previousSelection = SelectedItem;
        }

        public void Finish() {
            var selected = SelectedColor;
            if (selected.Name != track.TrackColor) {
                DocManager.Inst.StartUndoGroup("command.track.setting");
                DocManager.Inst.ExecuteCmd(new ChangeTrackColorCommand(DocManager.Inst.Project, track, selected.Name));
                DocManager.Inst.EndUndoGroup();
                MessageBus.Current.SendMessage(new ThemeChangedEvent());
                MessageBus.Current.SendMessage(new PianorollRefreshEvent("TrackColor"));
            }
        }
    }
}
