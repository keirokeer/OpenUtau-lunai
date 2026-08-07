using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Core.Format;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Core.Util {
    /// <summary>
    /// Resolves expression empty-baseline: track override → descriptor CustomDefault (TryGetExpDescriptor) → factory.
    /// Authored phoneme/curve values always win over this hierarchy.
    /// </summary>
    public static class ExpressionDefaultResolver {
        public static string NormalizeAbbr(string abbr) =>
            string.IsNullOrEmpty(abbr) ? abbr : abbr.ToLowerInvariant();

        public static float GetProjectDefault(UProject project, string abbr) {
            abbr = NormalizeAbbr(abbr);
            if (project.expressions.TryGetValue(abbr, out var descriptor)) {
                return descriptor.CustomDefaultValue;
            }
            return 0;
        }

        public static float? GetTrackOverride(UTrack? track, string abbr) {
            if (track?.ExpressionDefaultOverrides == null) {
                return null;
            }
            abbr = NormalizeAbbr(abbr);
            if (track.ExpressionDefaultOverrides.TryGetValue(abbr, out var value)) {
                return value;
            }
            return null;
        }

        public static bool HasTrackOverride(UTrack? track, string abbr) =>
            GetTrackOverride(track, abbr).HasValue;

        /// <summary>
        /// Effective empty baseline for phonemes/curves on this track.
        /// </summary>
        public static float GetEffectiveDefault(UProject project, UTrack? track, string abbr) {
            abbr = NormalizeAbbr(abbr);
            var overrideValue = GetTrackOverride(track, abbr);
            if (overrideValue.HasValue) {
                return ClampToDescriptor(project, track, abbr, overrideValue.Value);
            }
            if (track != null && track.TryGetExpDescriptor(project, abbr, out var descriptor)) {
                return descriptor.CustomDefaultValue;
            }
            return GetProjectDefault(project, abbr);
        }

        public static int GetEffectiveDefaultInt(UProject project, UTrack? track, string abbr) =>
            (int)Math.Round(GetEffectiveDefault(project, track, abbr));

        /// <summary>
        /// Set or clear a track override. Returns false if nothing changed.
        /// Value equal to project baseline clears the override.
        /// </summary>
        public static bool ApplyTrackOverride(UProject project, UTrack track, string abbr, float? value) {
            abbr = NormalizeAbbr(abbr);
            EnsureOverridesDict(track);
            float projectBaseline = GetDescriptorBaseline(project, track, abbr);
            if (!value.HasValue || ApproximatelyEqual(value.Value, projectBaseline)) {
                return track.ExpressionDefaultOverrides.Remove(abbr);
            }
            float clamped = ClampToDescriptor(project, track, abbr, value.Value);
            if (ApproximatelyEqual(clamped, projectBaseline)) {
                return track.ExpressionDefaultOverrides.Remove(abbr);
            }
            if (track.ExpressionDefaultOverrides.TryGetValue(abbr, out var existing) &&
                ApproximatelyEqual(existing, clamped)) {
                return false;
            }
            track.ExpressionDefaultOverrides[abbr] = clamped;
            return true;
        }

        /// <summary>
        /// Drop track overrides that match the current project baseline for the given abbrs (all tracks).
        /// </summary>
        public static void PruneMatchingOverrides(UProject project, IEnumerable<string>? abbrs = null) {
            HashSet<string>? filter = null;
            if (abbrs != null) {
                filter = new HashSet<string>(abbrs.Select(NormalizeAbbr));
            }
            foreach (var track in project.tracks) {
                if (track.ExpressionDefaultOverrides == null || track.ExpressionDefaultOverrides.Count == 0) {
                    continue;
                }
                var keys = track.ExpressionDefaultOverrides.Keys.ToList();
                foreach (var key in keys) {
                    if (filter != null && !filter.Contains(key)) {
                        continue;
                    }
                    float baseline = GetDescriptorBaseline(project, track, key);
                    if (ApproximatelyEqual(track.ExpressionDefaultOverrides[key], baseline)) {
                        track.ExpressionDefaultOverrides.Remove(key);
                    }
                }
            }
        }

        public static Dictionary<string, float> CloneOverrides(UTrack? track) {
            if (track?.ExpressionDefaultOverrides == null || track.ExpressionDefaultOverrides.Count == 0) {
                return new Dictionary<string, float>();
            }
            return new Dictionary<string, float>(track.ExpressionDefaultOverrides);
        }

        public static void EnsureOverridesDict(UTrack track) {
            if (track.ExpressionDefaultOverrides == null) {
                track.ExpressionDefaultOverrides = new Dictionary<string, float>();
            }
        }

        /// <summary>
        /// Baseline used when deciding whether a track override is redundant:
        /// TrackExpressions-owned descriptor custom default if present, else project.
        /// </summary>
        public static float GetDescriptorBaseline(UProject project, UTrack? track, string abbr) {
            abbr = NormalizeAbbr(abbr);
            if (track != null && track.TryGetExpDescriptor(project, abbr, out var descriptor)) {
                // For CLR, TryGet returns VoiceColorExp — its CustomDefault tracks project unless diverged.
                // Project-layer baseline for override comparison should be project.expressions when available.
                if (abbr == Format.Ustx.CLR && project.expressions.TryGetValue(abbr, out var projectClr)) {
                    return projectClr.CustomDefaultValue;
                }
                // Prefer project dictionary when the resolved descriptor is the project one.
                if (project.expressions.TryGetValue(abbr, out var projectDesc) &&
                    ReferenceEquals(descriptor, projectDesc)) {
                    return projectDesc.CustomDefaultValue;
                }
                if (project.expressions.TryGetValue(abbr, out projectDesc) &&
                    track.TrackExpressions.All(e => e.abbr != abbr)) {
                    return projectDesc.CustomDefaultValue;
                }
                return descriptor.CustomDefaultValue;
            }
            return GetProjectDefault(project, abbr);
        }

        static float ClampToDescriptor(UProject project, UTrack? track, string abbr, float value) {
            if (track != null && track.TryGetExpDescriptor(project, abbr, out var descriptor)) {
                if (descriptor.max >= descriptor.min) {
                    return Math.Clamp(value, descriptor.min, descriptor.max);
                }
            } else if (project.expressions.TryGetValue(NormalizeAbbr(abbr), out var projectDesc)) {
                if (projectDesc.max >= projectDesc.min) {
                    return Math.Clamp(value, projectDesc.min, projectDesc.max);
                }
            }
            return value;
        }

        public static bool ApproximatelyEqual(float a, float b) =>
            Math.Abs(a - b) < 0.0001f;
    }
}
