using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenUtau.Core.DiffSinger;
using OpenUtau.Core.Format;
using OpenUtau.Core.Util;
using Xunit;

namespace OpenUtau.Core {
    public class PhonemeVarianceRemapTest {
        static PhonemeTypeLookup LoadTestTypes() {
            var path = Path.Combine(Path.GetTempPath(), "ou-phoneme-remap-" + System.Guid.NewGuid() + ".yaml");
            File.WriteAllText(path, """
                symbols:
                - {symbol: ru/k, type: stop}
                - {symbol: ru/a, type: vowel}
                - {symbol: ar/m, type: nasal}
                - {symbol: hh, type: stop}
                - {symbol: de/k, type: stop}
                """);
            try {
                return PhonemeTypeLookup.TryLoad(path)!;
            } finally {
                File.Delete(path);
            }
        }

        [Fact]
        public void OpenMouthScaleLookupUsesEmbeddedTable() {
            Assert.Equal(0f, PhonemeVarianceRemap.GetScale("ru/k", Format.Ustx.OPEC, null));
            Assert.Equal(100f, PhonemeVarianceRemap.GetScale("ru/a", Format.Ustx.OPEC, null));
            Assert.Equal(50f, PhonemeVarianceRemap.GetScale("hh", Format.Ustx.OPEC, null));
        }

        [Fact]
        public void TensionScaleLookupUsesRulesAndExceptions() {
            var types = LoadTestTypes();
            Assert.Equal(100f, PhonemeVarianceRemap.GetScale("hh", Format.Ustx.TENC, types));
            Assert.Equal(50f, PhonemeVarianceRemap.GetScale("de/k", Format.Ustx.TENC, types));
            Assert.Equal(100f, PhonemeVarianceRemap.GetScale("ru/a", Format.Ustx.TENC, types));
        }

        [Fact]
        public void NasalScalesDifferBetweenOpenMouthAndTension() {
            var types = LoadTestTypes();
            Assert.Equal(0f, PhonemeVarianceRemap.GetScale("ar/m", Format.Ustx.OPEC, types));
            Assert.Equal(100f, PhonemeVarianceRemap.GetScale("ar/m", Format.Ustx.TENC, types));
        }

        [Fact]
        public void CrossfadeInterpolatesBetweenNeighborPhones() {
            var phonemes = new[] { "ru/k", "ru/a" };
            var durations = new List<int> { 2, 10, 10, 2 };
            var scales = PhonemeVarianceRemap.BuildFrameScales(
                phonemes, durations, Format.Ustx.OPEC, 5f, null);
            float min = scales.Skip(2).Take(18).Min();
            float max = scales.Skip(2).Take(18).Max();
            Assert.Equal(0f, min);
            Assert.Equal(100f, max);
            Assert.True(scales.Any(v => v > 0f && v < 100f));
        }

        [Fact]
        public void JapaneseOpenMouthScalesUseEmbeddedTable() {
            Assert.Equal(100f, PhonemeVarianceRemap.GetScale("ja/a", Format.Ustx.OPEC, null));
            Assert.Equal(75f, PhonemeVarianceRemap.GetScale("ja/e", Format.Ustx.OPEC, null));
            Assert.Equal(50f, PhonemeVarianceRemap.GetScale("ja/i", Format.Ustx.OPEC, null));
            Assert.Equal(0f, PhonemeVarianceRemap.GetScale("ja/N", Format.Ustx.OPEC, null));
        }

        [Fact]
        public void SpPhonemeSkipsRemapFilter() {
            Assert.Equal(100f, PhonemeVarianceRemap.GetScale("SP", Format.Ustx.OPEC, null));
            Assert.Equal(100f, PhonemeVarianceRemap.GetScale("SP", Format.Ustx.TENC, null));
            Assert.Equal(100f, PhonemeVarianceRemap.RemapSample(100f, 100f, Format.Ustx.OPEC));
        }

