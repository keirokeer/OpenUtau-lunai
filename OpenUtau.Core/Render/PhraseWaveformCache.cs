using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace OpenUtau.Core.Render {
    /// <summary>Per-phrase rendered audio used for incremental piano-roll waveform display.</summary>
    public static class PhraseWaveformCache {
        public const double FadeDurationMs = 220;
        const double SameSlotPosEpsilonMs = 1.0;

        public readonly struct Entry {
            public readonly int TrackNo;
            public readonly double PosMs;
            /// <summary>Play-seed samples (authoritative audio).</summary>
            public readonly float[] Samples;
            /// <summary>Piano-roll display samples (may be locally composed).</summary>
            public readonly float[] WaveformSamples;
            public readonly DateTime RenderTime;
            public readonly DateTime? FadeOutSince;

            internal Entry(CacheEntry entry) {
                TrackNo = entry.TrackNo;
                PosMs = entry.PosMs;
                Samples = entry.Samples;
                WaveformSamples = entry.WaveformSamples ?? entry.Samples;
                RenderTime = entry.RenderTime;
                FadeOutSince = entry.FadeOutSince;
            }
        }

        internal sealed class CacheEntry {
            public int TrackNo;
            public double PosMs;
            public float[] Samples = Array.Empty<float>();
            public float[]? WaveformSamples;
            public DateTime RenderTime;
            public DateTime? FadeOutSince;
        }

        static readonly ConcurrentDictionary<string, CacheEntry> entries = new ConcurrentDictionary<string, CacheEntry>();

        public static event Action? Changed;

        public static void Clear() {
            entries.Clear();
            Changed?.Invoke();
        }

        public static bool Remove(ulong phraseHash) {
            if (entries.TryRemove(phraseHash.ToString(), out _)) {
                Changed?.Invoke();
                return true;
            }
            return false;
        }

        public static bool TryGet(ulong phraseHash, out Entry entry) {
            if (entries.TryGetValue(phraseHash.ToString(), out var cached)) {
                entry = new Entry(cached);
                return true;
            }
            entry = default;
            return false;
        }

        /// <summary>
        /// Drop phrases no longer in the layout. Same-slot predecessors (same PosMs)
        /// are left for <see cref="Put"/> to replace without a full-phrase fade.
        /// </summary>
        public static void RemoveStaleForTrack(int trackNo, IEnumerable<ulong> keepHashes) {
            RemoveStaleForTrack(trackNo, keepHashes.Select(hash => (hash, double.NaN)));
        }

        public static void RemoveStaleForTrack(
            int trackNo,
            IEnumerable<(ulong hash, double posMs)> keepSlots) {
            var keepList = keepSlots.ToList();
            var keep = keepList.Select(slot => slot.hash.ToString()).ToHashSet();
            var keepPositions = keepList
                .Where(slot => !double.IsNaN(slot.posMs))
                .Select(slot => slot.posMs)
                .ToList();
            bool anyChanged = false;
            foreach (var pair in entries) {
                if (pair.Value.TrackNo != trackNo || keep.Contains(pair.Key)) {
                    continue;
                }
                bool sameSlotPending = keepPositions.Any(pos =>
                    Math.Abs(pos - pair.Value.PosMs) <= SameSlotPosEpsilonMs);
                if (sameSlotPending) {
                    // Put will replace this slot in-place; don't start a phrase-wide fade.
                    continue;
                }
                if (!pair.Value.FadeOutSince.HasValue) {
                    pair.Value.FadeOutSince = DateTime.Now;
                    anyChanged = true;
                }
            }
            if (PurgeCompletedFadeOuts()) {
                anyChanged = true;
            }
            if (anyChanged) {
                Changed?.Invoke();
            }
        }

        public static void Put(int trackNo, ulong phraseHash, double posMs, float[] samples) {
            Put(trackNo, phraseHash, posMs, samples, waveformSamples: null);
        }

        /// <summary>
        /// Store phrase audio. When a same-slot entry exists (track + pos + length),
        /// replaces it without fade-out/fade-in. Returns whether the piano-roll
        /// display buffer changed (false on replay wav-cache hits that only refresh play samples).
        /// </summary>
        public static bool Put(
            int trackNo,
            ulong phraseHash,
            double posMs,
            float[] samples,
            float[]? waveformSamples) {
            var key = phraseHash.ToString();
            DateTime renderTime = DateTime.Now;
            bool continuity = false;
            float[]? previousDedicatedWave = null;

            foreach (var pair in entries.ToArray()) {
                if (pair.Value.TrackNo != trackNo) {
                    continue;
                }
                if (Math.Abs(pair.Value.PosMs - posMs) > SameSlotPosEpsilonMs) {
                    continue;
                }
                if (pair.Value.Samples.Length != samples.Length) {
                    continue;
                }
                continuity = true;
                previousDedicatedWave = pair.Value.WaveformSamples;
                double age = (DateTime.Now - pair.Value.RenderTime).TotalMilliseconds;
                renderTime = age >= FadeDurationMs
                    ? DateTime.Now.AddMilliseconds(-FadeDurationMs - 1)
                    : pair.Value.RenderTime;
                if (pair.Key != key) {
                    entries.TryRemove(pair.Key, out _);
                }
                break;
            }

            float[]? storedWave;
            bool visualChanged;
            if (waveformSamples != null && !ReferenceEquals(waveformSamples, samples)) {
                // Explicit display buffer (e.g. DiffSinger local retake HardCompose).
                storedWave = waveformSamples;
                visualChanged = true;
            } else if (waveformSamples == null &&
                       continuity &&
                       previousDedicatedWave != null &&
                       previousDedicatedWave.Length == samples.Length) {
                // Replay / wav-cache hit: keep the composed display so the wave doesn't flash again.
                storedWave = previousDedicatedWave;
                visualChanged = false;
            } else {
                storedWave = null;
                visualChanged = !continuity || previousDedicatedWave != null;
            }

            entries[key] = new CacheEntry {
                TrackNo = trackNo,
                PosMs = posMs,
                Samples = samples,
                WaveformSamples = storedWave,
                RenderTime = continuity ? renderTime : DateTime.Now,
                FadeOutSince = null,
            };
            if (visualChanged || !continuity) {
                Changed?.Invoke();
                return true;
            }
            return false;
        }

        public static IEnumerable<Entry> GetForTrack(int trackNo) {
            PurgeCompletedFadeOuts();
            return entries.Values
                .Where(entry => entry.TrackNo == trackNo)
                .Select(entry => new Entry(entry));
        }

        public static float GetVisualScale(in Entry entry, ref bool needsAnotherFrame) {
            if (entry.FadeOutSince.HasValue) {
                double fadeOutAge = (DateTime.Now - entry.FadeOutSince.Value).TotalMilliseconds;
                double fadeOutProgress = Math.Clamp(fadeOutAge / FadeDurationMs, 0.0, 1.0);
                if (fadeOutProgress < 1.0) {
                    needsAnotherFrame = true;
                }
                return 1.0f - EaseOutCubic((float)fadeOutProgress);
            }
            double fadeInAge = (DateTime.Now - entry.RenderTime).TotalMilliseconds;
            double fadeInProgress = Math.Clamp(fadeInAge / FadeDurationMs, 0.0, 1.0);
            if (fadeInProgress < 1.0) {
                needsAnotherFrame = true;
            }
            return EaseOutCubic((float)fadeInProgress);
        }

        static bool PurgeCompletedFadeOuts() {
            var removeKeys = entries
                .Where(pair => pair.Value.FadeOutSince.HasValue
                    && (DateTime.Now - pair.Value.FadeOutSince.Value).TotalMilliseconds >= FadeDurationMs)
                .Select(pair => pair.Key)
                .ToArray();
            if (removeKeys.Length == 0) {
                return false;
            }
            foreach (var key in removeKeys) {
                entries.TryRemove(key, out _);
            }
            return true;
        }

        static float EaseOutCubic(float t) {
            return 1.0f - (float)Math.Pow(1.0 - t, 3);
        }
    }
}
