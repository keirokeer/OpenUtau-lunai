using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using NWaves.Signals;
using OpenUtau.App;
using OpenUtau.Core;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using ReactiveUI;
using Serilog;

namespace OpenUtau.App.Controls {
    class PartControl : Control, IDisposable, IProgress<int> {
        public static readonly DirectProperty<PartControl, double> TickWidthProperty =
            AvaloniaProperty.RegisterDirect<PartControl, double>(
                nameof(TickWidth),
                o => o.TickWidth,
                (o, v) => o.TickWidth = v);
        public static readonly DirectProperty<PartControl, double> TrackHeightProperty =
            AvaloniaProperty.RegisterDirect<PartControl, double>(
                nameof(TrackHeight),
                o => o.TrackHeight,
                (o, v) => o.TrackHeight = v);
        public static readonly DirectProperty<PartControl, double> ViewWidthProperty =
            AvaloniaProperty.RegisterDirect<PartControl, double>(
                nameof(ViewWidth),
                o => o.ViewWidth,
                (o, v) => o.ViewWidth = v);
        public static readonly DirectProperty<PartControl, double> TickOffsetProperty =
            AvaloniaProperty.RegisterDirect<PartControl, double>(
                nameof(TickOffset),
                o => o.TickOffset,
                (o, v) => o.TickOffset = v);
        public static readonly DirectProperty<PartControl, Point> OffsetProperty =
            AvaloniaProperty.RegisterDirect<PartControl, Point>(
                nameof(Offset),
                o => o.Offset,
                (o, v) => o.Offset = v);
        public static readonly DirectProperty<PartControl, string> TextProperty =
            AvaloniaProperty.RegisterDirect<PartControl, string>(
                nameof(Text),
                o => o.Text,
                (o, v) => o.Text = v);
        public static readonly DirectProperty<PartControl, bool> SelectedProperty =
            AvaloniaProperty.RegisterDirect<PartControl, bool>(
                nameof(Selected),
                o => o.Selected,
                (o, v) => o.Selected = v);
        public static readonly DirectProperty<PartControl, double> FadeInProperty =
            AvaloniaProperty.RegisterDirect<PartControl, double>(
                nameof(FadeIn),
                o => o.FadeIn,
                (o, v) => o.FadeIn = v);
        public static readonly DirectProperty<PartControl, double> FadeOutProperty =
            AvaloniaProperty.RegisterDirect<PartControl, double>(
                nameof(FadeOut),
                o => o.FadeOut,
                (o, v) => o.FadeOut = v);
        public static readonly DirectProperty<PartControl, double> PianoRollViewTickOffsetProperty =
            AvaloniaProperty.RegisterDirect<PartControl, double>(
                nameof(PianoRollViewTickOffset),
                o => o.PianoRollViewTickOffset,
                (o, v) => o.PianoRollViewTickOffset = v);
        public static readonly DirectProperty<PartControl, double> PianoRollViewViewportTicksProperty =
            AvaloniaProperty.RegisterDirect<PartControl, double>(
                nameof(PianoRollViewViewportTicks),
                o => o.PianoRollViewViewportTicks,
                (o, v) => o.PianoRollViewViewportTicks = v);

        // Tick width in pixel.
        public double TickWidth {
            get => tickWidth;
            set => SetAndRaise(TickWidthProperty, ref tickWidth, value);
        }
        public double TrackHeight {
            get => trackHeight;
            set => SetAndRaise(TrackHeightProperty, ref trackHeight, value);
        }
        public double ViewWidth {
            get { return viewWidth; }
            set { SetAndRaise(ViewWidthProperty, ref viewWidth, value); }
        }
        public double TickOffset {
            get { return tickOffset; }
            set { SetAndRaise(TickOffsetProperty, ref tickOffset, value); }
        }
        public Point Offset {
            get { return offset; }
            set { SetAndRaise(OffsetProperty, ref offset, value); }
        }
        public string Text {
            get { return text; }
            set { SetAndRaise(TextProperty, ref text, value); }
        }
        public bool Selected {
            get { return selected; }
            set { SetAndRaise(SelectedProperty, ref selected, value); }
        }
        public double FadeIn {
            get { return fadeIn * TickWidth; }
            set { SetAndRaise(FadeInProperty, ref fadeIn, value); }
        }
        public double FadeOut {
            get { return Width - (fadeOut * TickWidth); }
            set { SetAndRaise(FadeOutProperty, ref fadeOut, value); }
        }
        public double PianoRollViewTickOffset {
            get => pianoRollViewTickOffset;
            set => SetAndRaise(PianoRollViewTickOffsetProperty, ref pianoRollViewTickOffset, value);
        }
        public double PianoRollViewViewportTicks {
            get => pianoRollViewViewportTicks;
            set => SetAndRaise(PianoRollViewViewportTicksProperty, ref pianoRollViewViewportTicks, value);
        }

