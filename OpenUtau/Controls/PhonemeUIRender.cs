using System;
using Avalonia;
using Avalonia.Media.TextFormatting;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using OpenUtau.Core.DiffSinger;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;

namespace OpenUtau.App.Controls {
    static class PhonemeUIRender {

        public static string getLangCode(UVoicePart part){
            int trackNo = part.trackNo;
            var track = DocManager.Inst.Project.tracks[trackNo];
            string langCode = "";
            if (track.Phonemizer is DiffSingerG2pPhonemizer g2pPhonemizer) {
                langCode = g2pPhonemizer.GetLangCode();
            } else if (track.Phonemizer is DiffSingerBasePhonemizer basePhonemizer) {
                langCode = basePhonemizer.GetLangCode();
            }
            return langCode;
        }

        public static bool IsDiffSingerTrack(UTrack? track) {
            return track?.Singer != null && track.Singer.Found && track.Singer.SingerType == USingerType.DiffSinger;
        }

        /// <summary>
        /// Whether to hide the language prefix in the phoneme panel for the open track (display only; does not change preferences).
        /// DiffSinger bank: follows "Hide language prefix" preference.
        /// UTAU bank with DiffSinger-friendly panel: always hidden visually.
        /// Classic phoneme panel: never hidden via these rules.
        /// </summary>
        public static bool ShouldHideLangPrefixForDisplay(UTrack? track) {
            if (!Preferences.Default.DiffSingerPhonemePanelMode) {
                return false;
            }
            if (track == null) {
                return false;
            }
            if (IsDiffSingerTrack(track)) {
                return Preferences.Default.DiffSingerLangCodeHide;
            }
            return true;
        }

        static UTrack? TrackForPart(UVoicePart? part) {
            if (part == null || DocManager.Inst.Project.tracks.Count <= part.trackNo) {
                return null;
            }
            return DocManager.Inst.Project.tracks[part.trackNo];
        }

        /// <summary>
        /// Splits phoneme text into language tag (e.g. "ja") and phoneme without tag (e.g. "a").
        /// Same logic as "hide language prefix" preference: prefix is langCode + "/".
        /// Fallback: if langCode is empty but phoneme contains "/", split on first "/".
        /// </summary>
        public static (string tagText, string phonemeOnly) SplitTagAndPhoneme(string phonemeText, string? langCode) {
            if (string.IsNullOrEmpty(phonemeText)) {
                return ("", "");
            }
            // Match langCode + "/" prefix (case-insensitive for robustness)
            if (!string.IsNullOrEmpty(langCode)) {
                var prefix = langCode + "/";
                if (phonemeText.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) {
                    return (langCode, phonemeText.Substring(prefix.Length));
                }
            }
            // Fallback: split on first "/" (e.g. "ja/o" -> "ja", "o")
            var slashIdx = phonemeText.IndexOf('/');
            if (slashIdx > 0 && slashIdx < phonemeText.Length - 1) {
                return (phonemeText.Substring(0, slashIdx), phonemeText.Substring(slashIdx + 1));
            }
            return ("", phonemeText);
        }

        public static bool HasActiveBlend(UPhoneme phoneme) {
            return phoneme != null
                && !string.IsNullOrWhiteSpace(phoneme.blendPhoneme)
                && phoneme.blendWeight > 0;
        }

        /// <summary>Phoneme-only display text (lang tag stripped), e.g. ja/sh → sh.</summary>
        public static string DisplayPhonemeOnly(string? phonemeText, string? langCode) {
            if (string.IsNullOrWhiteSpace(phonemeText)) {
                return string.Empty;
            }
            return SplitTagAndPhoneme(phonemeText.Trim(), langCode).phonemeOnly;
        }

        //Calculates the position of a phoneme alias on a piano roll view, 
        //considering factors like tick width, phoneme text, and text layout. 
        //It returns the x-coordinate and text y-coordinate of the alias
        public static (double textX, double textY, Size size, TextLayout textLayout) 
            AliasPosition(NotesViewModel viewModel, UPhoneme phoneme, string? langCode, ref double lastTextEndX, ref bool raiseText){

            string phonemeText = !string.IsNullOrEmpty(phoneme.phonemeMapped) ? phoneme.phonemeMapped : phoneme.phoneme;
            var track = TrackForPart(viewModel.Part);
            if (ShouldHideLangPrefixForDisplay(track) && !string.IsNullOrEmpty(langCode)) {
                var prefix = langCode + "/";
                if (phonemeText.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) {
                    phonemeText = phonemeText.Substring(prefix.Length);
                }
            }
            var x = viewModel.TickToneToPoint(phoneme.position, 0).X;
            var bold = phoneme.phoneme != phoneme.rawPhoneme;
            var textLayout = TextLayoutCache.Get(phonemeText, ThemeManager.ForegroundBrush!, 12, bold);
            if (x < lastTextEndX) {
                raiseText = !raiseText;
            } else {
                raiseText = false;
            }
            double textY = raiseText ? ViewConstants.PhonemeAliasRaisedTextY : ViewConstants.PhonemeAliasNormalTextY;
            var size = new Size(textLayout.Width + 4, textLayout.Height - 2);
            //var rect = new Rect(new Point(x - 2, textY + 1.5), size);
            /*if (rect.Contains(mousePos)) {
                result.phoneme = phoneme;
                result.hit = true;
                return result;
            }*/
            lastTextEndX = x + size.Width;
            return (x, textY, size, textLayout);
        }
    }
}
