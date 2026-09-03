using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using DynamicData;
using DynamicData.Binding;
using OpenUtau.App;
using OpenUtau.App.Controls;
using OpenUtau.App.Views;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using OpenUtau.ViewModels;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;
using SharpCompress;

namespace OpenUtau.App.ViewModels {
    public class NotesRefreshEvent { }
    public class DiffSingerPhonemePanelAutoApplyEvent { }
    public class NotesSelectionEvent {
        public readonly UNote[] selectedNotes;
        public readonly UNote[] tempSelectedNotes;
        public NotesSelectionEvent(NoteSelectionViewModel selection) {
            selectedNotes = selection.ToArray();
            tempSelectedNotes = selection.TempSelectedNotes.ToArray();
        }
    }
    public class WaveformRefreshEvent { }

    public class NotesViewModel : ViewModelBase, ICmdSubscriber {
        [Reactive] public Rect Bounds { get; set; }
        public int TickCount => Part?.Duration ?? 480 * 4;
        public int TrackCount => ViewConstants.MaxTone;
        [Reactive] public double TickWidth { get; set; }
        public double TrackHeightMin => ViewConstants.NoteHeightMin;
        public double TrackHeightMax => ViewConstants.NoteHeightMax;
        [Reactive] public double TrackHeight { get; set; }
        [Reactive] public int TickOrigin { get; set; }
        [Reactive] public double TickOffset { get; set; }
        [Reactive] public double TrackOffset { get; set; }
        [Reactive] public int SnapDiv { get; set; }
        [Reactive] public int Key { get; set; }
        [Reactive] public bool KeyIsMajor { get; set; }
        [Reactive] public bool ShowKeyScale { get; set; }
        public ObservableCollectionExtended<int> SnapTicks { get; } = new ObservableCollectionExtended<int>();
        [Reactive] public double PlayPosX { get; set; }
        [Reactive] public double PlayPosHighlightX { get; set; }
        [Reactive] public double PlayPosHighlightWidth { get; set; }
        [Reactive] public bool PlayPosWaitingRendering { get; set; }
        [Reactive] public int PlayPosTick { get; set; }
        [Reactive] public bool ShowPlaybackNoteHighlight { get; set; }
        [Reactive] public bool ShowPlaybackNoteBounce { get; set; }
        [Reactive] public bool UseModernPlayhead { get; set; }
        public bool HasRangeSelection => DocManager.Inst.rangeEndTick > DocManager.Inst.rangeStartTick;
        public bool ShowPlaybackBarHighlight => !PlaybackManager.Inst.PlayingMaster || HasRangeSelection;
        public bool ShowClassicPlayPosMarker => !UseModernPlayhead;
        public bool ShowModernPlayPosMarker => UseModernPlayhead;
        public bool ShowWidePlayPosBar =>
            ShowPlaybackBarHighlight && (HasRangeSelection || !UseModernPlayhead);
        public bool ShowThinPlayPosLine {
            get {
                if (UseModernPlayhead) {
                    return true;
                }
                return PlaybackManager.Inst.PlayingMaster;
            }
        }

        public void RefreshPlaybackHighlightVisibility() {
            this.RaisePropertyChanged(nameof(ShowPlaybackBarHighlight));
            this.RaisePropertyChanged(nameof(ShowClassicPlayPosMarker));
            this.RaisePropertyChanged(nameof(ShowModernPlayPosMarker));
            this.RaisePropertyChanged(nameof(ShowWidePlayPosBar));
            this.RaisePropertyChanged(nameof(ShowThinPlayPosLine));
        }
        [Reactive] public bool ShowTips { get; set; }
        [Reactive] public bool PlayTone { get; set; }
        [Reactive] public bool LivePitchNormal { get; set; }
        [Reactive] public bool LivePitchSuperFast { get; set; }
        bool livePitchSyncing;
        [Reactive] public bool ShowVibrato { get; set; }
        [Reactive] public bool ShowPitch { get; set; }
        [Reactive] public bool ShowFinalPitch { get; set; }
        [Reactive] public bool ShowWaveform { get; set; }
        [Reactive] public bool ShowPitchFollowPath { get; set; }
        [Reactive] public bool ShowPhoneme { get; set; }
        [Reactive] public bool ShowNoteParams { get; set; }
        [Reactive] public double NotePropertiesPanelWidth { get; set; }
        [Reactive] public bool ShowExpressions { get; set; }
        [Reactive] public bool ShowRealCurves { get; set; }
        [Reactive] public bool IsSnapOn { get; set; }
        [Reactive] public string SnapDivText { get; set; }
        [Reactive] public string KeyText { get; set; }
        [Reactive] public Rect ExpBounds { get; set; }
        [Reactive] public string PrimaryKey { get; set; }
        [Reactive] public bool PrimaryKeyNotSupported { get; set; }
        [Reactive] public bool ShowCurveToolbar { get; set; }
        [Reactive] public string SecondaryKey { get; set; }
        [Reactive] public double ExpTrackHeight { get; set; }
        [Reactive] public double ExpShadowOpacity { get; set; }
        [Reactive] public double ExpHeightMin { get; set; }
        [Reactive] public double ExpHeightMax { get; set; }
        [Reactive] public double PhonemePanelHeight { get; set; }
        [Reactive] public double PhonemePanelHeightMin { get; set; }
        [Reactive] public double PhonemePanelHeightMax { get; set; }
        public bool PhonemePanelResizeEnabled => Preferences.Default.DiffSingerPhonemePanelMode;
        public bool PhonemePanelDetached => ShowPhoneme && Preferences.Default.DiffSingerPhonemePanelMode;
        public bool ShowEmbeddedPhoneme => ShowPhoneme && !Preferences.Default.DiffSingerPhonemePanelMode;
        public double PhonemeEmbeddedHeight => ViewConstants.PhonemeEmbeddedHeight;
        public double WaveformBottomMargin => ShowEmbeddedPhoneme ? ViewConstants.PhonemeEmbeddedHeight + 4 : 4;
        public double SearchBarBottomMargin => ShowEmbeddedPhoneme ? 70 + ViewConstants.PhonemeEmbeddedHeight : 70;
        public Thickness WaveformBottomMarginThickness => new Thickness(0, 0, 0, WaveformBottomMargin);
        public Thickness SearchBarBottomMarginThickness => new Thickness(12, 12, 12, SearchBarBottomMargin);
        // Tag strip (20px) only in DiffSinger panel mode when lang prefix is shown for the open track.
        public double PhonemePanelTagStripHeight {
            get {
                if (!Preferences.Default.DiffSingerPhonemePanelMode || Part == null) {
                    return 0;
                }
                var track = Part.trackNo < Project.tracks.Count ? Project.tracks[Part.trackNo] : null;
                if (PhonemeUIRender.ShouldHideLangPrefixForDisplay(track)) {
                    return 0;
                }
                return ViewConstants.PhonemeTagStripHeight;
            }
        }
        public Thickness PianoRollHScrollBottomMargin => new Thickness(0, 0, 0, ShowEmbeddedPhoneme ? ViewConstants.PhonemeEmbeddedHeight + 4 : 4);
        public GridLength PhonemeGapGridLength => ShowPhoneme && PhonemePanelDetached ? new GridLength(8) : new GridLength(0);
        public double PhonemePanelOuterHeight => PhonemePanelHeight + PhonemePanelTagStripHeight;
        public GridLength PhonemePanelOuterGridLength => ShowPhoneme && PhonemePanelDetached
            ? new GridLength(PhonemePanelOuterHeight)
            : new GridLength(0);
        public double PhonemePanelOuterMinHeight => ShowPhoneme && PhonemePanelDetached
            ? PhonemePanelHeightMin + PhonemePanelTagStripHeight
            : 0;
        public GridLength ExpGapGridLength => ShowExpressions ? new GridLength(8) : new GridLength(0);
        public GridLength ExpPanelGridLength => ShowExpressions ? new GridLength(ViewConstants.ExpPanelHeightDefault) : new GridLength(0);
        public GridLength PhonemePanelHeightGridLength => new GridLength(PhonemePanelHeight + PhonemePanelTagStripHeight);
        public GridLength NotePropsGapGridLength => ShowNoteParams ? new GridLength(8) : new GridLength(0);
        public GridLength NotePropsColumnWidth => ShowNoteParams
            ? new GridLength(NotePropsPanelMetrics.ClampWidth(NotePropertiesPanelWidth))
            : new GridLength(0);
        public bool NotePropsHidden => !ShowNoteParams;
        [Reactive] public UVoicePart? Part { get; set; }
        [Reactive] public Bitmap? Avatar { get; set; }
        [Reactive] public Bitmap? Portrait { get; set; }
        [Reactive] public IBrush? PortraitMask { get; set; }
        [Reactive] public string WindowTitle { get; set; } = "Piano Roll";
        [Reactive] public string PartDisplayName { get; set; } = string.Empty;
        [Reactive] public SolidColorBrush TrackAccentColor { get; set; } = ThemeManager.GetTrackColor("Blue").AccentColor;
        [Reactive] public SolidColorBrush TrackNoteColor { get; set; } = ThemeManager.GetTrackColor("Blue").NoteColor;
        public double ViewportTicks => viewportTicks.Value;
        public double ViewportTracks => viewportTracks.Value;
        public double SmallChangeX => smallChangeX.Value;
        public double SmallChangeY => smallChangeY.Value;
        public double HScrollBarMax => Math.Max(0, TickCount - ViewportTicks);
        public double VScrollBarMax => Math.Max(0, TrackCount - ViewportTracks);
        public UProject Project => DocManager.Inst.Project;
        [Reactive] public List<MenuItemViewModel> SnapDivs { get; set; }
        [Reactive] public List<MenuItemViewModel> Keys { get; set; }

        public ReactiveCommand<int, Unit> SetSnapUnitCommand { get; set; }
        public ReactiveCommand<int, Unit> SetKeyCommand { get; set; }

        // See the comments on TracksViewModel.playPosXToTickOffset
        private double playPosXToTickOffset => Bounds.Width != 0 ? ViewportTicks / Bounds.Width : 0;

