using System.Collections.Generic;
using OpenUtau.Api;
using OpenUtau.Core.DiffSinger;
using Xunit;

namespace OpenUtau.Core {
    public class DiffSingerPhonemeMapperTest {
        static IG2p BuildG2p(params (string symbol, bool vowel)[] symbols) {
            var builder = G2pDictionary.NewBuilder();
            foreach (var (symbol, vowel) in symbols) {
                builder.AddSymbol(symbol, vowel);
            }
            builder.AddSymbol("SP", true);
            builder.AddSymbol("AP", true);
            return builder.Build();
        }

        static DiffSingerPhonemeMapper Mapper(params string[] vocab) {
            var tokens = new Dictionary<string, int>();
            for (int i = 0; i < vocab.Length; i++) {
                tokens[vocab[i]] = i;
            }
            var g2pSymbols = new List<(string, bool)>();
            foreach (var v in vocab) {
                bool vowel = v is "a" or "i" or "u" or "e" or "o"
                    || v.EndsWith("/a") || v.EndsWith("/i") || v.EndsWith("/u")
                    || v.EndsWith("/e") || v.EndsWith("/o");
                g2pSymbols.Add((v, vowel));
            }
            return new DiffSingerPhonemeMapper(tokens, BuildG2p(g2pSymbols.ToArray()));
        }

        [Fact]
        public void ExactMatchUnchanged() {
            var mapper = Mapper("en/s", "en/a", "SP");
            Assert.Equal("en/s", mapper.Map("en/s"));
        }

        [Fact]
        public void RetagsLanguagePrefix() {
            var mapper = Mapper("ru/s", "ru/a", "SP");
            Assert.Equal("ru/s", mapper.Map("en/s"));
        }

        [Fact]
        public void StripsLanguageWhenBaseExists() {
            var mapper = Mapper("s", "a", "SP");
            Assert.Equal("s", mapper.Map("en/s"));
        }

        [Fact]
        public void FallsBackToSimilarConsonant() {
            var mapper = Mapper("sh", "a", "SP");
            Assert.Equal("sh", mapper.Map("en/s"));
        }

        [Fact]
        public void CachesRepeatedLookups() {
            var mapper = Mapper("ru/s", "SP");
            Assert.Equal(mapper.Map("en/s"), mapper.Map("en/s"));
        }

        [Fact]
        public void UnknownFallsBackToSp() {
            var mapper = Mapper("a", "SP");
            Assert.Equal("SP", mapper.Map("xyzzy"));
        }

        [Fact]
        public void ClassicKanaMapsToVowelOrRomaji() {
            var mapper = Mapper("a", "k", "ka", "SP");
            // "か" → romaji "ka" (exact) preferred over vowel-only.
            Assert.Equal("ka", mapper.Map("か"));
            Assert.Equal("a", mapper.Map("- あ"));
        }

        [Fact]
        public void ClassicDiphoneTakesLastToken() {
            var mapper = Mapper("a", "SP");
            Assert.Equal("a", mapper.Map("a あ"));
        }
    }
}
