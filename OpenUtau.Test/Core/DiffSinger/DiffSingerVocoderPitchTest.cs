using System;
using OpenUtau.Core.DiffSinger;
using Xunit;

namespace OpenUtau.Core {
    public class DiffSingerVocoderPitchTest {
        [Fact]
        public void ApplyVocoderPitchCents_ZeroCents_LeavesF0Unchanged() {
            float[] baseF0 = { 100f, 200f, 400f };
            double[] cents = { 0, 0, 0 };

            float[] result = DiffSingerUtils.ApplyVocoderPitchCents(baseF0, cents);

            Assert.Equal(baseF0, result);
        }

        [Fact]
        public void ApplyVocoderPitchCents_Plus1200_DoublesF0() {
            float[] baseF0 = { 220f };
            double[] cents = { 1200 };

            float[] result = DiffSingerUtils.ApplyVocoderPitchCents(baseF0, cents);

            Assert.Equal(440f, result[0], precision: 3);
        }

        [Fact]
        public void ApplyVocoderPitchCents_Minus1200_HalvesF0() {
            float[] baseF0 = { 440f };
            double[] cents = { -1200 };

            float[] result = DiffSingerUtils.ApplyVocoderPitchCents(baseF0, cents);

            Assert.Equal(220f, result[0], precision: 3);
        }

        [Fact]
        public void ApplyVocoderPitchCents_NullCents_CopiesBase() {
            float[] baseF0 = { 100f, 200f };

            float[] result = DiffSingerUtils.ApplyVocoderPitchCents(baseF0, null);

            Assert.Equal(baseF0, result);
            Assert.NotSame(baseF0, result);
        }

        [Fact]
        public void ApplyVocoderPitchCents_ShorterCents_LeavesTailUnshifted() {
            float[] baseF0 = { 100f, 200f, 300f };
            double[] cents = { 1200 };

            float[] result = DiffSingerUtils.ApplyVocoderPitchCents(baseF0, cents);

            Assert.Equal(200f, result[0], precision: 3);
            Assert.Equal(200f, result[1]);
            Assert.Equal(300f, result[2]);
        }

        [Fact]
        public void VpitConstant_IsDiffSingerOnlyAbbr() {
            Assert.Equal("vpit", DiffSingerUtils.VPIT);
        }
    }
}
