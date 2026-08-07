using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenUtau.Core.DiffSinger;
using Xunit;

namespace OpenUtau.Core {
    public class DiffSingerAcousticRetakeTest {
        [Fact]
        public void PadMaskExpandsChangedFrames() {
            var mask = new[] { false, false, true, false, false };
            var padded = DiffSingerAcousticRetake.PadMask(mask, 1);
            Assert.Equal(new[] { false, true, true, true, false }, padded);
        }

        [Fact]
        public void PadMaskZeroLeavesMaskUnchanged() {
            var mask = new[] { false, true, false };
            Assert.Equal(mask, DiffSingerAcousticRetake.PadMask(mask, 0));
        }

        [Fact]
        public void BuildRetakeMaskMarksOnlyChangedConditionFrames() {
            var previous = State(
                conditions: new() { ["f0"] = new[] { 100f, 100f, 100f, 200f } },
                mel: Mel(4, 2, 1f),
                frameMs: 64f);
            var current = new Dictionary<string, float[]> {
                ["f0"] = new[] { 100f, 110f, 100f, 200f },
            };

            var mask = DiffSingerAcousticRetake.BuildRetakeMask(previous, current, 4, 64f);

            Assert.NotNull(mask);
            // PadMs=64 with frameMs=64 → pad 1 around frame 1
            Assert.Equal(new[] { true, true, true, false }, mask);
        }

        [Fact]
        public void BuildRetakeMaskReturnsAllFalseWhenUnchanged() {
            var f0 = new[] { 100f, 200f, 300f };
            var previous = State(
                conditions: new() { ["f0"] = f0 },
                mel: Mel(3, 2, 1f));
            var current = new Dictionary<string, float[]> {
                ["f0"] = (float[])f0.Clone(),
            };

            var mask = DiffSingerAcousticRetake.BuildRetakeMask(previous, current, 3, 10f);

            Assert.NotNull(mask);
            Assert.Equal(new[] { false, false, false }, mask);
        }

        [Fact]
        public void BuildRetakeMaskReturnsNullForIncompatibleFrameCount() {
            var previous = State(
                conditions: new() { ["f0"] = new[] { 1f, 2f } },
                mel: Mel(2, 2, 1f),
                totalFrames: 2);
            var current = new Dictionary<string, float[]> {
                ["f0"] = new[] { 1f, 2f, 3f },
            };

            Assert.Null(DiffSingerAcousticRetake.BuildRetakeMask(previous, current, 3, 10f));
        }

        [Fact]
        public void BuildRetakeMaskFullRetakeWhenMostFramesChange() {
            var previous = State(
                conditions: new() { ["f0"] = new[] { 1f, 2f, 3f, 4f } },
                mel: Mel(4, 2, 1f));
            var current = new Dictionary<string, float[]> {
                ["f0"] = new[] { 10f, 20f, 30f, 40f },
            };

            var mask = DiffSingerAcousticRetake.BuildRetakeMask(previous, current, 4, 10f);

            Assert.NotNull(mask);
            Assert.All(mask, x => Assert.True(x));
        }

        [Fact]
        public void HardComposeMelPreservesUnmaskedFrames() {
            var previous = Mel(3, 2, frame => frame == 0 ? 1f : frame == 1 ? 2f : 3f);
            var predicted = Mel(3, 2, _ => 99f);
            var mask = new[] { false, true, false };

            var result = DiffSingerAcousticRetake.HardComposeMel(previous, predicted, mask);

            Assert.Equal(1f, result[0, 0, 0]);
            Assert.Equal(1f, result[0, 0, 1]);
            Assert.Equal(99f, result[0, 1, 0]);
            Assert.Equal(99f, result[0, 1, 1]);
            Assert.Equal(3f, result[0, 2, 0]);
            Assert.Equal(3f, result[0, 2, 1]);
        }

        [Fact]
        public void HardComposeMelFallsBackToPredictedOnIncompatibleLayout() {
            var previous = new DenseTensor<float>(new float[4], new[] { 1, 2, 2 });
            var predicted = new DenseTensor<float>(new[] { 1f, 2f, 3f, 4f, 5f, 6f }, new[] { 1, 3, 2 });
            var mask = new[] { true, false, true };

            var result = DiffSingerAcousticRetake.HardComposeMel(previous, predicted, mask);

            Assert.Equal(3, result.Dimensions[1]);
            Assert.Equal(1f, result[0, 0, 0]);
            Assert.Equal(6f, result[0, 2, 1]);
        }

        [Fact]
        public void AcousticRetakeStateCacheEvictsLeastRecentlyUsed() {
            var cache = new AcousticRetakeStateCache(2);
            cache.Set(1, State(mel: Mel(1, 1, 1f)));
            cache.Set(2, State(mel: Mel(1, 1, 2f)));
            Assert.True(cache.TryGetValue(1, out _));

            cache.Set(3, State(mel: Mel(1, 1, 3f)));

            Assert.Equal(2, cache.Count);
            Assert.True(cache.TryGetValue(1, out _));
            Assert.False(cache.TryGetValue(2, out _));
            Assert.True(cache.TryGetValue(3, out _));
        }

