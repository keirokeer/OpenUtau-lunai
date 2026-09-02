using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using OpenUtau.App.ViewModels;
using OpenUtau.App;
using OpenUtau.Core;
using OpenUtau.Core.DiffSinger;
using OpenUtau.Core.Format;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using OpenUtau.ViewModels;
using ReactiveUI;

namespace OpenUtau.App.Controls {
    public enum ExpDisMode { Hidden, Visible, Shadow };

    class ExpressionCanvas : Control {
        public static readonly DirectProperty<ExpressionCanvas, double> TickWidthProperty =
            AvaloniaProperty.RegisterDirect<ExpressionCanvas, double>(
                nameof(TickWidth),
                o => o.TickWidth,
                (o, v) => o.TickWidth = v);
        public static readonly DirectProperty<ExpressionCanvas, double> TickOffsetProperty =
            AvaloniaProperty.RegisterDirect<ExpressionCanvas, double>(
                nameof(TickOffset),
                o => o.TickOffset,
                (o, v) => o.TickOffset = v);
        public static readonly DirectProperty<ExpressionCanvas, UVoicePart?> PartProperty =
            AvaloniaProperty.RegisterDirect<ExpressionCanvas, UVoicePart?>(
                nameof(Part),
                o => o.Part,
                (o, v) => o.Part = v);
        public static readonly DirectProperty<ExpressionCanvas, string> KeyProperty =
            AvaloniaProperty.RegisterDirect<ExpressionCanvas, string>(
                nameof(Key),
                o => o.Key,
                (o, v) => o.Key = v);
        public static readonly DirectProperty<ExpressionCanvas, bool> ShowRealCurveProperty =
            AvaloniaProperty.RegisterDirect<ExpressionCanvas, bool>(
                nameof(ShowRealCurve),
                o => o.ShowRealCurve,
                (o, v) => o.ShowRealCurve = v);

        public double TickWidth {
            get => tickWidth;
            private set => SetAndRaise(TickWidthProperty, ref tickWidth, value);
        }
        public double TickOffset {
            get => tickOffset;
            private set => SetAndRaise(TickOffsetProperty, ref tickOffset, value);
        }
        public UVoicePart? Part {
            get => part;
            set => SetAndRaise(PartProperty, ref part, value);
        }
        public string Key {
            get => key;
            set => SetAndRaise(KeyProperty, ref key, value);
        }
        public bool ShowRealCurve {
            get => showRealCurve;
            set => SetAndRaise(ShowRealCurveProperty, ref showRealCurve, value);
        }

        private double tickWidth;
        private double tickOffset;
        private UVoicePart? part;
        private string key = string.Empty;
        private bool showRealCurve = true;

        private HashSet<UNote> selectedNotes = new HashSet<UNote>();
        private CurveSelection curveSelection = new CurveSelection();
        private Geometry pointGeometry;
        private Geometry circleGeometry;
        private string? cachedFillBrushKey;
        private IBrush? cachedFillBrush;

        public ExpressionCanvas() {
            ClipToBounds = true;
            pointGeometry = new EllipseGeometry(new Rect(-2.5, -2.5, 5, 5));
            circleGeometry = new EllipseGeometry(new Rect(-4.5, -4.5, 9, 9));
            MessageBus.Current.Listen<NotesRefreshEvent>()
                .Subscribe(_ => InvalidateVisual());
            MessageBus.Current.Listen<NotesSelectionEvent>()
                .Subscribe(e => {
                    selectedNotes.Clear();
                    selectedNotes.UnionWith(e.selectedNotes);
                    selectedNotes.UnionWith(e.tempSelectedNotes);
                    InvalidateVisual();
                });
            MessageBus.Current.Listen<CurveSelectionEvent>()
                .Subscribe(e => {
                    curveSelection = e.selection;
                    InvalidateVisual();
                });
            MessageBus.Current.Listen<ThemeChangedEvent>()
                .Subscribe(_ => {
                    cachedFillBrush = null;
                    cachedFillBrushKey = null;
                    InvalidateVisual();
                });
            MessageBus.Current.Listen<ExpressionCurveStyleChangedEvent>()
                .Subscribe(_ => InvalidateVisual());
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
            base.OnPropertyChanged(change);
            if (change.Property == PartProperty ||
                change.Property == KeyProperty ||
                change.Property == TickWidthProperty ||
                change.Property == TickOffsetProperty) {
                cachedFillBrush = null;
                cachedFillBrushKey = null;
            }
            InvalidateVisual();
        }

