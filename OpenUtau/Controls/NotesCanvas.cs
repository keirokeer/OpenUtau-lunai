using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using OpenUtau.App;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using OpenUtau.Core.DiffSinger;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using OpenUtau.Api;
using OpenUtau.Core.Util;
using ReactiveUI;

namespace OpenUtau.App.Controls {
    class NotesCanvas : Control {
        /// <summary>Recognized 2–4 letter codes in phonemizer tags (e.g. "EN VCCV") or as standalone tokens.</summary>
        static readonly HashSet<string> KnownLangCodes = new(StringComparer.OrdinalIgnoreCase) {
            "EN", "JA", "ZH", "KO", "ES", "IT", "FR", "DE", "PT", "RU", "UA", "UK",
            "TH", "FIL", "PL", "TR", "VI", "VIE", "MS", "ID", "NL", "SV", "NO", "DA",
            "FI", "CS", "SK", "HU", "RO", "EL", "HE", "AR", "HI", "BN", "TL", "MNL",
        };

        /// <summary>Maps phonemizer display tags (DiffSinger second attr, etc.) to short codes when language attr is engine name.</summary>
        static readonly Dictionary<string, string> PhonemizerTagToLangCode = BuildPhonemizerTagToLangCode();

        static Dictionary<string, string> BuildPhonemizerTagToLangCode() {
            void reg(Dictionary<string, string> d, string key, string code) {
                var k = key.Replace(" ", "").ToLowerInvariant();
                d[k] = code;
            }
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            reg(d, "English", "EN");
            reg(d, "English+", "EN");
            reg(d, "Japanese", "JA");
            reg(d, "Chinese", "ZH");
            reg(d, "Jyutping", "ZH");
            reg(d, "Korean", "KO");
            reg(d, "Korean+", "KO");
            reg(d, "Spanish", "ES");
            reg(d, "Italian", "IT");
            reg(d, "French", "FR");
            reg(d, "German", "DE");
            reg(d, "Portuguese", "PT");
            reg(d, "Portuguese BRAPA", "PT");
            reg(d, "German Marzipan", "DE");
            reg(d, "French Millefeuille", "FR");
            reg(d, "Russian", "RU");
            reg(d, "Ukrainian", "UA");
            reg(d, "Thai", "TH");
            reg(d, "Filipino", "FIL");
            reg(d, "Polish", "PL");
            reg(d, "Turkish", "TR");
            reg(d, "Vietnamese", "VIE");
            reg(d, "Default", "");
            reg(d, "Rhythmizer", "DS");
            return d;
        }

