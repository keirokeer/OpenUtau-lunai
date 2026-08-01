using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using DynamicData.Binding;
using OpenUtau.App;
using OpenUtau.App.Controls;
using OpenUtau.App.Views;
using OpenUtau.Classic;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using OpenUtau.ViewModels;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtau.App.ViewModels {
    public class PhonemeMouseoverEvent {
        public readonly UPhoneme? mouseoverPhoneme;
        public PhonemeMouseoverEvent(UPhoneme? mouseoverPhoneme) {
            this.mouseoverPhoneme = mouseoverPhoneme;
        }
    }

    public class NotesContextMenuArgs {
        public PianoRollViewModel? ViewModel { get; set; }

        public bool ForNote { get; set; }
        public NoteHitInfo NoteHitInfo { get; set; }

        public bool ForPitchPoint { get; set; }
        public bool PitchPointIsFirst { get; set; }
        public bool PitchPointCanDel { get; set; }
        public bool PitchPointCanAdd { get; set; }
        public PitchPointHitInfo PitchPointHitInfo { get; set; }
    }

    public class PianorollRefreshEvent {
        public readonly string refreshItem;
        public PianorollRefreshEvent(string refreshItem) {
            this.refreshItem = refreshItem;
        }
    }

    public class PianoRollViewModel : ViewModelBase, ICmdSubscriber {
        DispatcherTimer? progressSmoothTimer;
        double progressTarget;

        [Reactive] public NotesViewModel NotesViewModel { get; set; }
        [Reactive] public PlaybackViewModel? PlaybackViewModel { get; set; }
        [Reactive] public CurveViewModel CurveViewModel { get; set; }
        [Reactive] public Dictionary<string, string> Hotkeys { get; set; } = new Dictionary<string, string>();

        public double Width => Preferences.Default.PianorollWindowSize.Width;
        public double Height => Preferences.Default.PianorollWindowSize.Height;

        public bool LockPitchPoints { get => Preferences.Default.LockUnselectedNotesPitch; }
        public bool LockVibrato { get => Preferences.Default.LockUnselectedNotesVibrato; }
        public bool LockExpressions { get => Preferences.Default.LockUnselectedNotesExpressions; }
        public bool ShowPortrait { get => Preferences.Default.ShowPortrait; }
        public bool ShowIcon { get => Preferences.Default.ShowIcon; }
        public bool ShowGhostNotes { get => Preferences.Default.ShowGhostNotes; }
        public bool ShowNoteBorder { get => Preferences.Default.ShowNoteBorder; }
        public bool UseTrackColor { get => Preferences.Default.UseTrackColor; }
        public bool DegreeStyle0 { get => Preferences.Default.DegreeStyle == 0 ? true : false; }
        public bool DegreeStyle1 { get => Preferences.Default.DegreeStyle == 1 ? true : false; }
        public bool DegreeStyle2 { get => Preferences.Default.DegreeStyle == 2 ? true : false; }
        public bool LockStartTime0 { get => Preferences.Default.LockStartTime == 0 ? true : false; }
        public bool LockStartTime1 { get => Preferences.Default.LockStartTime == 1 ? true : false; }
        public bool LockStartTime2 { get => Preferences.Default.LockStartTime == 2 ? true : false; }
        public bool PlaybackAutoScroll0 { get => Preferences.Default.PlaybackAutoScroll == 0 ? true : false; }
        public bool PlaybackAutoScroll1 { get => Preferences.Default.PlaybackAutoScroll == 1 ? true : false; }
        public bool PlaybackAutoScroll2 { get => Preferences.Default.PlaybackAutoScroll == 2 ? true : false; }
        public bool PianoRollDetached { get => Preferences.Default.DetachPianoRoll; }
        [Reactive] public bool PianoRollFullscreen { get; set; }
        public bool UsesExpandedPianoRollLayout => PianoRollDetached || PianoRollFullscreen;
        public bool IsSidePanelVisible => !UsesExpandedPianoRollLayout;
        public bool IsLeftDockPanelVisible => (ShowAppearancePanel || ShowDiffSingerPanel || (ShowExpressionDefaultsPanel && IsDiffSingerTrack)) && !UsesExpandedPianoRollLayout;
        public bool IsAppearancePanelVisible => ShowAppearancePanel && !UsesExpandedPianoRollLayout;
        public bool IsDiffSingerPanelVisible => ShowDiffSingerPanel && !UsesExpandedPianoRollLayout;
        public bool IsExpressionDefaultsPanelVisible => ShowExpressionDefaultsPanel && IsDiffSingerTrack && !UsesExpandedPianoRollLayout;
        public bool IsThemeEditorPanelVisible => ShowThemeEditorPanel && IsAppearancePanelVisible;
        public bool IsAppearanceOnlyGapResizeVisible => IsLeftDockPanelVisible && !IsThemeEditorPanelVisible;
        public GridLength PianoRollSideColumnWidth => UsesExpandedPianoRollLayout ? new GridLength(0) : new GridLength(48);
        public GridLength PianoRollSideGapWidth => UsesExpandedPianoRollLayout ? new GridLength(0) : new GridLength(8);
        [Reactive] public bool ShowAppearancePanel { get; set; }
        [Reactive] public bool ShowDiffSingerPanel { get; set; }
        [Reactive] public bool ShowExpressionDefaultsPanel { get; set; }
        [Reactive] public bool IsDiffSingerTrack { get; private set; }
        [Reactive] public bool ShowThemeEditorPanel { get; set; }
        [Reactive] public string? ThemeEditorPath { get; set; }
        [Reactive] public double AppearancePanelWidth { get; set; }
        [Reactive] public double ThemeEditorPanelWidth { get; set; }
        PreferencesViewModel? appearancePreferences;
        ExpressionDefaultsViewModel? expressionDefaults;
        static PreferencesViewModel? sharedAppearancePreferences;

        public static void WarmUpAppearancePreferences() {
            if (sharedAppearancePreferences != null) {
                return;
            }
            sharedAppearancePreferences = new PreferencesViewModel();
        }

        public static void ResetSharedPreferencesViewModel() {
            sharedAppearancePreferences = null;
        }

        public static PreferencesViewModel GetSharedPreferencesViewModel() {
            WarmUpAppearancePreferences();
            return sharedAppearancePreferences!;
        }

        public PreferencesViewModel AppearancePreferences =>
            appearancePreferences ??= sharedAppearancePreferences ??= new PreferencesViewModel();
        public ExpressionDefaultsViewModel ExpressionDefaults =>
            expressionDefaults ??= new ExpressionDefaultsViewModel();
        public GridLength AppearancePanelLeadingGapWidth =>
            UsesExpandedPianoRollLayout || !IsLeftDockPanelVisible ? new GridLength(0) : new GridLength(8);
        public GridLength AppearancePanelColumnWidth =>
            UsesExpandedPianoRollLayout || !IsLeftDockPanelVisible
                ? new GridLength(0)
                : new GridLength(WorkspaceDockPanelMetrics.ClampWidth(AppearancePanelWidth));
        public GridLength ThemeEditorPanelLeadingGapWidth =>
            !IsThemeEditorPanelVisible ? new GridLength(0) : new GridLength(8);
        public GridLength ThemeEditorPanelColumnWidth =>
            !IsThemeEditorPanelVisible
                ? new GridLength(0)
                : new GridLength(WorkspaceDockPanelMetrics.ClampWidth(ThemeEditorPanelWidth));
        public ReactiveCommand<Unit, Unit> ApplyDiffSingerQualityPresetCommand { get; }
        public ReactiveCommand<Unit, Unit> ApplyDiffSingerMediumPresetCommand { get; }
        public ReactiveCommand<Unit, Unit> ApplyDiffSingerLowPresetCommand { get; }
        [Reactive] public bool DiffSingerHqPresetActive { get; private set; }
        [Reactive] public bool DiffSingerMqPresetActive { get; private set; }
        [Reactive] public bool DiffSingerLqPresetActive { get; private set; }
        public bool ShowPhonemizerTags {
            get => Preferences.Default.ShowPhonemizerTags;
            set {
                Preferences.Default.ShowPhonemizerTags = value;
                Preferences.Save();
                this.RaisePropertyChanged(nameof(ShowPhonemizerTags));
            }
        }
        [Reactive] public bool IsTikTokMode { get; set; }

        public EditTool EditTool { get; set; } = Preferences.Default.EditTool;
        [Reactive] public int ToolIndex { get; set; } = Preferences.Default.EditTool.BaseTool;
        [Reactive] public int PenToolIndex { get; set; } = Preferences.Default.EditTool.PenToolVariation;
        [Reactive] public bool PitchOverwrite { get; set; } = Preferences.Default.EditTool.OverwritePitch;
        [Reactive] public bool PitchFocusDim { get; private set; }

        public bool CursorTool => ToolIndex == 0;
        public bool PenTool => ToolIndex == 1 && PenToolIndex == 0;
        public bool PenPlusTool => ToolIndex == 1 && PenToolIndex == 1;
        public bool EraserTool => ToolIndex == 2;
        public bool KnifeTool => ToolIndex == 4;

        public ObservableCollectionExtended<MenuItemViewModel> LegacyPlugins { get; private set; }
            = new ObservableCollectionExtended<MenuItemViewModel>();
        public ObservableCollectionExtended<MenuItemViewModel> NoteBatchEdits { get; private set; }
            = new ObservableCollectionExtended<MenuItemViewModel>();
        public ObservableCollectionExtended<MenuItemViewModel> LyricBatchEdits { get; private set; }
            = new ObservableCollectionExtended<MenuItemViewModel>();
        public ObservableCollectionExtended<MenuItemViewModel> ResetBatchEdits { get; private set; }
            = new ObservableCollectionExtended<MenuItemViewModel>();
        public ObservableCollectionExtended<MenuItemViewModel> ExternalBatchEdits { get; private set; }
            = new ObservableCollectionExtended<MenuItemViewModel>();
        public ObservableCollectionExtended<MenuItemViewModel> NotesContextMenuItems { get; private set; }
            = new ObservableCollectionExtended<MenuItemViewModel>();
        public Dictionary<Key, MenuItemViewModel> LegacyPluginShortcuts { get; private set; }
            = new Dictionary<Key, MenuItemViewModel>();

        [Reactive] public double Progress { get; set; }
        [Reactive] public string ProgressText { get; set; } = string.Empty;
        [Reactive] public bool CanUndo { get; set; } = false;
        [Reactive] public bool CanRedo { get; set; } = false;
        [Reactive] public string UndoText { get; set; } = ThemeManager.GetString("menu.edit.undo");
        [Reactive] public string RedoText { get; set; } = ThemeManager.GetString("menu.edit.redo");

        public ReactiveCommand<NoteHitInfo, Unit> NoteDeleteCommand { get; set; }
        public ReactiveCommand<NoteHitInfo, Unit> NoteCopyCommand { get; set; }
        public ReactiveCommand<NoteHitInfo, Unit> ClearPhraseCacheCommand { get; set; }
        public ReactiveCommand<PitchPointHitInfo, Unit> PitEaseInOutCommand { get; set; }
        public ReactiveCommand<PitchPointHitInfo, Unit> PitLinearCommand { get; set; }
        public ReactiveCommand<PitchPointHitInfo, Unit> PitEaseInCommand { get; set; }
        public ReactiveCommand<PitchPointHitInfo, Unit> PitEaseOutCommand { get; set; }
        public ReactiveCommand<PitchPointHitInfo, Unit> PitSplineCommand { get; set; }
        public ReactiveCommand<PitchPointHitInfo, Unit> PitSnapCommand { get; set; }
        public ReactiveCommand<PitchPointHitInfo, Unit> PitDelCommand { get; set; }
        public ReactiveCommand<PitchPointHitInfo, Unit> PitAddCommand { get; set; }

        private ReactiveCommand<Classic.Plugin, Unit> legacyPluginCommand;

        public void ReloadShortcuts() {
            var newHotkeys = new Dictionary<string, string>();
            
            foreach (var sc in Preferences.Default.Shortcuts) {
                Enum.TryParse<KeyModifiers>(sc.ModifiersName, out var parsedMods);
                
                string mods = KeyTranslator.GetFriendlyModifiersName(parsedMods);
                string key = KeyTranslator.GetFriendlyName(sc.KeyName); 
                
                if (string.IsNullOrEmpty(mods) || sc.ModifiersName == "None") {
                    newHotkeys[sc.ActionId] = key;
                } else {
                    // Mac gets no separator, Windows gets standard "+" for menus
                    newHotkeys[sc.ActionId] = KeyTranslator.IsMac ? $"{mods}{key}" : $"{mods.Replace(" + ", "+")}+{key}";
                }
            }
            
            Hotkeys = newHotkeys;
        }

        bool suppressDockPanelExclusion;

        public PianoRollViewModel() {
            ReloadShortcuts();
            NotesViewModel = new NotesViewModel();
            CurveViewModel = new CurveViewModel();
            ShowAppearancePanel = Preferences.Default.ShowAppearancePanel;
            ShowDiffSingerPanel = Preferences.Default.ShowDiffSingerPanel;
            ShowExpressionDefaultsPanel = Preferences.Default.ShowExpressionDefaultsPanel;
            EnforceSingleLeftDockPanel();
            AppearancePanelWidth = WorkspaceDockPanelMetrics.ClampWidth(Preferences.Default.AppearancePanelWidth);
            ThemeEditorPanelWidth = WorkspaceDockPanelMetrics.ClampWidth(Preferences.Default.ThemeEditorPanelWidth);
            this.WhenAnyValue(vm => vm.AppearancePanelWidth)
                .Subscribe(width => {
                    var clamped = WorkspaceDockPanelMetrics.ClampWidth(width);
                    if (Math.Abs(clamped - width) > 0.5) {
                        AppearancePanelWidth = clamped;
                        return;
                    }
                    Preferences.Default.AppearancePanelWidth = clamped;
                    Preferences.Save();
                    this.RaisePropertyChanged(nameof(AppearancePanelColumnWidth));
                });
            this.WhenAnyValue(vm => vm.ThemeEditorPanelWidth)
                .Subscribe(width => {
                    var clamped = WorkspaceDockPanelMetrics.ClampWidth(width);
                    if (Math.Abs(clamped - width) > 0.5) {
                        ThemeEditorPanelWidth = clamped;
                        return;
                    }
                    Preferences.Default.ThemeEditorPanelWidth = clamped;
                    Preferences.Save();
                    this.RaisePropertyChanged(nameof(ThemeEditorPanelColumnWidth));
                });
            ApplyDiffSingerQualityPresetCommand = ReactiveCommand.Create(() => ApplyDiffSingerRenderPreset(0));
            ApplyDiffSingerMediumPresetCommand = ReactiveCommand.Create(() => ApplyDiffSingerRenderPreset(1));
            ApplyDiffSingerLowPresetCommand = ReactiveCommand.Create(() => ApplyDiffSingerRenderPreset(2));
            WarmUpAppearancePreferences();
            GetSharedPreferencesViewModel()
                .WhenAnyValue(
                    p => p.DiffSingerSteps,
                    p => p.DiffSingerStepsVariance,
                    p => p.DiffSingerStepsPitch)
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ => UpdateDiffSingerPresetHighlight());
            UpdateDiffSingerPresetHighlight();
            this.WhenAnyValue(vm => vm.ShowAppearancePanel)
                .Subscribe(show => {
                    Preferences.Default.ShowAppearancePanel = show;
                    Preferences.Save();
                    if (show && !suppressDockPanelExclusion) {
                        CloseOtherLeftDockPanels(exceptAppearance: true);
                    }
                    if (!show) {
                        CloseThemeEditor();
                    }
                    RaiseLeftDockLayoutProperties();
                });
            this.WhenAnyValue(vm => vm.ShowDiffSingerPanel)
                .Subscribe(show => {
                    Preferences.Default.ShowDiffSingerPanel = show;
                    Preferences.Save();
                    if (show && !suppressDockPanelExclusion) {
                        CloseOtherLeftDockPanels(exceptDiffSinger: true);
                    }
                    if (show) {
                        CloseThemeEditor();
                    }
                    RaiseLeftDockLayoutProperties();
                });
            this.WhenAnyValue(vm => vm.ShowExpressionDefaultsPanel)
                .Subscribe(show => {
                    Preferences.Default.ShowExpressionDefaultsPanel = show;
                    Preferences.Save();
                    if (show && !suppressDockPanelExclusion) {
                        CloseOtherLeftDockPanels(exceptExpressionDefaults: true);
                        ExpressionDefaults.AttachPart(NotesViewModel.Part as UVoicePart);
                    }
                    if (show) {
                        CloseThemeEditor();
                    }
                    RaiseLeftDockLayoutProperties();
                });
            this.WhenAnyValue(vm => vm.ShowThemeEditorPanel)
                .Subscribe(_ => RaiseThemeEditorLayoutProperties());
            MessageBus.Current.Listen<OpenDockedThemeEditorEvent>()
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(e => OpenThemeEditor(e.Path));
            MessageBus.Current.Listen<CloseDockedThemeEditorEvent>()
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ => CloseThemeEditor());

            this.WhenAnyValue(vm => vm.ToolIndex)
                .Subscribe(index => EditTool.BaseTool = index);
            this.WhenAnyValue(vm => vm.PenToolIndex)
                .Subscribe(index => EditTool.PenToolVariation = index);
            this.WhenAnyValue(vm => vm.PitchOverwrite)
                .Subscribe(val => { EditTool.OverwritePitch = val; Preferences.Default.EditTool.OverwritePitch = val; Preferences.Save(); });
            this.WhenAnyValue(vm => vm.ToolIndex, vm => vm.PitchOverwrite)
                .Subscribe(_ => UpdatePitchFocusDim());
            UpdatePitchFocusDim();

            NoteDeleteCommand = ReactiveCommand.Create<NoteHitInfo>(info => {
                NotesViewModel.DeleteSelectedNotes();
            });
            NoteCopyCommand = ReactiveCommand.Create<NoteHitInfo>(info => {
                NotesViewModel.CopyNotes();
            });
            ClearPhraseCacheCommand = ReactiveCommand.Create<NoteHitInfo>(info => {
                NotesViewModel.ClearPhraseCache();
            });
            PitEaseInOutCommand = ReactiveCommand.Create<PitchPointHitInfo>(info => {
                if (NotesViewModel.Part == null) { return; }
                DocManager.Inst.StartUndoGroup("command.pitch.editpoint");
                DocManager.Inst.ExecuteCmd(new ChangePitchPointShapeCommand(NotesViewModel.Part, info.Note.pitch.data[info.Index], PitchPointShape.io));
                DocManager.Inst.EndUndoGroup();
            });
            PitLinearCommand = ReactiveCommand.Create<PitchPointHitInfo>(info => {
                if (NotesViewModel.Part == null) { return; }
                DocManager.Inst.StartUndoGroup("command.pitch.editpoint");
                DocManager.Inst.ExecuteCmd(new ChangePitchPointShapeCommand(NotesViewModel.Part, info.Note.pitch.data[info.Index], PitchPointShape.l));
                DocManager.Inst.EndUndoGroup();
            });
            PitEaseInCommand = ReactiveCommand.Create<PitchPointHitInfo>(info => {
                if (NotesViewModel.Part == null) { return; }
                DocManager.Inst.StartUndoGroup("command.pitch.editpoint");
                DocManager.Inst.ExecuteCmd(new ChangePitchPointShapeCommand(NotesViewModel.Part, info.Note.pitch.data[info.Index], PitchPointShape.i));
                DocManager.Inst.EndUndoGroup();
            });
            PitEaseOutCommand = ReactiveCommand.Create<PitchPointHitInfo>(info => {
                if (NotesViewModel.Part == null) { return; }
                DocManager.Inst.StartUndoGroup("command.pitch.editpoint");
                DocManager.Inst.ExecuteCmd(new ChangePitchPointShapeCommand(NotesViewModel.Part, info.Note.pitch.data[info.Index], PitchPointShape.o));
                DocManager.Inst.EndUndoGroup();
            });
            PitSplineCommand = ReactiveCommand.Create<PitchPointHitInfo>(info => {
                if (NotesViewModel.Part == null) { return; }
                DocManager.Inst.StartUndoGroup("command.pitch.editpoint");
                DocManager.Inst.ExecuteCmd(new ChangePitchPointShapeCommand(NotesViewModel.Part, info.Note.pitch.data[info.Index], PitchPointShape.sp));
                DocManager.Inst.EndUndoGroup();
            });
            PitSnapCommand = ReactiveCommand.Create<PitchPointHitInfo>(info => {
                if (NotesViewModel.Part == null) { return; }
                DocManager.Inst.StartUndoGroup("command.pitch.editpoint");
                DocManager.Inst.ExecuteCmd(new SnapPitchPointCommand(NotesViewModel.Part, info.Note));
                DocManager.Inst.EndUndoGroup();
            });
            PitDelCommand = ReactiveCommand.Create<PitchPointHitInfo>(info => {
                if (NotesViewModel.Part == null) { return; }
                DocManager.Inst.StartUndoGroup("command.pitch.delete");
                DocManager.Inst.ExecuteCmd(new DeletePitchPointCommand(NotesViewModel.Part, info.Note, info.Index));
                DocManager.Inst.EndUndoGroup();
            });
            PitAddCommand = ReactiveCommand.Create<PitchPointHitInfo>(info => {
                if (NotesViewModel.Part == null) { return; }
                DocManager.Inst.StartUndoGroup("command.pitch.add");
                DocManager.Inst.ExecuteCmd(new AddPitchPointCommand(NotesViewModel.Part, info.Note, new PitchPoint(info.X, info.Y, NotePresets.Default.DefaultPitchShape), info.Index + 1));
                DocManager.Inst.EndUndoGroup();
            });

            legacyPluginCommand = ReactiveCommand.Create<Classic.Plugin>(async plugin => {
                if (NotesViewModel.Part == null || NotesViewModel.Part.notes.Count == 0) {
                    return;
                }
                DocManager.Inst.ExecuteCmd(new LoadingNotification(typeof(PianoRoll), true, "legacy plugin"));
                
                try {
                    var part = NotesViewModel.Part;
                    UNote? first;
                    UNote? last;
                    if (NotesViewModel.Selection.IsEmpty) {
                        first = part.notes.First();
                        last = part.notes.Last();
                    } else {
                        first = NotesViewModel.Selection.FirstOrDefault();
                        last = NotesViewModel.Selection.LastOrDefault();
                    }
                    var runner = PluginRunner.from(PathManager.Inst, DocManager.Inst);
                    await runner.Execute(NotesViewModel.Project, part, first, last, plugin);

                } catch (Exception e) {
                    DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
                } finally {
                    DocManager.Inst.ExecuteCmd(new LoadingNotification(typeof(PianoRoll), false, "legacy plugin"));
                }
            });
            LoadLegacyPlugins();
            MessageBus.Current.Listen<ShortcutsRefreshEvent>()
                .Subscribe(_ => ReloadShortcuts());
            NotesViewModel.WhenAnyValue(vm => vm.Part)
                .Subscribe(_ => UpdateDiffSingerTrackVisibility());
            MessageBus.Current.Listen<DiffSingerPhonemePanelAutoApplyEvent>()
                .Subscribe(_ => UpdateDiffSingerTrackVisibility());
            UpdateDiffSingerTrackVisibility();
            DocManager.Inst.AddSubscriber(this);
        }

        private void SetUndoState() {
            CanUndo = DocManager.Inst.GetUndoState(out string? undoNameKey);
            if (!string.IsNullOrWhiteSpace(undoNameKey)) {
                UndoText = $"{ThemeManager.GetString("menu.edit.undo")}: {ThemeManager.GetString(undoNameKey)}";
            } else {
                UndoText = ThemeManager.GetString("menu.edit.undo");
            }
            CanRedo = DocManager.Inst.GetRedoState(out string? redoNameKey);
            if (!string.IsNullOrWhiteSpace(redoNameKey)) {
                RedoText = $"{ThemeManager.GetString("menu.edit.redo")}:  {ThemeManager.GetString(redoNameKey)}";
            } else {
                RedoText = ThemeManager.GetString("menu.edit.redo");
            }
        }

        private void LoadLegacyPlugins() {
            LegacyPlugins.Clear();
            
            LegacyPlugins.AddRange(DocManager.Inst.Plugins.Select(plugin => new MenuItemViewModel() {
                Header = plugin.Name,
                InputGesture = KeyTranslator.GetGesture(plugin.Name),
                Command = legacyPluginCommand,
                CommandParameter = plugin,
            }));

            LegacyPluginShortcuts.Clear();
            foreach (MenuItemViewModel menu in LegacyPlugins) {
                if (menu.InputGesture != null && !LegacyPluginShortcuts.ContainsKey(menu.InputGesture.Key)) {
                    LegacyPluginShortcuts.Add(menu.InputGesture.Key, menu);
                }
            }

            LegacyPlugins.Add(new MenuItemViewModel() { Header = "-", Height = 1 });
            LegacyPlugins.Add(new MenuItemViewModel() {
                Header = ThemeManager.GetString("pianoroll.menu.plugin.openfolder"),
                Command = ReactiveCommand.Create(() => {
                    try { OS.OpenFolder(PathManager.Inst.PluginsPath); } 
                    catch (Exception e) { DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e)); }
                })
            });
            LegacyPlugins.Add(new MenuItemViewModel() {
                Header = ThemeManager.GetString("pianoroll.menu.plugin.reload"),
                Command = ReactiveCommand.Create(() => {
                    DocManager.Inst.SearchAllLegacyPlugins();
                    LoadLegacyPlugins();
                })
            });
        }

        public void Undo() => DocManager.Inst.Undo();
        public void Redo() => DocManager.Inst.Redo();
        public void Cut() {
            if (CurveViewModel.IsSelected(NotesViewModel.PrimaryKey)) {
                CurveViewModel.Cut(NotesViewModel.Part!);
            } else {
                NotesViewModel.CutNotes();
            }
        }
        public void Copy() {
            if (CurveViewModel.IsSelected(NotesViewModel.PrimaryKey)) {
                CurveViewModel.Copy(NotesViewModel.Part!);
            } else {
                NotesViewModel.CopyNotes();
            }
        }
        public void Paste() {
            if (DocManager.Inst.NotesClipboard != null && DocManager.Inst.NotesClipboard.Count > 0) {
                NotesViewModel.PasteNotes();
            } else if (DocManager.Inst.CurvesClipboard != null && NotesViewModel.Part != null) {
                var track = NotesViewModel.Project.tracks[NotesViewModel.Part.trackNo];
                if (track.TryGetExpDescriptor(NotesViewModel.Project, NotesViewModel.PrimaryKey, out var descriptor)) {
                    CurveViewModel.Paste(NotesViewModel.Part, descriptor);
                }
            }
        }
        public void PastePlain() => NotesViewModel.PastePlainNotes();
        public void Delete() => NotesViewModel.DeleteSelectedNotes();
        public void SelectAll() => NotesViewModel.SelectAllNotes();

        public void MouseoverPhoneme(UPhoneme? phoneme) {
            MessageBus.Current.SendMessage(new PhonemeMouseoverEvent(phoneme));
        }

        void UpdatePitchFocusDim() {
            bool active = EditTool.IsPitchTool;
            if (PitchFocusDim == active) {
                return;
            }
            PitchFocusDim = active;
            MessageBus.Current.SendMessage(new NotesRefreshEvent());
        }

        static readonly (string Label, int Acoustic, int Variance, int Pitch)[] DiffSingerRenderPresets = {
            ("HQ", 50, 50, 20),
            ("MQ", 20, 20, 20),
            ("LQ", 1, 1, 20),
        };

        void UpdateDiffSingerPresetHighlight() {
            int acoustic = Preferences.Default.DiffSingerSteps;
            int variance = Preferences.Default.DiffSingerStepsVariance;
            int pitch = Preferences.Default.DiffSingerStepsPitch;
            DiffSingerHqPresetActive = MatchesDiffSingerRenderPreset(0, acoustic, variance, pitch);
            DiffSingerMqPresetActive = MatchesDiffSingerRenderPreset(1, acoustic, variance, pitch);
            DiffSingerLqPresetActive = MatchesDiffSingerRenderPreset(2, acoustic, variance, pitch);
        }

        static bool MatchesDiffSingerRenderPreset(int preset, int acoustic, int variance, int pitch) {
            if (preset < 0 || preset >= DiffSingerRenderPresets.Length) {
                return false;
            }
            var (_, presetAcoustic, presetVariance, presetPitch) = DiffSingerRenderPresets[preset];
            return acoustic == presetAcoustic && variance == presetVariance && pitch == presetPitch;
        }

        void ApplyDiffSingerRenderPreset(int preset) {
            if (preset < 0 || preset >= DiffSingerRenderPresets.Length) {
                return;
            }
            var (label, acoustic, variance, pitch) = DiffSingerRenderPresets[preset];
            Preferences.Default.DiffSingerSteps = acoustic;
            Preferences.Default.DiffSingerStepsVariance = variance;
            Preferences.Default.DiffSingerStepsPitch = pitch;
            Preferences.Save();
            var prefs = appearancePreferences ?? sharedAppearancePreferences;
            if (prefs != null) {
                prefs.DiffSingerSteps = acoustic;
                prefs.DiffSingerStepsVariance = variance;
                prefs.DiffSingerStepsPitch = pitch;
            }
            UpdateDiffSingerPresetHighlight();
            var message = string.Format(
                ThemeManager.GetString("progress.diffsinger.preset"),
                label, acoustic, variance, pitch);
            DocManager.Inst.ExecuteCmd(new ProgressBarNotification(0, message, autoClearSeconds: 4));
        }

        public void OpenThemeEditor(string path) {
            ThemeEditorWindow.CloseIfOpen();
            ThemeEditorPath = path;
            ShowThemeEditorPanel = true;
            ThemeEditorDockState.SetOpen(true);
            RaiseThemeEditorLayoutProperties();
        }

        public void CloseThemeEditor() {
            if (!ShowThemeEditorPanel && ThemeEditorPath == null) {
                return;
            }
            ShowThemeEditorPanel = false;
            ThemeEditorPath = null;
            ThemeEditorDockState.SetOpen(false);
            RaiseThemeEditorLayoutProperties();
            App.SetTheme();
        }

        void UpdateDiffSingerTrackVisibility() {
            var singer = GetOpenTrackSinger();
            bool isDiffSinger = singer is { Found: true, SingerType: USingerType.DiffSinger };
            if (IsDiffSingerTrack != isDiffSinger) {
                IsDiffSingerTrack = isDiffSinger;
                if (!isDiffSinger) {
                    if (ShowDiffSingerPanel) {
                        ShowDiffSingerPanel = false;
                    }
                    // Keep ShowExpressionDefaultsPanel preference; hide via IsExpressionDefaultsPanelVisible.
                }
                RaiseLeftDockLayoutProperties();
            }
            if (isDiffSinger) {
                SyncOpenTrackExpressionSuggestions();
            }
            ApplyDiffSingerPhonemePanelAuto(isDiffSinger);
        }

        void SyncOpenTrackExpressionSuggestions() {
            var part = NotesViewModel.Part;
            var project = DocManager.Inst.Project;
            if (part == null || part.trackNo < 0 || part.trackNo >= project.tracks.Count) {
                return;
            }
            if (ExpressionSuggestionSync.UpsertSuggested(project, project.tracks[part.trackNo])) {
                DocManager.Inst.ExecuteCmd(new ExpressionsSuggestedNotification());
            }
        }

        void EnforceSingleLeftDockPanel() {
            int open = 0;
            if (ShowAppearancePanel) open++;
            if (ShowDiffSingerPanel) open++;
            if (ShowExpressionDefaultsPanel) open++;
            if (open <= 1) {
                return;
            }
            // Prefer keeping Appearance if multiple were persisted open.
            if (ShowAppearancePanel) {
                ShowDiffSingerPanel = false;
                ShowExpressionDefaultsPanel = false;
            } else if (ShowDiffSingerPanel) {
                ShowExpressionDefaultsPanel = false;
            }
        }

        void CloseOtherLeftDockPanels(
            bool exceptAppearance = false,
            bool exceptDiffSinger = false,
            bool exceptExpressionDefaults = false) {
            suppressDockPanelExclusion = true;
            if (!exceptAppearance && ShowAppearancePanel) {
                ShowAppearancePanel = false;
            }
            if (!exceptDiffSinger && ShowDiffSingerPanel) {
                ShowDiffSingerPanel = false;
            }
            if (!exceptExpressionDefaults && ShowExpressionDefaultsPanel) {
                ShowExpressionDefaultsPanel = false;
            }
            suppressDockPanelExclusion = false;
        }

        static void ApplyDiffSingerPhonemePanelAuto(bool isDiffSinger) {
            if (!Preferences.Default.DiffSingerPhonemePanelAuto) {
                return;
            }
            if (Preferences.Default.DiffSingerPhonemePanelMode == isDiffSinger) {
                return;
            }
            Preferences.Default.DiffSingerPhonemePanelMode = isDiffSinger;
            Preferences.Save();
            MessageBus.Current.SendMessage(new NotesRefreshEvent());
        }

        USinger? GetOpenTrackSinger() {
            var part = NotesViewModel.Part;
            var project = NotesViewModel.Project;
            if (part == null || project == null || part.trackNo < 0 || part.trackNo >= project.tracks.Count) {
                return null;
            }
            return project.tracks[part.trackNo].Singer;
        }

        void RaiseLeftDockLayoutProperties() {
            RaiseThemeEditorLayoutProperties();
            this.RaisePropertyChanged(nameof(IsLeftDockPanelVisible));
            this.RaisePropertyChanged(nameof(IsAppearancePanelVisible));
            this.RaisePropertyChanged(nameof(IsDiffSingerPanelVisible));
            this.RaisePropertyChanged(nameof(IsExpressionDefaultsPanelVisible));
            this.RaisePropertyChanged(nameof(AppearancePanelLeadingGapWidth));
            this.RaisePropertyChanged(nameof(AppearancePanelColumnWidth));
        }

        void RaiseThemeEditorLayoutProperties() {
            this.RaisePropertyChanged(nameof(IsThemeEditorPanelVisible));
            this.RaisePropertyChanged(nameof(IsAppearanceOnlyGapResizeVisible));
            this.RaisePropertyChanged(nameof(ThemeEditorPanelLeadingGapWidth));
            this.RaisePropertyChanged(nameof(ThemeEditorPanelColumnWidth));
        }

        #region ICmdSubscriber

        public void OnNext(UCommand cmd, bool isUndo) {
            if (cmd is TrackChangeSingerCommand trackChangeSinger) {
                var part = NotesViewModel.Part;
                if (part != null && trackChangeSinger.track.TrackNo == part.trackNo) {
                    UpdateDiffSingerTrackVisibility();
                }
            }
            if (cmd is ProgressBarNotification progressBarNotification) {
                if (UsesExpandedPianoRollLayout) {
                    Dispatcher.UIThread.InvokeAsync(() => {
                        ApplyProgressNotification(progressBarNotification);
                    }, DispatcherPriority.Background);
                }
            }
            SetUndoState();
        }

        void ApplyProgressNotification(ProgressBarNotification progressBarNotification) {
            ProgressText = progressBarNotification.Info;
            if (progressBarNotification.Progress <= 0 && string.IsNullOrEmpty(progressBarNotification.Info)) {
                progressTarget = 0;
                Progress = 0;
                ProgressText = string.Empty;
                progressSmoothTimer?.Stop();
                return;
            }
            AnimateProgressTo(progressBarNotification.Progress);
        }

        void AnimateProgressTo(double target) {
            progressTarget = Math.Clamp(target, 0, 100);
            if (Progress <= 0.01 && progressTarget > 0) {
                Progress = Math.Max(1.0, progressTarget * 0.04);
            }
            if (progressSmoothTimer == null) {
                progressSmoothTimer = new DispatcherTimer {
                    Interval = TimeSpan.FromMilliseconds(16),
                };
                progressSmoothTimer.Tick += (_, _) => {
                    double delta = progressTarget - Progress;
                    if (Math.Abs(delta) < 0.4) {
                        Progress = progressTarget;
                        if (progressTarget <= 0) {
                            progressSmoothTimer?.Stop();
                        }
                        return;
                    }
                    Progress += delta * 0.22;
                };
            }
            if (!progressSmoothTimer.IsEnabled) {
                progressSmoothTimer.Start();
            }
        }

        #endregion
    }
}
