using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Api;
using OpenUtau.Core.G2p;

#nullable enable
namespace OpenUtau.Core.G2p {
    public class JapaneseRuleBasedG2p : IG2p {
        private static readonly string[] validPhonemes = {
            "a", "i", "u", "e", "o", "N",
            "k", "s", "t", "n", "h", "f", "m", "y", "r", "w", "v", "l",
            "g", "z", "d", "b", "p", "j", "sh", "ch", "ts", "dz", "cl", "q",
            "ky", "gy", "sy", "zy", "ty", "dy", "ny", "hy", "fy", "by", "py", "my", "ry",
            "SP", "AP"
        };

        // all the single hiragana get defined directly
        private static readonly Dictionary<string, string[]> KanaMap = new Dictionary<string, string[]> {
            // Vowels
            { "あ", new[] { "a" } }, { "い", new[] { "i" } }, { "う", new[] { "u" } }, { "え", new[] { "e" } }, { "お", new[] { "o" } },

            // K-series
            { "か", new[] { "k", "a" } }, { "き", new[] { "k", "i" } }, { "く", new[] { "k", "u" } }, { "け", new[] { "k", "e" } }, { "こ", new[] { "k", "o" } },
            { "が", new[] { "g", "a" } }, { "ぎ", new[] { "g", "i" } }, { "ぐ", new[] { "g", "u" } }, { "げ", new[] { "g", "e" } }, { "ご", new[] { "g", "o" } },

            // S-series
            { "さ", new[] { "s", "a" } }, { "し", new[] { "sh", "i" } }, { "す", new[] { "s", "u" } }, { "せ", new[] { "s", "e" } }, { "そ", new[] { "s", "o" } },
            { "ざ", new[] { "z", "a" } }, { "じ", new[] { "j", "i" } }, { "ず", new[] { "z", "u" } }, { "ぜ", new[] { "z", "e" } }, { "ぞ", new[] { "z", "o" } },

            // T-series
            { "た", new[] { "t", "a" } }, { "ち", new[] { "ch", "i" } }, { "つ", new[] { "ts", "u" } }, { "て", new[] { "t", "e" } }, { "と", new[] { "t", "o" } },
            { "だ", new[] { "d", "a" } }, { "ぢ", new[] { "dz", "i" } }, { "づ", new[] { "dz", "u" } }, { "で", new[] { "d", "e" } }, { "ど", new[] { "d", "o" } },

            // N-series
            { "な", new[] { "n", "a" } }, { "に", new[] { "n", "i" } }, { "ぬ", new[] { "n", "u" } }, { "ね", new[] { "n", "e" } }, { "の", new[] { "n", "o" } },

            // H-series
            { "は", new[] { "h", "a" } }, { "ひ", new[] { "h", "i" } }, { "ふ", new[] { "f", "u" } }, { "へ", new[] { "h", "e" } }, { "ほ", new[] { "h", "o" } },
            { "ば", new[] { "b", "a" } }, { "び", new[] { "b", "i" } }, { "ぶ", new[] { "b", "u" } }, { "べ", new[] { "b", "e" } }, { "ぼ", new[] { "b", "o" } },
            { "ぱ", new[] { "p", "a" } }, { "ぴ", new[] { "p", "i" } }, { "ぷ", new[] { "p", "u" } }, { "ぺ", new[] { "p", "e" } }, { "ぽ", new[] { "p", "o" } },

            // M-series
            { "ま", new[] { "m", "a" } }, { "み", new[] { "m", "i" } }, { "む", new[] { "m", "u" } }, { "め", new[] { "m", "e" } }, { "も", new[] { "m", "o" } },
            
            // Y-series
            { "や", new[] { "y", "a" } }, { "ゆ", new[] { "y", "u" } }, { "よ", new[] { "y", "o" } },

            // R-series
            { "ら", new[] { "r", "a" } }, { "り", new[] { "r", "i" } }, { "る", new[] { "r", "u" } }, { "れ", new[] { "r", "e" } }, { "ろ", new[] { "r", "o" } },

            // W-series
            { "わ", new[] { "w", "a" } }, { "を", new[] { "w", "o" } },
            
            // etc.
            { "ん", new[] { "N" } }, { "っ", new[] { "cl" } }, { "ヴ", new[] { "v", "u"}}, { "ゔ", new[] { "v", "u"}},

            // katakana R series -> L
            { "ラ", new[] { "l", "a" } }, { "リ", new[] { "l", "i" } }, { "ル", new[] { "l", "u" } }, { "レ", new[] { "l", "e" } }, { "ロ", new[] { "l", "o" } },
        };

        private static readonly string[] romajiMultiChar = { "sh", "ch", "ts", "ky", "gy", "sy", "zy", "ty", "dy", "ny", "hy", "fy", "by", "py", "my", "ry" };