        /// <summary>Short language code for note badge (EN, JA, …), independent of engine-based sorting in <c>language</c> attr.</summary>
        static string GetPhonemizerLanguageBadgeCode(PhonemizerFactory? factory) {
            if (factory == null) {
                return string.Empty;
            }
            var tag = factory.tag?.Trim() ?? "";
            if (!string.IsNullOrEmpty(tag)) {
                var parts = tag.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                var first = parts.Length > 0 ? parts[0] : "";
                if (first.Length >= 2 && first.Length <= 5 && first.All(c => char.IsLetter(c) || c == '-')) {
                    var up = first.ToUpperInvariant();
                    if (up is not ("UTAU" or "DIFFSINGER" or "DEFAULT") && KnownLangCodes.Contains(up)) {
                        return up;
                    }
                }
                var compact = new string(tag.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToLowerInvariant();
                if (PhonemizerTagToLangCode.TryGetValue(compact, out var fromMap) && !string.IsNullOrEmpty(fromMap)) {
                    return fromMap;
                }
                foreach (Match m in Regex.Matches(tag.ToUpperInvariant(), @"\b[A-Z]{2,4}\b")) {
                    if (KnownLangCodes.Contains(m.Value)) {
                        return m.Value;
                    }
                }
            }
            var attr = factory.type.GetCustomAttribute<PhonemizerAttribute>();
            var attrTag = attr?.Tag?.Trim() ?? "";
            if (!string.IsNullOrEmpty(attrTag) && attrTag != tag) {
                var compactAttr = new string(attrTag.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToLowerInvariant();
                if (PhonemizerTagToLangCode.TryGetValue(compactAttr, out var fromAttr) && !string.IsNullOrEmpty(fromAttr)) {
                    return fromAttr;
                }
            }
            var lang = factory.language?.Trim() ?? "";
            if (lang.Length >= 2 && lang.Length <= 5 && lang.All(char.IsLetter)
                && !lang.Equals("UTAU", StringComparison.OrdinalIgnoreCase)
                && !lang.Equals("DiffSinger", StringComparison.OrdinalIgnoreCase)
                && KnownLangCodes.Contains(lang.ToUpperInvariant())) {
                return lang.ToUpperInvariant();
            }
            return string.Empty;
        }

        public static readonly DirectProperty<NotesCanvas, double> TickWidthProperty =
            AvaloniaProperty.RegisterDirect<NotesCanvas, double>(
                nameof(TickWidth),
                o => o.TickWidth,
                (o, v) => o.TickWidth = v);
        public static readonly DirectProperty<NotesCanvas, double> TrackHeightProperty =
            AvaloniaProperty.RegisterDirect<NotesCanvas, double>(
                nameof(TrackHeight),
                o => o.TrackHeight,
                (o, v) => o.TrackHeight = v);
        public static readonly DirectProperty<NotesCanvas, double> TickOffsetProperty =
            AvaloniaProperty.RegisterDirect<NotesCanvas, double>(
                nameof(TickOffset),
                o => o.TickOffset,
                (o, v) => o.TickOffset = v);
        public static readonly DirectProperty<NotesCanvas, double> TrackOffsetProperty =
            AvaloniaProperty.RegisterDirect<NotesCanvas, double>(
                nameof(TrackOffset),
                o => o.TrackOffset,
                (o, v) => o.TrackOffset = v);
        public static readonly DirectProperty<NotesCanvas, UVoicePart?> PartProperty =
            AvaloniaProperty.RegisterDirect<NotesCanvas, UVoicePart?>(
                nameof(Part),
                o => o.Part,
                (o, v) => o.Part = v);
        public static readonly DirectProperty<NotesCanvas, bool> ShowPitchProperty =
            AvaloniaProperty.RegisterDirect<NotesCanvas, bool>(
                nameof(ShowPitch),
                o => o.ShowPitch,
                (o, v) => o.ShowPitch = v);
        public static readonly DirectProperty<NotesCanvas, bool> ShowFinalPitchProperty =
            AvaloniaProperty.RegisterDirect<NotesCanvas, bool>(
                nameof(ShowFinalPitch),
                o => o.ShowFinalPitch,
                (o, v) => o.ShowFinalPitch = v);
        public static readonly DirectProperty<NotesCanvas, bool> ShowVibratoProperty =
            AvaloniaProperty.RegisterDirect<NotesCanvas, bool>(
                nameof(ShowVibrato),
                o => o.ShowVibrato,
                (o, v) => o.ShowVibrato = v);
        public static readonly DirectProperty<NotesCanvas, bool> ShowPhonemizerTagsProperty =
            AvaloniaProperty.RegisterDirect<NotesCanvas, bool>(
                nameof(ShowPhonemizerTags),
                o => o.ShowPhonemizerTags,
                (o, v) => o.ShowPhonemizerTags = v);
        public static readonly DirectProperty<NotesCanvas, bool> ShowPlaybackNoteHighlightProperty =
            AvaloniaProperty.RegisterDirect<NotesCanvas, bool>(
                nameof(ShowPlaybackNoteHighlight),
                o => o.ShowPlaybackNoteHighlight,
                (o, v) => o.ShowPlaybackNoteHighlight = v);
        public static readonly DirectProperty<NotesCanvas, bool> ShowPlaybackNoteBounceProperty =
            AvaloniaProperty.RegisterDirect<NotesCanvas, bool>(
                nameof(ShowPlaybackNoteBounce),
                o => o.ShowPlaybackNoteBounce,
                (o, v) => o.ShowPlaybackNoteBounce = v);
        public static readonly DirectProperty<NotesCanvas, int> PlayPosTickProperty =
            AvaloniaProperty.RegisterDirect<NotesCanvas, int>(
                nameof(PlayPosTick),
                o => o.PlayPosTick,
                (o, v) => o.PlayPosTick = v);
        public static readonly StyledProperty<bool> PitchFocusDimProperty =
            AvaloniaProperty.Register<NotesCanvas, bool>(nameof(PitchFocusDim));

        const double PitchFocusNoteOpacity = 0.2;
        const double PitchFocusFinalPitchThickness = 1.5;
        const double PitchFocusPhonemeGuideOpacity = 0.2;
        const double PitchFocusCenterLineOpacity = 0.5;

        public double TickWidth {
            get => tickWidth;
            private set => SetAndRaise(TickWidthProperty, ref tickWidth, value);
        }
        public double TrackHeight {
            get => trackHeight;
            private set => SetAndRaise(TrackHeightProperty, ref trackHeight, value);
        }
        public double TickOffset {
            get => tickOffset;
            private set => SetAndRaise(TickOffsetProperty, ref tickOffset, value);
        }
        public double TrackOffset {
            get => trackOffset;
            private set => SetAndRaise(TrackOffsetProperty, ref trackOffset, value);
        }
        public UVoicePart? Part {
            get => part;
            set => SetAndRaise(PartProperty, ref part, value);
        }
        public bool ShowPitch {
            get => showPitch;
            private set => SetAndRaise(ShowPitchProperty, ref showPitch, value);
        }
        public bool ShowFinalPitch {
            get => showFinalPitch;
            private set => SetAndRaise(ShowFinalPitchProperty, ref showFinalPitch, value);
        }
        public bool ShowVibrato {
            get => showVibrato;
            private set => SetAndRaise(ShowVibratoProperty, ref showVibrato, value);
        }
        public bool ShowPhonemizerTags {
            get => showPhonemizerTags;
            private set => SetAndRaise(ShowPhonemizerTagsProperty, ref showPhonemizerTags, value);
        }
        public bool ShowPlaybackNoteHighlight {
            get => showPlaybackNoteHighlight;
            private set => SetAndRaise(ShowPlaybackNoteHighlightProperty, ref showPlaybackNoteHighlight, value);
        }
        public bool ShowPlaybackNoteBounce {
            get => showPlaybackNoteBounce;
            private set => SetAndRaise(ShowPlaybackNoteBounceProperty, ref showPlaybackNoteBounce, value);
        }
        public int PlayPosTick {
            get => playPosTick;
            private set => SetAndRaise(PlayPosTickProperty, ref playPosTick, value);
        }
        public bool PitchFocusDim {
            get => GetValue(PitchFocusDimProperty);
            set => SetValue(PitchFocusDimProperty, value);
        }

        private double tickWidth;
        private double trackHeight;
        private double tickOffset;
        private double trackOffset;
        private UVoicePart? part;
        private bool showPitch = true;
        private bool showFinalPitch = true;
        private bool showVibrato = true;
        private bool showPhonemizerTags = true;
        private bool showPlaybackNoteHighlight;
        private bool showPlaybackNoteBounce;
        private int playPosTick = int.MinValue;

        private UNote? activePlaybackNote;
        private UNote? fadingPlaybackNote;
        private float activeHighlight;
        private float fadingHighlight;
        private float activeBounceElapsed;
        private DateTime highlightLastFrame = DateTime.UtcNow;
        private readonly DispatcherTimer highlightTimer;
        private readonly Dictionary<(Color from, Color to, byte amount), IBrush> highlightBrushes = new();
        private bool playbackSeekPending = true;
        private bool renderPassActive;
        private bool invalidatePending;

        private const double HoverGlowDuration = 0.12;
        private const float PlaybackHighlightFadeInPerSecond = 8.0f;
        private const float PlaybackHighlightFadeOutPerSecond = 6.2f;
        private const float PlaybackNoteBounceDuration = 0.25f;
        private const double PlaybackNoteBounceHeight = 12.0;
        private UNote? hoverNote;
        private UNote? fadingHoverNote;
        private float hoverGlow;
        private float hoverFadeGlow;
        private DateTime hoverLastFrame = DateTime.UtcNow;
        private readonly DispatcherTimer hoverTimer;
        private Point lastPointerPos;
        private readonly Dictionary<(Color color, byte alpha, int thickness), Pen> glowPens = new();

        private PolylineGeometry polylineGeometry = new PolylineGeometry();
        private Points points = new Points();

        private HashSet<UNote> selectedNotes = new HashSet<UNote>();
        private Geometry pointGeometry;

        private bool showGhostNotes = true;
        private List<UPart> otherPartsInView = new List<UPart>();

        public NotesCanvas() {
            ClipToBounds = true;
            pointGeometry = new EllipseGeometry(new Rect(-2.5, -2.5, 5, 5));

            highlightTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000.0 / 30.0) };
            highlightTimer.Tick += (_, _) => UpdatePlaybackHighlight(false);
            hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000.0 / 60.0) };
            hoverTimer.Tick += (_, _) => UpdateHoverGlow();