        public override void Render(DrawingContext context) {
            base.Render(context);
            if (Part == null) {
                return;
            }
            var viewModel = ((PianoRollViewModel?)DataContext)?.NotesViewModel;
            if (viewModel == null) {
                return;
            }
            var project = DocManager.Inst.Project;
            var track = project.tracks[Part.trackNo];
            if (!track.TryGetExpDescriptor(project, key, out var descriptor)) {
                return;
            }
            if (descriptor.max <= descriptor.min) {
                return;
            }
            bool useTrackColor = Preferences.Default.UseTrackColor;
            var tcolor = ThemeManager.GetTrackColor(track.TrackColor);
            var pointOutlinePenNormal = useTrackColor ? new Pen(tcolor.AccentColorCenterKey, 2) : ThemeManager.AccentPen1Thickness2;
            var pointOutlinePenSelected = useTrackColor ? new Pen(tcolor.AccentColorLight, 2) : ThemeManager.AccentPen1Thickness2;
            var useTrackColorForCurve = useTrackColor && !ThemeManager.IsDarkMode;
            var lPen = useTrackColorForCurve ? new Pen(tcolor.AccentColorDark, 1) : ThemeManager.AccentPen1;
            var lPen2 = useTrackColorForCurve ? new Pen(tcolor.AccentColorDark, 2) : ThemeManager.AccentPen1Thickness2;
            var accentBrush = useTrackColor ? tcolor.AccentColor : (IBrush)ThemeManager.AccentBrush1Note;
            var accentPen2 = useTrackColor ? new Pen(tcolor.AccentColorDark, 2) : ThemeManager.AccentPen2Thickness2;
            var accentPen3 = useTrackColor ? new Pen(tcolor.AccentColorDark, 3) : ThemeManager.AccentPen2Thickness3;
            var accentBrush2 = useTrackColor ? tcolor.AccentColorDark : (IBrush)ThemeManager.AccentBrush2;
            DrawBackgroundForHitTest(context);
            double leftTick = TickOffset - 480;
            double rightTick = TickOffset + Bounds.Width / TickWidth + 480;
            double optionHeight = descriptor.type == UExpressionType.Options
                ? Bounds.Height / descriptor.options.Length
                : 0;
            if (descriptor.type == UExpressionType.Curve) {
                var curve = Part.curves.FirstOrDefault(c => c.descriptor == descriptor)
                    ?? Part.curves.FirstOrDefault(c => c.abbr == descriptor.abbr);
                float baseline = descriptor.CustomDefaultValue;
                if (Part.trackNo >= 0 && Part.trackNo < project.tracks.Count) {
                    baseline = ExpressionDefaultResolver.GetEffectiveDefault(
                        project, project.tracks[Part.trackNo], descriptor.abbr);
                }
                double defaultHeight = Math.Round(Bounds.Height - Bounds.Height * (baseline - descriptor.min) / (descriptor.max - descriptor.min));
                Color curveFillColor = useTrackColor
                    ? tcolor.AccentColor.Color
                    : ((SolidColorBrush)ThemeManager.AccentBrush1).Color;
                var lPen2Selected = useTrackColorForCurve ? new Pen(tcolor.AccentColorDark, 2) : ThemeManager.AccentPen2Thickness2;
                IBrush defaultLineBrush = Preferences.Default.SolidExpPanelGridLines
                    ? HalveEffectiveOpacity(ThemeManager.NeutralAccentBrush)
                    : ThemeManager.NeutralAccentBrush;
                var defaultValuePen = Preferences.Default.SolidExpPanelGridLines
                    ? new Pen(defaultLineBrush, 1)
                    : new Pen(ThemeManager.NeutralAccentBrush, 1, new DashStyle(new double[] { 4, 4 }, 0));
                double x3 = Math.Round(viewModel.TickToneToPoint(leftTick, 0).X);
                double x4 = Math.Round(viewModel.TickToneToPoint(rightTick, 0).X);
                // Project default baseline — always visible; authored curve is drawn only on real points.
                context.DrawLine(defaultValuePen, new Point(x3, defaultHeight), new Point(x4, defaultHeight));

                curveSelection.GetWholeCurveAndSelection(descriptor.abbr, curve, out List<int> xs, out List<int> ys);

                int lTick = (int)Math.Floor(leftTick / 5) * 5;
                int rTick = (int)Math.Ceiling(rightTick / 5) * 5;
                if (xs.Count >= 2) {
                    if (ShowRealCurve) {
                        DrawCurveValueFill(context, viewModel, descriptor, curve, xs, ys, defaultHeight, baseline, lTick, rTick, curveFillColor);
                    }
                    // Only connect real control points — never invent edge points at the current default
                    // (that created long diagonals across empty regions after the default changed).
                    for (int i = 0; i < xs.Count - 1; i++) {
                        int tick1 = xs[i];
                        int tick2 = xs[i + 1];
                        if (tick2 < lTick || tick1 > rTick) {
                            continue;
                        }
                        if (curve != null && curve.HasBreakBetween(tick1, tick2)) {
                            continue;
                        }
                        float value1 = ys[i];
                        float value2 = ys[i + 1];
                        bool atDefault = Math.Abs(value1 - baseline) < 0.0001f && Math.Abs(value2 - baseline) < 0.0001f;
                        if (atDefault) {
                            // Flat on the baseline — already drawn as the default line.
                            continue;
                        }
                        double x1 = viewModel.TickToneToPoint(tick1, 0).X;
                        double y1 = defaultHeight - Bounds.Height * (value1 - baseline) / (descriptor.max - descriptor.min);
                        double x2 = viewModel.TickToneToPoint(tick2, 0).X;
                        double y2 = defaultHeight - Bounds.Height * (value2 - baseline) / (descriptor.max - descriptor.min);
                        IPen pen;
                        if (curveSelection.HasValue(descriptor.abbr) &&
                            curveSelection.StartPoint.x <= tick1 && tick1 <= curveSelection.EndPoint.x &&
                            curveSelection.StartPoint.x <= tick2 && tick2 <= curveSelection.EndPoint.x) {
                            pen = lPen2Selected;
                        } else {
                            pen = lPen2;
                        }
                        context.DrawLine(pen, new Point(x1, y1), new Point(x2, y2));
                    }
                }
                if (xs.Count == 1) {
                    int tick = xs[0];
                    if (tick >= lTick && tick <= rTick) {
                        float value = ys[0];
                        double x = viewModel.TickToneToPoint(tick, 0).X;
                        double y = defaultHeight - Bounds.Height * (value - baseline) / (descriptor.max - descriptor.min);
                        bool selected = curveSelection.HasValue(descriptor.abbr) &&
                            curveSelection.StartPoint.x <= tick && tick <= curveSelection.EndPoint.x;
                        var pen = selected ? lPen2Selected : lPen2;
                        context.DrawEllipse(null, pen, new Point(x, y), 2.5, 2.5);
                    }
                }
                // Real curves must draw even when the user has not authored any points yet.
                if (ShowRealCurve && curve != null) {
                    DrawRealCurveGeometry(context, viewModel, curve, lTick, rTick);
                }
                DrawPhonemeRemapCurveGeometry(context, viewModel, descriptor, curve, baseline, lTick, rTick);
                return;
            }
            foreach (var phoneme in Part.phonemes) {
                if (phoneme.Error || phoneme.Parent == null) {
                    continue;
                }
                double leftBound = phoneme.position;
                double rightBound = phoneme.End;
                if (leftBound >= rightTick || rightBound <= leftTick) {
                    continue;
                }
                var note = phoneme.Parent;
                var hPen = selectedNotes.Contains(note) ? accentPen2 : lPen2;
                var vPen = selectedNotes.Contains(note) ? accentPen3 : (useTrackColor ? new Pen(tcolor.AccentColor, 3) : ThemeManager.AccentPen1Thickness3);
                var brush = selectedNotes.Contains(note) ? accentBrush2 : (useTrackColor ? tcolor.AccentColor : (IBrush)ThemeManager.AccentBrush1);
                var (value, overriden) = phoneme.GetExpression(project, track, Key);
                double x1 = Math.Round(viewModel.TickToneToPoint(phoneme.position, 0).X);
                double x2 = Math.Round(viewModel.TickToneToPoint(phoneme.End, 0).X);
                if (descriptor.type == UExpressionType.Numerical) {
                    double valueHeight = Math.Round(Bounds.Height - Bounds.Height * (value - descriptor.min) / (descriptor.max - descriptor.min));
                    double zeroHeight = Math.Round(Bounds.Height - Bounds.Height * (0f - descriptor.min) / (descriptor.max - descriptor.min));
                    context.DrawLine(vPen, new Point(x1 + 0.5, zeroHeight + 0.5), new Point(x1 + 0.5, valueHeight + 3));
                    context.DrawLine(hPen, new Point(x1 + 3, valueHeight), new Point(Math.Max(x1 + 3, x2 - 3), valueHeight));
                    var pointOutlinePen = selectedNotes.Contains(note) ? pointOutlinePenSelected : pointOutlinePenNormal;
                    using (var state = context.PushTransform(Matrix.CreateTranslation(x1 + 0.5, valueHeight))) {
                        context.DrawGeometry(overriden ? brush : ThemeManager.BackgroundBrush, pointOutlinePen, pointGeometry);
                    }
                } else if (descriptor.type == UExpressionType.Options) {
                    var circleOutlinePen = selectedNotes.Contains(note) ? pointOutlinePenSelected : pointOutlinePenNormal;
                    for (int i = 0; i < descriptor.options.Length; ++i) {
                        double y = optionHeight * (descriptor.options.Length - 1 - i + 0.5);
                        using (var state = context.PushTransform(Matrix.CreateTranslation(x1 + 4.5, y))) {
                            if ((int)value == i) {
                                if (overriden) {
                                    context.DrawGeometry(brush, null, pointGeometry);
                                }
                                context.DrawGeometry(null, circleOutlinePen, circleGeometry);
                            } else {
                                context.DrawGeometry(null, ThemeManager.NeutralAccentPenSemi, circleGeometry);
                            }
                        }
                    }
                }
            }
            if (descriptor.type == UExpressionType.Options) {
                for (int i = 0; i < descriptor.options.Length; ++i) {
                    string option = descriptor.options[i];
                    if (string.IsNullOrEmpty(option)) {
                        option = "\"\"";
                    }
                    var textLayout = TextLayoutCache.Get(option, ThemeManager.ForegroundBrush, 12);
                    double y = optionHeight * (descriptor.options.Length - 1 - i + 0.5) - textLayout.Height * 0.5;
                    y = Math.Round(y);
                    var size = new Size(textLayout.Width + 8, textLayout.Height + 2);
                    using (var state = context.PushTransform(Matrix.CreateTranslation(12, y))) {
                        context.DrawRectangle(
                            ThemeManager.BackgroundBrush,
                            ThemeManager.NeutralAccentPenSemi,
                            new Rect(new Point(-4, -0.5), size), 4, 4);
                        textLayout.Draw(context, new Point());
                    }
                }
            }
        }

