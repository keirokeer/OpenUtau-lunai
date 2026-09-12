using System;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Core.Util {
    /// <summary>Looks up optional voice-color descriptions from singer subbanks.</summary>
    public static class VoiceColorDescription {
        public static string? ForColor(USinger? singer, string? color) {
            if (singer?.Subbanks == null || string.IsNullOrEmpty(color)) {
                return null;
            }
            foreach (var subbank in singer.Subbanks) {
                if (string.Equals(subbank.Color, color, StringComparison.Ordinal)
                    && !string.IsNullOrWhiteSpace(subbank.Description)) {
                    return subbank.Description;
                }
            }
            return null;
        }

        public static string? ForOptionIndex(USinger? singer, string[]? options, int index) {
            if (options == null || index < 0 || index >= options.Length) {
                return null;
            }
            return ForColor(singer, options[index]);
        }

        /// <summary>
        /// Maps DiffSinger clNN curve name ("voice color 02: lady") or display name to description.
        /// </summary>
        public static string? ForVoiceColorDescriptor(USinger? singer, string? descriptorName) {
            if (singer == null || string.IsNullOrEmpty(descriptorName)) {
                return null;
            }
            string color = DiffSinger.DiffSingerUtils.FormatVoiceColorDisplayName(descriptorName);
            return ForColor(singer, color);
        }

        public static string AppendTip(string valueTip, string? description) {
            if (string.IsNullOrWhiteSpace(description)) {
                return valueTip;
            }
            if (string.IsNullOrEmpty(valueTip)) {
                return description!;
            }
            return $"{valueTip}\n{description}";
        }
    }
}