        // Smooth scroll for Stationary Cursor (PlaybackAutoScroll == 1): exponential ease toward target.
        private const double SmoothScrollSnapThreshold = 0.05;
        /// <summary>Target TickOffset for smooth stationary-cursor scroll; null when not in use.</summary>
        private double? smoothScrollTargetTickOffset;
        /// <summary>True after playback-driven smooth scroll was active; used to discard stale targets on pause/EOF.</summary>
        private bool stationaryCursorFollowedPlayback;
        /// <summary>True while SmoothScrollStep is updating TickOffset, so we don't treat that as user scroll.</summary>
        private bool _inSmoothScrollStep;

        private readonly PlaybackPitchFollowPath pitchFollowPath = new PlaybackPitchFollowPath();
        private readonly List<PitchFollowPathSamplePoint> pitchFollowPathSamples = new();
        public IReadOnlyList<PitchFollowPathSamplePoint> PitchFollowPathSamples => pitchFollowPathSamples;
        public bool PitchFollowPathIsBuilt => pitchFollowPath.IsBuilt;
        private bool _inPitchFollowScrollStep;
        private bool pitchFollowUserOverride;
        private bool pitchFollowWasPlaying;
        private DateTime pitchFollowLastStepUtc;
        private bool pitchFollowRenderingActive;

        private readonly ObservableAsPropertyHelper<double> viewportTicks;
        private readonly ObservableAsPropertyHelper<double> viewportTracks;
        private readonly ObservableAsPropertyHelper<double> smallChangeX;
        private readonly ObservableAsPropertyHelper<double> smallChangeY;

        public readonly NoteSelectionViewModel Selection = new NoteSelectionViewModel();

        internal NotesViewModelHitTest HitTest;
        private int _lastNoteLength = 480;
        private UNote[] playbackNotes = Array.Empty<UNote>();
        private string? portraitSource;
        private readonly object portraitLock = new object();
        private Bitmap? portraitFull;
        private int portraitRasterHeight;
        private int userSnapDiv = -2;
        private int userKey => Project.key;

        public NotesViewModel() {
            SnapDivs = new List<MenuItemViewModel>();
            SetSnapUnitCommand = ReactiveCommand.Create<int>(div => {
                userSnapDiv = div;
                UpdateSnapDiv();
            });

            Keys = new List<MenuItemViewModel>();
            SetKeyCommand = ReactiveCommand.Create<int>(encoded => {
                var key = KeySignatureHelper.Decode(encoded);
                DocManager.Inst.StartUndoGroup("command.project.key");
                DocManager.Inst.ExecuteCmd(new KeyCommand(Project, key.Tonic, key.IsMajor));
                DocManager.Inst.EndUndoGroup();
                UpdateKey();
            });

            viewportTicks = this.WhenAnyValue(x => x.Bounds, x => x.TickWidth)
                .Select(v => v.Item1.Width / Math.Max(v.Item2, ViewConstants.TickWidthMin))
                .ToProperty(this, x => x.ViewportTicks);
            viewportTracks = this.WhenAnyValue(x => x.Bounds, x => x.TrackHeight)
                .Select(v => v.Item1.Height / v.Item2)
                .ToProperty(this, x => x.ViewportTracks);
            smallChangeX = this.WhenAnyValue(x => x.ViewportTicks)
                .Select(w => w / 8)
                .ToProperty(this, x => x.SmallChangeX);
            smallChangeY = this.WhenAnyValue(x => x.ViewportTracks)
                .Select(h => h / 8)
                .ToProperty(this, x => x.SmallChangeY);
            this.WhenAnyValue(x => x.Bounds)
                .Subscribe(_ => {
                    OnXZoomed(new Point(), 0);
                    OnYZoomed(new Point(), 0);
                });

            this.WhenAnyValue(x => x.TickWidth)
                .Subscribe(tickWidth => {
                    UpdateSnapDiv();
                    SetPlayPos(DocManager.Inst.playPosTick, false);
                });
            this.WhenAnyValue(x => x.TickOffset)
                .Subscribe(tickOffset => {
                    SetPlayPos(DocManager.Inst.playPosTick, false);
                });
            this.WhenAnyValue(x => x.ExpBounds, x => x.PrimaryKey)
                .Subscribe(t => {
                    UExpressionDescriptor? descriptor = null;
                    if (t.Item2 != null) {
                        UExpressionDescriptor trackDesc = default!;
                        bool hasTrackDesc = Part != null && Project.tracks[Part.trackNo]
                            .TryGetExpDescriptor(Project, t.Item2, out trackDesc);
                        if (hasTrackDesc) {
                            descriptor = trackDesc;
                        } else if (Project.expressions.TryGetValue(t.Item2, out var projDesc)) {
                            descriptor = projDesc;
                        }
                    }
                    if (descriptor != null) {
                        if (descriptor.type == UExpressionType.Options) {
                            int numOptions = Math.Max(descriptor.options.Length, 1);
                            ExpTrackHeight = t.Item1.Height / numOptions;
                            ExpShadowOpacity = 0;
                        } else {
                            ExpTrackHeight = 0;
                        }
                        ShowCurveToolbar = descriptor.type == UExpressionType.Curve;
                    } else {
                        ExpTrackHeight = 0;
                        ExpShadowOpacity = 0.3;
                        ShowCurveToolbar = false;
                    }
                });
            this.WhenAnyValue(x => x.Project)
                .Subscribe(project => {
                    if (project == null) {
                        return;
                    }
                    SnapDivs.Clear();
                    SnapDivs.Add(new MenuItemViewModel {
                        Header = ThemeManager.GetString("pianoroll.toggle.snap.auto"),
                        Command = SetSnapUnitCommand,
                        CommandParameter = -2,
                    });
                    SnapDivs.Add(new MenuItemViewModel {
                        Header = ThemeManager.GetString("pianoroll.toggle.snap.autotriplet"),
                        Command = SetSnapUnitCommand,
                        CommandParameter = -3,
                    });
                    SnapDivs.AddRange(MusicMath.GetSnapDivs(project.resolution)
                        .Select(div => new MenuItemViewModel {
                            Header = $"1/{div}",
                            Command = SetSnapUnitCommand,
                            CommandParameter = div,
                        }));
                    Keys.Clear();
                    Keys.AddRange(KeySignatureHelper.AllKeys()
                        .Select(key => new MenuItemViewModel {
                            Header = KeySignatureHelper.FormatKey(key),
                            Command = SetKeyCommand,
                            CommandParameter = KeySignatureHelper.Encode(key),
                        }));
                });

            ShowTips = Preferences.Default.ShowTips;
            ShowKeyScale = Preferences.Default.ShowKeyScaleOnPianoRoll;
            this.WhenAnyValue(x => x.ShowKeyScale)
                .Skip(1)
                .Subscribe(showKeyScale => {
                    Preferences.Default.ShowKeyScaleOnPianoRoll = showKeyScale;
                    Preferences.Save();
                    MessageBus.Current.SendMessage(new PianorollRefreshEvent("KeyScale"));
                });
            IsSnapOn = true;
            SnapDivText = string.Empty;
            KeyText = string.Empty;

            PlayTone = Preferences.Default.PlayTone;
            this.WhenAnyValue(x => x.PlayTone)
                .Subscribe(playTone => {
                 Preferences.Default.PlayTone = playTone;
                 Preferences.Save();
             });
            ApplyLivePitchModeFromPreferences();
            this.WhenAnyValue(x => x.LivePitchNormal)
                .Subscribe(checkedNormal => {
                    if (livePitchSyncing) {
                        return;
                    }
                    if (checkedNormal) {
                        SetLivePitchMode(LivePitchMode.Normal);
                    } else if (Preferences.Default.RealTimePitchMode == (int)LivePitchMode.Normal) {
                        SetLivePitchMode(LivePitchMode.Off);
                    }
                });
            this.WhenAnyValue(x => x.LivePitchSuperFast)
                .Subscribe(checkedFast => {
                    if (livePitchSyncing) {
                        return;
                    }
                    if (checkedFast) {
                        SetLivePitchMode(LivePitchMode.SuperFast);
                    } else if (Preferences.Default.RealTimePitchMode == (int)LivePitchMode.SuperFast) {
                        SetLivePitchMode(LivePitchMode.Off);
                    }
                });
            ShowVibrato = Preferences.Default.ShowVibrato;
            this.WhenAnyValue(x => x.ShowVibrato)
            .Subscribe(showVibrato => {
                Preferences.Default.ShowVibrato = showVibrato;
                Preferences.Save();
            });
            ShowPitch = Preferences.Default.ShowPitch;
            this.WhenAnyValue(x => x.ShowPitch)
            .Subscribe(showPitch => {
                Preferences.Default.ShowPitch = showPitch;
                Preferences.Save();
            });
            ShowFinalPitch = Preferences.Default.ShowFinalPitch;
            this.WhenAnyValue(x => x.ShowFinalPitch)
            .Subscribe(showFinalPitch => {
                Preferences.Default.ShowFinalPitch = showFinalPitch;
                Preferences.Save();
            });
            ShowWaveform = Preferences.Default.ShowWaveform;
            this.WhenAnyValue(x => x.ShowWaveform)
            .Subscribe(showWaveform => {
                Preferences.Default.ShowWaveform = showWaveform;
                Preferences.Save();
            });
            ShowPitchFollowPath = IsPitchFollowPathPreviewVisible();
            this.WhenAnyValue(x => x.ShowPitchFollowPath)
            .Subscribe(showPath => {
                if (!Preferences.Default.PlaybackPitchFollowEnabled) {
                    return;
                }
                Preferences.Default.PlaybackPitchFollowShowPath = showPath;
                Preferences.Save();
                RefreshPitchFollowPathPreview();
            });
            ShowPhoneme = Preferences.Default.ShowPhoneme;
            UpdatePhonemePanelLayoutConstraints();
            this.WhenAnyValue(x => x.PhonemePanelHeight)
                .Subscribe(_ => {
                    this.RaisePropertyChanged(nameof(PhonemePanelOuterHeight));
                    this.RaisePropertyChanged(nameof(PhonemePanelOuterGridLength));
                    this.RaisePropertyChanged(nameof(PhonemePanelHeightGridLength));
                });
            MessageBus.Current.Listen<NotesRefreshEvent>()
                .Subscribe(_ => {
                    UpdatePhonemePanelLayoutConstraints();
                    RaisePhonemePanelLayoutChanged();
                });
            this.WhenAnyValue(x => x.ShowPhoneme)
            .Subscribe(showPhoneme => {
                Preferences.Default.ShowPhoneme = showPhoneme;
                Preferences.Save();
                this.RaisePropertyChanged(nameof(PhonemeGapGridLength));
                this.RaisePropertyChanged(nameof(PhonemePanelDetached));
                this.RaisePropertyChanged(nameof(ShowEmbeddedPhoneme));
                this.RaisePropertyChanged(nameof(WaveformBottomMargin));
                this.RaisePropertyChanged(nameof(SearchBarBottomMargin));
                this.RaisePropertyChanged(nameof(WaveformBottomMarginThickness));
                this.RaisePropertyChanged(nameof(SearchBarBottomMarginThickness));
            this.RaisePropertyChanged(nameof(PianoRollHScrollBottomMargin));
                this.RaisePropertyChanged(nameof(PhonemePanelOuterHeight));
                this.RaisePropertyChanged(nameof(PhonemePanelOuterGridLength));
                this.RaisePropertyChanged(nameof(PhonemePanelOuterMinHeight));
            });
            ShowExpressions = Preferences.Default.ShowExpressions;
            this.WhenAnyValue(x => x.ShowExpressions)
            .Subscribe(showExpressions => {
                ExpHeightMin = showExpressions
                    ? ViewConstants.ExpHeightMin : 0;
                ExpHeightMax = showExpressions
                    ? ViewConstants.ExpHeightMax : 0;
                Preferences.Default.ShowExpressions = showExpressions;
                Preferences.Save();
                this.RaisePropertyChanged(nameof(ExpGapGridLength));
                this.RaisePropertyChanged(nameof(ExpPanelGridLength));
            });
            ShowRealCurves = Preferences.Default.ShowRealCurves;
            this.WhenAnyValue(x => x.ShowRealCurves)
            .Subscribe(showRealCurves => {
                Preferences.Default.ShowRealCurves = showRealCurves;
                Preferences.Save();
            });
            ShowNoteParams = Preferences.Default.ShowNoteParams;
            ShowPlaybackNoteHighlight = Preferences.Default.ShowPlaybackNoteHighlight;
            ShowPlaybackNoteBounce = Preferences.Default.ShowPlaybackNoteBounce;
            NotePropertiesPanelWidth = NotePropsPanelMetrics.ClampWidth(Preferences.Default.NotePropertiesPanelWidth);
            this.WhenAnyValue(x => x.ShowNoteParams)
            .Subscribe(showNoteParams => {
                Preferences.Default.ShowNoteParams = showNoteParams;
                Preferences.Save();
                this.RaisePropertyChanged(nameof(NotePropsGapGridLength));
                this.RaisePropertyChanged(nameof(NotePropsColumnWidth));
                this.RaisePropertyChanged(nameof(NotePropsHidden));
            });
            this.WhenAnyValue(x => x.NotePropertiesPanelWidth)
            .Subscribe(width => {
                var clamped = NotePropsPanelMetrics.ClampWidth(width);
                if (System.Math.Abs(clamped - width) > 0.01) {
                    NotePropertiesPanelWidth = clamped;
                    return;
                }
                Preferences.Default.NotePropertiesPanelWidth = clamped;
                Preferences.Save();
                this.RaisePropertyChanged(nameof(NotePropsColumnWidth));
            });
            UseModernPlayhead = Preferences.Default.UseModernPlayhead;
            MessageBus.Current.Listen<PlayheadModeChangedEvent>()
                .Subscribe(e => {
                    UseModernPlayhead = e.UseModernPlayhead;
                    this.RaisePropertyChanged(nameof(ShowClassicPlayPosMarker));
                    this.RaisePropertyChanged(nameof(ShowModernPlayPosMarker));
                    RefreshPlaybackHighlightVisibility();
                    if (Part != null) {
                        SetPlayPos(DocManager.Inst.playPosTick, false);
                    }
                });
            // When user scrolls (scrollbar, zoom, pan), sync smooth-scroll target so we don't pull the view back.
            this.WhenAnyValue(x => x.TickOffset)
                .Skip(1)
                .Subscribe(_ => {
                    if (!_inSmoothScrollStep && Preferences.Default.PlaybackAutoScroll == 1) {
                        smoothScrollTargetTickOffset = TickOffset;
                    }
                });
            this.WhenAnyValue(x => x.Part)
                .Subscribe(_ => {
                    smoothScrollTargetTickOffset = null;
                    stationaryCursorFollowedPlayback = false;
                });

            this.WhenAnyValue(x => x.ViewportTracks)
                .Subscribe(_ => RebuildPitchFollowPath());
            this.WhenAnyValue(x => x.TrackOffset)
                .Skip(1)
                .Subscribe(_ => {
                    if (!_inPitchFollowScrollStep && Preferences.Default.PlaybackPitchFollowEnabled) {
                        pitchFollowUserOverride = true;
                    }
                });
            MessageBus.Current.Listen<PlaybackPitchFollowSettingsChangedEvent>()
                .Subscribe(_ => {
                    RebuildPitchFollowPath();
                    ShowPitchFollowPath = IsPitchFollowPathPreviewVisible();
                });

            TickWidth = ViewConstants.PianoRollTickWidthDefault;
            TrackHeight = ViewConstants.NoteHeightDefault;
            TrackOffset = 4 * 12 + 6;
            if (Preferences.Default.ShowTips) {
                Preferences.Default.ShowTips = false;
                Preferences.Save();
            }
            PrimaryKey = Core.Format.Ustx.VEL;
            SecondaryKey = Core.Format.Ustx.VOL;

            HitTest = new NotesViewModelHitTest(this);
            DocManager.Inst.AddSubscriber(this);

            this.WhenAnyValue(x => x.Part)
                .Subscribe(p => {
                    MessageBus.Current.SendMessage(new PianoRollOpenPartChangedEvent(p));
                    PublishPianoRollViewport();
                });

            this.WhenAnyValue(x => x.TickOffset, x => x.ViewportTicks, x => x.Bounds)
                .Subscribe(_ => PublishPianoRollViewport());

            MessageBus.Current.Listen<PianorollRefreshEvent>()
                .Subscribe(e => {
                    switch (e.refreshItem) {
                        case "Part":
                            if (Part == null || Project == null) {
                                UnloadPart();
                            } else {
                                LoadPart(Part, Project);
                            }
                            break;
                        case "Portrait":
                            LoadPortrait(Part, Project);
                            break;
                        case "TrackColor":
                            LoadTrackColor(Part, Project);
                            break;
                        case "PlaybackNoteHighlight":
                            ShowPlaybackNoteHighlight = Preferences.Default.ShowPlaybackNoteHighlight;
                            break;
                        case "PlaybackNoteBounce":
                            ShowPlaybackNoteBounce = Preferences.Default.ShowPlaybackNoteBounce;
                            break;
                    }
                });
            MessageBus.Current.Listen<CurveSelectionEvent>()
                .Subscribe(e => {
                    Selection.SelectNone();
                    MessageBus.Current.SendMessage(new NotesSelectionEvent(Selection));
                });
            MessageBus.Current.Listen<CurveCopyEvent>()
                .Subscribe(e => {
                    DocManager.Inst.NotesClipboard?.Clear();
                });
            MessageBus.Current.Listen<ThemeChangedEvent>()
                .Subscribe(_ => RefreshTrackColorBrushes(Part, Project));
        }