            MessageBus.Current.Listen<NotesRefreshEvent>()
                .Subscribe(_ => InvalidateVisual());
            MessageBus.Current.Listen<NotesSelectionEvent>()
                .Subscribe(e => {
                    selectedNotes.Clear();
                    selectedNotes.UnionWith(e.selectedNotes);
                    selectedNotes.UnionWith(e.tempSelectedNotes);
                    InvalidateVisual();
                });
            MessageBus.Current.Listen<PartRefreshEvent>()
                .Subscribe(_ => RefreshGhostNotes());
            MessageBus.Current.Listen<ThemeChangedEvent>()
                .Subscribe(_ => InvalidateVisual());
            this.WhenAnyValue(x => x.Part)
                .Subscribe(_ => {
                    RefreshGhostNotes();
                    hoverNote = null;
                    fadingHoverNote = null;
                    hoverGlow = 0;
                    hoverFadeGlow = 0;
                    hoverTimer.Stop();
                });
        }

        void RefreshGhostNotes() {
            showGhostNotes = Convert.ToBoolean(Preferences.Default.ShowGhostNotes);
            if (Part == null || !showGhostNotes) {
                return;
            }
            otherPartsInView = DocManager.Inst.Project.parts
                .Where(other => other.trackNo != Part.trackNo &&
                    other.position < Part.End &&
                    Part.position < other.End)
                .ToList();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
            base.OnPropertyChanged(change);
            if (change.Property == PlayPosTickProperty) {
                if (!ShowPlaybackNoteHighlight && !ShowPlaybackNoteBounce) {
                    return;
                }
                playbackSeekPending = true;
                UpdatePlaybackHighlight(true);
                return;
            }
            if (change.Property == ShowPlaybackNoteHighlightProperty ||
                change.Property == ShowPlaybackNoteBounceProperty) {
                playbackSeekPending = true;
                UpdatePlaybackHighlight(false);
                InvalidateVisual();
                return;
            }
            InvalidateVisual();
        }

        protected override void OnPointerMoved(PointerEventArgs e) {
            base.OnPointerMoved(e);
            lastPointerPos = e.GetPosition(this);
            UpdateHoveredNote();
        }

        protected override void OnPointerExited(PointerEventArgs e) {
            base.OnPointerExited(e);
            SetHoveredNote(null);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e) {
            base.OnPointerPressed(e);
            SetHoveredNote(null);
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e) {
            base.OnPointerReleased(e);
            UpdateHoveredNote();
        }

        void UpdateHoveredNote() {
            if (!Preferences.Default.NoteHoverGlow || Part == null) {
                SetHoveredNote(null);
                return;
            }
            var viewModel = ((PianoRollViewModel?)DataContext)?.NotesViewModel;
            if (viewModel == null) {
                SetHoveredNote(null);
                return;
            }
            double tick = viewModel.PointToTick(lastPointerPos);
            int tone = viewModel.PointToTone(lastPointerPos);
            UNote? found = null;
            foreach (var note in Part.notes) {
                if (note.position > tick && note.LeftBound > tick) {
                    break;
                }
                if (note.LeftBound <= tick && tick < note.RightBound && note.AdjustedTone == tone) {
                    found = note;
                    break;
                }
            }
            SetHoveredNote(found);
        }

        void SetHoveredNote(UNote? note) {
            if (!Preferences.Default.NoteHoverGlow) {
                note = null;
            }
            if (ReferenceEquals(note, hoverNote)) {
                return;
            }
            if (hoverNote != null && hoverGlow > 0.001f) {
                fadingHoverNote = hoverNote;
                hoverFadeGlow = hoverGlow;
            } else if (hoverNote != null) {
                fadingHoverNote = null;
                hoverFadeGlow = 0;
            }
            hoverNote = note;
            hoverGlow = 0;
            hoverLastFrame = DateTime.UtcNow;
            hoverTimer.Start();
        }

        void UpdateHoverGlow() {
            var now = DateTime.UtcNow;
            float dt = (float)Math.Clamp((now - hoverLastFrame).TotalSeconds, 0, 0.1);
            hoverLastFrame = now;
            float step = dt / (float)HoverGlowDuration;
            bool changed = false;
            float newActive = MoveTowards(hoverGlow, hoverNote == null ? 0f : 1f, step);
            if (newActive != hoverGlow) {
                hoverGlow = newActive;
                changed = true;
            }
            float newFade = MoveTowards(hoverFadeGlow, 0f, step);
            if (newFade != hoverFadeGlow) {
                hoverFadeGlow = newFade;
                changed = true;
            }
            if (hoverFadeGlow <= 0.001f) {
                fadingHoverNote = null;
                hoverFadeGlow = 0;
            }
            bool settled = (hoverNote == null ? hoverGlow == 0f : hoverGlow == 1f) && fadingHoverNote == null;
            if (!changed && settled) {
                hoverTimer.Stop();
                return;
            }
            InvalidateVisual();
        }

        float GetHoverGlow(UNote note) {
            if (note == hoverNote) {
                return hoverGlow;
            }
            if (note == fadingHoverNote) {
                return hoverFadeGlow;
            }
            return 0f;
        }

        Pen GetGlowPen(Color color, byte alpha, int thickness) {
            var key = (color, alpha, thickness);
            if (!glowPens.TryGetValue(key, out var pen)) {
                pen = new Pen(new ImmutableSolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B)), thickness) {
                    LineJoin = PenLineJoin.Round,
                };
                glowPens[key] = pen;
            }
            return pen;
        }

        void DrawHoverGlow(DrawingContext context, Point leftTop, Size size, double radius, float glow) {
            ISolidColorBrush? solid = ThemeManager.NoteBrush as ISolidColorBrush
                ?? ThemeManager.AccentBrush1 as ISolidColorBrush;
            if (glow <= 0.01f || solid == null) {
                return;
            }
            byte alpha = (byte)Math.Clamp((int)Math.Round(glow * 100), 0, 255);
            context.DrawRectangle(null, GetGlowPen(solid.Color, alpha, 2),
                Inflate(leftTop, size, 1), radius + 1, radius + 1);
            context.DrawRectangle(null, GetGlowPen(solid.Color, (byte)(alpha * 2 / 5), 3),
                Inflate(leftTop, size, 2.5), radius + 2.5, radius + 2.5);
        }

        static Rect Inflate(Point leftTop, Size size, double d) =>
            new Rect(leftTop.X - d, leftTop.Y - d, size.Width + d * 2, size.Height + d * 2);

        private void UpdatePlaybackHighlight(bool seek) {
            var now = DateTime.UtcNow;
            float dt = (float)Math.Clamp((now - highlightLastFrame).TotalSeconds, 0, 0.1);
            highlightLastFrame = now;
            var target = ((!ShowPlaybackNoteHighlight && !ShowPlaybackNoteBounce) || !PlaybackManager.Inst.PlayingMaster)
                ? null
                : seek || activePlaybackNote == null ? FindPlaybackNote() : activePlaybackNote;
            bool changed = false;
            if (target != activePlaybackNote) {
                if (activePlaybackNote != null && activeHighlight > 0.001f) {
                    fadingPlaybackNote = activePlaybackNote;
                    fadingHighlight = activeHighlight;
                }
                activePlaybackNote = target;
                activeHighlight = 0;
                activeBounceElapsed = 0;
                changed = true;
            }
            float newActive = MoveTowards(activeHighlight, !ShowPlaybackNoteHighlight || activePlaybackNote == null ? 0 : 1,
                PlaybackHighlightFadeInPerSecond * dt);
            if (newActive != activeHighlight) {
                activeHighlight = newActive;
                changed = true;
            }
            float newFading = MoveTowards(fadingHighlight, 0,
                PlaybackHighlightFadeOutPerSecond * dt);
            if (newFading != fadingHighlight) {
                fadingHighlight = newFading;
                changed = true;
            }
            if (fadingHighlight <= 0.001f) {
                fadingPlaybackNote = null;
                fadingHighlight = 0;
            }
            bool bouncing = ShowPlaybackNoteBounce && activePlaybackNote != null &&
                activeBounceElapsed < PlaybackNoteBounceDuration;
            if (bouncing) {
                activeBounceElapsed += dt;
                changed = true;
            }
            bool needed = activeHighlight > 0.001f || fadingHighlight > 0.001f || bouncing;
            if (needed) {
                if (!highlightTimer.IsEnabled) {
                    highlightTimer.Start();
                }
            } else if (highlightTimer.IsEnabled) {
                highlightTimer.Stop();
            }
            if (changed) {
                if (renderPassActive) {
                    if (!invalidatePending) {
                        invalidatePending = true;
                        Dispatcher.UIThread.Post(() => {
                            invalidatePending = false;
                            InvalidateVisual();
                        }, DispatcherPriority.Background);
                    }
                } else {
                    InvalidateVisual();
                }
            }
        }

        private UNote? FindPlaybackNote() {
            var viewModel = ((PianoRollViewModel?)DataContext)?.NotesViewModel;
            return viewModel?.FindVoiceNoteAtTick(PlayPosTick);
        }

        private Vector GetPlaybackBounceOffset(UNote note) {
            if (!ShowPlaybackNoteBounce || note != activePlaybackNote || !PlaybackManager.Inst.PlayingMaster) {
                return default;
            }
            double progress = Math.Clamp(activeBounceElapsed / PlaybackNoteBounceDuration, 0, 1);
            double height = Math.Min(PlaybackNoteBounceHeight, TrackHeight * 0.4);
            return new Vector(0, -Math.Sin(progress * Math.PI) * height);
        }

        private IBrush BlendBrush(IBrush from, IBrush to, float amount) {
            if (amount <= 0.001f || from is not ISolidColorBrush fromSolid || to is not ISolidColorBrush toSolid) return from;
            byte quantizedAmount = (byte)Math.Clamp((int)Math.Round(amount * 255), 0, 255);
            var key = (fromSolid.Color, toSolid.Color, quantizedAmount);
            if (!highlightBrushes.TryGetValue(key, out var brush)) {
                float t = quantizedAmount / 255f;
                var a = fromSolid.Color;
                var b = toSolid.Color;
                brush = new SolidColorBrush(Color.FromArgb(
                    (byte)(a.A + (b.A - a.A) * t),
                    (byte)(a.R + (b.R - a.R) * t),
                    (byte)(a.G + (b.G - a.G) * t),
                    (byte)(a.B + (b.B - a.B) * t)));
                highlightBrushes[key] = brush;
            }
            return brush;
        }

        private static float MoveTowards(float value, float target, float delta) =>
            Math.Abs(target - value) <= delta ? target : value + Math.Sign(target - value) * delta;

        public override void Render(DrawingContext context) {
            base.Render(context);
            if (Part == null) {
                return;
            }
            var viewModel = ((PianoRollViewModel?)DataContext)?.NotesViewModel;
            if (viewModel == null) {
                return;
            }
            renderPassActive = true;
            try {
                double leftTick = TickOffset - 480;
                double rightTick = TickOffset + Bounds.Width / TickWidth + 480;
                bool hidePitch = viewModel.TickWidth <= ViewConstants.PianoRollTickWidthShowDetails * 0.5;
                double noteCornerRadius = Math.Clamp(Preferences.Default.NoteCornerRadius, 0, 12);
                var pianoRollVm = DataContext as PianoRollViewModel;
                bool pitchFocusDim = pianoRollVm?.PitchFocusDim ?? PitchFocusDim;
                bool seek = playbackSeekPending;
                playbackSeekPending = false;
                UpdatePlaybackHighlight(seek);

                DrawBackgroundForHitTest(context);

                void RenderGhostNotes() {
                    if (!showGhostNotes) {
                        return;
                    }
                    foreach (UPart otherPart in otherPartsInView) {
                        if (otherPart is not UVoicePart otherVoicePart) {
                            continue;
                        }
                        var xOffset = otherVoicePart.position - Part.position;
                        var brush = ThemeManager.NeutralAccentBrushSemi;
                        if (otherVoicePart.trackNo >= 0) {
                            var track = DocManager.Inst.Project.tracks[otherVoicePart.trackNo];
                            brush = ThemeManager.GetTrackColor(track.TrackColor).AccentColorLightSemi;
                        }
                        foreach (var note in otherVoicePart.notes) {
                            if (note.LeftBound + xOffset >= rightTick || note.RightBound + xOffset <= leftTick) {
                                continue;
                            }
                            RenderGhostNote(note, viewModel, context, xOffset, brush);
                        }
                    }
                }

                void RenderNoteBodies() {
                    foreach (var note in Part.notes) {
                        if (note.LeftBound >= rightTick || note.RightBound <= leftTick) {
                            continue;
                        }
                        RenderNoteBody(note, viewModel, context, noteCornerRadius);
                    }
                }

                if (pitchFocusDim) {
                    using (context.PushOpacity(PitchFocusNoteOpacity)) {
                        RenderGhostNotes();
                        RenderNoteBodies();
                        if (ShowPitch && !hidePitch) {
                            foreach (var note in Part.notes) {
                                if (note.LeftBound >= rightTick || note.RightBound <= leftTick) {
                                    continue;
                                }
                                RenderPitchBend(note, viewModel, context);
                            }
                        }
                    }
                    RenderDiffSingerPhraseBoundaries(leftTick, rightTick, viewModel, context);
                    if (!hidePitch) {
                        RenderPhonemeTimingGuides(leftTick, rightTick, viewModel, context);
                        using (context.PushOpacity(PitchFocusCenterLineOpacity)) {
                            foreach (var note in Part.notes) {
                                if (note.LeftBound >= rightTick || note.RightBound <= leftTick) {
                                    continue;
                                }
                                RenderNotePitchCenterLine(note, viewModel, context);
                            }
                        }
                        if (ShowFinalPitch) {
                            RenderFinalPitch(leftTick, rightTick, viewModel, context, PitchFocusFinalPitchThickness);
                        }
                        RenderAcousticF0PatchPreview(leftTick, rightTick, viewModel, context);
                    }
                } else {
                    RenderGhostNotes();
                    RenderNoteBodies();
                    RenderDiffSingerPhraseBoundaries(leftTick, rightTick, viewModel, context);
                    if (ShowFinalPitch && !hidePitch) {
                        RenderFinalPitch(leftTick, rightTick, viewModel, context);
                    }
                    RenderAcousticF0PatchPreview(leftTick, rightTick, viewModel, context);
                    foreach (var note in Part.notes) {
                        if (note.LeftBound >= rightTick || note.RightBound <= leftTick) {
                            continue;
                        }
                        if (ShowPitch && !hidePitch) {
                            RenderPitchBend(note, viewModel, context);
                        }
                        if ((ShowPitch || ShowVibrato) && !hidePitch) {
                            RenderVibrato(note, viewModel, context);
                        }
                        if (ShowVibrato && !note.Error && !hidePitch) {
                            RenderVibratoToggle(note, viewModel, context);
                            RenderVibratoControl(note, viewModel, context);
                        }
                    }
                }
            } finally {
                renderPassActive = false;
            }
        }

        private void DrawBackgroundForHitTest(DrawingContext context) {
            context.DrawRectangle(Brushes.Transparent, null, Bounds.WithX(0).WithY(0));
        }

        private void RenderNoteBody(UNote note, NotesViewModel viewModel, DrawingContext context, double cornerRadius) {
            List<string> triggerItems = new List<string> { "br", "-", "AP", "SP", "ap", "sp", "pau", "sil", "R", "cl", "vf", "hh", "exh", "'", "・", "息", ".sil", ".br", ".cl", ".hh" };
            Point leftTop = viewModel.TickToneToPoint(note.position, note.AdjustedTone);
            leftTop = leftTop.WithX(leftTop.X + 1).WithY(Math.Round(leftTop.Y));
            Size size = viewModel.TickToneToSize(note.duration, 1);
            size = size.WithWidth(size.Width - 1).WithHeight(Math.Floor(size.Height));
            leftTop += GetPlaybackBounceOffset(note);
            Point rightBottom = new Point(leftTop.X + size.Width, leftTop.Y + size.Height);
            bool hasError = note.Error;
            if (!hasError && Part != null && Part.phonemes != null) {
                int phonemeCount = 0;
                foreach (var p in Part.phonemes) {
                    if (p.Parent == note) {
                        phonemeCount++;
                        if (p.Error) {
                            hasError = true;
                            break;
                        }
                    }
                }
                if (!hasError && Part.PhonemesUpToDate && phonemeCount == 0 && !note.lyric.StartsWith("+") && !note.lyric.StartsWith("-")) {
                    hasError = true;
                }
            }
            bool showBorder = Preferences.Default.ShowNoteBorder;
            if (triggerItems.Contains(note.lyric)) {
                var brush1 = selectedNotes.Contains(note)
                    ? ThemeManager.NoteBrushPressed
                    : ThemeManager.NoteEmptyBrush;
                if (!selectedNotes.Contains(note)) {
                    float highlight = ShowPlaybackNoteHighlight
                        ? (note == activePlaybackNote ? activeHighlight : note == fadingPlaybackNote ? fadingHighlight : 0)
                        : 0;
                    if (highlight > 0.001f) {
                        brush1 = BlendBrush(brush1, ThemeManager.NoteBrushPressed, highlight);
                    }
                }
                IPen? pen = showBorder ? ThemeManager.NoteBorderPen : null;
                context.DrawRectangle(brush1, pen, new Rect(leftTop, rightBottom), cornerRadius, cornerRadius);
                if (Preferences.Default.NoteHoverGlow) {
                    DrawHoverGlow(context, leftTop, size, cornerRadius, GetHoverGlow(note));
                }
            } else {
                var brush = hasError
                    ? (selectedNotes.Contains(note) ? ThemeManager.NoteBrushPressed : ThemeManager.NoteEmptyBrush)
                    : (selectedNotes.Contains(note) ? ThemeManager.NoteBrushPressed : ThemeManager.NoteBrush);
                if (!selectedNotes.Contains(note)) {
                    float highlight = ShowPlaybackNoteHighlight
                        ? (note == activePlaybackNote ? activeHighlight : note == fadingPlaybackNote ? fadingHighlight : 0)
                        : 0;
                    if (highlight > 0.001f) {
                        brush = BlendBrush(brush, hasError ? ThemeManager.NoteEmptyBrush : ThemeManager.NoteBrushPressed, highlight);
                    }
                }
                IPen? borderPen = !showBorder ? null
                    : selectedNotes.Contains(note)
                    ? ThemeManager.NoteBorderPenPressed
                    : ThemeManager.NoteBorderPen;
                context.DrawRectangle(brush, borderPen, new Rect(leftTop, rightBottom), cornerRadius, cornerRadius);
                if (Preferences.Default.NoteHoverGlow) {
                    DrawHoverGlow(context, leftTop, size, cornerRadius, GetHoverGlow(note));
                }
            }
            if (TrackHeight < 10 || note.lyric.Length == 0) {
                return;
            }
            // grey out the Phonemizer Transition Badges
            if (ShowPhonemizerTags && TrackHeight >= 20) {
                string currentOver = note.PhonemizerOverride ?? "";
                bool isCurrentDefault = string.IsNullOrEmpty(currentOver) || currentOver.Equals("Default", StringComparison.OrdinalIgnoreCase);
                string currentPh = isCurrentDefault ? "Default" : currentOver;
                string prevPh = "Default"; 
                if (note.Prev != null) {
                    string prevOver = note.Prev.PhonemizerOverride ?? "";
                    bool isPrevDefault = string.IsNullOrEmpty(prevOver) || prevOver.Equals("Default", StringComparison.OrdinalIgnoreCase);
                    prevPh = isPrevDefault ? "Default" : prevOver;
                }
                bool isContinuation = note.lyric.StartsWith("+");
                bool isTransition = !isContinuation && ((note.Prev == null && !isCurrentDefault) || (note.Prev != null && currentPh != prevPh));
                
                if (isTransition) {
                    var badgeBrush = hasError
                        ? ThemeManager.NeutralAccentBrushSemi
                        : (selectedNotes.Contains(note) ? ThemeManager.NoteBrushPressed : ThemeManager.NoteBrush);

                    if (isCurrentDefault) {
                        double boxWidth = 16; 
                        double boxHeight = 16;
                        double dotRadius = 3;
                        Avalonia.Rect boxRect = new Avalonia.Rect(
                            leftTop.X + 2, 
                            leftTop.Y - boxHeight - 4, 
                            boxWidth, 
                            boxHeight
                        );
                        Avalonia.Point center = new Avalonia.Point(
                            boxRect.X + boxWidth / 2, 
                            boxRect.Y + boxHeight / 2
                        );
                        context.DrawRectangle(badgeBrush, null, boxRect, cornerRadius, cornerRadius);
                        context.DrawEllipse(Brushes.White, null, center, dotRadius, dotRadius);
                        
                    } else {
                        var factory = PhonemizerFactory.Get(currentPh) ?? PhonemizerFactory.GetAll().FirstOrDefault(f => f.name == currentPh || (currentPh.Length > 0 && f.name.EndsWith(currentPh)));
                        string displayLang = GetPhonemizerLanguageBadgeCode(factory);
                        if (!string.IsNullOrEmpty(displayLang)) {
                            var langLayout = TextLayoutCache.Get(displayLang, Avalonia.Media.Brushes.White, 10);
                            double paddingX = 3;
                            double paddingY = 1.5;
                            Avalonia.Rect badgeRect = new Avalonia.Rect(
                                leftTop.X + 2, 
                                leftTop.Y - langLayout.Height - (paddingY * 2) - 4, 
                                langLayout.Width + (paddingX * 2), 
                                langLayout.Height + (paddingY * 2)
                            );
                            context.DrawRectangle(badgeBrush, null, badgeRect, cornerRadius, cornerRadius);
                            Avalonia.Point textPos = new Avalonia.Point(badgeRect.X + paddingX, badgeRect.Y + paddingY);
                            using (var state = context.PushTransform(Avalonia.Matrix.CreateTranslation(textPos.X, textPos.Y))) {
                                langLayout?.Draw(context, new Avalonia.Point());
                            }
                        }
                    }
                }
            }
            string displayLyric = note.lyric;
            int txtsize = 14;
            var textLayout = TextLayoutCache.Get(displayLyric, Brushes.White, txtsize);
            if (txtsize > size.Height) {
                return;
            }
            if (textLayout.Height + 5 < size.Height) {
                txtsize = (int)(12 * (size.Height / textLayout.Height));
                textLayout = TextLayoutCache.Get(displayLyric, Brushes.White, txtsize);
            }
            if (textLayout.Width + 5 > size.Width) {
                displayLyric = displayLyric[0] + "..";
                textLayout = TextLayoutCache.Get(displayLyric, Brushes.White, txtsize);
                if (textLayout.Width + 5 > size.Width) {
                    return;
                }
            }
            Point textPosition = leftTop.WithX(leftTop.X + 5)
                .WithY(Math.Round(leftTop.Y + (size.Height - textLayout.Height) / 2));
            using (var state = context.PushTransform(Matrix.CreateTranslation(textPosition.X, textPosition.Y))) {
                textLayout.Draw(context, new Point());
            }
        }

        private void RenderGhostNote(UNote note, NotesViewModel viewModel, DrawingContext context, int partOffset, IBrush brush) {
            // REVIEW should ghost note be smaller?
            double relativeSize = 0.5d;
            double height = TrackHeight * relativeSize;
            double yOffset = Math.Floor(height * 0.5f);
            Point leftTop = viewModel.TickToneToPoint(partOffset + note.position, note.AdjustedTone);
            leftTop = leftTop.WithX(leftTop.X + 1).WithY(Math.Round(leftTop.Y + 1 + yOffset));

            Size size = viewModel.TickToneToSize(note.duration, relativeSize);
            size = size.WithWidth(size.Width - 1).WithHeight(Math.Floor(size.Height - 2));

            Point rightBottom = new Point(leftTop.X + size.Width, leftTop.Y + size.Height);

            // Fixed rounding for ghost strips (half-height); do not use main note corner radius preference.
            context.DrawRectangle(brush, null, new Rect(leftTop, rightBottom), 2, 2);
        }

        void RenderNotePitchCenterLine(UNote note, NotesViewModel viewModel, DrawingContext context) {
            double y = note.AdjustedTone - 0.5;
            var left = viewModel.TickToneToPoint(note.position, y);
            var right = viewModel.TickToneToPoint(note.End, y);
            context.DrawLine(ThemeManager.NoteBorderPenPressed, left, right);
        }

        void RenderPhonemeTimingGuides(
            double leftTick, double rightTick, NotesViewModel viewModel, DrawingContext context) {
            if (!viewModel.ShowPhoneme || Part == null || Part.phonemes.Count == 0) {
                return;
            }
            double panelTop = Bounds.Height;
            var guideBrush = ThemeManager.NoteBorderPenPressed.Brush;
            if (guideBrush == null) {
                return;
            }
            var guidePen = new Pen(guideBrush, 1);
            using (context.PushOpacity(PitchFocusPhonemeGuideOpacity)) {
                foreach (var phoneme in Part.phonemes) {
                    if (phoneme.Parent.OverlapError) {
                        continue;
                    }
                    double leftBound = viewModel.Project.timeAxis.MsPosToTickPos(phoneme.PositionMs - phoneme.preutter) - Part.position;
                    if (leftBound > rightTick || phoneme.End < leftTick) {
                        continue;
                    }
                    double x = viewModel.TickToneToPoint(phoneme.position, 0).X;
                    context.DrawLine(guidePen, new Point(x, panelTop), new Point(x, 0));
                }
            }
        }

        private static readonly IDashStyle AcousticF0PatchPreviewDashStyle = new ImmutableDashStyle(new double[] { 6, 3 }, 0);
        private static readonly IBrush AcousticF0PatchPreviewBrush = new ImmutableSolidColorBrush(Color.FromRgb(0xFF, 0xA5, 0x3A));

        private void RenderAcousticF0PatchPreview(
            double leftTick, double rightTick, NotesViewModel viewModel, DrawingContext context) {
            if (!Preferences.Default.DiffSingerUnvoicedConsonantAcousticF0Interpolate
                || !Preferences.Default.DiffSingerShowAcousticF0PatchPreview) {
                return;
            }
            if (!TryGetDiffSingerRenderer(viewModel, out _)) {
                return;
            }
            var pen = new Pen(AcousticF0PatchPreviewBrush, 2) { DashStyle = AcousticF0PatchPreviewDashStyle };
            RenderPhrase[] phrases;
            lock (Part!) {
                phrases = Part!.renderPhrases.ToArray();
            }
            int headFrames = DiffSingerUtils.headFrames;
            foreach (var phrase in phrases) {
                if (phrase.position - Part!.position > rightTick || phrase.end - Part.position < leftTick) {
                    continue;
                }
                if (!DiffSingerRenderer.TryBuildAcousticF0PatchPreview(phrase, out float frameMs, out float[] acousticF0Hz)) {
                    continue;
                }
                points.Clear();
                double startMs = phrase.positionMs - headFrames * frameMs;
                for (int i = 0; i < acousticF0Hz.Length; ++i) {
                    double posMs = startMs + i * frameMs;
                    int tick = phrase.timeAxis.MsPosToTickPos(posMs) - Part.position;
                    if (tick < leftTick - 480 || tick > rightTick + 480) {
                        continue;
                    }
                    if (acousticF0Hz[i] <= 0f) {
                        continue;
                    }
                    float pitch = (float)MusicMath.FreqToTone(acousticF0Hz[i]) * 100f;
                    points.Add(viewModel.TickToneToPoint(tick, pitch / 100f - 0.5));
                }
                if (points.Count < 2) {
                    continue;
                }
                context.DrawGeometry(null, pen, new PolylineGeometry(points.ToArray(), false));
            }
        }

        private static readonly IDashStyle PhraseBoundaryDashStyle = new ImmutableDashStyle(new double[] { 4, 2, 1, 2 }, 0);
        private static readonly IBrush PhraseOverlapBrush = new ImmutableSolidColorBrush(Color.FromRgb(0xFF, 0x8C, 0x00));

        private void RenderDiffSingerPhraseBoundaries(double viewLeftTick, double viewRightTick, NotesViewModel viewModel, DrawingContext context) {
            if (!Preferences.Default.DiffSingerShowRenderPhraseBoundaries) {
                return;
            }
            if (!TryGetDiffSingerRenderer(viewModel, out var renderer)) {
                return;
            }
            var accent = ThemeManager.AccentBrush3;
            var boundaryPen = new Pen(accent, 1) { DashStyle = PhraseBoundaryDashStyle };
            var railPen = new Pen(accent, 2);
            var overlapRailPen = new Pen(PhraseOverlapBrush, 2);
            RenderPhrase[] phrases;
            lock (Part!) {
                phrases = Part!.renderPhrases.ToArray();
            }
            var visible = new List<(double startTick, double endTick)>(phrases.Length);
            foreach (var phrase in phrases) {
                var (startTick, endTick) = GetRenderedPhraseTickBounds(phrase, renderer);
                if (startTick >= viewRightTick || endTick <= viewLeftTick) {
                    continue;
                }
                visible.Add((startTick, endTick));
            }
            foreach (var (startTick, endTick) in visible) {
                DrawPhraseBoundaryLine(context, boundaryPen, viewModel.TickToneToPoint(startTick, 0).X);
                DrawPhraseBoundaryLine(context, boundaryPen, viewModel.TickToneToPoint(endTick, 0).X);
            }
            var events = new List<(double tick, int delta)>(visible.Count * 2);
            foreach (var (startTick, endTick) in visible) {
                events.Add((startTick, +1));
                events.Add((endTick, -1));
            }
            events.Sort((a, b) => a.tick.CompareTo(b.tick));
            int coverage = 0;
            double? segStart = null;
            int i = 0;
            while (i < events.Count) {
                double tick = events[i].tick;
                if (segStart.HasValue && coverage > 0 && tick > segStart.Value) {
                    double startX = Math.Clamp(viewModel.TickToneToPoint(segStart.Value, 0).X, 0, Bounds.Width);
                    double endX = Math.Clamp(viewModel.TickToneToPoint(tick, 0).X, 0, Bounds.Width);
                    if (endX > startX) {
                        var pen = coverage >= 2 ? overlapRailPen : railPen;
                        context.DrawLine(pen, new Point(startX, 3.5), new Point(endX, 3.5));
                    }
                }
                while (i < events.Count && events[i].tick == tick) {
                    coverage += events[i].delta;
                    i++;
                }
                segStart = tick;
            }
        }

        private void DrawPhraseBoundaryLine(DrawingContext context, IPen pen, double x) {
            if (Bounds.Width < 1 || x < 0 || x > Bounds.Width) {
                return;
            }
            double crispX = Math.Clamp(Math.Round(x) + 0.5, 0.5, Bounds.Width - 0.5);
            context.DrawLine(pen, new Point(crispX, 0), new Point(crispX, Bounds.Height));
        }

        private bool TryGetDiffSingerRenderer(NotesViewModel viewModel, out IRenderer? renderer) {
            renderer = null;
            if (Part == null || viewModel.Project == null || Part.trackNo < 0 || Part.trackNo >= viewModel.Project.tracks.Count) {
                return false;
            }
            var settings = viewModel.Project.tracks[Part.trackNo].RendererSettings;
            renderer = settings?.Renderer;
            return string.Equals(renderer?.ToString(), Renderers.DIFFSINGER, StringComparison.OrdinalIgnoreCase)
                || string.Equals(settings?.renderer, Renderers.DIFFSINGER, StringComparison.OrdinalIgnoreCase);
        }

        private (double startTick, double endTick) GetRenderedPhraseTickBounds(RenderPhrase phrase, IRenderer? renderer) {
            if (Part == null) {
                return (0, 0);
            }
            try {
                var layout = renderer?.Layout(phrase);
                if (layout != null) {
                    double startMs = layout.positionMs - layout.leadingMs;
                    double endMs = startMs + layout.estimatedLengthMs;
                    return (
                        phrase.timeAxis.MsPosToTickPos(startMs) - Part.position,
                        phrase.timeAxis.MsPosToTickPos(endMs) - Part.position);
                }
            } catch {
                // Rendering invalid singers should not break piano roll painting.
            }
            return (phrase.position - phrase.leading - Part.position, phrase.end - Part.position);
        }

        private void RenderPitchBend(UNote note, NotesViewModel viewModel, DrawingContext context) {
            var pitchExp = note.pitch;
            var pts = pitchExp.data;
            if (pts.Count < 2 || viewModel.Part == null) return;

            var project = viewModel.Project;
            double p0Tick = project.timeAxis.MsPosToTickPos(note.PositionMs + pts[0].X) - viewModel.Part.position;
            double p0Tone = note.AdjustedTone + pts[0].Y / 10.0;
            Point p0 = viewModel.TickToneToPoint(p0Tick, p0Tone - 0.5);
            Point p_1 = p0;
            points.Clear();
            points.Add(p0);

            var brush = note.pitch.snapFirst ? ThemeManager.AccentBrush3 : null;
            var pen = ThemeManager.AccentPen3;
            using (var state = context.PushTransform(Matrix.CreateTranslation(p0.X, p0.Y))) {
                context.DrawGeometry(brush, pen, pointGeometry);
            }

            for (int i = 1; i < pts.Count; i++) {
                double p1Tick = project.timeAxis.MsPosToTickPos(note.PositionMs + pts[i].X) - viewModel.Part.position;
                double p1Tone = note.AdjustedTone + pts[i].Y / 10.0;
                Point p1 = viewModel.TickToneToPoint(p1Tick, p1Tone - 0.5);
                CubicSplineSegment? curve = null;

                if (pts.Count > 2 && pts[i - 1].shape == PitchPointShape.sp) {
                    var p2 = p1;
                    if (i == 1) {
                        if (note.pitch.data[0].X > 0) {
                            p_1 = viewModel.TickToneToPoint(note.position, p0Tone - 0.5);
                        }
                    }
                    if (i < pts.Count - 1) {
                        double p2Tick = project.timeAxis.MsPosToTickPos(note.PositionMs + pts[i + 1].X) - viewModel.Part.position;
                        double p2Tone = note.AdjustedTone + pts[i + 1].Y / 10.0;
                        p2 = viewModel.TickToneToPoint(p2Tick, p2Tone - 0.5);
                    } else if (pts[i].X < note.DurationMs) {
                        p2 = viewModel.TickToneToPoint(note.End, note.AdjustedTone - 0.5);
                    }
                    curve = new CubicSplineSegment(
                                p_1.X, p_1.Y,
                                p0.X, p0.Y,
                                p1.X, p1.Y,
                                p2.X, p2.Y);
                }
                // Draw arc
                double x0 = p0.X;
                double y0 = p0.Y;
                double x1 = p0.X;
                double y1 = p0.Y;
                if (p1.X - p0.X < 5) {
                    points.Add(p1);
                } else {
                    points.Add(new Point(x0, y0));
                    while (x0 < p1.X) {
                        x1 = Math.Min(x1 + 4, p1.X);
                        y1 = curve?.GetY(x1) ?? MusicMath.InterpolateShape(p0.X, p1.X, p0.Y, p1.Y, x1, pts[i - 1].shape);
                        points.Add(new Point(x1, y1));
                        x0 = x1;
                        y0 = y1;
                    }
                }
                p_1 = p0;
                p0 = p1;
                using (var state = context.PushTransform(Matrix.CreateTranslation(p0.X, p0.Y))) {
                    context.DrawGeometry(null, pen, pointGeometry);
                }
            }
            var polyline = new PolylineGeometry(points.ToArray(), false);
            context.DrawGeometry(null, pen, polyline);
        }

        private void RenderVibrato(UNote note, NotesViewModel viewModel, DrawingContext context) {
            var vibrato = note.vibrato;
            if (vibrato == null || vibrato.length == 0) {
                return;
            }

            var pen = ThemeManager.AccentPen3;
            float nPeriod = (float)viewModel.Project.timeAxis.TicksBetweenMsPos(note.PositionMs, note.PositionMs + vibrato.period) / note.duration;
            float nPos = vibrato.NormalizedStart;
            var point = vibrato.Evaluate(nPos, nPeriod, note);
            points.Clear();
            points.Add(viewModel.TickToneToPoint(point.X, point.Y - 0.5));
            while (nPos < 1) {
                nPos = Math.Min(1, nPos + nPeriod / 16);
                point = vibrato.Evaluate(nPos, nPeriod, note);
                points.Add(viewModel.TickToneToPoint(point.X, point.Y - 0.5));
            }
            polylineGeometry.Points = points;
            context.DrawGeometry(null, pen, polylineGeometry);
        }

        private readonly Geometry vibratoIcon = Geometry.Parse("M-6.5 1 L-6 1.5 L-4.5 0 L-2 2.5 L0.5 0 L3 2.5 L6.5 -1 L6 -1.5 L4.5 0 L2 -2.5 L-0.5 0 L-3 -2.5 Z");
        private void RenderVibratoToggle(UNote note, NotesViewModel viewModel, DrawingContext context) {
            var vibrato = note.vibrato;
            var togglePos = vibrato.GetToggle(note);
            Point icon = viewModel.TickToneToPoint(togglePos.X, togglePos.Y);
            var pen = ThemeManager.BarNumberPen;
            using (var state = context.PushTransform(Matrix.CreateTranslation(icon.X - 10, icon.Y))) {
                context.DrawGeometry(vibrato.length == 0 ? null : pen.Brush, pen, vibratoIcon);
            }
        }

        private void RenderVibratoControl(UNote note, NotesViewModel viewModel, DrawingContext context) {
            var vibrato = note.vibrato;
            if (vibrato.length == 0) {
                return;
            }
            var pen = ThemeManager.BarNumberPen!;
            Point start = viewModel.TickToneToPoint(vibrato.GetEnvelopeStart(note));
            Point fadeIn = viewModel.TickToneToPoint(vibrato.GetEnvelopeFadeIn(note));
            Point fadeOut = viewModel.TickToneToPoint(vibrato.GetEnvelopeFadeOut(note));
            Point end = viewModel.TickToneToPoint(vibrato.GetEnvelopeEnd(note));
            context.DrawLine(pen, start, fadeIn);
            context.DrawLine(pen, fadeIn, fadeOut);
            context.DrawLine(pen, fadeOut, end);
            using (var state = context.PushTransform(Matrix.CreateTranslation(start))) {
                context.DrawGeometry(pen.Brush, pen, pointGeometry);
            }
            using (var state = context.PushTransform(Matrix.CreateTranslation(fadeIn))) {
                context.DrawGeometry(pen.Brush, pen, pointGeometry);
            }
            using (var state = context.PushTransform(Matrix.CreateTranslation(fadeOut))) {
                context.DrawGeometry(pen.Brush, pen, pointGeometry);
            }
            vibrato.GetPeriodStartEnd(DocManager.Inst.Project, note, out var periodStartPos, out var periodEndPos);
            Point periodStart = viewModel.TickToneToPoint(periodStartPos);
            Point periodEnd = viewModel.TickToneToPoint(periodEndPos);
            float height = (float)TrackHeight / 3;
            periodStart = periodStart.WithY(periodStart.Y - height / 2 - 0.5f);
            double width = periodEnd.X - periodStart.X;
            periodEnd = periodEnd.WithX(periodEnd.X - 2).WithY(periodEnd.Y - height / 2 - 0.5f);
            context.DrawRectangle(null, pen, new Rect(periodStart, new Size(width, height)), 1, 1);
            context.DrawLine(pen, periodEnd, periodEnd + new Vector(0, height));
        }

        private void RenderFinalPitch(
            double leftTick, double rightTick, NotesViewModel viewModel, DrawingContext context,
            double? thickness = null) {
            IPen pen = ThemeManager.FinalPitchPen!;
            if (thickness.HasValue && pen.Brush != null) {
                pen = new Pen(pen.Brush, thickness.Value);
            }
            lock (Part!) {
                foreach (var phrase in Part!.renderPhrases) {
                    if (phrase.position - Part.position > rightTick || phrase.end - Part.position < leftTick) {
                        continue;
                    }
                    int pitchStart = phrase.position - phrase.leading - Part.position;
                    int startIdx = (int)Math.Max(0, (leftTick - pitchStart) / 5);
                    int endIdx = (int)Math.Min(phrase.pitches.Length, (rightTick - pitchStart) / 5 + 1);
                    if (endIdx <= startIdx) {
                        continue;
                    }
                    points.Clear();
                    for (int i = startIdx; i < endIdx; ++i) {
                        int t = pitchStart + i * 5;
                        float p = phrase.pitches[i];
                        points.Add(viewModel.TickToneToPoint(t, p / 100 - 0.5));
                    }
                    if (points.Count < 2) {
                        continue;
                    }
                    var polyline = new PolylineGeometry(points.ToArray(), false);
                    context.DrawGeometry(null, pen, polyline);
                }
            }
        }
    }
}
