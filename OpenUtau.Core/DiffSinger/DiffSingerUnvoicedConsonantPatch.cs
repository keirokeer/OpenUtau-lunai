using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Core.Render;
using OpenUtau.Core.Util;

namespace OpenUtau.Core.DiffSinger {
    readonly struct UnvoicedFrameRange {
        public readonly int start;
        public readonly int end;

        public UnvoicedFrameRange(int start, int end) {
            this.start = start;
            this.end = end;
        }
    }

    /// <summary>
    /// Smooths acoustic-model F0 on unvoiced consonants before diffusion render.
    /// Target phonemes come from the voicebank dsunvoiced.yaml file.
    /// </summary>
    internal static class DiffSingerUnvoicedConsonantPatch {
        const float CrossfadeMs = 60f;

        public static void ApplyAcousticF0(RenderPhrase phrase, IReadOnlyList<int> durations, float frameMs, float[] f0) {
            if (f0 == null || f0.Length == 0
                || !Preferences.Default.DiffSingerUnvoicedConsonantAcousticF0Interpolate
                || phrase.singer is not DiffSingerSinger singer
                || singer.unvoicedPhonemes.Count == 0) {
                return;
            }
            var phonemes = phrase.phones.Select(p => p.phoneme).ToArray();
            ApplyAcousticF0(phonemes, durations, frameMs, f0, singer.unvoicedPhonemes);
        }

        /// <summary>Builds per-frame acoustic F0 (Hz) after the interpolate patch for piano-roll preview.</summary>
        public static bool TryBuildAcousticF0Preview(RenderPhrase phrase, out float frameMs, out float[] acousticF0Hz) {
            frameMs = 0f;
            acousticF0Hz = Array.Empty<float>();
            if (phrase.singer is not DiffSingerSinger singer || singer.unvoicedPhonemes.Count == 0) {
                return false;
            }
            try {
                var vocoder = singer.getVocoder();
                frameMs = vocoder.frameMs();
                int headFrames = DiffSingerUtils.headFrames;
                int tailFrames = DiffSingerUtils.tailFrames;
                var durations = DiffSingerUtils.PaddedPhoneDurations(phrase, frameMs, headFrames, tailFrames).ToList();
                int totalFrames = durations.Sum();
                acousticF0Hz = DiffSingerUtils.SampleCurve(
                    phrase, phrase.pitches, 0, frameMs, totalFrames, headFrames, tailFrames,
                    x => MusicMath.ToneToFreq(x * 0.01)).Select(f => (float)f).ToArray();
                var phonemes = phrase.phones.Select(p => p.phoneme).ToArray();
                ApplyAcousticF0(phonemes, durations, frameMs, acousticF0Hz, singer.unvoicedPhonemes);
                return acousticF0Hz.Length > 0;
            } catch {
                return false;
            }
        }

        internal static void ApplyAcousticF0(
            IReadOnlyList<string> phonemes,
            IReadOnlyList<int> durations,
            float frameMs,
            float[] f0,
            IReadOnlySet<string> targetPhonemes) {
            if (!Preferences.Default.DiffSingerUnvoicedConsonantAcousticF0Interpolate || targetPhonemes.Count == 0) {
                return;
            }
            var ranges = FindTargetPhoneRanges(phonemes, durations, targetPhonemes);
            if (ranges.Count == 0) {
                return;
            }
            ApplyInterpolation(f0, ranges, frameMs);
        }

        static void ApplyInterpolation(float[] f0, IReadOnlyList<UnvoicedFrameRange> ranges, float frameMs) {
            int crossfadeFrames = Math.Clamp((int)Math.Round(CrossfadeMs / frameMs), 1, 20);
            var weights = BuildWeights(f0.Length, ranges, crossfadeFrames);
            foreach (var range in ranges) {
                int left = range.start - 1;
                int right = range.end;
                float f0Left = left >= 0 ? f0[left] : f0[Math.Clamp(range.start, 0, f0.Length - 1)];
                float f0Right = right < f0.Length ? f0[right] : f0[Math.Clamp(range.end - 1, 0, f0.Length - 1)];
                int span = range.end - range.start;
                for (int i = range.start; i < range.end; ++i) {
                    float weight = weights[i];
                    if (weight <= 0f) {
                        continue;
                    }
                    float t = span <= 1 ? 0.5f : (i - range.start) / (span - 1f);
                    float target = f0Left + (f0Right - f0Left) * t;
                    f0[i] = f0[i] + weight * (target - f0[i]);
                }
            }
        }

        internal static List<UnvoicedFrameRange> FindTargetPhoneRanges(
            IReadOnlyList<string> phonemes,
            IReadOnlyList<int> durations,
            IReadOnlySet<string> targetPhonemes) {
            var ranges = new List<UnvoicedFrameRange>();
            if (phonemes.Count == 0 || durations.Count < phonemes.Count + 2 || targetPhonemes.Count == 0) {
                return ranges;
            }
            int frame = durations[0];
            int runStart = -1;
            for (int phoneIndex = 0; phoneIndex < phonemes.Count; ++phoneIndex) {
                int start = frame;
                frame += durations[phoneIndex + 1];
                if (targetPhonemes.Contains(phonemes[phoneIndex])) {
                    if (runStart < 0) {
                        runStart = start;
                    }
                } else if (runStart >= 0) {
                    ranges.Add(new UnvoicedFrameRange(runStart, start));
                    runStart = -1;
                }
            }
            if (runStart >= 0) {
                ranges.Add(new UnvoicedFrameRange(runStart, frame));
            }
            return ranges;
        }

        static float[] BuildWeights(int length, IReadOnlyList<UnvoicedFrameRange> ranges, int crossfadeFrames) {
            var weights = new float[length];
            foreach (var range in ranges) {
                int start = Math.Clamp(range.start, 0, length);
                int end = Math.Clamp(range.end, start, length);
                for (int i = start; i < end; ++i) {
                    weights[i] = 1f;
                }
                int leftStart = Math.Max(0, start - crossfadeFrames);
                int leftLength = start - leftStart;
                for (int i = leftStart; i < start; ++i) {
                    float weight = (float)(i - leftStart + 1) / (leftLength + 1);
                    weights[i] = Math.Max(weights[i], weight);
                }
                int rightEnd = Math.Min(length, end + crossfadeFrames);
                int rightLength = rightEnd - end;
                for (int i = end; i < rightEnd; ++i) {
                    float weight = 1f - (float)(i - end + 1) / (rightLength + 1);
                    weights[i] = Math.Max(weights[i], weight);
                }
            }
            return weights;
        }
    }
}