        private void UpdateSnapDiv() {
            if (userSnapDiv > 0) {
                SnapDiv = userSnapDiv;
                SnapDivText = $"1/{userSnapDiv}";
                return;
            }
            MusicMath.GetSnapUnit(
                Project.resolution,
                ViewConstants.PianoRollMinTicklineWidth / TickWidth,
                userSnapDiv % 3 == 0,
                out int ticks,
                out int div);
            SnapDiv = div;
            SnapDivText = $"(1/{div})";
        }

        private void UpdateKey() {
            Key = userKey;
            KeyIsMajor = Project.keyIsMajor;
            KeyText = KeySignatureHelper.FormatProjectKey(Project, shortName: true);
        }

        public void OnXZoomed(Point position, double delta) {
            bool recenter = true;
            if (TickOffset == 0 && position.X < 0.1) {
                recenter = false;
            }
            double center = TickOffset + position.X * ViewportTicks;
            double tickWidth = TickWidth * (1.0 + delta * 2);
            tickWidth = Math.Clamp(tickWidth, ViewConstants.PianoRollTickWidthMin, ViewConstants.PianoRollTickWidthMax);
            tickWidth = Math.Max(tickWidth, Bounds.Width / TickCount);
            TickWidth = tickWidth;
            double tickOffset = recenter
                    ? center - position.X * ViewportTicks
                    : TickOffset;
            TickOffset = Math.Clamp(tickOffset, 0, HScrollBarMax);
            Notify();
        }

        public void OnYZoomed(Point position, double delta) {
            double center = TrackOffset + position.Y * ViewportTracks;
            double trackHeight = TrackHeight * (1.0 + delta * 2);
            trackHeight = Math.Clamp(trackHeight, ViewConstants.NoteHeightMin, ViewConstants.NoteHeightMax);
            trackHeight = Math.Max(trackHeight, Bounds.Height / TrackCount);
            TrackHeight = trackHeight;
            double trackOffset = center - position.Y * ViewportTracks;
            TrackOffset = Math.Clamp(trackOffset, 0, VScrollBarMax);
            Notify();
        }

        private void Notify() {
            this.RaisePropertyChanged(nameof(TickCount));
            this.RaisePropertyChanged(nameof(HScrollBarMax));
            this.RaisePropertyChanged(nameof(ViewportTicks));
            this.RaisePropertyChanged(nameof(TrackCount));
            this.RaisePropertyChanged(nameof(VScrollBarMax));
            this.RaisePropertyChanged(nameof(ViewportTracks));
            PublishPianoRollViewport();
        }

