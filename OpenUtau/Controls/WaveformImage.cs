using System;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using OpenUtau.App;
using OpenUtau.App.ViewModels;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using ReactiveUI;
using Serilog;

namespace OpenUtau.App.Controls {
    class WaveformImage : Control {
        const float DiffSingerAmplitudeGain = 1.2f;
        const float VerticalFill = 0.92f;
        public static readonly DirectProperty<WaveformImage, double> TickWidthProperty =
            AvaloniaProperty.RegisterDirect<WaveformImage, double>(
                nameof(TickWidth),
                o => o.TickWidth,
                (o, v) => o.TickWidth = v);
        public static readonly DirectProperty<WaveformImage, double> TickOffsetProperty =
            AvaloniaProperty.RegisterDirect<WaveformImage, double>(
                nameof(TickOffset),
                o => o.TickOffset,
                (o, v) => o.TickOffset = v);
        public static readonly DirectProperty<WaveformImage, bool> ShowWaveformProperty =
            AvaloniaProperty.RegisterDirect<WaveformImage, bool>(
                nameof(ShowWaveform),
                o => o.ShowWaveform,
                (o, v) => o.ShowWaveform = v);

        public double TickWidth {
            get => tickWidth;
            set => SetAndRaise(TickWidthProperty, ref tickWidth, value);
        }
        public double TickOffset {
            get { return tickOffset; }
            set { SetAndRaise(TickOffsetProperty, ref tickOffset, value); }
        }
        public bool ShowWaveform {
            get { return showWaveform; }
            set { SetAndRaise(ShowWaveformProperty, ref showWaveform, value); }
        }

        private double tickWidth;
        private double tickOffset;
        private bool showWaveform;

        private WriteableBitmap? bitmap;
        private float[] sampleData = new float[0];
        private float[] mixScratch = new float[0];
        private int sampleCount;
        private int[] bitmapData = new int[0];
        private DateTime mixUnlockTime = DateTime.MinValue;
        private bool wasRendering = false;

        public WaveformImage() {
            PhraseWaveformCache.Changed += OnPhraseWaveformCacheChanged;
            MessageBus.Current.Listen<WaveformRefreshEvent>()
                .Subscribe(e => RequestRedraw());
            MessageBus.Current.Listen<ThemeChangedEvent>()
                .Subscribe(e => RequestRedraw());
        }

        void OnPhraseWaveformCacheChanged() {
            RequestRedraw();
        }

        void RequestRedraw() {
            Avalonia.Threading.Dispatcher.UIThread.Post(
                InvalidateVisual,
                Avalonia.Threading.DispatcherPriority.Render);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
            base.OnPropertyChanged(change);
            if (change.Property == DataContextProperty ||
                change.Property == TickWidthProperty ||
                change.Property == TickOffsetProperty ||
                change.Property == ShowWaveformProperty) {
                InvalidateVisual();
            }
        }

        public override void Render(DrawingContext context) {
            if (DataContext == null || double.IsNaN(((NotesViewModel)DataContext).TickOffset)) {
                return;
            }
            var bitmap = GetBitmap();
            if (bitmap != null) {
                Array.Clear(bitmapData, 0, bitmapData.Length);
                var viewModel = (NotesViewModel?)DataContext;
                if (viewModel != null && ShowWaveform &&
                    viewModel.TickWidth > ViewConstants.PianoRollTickWidthShowDetails) {
                    var project = viewModel.Project;
                    var part = viewModel.Part;
                    if (project != null && part != null) {
                        double leftMs = project.timeAxis.TickPosToMsPos(viewModel.TickOrigin + viewModel.TickOffset);
                        double rightMs = project.timeAxis.TickPosToMsPos(viewModel.TickOrigin + viewModel.TickOffset + viewModel.ViewportTicks);
                        int samplePos = (int)(leftMs * 44100 / 1000) * 2;
                        sampleCount = (int)((rightMs - leftMs) * 44100 / 1000) * 2;

                        if (sampleData.Length < sampleCount) {
                            Array.Resize(ref sampleData, sampleCount);
                        }
                        if (mixScratch.Length < sampleCount) {
                            Array.Resize(ref mixScratch, sampleCount);
                        }

                        bool needsAnotherFrame = false;
                        Array.Clear(sampleData, 0, sampleData.Length);

                        FillFromPhraseCache(part.trackNo, leftMs, ref needsAnotherFrame);

                        bool hasCachedPhrases = PhraseWaveformCache.GetForTrack(part.trackNo).Any();
                        if (part.Mix != null && !hasCachedPhrases) {
                            Array.Clear(mixScratch, 0, sampleCount);
                            part.Mix.Mix(samplePos, mixScratch, 0, sampleCount);
                            Array.Copy(mixScratch, sampleData, sampleCount);
                        }

                        bool isRendering = OpenUtau.Core.PlaybackManager.Inst.StartingToPlay;
                        if (wasRendering && !isRendering) {
                            mixUnlockTime = DateTime.Now;
                        }
                        wasRendering = isRendering;

                        float snapEase = 1.0f;
                        if (part.RenderMixComplete) {
                            double snapAgeMs = (DateTime.Now - mixUnlockTime).TotalMilliseconds;
                            double snapProgress = Math.Clamp(snapAgeMs / PhraseWaveformCache.FadeDurationMs, 0.0, 1.0);
                            snapEase = 1.0f - (float)Math.Pow(1.0 - snapProgress, 3);
                            if (snapProgress < 1.0) needsAnotherFrame = true;
                        }

                        float amplitudeGain = GetAmplitudeGain(part, project);
                        int startSample = 0;
                        for (int i = 0; i < bitmap.PixelSize.Width; ++i) {
                            double endTick = viewModel.TickOrigin + viewModel.TickOffset + (i + 1.0) / viewModel.TickWidth;
                            double endMs = project.timeAxis.TickPosToMsPos(endTick);
                            int endSample = Math.Clamp((int)((endMs - leftMs) * 44100 / 1000) * 2, 0, sampleCount);

                            if (endSample > startSample) {
                                float rawMin = float.MaxValue;
                                float rawMax = float.MinValue;
                                for (int s = startSample; s < endSample; s++) {
                                    float val = sampleData[s];
                                    if (val < rawMin) rawMin = val;
                                    if (val > rawMax) rawMax = val;
                                }
                                if (rawMin == float.MaxValue) rawMin = 0;
                                if (rawMax == float.MinValue) rawMax = 0;
                                rawMin = Math.Clamp(rawMin * amplitudeGain * snapEase, -1f, 1f);
                                rawMax = Math.Clamp(rawMax * amplitudeGain * snapEase, -1f, 1f);
                                float min = 0.5f + rawMin * 0.5f * VerticalFill;
                                float max = 0.5f + rawMax * 0.5f * VerticalFill;
                                float yMax = Math.Clamp(max * bitmap.PixelSize.Height, 0, bitmap.PixelSize.Height - 1);
                                float yMin = Math.Clamp(min * bitmap.PixelSize.Height, 0, bitmap.PixelSize.Height - 1);
                                if (Math.Abs(yMax - yMin) > 0.01) {
                                    DrawPeak(bitmapData, bitmap.PixelSize.Width, i, (int)Math.Round(yMin), (int)Math.Round(yMax));
                                }
                            }
                            startSample = endSample;
                        }

                        if (needsAnotherFrame) {
                            RequestRedraw();
                        }
                    }
                }
                using (var frameBuffer = bitmap.Lock()) {
                    Marshal.Copy(bitmapData, 0, frameBuffer.Address, bitmapData.Length);
                }
            }
            base.Render(context);
            if (bitmap != null) {
                var rect = Bounds.WithX(0).WithY(0);
                context.DrawImage(bitmap, rect, rect);
            }
        }