        private void DrawBackgroundForHitTest(DrawingContext context) {
            context.DrawRectangle(Brushes.Transparent, null, Bounds.WithX(0).WithY(0));
        }

        private static readonly IPen PhonemeRemapPreviewPen = new Pen(Brushes.White, 2);

        void DrawPhonemeRemapCurveGeometry(
            DrawingContext context,
            NotesViewModel viewModel,
            UExpressionDescriptor descriptor,
            UCurve? curve,
            float baseline,
            int lTick,
            int rTick) {
            if (!Preferences.Default.DiffSingerShowPhonemeVarianceRemapPreview) {
                return;
            }
            if (descriptor.abbr != Ustx.OPEC && descriptor.abbr != Ustx.TENC) {
                return;
            }
            if (Part == null) {
                return;
            }
            var project = DocManager.Inst.Project;
            var track = project.tracks[Part.trackNo];
            if (!DiffSingerUtils.IsExpressionAvailable(track.Singer, descriptor.abbr)) {
                return;
            }
            RenderPhrase[] phrases;
            lock (Part) {
                phrases = Part.renderPhrases.ToArray();
            }
            foreach (var phrase in phrases) {
                if (phrase.position - Part.position > rTick || phrase.end - Part.position < lTick) {
                    continue;
                }
                float[]? phraseUserCurve = descriptor.abbr switch {
                    Ustx.TENC => phrase.tension,
                    Ustx.OPEC => phrase.mouthOpening,
                    _ => null,
                };
                if (!PhonemeVarianceRemapPreview.TryBuildEffectiveCurve(
                    phrase, Part.position, descriptor.abbr, phraseUserCurve, baseline,
                    out float[] ticks, out float[] values)) {
                    continue;
                }
                var points = new List<Point>();
                for (int i = 0; i < ticks.Length; ++i) {
                    int tick = (int)Math.Round(ticks[i]);
                    if (tick < lTick || tick > rTick) {
                        continue;
                    }
                    float value = values[i];
                    double x = viewModel.TickToneToPoint(tick, 0).X;
                    double y = Bounds.Height - Bounds.Height * (value - descriptor.min) / (descriptor.max - descriptor.min);
                    points.Add(new Point(x, y));
                }
                if (points.Count < 2) {
                    continue;
                }
                context.DrawGeometry(null, PhonemeRemapPreviewPen, new PolylineGeometry(points.ToArray(), false));
            }
        }