        void PublishPianoRollViewport() {
            if (Part == null || ViewportTicks <= 0) {
                MessageBus.Current.SendMessage(new PianoRollViewportChangedEvent(0, 0));
                return;
            }
            MessageBus.Current.SendMessage(new PianoRollViewportChangedEvent(TickOffset, ViewportTicks));
        }

        /// <summary>
        /// Convert mouse position in piano roll window to tick in part
        /// </summary>
        /// <param name="point">Mouse position</param>
        /// <returns>Tick position related to the beginning of the part</returns>
        public int PointToTick(Point point) {
            return (int)(point.X / TickWidth + TickOffset);
        }

        public void TickToLineTick(int tick, out int left, out int right) {
            if (SnapTicks.Count == 0) {
                left = 0;
                right = Project.resolution;
                return;
            }
            int index = SnapTicks.BinarySearch(tick + TickOrigin);
            if (index < 0) {
                index = ~index - 1;
            }
            if (0 >= SnapTicks.Count - 2) {
                left = right = tick;
                return;
            }
            index = Math.Clamp(index, 0, SnapTicks.Count - 2);
            left = SnapTicks[index] - TickOrigin;
            right = SnapTicks[index + 1] - TickOrigin;
        }

        public void PointToLineTick(Point point, out int left, out int right) {
            int tick = PointToTick(point);
            TickToLineTick(tick, out left, out right);
        }

        public int PointToTone(Point point) {
            return ViewConstants.MaxTone - 1 - (int)(point.Y / TrackHeight + TrackOffset);
        }
        public double PointToToneDouble(Point point) {
            return ViewConstants.MaxTone - 1 - (point.Y / TrackHeight + TrackOffset) + 0.5;
        }
        public Point TickToneToPoint(double tick, double tone) {
            return new Point(
                (tick - TickOffset) * TickWidth,
                (ViewConstants.MaxTone - 1 - tone - TrackOffset) * TrackHeight);
        }
        public Point TickToneToPoint(Vector2 tickTone) {
            return TickToneToPoint(tickTone.X, tickTone.Y);
        }
        public Size TickToneToSize(double ticks, double tone) {
            return new Size(ticks * TickWidth, tone * TrackHeight);
        }

        public UNote? MaybeAddNote(Point point, bool useLastLength) {
            if (Part == null) {
                return null;
            }
            var project = DocManager.Inst.Project;
            int tone = PointToTone(point);
            if (tone >= ViewConstants.MaxTone || tone < 0) {
                return null;
            }
            int snapUnit = project.resolution * 4 / SnapDiv;
            int tick = PointToTick(point);
            int snappedTick = (int)Math.Floor((double)tick / snapUnit) * snapUnit;
            UNote note = project.CreateNote(tone, snappedTick,
                useLastLength ? _lastNoteLength : IsSnapOn ? snapUnit : 15);
            DocManager.Inst.ExecuteCmd(new AddNoteCommand(Part, note));
            return note;
        }

        private void LoadPart(UPart part, UProject project) {
            if (!(part is UVoicePart)) {
                return;
            }
            UnloadPart();
            Part = part as UVoicePart;
            UpdatePhonemePanelLayoutConstraints();
            OnPartModified();
            RebuildPlaybackNoteIndex();
            LoadPortrait(part, project);
            LoadWindowTitle(part, project);
            LoadTrackColor(part, project);
            UpdateKey();
            RebuildPitchFollowPath();
            pitchFollowUserOverride = false;
        }

        //If PortraitHeight is 0, keep the source image at full resolution.
        //If PortraitHeight isn't 0, downscale to the specified height when the source is taller.
        private static Bitmap ScalePortraitToHeight(Bitmap source, int portraitHeight) {
            if (portraitHeight <= 0 || source.PixelSize.Height <= portraitHeight) {
                return source;
            }
            int targetWidth = Math.Max(1, (int)Math.Round(
                portraitHeight * source.PixelSize.Width / (double)source.PixelSize.Height));
            return source.CreateScaledBitmap(
                new PixelSize(targetWidth, portraitHeight),
                BitmapInterpolationMode.HighQuality);
        }

        void DisposePortraitBitmaps() {
            if (!ReferenceEquals(Portrait, portraitFull)) {
                Portrait?.Dispose();
            }
            Portrait = null;
            portraitFull?.Dispose();
            portraitFull = null;
            portraitRasterHeight = 0;
        }

        public void EnsurePortraitForDisplayHeight(double displayHeight, double renderScale = 1.0) {
            lock (portraitLock) {
                if (portraitFull == null) {
                    return;
                }
                int targetH = (int)Math.Ceiling(displayHeight * renderScale);
                targetH = Math.Clamp(targetH, 1, portraitFull.PixelSize.Height);
                if (Portrait != null && portraitRasterHeight == targetH) {
                    return;
                }
                Bitmap next;
                if (targetH >= portraitFull.PixelSize.Height) {
                    next = portraitFull;
                } else {
                    int targetW = Math.Max(1, (int)Math.Round(
                        targetH * portraitFull.PixelSize.Width / (double)portraitFull.PixelSize.Height));
                    next = portraitFull.CreateScaledBitmap(
                        new PixelSize(targetW, targetH),
                        BitmapInterpolationMode.HighQuality);
                }
                if (!ReferenceEquals(Portrait, portraitFull) && Portrait != null) {
                    Portrait.Dispose();
                }
                Portrait = next;
                portraitRasterHeight = targetH;
            }
            this.RaisePropertyChanged(nameof(Portrait));
        }

        private void LoadPortrait(UPart? part, UProject? project) {
            if (part == null || project == null) {
                lock (portraitLock) {
                    DisposePortraitBitmaps();
                    portraitSource = null;
                }
                return;
            }
            var singer = project.tracks[part.trackNo].Singer;
            lock (portraitLock) {
                Avatar?.Dispose();
                Avatar = null;
                if (singer != null && singer.AvatarData != null && Preferences.Default.ShowIcon) {
                    try {
                        using (var stream = new MemoryStream(singer.AvatarData)) {
                            Avatar = new Bitmap(stream);
                        }
                    } catch (Exception e) {
                        Avatar?.Dispose();
                        Avatar = null;
                        Log.Error(e, $"Failed to load Avatar {singer.Avatar}");
                    }
                }
            }
            if (singer == null || string.IsNullOrEmpty(singer.Portrait) || !Preferences.Default.ShowPortrait) {
                lock (portraitLock) {
                    DisposePortraitBitmaps();
                    portraitSource = null;
                }
                return;
            }
            if (portraitSource == singer.Portrait && portraitFull != null) {
                return;
            }
            var portraitKey = singer.Portrait;
            var portraitOpacity = singer.PortraitOpacity;
            var portraitHeight = singer.PortraitHeight;
            PortraitMask = new SolidColorBrush(Avalonia.Media.Colors.White, portraitOpacity);
            Task.Run(() => {
                Bitmap? loaded = null;
                try {
                    var data = singer.LoadPortrait();
                    if (data != null) {
                        using var stream = new MemoryStream(data);
                        loaded = new Bitmap(stream);
                        if (portraitHeight > 0) {
                            var scaled = ScalePortraitToHeight(loaded, portraitHeight);
                            if (!ReferenceEquals(scaled, loaded)) {
                                loaded.Dispose();
                                loaded = scaled;
                            }
                        }
                    }
                } catch (Exception e) {
                    loaded?.Dispose();
                    loaded = null;
                    Log.Error(e, $"Failed to load Portrait {portraitKey}");
                }
                var bitmap = loaded;
                Dispatcher.UIThread.Post(() => {
                    lock (portraitLock) {
                        DisposePortraitBitmaps();
                        portraitFull = bitmap;
                        Portrait = bitmap;
                        portraitRasterHeight = bitmap?.PixelSize.Height ?? 0;
                        portraitSource = bitmap == null ? null : portraitKey;
                    }
                    this.RaisePropertyChanged(nameof(Portrait));
                    MessageBus.Current.SendMessage(new PianorollRefreshEvent("Portrait"));
                });
            });
        }
        private void LoadWindowTitle(UPart? part, UProject? project) {
            if (part == null || project == null) {
                WindowTitle = "Piano Roll";
                PartDisplayName = string.Empty;
                return;
            }
            WindowTitle = project.tracks[part.trackNo].TrackName + " - " + part.DisplayName;
            PartDisplayName = part.DisplayName;
        }

        private void LoadTrackColor(UPart? part, UProject? project) {
            RefreshTrackColorBrushes(part, project);
            string name = part == null || project == null
                ? "Blue"
                : Preferences.Default.UseTrackColor
                    ? project.tracks[part.trackNo].TrackColor
                    : "Blue";
            ThemeManager.ChangePianorollColor(name);
        }

        private void RefreshTrackColorBrushes(UPart? part, UProject? project) {
            if (!Preferences.Default.UseTrackColor) {
                var noteBrush = ThemeManager.AccentBrush1Note as SolidColorBrush
                    ?? ThemeManager.GetTrackColor("Blue").NoteColor;
                var accentBrush = ThemeManager.AccentBrush2 as SolidColorBrush
                    ?? ThemeManager.GetTrackColor("Blue").AccentColor;
                TrackAccentColor = accentBrush;
                TrackNoteColor = new SolidColorBrush(noteBrush.Color) {
                    Opacity = Preferences.Default.NoteOpacity * 0.5
                };
                return;
            }
            if (part == null || project == null) {
                TrackAccentColor = ThemeManager.GetTrackColor("Blue").AccentColor;
                TrackNoteColor = CreateTrackNoteChromeBrush(ThemeManager.GetTrackColor("Blue"));
                return;
            }
            var trackColor = ThemeManager.GetTrackColor(project.tracks[part.trackNo].TrackColor);
            TrackAccentColor = trackColor.AccentColor;
            TrackNoteColor = CreateTrackNoteChromeBrush(trackColor);
        }