        void FillFromPhraseCache(int trackNo, double leftMs, ref bool needsAnotherFrame) {
            foreach (var cacheItem in PhraseWaveformCache.GetForTrack(trackNo)) {
                float visualScale = PhraseWaveformCache.GetVisualScale(in cacheItem, ref needsAnotherFrame);
                if (visualScale <= 0.001f) {
                    continue;
                }
                double phraseStartMs = cacheItem.PosMs;
                float[] phraseSamples = cacheItem.WaveformSamples;
                int phraseStartSampleIdx = (int)((phraseStartMs - leftMs) * 44100 / 1000);
                int startJ = Math.Max(0, -phraseStartSampleIdx);
                int endJ = Math.Min(phraseSamples.Length, (sampleCount / 2) - phraseStartSampleIdx);
                for (int j = startJ; j < endJ; j++) {
                    int targetIdx = (phraseStartSampleIdx + j) * 2;
                    float scaledSample = phraseSamples[j] * visualScale;
                    sampleData[targetIdx] += scaledSample;
                    sampleData[targetIdx + 1] += scaledSample;
                }
            }
        }

        static float GetAmplitudeGain(UVoicePart part, UProject project) {
            if (part.trackNo < 0 || part.trackNo >= project.tracks.Count) {
                return 1f;
            }
            return project.tracks[part.trackNo].Singer?.SingerType == USingerType.DiffSinger
                ? DiffSingerAmplitudeGain
                : 1f;
        }

        private WriteableBitmap? GetBitmap() {
            int desiredWidth = (int)Bounds.Width;
            int desiredHeight = (int)Bounds.Height;
            if (desiredWidth == 0 || desiredHeight == 0) {
                return null;
            }
            if (bitmap == null || bitmap.Size.Width < desiredWidth) {
                bitmap?.Dispose();
                var size = new PixelSize(desiredWidth, desiredHeight);
                bitmap = new WriteableBitmap(
                    size, new Vector(96, 96),
                    Avalonia.Platform.PixelFormat.Rgba8888,
                    Avalonia.Platform.AlphaFormat.Unpremul);
                Log.Information($"Created bitmap {size}");
                bitmapData = new int[size.Width * size.Height];
            }
            return bitmap;
        }

        private static int GetWaveformPeakColor() {
            if (Application.Current?.TryFindResource("PianoRollWaveformPeakColor", out var resource) == true
                && resource is Color color) {
                return unchecked((int)(((uint)color.A << 24) | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B));
            }
            return unchecked((int)0x3BFFFFFF);
        }

        private void DrawPeak(int[] data, int width, int x, int y1, int y2) {
            int color = GetWaveformPeakColor();
            if (y1 > y2) {
                int temp = y2;
                y2 = y1;
                y1 = temp;
            }
            if (y2 - y1 > 0.01) {
                for (var y = y1; y <= y2; ++y) {
                    data[x + width * y] = color;
                }
            }
        }
    }
}
