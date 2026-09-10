using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using OpenUtau.Core.Render;
using Serilog;

namespace OpenUtau.Core.DiffSinger {
    /// <summary>
    /// Builds tokens_b / blend tensors for DiffSinger models that support phoneme embedding blend.
    /// UI weight 0–100 maps to 0–1. Empty B or weight 0 leaves zeros (inactive).
    /// </summary>
    public static class DiffSingerPhonemeBlend {
        public static bool Supports(InferenceSession acousticSession) {
            if (acousticSession == null) {
                return false;
            }
            return SupportsFromInputNames(acousticSession.InputNames);
        }

        /// <summary>True when the track singer is DiffSinger and acoustic ONNX has tokens_b + blend.</summary>
        public static bool Supports(OpenUtau.Core.Ustx.USinger? singer) {
            return singer is DiffSingerSinger dsSinger && dsSinger.SupportsPhonemeBlend;
        }

        public static bool SupportsFromInputNames(IEnumerable<string> inputNames) {
            if (inputNames == null) {
                return false;
            }
            var set = inputNames as ISet<string> ?? inputNames.ToHashSet(StringComparer.Ordinal);
            return set.Contains("tokens_b") && set.Contains("blend");
        }

        public static long[] BuildTokensB(
            long[] tokens,
            RenderPhone[] phones,
            Func<string, int?> tryTokenize) {
            if (phones == null) {
                throw new ArgumentNullException(nameof(phones));
            }
            var indexes = BuildLegacyPhoneIndexes(phones.Length);
            return BuildTokensB(tokens, phones, indexes, tryTokenize);
        }

        /// <summary>
        /// Copy of <paramref name="tokens"/>. For each segment with PhoneIndex &gt;= 0 and an active blend,
        /// replaces the corresponding token with the B phoneme token.
        /// Invalid B → leave A token (caller should zero that weight).
        /// </summary>
        public static long[] BuildTokensB(
            long[] tokens,
            RenderPhone[] phones,
            IReadOnlyList<int> segmentPhoneIndexes,
            Func<string, int?> tryTokenize) {
            if (tokens == null) {
                throw new ArgumentNullException(nameof(tokens));
            }
            if (phones == null) {
                throw new ArgumentNullException(nameof(phones));
            }
            if (segmentPhoneIndexes == null) {
                throw new ArgumentNullException(nameof(segmentPhoneIndexes));
            }
            if (tryTokenize == null) {
                throw new ArgumentNullException(nameof(tryTokenize));
            }
            var result = (long[])tokens.Clone();
            if (tokens.Length != segmentPhoneIndexes.Count) {
                Log.Warning(
                    "Phoneme blend: token count {TokenCount} != segment indexes ({Expected}); skipping tokens_b edits",
                    tokens.Length, segmentPhoneIndexes.Count);
                return result;
            }
            for (int i = 0; i < segmentPhoneIndexes.Count; i++) {
                int phoneIndex = segmentPhoneIndexes[i];
                if (phoneIndex < 0 || phoneIndex >= phones.Length) {
                    continue;
                }
                var phone = phones[phoneIndex];
                if (!IsActive(phone.blendPhoneme, phone.blendWeight)) {
                    continue;
                }
                string blend = phone.blendPhoneme!.Trim();
                int? tok = tryTokenize(blend);
                if (tok == null) {
                    Log.Warning("Phoneme blend: unsupported blend phoneme \"{Phoneme}\"; ignoring", blend);
                    continue;
                }
                result[i] = tok.Value;
            }
            return result;
        }