        [Fact]
        public void FramesToSampleRangesMergesContiguousFrames() {
            var mask = new[] { false, true, true, false, true };
            var ranges = DiffSingerAcousticRetake.FramesToSampleRanges(mask, hopSize: 10, sampleCount: 50);

            Assert.Equal(new[] { (10, 30), (40, 50) }, ranges);
        }

        [Fact]
        public void FramesToSampleRangesExtendsLastGroupToSampleCount() {
            var mask = new[] { true, true };
            var ranges = DiffSingerAcousticRetake.FramesToSampleRanges(mask, hopSize: 10, sampleCount: 23);

            Assert.Equal(new[] { (0, 23) }, ranges);
        }

        [Fact]
        public void HardComposeSamplesPreservesUnmaskedRegions() {
            var previous = new float[] { 1, 1, 1, 1, 1, 1 };
            var predicted = new float[] { 9, 9, 9, 9, 9, 9 };
            var mask = new[] { false, true, false };

            var result = DiffSingerAcousticRetake.HardComposeSamples(
                previous, predicted, mask, hopSize: 2, crossfadeSamples: 0);

            Assert.Equal(new float[] { 1, 1, 9, 9, 1, 1 }, result);
        }

        [Fact]
        public void HardComposeSamplesCrossfadesEdges() {
            var previous = new float[12];
            var predicted = new float[12];
            Array.Fill(predicted, 1f);
            var mask = new[] { false, true, true, false, false, false };

            var result = DiffSingerAcousticRetake.HardComposeSamples(
                previous, predicted, mask, hopSize: 2, crossfadeSamples: 2);

            // Range [2, 6), soft gate ±2 → blends [0, 8)
            Assert.Equal(0f, result[0]);
            Assert.True(result[1] > 0f && result[1] < 1f);
            Assert.True(result[3] > 0.85f);
            Assert.True(result[4] > 0.85f);
            Assert.True(result[6] > 0f && result[6] < 1f);
            Assert.Equal(0f, result[8]);
            Assert.Equal(0f, result[11]);
        }

        [Fact]
        public void DefaultCrossfadeSamplesIsAboutSixtyMs() {
            int fade = DiffSingerAcousticRetake.DefaultCrossfadeSamples(hopSize: 512, sampleRate: 44100);
            Assert.True(fade >= 1024); // max(60ms≈2646, 2*hop)
            Assert.InRange(fade, 2500, 2800);
        }

        [Fact]
        public void EqualPowerInIsMonotonic() {
            Assert.Equal(0f, DiffSingerAcousticRetake.EqualPowerIn(0f));
            Assert.Equal(1f, DiffSingerAcousticRetake.EqualPowerIn(1f), 5);
            Assert.True(DiffSingerAcousticRetake.EqualPowerIn(0.5f) > 0.7f);
        }

        [Fact]
        public void HardComposeSamplesFallsBackOnLengthMismatch() {
            var previous = new float[] { 1, 2 };
            var predicted = new float[] { 9, 9, 9 };
            var mask = new[] { true, false };

            var result = DiffSingerAcousticRetake.HardComposeSamples(
                previous, predicted, mask, hopSize: 1, crossfadeSamples: 0);

            Assert.Equal(predicted, result);
        }

        [Fact]
        public void HardComposeSamplesReturnsPreviousWhenMaskAllFalse() {
            var previous = new float[] { 1, 2, 3 };
            var predicted = new float[] { 9, 9, 9 };
            var mask = new[] { false, false, false };

            var result = DiffSingerAcousticRetake.HardComposeSamples(
                previous, predicted, mask, hopSize: 1, crossfadeSamples: 0);

            Assert.Equal(previous, result);
            Assert.NotSame(previous, result);
        }

        static AcousticRetakeState State(
            Dictionary<string, float[]> conditions = null,
            Tensor<float> mel = null,
            int? totalFrames = null,
            float frameMs = 10f,
            float[] rawSamples = null,
            int hopSize = 0) {
            mel ??= Mel(2, 2, 0f);
            int frames = totalFrames ?? mel.Dimensions[1];
            int bins = mel.Dimensions[2];
            return new AcousticRetakeState(
                conditions ?? new Dictionary<string, float[]>(),
                DenseCopy(mel),
                mel.Dimensions.ToArray(),
                frames,
                bins,
                frameMs,
                rawSamples,
                sampleRate: 44100,
                hopSize: hopSize);
        }

        static float[] DenseCopy(Tensor<float> mel) {
            var data = new float[mel.Length];
            for (int i = 0; i < data.Length; i++) {
                data[i] = mel.GetValue(i);
            }
            return data;
        }

        static DenseTensor<float> Mel(int frames, int bins, float fill) {
            var data = new float[frames * bins];
            Array.Fill(data, fill);
            return new DenseTensor<float>(data, new[] { 1, frames, bins });
        }

        static DenseTensor<float> Mel(int frames, int bins, Func<int, float> fillByFrame) {
            var data = new float[frames * bins];
            for (int t = 0; t < frames; t++) {
                float v = fillByFrame(t);
                for (int c = 0; c < bins; c++) {
                    data[t * bins + c] = v;
                }
            }
            return new DenseTensor<float>(data, new[] { 1, frames, bins });
        }
    }
}
