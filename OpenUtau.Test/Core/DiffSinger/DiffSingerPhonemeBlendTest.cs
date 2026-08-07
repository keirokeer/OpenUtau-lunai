using System.Collections.Generic;
using OpenUtau.Core.DiffSinger;
using Xunit;

namespace OpenUtau.Core {
    public class DiffSingerPhonemeBlendTest {
        [Fact]
        public void SupportsFromInputNamesRequiresTokensBAndBlend() {
            Assert.False(DiffSingerPhonemeBlend.SupportsFromInputNames(new[] { "tokens", "blend" }));
            Assert.False(DiffSingerPhonemeBlend.SupportsFromInputNames(new[] { "tokens_b", "f0" }));
            Assert.False(DiffSingerPhonemeBlend.SupportsFromInputNames(System.Array.Empty<string>()));
            Assert.True(DiffSingerPhonemeBlend.SupportsFromInputNames(new[] { "tokens", "tokens_b", "blend" }));
        }

        [Fact]
        public void BuildTokensBReplacesOnlyBlendedIndex() {
            // SP, a, i, SP
            long[] tokens = { 0, 10, 20, 0 };
            var blends = new (string?, int)[] {
                ("o", 50),
                (null, 0),
            };
            var result = DiffSingerPhonemeBlend.BuildTokensB(
                tokens, blends, p => p == "o" ? 99 : null);

            Assert.Equal(new long[] { 0, 99, 20, 0 }, result);
            Assert.Equal(new long[] { 0, 10, 20, 0 }, tokens); // original unchanged
        }

        [Fact]
        public void BuildTokenBlendWeightsZeroWhenEmptyOrZeroWeight() {
            long[] tokens = { 0, 10, 20, 0 };
            var blends = new (string?, int)[] {
                ("", 80),
                ("o", 0),
            };
            var weights = DiffSingerPhonemeBlend.BuildTokenBlendWeights(tokens, blends);
            Assert.Equal(new float[] { 0, 0, 0, 0 }, weights);
        }

        [Fact]
        public void BuildTokenBlendWeightsMapsPercentToUnit() {
            long[] tokens = { 0, 10, 0 };
            var blends = new (string?, int)[] { ("o", 50) };
            var weights = DiffSingerPhonemeBlend.BuildTokenBlendWeights(tokens, blends);
            Assert.Equal(new float[] { 0f, 0.5f, 0f }, weights);
        }

        [Fact]
        public void BuildTokenBlendWeightsZerosInvalidTokenize() {
            long[] tokens = { 0, 10, 0 };
            var blends = new (string?, int)[] { ("zzz", 80) };
            var weights = DiffSingerPhonemeBlend.BuildTokenBlendWeights(
                tokens, blends, _ => null);
            Assert.Equal(new float[] { 0f, 0f, 0f }, weights);
        }

        [Fact]
        public void ExpandToFramesFollowsDurations() {
            float[] tokenWeights = { 0f, 0.5f, 0.25f, 0f };
            int[] durations = { 2, 3, 1, 2 };
            var frames = DiffSingerPhonemeBlend.ExpandToFrames(tokenWeights, durations);
            Assert.Equal(
                new float[] { 0, 0, 0.5f, 0.5f, 0.5f, 0.25f, 0, 0 },
                frames);
        }

        [Fact]
        public void ExpandToFramesRejectsLengthMismatch() {
            Assert.Throws<System.ArgumentException>(() =>
                DiffSingerPhonemeBlend.ExpandToFrames(new float[] { 1f }, new List<int> { 1, 2 }));
        }
    }
}