        void DrawRealCurveGeometry(
            DrawingContext context,
            NotesViewModel viewModel,
            UCurve curve,
            int lTick,
            int rTick) {
            if (curve.realXs.Count < 2) {
                return;
            }
            int baseIndexL = curve.realXs.BinarySearch(lTick);
            if (baseIndexL < 0) {
                baseIndexL = ~baseIndexL;
            }
            baseIndexL = Math.Max(0, baseIndexL - 1);
            int baseIndexR = curve.realXs.BinarySearch(rTick);
            if (baseIndexR < 0) {
                baseIndexR = ~baseIndexR;
            }
            int offset = baseIndexL;
            while (offset < baseIndexR) {
                // negative values are breakpoints
                int start = offset;
                while (start < baseIndexR && curve.realYs[start] < 0) ++start;
                int end = start;
                while (end < baseIndexR && curve.realYs[end] >= 0) ++end;
                if (end - start < 2) {
                    offset = end;
                    continue;
                }
                var geometry = new PathGeometry();
                var figure = new PathFigure {
                    IsClosed = false
                };
                for (int i = start; i < end; ++i) {
                    float tick = curve.realXs[i];
                    float value = curve.realYs[i];
                    double x = viewModel.TickToneToPoint(tick, 0).X;
                    double y = Bounds.Height * (1 - value / 1000.0);
                    if (i == start) {
                        figure.StartPoint = new Point(x, Bounds.Height);
                    }
                    figure.Segments!.Add(new LineSegment {
                        Point = new Point(x, y),
                        IsStroked = i != start
                    });
                    if (i == end - 1) {
                        figure.Segments!.Add(new LineSegment {
                            Point = new Point(x, Bounds.Height),
                            IsStroked = false
                        });
                    }
                }
                geometry.Figures!.Add(figure);
                var realCurvePen = Preferences.Default.SolidExpPanelGridLines
                    ? new Pen(ThemeManager.RealCurveStrokeBrush, ThemeManager.RealCurvePen.Thickness)
                    : ThemeManager.RealCurvePen;
                context.DrawGeometry(ThemeManager.RealCurveFillBrush, realCurvePen, geometry);
                offset = end;
            }
        }