        static SolidColorBrush CreateTrackNoteChromeBrush(TrackColor trackColor) {
            // Piano-roll chrome used Opacity=0.5 on top of the old baked ~0.44 note alpha.
            return new SolidColorBrush(trackColor.NoteColor.Color) {
                Opacity = Preferences.Default.NoteOpacity * 0.5
            };
        }

        private void UnloadPart() {
            DeselectNotes();
            Part = null;
            playbackNotes = Array.Empty<UNote>();
            pitchFollowPath.Build(null, 0, 0, 0, 0, Project.resolution);
            LoadPortrait(null, null);
            LoadWindowTitle(null, null);
        }

        private void OnPartModified() {
            if (Part == null) {
                return;
            }
            TickOrigin = Part.position;
            RaisePhonemePanelLayoutChanged();
            RebuildPitchFollowPath();
            Notify();
        }

        bool? lastDiffSingerPhonemePanelMode;

        void UpdatePhonemePanelLayoutConstraints() {
            bool mode = Preferences.Default.DiffSingerPhonemePanelMode;
            if (mode) {
                PhonemePanelHeightMin = ViewConstants.PhonemePanelHeightMin;
                PhonemePanelHeightMax = ViewConstants.PhonemePanelHeightMax;
                bool enteringDiffSingerMode = lastDiffSingerPhonemePanelMode != true;
                bool classicHeightLeftover =
                    Math.Abs(PhonemePanelHeight - ViewConstants.PhonemeEmbeddedHeight) < 0.01;
                if (enteringDiffSingerMode || classicHeightLeftover
                    || PhonemePanelHeight < PhonemePanelHeightMin) {
                    PhonemePanelHeight = ViewConstants.PhonemePanelHeightMin;
                }
            } else {
                PhonemePanelHeightMin = ViewConstants.PhonemeEmbeddedHeight;
                PhonemePanelHeightMax = ViewConstants.PhonemeEmbeddedHeight;
                PhonemePanelHeight = ViewConstants.PhonemeEmbeddedHeight;
            }
            lastDiffSingerPhonemePanelMode = mode;
            this.RaisePropertyChanged(nameof(PhonemePanelResizeEnabled));
            this.RaisePropertyChanged(nameof(PhonemePanelDetached));
            this.RaisePropertyChanged(nameof(ShowEmbeddedPhoneme));
            this.RaisePropertyChanged(nameof(PhonemeEmbeddedHeight));
            this.RaisePropertyChanged(nameof(WaveformBottomMargin));
            this.RaisePropertyChanged(nameof(SearchBarBottomMargin));
            this.RaisePropertyChanged(nameof(WaveformBottomMarginThickness));
            this.RaisePropertyChanged(nameof(SearchBarBottomMarginThickness));
            this.RaisePropertyChanged(nameof(PianoRollHScrollBottomMargin));
        }

        void RaisePhonemePanelLayoutChanged() {
            this.RaisePropertyChanged(nameof(PhonemePanelTagStripHeight));
            this.RaisePropertyChanged(nameof(PhonemePanelDetached));
            this.RaisePropertyChanged(nameof(ShowEmbeddedPhoneme));
            this.RaisePropertyChanged(nameof(PhonemeEmbeddedHeight));
            this.RaisePropertyChanged(nameof(WaveformBottomMargin));
            this.RaisePropertyChanged(nameof(SearchBarBottomMargin));
            this.RaisePropertyChanged(nameof(WaveformBottomMarginThickness));
            this.RaisePropertyChanged(nameof(SearchBarBottomMarginThickness));
            this.RaisePropertyChanged(nameof(PianoRollHScrollBottomMargin));
            this.RaisePropertyChanged(nameof(PhonemeGapGridLength));
            this.RaisePropertyChanged(nameof(PhonemePanelOuterHeight));
            this.RaisePropertyChanged(nameof(PhonemePanelOuterGridLength));
            this.RaisePropertyChanged(nameof(PhonemePanelOuterMinHeight));
            this.RaisePropertyChanged(nameof(PhonemePanelHeightGridLength));
        }

        private void DeselectNote(UNote note) {
            if (Selection.Remove(note)) {
                MessageBus.Current.SendMessage(new NotesSelectionEvent(Selection));
            }
        }

        public void DeselectNotes() {
            Selection.SelectNone();
            MessageBus.Current.SendMessage(new NotesSelectionEvent(Selection));
        }

        public void ToggleSelectNote(UNote note) {
            /// <summary>
            /// Change the selection state of a note without affecting the selection state of the other notes.
            /// Add it to selection if it isn't selected, or deselect it if it is already selected.
            /// </summary>
            if (Part == null) {
                return;
            }
            if (Selection.Contains(note)) {
                DeselectNote(note);
            } else {
                SelectNote(note, false);
            }
        }

        public void SelectNote(UNote note) {
            /// <summary>
            /// Select a note and deselect all the other notes.
            /// </summary>
            SelectNote(note, true);
        }
        public void SelectNote(UNote note, bool deselectExisting) {
            if (Part == null) {
                return;
            }
            if (deselectExisting ? Selection.Select(note) : Selection.Add(note)) {
                MessageBus.Current.SendMessage(new NotesSelectionEvent(Selection));
            }
        }
        public void MoveSelection(int delta) {
            if (Selection.Move(delta)) {
                MessageBus.Current.SendMessage(new NotesSelectionEvent(Selection));
                ScrollIntoView(Selection.Head!);
            }
        }
        public void ExtendSelection(int delta) {
            if (Selection.Resize(delta)) {
                MessageBus.Current.SendMessage(new NotesSelectionEvent(Selection));
                ScrollIntoView(Selection.Head!);
            }
        }
        public void ExtendSelection(UNote note) {
            if (Selection.SelectTo(note)) {
                MessageBus.Current.SendMessage(new NotesSelectionEvent(Selection));
            }
        }

        public void MoveCursor(int delta) {
            if (!Selection.IsEmpty) {
                MoveSelection(delta);
                return;
            }
            var target = Part!.notes.FirstOrDefault();
            if (target == null) {
                return;
            }
            var centerTick = TickOffset + ViewportTicks * 0.5;
            // get closest note to center, without going over
            while (target.Next != null && (target!.position < TickOffset || target!.Next.position < centerTick)) {
                target = target.Next;
            }
            SelectNote(target);
            ScrollIntoView(target);
        }

        public void SelectAllNotes() {
            if (Part == null) {
                return;
            }
            Selection.Select(Part);
            MessageBus.Current.SendMessage(new NotesSelectionEvent(Selection));
        }

        public void SelectNotesUntil(UNote note) {
            if (Part == null) {
                return;
            }
            if (Part.notes.Intersect(Selection).ToList().Count == 0) {
                SelectNote(note);
                return;
            }
            var thisIndex = Part.notes.IndexOf(note);
            if (thisIndex < 0) {
                return;
            }
            var firstSelectedNote = Part.notes.FirstOrDefault(x => Selection.Contains(x));
            if (firstSelectedNote == null) {
                return;
            }
            var rangeStart = Part.notes.IndexOf(firstSelectedNote);
            var lastSelectedNote = Part.notes.LastOrDefault(x => Selection.Contains(x));
            if (lastSelectedNote == null) {
                return;
            }
            var rangeEndInclusive = Part.notes.IndexOf(lastSelectedNote);
            int rangeToAddStart;
            int rangeToAddEndInclusive;
            if (thisIndex < rangeStart) {
                rangeToAddStart = thisIndex;
                rangeToAddEndInclusive = rangeEndInclusive;
            } else if (thisIndex > rangeEndInclusive) {
                rangeToAddStart = rangeStart;
                rangeToAddEndInclusive = thisIndex;
            } else {
                rangeToAddStart = rangeStart;
                rangeToAddEndInclusive = rangeEndInclusive;
            }
            var notesToAdd = Part.notes.ToList().GetRange(rangeToAddStart, rangeToAddEndInclusive - rangeToAddStart + 1);
            var changed = Selection.Add(notesToAdd);
            if (changed) {
                MessageBus.Current.SendMessage(new NotesSelectionEvent(Selection));
            }
        }

        public void TempSelectNotes(int x0, int x1, int y0, int y1) {
            if (Part == null) {
                return;
            }
            var tempNotes = Part.notes
                .Where(note => note.End > x0 && note.position < x1 && note.tone > y0 && note.tone <= y1)
                .ToList();

            Selection.SetTemporarySelection(tempNotes);
            MessageBus.Current.SendMessage(new NotesSelectionEvent(Selection));
        }

        public void CommitTempSelectNotes() {
            Selection.CommitTemporarySelection();
            MessageBus.Current.SendMessage(new NotesSelectionEvent(Selection));
        }

        public void CleanupSelectedNotes() {
            if (Part == null) {
                return;
            }
            var toCleanup = Selection.Except(Part.notes).ToList();
            Selection.Remove(toCleanup);
        }

        public void InsertNote() {
            if (Part == null) {
                return;
            }

            var project = DocManager.Inst.Project;
            int snapUnit = project.resolution * 4 / SnapDiv;

            var fromNote = Selection.LastOrDefault();
            int DEFAULT_TONE = 12 * 5; // C4
            int tone = fromNote?.tone ?? DEFAULT_TONE;
            int tick = fromNote?.RightBound ?? (int)TickOffset;
            int dur = fromNote?.duration ?? snapUnit;
            DocManager.Inst.StartUndoGroup("command.note.add");
            UNote note = DocManager.Inst.Project.CreateNote(tone, tick, dur);
            DocManager.Inst.ExecuteCmd(new AddNoteCommand(Part, note));
            SelectNote(note);
            DocManager.Inst.EndUndoGroup();
        }

