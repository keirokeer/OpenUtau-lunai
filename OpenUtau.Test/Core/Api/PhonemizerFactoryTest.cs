using System;
using OpenUtau.Api;
using OpenUtau.Core.DiffSinger;
using OpenUtau.Plugin.Builtin;
using Xunit;

namespace OpenUtau.Core.Api {
    /// <summary>
    /// Regression for https://github.com/keirokeer/OpenUtau-lunai/issues/15 —
    /// external DiffSinger plugins use language codes (PL/VI) and DIFFS* tags,
    /// not language "DiffSinger".
    /// </summary>
    public class PhonemizerFactoryTest {
        static PhonemizerFactory Fake(
            string name,
            string tag,
            string language,
            Type type = null) => new PhonemizerFactory {
            type = type ?? typeof(object),
            name = name,
            tag = tag,
            language = language,
        };

        [Fact]
        public void ExternalDiffsPlIsDiffSingerNotUtau() {
            // Real DIFF_PL_phonemiser.dll attribute surface
            var factory = Fake(
                "DiffSinger Polish Phonemizer",
                "DIFFS PL",
                "PL");

            Assert.True(PhonemizerFactory.IsDiffSingerPhonemizer(factory));
            Assert.False(PhonemizerFactory.IsUtauPhonemizer(factory));
        }

        [Fact]
        public void ExternalDiffsViIsDiffSingerNotUtau() {
            // Real diffs_vi_tgm.dll attribute surface
            var factory = Fake(
                "DiffSinger Vietnamese Phonemizer",
                "DIFFS VI",
                "VI");

            Assert.True(PhonemizerFactory.IsDiffSingerPhonemizer(factory));
            Assert.False(PhonemizerFactory.IsUtauPhonemizer(factory));
        }

        [Fact]
        public void DiffSingerDetectedByTagEvenWithoutDiffSingerInName() {
            var factory = Fake("Polish Phonemizer", "DIFFS PL", "PL");
            Assert.True(PhonemizerFactory.IsDiffSingerPhonemizer(factory));
        }

        [Fact]
        public void DiffSingerDetectedByBaseTypeWithClassicLanguageCode() {
            // Upstream-style attribute: language is EN, not DiffSinger
            var factory = Fake(
                "English Phonemizer",
                "EN",
                "EN",
                typeof(DiffSingerEnglishPhonemizer));
            Assert.True(PhonemizerFactory.IsDiffSingerPhonemizer(factory));
        }

        [Fact]
        public void ClassicPolishCvcIsUtauNotDiffSinger() {
            var factory = Fake(
                "Polish CVC Phonemizer",
                "PL CVC",
                "PL",
                typeof(PolishCVCPhonemizer));
            Assert.False(PhonemizerFactory.IsDiffSingerPhonemizer(factory));
            Assert.True(PhonemizerFactory.IsUtauPhonemizer(factory));
        }

        [Fact]
        public void BuiltInDiffSingerLanguageStillDetected() {
            var factory = Fake(
                "DiffSinger English Phonemizer",
                "English",
                PhonemizerFactory.DiffSingerLanguage,
                typeof(DiffSingerEnglishPhonemizer));
            Assert.True(PhonemizerFactory.IsDiffSingerPhonemizer(factory));
        }
    }
}