        /// <summary>
        /// Copy of <paramref name="tokens"/> (SP + phones + SP). For each phone with an active blend,
        /// replaces the corresponding token with the B phoneme token.
        /// Invalid B → leave A token (caller should zero that weight).
        /// </summary>
        public static long[] BuildTokensB(
            long[] tokens,
            IReadOnlyList<(string? blendPhoneme, int blendWeight)> blends,
            Func<string, int?> tryTokenize) {
            if (tokens == null) {
                throw new ArgumentNullException(nameof(tokens));
            }
            if (blends == null) {
                throw new ArgumentNullException(nameof(blends));
            }
            if (tryTokenize == null) {
                throw new ArgumentNullException(nameof(tryTokenize));
            }
            var result = (long[])tokens.Clone();
            // tokens layout: [SP, phone0, ..., phoneN-1, SP]
            int expected = blends.Count + 2;
            if (tokens.Length != expected) {
                Log.Warning(
                    "Phoneme blend: token count {TokenCount} != phones+2 ({Expected}); skipping tokens_b edits",
                    tokens.Length, expected);
                return result;
            }
            for (int i = 0; i < blends.Count; i++) {
                var (blendPhoneme, blendWeight) = blends[i];
                if (!IsActive(blendPhoneme, blendWeight)) {
                    continue;
                }
                string blend = blendPhoneme!.Trim();
                int? tok = tryTokenize(blend);
                if (tok == null) {
                    Log.Warning("Phoneme blend: unsupported blend phoneme \"{Phoneme}\"; ignoring", blend);
                    continue;
                }
                result[i + 1] = tok.Value;
            }
            return result;
        }

        public static float[] BuildTokenBlendWeights(long[] tokens, RenderPhone[] phones) {
            if (phones == null) {
                throw new ArgumentNullException(nameof(phones));
            }
            return BuildTokenBlendWeights(tokens, phones, BuildLegacyPhoneIndexes(phones.Length));
        }

        /// <summary>Per-token blend weights in 0–1; SP / gap segments always 0.</summary>
        public static float[] BuildTokenBlendWeights(
            long[] tokens,
            RenderPhone[] phones,
            IReadOnlyList<int> segmentPhoneIndexes) {
            if (tokens == null) {
                throw new ArgumentNullException(nameof(tokens));
            }
            if (phones == null) {
                throw new ArgumentNullException(nameof(phones));
            }
            if (segmentPhoneIndexes == null) {
                throw new ArgumentNullException(nameof(segmentPhoneIndexes));
            }
            var weights = new float[tokens.Length];
            if (tokens.Length != segmentPhoneIndexes.Count) {
                return weights;
            }
            for (int i = 0; i < segmentPhoneIndexes.Count; i++) {
                int phoneIndex = segmentPhoneIndexes[i];
                if (phoneIndex < 0 || phoneIndex >= phones.Length) {
                    continue;
                }
                var phone = phones[phoneIndex];
                if (!IsActive(phone.blendPhoneme, phone.blendWeight)) {
                    continue;
                }
                weights[i] = Math.Clamp(phone.blendWeight, 0, 100) / 100f;
            }
            return weights;
        }

        /// <summary>Per-token blend weights in 0–1; head/tail SP always 0.</summary>
        public static float[] BuildTokenBlendWeights(
            long[] tokens,
            IReadOnlyList<(string? blendPhoneme, int blendWeight)> blends) {
            if (tokens == null) {
                throw new ArgumentNullException(nameof(tokens));
            }
            if (blends == null) {
                throw new ArgumentNullException(nameof(blends));
            }
            var weights = new float[tokens.Length];
            int expected = blends.Count + 2;
            if (tokens.Length != expected) {
                return weights;
            }
            for (int i = 0; i < blends.Count; i++) {
                var (blendPhoneme, blendWeight) = blends[i];
                if (!IsActive(blendPhoneme, blendWeight)) {
                    continue;
                }
                weights[i + 1] = Math.Clamp(blendWeight, 0, 100) / 100f;
            }
            return weights;
        }

