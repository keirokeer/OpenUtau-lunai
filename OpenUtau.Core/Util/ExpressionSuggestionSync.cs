using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Core.DiffSinger;
using OpenUtau.Core.Ustx;
using Serilog;
using FormatUstx = OpenUtau.Core.Format.Ustx;

namespace OpenUtau.Core.Util {
    /// <summary>
    /// Upserts renderer-suggested expressions into a project (add missing, refresh names).
    /// Does not remove expressions or touch CustomDefaultValue.
    /// </summary>
    public static class ExpressionSuggestionSync {
        static readonly HashSet<string> PanelHiddenAbbrs = new(System.StringComparer.OrdinalIgnoreCase) {
            FormatUstx.DYN,
            FormatUstx.PITD,
        };

        /// <returns>True if any descriptor was added or metadata updated.</returns>
        public static bool UpsertSuggested(UProject project, UTrack track) {
            if (project == null || track == null) {
                return false;
            }
            var renderer = track.RendererSettings?.Renderer;
            if (renderer == null) {
                return false;
            }
            UExpressionDescriptor[] suggestions;
            try {
                suggestions = renderer.GetSuggestedExpressions(track.Singer, track.RendererSettings);
            } catch (Exception e) {
                Log.Error(e, "GetSuggestedExpressions failed for track {Track}", track.TrackName);
                return false;
            }
            if (suggestions == null || suggestions.Length == 0) {
                return false;
            }
            bool changed = false;
            foreach (var suggestion in suggestions) {
                if (suggestion == null || string.IsNullOrEmpty(suggestion.abbr)) {
                    continue;
                }
                var abbr = suggestion.abbr.ToLowerInvariant();
                if (!project.expressions.TryGetValue(abbr, out var existing)) {
                    var clone = suggestion.Clone();
                    clone.abbr = abbr;
                    project.expressions[abbr] = clone;
                    changed = true;
                    continue;
                }
                if (ApplySuggestionMetadata(existing, suggestion)) {
                    changed = true;
                }
            }
            return changed;
        }

        /// <summary>
        /// Numerical/Curve descriptors for the expression-defaults panel (excludes DYN/PITD/Options).
        /// </summary>
        public static List<UExpressionDescriptor> GetPanelDescriptors(UProject project, UTrack track) {
            var result = new List<UExpressionDescriptor>();
            if (project == null || track == null) {
                return result;
            }
            foreach (var descriptor in track.GetSupportedExps(project)) {
                if (descriptor.type == UExpressionType.Options) {
                    continue;
                }
                if (descriptor.type != UExpressionType.Numerical && descriptor.type != UExpressionType.Curve) {
                    continue;
                }
                if (PanelHiddenAbbrs.Contains(descriptor.abbr)) {
                    continue;
                }
                if (DiffSingerUtils.IsVoiceColorAbbr(descriptor.abbr)) {
                    // Only colors suggested for this singer (already filtered by IsExpressionAvailable).
                    result.Add(descriptor);
                    continue;
                }
                result.Add(descriptor);
            }
            return result;
        }

        public static List<UExpressionDescriptor> GetPanelParameterDescriptors(UProject project, UTrack track) {
            return GetPanelDescriptors(project, track)
                .Where(d => !DiffSingerUtils.IsVoiceColorAbbr(d.abbr))
                .ToList();
        }

        public static List<UExpressionDescriptor> GetPanelVoiceColorDescriptors(UProject project, UTrack track) {
            return GetPanelDescriptors(project, track)
                .Where(d => DiffSingerUtils.IsVoiceColorAbbr(d.abbr))
                .ToList();
        }

        public static string GetPanelDisplayName(UExpressionDescriptor descriptor) {
            if (DiffSingerUtils.IsVoiceColorAbbr(descriptor.abbr)) {
                return DiffSingerUtils.FormatVoiceColorDisplayName(descriptor.name);
            }
            return descriptor.name;
        }

        static bool ApplySuggestionMetadata(UExpressionDescriptor existing, UExpressionDescriptor suggestion) {
            bool changed = false;
            if (existing.name != suggestion.name) {
                existing.name = suggestion.name;
                changed = true;
            }
            if (existing.type != suggestion.type) {
                existing.type = suggestion.type;
                changed = true;
            }
            if (existing.min != suggestion.min) {
                existing.min = suggestion.min;
                changed = true;
            }
            if (existing.max != suggestion.max) {
                existing.max = suggestion.max;
                changed = true;
            }
            if (existing.defaultValue != suggestion.defaultValue) {
                float custom = existing.CustomDefaultValue;
                existing.defaultValue = suggestion.defaultValue;
                existing.CustomDefaultValue = custom;
                changed = true;
            }
            if (existing.isFlag != suggestion.isFlag) {
                existing.isFlag = suggestion.isFlag;
                changed = true;
            }
            if (existing.flag != suggestion.flag) {
                existing.flag = suggestion.flag ?? string.Empty;
                changed = true;
            }
            return changed;
        }
    }
}
