using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Api;
using WanaKanaNet;

namespace OpenUtau.Core.DiffSinger {
    /// <summary>
    /// Maps project phoneme labels onto a pitch model's vocabulary
    /// (exact → classic/kana normalize → strip/retag language → similar base → family → SP).
    /// Works across DiffSinger, Enunu, Voicevox and Classic aliases.
    /// </summary>
    public sealed class DiffSingerPhonemeMapper {
        static readonly string[][] SimilarFamilies = {
            new[] { "s", "sh", "z", "zh", "ts", "ch", "j", "jh", "x", "c", "q", "zh", "ɕ", "ʑ", "ʂ", "ʐ" },
            new[] { "p", "b", "t", "d", "k", "g", "q", "cl", "dx", "tt", "dd", "kk", "gg", "pp", "bb" },
            new[] { "m", "n", "ng", "N", "ny", "nn", "mm", "ŋ" },
            new[] { "f", "v", "h", "th", "dh", "hh", "hv", "ɸ", "β" },
            new[] { "l", "r", "rr", "rh", "w", "y", "j", "ɾ", "ɹ" },
            new[] { "a", "aa", "ae", "ah", "ao", "aw", "ay", "ax", "eh", "er", "ey", "ih", "iy", "ow", "oy", "uh", "uw",
                "a", "i", "u", "e", "o", "A", "I", "U", "E", "O", "ä", "ö", "ü" },
        };
        static readonly HashSet<string> RestAliases = new(StringComparer.OrdinalIgnoreCase) {
            "R", "r", "rest", "sil", "pau", "PAU", "SP", "AP", "-", "息", "吸",
        };

        readonly HashSet<string> vocab;
        readonly string[] vocabArr;
        readonly IG2p g2p;
        readonly Dictionary<string, string> cache = new(StringComparer.Ordinal);

        public DiffSingerPhonemeMapper(IReadOnlyDictionary<string, int> phonemeTokens, IG2p g2p) {
            this.g2p = g2p ?? throw new ArgumentNullException(nameof(g2p));
            vocab = new HashSet<string>(phonemeTokens.Keys, StringComparer.Ordinal);
            vocabArr = vocab.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        }

        public static DiffSingerPhonemeMapper ForPitch(DsPitch pitch) {
            return new DiffSingerPhonemeMapper(pitch.PhonemeTokenTable, pitch.G2p);
        }

        public string Map(string phoneme) {
            if (string.IsNullOrEmpty(phoneme)) {
                return Prefer("SP", "AP") ?? "SP";
            }
            if (cache.TryGetValue(phoneme, out var hit)) {
                return hit;
            }
            var mapped = MapCore(phoneme);
            cache[phoneme] = mapped;
            return mapped;
        }

        string MapCore(string phoneme) {
            if (IsUsable(phoneme)) {
                return phoneme;
            }
            if (RestAliases.Contains(phoneme) || phoneme is "AP" or "SP" or "PAU" or "sil") {
                return Prefer("SP", "AP", "PAU", "sil") ?? "SP";
            }

            // Exact / retag first. Prefer a vowel hit: Classic VCV/kana is one phone per note,
            // and pitch models care about the mora vowel more than a CV token or consonant.
            string? nonVowelHit = null;
            foreach (var candidate in EnumerateCandidates(phoneme)) {
                var hit = MapExactOrRetag(candidate);
                if (hit == null) {
                    continue;
                }
                if (IsVowelLike(hit)) {
                    return hit;
                }
                nonVowelHit ??= hit;
            }
            if (nonVowelHit != null) {
                return nonVowelHit;
            }
            foreach (var candidate in EnumerateCandidates(phoneme)) {
                SplitLang(candidate, out _, out var basePhone);
                var similar = FindSimilar(string.IsNullOrEmpty(basePhone) ? candidate : basePhone);
                if (similar != null) {
                    return similar;
                }
            }

            return Prefer("SP", "AP") ?? vocabArr.FirstOrDefault(IsUsable) ?? phoneme;
        }