        IBrush GetCurveFillBrush(double defaultHeight, Color color) {
            double w = Bounds.Width;
            double h = Bounds.Height;
            string key = $"{w:R}:{h:R}:{defaultHeight:R}:{color}";
            if (cachedFillBrush != null && cachedFillBrushKey == key) {
                return cachedFillBrush;
            }
            // Absolute coordinates so each fill segment samples the same panel-wide gradient
            // (RelativeUnit.Relative maps to each geometry's bounds and causes 100%-0%-100% bands).
            double defaultOffset = h > 0 ? Math.Clamp(defaultHeight / h, 0, 1) : 0.5;
            byte peakAlpha = (byte)Math.Clamp((int)Math.Round(color.A * 0.25), 0, 255);
            var peak = Color.FromArgb(peakAlpha, color.R, color.G, color.B);
            var transparent = Color.FromArgb(0, color.R, color.G, color.B);
            var brush = new LinearGradientBrush {
                StartPoint = new RelativePoint(w * 0.5, 0, RelativeUnit.Absolute),
                EndPoint = new RelativePoint(w * 0.5, h, RelativeUnit.Absolute),
                GradientStops = new GradientStops {
                    new GradientStop(peak, 0),
                    new GradientStop(transparent, defaultOffset),
                    new GradientStop(peak, 1),
                },
            };
            cachedFillBrushKey = key;
            cachedFillBrush = brush;
            return brush;
        }