        public bool IsGlide(string symbol) => symbol == "w" || symbol == "y";
        public bool IsValidSymbol(string symbol) => validPhonemes.Contains(symbol);
        public bool IsVowel(string symbol) => "aiueoN".Contains(symbol);
        bool IsConsonant(string symbol) => !IsVowel(symbol);

        public string[] UnpackHint(string hint, char separator = ' ') => hint.Split(separator).ToArray();

        public string[] Query(string grapheme) {
            if (string.IsNullOrEmpty(grapheme)) return null;
            // Handle pause/silence tokens directly
            if (grapheme == "SP") return new[] { "SP" };
            if (grapheme == "AP") return new[] { "AP" };
            if (grapheme == "EP") return new[] { "EP" };
            return Predict(grapheme);
        }

        private string[] Predict(string input) {
            List<string> phonemes = new List<string>();
            int i = 0;
            string text = input;

            // 1. Handle Romaji Doubled Consonant (e.g. 'kk' -> 'cl', 'k')
            // Check multi-char consonants first so 'cchi' isn't misread as doubled 'c'.
            if (i + 4 <= text.Length) {
                string twoChar = char.ToLowerInvariant(text[i]).ToString() + char.ToLowerInvariant(text[i+1]).ToString();
                if (romajiMultiChar.Contains(twoChar) && twoChar != "nn") {
                    string nextTwo = char.ToLowerInvariant(text[i+2]).ToString() + char.ToLowerInvariant(text[i+3]).ToString();
                    if (twoChar == nextTwo) {
                        phonemes.Add("cl");
                        phonemes.Add(twoChar);
                        i += 4;
                    }
                }
            }
            // Single-char doubling fallback.
            if (i + 1 < text.Length && char.IsLetter(text[i]) && char.ToLower(text[i]) == char.ToLower(text[i+1])) {
                string c = char.ToLower(text[i]).ToString();
                if (c != "n") { // 'nn' is usually handled by the 'n' rules
                    phonemes.Add("cl");
                    // Check if combining this doubled letter with what follows forms a multi-char consonant.
                    // e.g. ttsu → cl + ts (not cl + t)
                    bool foundMulti = false;
                    foreach (var multi in romajiMultiChar) {
                        if (multi.StartsWith(c) && text.Substring(i+2).StartsWith(multi.Substring(1), StringComparison.OrdinalIgnoreCase)) {
                            phonemes.Add(multi);
                            i += 2 + multi.Length - 1; // skip doubled char + rest of multi-char
                            foundMulti = true;
                            break;
                        }
                    }
                    if (!foundMulti) {
                        phonemes.Add(c);
                        i += 2;
                    }
                }
            }

            while (i < text.Length) {
                char c = text[i];

                // Handle Sokuon (っ)
                if (c == 'っ' || c == 'ッ') {
                    phonemes.Add("cl");
                    i++;
                    continue;
                }

                // Handle Yoon (ゃ, ゅ, ょ) - Merges with previous consonant
                if (IsSmallVowel(c)) {
                    if (phonemes.Count > 0) {
                        string last = phonemes.Last();
                        if (IsVowel(last) && phonemes.Count >= 2) {
                            // Last is a vowel, look back for the consonant (e.g. きゃ -> [k, i] + ゃ)
                            string prev = phonemes[phonemes.Count - 2];
                            phonemes.RemoveAt(phonemes.Count - 1);
                            phonemes.RemoveAt(phonemes.Count - 1);
                            phonemes.Add(GetYoonConsonant(prev, c));
                            phonemes.Add(GetYoonVowel(c));
                        } else if (!IsVowel(last)) {
                            // Last is already a consonant (romaji case)
                            phonemes.RemoveAt(phonemes.Count - 1);
                            phonemes.Add(GetYoonConsonant(last, c));
                            phonemes.Add(GetYoonVowel(c));
                        }
                    }
                    i++;
                    continue;
                }

                // Handle い/う as glides when followed by small vowels (loanword patterns like いぇ -> ye)
                if ((c == 'い' || c == 'イ') && i + 1 < text.Length && IsSmallVowel(text[i+1])) {
                    phonemes.Add("y");
                    i++;
                    continue;
                }
                if ((c == 'う' || c == 'ウ') && i + 1 < text.Length && IsSmallVowel(text[i+1])) {
                    phonemes.Add("w");
                    i++;
                    continue;
                }

                // Handle Kana
                string charStr = c.ToString();
                if (KanaMap.TryGetValue(charStr, out var components)) {
                    foreach (var comp in components) {
                        phonemes.Add(comp);
                    }
                    i++;
                }
                // Handle Romaji
                else {
                    bool matchedMulti = false;
                    string remaining = text.Substring(i);
                    foreach (var multi in romajiMultiChar) {
                        if (remaining.StartsWith(multi, StringComparison.OrdinalIgnoreCase)) {
                            phonemes.Add(multi);
                            i += multi.Length;
                            matchedMulti = true;
                            break;
                        }
                    }

                    if (!matchedMulti) {
                        string single = char.ToLower(text[i]).ToString();
                                                if (single == "n") {
                            // Look ahead: if not followed by a vowel (or at end of string), it's the moraic 'N'
                            bool nextIsVowel = (i + 1 < text.Length) && IsVowel(char.ToLowerInvariant(text[i+1]).ToString());
                            if (!nextIsVowel)
                                phonemes.Add("N");
                            else
                                phonemes.Add("n");
                        }
                        else if (validPhonemes.Contains(single)) {
                            phonemes.Add(single);
                        }
                        i++;
                    }
                }
            }

            return phonemes.Count == 0 ? null : phonemes.ToArray();
        }

