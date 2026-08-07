using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using DynamicData.Binding;
using OpenUtau.App;
using OpenUtau.Core;
using OpenUtau.Core.Format;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtau.App.ViewModels {
    public class ExpressionDefaultItem : ReactiveObject {
        public string Abbr { get; }
        [Reactive] public string Name { get; set; }
        [Reactive] public float Min { get; set; }
        [Reactive] public float Max { get; set; }
        [Reactive] public float DefaultValue { get; set; }
        [Reactive] public float PlayheadValue { get; set; }
        [Reactive] public bool ShowPlayheadMarker { get; set; }
        [Reactive] public bool HasTrackOverride { get; set; }

        public ExpressionDefaultItem(UExpressionDescriptor descriptor) {
            Abbr = descriptor.abbr;
            Name = ExpressionSuggestionSync.GetPanelDisplayName(descriptor);
            Min = descriptor.min;
            Max = descriptor.max;
            DefaultValue = descriptor.CustomDefaultValue;
            PlayheadValue = DefaultValue;
            ShowPlayheadMarker = false;
            HasTrackOverride = false;
        }

        public void SyncFromDescriptor(UExpressionDescriptor descriptor) {
            Name = ExpressionSuggestionSync.GetPanelDisplayName(descriptor);
            Min = descriptor.min;
            Max = descriptor.max;
        }
    }

    public class ExpressionStyleItemViewModel : ViewModelBase {
        public string Name { get; }
        public string SingerName { get; }
        public string ToolTipText { get; }
        public ExpressionStyleYaml Style { get; }

        public ExpressionStyleItemViewModel(ExpressionStyleYaml style) {
            Style = style;
            Name = style.Name;
            SingerName = style.SingerName ?? string.Empty;
            if (string.IsNullOrWhiteSpace(SingerName)) {
                ToolTipText = Name;
            } else {
                var savedWith = ThemeManager.GetString("workspace.panel.expressions.styles.savedwith");
                ToolTipText = $"{Name}\n{savedWith} {SingerName}";
            }
        }
    }

    public class ExpressionDefaultsViewModel : ViewModelBase, ICmdSubscriber {
        public ObservableCollectionExtended<ExpressionDefaultItem> ParameterItems { get; } = new();
        public ObservableCollectionExtended<ExpressionDefaultItem> VoiceColorItems { get; } = new();
        public ObservableCollectionExtended<string> VoiceColorOptions { get; } = new();
        public ObservableCollectionExtended<ExpressionStyleItemViewModel> StyleItems { get; } = new();

        [Reactive] public bool HasParameters { get; private set; }
        [Reactive] public bool HasVoiceColors { get; private set; }
        [Reactive] public bool ShowDefaultVoiceColorPicker { get; private set; }
        [Reactive] public bool HasStyles { get; private set; }
        [Reactive] public bool CanSaveStyle { get; private set; }
        [Reactive] public int SelectedVoiceColorIndex { get; set; }
        [Reactive] public string VoiceColorCurveMaxText { get; set; } = "100";
        [Reactive] public bool IsTrackMode { get; set; }
        [Reactive] public bool CanUseTrackMode { get; private set; }
        [Reactive] public bool HasTrackOverrides { get; private set; }
        [Reactive] public string PanelTitle { get; private set; } = string.Empty;
        [Reactive] public string TrackSubtitle { get; private set; } = string.Empty;
        [Reactive] public bool HasTrackSubtitle { get; private set; }
        [Reactive] public string ResetTooltip { get; private set; } = string.Empty;

        UVoicePart? part;
        int trackNo = -1;
        bool applyingSlider;
        bool applyingVoiceColor;
        bool applyingVoiceColorMax;
        bool applyingMode;
        string? pendingAbbr;
        float pendingOldValue;
        float pendingNewValue;
        float? pendingOldTrackOverride;

        public ExpressionDefaultsViewModel() {
            DocManager.Inst.AddSubscriber(this);
            applyingVoiceColorMax = true;
            VoiceColorCurveMaxText = Preferences.GetVoiceColorCurveMax().ToString();
            applyingVoiceColorMax = false;
            UpdateChrome();
            this.WhenAnyValue(vm => vm.SelectedVoiceColorIndex)
                .Subscribe(index => {
                    if (!applyingVoiceColor) {
                        CommitDefaultVoiceColor(index);
                    }
                });
            this.WhenAnyValue(vm => vm.VoiceColorCurveMaxText)
                .Subscribe(_ => OnVoiceColorCurveMaxTextChanged());
            this.WhenAnyValue(vm => vm.IsTrackMode)
                .Subscribe(_ => {
                    if (applyingMode) {
                        return;
                    }
                    if (pendingAbbr != null) {
                        CommitPendingEdit();
                    }
                    UpdateChrome();
                    RefreshList();
                    RefreshPlayheadValues();
                });
            RefreshList();
            RefreshStyles();
            RefreshPlayheadValues();
        }

        void UpdateChrome() {
            if (IsTrackMode && CanUseTrackMode) {
                PanelTitle = ThemeManager.GetString("workspace.panel.expressions.track");
                ResetTooltip = ThemeManager.GetString("workspace.panel.expressions.resettooltip.track");
            } else {
                PanelTitle = ThemeManager.GetString("workspace.panel.expressions");
                ResetTooltip = ThemeManager.GetString("workspace.panel.expressions.resettooltip");
            }
            var project = DocManager.Inst.Project;
            if (trackNo >= 0 && trackNo < project.tracks.Count) {
                TrackSubtitle = project.tracks[trackNo].TrackName ?? string.Empty;
            } else {
                TrackSubtitle = string.Empty;
            }
            HasTrackSubtitle = IsTrackMode && CanUseTrackMode && !string.IsNullOrEmpty(TrackSubtitle);
        }

        void OnVoiceColorCurveMaxTextChanged() {
            if (applyingVoiceColorMax) {
                return;
            }
            if (!int.TryParse(VoiceColorCurveMaxText, out var n)) {
                return;
            }
            n = Math.Clamp(n, Preferences.VoiceColorCurveMaxMin, Preferences.VoiceColorCurveMaxMax);
            if (n == Preferences.GetVoiceColorCurveMax()) {
                return;
            }
            Preferences.SetVoiceColorCurveMax(n);
            applyingVoiceColorMax = true;
            VoiceColorCurveMaxText = n.ToString();
            applyingVoiceColorMax = false;
            SyncSuggestionsForOpenTrack();
            RefreshExpressionLists();
        }

        void SyncVoiceColorCurveMaxTextFromPrefs() {
            applyingVoiceColorMax = true;
            VoiceColorCurveMaxText = Preferences.GetVoiceColorCurveMax().ToString();
            applyingVoiceColorMax = false;
        }

        public void AttachPart(UVoicePart? voicePart) {
            part = voicePart;
            trackNo = voicePart?.trackNo ?? -1;
            CanUseTrackMode = trackNo >= 0;
            if (!CanUseTrackMode && IsTrackMode) {
                applyingMode = true;
                IsTrackMode = false;
                applyingMode = false;
            }
            SyncSuggestionsForOpenTrack();
            UpdateChrome();
            RefreshList();
            RefreshPlayheadValues();
        }

        public string CurrentSingerDisplayName {
            get {
                var project = DocManager.Inst.Project;
                if (trackNo < 0 || trackNo >= project.tracks.Count) {
                    return string.Empty;
                }
                return project.tracks[trackNo].Singer?.Name?.Trim() ?? string.Empty;
            }
        }

        UTrack? CurrentTrack {
            get {
                var project = DocManager.Inst.Project;
                if (trackNo < 0 || trackNo >= project.tracks.Count) {
                    return null;
                }
                return project.tracks[trackNo];
            }
        }

        public void RefreshStyles() {
            StyleItems.Clear();
            foreach (var style in ExpressionStyleStore.LoadAll()) {
                StyleItems.Add(new ExpressionStyleItemViewModel(style));
            }
            HasStyles = StyleItems.Count > 0;
        }

        public void PrepareSaveDialog(SaveExpressionStyleViewModel dialogVm) {
            var project = DocManager.Inst.Project;
            UExpressionDescriptor? clr = null;
            string[]? options = null;
            if (ShowDefaultVoiceColorPicker
                && trackNo >= 0
                && trackNo < project.tracks.Count
                && project.tracks[trackNo].VoiceColorExp != null) {
                clr = project.tracks[trackNo].VoiceColorExp;
                options = clr!.options;
            }
            // Styles always snapshot project-layer defaults (not track overrides).
            dialogVm.LoadFromPanel(
                suggestedName: string.Empty,
                singerName: CurrentSingerDisplayName,
                parameters: ParameterItems.Select(i => (
                    i.Abbr,
                    i.Name,
                    ExpressionDefaultResolver.GetProjectDefault(project, i.Abbr))),
                voiceColors: VoiceColorItems.Select(i => (
                    i.Abbr,
                    i.Name,
                    ExpressionDefaultResolver.GetProjectDefault(project, i.Abbr))),
                clrDescriptor: clr,
                selectedVoiceColorIndex: (int)Math.Round(
                    ExpressionDefaultResolver.GetProjectDefault(project, Ustx.CLR)),
                voiceColorOptions: options);
        }

        public bool TrySaveStyle(ExpressionStyleYaml style, bool overwrite) {
            if (!ExpressionStyleStore.TrySave(style, out _, overwrite)) {
                return false;
            }
            RefreshStyles();
            return true;
        }

        public void ApplyStyle(ExpressionStyleYaml style) {
            if (style?.Values == null || style.Values.Count == 0) {
                return;
            }
            if (pendingAbbr != null) {
                CommitPendingEdit();
            }
            var project = DocManager.Inst.Project;
            if (trackNo >= 0 && trackNo < project.tracks.Count) {
                ExpressionSuggestionSync.UpsertSuggested(project, project.tracks[trackNo]);
            }

            var changes = new List<(string abbr, float newValue, float oldValue)>();
            foreach (var pair in style.Values) {
                var abbr = pair.Key;
                UExpressionDescriptor? descriptor = null;
                if (string.Equals(abbr, Ustx.CLR, StringComparison.OrdinalIgnoreCase)
                    && trackNo >= 0
                    && trackNo < project.tracks.Count
                    && project.tracks[trackNo].VoiceColorExp != null) {
                    descriptor = project.tracks[trackNo].VoiceColorExp;
                } else if (!project.expressions.TryGetValue(abbr, out descriptor)) {
                    continue;
                }
                float min = descriptor!.min;
                float max = descriptor.max;
                float newValue = max < min ? pair.Value : Math.Clamp(pair.Value, min, max);
                float oldValue = ExpressionDefaultResolver.GetProjectDefault(project, descriptor.abbr);
                if (Math.Abs(oldValue - newValue) < 0.0001f) {
                    continue;
                }
                changes.Add((descriptor.abbr, newValue, oldValue));
            }

            if (changes.Count == 0) {
                return;
            }

            DocManager.Inst.StartUndoGroup();
            foreach (var (abbr, newValue, oldValue) in changes) {
                DocManager.Inst.ExecuteCmd(
                    new SetExpressionCustomDefaultCommand(project, abbr, newValue, oldValue));
            }
            DocManager.Inst.EndUndoGroup();
            MessageBus.Current.SendMessage(new NotesRefreshEvent());
            RefreshList();
            RefreshPlayheadValues();
        }

        public bool TryDeleteStyle(string name) {
            if (!ExpressionStyleStore.TryDelete(name)) {
                return false;
            }
            RefreshStyles();
            return true;
        }

        public void ClearAllTrackOverrides() {
            var track = CurrentTrack;
            if (track == null || track.ExpressionDefaultOverrides == null ||
                track.ExpressionDefaultOverrides.Count == 0) {
                return;
            }
            if (pendingAbbr != null) {
                CommitPendingEdit();
            }
            DocManager.Inst.StartUndoGroup();
            DocManager.Inst.ExecuteCmd(new ClearTrackExpressionDefaultsCommand(DocManager.Inst.Project, track));
            DocManager.Inst.EndUndoGroup();
            MessageBus.Current.SendMessage(new NotesRefreshEvent());
            RefreshList();
            RefreshPlayheadValues();
        }

        public void BeginEdit(ExpressionDefaultItem item) {
            if (pendingAbbr != null && pendingAbbr != item.Abbr) {
                CommitPendingEdit();
            }
            if (pendingAbbr == item.Abbr) {
                return;
            }
            pendingAbbr = item.Abbr;
            pendingOldValue = item.DefaultValue;
            pendingNewValue = item.DefaultValue;
            pendingOldTrackOverride = CurrentTrack == null
                ? null
                : ExpressionDefaultResolver.GetTrackOverride(CurrentTrack, item.Abbr);
        }

        public void PreviewEdit(ExpressionDefaultItem item, float value) {
            applyingSlider = true;
            item.DefaultValue = value;
            pendingAbbr = item.Abbr;
            pendingNewValue = value;
            ApplyLiveDefault(item.Abbr, value);
            applyingSlider = false;
        }

        public void EndEdit(ExpressionDefaultItem item) {
            pendingAbbr = item.Abbr;
            pendingNewValue = item.DefaultValue;
            CommitPendingEdit();
        }

        /// <summary>
        /// Project mode: reset to factory. Track mode: clear track override (inherit project).
        /// </summary>
        public void ResetSliderDefault(ExpressionDefaultItem item) {
            if (item == null) {
                return;
            }
            if (pendingAbbr != null) {
                CommitPendingEdit();
            }
            if (IsTrackMode && CanUseTrackMode) {
                ResetTrackOverride(item);
            } else {
                ResetToFactoryDefault(item);
            }
        }

        void ResetTrackOverride(ExpressionDefaultItem item) {
            var track = CurrentTrack;
            var project = DocManager.Inst.Project;
            if (track == null || !ExpressionDefaultResolver.HasTrackOverride(track, item.Abbr)) {
                return;
            }
            float? oldOverride = ExpressionDefaultResolver.GetTrackOverride(track, item.Abbr);
            float projectValue = ExpressionDefaultResolver.GetProjectDefault(project, item.Abbr);
            applyingSlider = true;
            item.DefaultValue = projectValue;
            item.HasTrackOverride = false;
            applyingSlider = false;
            DocManager.Inst.StartUndoGroup();
            DocManager.Inst.ExecuteCmd(new SetTrackExpressionDefaultCommand(
                project, track, item.Abbr, null, oldOverride));
            DocManager.Inst.EndUndoGroup();
            MessageBus.Current.SendMessage(new NotesRefreshEvent());
            RefreshList();
            RefreshPlayheadValues();
        }

        void ResetToFactoryDefault(ExpressionDefaultItem item) {
            var project = DocManager.Inst.Project;
            if (!project.expressions.TryGetValue(item.Abbr, out var descriptor)) {
                return;
            }
            if (trackNo >= 0 && trackNo < project.tracks.Count) {
                ExpressionSuggestionSync.UpsertSuggested(project, project.tracks[trackNo]);
            }
            float factory = SetExpressionCustomDefaultCommand.GetFactoryDefault(descriptor);
            float current = ExpressionDefaultResolver.GetProjectDefault(project, item.Abbr);
            if (Math.Abs(current - factory) < 0.0001f) {
                return;
            }
            applyingSlider = true;
            item.DefaultValue = factory;
            applyingSlider = false;
            ApplyLiveDefault(item.Abbr, factory);
            DocManager.Inst.StartUndoGroup();
            DocManager.Inst.ExecuteCmd(new SetExpressionCustomDefaultCommand(project, item.Abbr, factory, current));
            DocManager.Inst.EndUndoGroup();
            MessageBus.Current.SendMessage(new NotesRefreshEvent());
        }

        void ApplyLiveDefault(string abbr, float value) {
            var project = DocManager.Inst.Project;
            var track = CurrentTrack;
            if (IsTrackMode && track != null) {
                ExpressionDefaultResolver.ApplyTrackOverride(project, track, abbr, value);
                if (string.Equals(abbr, Ustx.CLR, StringComparison.OrdinalIgnoreCase) &&
                    track.VoiceColorExp != null) {
                    float effective = ExpressionDefaultResolver.GetEffectiveDefault(project, track, abbr);
                    track.VoiceColorExp.CustomDefaultValue = Math.Clamp(
                        effective, track.VoiceColorExp.min, track.VoiceColorExp.max);
                }
                MessageBus.Current.SendMessage(new NotesRefreshEvent());
                return;
            }
            if (!project.expressions.TryGetValue(abbr, out var descriptor)) {
                return;
            }
            SetExpressionCustomDefaultCommand.SetEffectiveDefault(descriptor, value);
            if (string.Equals(abbr, Ustx.CLR, StringComparison.OrdinalIgnoreCase)) {
                foreach (var t in project.tracks) {
                    if (ExpressionDefaultResolver.HasTrackOverride(t, Ustx.CLR)) {
                        continue;
                    }
                    if (t.VoiceColorExp != null) {
                        float min = t.VoiceColorExp.min;
                        float max = t.VoiceColorExp.max;
                        float clamped = max < min ? value : Math.Clamp(value, min, max);
                        SetExpressionCustomDefaultCommand.SetEffectiveDefault(t.VoiceColorExp, clamped);
                    }
                }
            }
            MessageBus.Current.SendMessage(new NotesRefreshEvent());
        }

        void CommitPendingEdit() {
            if (pendingAbbr == null) {
                return;
            }
            var abbr = pendingAbbr;
            var oldValue = pendingOldValue;
            var newValue = pendingNewValue;
            var oldTrackOverride = pendingOldTrackOverride;
            pendingAbbr = null;
            pendingOldTrackOverride = null;
            if (Math.Abs(oldValue - newValue) < 0.0001f) {
                return;
            }
            var project = DocManager.Inst.Project;
            var track = CurrentTrack;
            if (IsTrackMode && track != null) {
                DocManager.Inst.StartUndoGroup();
                DocManager.Inst.ExecuteCmd(new SetTrackExpressionDefaultCommand(
                    project, track, abbr, newValue, oldTrackOverride));
                DocManager.Inst.EndUndoGroup();
                MessageBus.Current.SendMessage(new NotesRefreshEvent());
                RefreshList();
                return;
            }
            if (!project.expressions.ContainsKey(abbr)) {
                return;
            }
            DocManager.Inst.StartUndoGroup();
            DocManager.Inst.ExecuteCmd(new SetExpressionCustomDefaultCommand(project, abbr, newValue, oldValue));
            DocManager.Inst.EndUndoGroup();
            MessageBus.Current.SendMessage(new NotesRefreshEvent());
            RefreshList();
        }

        void CommitDefaultVoiceColor(int index) {
            var project = DocManager.Inst.Project;
            if (trackNo < 0 || trackNo >= project.tracks.Count || !ShowDefaultVoiceColorPicker) {
                return;
            }
            var track = project.tracks[trackNo];
            if (track.VoiceColorExp == null || index < 0 || index > track.VoiceColorExp.max) {
                return;
            }
            if (IsTrackMode) {
                float? oldOverride = ExpressionDefaultResolver.GetTrackOverride(track, Ustx.CLR);
                float current = ExpressionDefaultResolver.GetEffectiveDefault(project, track, Ustx.CLR);
                if (Math.Abs(current - index) < 0.0001f) {
                    return;
                }
                DocManager.Inst.StartUndoGroup();
                DocManager.Inst.ExecuteCmd(new SetTrackExpressionDefaultCommand(
                    project, track, Ustx.CLR, index, oldOverride));
                DocManager.Inst.EndUndoGroup();
            } else {
                float current = ExpressionDefaultResolver.GetProjectDefault(project, Ustx.CLR);
                if (Math.Abs(current - index) < 0.0001f) {
                    return;
                }
                DocManager.Inst.StartUndoGroup();
                DocManager.Inst.ExecuteCmd(new SetExpressionCustomDefaultCommand(project, Ustx.CLR, index, current));
                DocManager.Inst.EndUndoGroup();
            }
            MessageBus.Current.SendMessage(new NotesRefreshEvent());
            RefreshList();
        }

        public void SyncSuggestionsForOpenTrack() {
            var project = DocManager.Inst.Project;
            if (trackNo < 0 || trackNo >= project.tracks.Count) {
                return;
            }
            if (ExpressionSuggestionSync.UpsertSuggested(project, project.tracks[trackNo])) {
                DocManager.Inst.ExecuteCmd(new ExpressionsSuggestedNotification());
            }
        }

        void RefreshList() {
            SyncVoiceColorCurveMaxTextFromPrefs();
            RefreshExpressionLists();
            UpdateChrome();
        }

        void RefreshExpressionLists() {
            var project = DocManager.Inst.Project;
            if (trackNo < 0 || trackNo >= project.tracks.Count) {
                ParameterItems.Clear();
                VoiceColorItems.Clear();
                VoiceColorOptions.Clear();
                HasParameters = false;
                HasVoiceColors = false;
                ShowDefaultVoiceColorPicker = false;
                CanSaveStyle = false;
                HasTrackOverrides = false;
                return;
            }
            var track = project.tracks[trackNo];
            RebuildItemList(ParameterItems, ExpressionSuggestionSync.GetPanelParameterDescriptors(project, track));
            RebuildItemList(VoiceColorItems, ExpressionSuggestionSync.GetPanelVoiceColorDescriptors(project, track));
            HasParameters = ParameterItems.Count > 0;
            HasVoiceColors = VoiceColorItems.Count > 0;
            HasTrackOverrides = track.ExpressionDefaultOverrides != null &&
                track.ExpressionDefaultOverrides.Count > 0;

            VoiceColorOptions.Clear();
            applyingVoiceColor = true;
            SelectedVoiceColorIndex = -1;
            if (track.VoiceColorExp?.options != null && track.VoiceColorExp.options.Length > 0) {
                foreach (var option in track.VoiceColorExp.options) {
                    VoiceColorOptions.Add(option);
                }
                ShowDefaultVoiceColorPicker = true;
                float clrValue = IsTrackMode
                    ? ExpressionDefaultResolver.GetEffectiveDefault(project, track, Ustx.CLR)
                    : ExpressionDefaultResolver.GetProjectDefault(project, Ustx.CLR);
                int index = (int)Math.Round(clrValue);
                SelectedVoiceColorIndex = Math.Clamp(index, 0, track.VoiceColorExp.options.Length - 1);
                HasVoiceColors = true;
            } else {
                ShowDefaultVoiceColorPicker = false;
                SelectedVoiceColorIndex = 0;
            }
            applyingVoiceColor = false;
            CanSaveStyle = HasParameters || HasVoiceColors;
        }

        void RebuildItemList(
            ObservableCollectionExtended<ExpressionDefaultItem> target,
            List<UExpressionDescriptor> descriptors) {
            var project = DocManager.Inst.Project;
            var track = CurrentTrack;
            var byAbbr = target.ToDictionary(i => i.Abbr, StringComparer.OrdinalIgnoreCase);
            target.Clear();
            foreach (var descriptor in descriptors) {
                ExpressionDefaultItem item;
                if (byAbbr.TryGetValue(descriptor.abbr, out var existing)) {
                    existing.SyncFromDescriptor(descriptor);
                    item = existing;
                } else {
                    item = new ExpressionDefaultItem(descriptor);
                }
                item.HasTrackOverride = ExpressionDefaultResolver.HasTrackOverride(track, descriptor.abbr);
                item.DefaultValue = IsTrackMode && track != null
                    ? ExpressionDefaultResolver.GetEffectiveDefault(project, track, descriptor.abbr)
                    : ExpressionDefaultResolver.GetProjectDefault(project, descriptor.abbr);
                target.Add(item);
            }
        }

        public void RefreshPlayheadValues() {
            RefreshPlayheadValuesFor(ParameterItems);
            RefreshPlayheadValuesFor(VoiceColorItems);
        }

        void RefreshPlayheadValuesFor(ObservableCollectionExtended<ExpressionDefaultItem> items) {
            var project = DocManager.Inst.Project;
            if (part == null || trackNo < 0 || trackNo >= project.tracks.Count) {
                foreach (var item in items) {
                    item.ShowPlayheadMarker = false;
                    item.PlayheadValue = item.DefaultValue;
                }
                return;
            }
            var track = project.tracks[trackNo];
            int localTick = DocManager.Inst.playPosTick - part.position;
            bool inPart = localTick >= 0 && localTick <= part.Duration;
            UPhoneme? phoneme = null;
            if (inPart) {
                phoneme = part.phonemes.FirstOrDefault(p =>
                    !p.Error && p.Parent != null &&
                    p.position <= localTick && localTick <= p.End);
            }
            foreach (var item in items) {
                if (!track.TryGetExpDescriptor(project, item.Abbr, out var descriptor)) {
                    item.ShowPlayheadMarker = false;
                    continue;
                }
                float baseline = ExpressionDefaultResolver.GetEffectiveDefault(project, track, item.Abbr);
                float value = baseline;
                bool hasOverride = false;
                if (descriptor.type == UExpressionType.Curve) {
                    var curve = part.curves?.FirstOrDefault(c =>
                        string.Equals(c.abbr, item.Abbr, StringComparison.OrdinalIgnoreCase));
                    if (curve != null && curve.descriptor != null && inPart && curve.xs.Count > 0) {
                        int empty = ExpressionDefaultResolver.GetEffectiveDefaultInt(project, track, item.Abbr);
                        value = curve.Sample(localTick, empty);
                        hasOverride = Math.Abs(value - baseline) > 0.0001f;
                    }
                } else if (descriptor.type == UExpressionType.Numerical && phoneme != null) {
                    var (expValue, overridden) = phoneme.GetExpression(project, track, item.Abbr);
                    value = expValue;
                    hasOverride = overridden || Math.Abs(value - baseline) > 0.0001f;
                }
                item.PlayheadValue = item.Max < item.Min
                    ? value
                    : Math.Clamp(value, item.Min, item.Max);
                item.ShowPlayheadMarker = inPart && hasOverride;
            }
        }

        public void OnNext(UCommand cmd, bool isUndo) {
            if (cmd is LoadPartNotification loadPart) {
                AttachPart(loadPart.part as UVoicePart);
                return;
            }
            if (cmd is LoadProjectNotification) {
                part = null;
                trackNo = -1;
                CanUseTrackMode = false;
                applyingMode = true;
                IsTrackMode = false;
                applyingMode = false;
                ParameterItems.Clear();
                VoiceColorItems.Clear();
                VoiceColorOptions.Clear();
                HasParameters = false;
                HasVoiceColors = false;
                ShowDefaultVoiceColorPicker = false;
                CanSaveStyle = false;
                HasTrackOverrides = false;
                UpdateChrome();
                return;
            }
            if (cmd is TrackChangeSingerCommand changeSinger) {
                if (changeSinger.track.TrackNo == trackNo) {
                    SyncSuggestionsForOpenTrack();
                    RefreshList();
                    RefreshPlayheadValues();
                }
                return;
            }
            if (cmd is TrackChangeRenderSettingCommand changeRenderer) {
                if (changeRenderer.track.TrackNo == trackNo) {
                    SyncSuggestionsForOpenTrack();
                    RefreshList();
                    RefreshPlayheadValues();
                }
                return;
            }
            if (cmd is ConfigureExpressionsCommand ||
                cmd is ExpressionsSuggestedNotification ||
                cmd is SingersRefreshedNotification) {
                RefreshList();
                RefreshPlayheadValues();
                return;
            }
            if ((cmd is SetExpressionCustomDefaultCommand ||
                 cmd is SetTrackExpressionDefaultCommand ||
                 cmd is ClearTrackExpressionDefaultsCommand) && !applyingSlider) {
                applyingVoiceColor = cmd is SetExpressionCustomDefaultCommand setDefault &&
                    string.Equals(setDefault.Abbr, Ustx.CLR, StringComparison.OrdinalIgnoreCase);
                RefreshList();
                applyingVoiceColor = false;
                RefreshPlayheadValues();
                return;
            }
            if (cmd is SetPlayPosTickNotification ||
                cmd is SetCurveCommand ||
                cmd is EraseCurveCommand ||
                cmd is SetNotesSameExpressionCommand ||
                cmd is PhonemizedNotification) {
                RefreshPlayheadValues();
            }
        }
    }
}