        public void TransposeSelection(int deltaNoteNum) {
            if (Part == null || Selection.IsEmpty) {
                return;
            }
            var selectedNotes = Selection.ToList();
            if (selectedNotes.Any(note => note.tone + deltaNoteNum <= 0 || note.tone + deltaNoteNum >= ViewConstants.MaxTone)) {
                return;
            }
            DocManager.Inst.StartUndoGroup("command.note.move");
            DocManager.Inst.ExecuteCmd(new MoveNoteCommand(Part, selectedNotes, 0, deltaNoteNum));
            DocManager.Inst.EndUndoGroup();
        }
        public void MoveSelectedNotes(int deltaTicks) {
            if (Part == null || Selection.IsEmpty) {
                return;
            }
            var selectedNotes = Selection.ToList();
            // TODO REVIEW should the end be clamped to end of part? or allow to go over?
            //var delta = Math.Clamp(deltaTicks, -1 * selectedNotes.First().position, Part.End - selectedNotes.Last().position);
            var delta = Math.Max(deltaTicks, -1 * selectedNotes.First().position);

            DocManager.Inst.StartUndoGroup("command.note.move");
            DocManager.Inst.ExecuteCmd(new MoveNoteCommand(Part, selectedNotes, delta, 0));
            DocManager.Inst.EndUndoGroup();
        }

        public void ResizeSelectedNotes(int deltaTicks) {
            if (Part == null || Selection.IsEmpty) {
                return;
            }

            var selectedNotes = Selection.ToList();

            // ignore if change would make a note smaller than minimal size
            if (deltaTicks < 0) {
                int smallestDuration = selectedNotes.Select(n => n.duration).Min();

                var project = DocManager.Inst.Project;
                int snapUnit = project.resolution * 4 / SnapDiv;
                int minNoteTicks = IsSnapOn ? snapUnit : 15;

                if (smallestDuration + deltaTicks < minNoteTicks) {
                    return;
                }
            }
            DocManager.Inst.StartUndoGroup("command.note.edit");
            DocManager.Inst.ExecuteCmd(new ResizeNoteCommand(Part, selectedNotes, deltaTicks));
            DocManager.Inst.EndUndoGroup();
        }

        public void MergeSelectedNotes() {
            if (Part == null || Selection.IsEmpty || Selection.Count <= 1) {
                return;
            }
            var notes = Selection.ToList();
            notes.Sort((a, b) => a.position.CompareTo(b.position));
            //Ignore slur lyrics
            var mergedLyrics = String.Join("", notes.Select(x => x.lyric).Where(l => !l.StartsWith("+")));
            if (mergedLyrics == "") { //If all notes are slur, the merged note is single slur note
                mergedLyrics = notes[0].lyric;
            }
            DocManager.Inst.StartUndoGroup("command.note.edit");
            DocManager.Inst.ExecuteCmd(new ChangeNoteLyricCommand(Part, notes[0], mergedLyrics));
            DocManager.Inst.ExecuteCmd(new ResizeNoteCommand(Part, notes[0], notes.Last().End - notes[0].End));
            notes.RemoveAt(0);
            DocManager.Inst.ExecuteCmd(new RemoveNoteCommand(Part, notes));
            DocManager.Inst.EndUndoGroup();
        }

        internal void DeleteSelectedNotes() {
            if (Part == null || Selection.IsEmpty) {
                return;
            }
            DocManager.Inst.StartUndoGroup("command.note.delete");
            DocManager.Inst.ExecuteCmd(new RemoveNoteCommand(Part, Selection.ToList()));
            DocManager.Inst.EndUndoGroup();
        }

        public void CopyNotes() {
            if (Part != null && !Selection.IsEmpty) {
                var selectedNotes = Selection.ToList();
                DocManager.Inst.NotesClipboard = selectedNotes.Select(note => note.Clone()).ToList();
            }
        }

        public void CutNotes() {
            if (Part != null && !Selection.IsEmpty) {
                var selectedNotes = Selection.ToList();
                DocManager.Inst.NotesClipboard = selectedNotes.Select(note => note.Clone()).ToList();
                DocManager.Inst.StartUndoGroup("command.note.delete");
                DocManager.Inst.ExecuteCmd(new RemoveNoteCommand(Part, selectedNotes));
                DocManager.Inst.EndUndoGroup();
            }
        }

        public void PasteNotes() {
            if (Part != null && DocManager.Inst.NotesClipboard != null && DocManager.Inst.NotesClipboard.Count > 0) {
                int snapUnit = DocManager.Inst.Project.resolution * 4 / SnapDiv;
                int left = (DocManager.Inst.playPosTick / snapUnit) * snapUnit;
                int minPosition = DocManager.Inst.NotesClipboard.Select(note => note.position).Min();
                //If PlayPos is before the beginning of the part, don't paste.
                if (left < Part.position) {
                    return;
                }
                int offset = left - minPosition - Part.position;
                var notes = DocManager.Inst.NotesClipboard.Select(note => note.Clone()).ToList();
                notes.ForEach(note => note.position += offset);
                DocManager.Inst.StartUndoGroup("command.note.paste");
                DocManager.Inst.ExecuteCmd(new AddNoteCommand(Part, notes));
                int minDurTick = Part.GetMinDurTick(Project);
                if (Part.Duration < minDurTick) {
                    DocManager.Inst.ExecuteCmd(new ResizeVoicePartCommand(Project, Part, minDurTick - Part.Duration, false));
                }
                DocManager.Inst.EndUndoGroup();
                Selection.Select(notes);
                MessageBus.Current.SendMessage(new NotesSelectionEvent(Selection));

                var note = notes.First();
                if (left < TickOffset || TickOffset + ViewportTicks < note.position + note.duration + Part.position) {
                    TickOffset = Math.Clamp(note.position + note.duration * 0.5 - ViewportTicks * 0.5, 0, HScrollBarMax);
                }
            }
        }

        /// <summary>
        /// Paste notes but only keep tone, relative position, duration and lyric.
        /// </summary>
        public void PastePlainNotes() {
            UNote toPlainNote(UNote note) {
                var plainNote = DocManager.Inst.Project.CreateNote(
                    note.tone,
                    note.position,
                    note.duration);
                plainNote.lyric = note.lyric;
                return plainNote;
            }

            if (Part != null && DocManager.Inst.NotesClipboard != null && DocManager.Inst.NotesClipboard.Count > 0) {
                int snapUnit = DocManager.Inst.Project.resolution * 4 / SnapDiv;
                int left = (DocManager.Inst.playPosTick / snapUnit) * snapUnit;
                int minPosition = DocManager.Inst.NotesClipboard.Select(note => note.position).Min();
                //If PlayPos is before the beginning of the part, don't paste.
                if (left < Part.position) {
                    return;
                }
                int offset = left - minPosition - Part.position;
                var notes = DocManager.Inst.NotesClipboard.Select(note => toPlainNote(note)).ToList();
                notes.ForEach(note => note.position += offset);
                DocManager.Inst.StartUndoGroup("command.note.paste");
                DocManager.Inst.ExecuteCmd(new AddNoteCommand(Part, notes));
                int minDurTick = Part.GetMinDurTick(Project);
                if (Part.Duration < minDurTick) {
                    DocManager.Inst.ExecuteCmd(new ResizeVoicePartCommand(Project, Part, minDurTick - Part.Duration, false));
                }
                DocManager.Inst.EndUndoGroup();
                Selection.Select(notes);
                MessageBus.Current.SendMessage(new NotesSelectionEvent(Selection));

                var note = notes.First();
                if (left < TickOffset || TickOffset + ViewportTicks < note.position + note.duration + Part.position) {
                    TickOffset = Math.Clamp(note.position + note.duration * 0.5 - ViewportTicks * 0.5, 0, HScrollBarMax);
                }
            }
        }

        public async void PasteSelectedParams(Window window) {
            if (Part != null && DocManager.Inst.NotesClipboard != null && DocManager.Inst.NotesClipboard.Count > 0) {
                var selectedNotes = Selection.ToList();
                if (selectedNotes.Count == 0) {
                    return;
                }

                var dialog = new PasteParamDialog();
                var vm = new PasteParamViewModel();
                dialog.DataContext = vm;
                await dialog.ShowDialog(window);

                if (dialog.Apply) {
                    DocManager.Inst.StartUndoGroup("command.parameter.paste");

                    int c = 0;
                    var track = Project.tracks[Part.trackNo];
                    foreach (var note in selectedNotes) {
                        var copyNote = DocManager.Inst.NotesClipboard[c];

                        for (int i = 0; i < vm.Params.Count; i++) {
                            switch (i) {
                                case 0:
                                    if (vm.Params[i].IsSelected) {
                                        DocManager.Inst.ExecuteCmd(new SetPitchPointsCommand(Part, note, copyNote.pitch));
                                    }
                                    break;
                                case 1:
                                    if (vm.Params[i].IsSelected) {
                                        DocManager.Inst.ExecuteCmd(new SetVibratoCommand(Part, note, copyNote.vibrato));
                                    }
                                    break;
                                default:
                                    if (vm.Params[i].IsSelected) {
                                        float?[] values = copyNote.GetExpressionNoteHas(Project, track, vm.Params[i].Abbr);
                                        DocManager.Inst.ExecuteCmd(new SetNoteExpressionCommand(Project, track, Part, note, vm.Params[i].Abbr, values));
                                    }
                                    break;
                            }
                        }

                        c++;
                        if (c >= DocManager.Inst.NotesClipboard.Count) {
                            c = 0;
                        }
                    }
                    DocManager.Inst.EndUndoGroup();
                }
            }
        }

        public void ToggleVibrato(UNote note) {
            if (Part == null) {
                return;
            }
            var vibrato = note.vibrato;
            DocManager.Inst.StartUndoGroup("command.vibrato.edit");
            DocManager.Inst.ExecuteCmd(new VibratoLengthCommand(Part, note, vibrato.length == 0 ? NotePresets.Default.DefaultVibrato.VibratoLength : 0));
            DocManager.Inst.EndUndoGroup();
        }

        public void ClearPhraseCache() {
            if (Part != null && !Selection.IsEmpty) {
                DocManager.Inst.ExecuteCmd(new ProgressBarNotification(0, ThemeManager.GetString("progress.clearingcache")));
                var selectedNotes = Selection.ToList();
                var phrases = Part.renderPhrases.Where(phrase => selectedNotes.Any(note => phrase.notes.Any(rnote => rnote.position == Part.position + note.position - phrase.position && rnote.duration == note.duration)));
                foreach (var phrase in phrases) {
                    phrase.DeleteCacheFiles();
                }
                DocManager.Inst.ExecuteCmd(new ProgressBarNotification(0, ThemeManager.GetString("progress.cachecleared")));
            }
        }