        internal bool IsVowelLike(string phoneme) {
            if (string.IsNullOrEmpty(phoneme) || RestAliases.Contains(phoneme)) {
                return false;
            }
            if (g2p.IsValidSymbol(phoneme) && g2p.IsVowel(phoneme)) {
                return true;
            }
            SplitLang(phoneme, out _, out var basePhone);
            if (!string.IsNullOrEmpty(basePhone) && g2p.IsValidSymbol(basePhone) && g2p.IsVowel(basePhone)) {
                return true;
            }
            if (string.IsNullOrEmpty(basePhone)) {
                return false;
            }
            char last = basePhone[^1];
            return last is 'a' or 'i' or 'u' or 'e' or 'o' or 'A' or 'I' or 'U' or 'E' or 'O';
        }

        string? MapExactOrRetag(string token) {
            if (string.IsNullOrEmpty(token)) {
                return null;
            }
            if (IsUsable(token)) {
                return token;
            }
            SplitLang(token, out var lang, out var basePhone);
            if (string.IsNullOrEmpty(basePhone)) {
                return null;
            }
            if (!string.IsNullOrEmpty(lang) && IsUsable(basePhone)) {
                return basePhone;
            }
            var retagged = vocabArr.FirstOrDefault(v => {
                SplitLang(v, out _, out var b);
                return b == basePhone && IsUsable(v);
            });
            if (retagged != null) {
                return retagged;
            }
            if (string.IsNullOrEmpty(lang)) {
                foreach (var prefix in EnumerateLangPrefixes()) {
                    var tagged = prefix + "/" + basePhone;
                    if (IsUsable(tagged)) {
                        return tagged;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Expands Classic diphones ("- あ", "a か"), kana, and romaji CV into map candidates.
        /// </summary>
        IEnumerable<string> EnumerateCandidates(string phoneme) {
            yield return phoneme;

            var token = NormalizeToken(phoneme);
            if (!string.Equals(token, phoneme, StringComparison.Ordinal)) {
                yield return token;
            }

            // Classic / VCV: take the last whitespace-separated piece ("a か" → "か").
            var parts = phoneme.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 1) {
                var last = NormalizeToken(parts[^1]);
                yield return last;
                foreach (var piece in ExpandKanaOrCv(last)) {
                    yield return piece;
                }
            }

            foreach (var piece in ExpandKanaOrCv(token)) {
                yield return piece;
            }
        }

        static IEnumerable<string> ExpandKanaOrCv(string token) {
            if (string.IsNullOrEmpty(token)) {
                yield break;
            }
            if (ContainsKana(token)) {
                string romaji;
                try {
                    romaji = WanaKana.ToRomaji(token).ToLowerInvariant().Replace(" ", "");
                } catch {
                    yield break;
                }
                if (!string.IsNullOrEmpty(romaji)) {
                    // Vowel before full CV so pitch mapping locks onto the mora nucleus.
                    foreach (var piece in SplitCvRomaji(romaji)) {
                        yield return piece;
                    }
                    yield return romaji;
                }
                yield break;
            }
            foreach (var piece in SplitCvRomaji(token)) {
                yield return piece;
            }
        }

        /// <summary>Prefer vowel for pitch when CV like "ka" is a single Classic phone.</summary>
        static IEnumerable<string> SplitCvRomaji(string romaji) {
            if (string.IsNullOrEmpty(romaji) || romaji.Length < 2) {
                yield break;
            }
            // Trailing vowel of a mora (ka→a, shi→i, tsu→u, kya→a).
            char last = romaji[^1];
            if (last is 'a' or 'i' or 'u' or 'e' or 'o') {
                yield return last.ToString();
                if (romaji.Length >= 2) {
                    yield return romaji[..^1]; // consonant cluster "ky", "sh", "ts"
                }
            }
        }

        static string NormalizeToken(string phoneme) {
            if (string.IsNullOrEmpty(phoneme)) {
                return phoneme;
            }
            var trimmed = phoneme.Trim().TrimStart('-', '_');
            // Drop [flags] / voice-color brackets sometimes left on aliases.
            int bracket = trimmed.IndexOf('[');
            if (bracket > 0) {
                trimmed = trimmed.Substring(0, bracket);
            }
            return trimmed.Trim();
        }

        static bool ContainsKana(string text) {
            foreach (var ch in text) {
                if (ch is (>= '\u3040' and <= '\u309F') // hiragana
                    or (>= '\u30A0' and <= '\u30FF') // katakana
                    or (>= '\uFF66' and <= '\uFF9D')) { // half-width katakana
                    return true;
                }
            }
            return false;
        }

        string? FindSimilar(string basePhone) {
            if (string.IsNullOrEmpty(basePhone)) {
                return null;
            }
            bool wantVowel = g2p.IsValidSymbol(basePhone) && g2p.IsVowel(basePhone);

            foreach (var family in SimilarFamilies) {
                if (!family.Contains(basePhone, StringComparer.OrdinalIgnoreCase)) {
                    continue;
                }
                foreach (var member in family) {
                    var hit = PreferExactOrRetagged(member, wantVowel);
                    if (hit != null) {
                        return hit;
                    }
                }
            }

            // Closest usable vocab base by Levenshtein, preferring same vowel/consonant class.
            string? best = null;
            int bestDist = int.MaxValue;
            foreach (var v in vocabArr) {
                if (!IsUsable(v)) {
                    continue;
                }
                SplitLang(v, out _, out var b);
                if (string.IsNullOrEmpty(b)) {
                    continue;
                }
                bool isVowel = g2p.IsValidSymbol(b) && g2p.IsVowel(b);
                if (g2p.IsValidSymbol(basePhone) && isVowel != wantVowel) {
                    continue;
                }
                int dist = Levenshtein(basePhone, b);
                if (dist < bestDist) {
                    bestDist = dist;
                    best = v;
                }
            }
            // Accept only reasonably close matches (short bases).
            if (best != null && bestDist <= Math.Max(1, basePhone.Length / 2)) {
                return best;
            }
            return null;
        }

        string? PreferExactOrRetagged(string basePhone, bool wantVowel) {
            if (IsUsable(basePhone)) {
                return basePhone;
            }
            foreach (var v in vocabArr) {
                SplitLang(v, out _, out var b);
                if (b != basePhone || !IsUsable(v)) {
                    continue;
                }
                if (g2p.IsValidSymbol(b) && g2p.IsVowel(b) != wantVowel) {
                    continue;
                }
                return v;
            }
            return null;
        }

        IEnumerable<string> EnumerateLangPrefixes() {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var v in vocabArr) {
                SplitLang(v, out var lang, out _);
                if (!string.IsNullOrEmpty(lang) && seen.Add(lang)) {
                    yield return lang;
                }
            }
        }

        string? Prefer(params string[] names) {
            foreach (var n in names) {
                if (IsUsable(n)) {
                    return n;
                }
            }
            return null;
        }

        bool IsUsable(string phoneme) {
            return vocab.Contains(phoneme) && g2p.IsValidSymbol(phoneme);
        }

        static void SplitLang(string phoneme, out string lang, out string basePhone) {
            int slash = phoneme.IndexOf('/');
            if (slash > 0 && slash < phoneme.Length - 1) {
                lang = phoneme.Substring(0, slash);
                basePhone = phoneme.Substring(slash + 1);
            } else {
                lang = string.Empty;
                basePhone = phoneme;
            }
        }

        static int Levenshtein(string a, string b) {
            if (a.Length == 0) {
                return b.Length;
            }
            if (b.Length == 0) {
                return a.Length;
            }
            var prev = new int[b.Length + 1];
            var curr = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) {
                prev[j] = j;
            }
            for (int i = 1; i <= a.Length; i++) {
                curr[0] = i;
                for (int j = 1; j <= b.Length; j++) {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    curr[j] = Math.Min(
                        Math.Min(curr[j - 1] + 1, prev[j] + 1),
                        prev[j - 1] + cost);
                }
                (prev, curr) = (curr, prev);
            }
            return prev[b.Length];
        }
    }
}