        private bool IsSmallVowel(char c) => "ゃゅょャュョぁぃぅぇぉ".Contains(c);

        // ゃ/ゅ/ょ can trigger Cy variants (e.g. ふゅ -> fy+u). ぁ/ぃ/ぅ/ぇ/ぉ never do.
        private bool IsYoonGlideVowel(char c) => "ゃゅょャュョ".Contains(c);

        private string GetYoonVowel(char smallVowel) => smallVowel switch {
            'ゃ' or 'ャ' => "a",
            'ゅ' or 'ュ' => "u",
            'ょ' or 'ョ' => "o",
            'ぁ' or 'ァ' => "a",
            'ぃ' or 'ィ' => "i",
            'ぅ' or 'ゥ' => "u",
            'ぇ' or 'ェ' => "e",
            'ぉ' or 'ォ' => "o",
            _ => ""
        };

        private string GetYoonConsonant(string consonant, char smallVowel) {
            // Only ゃ/ゅ/ょ can trigger Cy variants; ぁ/ぃ/ぅ/ぇ/ぉ never add a glide.
            if (!IsYoonGlideVowel(smallVowel)) return consonant;

            // Single consonant: check if an "Cy" variant exists in validPhonemes (e.g. fy, ky)
            if (consonant.Length == 1 && !IsVowel(consonant)) {
                string candidate = consonant + "y";
                if (validPhonemes.Contains(candidate)) return candidate;
            }
            // Multi-char consonants (sh, ch, ts) + j stay as-is — no shy/chy/jy in valid set.
            return consonant;
        }
    }
}

namespace OpenUtau.Core.DiffSinger {
    [Phonemizer("DiffSinger Japanese+ Phonemizer", "Japanese+", language: "DiffSinger", author: "AgentAsteriski")]
    public class DiffSingerJapaneseExtendedPhonemizer : DiffSingerG2pPhonemizer {
        protected override string GetDictionaryName() => "dsdict-ja.yaml";

        public override string GetLangCode() => "ja";

        protected override IG2p LoadBaseG2p() => new JapaneseRuleBasedG2p();

        protected override string[] GetBaseG2pVowels() => new string[] {
            "a", "e", "i", "o", "u", "N"
        };

        protected override string[] GetBaseG2pConsonants() => new string[] {
            "k", "s", "t", "n", "h", "f", "m", "y", "r", "w", "v", "l",
            "g", "z", "d", "b", "p", "j", "sh", "ch", "ts", "dz", "q", "cl",
            "ky", "gy", "sy", "zy", "ty", "dy", "ny", "hy", "fy", "by", "py", "my", "ry"
        };

        // I have no idea why this doesn't work.
        public override Result Process(Note[] notes, Note? prev, Note? next, Note? prevNeighbour, Note? nextNeighbour, Note[] prevs) {

 
            if (!partResult.TryGetValue(notes[0].position, out var phonemes)) {
                throw new Exception("Result not found in the part");
            }
            var processedPhonemes = new List<Phoneme>();
            var langCode = GetLangCode() + "/";

            for (int i = 0; i < phonemes.Count; i++) {
                var tu = phonemes[i];

                if (ShouldReplacePhoneme(tu.Item1, out string replacement)) {
                    // If phoneme should be replaced, process the replacement
                    processedPhonemes.Add(new Phoneme() {
                        phoneme = replacement,
                        position = tu.Item2
                    });
                } else {
                    // If no conditions are met, just add the current phoneme
                    processedPhonemes.Add(new Phoneme() {
                        phoneme = tu.Item1,
                        position = tu.Item2
                    });
                }
            }
            return new Result {
                phonemes = processedPhonemes.ToArray()
            };
        }

        // thank you cadlaxa
        private bool ShouldReplacePhoneme(string phoneme, out string replacement) {
            replacement = phoneme; // Defaults to the base phoneme if not found yk
            var langCode = GetLangCode() + "/";

            // If the voicebank has the base phoneme, no replacement is needed
            if (HasPhoneme(phoneme)) {
                return false;
            }

            // If the base is missing, dynamically check for the "ja/" prefix
            if (HasPhoneme(langCode + phoneme)) {
                replacement = langCode + phoneme;
                return true;
            }

            return false;
        } 
    }
}