        public class PlayheadModeChangedEvent {
            public readonly bool UseModernPlayhead;
            public PlayheadModeChangedEvent(bool useModernPlayhead) {
                UseModernPlayhead = useModernPlayhead;
            }
        }

        private void SetPlayPos(int tick, bool waitingRendering) {
            PlayPosWaitingRendering = waitingRendering;
            tick -= Part?.position ?? 0;
            PlayPosTick = tick;
            PlayPosX = TickToneToPoint(tick, 0).X;
            UpdateHighlight();
            this.RaisePropertyChanged(nameof(ShowThinPlayPosLine));
        }

        private void RebuildPlaybackNoteIndex() {
            playbackNotes = Part?.notes.ToArray() ?? Array.Empty<UNote>();
        }

        public UNote? FindVoiceNoteAtTick(int tick) {
            int low = 0;
            int high = playbackNotes.Length - 1;
            while (low <= high) {
                int mid = low + (high - low) / 2;
                var note = playbackNotes[mid];
                if (tick < note.LeftBound) {
                    high = mid - 1;
                } else if (tick >= note.RightBound) {
                    low = mid + 1;
                } else {
                    return note;
                }
            }
            return null;
        }

        private void UpdateHighlight() {
            if (DocManager.Inst.rangeEndTick > DocManager.Inst.rangeStartTick) {
                int partPos = Part?.position ?? 0;
                int left = DocManager.Inst.rangeStartTick - partPos;
                int right = DocManager.Inst.rangeEndTick - partPos;
                PlayPosHighlightX = TickToneToPoint(left, 0).X;
                PlayPosHighlightWidth = (right - left) * TickWidth;
            } else if (!UseModernPlayhead) {
                TickToLineTick((int)(PlayPosX / TickWidth + TickOffset), out int left, out int right);
                PlayPosHighlightX = TickToneToPoint(left, 0).X;
                PlayPosHighlightWidth = (right - left) * TickWidth;
            } else {
                PlayPosHighlightX = PlayPosX - 1;
                PlayPosHighlightWidth = 0;
            }
        }

        private void FocusNote(UNote note) {
            TickOffset = Math.Clamp(note.position + note.duration * 0.5 - ViewportTicks * 0.5, 0, HScrollBarMax);
            TrackOffset = Math.Clamp(ViewConstants.MaxTone - note.tone + 2 - ViewportTracks * 0.5, 0, VScrollBarMax);
        }

        private void ScrollIntoView(UNote note) {
            if (note.position < TickOffset || note.RightBound > TickOffset + ViewportTicks) {
                AutoScroll(TickToneToPoint(note.position, 0).X);
            }
            var toneMargin = 4;
            var noteOffset = ViewConstants.MaxTone - note.tone - 1;
            if (noteOffset < TrackOffset + toneMargin) {
                TrackOffset = Math.Max(noteOffset - toneMargin, 0);
            } else if (noteOffset > TrackOffset + ViewportTracks - toneMargin) {
                TrackOffset = Math.Min(noteOffset + toneMargin - ViewportTracks, VScrollBarMax);
            }
        }

        internal (UNote[], UNote[]) PrepareInsertLyrics() {
            var first = Selection.FirstOrDefault();
            if (Part == null) {
                return (Array.Empty<UNote>(), Array.Empty<UNote>());
            }
            //If no note is selected, InsertLyrics will apply to all notes in the part.
            if (first == null) {
                return (Part.notes.ToArray(), Array.Empty<UNote>());
            }
            List<UNote> notes = new List<UNote>();
            var note = first;
            while (note.Next != null) {
                notes.Add(note);
                note = note.Next;
            }
            notes.Add(note);
            return (notes.ToArray(), Selection.ToArray());
        }

        bool IsExpSupported(string expKey) {
            if (Project == null || Part == null || Project.tracks.Count <= Part.trackNo) {
                return true;
            }
            var track = Project.tracks[Part.trackNo];
            if (track.RendererSettings.Renderer == null) {
                return true;
            }
            if (track.TryGetExpDescriptor(Project, expKey, out var descriptor)) {
                return track.RendererSettings.Renderer.SupportsExpression(descriptor);
            }
            if (expKey == track.VoiceColorExp.abbr) {
                return track.RendererSettings.Renderer.SupportsExpression(track.VoiceColorExp);
            }
            return true;
        }

        public void OnNext(UCommand cmd, bool isUndo) {
            if (cmd is UNotification notif) {
                if (cmd is LoadPartNotification loadPart) {
                    LoadPart(loadPart.part, loadPart.project);
                    double tickOffset = loadPart.tick - loadPart.part.position - Bounds.Width / TickWidth / 2;
                    TickOffset = Math.Clamp(tickOffset, 0, HScrollBarMax);
                    PrimaryKeyNotSupported = !IsExpSupported(PrimaryKey);
                } else if (cmd is LoadProjectNotification) {
                    UnloadPart();
                    LoadPortrait(null, null);
                    PrimaryKeyNotSupported = !IsExpSupported(PrimaryKey);
                } else if (cmd is SelectExpressionNotification selectExp) {
                    SecondaryKey = PrimaryKey;
                    PrimaryKey = selectExp.ExpKey;
                    PrimaryKeyNotSupported = !IsExpSupported(PrimaryKey);
                } else if (cmd is SetPlayPosTickNotification setPlayPosTick) {
                    SetPlayPos(setPlayPosTick.playPosTick, setPlayPosTick.waitingRendering);
                    if (!setPlayPosTick.pause || Preferences.Default.LockStartTime == 1) {
                        MaybeAutoScroll(PlayPosX);
                    }
                } else if (cmd is SetRangeSelectionNotification) {
                    UpdateHighlight();
                    RefreshPlaybackHighlightVisibility();
                } else if (cmd is FocusNoteNotification focusNote) {
                    if (focusNote.part == Part) {
                        FocusNote(focusNote.note);
                        if (Selection.Count <= 1) {
                            SelectNote(focusNote.note);
                        }
                    }
                } else if (cmd is ValidateProjectNotification || cmd is SingersRefreshedNotification) {
                    if (Part != null) {
                        LoadPortrait(Part, Project);
                    }
                    OnPartModified();
                    RebuildPlaybackNoteIndex();
                    MessageBus.Current.SendMessage(new NotesRefreshEvent());
                } else if (cmd is PhonemizedNotification) {
                    OnPartModified();
                    RebuildPlaybackNoteIndex();
                    MessageBus.Current.SendMessage(new NotesRefreshEvent());
                } else if (notif is WaveformReadyNotification) {
                    MessageBus.Current.SendMessage(new WaveformRefreshEvent());
                } else if (notif is PhraseRenderedNotification phraseRendered && phraseRendered.part == Part) {
                    MessageBus.Current.SendMessage(new WaveformRefreshEvent());
                } else if (notif is PartRenderedNotification && notif.part == Part) {
                    MessageBus.Current.SendMessage(new WaveformRefreshEvent());
                } else if (notif is RealCurvesUpdatedNotification && notif.part == Part) {
                    MessageBus.Current.SendMessage(new NotesRefreshEvent());
                }
            } else if (cmd is PartCommand partCommand) {
                if (cmd is ReplacePartCommand replacePart) {
                    if (!isUndo) {
                        LoadPart(replacePart.newPart, replacePart.project);
                    } else {
                        LoadPart(replacePart.part, replacePart.project);
                    }
                }
                if (partCommand.part != Part) {
                    return;
                }
                if (cmd is RemovePartCommand) {
                    if (!isUndo) {
                        UnloadPart();
                    }
                } else if (cmd is AddPartCommand addPart) {
                    if (isUndo && addPart.part == Part) {
                        UnloadPart();
                    }
                } else if (cmd is ResizeVoicePartCommand) {
                    OnPartModified();
                } else if (cmd is MovePartCommand) {
                    OnPartModified();
                } else if (cmd is RenamePartCommand) {
                    LoadWindowTitle(Part, Project);
                }
            } else if (cmd is NoteCommand noteCommand) {
                CleanupSelectedNotes();
                if (noteCommand.Part == Part) {
                    RebuildPitchFollowPath();
                    RebuildPlaybackNoteIndex();
                    MessageBus.Current.SendMessage(new NotesRefreshEvent());

                    if (noteCommand is RemoveNoteCommand && isUndo) {
                        if (Selection.Select(noteCommand.Notes)) {
                            MessageBus.Current.SendMessage(new NotesSelectionEvent(Selection));
                        }
                    }
                }
            } else if (cmd is ExpCommand) {
                MessageBus.Current.SendMessage(new NotesRefreshEvent());
            } else if (cmd is TrackCommand) {
                if (cmd is RenameTrackCommand) {
                    LoadWindowTitle(Part, Project);
                    return;
                } else if (cmd is ChangeTrackColorCommand) {
                    LoadTrackColor(Part, Project);
                    return;
                } else if (cmd is RemoveTrackCommand removeTrack) {
                    if (Part != null && removeTrack.removedParts.Contains(Part)) {
                        UnloadPart();
                    }
                }
                MessageBus.Current.SendMessage(new NotesRefreshEvent());
                if (cmd is TrackChangeSingerCommand trackChangeSinger) {
                    if (Part != null && trackChangeSinger.track.TrackNo == Part.trackNo) {
                        LoadPortrait(Part, Project);
                    }
                }
                PrimaryKeyNotSupported = !IsExpSupported(PrimaryKey);
            } else if (cmd is KeyCommand) {
                UpdateKey();
            }
        }

        private void MaybeAutoScroll(double positionX) {
            if (PlaybackManager.Inst.StartingToPlay || PlayPosWaitingRendering) {
                return;
            }
            var autoScrollPreference = Convert.ToBoolean(Preferences.Default.PlaybackAutoScroll);
            if (autoScrollPreference) {
                AutoScroll(positionX);
            }
        }