        private double tickWidth;
        private double trackHeight;
        private double viewWidth;
        private double tickOffset;
        private Point offset;
        private string text = string.Empty;
        private bool selected;
        private double fadeIn;
        private double fadeOut;
        private double pianoRollViewTickOffset;
        private double pianoRollViewViewportTicks;
        private Geometry pointGeometry;

        public readonly UPart part;
        private readonly PartsCanvas partsCanvas;
        private readonly Pen notePen = new Pen(Brushes.White, 3);
        private readonly Pen fadePen = new Pen(Brushes.White);
        private List<IDisposable> unbinds = new List<IDisposable>();
        private WriteableBitmap? bitmap;
        private int[] bitmapData;
        private (RenderPhrase phrase, bool rendered)[] phraseBlocks = [];

        public PartControl(UPart part, PartsCanvas canvas) {
            this.part = part;
            partsCanvas = canvas;
            bitmapData = new int[0];
            pointGeometry = new EllipseGeometry(new Rect(0, 0, 6, 6));

            unbinds.Add(this.Bind(TickWidthProperty, canvas.GetObservable(PartsCanvas.TickWidthProperty)));
            unbinds.Add(this.Bind(TrackHeightProperty, canvas.GetObservable(PartsCanvas.TrackHeightProperty)));
            unbinds.Add(this.Bind(WidthProperty, canvas.GetObservable(PartsCanvas.TickWidthProperty).Select(tickWidth => tickWidth * part.Duration)));
            unbinds.Add(this.Bind(HeightProperty, canvas.GetObservable(PartsCanvas.TrackHeightProperty)));
            unbinds.Add(this.Bind(OffsetProperty, canvas.WhenAnyValue(x => x.TickOffset, x => x.TrackOffset,
                (tick, track) => new Point(-tick * TickWidth, -track * TrackHeight))));
            unbinds.Add(this.Bind(ViewWidthProperty, canvas.WhenAnyValue(x => x.Bounds).Select(bounds => bounds.Width)));
            unbinds.Add(this.Bind(TickOffsetProperty, canvas.WhenAnyValue(x => x.TickOffset).Select(tickOffset => tickOffset)));
            unbinds.Add(this.Bind(PianoRollViewTickOffsetProperty, canvas.GetObservable(PartsCanvas.PianoRollViewTickOffsetProperty)));
            unbinds.Add(this.Bind(PianoRollViewViewportTicksProperty, canvas.GetObservable(PartsCanvas.PianoRollViewViewportTicksProperty)));

            SetPosition();
            Refersh();

            if (part is UWavePart wavePart) {
                var scheduler = TaskScheduler.FromCurrentSynchronizationContext();
                wavePart.Peaks.ContinueWith((task) => {
                    if (task.IsFaulted) {
                        Log.Error(task.Exception, "Failed to build peaks");
                    } else {
                        InvalidateVisual();
                    }
                }, CancellationToken.None, TaskContinuationOptions.None, scheduler);
            }
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
            base.OnPropertyChanged(change);
            if (change.Property == OffsetProperty ||
                change.Property == TrackHeightProperty ||
                change.Property == TickWidthProperty) {
                SetPosition();
            }
            // Waveforms sample TickOffset/ViewWidth; keep them redrawing on track
            // scroll/resize. Piano-roll viewport props stay out: PartsCanvas already
            // invalidates the open part, and redrawing every part on those changes
            // made piano-roll scroll janky.
            if (change.Property == TickOffsetProperty ||
                change.Property == ViewWidthProperty) {
                InvalidateVisual();
            }
            if (change.Property == SelectedProperty ||
                change.Property == TextProperty || 
                change.Property == FadeInProperty ||
                change.Property == FadeOutProperty) {
                InvalidateVisual();
            }
        }

