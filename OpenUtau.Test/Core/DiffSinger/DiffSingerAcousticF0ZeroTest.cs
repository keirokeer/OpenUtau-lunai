using OpenUtau.Core.DiffSinger;
using OpenUtau.Core.Util;
using Xunit;

namespace OpenUtau.Core {
    public class DiffSingerAcousticF0ZeroTest {
        [Fact]
        public void ApplyAcousticF0ZeroAmount_Zero_LeavesF0Unchanged() {
            float[] f0 = { 100f, 200f, 400f };
            double[] amount = { 0, 0, 0 };

            DiffSingerUtils.ApplyAcousticF0ZeroAmount(f0, amount);

            Assert.Equal(new float[] { 100f, 200f, 400f }, f0);
        }

        [Fact]
        public void ApplyAcousticF0ZeroAmount_Hundred_ClearsF0() {
            float[] f0 = { 100f, 200f, 400f };
            double[] amount = { 100, 100, 100 };

            DiffSingerUtils.ApplyAcousticF0ZeroAmount(f0, amount);

            Assert.Equal(new float[] { 0f, 0f, 0f }, f0);
        }

        [Fact]
        public void ApplyAcousticF0ZeroAmount_Fifty_HalvesF0() {
            float[] f0 = { 100f, 200f };
            double[] amount = { 50, 50 };

            DiffSingerUtils.ApplyAcousticF0ZeroAmount(f0, amount);

            Assert.Equal(50f, f0[0], precision: 3);
            Assert.Equal(100f, f0[1], precision: 3);
        }

        [Fact]
        public void ApplyAcousticF0ZeroAmount_NullOrEmpty_NoOp() {
            float[] f0 = { 220f };
            DiffSingerUtils.ApplyAcousticF0ZeroAmount(f0, null);
            DiffSingerUtils.ApplyAcousticF0ZeroAmount(f0, System.Array.Empty<double>());
            Assert.Equal(220f, f0[0]);
        }

        [Fact]
        public void Af0zConstant_IsDiffSingerOnlyAbbr() {
            Assert.Equal("af0z", DiffSingerUtils.AF0Z);
        }

        [Fact]
        public void Preference_AcousticF0Zero_DefaultsOff() {
            // Fresh Preferences field default; do not mutate Default in case other tests rely on it.
            var prefs = new Preferences.SerializablePreferences();
            Assert.False(prefs.DiffSingerAcousticF0ZeroEnabled);
        }
    }
}
