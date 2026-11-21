using System;
using System.Collections.Generic;
using System.Text;

namespace OpenUtau.Plugin.Arabic;

/// <summary>
/// Regras de conversão G2P para Árabe (Modern Standard Arabic) com suporte a escrita Árabe (Harakat / Tashkeel),
/// Arabizi / Chat Arabic e transliteração direta Buckwalter / DIN 31635.
/// </summary>
public static class ArabicRuleG2p {
    public static string[] TranscribeToSampa(string word) {
        if (string.IsNullOrWhiteSpace(word)) return Array.Empty<string>();

        word = NormalizeArabic(word);
        var phones = new List<string>();

        int i = 0;
        while (i < word.Length) {
            char c = word[i];

            // Trata encontros consonantais ou dígrafos específicos
            if (i + 1 < word.Length) {
                string pair = word.Substring(i, 2);
                if (pair is "sh" or "th" or "dh" or "kh" or "gh" or "ay" or "aw" or "aa" or "ee" or "oo" or "ii" or "uu") {
                    phones.Add(pair switch {
                        "sh" => "sh",
                        "th" => "th",
                        "dh" => "dh",
                        "kh" => "kh",
                        "gh" => "gh",
                        "ay" => "ay",
                        "aw" => "aw",
                        "aa" => "a:",
                        "ee" => "i:",
                        "ii" => "i:",
                        "oo" => "u:",
                        "uu" => "u:",
                        _ => pair
                    });
                    i += 2;
                    continue;
                }
            }

            // Mapeamento caractere por caractere (Letras Árabes e Arabizi/Latin)
            string? mapped = c switch {
                // Letras Árabes & Vogais
                'ا' or 'أ' or 'إ' or 'آ' => "a:",
                'ب' => "b",
                'ت' => "t",
                'ث' => "th",
                'ج' => "j",
                'ح' => "H",
                'خ' => "kh",
                'د' => "d",
                'ذ' => "dh",
                'ر' => "r",
                'ز' => "z",
                'س' => "s",
                'ش' => "sh",
                'ص' => "s.",
                'ض' => "d.",
                'ط' => "t.",
                'ظ' => "dh.",
                'ع' => "3",
                'غ' => "gh",
                'ف' => "f",
                'ق' => "q",
                'ك' => "k",
                'ل' => "l",
                'م' => "m",
                'ن' => "n",
                'ه' => "h",
                'و' => "w",
                'ي' or 'ى' => "y",
                'ة' => "a",
                'ء' or 'ئ' or 'ؤ' => "?",
                
                // Harakat (Tashkeel)
                '\u064E' => "a",     // Fatha
                '\u064F' => "u",     // Damma
                '\u0650' => "i",     // Kasra
                '\u064B' => "a",     // Fathatan
                '\u064C' => "u",     // Dammatan
                '\u064D' => "i",     // Kasratan
                '\u0651' => null,    // Shaddah (duplicação tratada ou ignorada)
                '\u0652' => null,    // Sukun

                // Caracteres Latinos / Arabizi
                'a' => "a",
                'b' => "b",
                'c' => "k",
                'd' => "d",
                'e' => "e",
                'f' => "f",
                'g' => "gh",
                'h' => "h",
                'i' => "i",
                'j' => "j",
                'k' => "k",
                'l' => "l",
                'm' => "m",
                'n' => "n",
                'o' => "o",
                'p' => "b",
                'q' => "q",
                'r' => "r",
                's' => "s",
                't' => "t",
                'u' => "u",
                'v' => "f",
                'w' => "w",
                'x' => "kh",
                'y' => "y",
                'z' => "z",
                '2' => "?",
                '3' => "3",
                '5' => "kh",
                '6' => "t.",
                '7' => "H",
                '8' => "gh",
                '9' => "s.",
                _ => null
            };

            if (mapped != null) {
                phones.Add(mapped);
            }
            i++;
        }

        return phones.ToArray();
    }

    static string NormalizeArabic(string input) {
        var sb = new StringBuilder();
        foreach (char c in input.Trim().ToLowerInvariant()) {
            if (char.IsPunctuation(c) || char.IsWhiteSpace(c)) continue;
            sb.Append(c);
        }
        return sb.ToString();
    }
}