        public static float[] BuildTokenBlendWeights(
            long[] tokens,
            RenderPhone[] phones,
            Func<string, int?> tryTokenize) {
            if (phones == null) {
                throw new ArgumentNullException(nameof(phones));
            }
            return BuildTokenBlendWeights(tokens, phones, BuildLegacyPhoneIndexes(phones.Length), tryTokenize);
        }

        public static float[] BuildTokenBlendWeights(
            long[] tokens,
            RenderPhone[] phones,
            IReadOnlyList<int> segmentPhoneIndexes,
            Func<string, int?> tryTokenize) {
            var weights = BuildTokenBlendWeights(tokens, phones, segmentPhoneIndexes);
            if (tryTokenize == null || tokens.Length != segmentPhoneIndexes.Count) {
                return weights;
            }
            for (int i = 0; i < segmentPhoneIndexes.Count; i++) {
                int phoneIndex = segmentPhoneIndexes[i];
                if (phoneIndex < 0 || phoneIndex >= phones.Length) {
                    continue;
                }
                var phone = phones[phoneIndex];
                if (!IsActive(phone.blendPhoneme, phone.blendWeight)) {
                    continue;
                }
                if (tryTokenize(phone.blendPhoneme!.Trim()) == null) {
                    weights[i] = 0f;
                }
            }
            return weights;
        }

        /// <summary>
        /// Like <see cref="BuildTokenBlendWeights(long[],IReadOnlyList{ValueTuple{string,int}})"/> but zeros slots
        /// whose B failed to tokenize.
        /// </summary>
        public static float[] BuildTokenBlendWeights(
            long[] tokens,
            IReadOnlyList<(string? blendPhoneme, int blendWeight)> blends,
            Func<string, int?> tryTokenize) {
            var weights = BuildTokenBlendWeights(tokens, blends);
            if (tryTokenize == null || tokens.Length != blends.Count + 2) {
                return weights;
            }
            for (int i = 0; i < blends.Count; i++) {
                var (blendPhoneme, blendWeight) = blends[i];
                if (!IsActive(blendPhoneme, blendWeight)) {
                    continue;
                }
                if (tryTokenize(blendPhoneme!.Trim()) == null) {
                    weights[i + 1] = 0f;
                }
            }
            return weights;
        }

        static int[] BuildLegacyPhoneIndexes(int phoneCount) {
            var indexes = new int[phoneCount + 2];
            indexes[0] = -1;
            for (int i = 0; i < phoneCount; i++) {
                indexes[i + 1] = i;
            }
            indexes[^1] = -1;
            return indexes;
        }

        /// <summary>Expand per-token weights to per-frame using phone durations (same length as tokens).</summary>
        public static float[] ExpandToFrames(float[] tokenWeights, IReadOnlyList<int> durations) {
            if (tokenWeights == null) {
                throw new ArgumentNullException(nameof(tokenWeights));
            }
            if (durations == null) {
                throw new ArgumentNullException(nameof(durations));
            }
            if (tokenWeights.Length != durations.Count) {
                throw new ArgumentException(
                    $"tokenWeights length ({tokenWeights.Length}) != durations count ({durations.Count})");
            }
            int total = 0;
            for (int i = 0; i < durations.Count; i++) {
                total += Math.Max(0, durations[i]);
            }
            var frames = new float[total];
            int offset = 0;
            for (int i = 0; i < tokenWeights.Length; i++) {
                int d = Math.Max(0, durations[i]);
                float w = tokenWeights[i];
                for (int f = 0; f < d; f++) {
                    frames[offset++] = w;
                }
            }
            return frames;
        }

        public static bool IsActive(RenderPhone phone) {
            return phone != null && IsActive(phone.blendPhoneme, phone.blendWeight);
        }

        public static bool IsActive(string? blendPhoneme, int blendWeight) {
            return !string.IsNullOrWhiteSpace(blendPhoneme) && blendWeight > 0;
        }

        public static bool AnyActive(RenderPhone[] phones) {
            return phones != null && phones.Any(IsActive);
        }
    }
}