        [Fact]
        public void RemapSampleOnlyAffectsValuesAboveBaseline() {
            Assert.Equal(40f, PhonemeVarianceRemap.RemapSample(40f, 0f, Format.Ustx.OPEC));
            Assert.Equal(50f, PhonemeVarianceRemap.RemapSample(50f, 0f, Format.Ustx.OPEC));
            Assert.Equal(50f, PhonemeVarianceRemap.RemapSample(100f, 0f, Format.Ustx.OPEC));
            Assert.Equal(100f, PhonemeVarianceRemap.RemapSample(100f, 100f, Format.Ustx.OPEC));
            Assert.Equal(75f, PhonemeVarianceRemap.RemapSample(100f, 50f, Format.Ustx.OPEC));
        }

        [Fact]
        public void ApplyScalesUserCurveOnMockPhrase() {
            Preferences.Default.DiffSingerPhonemeVarianceRemap = true;
            var phonemes = new[] { "ru/k", "ru/a", "ru/k" };
            var durations = new List<int> { 2, 20, 20, 20, 2 };
            var scales = PhonemeVarianceRemap.BuildFrameScales(
                phonemes, durations, Format.Ustx.OPEC, 5f, null);
            var user = Enumerable.Repeat(100f, scales.Length).ToArray();
            PhonemeVarianceRemap.ApplyToUserSamples(user, scales, Format.Ustx.OPEC);
            Assert.Equal(50f, user[10]);
            Assert.Equal(100f, user[31]);
            Assert.Equal(50f, user[50]);
        }

        [Fact]
        public void CombinedMouthOpeningAttenuatesConsonants() {
            // keepFactors: 1 = leave, 0.5/−50%, 0.2/−80% (s), 0.4/−60% (sh)
            var values = new[] { 0.8f, 0.8f, 0.8f, 0.8f };
            var keeps = new[] { 0.5f, 1f, 0.2f, 0.4f };
            PhonemeVarianceRemap.AttenuateCombinedMouthOpening(values, keeps);
            Assert.Equal(0.05f, values[0]); // default consonant → ceiling
            Assert.Equal(0.8f, values[1]);  // vowel
            Assert.Equal(0.05f, values[2]); // s (−80%) still hits ceiling from 0.8
            Assert.Equal(0.05f, values[3]); // sh (−60%) still hits ceiling from 0.8
        }

        [Fact]
        public void ConsonantAttenuateKeepFactorsForSAndSh() {
            Assert.Equal(0.2f, PhonemeVarianceRemap.GetConsonantAttenuateKeepFactor("ru/s"));
            Assert.Equal(0.2f, PhonemeVarianceRemap.GetConsonantAttenuateKeepFactor("en/s"));
            Assert.Equal(0.2f, PhonemeVarianceRemap.GetConsonantAttenuateKeepFactor("es/s"));
            Assert.Equal(0.4f, PhonemeVarianceRemap.GetConsonantAttenuateKeepFactor("ru/sh"));
            Assert.Equal(0.4f, PhonemeVarianceRemap.GetConsonantAttenuateKeepFactor("ru/shj"));
            Assert.Equal(0.4f, PhonemeVarianceRemap.GetConsonantAttenuateKeepFactor("en/sh"));
            Assert.Equal(0.5f, PhonemeVarianceRemap.GetConsonantAttenuateKeepFactor("ru/k"));
            Assert.Equal(0.5f, PhonemeVarianceRemap.GetConsonantAttenuateKeepFactor("en/t"));
        }

        [Fact]
        public void CombinedMouthOpeningSKeepBelowCeiling() {
            // Small openings: s (−80%) keeps less than default (−50%).
            var values = new[] { 0.08f, 0.08f };
            var keeps = new[] { 0.5f, 0.2f };
            PhonemeVarianceRemap.AttenuateCombinedMouthOpening(values, keeps);
            Assert.Equal(0.04f, values[0], 3);
            Assert.Equal(0.016f, values[1], 3);
        }
    }
}