        void DrawCurveValueFill(DrawingContext context, NotesViewModel viewModel, UExpressionDescriptor descriptor,
            UCurve? curve, List<int> xs, List<int> ys, double defaultHeight, float baseline, int lTick, int rTick, Color fillColor) {
            const double eps = 0.5;
            var figures = new PathFigures();
            for (int i = 0; i < xs.Count - 1; i++) {
                int tick1 = xs[i];
                int tick2 = xs[i + 1];
                if (tick2 < lTick || tick1 > rTick) {
                    continue;
                }
                if (curve != null && curve.HasBreakBetween(tick1, tick2)) {
                    continue;
                }
                float value1 = ys[i];
                float value2 = ys[i + 1];
                double x1 = viewModel.TickToneToPoint(tick1, 0).X;
                double y1 = defaultHeight - Bounds.Height * (value1 - baseline) / (descriptor.max - descriptor.min);
                double x2 = viewModel.TickToneToPoint(tick2, 0).X;
                double y2 = defaultHeight - Bounds.Height * (value2 - baseline) / (descriptor.max - descriptor.min);
                var p1 = new Point(x1, y1);
                var p2 = new Point(x2, y2);
                if (Math.Abs(p1.Y - defaultHeight) >= eps || Math.Abs(p2.Y - defaultHeight) >= eps) {
                    AddCurveFillSegment(figures, p1, p2, defaultHeight);
                }
            }
            if (figures.Count == 0) {
                return;
            }
            context.DrawGeometry(GetCurveFillBrush(defaultHeight, fillColor), null, new PathGeometry { Figures = figures });
        }

        static void AddCurveFillSegment(PathFigures figures, Point p1, Point p2, double defaultY) {
            const double eps = 0.5;
            if (Math.Abs(p1.Y - defaultY) < eps && Math.Abs(p2.Y - defaultY) < eps) {
                return;
            }
            if ((p1.Y - defaultY) * (p2.Y - defaultY) < 0) {
                double t = (defaultY - p1.Y) / (p2.Y - p1.Y);
                double xc = p1.X + t * (p2.X - p1.X);
                var cross = new Point(xc, defaultY);
                AddCurveFillSegment(figures, p1, cross, defaultY);
                AddCurveFillSegment(figures, cross, p2, defaultY);
                return;
            }
            figures.Add(new PathFigure {
                StartPoint = new Point(p1.X, defaultY),
                IsClosed = true,
                Segments = new PathSegments {
                    new LineSegment { Point = p1 },
                    new LineSegment { Point = p2 },
                    new LineSegment { Point = new Point(p2.X, defaultY) },
                },
            });
        }

        static IBrush HalveEffectiveOpacity(IBrush brush) {
            if (brush is not SolidColorBrush scb) {
                return brush;
            }
            var c = scb.Color;
            double effective = (c.A / 255.0) * scb.Opacity * 0.5;
            byte newA = (byte)Math.Clamp((int)Math.Round(effective * 255.0), 0, 255);
            return new SolidColorBrush(Color.FromArgb(newA, c.R, c.G, c.B));
        }
    }
}
