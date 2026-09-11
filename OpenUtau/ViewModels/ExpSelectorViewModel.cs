using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using Avalonia.Media;
using OpenUtau.App.Controls;
using OpenUtau.Core;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace OpenUtau.App.ViewModels {
    public partial class ExpSelectorViewModel : ViewModelBase, ICmdSubscriber {
        [Reactive] public partial int Index { get; set; }
        [Reactive] public partial int SelectedIndex { get; set; }
        [Reactive] public partial ExpDisMode DisplayMode { get; set; }
        [Reactive] public partial UExpressionDescriptor? Descriptor { get; set; }
        public string Abbr {
            get{
                if (Descriptor == null) {
                    return "";
                }
                return Descriptor.abbr;
            }
        }
        public ObservableCollection<UExpressionDescriptor> Descriptors => descriptors;
        public string Header => header.Value;
        [Reactive] public partial IBrush TagBrush { get; set; }
        [Reactive] public partial IBrush Background { get; set; }

        ObservableCollection<UExpressionDescriptor> descriptors = new ObservableCollection<UExpressionDescriptor>();
        ObservableAsPropertyHelper<string> header;
        int currentTrackNo = -1;
        bool rebuildingList;

        public ExpSelectorViewModel() {
            DocManager.Inst.AddSubscriber(this);
            this.WhenAnyValue(x => x.DisplayMode)
                .Subscribe(_ => RefreshBrushes());
            this.WhenAnyValue(x => x.Descriptor)
                .Select(descriptor => descriptor == null ? string.Empty : descriptor.abbr.ToUpperInvariant())
                .ToProperty(this, x => x.Header, out header);
            this.WhenAnyValue(x => x.Descriptor)
                .Subscribe(SelectionChanged);
            this.WhenAnyValue(x => x.Index, x => x.Descriptors)
                .Subscribe(tuple => {
                    SetExp(DocManager.Inst.Project.expSelectors[tuple.Item1]);
                });
            MessageBus.Current.Listen<ThemeChangedEvent>()
                .Subscribe(_ => RefreshBrushes());
            TagBrush = ThemeManager.ExpNameBrush;
            Background = ThemeManager.ExpBrush;
            OnListChange();
        }

        public bool SetExp(string abbr) {
            if(Descriptors.Any(d => d.abbr == abbr)) {
                Descriptor = Descriptors.First(d => d.abbr == abbr);
                return true;
            } else {
                if (Descriptors != null && Descriptors.Count > Index) {
                    Descriptor = Descriptors[Index];
                }
                return false;
            }
        }

        public void OnSelected(bool store) {
            if (DisplayMode != ExpDisMode.Visible && Descriptor != null) {
                DocManager.Inst.ExecuteCmd(new SelectExpressionNotification(Descriptor.abbr, Index, true));
            }
            if(store) {
                var project = DocManager.Inst.Project;
                project.expSecondary = project.expPrimary;
                project.expPrimary = Index;
            }
        }

        void SelectionChanged(UExpressionDescriptor? descriptor) {
            if (rebuildingList) {
                return;
            }
            if (descriptor != null) {
                DocManager.Inst.ExecuteCmd(new SelectExpressionNotification(descriptor.abbr, Index, DisplayMode != ExpDisMode.Visible));
            }
            if (!string.IsNullOrEmpty(Abbr)) {
                DocManager.Inst.Project.expSelectors[Index] = Abbr;
            }
        }

        public void OnNext(UCommand cmd, bool isUndo) {
            if (cmd is LoadProjectNotification) {
                currentTrackNo = -1;
                OnListChange();
            } else if (cmd is LoadPartNotification loadPart) {
                currentTrackNo = loadPart.part.trackNo;
                OnListChange();
            } else if (cmd is TrackChangeSingerCommand changeSinger) {
                if (changeSinger.track.TrackNo == currentTrackNo) {
                    OnListChange();
                }
            } else if (cmd is TrackChangeRenderSettingCommand changeRenderer) {
                if (changeRenderer.track.TrackNo == currentTrackNo) {
                    OnListChange();
                }
            } else if (cmd is ConfigureExpressionsCommand ||
                cmd is ExpressionsSuggestedNotification ||
                cmd is ValidateProjectNotification ||
                cmd is SingersRefreshedNotification) {
                OnListChange();
            } else if (cmd is SelectExpressionNotification) {
                OnSelectExp((SelectExpressionNotification)cmd);
            }
        }

        private void OnListChange() {
            rebuildingList = true;
            try {
                var selectedIndex = SelectedIndex;
                var savedAbbr = Descriptor?.abbr ?? DocManager.Inst.Project.expSelectors[Index];
                Descriptors.Clear();
                foreach (var descriptor in GetVisibleDescriptors(currentTrackNo)) {
                    Descriptors.Add(descriptor);
                }
                if (Descriptors.Count == 0) {
                    return;
                }
                if (!string.IsNullOrEmpty(savedAbbr) && Descriptors.Any(d => d.abbr == savedAbbr)) {
                    SelectedIndex = Descriptors.IndexOf(Descriptors.First(d => d.abbr == savedAbbr));
                    Descriptor = Descriptors[SelectedIndex];
                } else if (selectedIndex >= Descriptors.Count) {
                    SelectedIndex = Math.Min(Index, Descriptors.Count - 1);
                    Descriptor = Descriptors[SelectedIndex];
                } else {
                    SelectedIndex = selectedIndex;
                    if (SelectedIndex >= 0 && SelectedIndex < Descriptors.Count) {
                        Descriptor = Descriptors[SelectedIndex];
                    }
                }
                if (!string.IsNullOrEmpty(Abbr)) {
                    DocManager.Inst.Project.expSelectors[Index] = Abbr;
                }
            } finally {
                rebuildingList = false;
            }
        }

        static bool IsDiffSingerTrack(int trackNo) {
            var project = DocManager.Inst.Project;
            if (trackNo < 0 || trackNo >= project.tracks.Count) {
                return false;
            }
            var singer = project.tracks[trackNo].Singer;
            return singer is { Found: true, SingerType: USingerType.DiffSinger };
        }

        static IEnumerable<UExpressionDescriptor> GetVisibleDescriptors(int trackNo) {
            var project = DocManager.Inst.Project;
            if (IsDiffSingerTrack(trackNo)) {
                return project.tracks[trackNo].GetSupportedExps(project);
            }
            return project.expressions.Values;
        }

        private void OnSelectExp(SelectExpressionNotification cmd) {
            if (Descriptors.Count == 0) {
                return;
            }
            if (cmd.SelectorIndex == Index) {
                var match = Descriptors.FirstOrDefault(d => d.abbr == cmd.ExpKey);
                if (match != null && (SelectedIndex < 0 || SelectedIndex >= Descriptors.Count || Descriptors[SelectedIndex].abbr != cmd.ExpKey)) {
                    SelectedIndex = Descriptors.IndexOf(match);
                }
                DisplayMode = ExpDisMode.Visible;
            } else if (cmd.UpdateShadow) {
                DisplayMode = DisplayMode == ExpDisMode.Visible ? ExpDisMode.Shadow : ExpDisMode.Hidden;
            }
        }

        private void RefreshBrushes() {
            TagBrush = DisplayMode == ExpDisMode.Visible
                    ? ThemeManager.ExpActiveNameBrush
                    : DisplayMode == ExpDisMode.Shadow
                    ? ThemeManager.ExpShadowNameBrush
                    : ThemeManager.ExpNameBrush;
            Background = DisplayMode == ExpDisMode.Visible
                    ? ThemeManager.ExpActiveBrush
                    : DisplayMode == ExpDisMode.Shadow
                    ? ThemeManager.ExpShadowBrush
                    : ThemeManager.ExpBrush;
        }
    }
}