        public void SetPosition() {
            Canvas.SetLeft(this, Offset.X + part.position * tickWidth);
            Canvas.SetTop(this, Offset.Y + part.trackNo * trackHeight);
        }

        public bool TryHitViewport(Point localPoint) {
            if (partsCanvas.PianoRollOpenPart != part || pianoRollViewViewportTicks <= 0 || tickWidth <= 0) {
                return false;
            }
            const double inset = 1;
            double outerWidth = Math.Max(0, Width - 2 * inset);
            double vpLeft = Math.Max(0, pianoRollViewTickOffset * tickWidth);
            double vpRight = Math.Min(outerWidth,
                (pianoRollViewTickOffset + pianoRollViewViewportTicks) * tickWidth);
            if (vpRight <= vpLeft + 1) {
                return false;
            }
            return localPoint.X >= inset + vpLeft && localPoint.X <= inset + vpRight
                && localPoint.Y >= inset && localPoint.Y <= Height - inset;
        }

        public void SetSize() {
            Width = TickWidth * part.Duration;
            Height = trackHeight;
        }

        public void Refersh() {
            Text = part.DisplayName;
            if (part is UWavePart wavePart) {
                FadeIn = wavePart.fadein;
                FadeOut = wavePart.fadeout;
            }
            // Part edits invalidate render progress; blank until phrases rebuild.
            if (part is UVoicePart) {
                SetAllBlocks(false);
            }
        }

        public override void Render(DrawingContext context) {
            bool isOpenInPianoRoll = partsCanvas.PianoRollOpenPart == part;
            const byte fillAlphaFull = 200;
            const byte fillAlphaDim = 78;
            const byte grayAlphaFull = 0xC8;
            const byte grayAlphaDim = 0x52;
            const double cornerRadius = 5;

            const byte accentAlphaFull = 255;
            const byte accentAlphaDim = 210;

            Color bodyRgb = Color.FromRgb(0x52, 0x52, 0x52);
            byte accentAlpha = isOpenInPianoRoll ? accentAlphaFull : accentAlphaDim;
            byte bodyAlpha = isOpenInPianoRoll ? grayAlphaFull : grayAlphaDim;
            if (part != null) {
                var project = DocManager.Inst.Project;
                if (project != null && part.trackNo >= 0 && part.trackNo < project.tracks.Count) {
                    var track = project.tracks[part.trackNo];
                    var note = ThemeManager.GetTrackColor(track.TrackColor).NoteColor.Color;
                    bodyRgb = Color.FromRgb(note.R, note.G, note.B);
                    bodyAlpha = isOpenInPianoRoll ? fillAlphaFull : fillAlphaDim;
                }
            }
            bool showRenderProgress = Preferences.Default.RenderStatusInTrackBar
                && part is UVoicePart { notes.Count: > 0 };
            // Dim base while phrase blocks fill in; keep Lunai track-color body otherwise.
            byte baseAlpha = showRenderProgress
                ? (byte)Math.Max(24, bodyAlpha / 3)
                : bodyAlpha;
            var bodyBrush = new SolidColorBrush(Color.FromArgb(bodyAlpha, bodyRgb.R, bodyRgb.G, bodyRgb.B));
            var baseBrush = showRenderProgress
                ? new SolidColorBrush(Color.FromArgb(baseAlpha, bodyRgb.R, bodyRgb.G, bodyRgb.B))
                : bodyBrush;
            var accentBrush = new SolidColorBrush(Color.FromArgb(accentAlpha, bodyRgb.R, bodyRgb.G, bodyRgb.B));
            var borderPen = new Pen(accentBrush, 1);

            const double inset = 1;
            var outerRect = new Rect(inset, inset, Math.Max(0, Width - 2 * inset), Math.Max(0, Height - 2 * inset));
            var borderRect = new Rect(inset + 0.5, inset + 0.5,
                Math.Max(0, Width - 2 * inset - 1), Math.Max(0, Height - 2 * inset - 1));
            context.DrawRectangle(baseBrush, null,
                new RoundedRect(outerRect, new CornerRadius(cornerRadius)));

            if (showRenderProgress && part is UVoicePart) {
                // Phrase blocks over the dim base — filled segments = rendered.
                List<Rect> phraseRects = [];
                for (int i = 0; i < phraseBlocks.Length; i++) {
                    var (phrase, rendered) = phraseBlocks[i];
                    int startX = (int)phraseRects.ElementAtOrDefault(i - 1).Right;
                    int endX = (int)((phrase.end - part.position) * tickWidth);
                    if (i == phraseBlocks.Length - 1) {
                        endX = (int)((part.End - part.position) * tickWidth);
                    }
                    var rect = new Rect(new Point(startX, inset), new Point(endX, Height - inset));
                    phraseRects.Add(rect);
                    if (!rendered) {
                        continue;
                    }
                    CornerRadius radii = i == 0 && i == phraseBlocks.Length - 1
                        ? new CornerRadius(cornerRadius)
                        : i == 0
                            ? new CornerRadius(cornerRadius, 0, 0, cornerRadius)
                            : i == phraseBlocks.Length - 1
                                ? new CornerRadius(0, cornerRadius, cornerRadius, 0)
                                : default;
                    context.DrawRectangle(bodyBrush, null, new RoundedRect(rect, radii));
                }
            }

            double visibleLeft = 0;
            double visibleRight = outerRect.Width;
            if (part != null) {
                visibleLeft = Math.Max(0, (TickOffset - part.position) * TickWidth);
                visibleRight = Math.Min(outerRect.Width,
                    (TickOffset + ViewWidth / TickWidth - part.position) * TickWidth);
            }
            context.DrawRectangle(null, borderPen, borderRect, cornerRadius, cornerRadius);

            if (isOpenInPianoRoll && pianoRollViewViewportTicks > 0) {
                double vpLeft = Math.Max(0, pianoRollViewTickOffset * tickWidth);
                double vpRight = Math.Min(outerRect.Width,
                    (pianoRollViewTickOffset + pianoRollViewViewportTicks) * tickWidth);
                if (vpRight > vpLeft + 1) {
                    var vpRect = new Rect(outerRect.X + vpLeft, outerRect.Y, vpRight - vpLeft, outerRect.Height);
                    var vpFill = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255));
                    var vpPen = new Pen(Brushes.White, 2);
                    context.DrawRectangle(vpFill, vpPen, new RoundedRect(vpRect, new CornerRadius(3)));
                }
            }