        private void AutoScroll(double positionX) {
            double scrollDelta = GetScrollValueDelta(positionX);
            if (Preferences.Default.PlaybackAutoScroll == 1) {
                bool playing = PlaybackManager.Inst.PlayingMaster || PlaybackManager.Inst.StartingToPlay;
                if (!playing) {
                    smoothScrollTargetTickOffset = Math.Clamp(TickOffset + scrollDelta, 0, HScrollBarMax);
                }
            } else {
                smoothScrollTargetTickOffset = null;
                TickOffset = Math.Clamp(TickOffset + scrollDelta, 0, HScrollBarMax);
            }
        }

        /// <summary>Smooth stationary-cursor scroll; called from the piano roll render loop or timer fallback.</summary>
        public void SmoothScrollStep(double deltaMs) {
            if (Part == null || Preferences.Default.PlaybackAutoScroll != 1) {
                return;
            }
            double? desiredTickOffset = GetStationaryCursorDesiredTickOffset();
            if (!desiredTickOffset.HasValue) {
                return;
            }
            deltaMs = Math.Clamp(deltaMs, 0.5, 50);
            double target = Math.Clamp(desiredTickOffset.Value, 0, HScrollBarMax);
            double diff = target - TickOffset;
            _inSmoothScrollStep = true;
            try {
                if (Math.Abs(diff) < SmoothScrollSnapThreshold) {
                    TickOffset = target;
                } else {
                    double alpha = 1 - Math.Exp(-deltaMs / PitchFollowScrollMath.SmoothScrollTimeConstantMs);
                    TickOffset = Math.Clamp(TickOffset + diff * alpha, 0, HScrollBarMax);
                }
            } finally {
                _inSmoothScrollStep = false;
            }
        }

        /// <summary>Fallback when the piano roll is not on a render loop (e.g. minimized).</summary>
        public void SmoothScrollStepFallback() {
            if (pitchFollowRenderingActive) {
                return;
            }
            SmoothScrollStep(PitchFollowScrollMath.ReferenceStepMs);
        }

        double? GetStationaryCursorDesiredTickOffset() {
            if (Bounds.Width <= 0 || TickWidth <= 0) {
                return null;
            }
            if (IsLivePlaybackScrollActive()) {
                stationaryCursorFollowedPlayback = true;
                return ComputeStationaryCursorDesiredTickOffset(GetStationaryCursorPlayPosX());
            }
            var playback = PlaybackManager.Inst;
            if (playback.PlayingMaster || playback.StartingToPlay) {
                return null;
            }
            if (stationaryCursorFollowedPlayback) {
                stationaryCursorFollowedPlayback = false;
                smoothScrollTargetTickOffset = null;
                return null;
            }
            if (!smoothScrollTargetTickOffset.HasValue) {
                return null;
            }
            return smoothScrollTargetTickOffset.Value;
        }

        /// <summary>True only while audio is audibly playing — not during pre-playback render or buffer wait.</summary>
        bool IsLivePlaybackScrollActive() {
            var playback = PlaybackManager.Inst;
            return playback.PlayingMaster
                && playback.OutputActive
                && !playback.StartingToPlay
                && !PlayPosWaitingRendering;
        }

        double GetStationaryCursorPlayPosX() {
            if (Part != null && PlaybackManager.Inst.TryGetSmoothPlayTick(out double absTick)) {
                double localTick = absTick - Part.position;
                return (localTick - TickOffset) * TickWidth;
            }
            return PlayPosX;
        }

        double ComputeStationaryCursorDesiredTickOffset(double positionX) {
            double rightMargin = Preferences.Default.PlayPosMarkerMargin * Bounds.Width;
            if (positionX <= rightMargin && positionX >= 0) {
                return TickOffset;
            }
            double localTick = TickOffset + positionX / TickWidth;
            if (positionX > rightMargin) {
                return Math.Clamp(localTick - rightMargin / TickWidth, 0, HScrollBarMax);
            }
            return Math.Clamp(localTick, 0, HScrollBarMax);
        }

        void RebuildPitchFollowPath() {
            var prefs = Preferences.Default;
            if (!prefs.PlaybackPitchFollowEnabled || Part == null) {
                ClearPitchFollowPath();
                return;
            }
            pitchFollowPath.Build(
                Part,
                ViewportTracks,
                VScrollBarMax,
                prefs.PlaybackPitchFollowSemitoneThreshold,
                prefs.PlaybackPitchFollowVerticalPosition,
                Project.resolution);
            RefreshPitchFollowPathPreview();
        }

        void ClearPitchFollowPath() {
            pitchFollowPath.Build(null, 0, 0, 0, 0, Project.resolution);
            pitchFollowPathSamples.Clear();
            this.RaisePropertyChanged(nameof(PitchFollowPathSamples));
            this.RaisePropertyChanged(nameof(PitchFollowPathIsBuilt));
            MessageBus.Current.SendMessage(new PitchFollowPathPreviewChangedEvent());
        }

        void RefreshPitchFollowPathPreview() {
            pitchFollowPathSamples.Clear();
            if (Preferences.Default.PlaybackPitchFollowEnabled && pitchFollowPath.IsBuilt && Part != null) {
                pitchFollowPath.SampleSmoothedPoints(
                    Preferences.Default.PlaybackPitchFollowFrameSmoothing,
                    Project.resolution,
                    pitchFollowPathSamples);
            }
            this.RaisePropertyChanged(nameof(PitchFollowPathSamples));
            this.RaisePropertyChanged(nameof(PitchFollowPathIsBuilt));
            MessageBus.Current.SendMessage(new PitchFollowPathPreviewChangedEvent());
        }

        /// <summary>Whether the preview overlay is drawn (playback still uses smoothed samples when follow is enabled).</summary>
        static bool IsPitchFollowPathPreviewVisible() {
            var prefs = Preferences.Default;
            return prefs.PlaybackPitchFollowEnabled && prefs.PlaybackPitchFollowShowPath;
        }

        public double EvaluatePitchFollowPath(double localTick) {
            if (pitchFollowPathSamples.Count > 0) {
                return pitchFollowPath.EvaluateSmoothedAtTick(pitchFollowPathSamples, localTick);
            }
            return pitchFollowPath.Evaluate(localTick);
        }

        public double GetPitchFollowCameraOffset(double localTick, bool playing) {
            if (playing && Preferences.Default.PlaybackPitchFollowEnabled
                && !pitchFollowUserOverride && pitchFollowPath.IsBuilt) {
                return TrackOffset;
            }
            return EvaluatePitchFollowPath(localTick);
        }

        public bool PianoRollRenderingActive => pitchFollowRenderingActive;

        public void NotifyPitchFollowRendering(bool active) {
            pitchFollowRenderingActive = active;
        }

        public void PitchFollowAnimationStep(double deltaMs) {
            bool playing = IsLivePlaybackScrollActive();
            if (playing && !pitchFollowWasPlaying) {
                pitchFollowUserOverride = false;
                pitchFollowLastStepUtc = DateTime.UtcNow;
                RebuildPitchFollowPath();
            }
            pitchFollowWasPlaying = playing;

            if (!playing || !Preferences.Default.PlaybackPitchFollowEnabled || Part == null
                || pitchFollowUserOverride || !pitchFollowPath.IsBuilt) {
                return;
            }

            int localTick = DocManager.Inst.playPosTick - Part.position;
            double target = EvaluatePitchFollowPath(localTick);
            _inPitchFollowScrollStep = true;
            try {
                TrackOffset = Math.Clamp(target, 0, VScrollBarMax);
            } finally {
                _inPitchFollowScrollStep = false;
            }
            pitchFollowLastStepUtc = DateTime.UtcNow;
        }

        /// <summary>Fallback when the piano roll is not on a render loop (e.g. minimized).</summary>
        public void PitchFollowAnimationStepFallback() {
            if (pitchFollowRenderingActive) {
                return;
            }
            double deltaMs = PitchFollowScrollMath.ReferenceStepMs;
            if (pitchFollowLastStepUtc != default) {
                deltaMs = Math.Clamp((DateTime.UtcNow - pitchFollowLastStepUtc).TotalMilliseconds, 0.5, 40);
            }
            PitchFollowAnimationStep(deltaMs);
        }

        private double GetScrollValueDelta(double positionX) {
            var pageScroll = Preferences.Default.PlaybackAutoScroll == 2;
            if (pageScroll) {
                return GetPageScrollScrollValueDelta(positionX);
            }
            return GetStationaryCursorScrollValueDelta(positionX);
        }

        private double GetStationaryCursorScrollValueDelta(double positionX) {
            double rightMargin = Preferences.Default.PlayPosMarkerMargin * Bounds.Width;
            if (positionX > rightMargin) {
                return (positionX - rightMargin) * playPosXToTickOffset;
            } else if (positionX < 0) {
                return positionX * playPosXToTickOffset;
            }
            return 0;
        }

        private double GetPageScrollScrollValueDelta(double positionX) {
            double leftMargin = (1 - Preferences.Default.PlayPosMarkerMargin) * Bounds.Width;
            if (positionX > Bounds.Width) {
                return (Bounds.Width - leftMargin) * playPosXToTickOffset;
            } else if (positionX < 0) {
                return (positionX - leftMargin) * playPosXToTickOffset;
            }
            return 0;
        }

        void ApplyLivePitchModeFromPreferences() {
            livePitchSyncing = true;
            var mode = (LivePitchMode)Preferences.Default.RealTimePitchMode;
            LivePitchNormal = mode == LivePitchMode.Normal;
            LivePitchSuperFast = mode == LivePitchMode.SuperFast;
            livePitchSyncing = false;
        }

        void SetLivePitchMode(LivePitchMode mode) {
            livePitchSyncing = true;
            LivePitchNormal = mode == LivePitchMode.Normal;
            LivePitchSuperFast = mode == LivePitchMode.SuperFast;
            Preferences.Default.RealTimePitchMode = (int)mode;
            Preferences.Save();
            livePitchSyncing = false;
        }
    }
}