            var textLayout = TextLayoutCache.Get(Text, Brushes.White, 12);
            double labelX = outerRect.X + Math.Max(6, visibleLeft + 6);
            double labelY = outerRect.Y + (Math.Min(18, outerRect.Height) - textLayout.Height) * 0.5;
            using (var clip = context.PushClip(new Rect(visibleLeft, 0, Math.Max(0, visibleRight - visibleLeft), outerRect.Height))) {
                using (var state = context.PushTransform(Matrix.CreateTranslation(labelX, labelY))) {
                    textLayout.Draw(context, new Point());
                }
            }

            if (part == null) {
                return;
            }
            if (part is UVoicePart voicePart && voicePart.notes.Count > 0) {
                // Notes
                int maxTone = voicePart.notes.Max(note => note.tone);
                int minTone = voicePart.notes.Min(note => note.tone);
                if (maxTone - minTone < 52) {
                    int additional = (52 - (maxTone - minTone)) / 2;
                    minTone -= additional;
                    maxTone += additional;
                }
                using var pushedState = context.PushTransform(Matrix.CreateScale(1, trackHeight / (maxTone - minTone)));
                foreach (var note in voicePart.notes) {
                    var start = new Point((int)(note.position * tickWidth), maxTone - note.tone);
                    var end = new Point((int)(note.End * tickWidth), maxTone - note.tone);
                    context.DrawLine(notePen, start, end);
                }
            } else if (part is UWavePart wavePart) {
                // Waveform
                try {
                    DrawWaveform(wavePart, GetBitmap(ViewWidth));
                    if (bitmap != null) {
                        var srcRect = Bounds.WithY(0);
                        var dstRect = Bounds.WithX(inset).WithY(inset).WithWidth(Math.Max(0, Bounds.Width - 2 * inset)).WithHeight(Math.Max(0, Bounds.Height - 2 * inset));
                        context.DrawImage(bitmap, srcRect, dstRect);
                    }
                } catch (Exception e) {
                    Log.Error(e, "failed to draw bitmap");
                }
                // Fade
                var brush = Brushes.White;
                var pen = Selected ? ThemeManager.AccentPen2 : ThemeManager.AccentPen1;
                using (var state = context.PushTransform(Matrix.CreateTranslation(FadeIn, 0))) {
                    context.DrawGeometry(brush, pen, pointGeometry);
                }
                if (wavePart.fadein > 0) {
                    context.DrawLine(fadePen, new Point(2, Height - 2), new Point(FadeIn + 1, 2));
                }
                using (var state = context.PushTransform(Matrix.CreateTranslation(FadeOut - 6, 0))) {
                    context.DrawGeometry(brush, pen, pointGeometry);
                }
                if (wavePart.fadeout > 0) {
                    context.DrawLine(fadePen, new Point(Width - 1, Height - 2), new Point(FadeOut, 2));
                }
            }
        }

        private WriteableBitmap GetBitmap(double width) {
            int w = 128 * (int)(width / 128 + 1);
            if (bitmap == null || bitmap.Size.Width < w) {
                bitmap?.Dispose();
                var size = new PixelSize(w, (int)ViewConstants.TrackHeightMax);
                Log.Information($"created bitmap {size}");
                bitmap = new WriteableBitmap(
                    size, new Vector(96, 96),
                    Avalonia.Platform.PixelFormat.Rgba8888,
                    Avalonia.Platform.AlphaFormat.Unpremul);
                bitmapData = new int[size.Width * size.Height];
            }
            return bitmap;
        }

        private void DrawWaveform(UWavePart wavePart, WriteableBitmap bitmap) {
            if (wavePart.Peaks == null ||
                !wavePart.Peaks.IsCompletedSuccessfully ||
                wavePart.Peaks.Result == null) {
                return;
            }
            var wholePeaks = wavePart.Peaks.Result;
            int skipCount = (int)(wavePart.peaksSampleRate * wavePart.GetSkipMs(Core.DocManager.Inst.Project) / 1000);
            if (skipCount >= wholePeaks[0].Length) return;

            double height = TrackHeight;
            double monoChnlAmp = (height - 4.0) / 2;
            double stereoChnlAmp = (height - 6.0) / 4;

            var timeAxis = Core.DocManager.Inst.Project.timeAxis;
            DiscreteSignal[] peaks = new DiscreteSignal[wholePeaks.Length];
            for (int i = 0; i < wholePeaks.Length; i++) {
                var newSamples = wholePeaks[i].Samples.Skip(skipCount);
                peaks[i] = new DiscreteSignal(wavePart.peaksSampleRate, newSamples);
            }
            int x = 0;
            if (TickOffset <= wavePart.position) {
                // Part starts in or to the right of view.
                x = (int)(TickWidth * (wavePart.position - TickOffset));
            }
            int posTick = (int)(TickOffset + x / TickWidth);
            double posMs = timeAxis.TickPosToMsPos(posTick);
            double offsetMs = timeAxis.TickPosToMsPos(wavePart.position);
            int sampleIndex = (int)(wavePart.peaksSampleRate * (posMs - offsetMs) * 0.001);
            sampleIndex = Math.Clamp(sampleIndex, 0, peaks[0].Length);
            using (var frameBuffer = bitmap.Lock()) {
                Array.Clear(bitmapData, 0, bitmapData.Length);
                while (x < frameBuffer.Size.Width) {
                    if (posTick >= wavePart.position + wavePart.Duration) {
                        break;
                    }
                    int nextPosTick = (int)(TickOffset + (x + 1) / TickWidth);
                    double nexPosMs = timeAxis.TickPosToMsPos(nextPosTick);
                    int nextSampleIndex = (int)(wavePart.peaksSampleRate * (nexPosMs - offsetMs) * 0.001);
                    nextSampleIndex = Math.Clamp(nextSampleIndex, 0, peaks[0].Length);
                    if (nextSampleIndex > sampleIndex) {
                        for (int i = 0; i < peaks.Length; ++i) {
                            var segment = new ArraySegment<float>(peaks[i].Samples, sampleIndex, nextSampleIndex - sampleIndex);
                            float min = segment.Min();
                            float max = segment.Max();
                            double ySpan = peaks.Length == 1 ? monoChnlAmp : stereoChnlAmp;
                            double yOffset = i == 1 ? monoChnlAmp : 0;
                            DrawPeak(bitmapData, frameBuffer.Size.Width, x,
                                (int)(ySpan * (1 + -min) + yOffset) + 2,
                                (int)(ySpan * (1 + -max) + yOffset) + 2);
                        }
                    }
                    x++;
                    posTick = nextPosTick;
                    posMs = nexPosMs;
                    sampleIndex = nextSampleIndex;
                }
                Marshal.Copy(bitmapData, 0, frameBuffer.Address, bitmapData.Length);
            }
        }

        private void DrawPeak(int[] data, int width, int x, int y1, int y2) {
            const int white = unchecked((int)0xFFFFFFFF);
            if (y1 > y2) {
                int temp = y2;
                y2 = y1;
                y1 = temp;
            }
            for (var y = y1; y <= y2; ++y) {
                data[x + width * y] = white;
            }
        }

        /// <summary>
        /// Updates render status of phrases within range, then updates visual if any change.
        /// startTick/endTick use the absolute position of the phrase.
        /// </summary>
        public void UpdateRenderStatus(int startTick, int endTick, bool rendered) {
            if (startTick > endTick) {
                Log.Error("StartTick cannot be greater than EndTick in UpdateRenderStatus, update ignored.");
                return;
            }

            bool triggerRender = false;
            for (var index = 0; index < phraseBlocks.Length; index++) {
                var phraseBlock = phraseBlocks[index];
                if (phraseBlock.phrase.end <= startTick || endTick <= phraseBlock.phrase.position) {
                    continue;
                }
                if (phraseBlock.rendered == rendered) {
                    continue;
                }
                phraseBlocks[index] = phraseBlock with { rendered = rendered };
                triggerRender = true;
            }

            if (triggerRender) {
                InvalidateVisual();
            }
        }

        /// <summary>
        /// Updates render status of phrase if exists, then updates visual if it changes.
        /// </summary>
        public void UpdateRenderStatus(RenderPhrase phrase, bool rendered) {
            for (var i = 0; i < phraseBlocks.Length; i++) {
                var phraseBlock = phraseBlocks[i];
                if (phraseBlock.phrase.hash != phrase.hash && !ReferenceEquals(phraseBlock.phrase, phrase)) {
                    continue;
                }
                if (phraseBlock.rendered == rendered) {
                    return;
                }
                phraseBlocks[i] = phraseBlock with { rendered = rendered };
                InvalidateVisual();
                return;
            }
        }

        /// <summary>
        /// Updates the phrase blocks based on voicePart, retains render status if phrases match.
        /// </summary>
        public bool RebuildPhraseBlocks(UVoicePart voicePart) {
            bool requiresVisualUpdate = voicePart.renderPhrases.Count != phraseBlocks.Length;
            var renderedHashes = new HashSet<ulong>(
                phraseBlocks.Where(b => b.rendered).Select(b => b.phrase.hash));
            var newPhraseBlocks = new List<(RenderPhrase phrase, bool rendered)>(voicePart.renderPhrases.Count);
            foreach (var partPhrase in voicePart.renderPhrases) {
                bool wasRendered = renderedHashes.Contains(partPhrase.hash);
                newPhraseBlocks.Add((partPhrase, wasRendered));
            }
            if (!requiresVisualUpdate) {
                for (int i = 0; i < newPhraseBlocks.Count; i++) {
                    if (phraseBlocks[i].phrase.hash != newPhraseBlocks[i].phrase.hash
                        || phraseBlocks[i].rendered != newPhraseBlocks[i].rendered) {
                        requiresVisualUpdate = true;
                        break;
                    }
                }
            }
            phraseBlocks = newPhraseBlocks.ToArray();
            return requiresVisualUpdate;
        }

        /// <summary>
        /// Sets all phrase blocks, and updates visual.
        /// </summary>
        public void SetAllBlocks(bool state) {
            UpdateRenderStatus(part.position, part.End, state);
        }

        public void Report(int value) {
        }

        public void Dispose() {
            bitmap?.Dispose();
            unbinds.ForEach(u => u.Dispose());
            unbinds.Clear();
        }
    }
}
