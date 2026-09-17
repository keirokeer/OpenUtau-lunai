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

        public readonly struct RenderingRange {
            public readonly int TrackNo;
            public readonly ulong PhraseHash;
            public readonly double StartMs;
            public readonly double EndMs;

            public RenderingRange(int trackNo, ulong phraseHash, double startMs, double endMs) {
                TrackNo = trackNo;
                PhraseHash = phraseHash;
                StartMs = startMs;
                EndMs = endMs;
            }
        }

        sealed class RenderingEntry {
            public int TrackNo;
            public ulong PhraseHash;
            public List<(double startMs, double endMs)> Ranges = new();
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
        static readonly ConcurrentDictionary<string, RenderingEntry> rendering = new ConcurrentDictionary<string, RenderingEntry>();

        public static event Action? Changed;

        public static void Clear() {
            entries.Clear();
            rendering.Clear();
            Changed?.Invoke();
        }

        public static bool Remove(ulong phraseHash) {
            string key = phraseHash.ToString();
            bool removed = entries.TryRemove(key, out _);
            bool cleared = ClearRendering(phraseHash);
            return removed || cleared;
        }

        /// <summary>
        /// Mark absolute-ms ranges as currently rendering (waveform placeholder animation).
        /// Does not replace ranges already marked for this phrase (e.g. retake holes).
        /// </summary>
        public static void MarkRendering(
            int trackNo,
            ulong phraseHash,
            IEnumerable<(double startMs, double endMs)> absoluteRanges) {
            string key = phraseHash.ToString();
            if (rendering.ContainsKey(key)) {
                Changed?.Invoke();
                return;
            }
            var ranges = MergeRanges(absoluteRanges);
            if (ranges.Count == 0) {
                return;
            }
            rendering[key] = new RenderingEntry {
                TrackNo = trackNo,
                PhraseHash = phraseHash,
                Ranges = ranges,
            };
            Changed?.Invoke();
        }

        /// <summary>
        /// Replace rendering ranges for a phrase (used when clearing display for retake).
        /// Overlapping / adjacent note holes are merged into contiguous spans.
        /// </summary>
        public static void SetRendering(
            int trackNo,
            ulong phraseHash,
            IEnumerable<(double startMs, double endMs)> absoluteRanges) {
            var ranges = MergeRanges(absoluteRanges);
            string key = phraseHash.ToString();
            if (ranges.Count == 0) {
                ClearRendering(phraseHash);
                return;
            }
            rendering[key] = new RenderingEntry {
                TrackNo = trackNo,
                PhraseHash = phraseHash,
                Ranges = ranges,
            };
            Changed?.Invoke();
        }

        public static bool ClearRendering(ulong phraseHash) {
            if (rendering.TryRemove(phraseHash.ToString(), out _)) {
                Changed?.Invoke();
                return true;
            }
            return false;
        }

        public static bool HasRendering(int trackNo) {
            return rendering.Values.Any(e => e.TrackNo == trackNo);
        }

        public static IReadOnlyList<RenderingRange> GetRenderingRanges(int trackNo) {
            var list = new List<RenderingRange>();
            foreach (var entry in rendering.Values) {
                if (entry.TrackNo != trackNo) {
                    continue;
                }
                foreach (var (startMs, endMs) in entry.Ranges) {
                    list.Add(new RenderingRange(entry.TrackNo, entry.PhraseHash, startMs, endMs));
                }
            }
            // Merge across phrases on the same track so contiguous retake holes
            // draw as one animation band.
            return MergeRenderingRanges(list);
        }

        /// <summary>
        /// Merge overlapping or touching absolute-ms ranges (pad gap ≤ 1 ms).
        /// </summary>
        static List<(double startMs, double endMs)> MergeRanges(
            IEnumerable<(double startMs, double endMs)> absoluteRanges) {
            const double gapMs = 1.0;
            var sorted = absoluteRanges
                .Where(r => r.endMs > r.startMs)
                .OrderBy(r => r.startMs)
                .ToList();
            if (sorted.Count == 0) {
                return sorted;
            }
            var merged = new List<(double startMs, double endMs)>(sorted.Count);
            double start = sorted[0].startMs;
            double end = sorted[0].endMs;
            for (int i = 1; i < sorted.Count; i++) {
                if (sorted[i].startMs <= end + gapMs) {
                    end = Math.Max(end, sorted[i].endMs);
                } else {
                    merged.Add((start, end));
                    start = sorted[i].startMs;
                    end = sorted[i].endMs;
                }
            }
            merged.Add((start, end));
            return merged;
        }

        static IReadOnlyList<RenderingRange> MergeRenderingRanges(
            List<RenderingRange> ranges) {
            if (ranges.Count <= 1) {
                return ranges;
            }
            const double gapMs = 1.0;
            var sorted = ranges.OrderBy(r => r.StartMs).ToList();
            var merged = new List<RenderingRange>(sorted.Count);
            var cur = sorted[0];
            for (int i = 1; i < sorted.Count; i++) {
                var next = sorted[i];
                if (next.StartMs <= cur.EndMs + gapMs) {
                    cur = new RenderingRange(
                        cur.TrackNo,
                        cur.PhraseHash,
                        cur.StartMs,
                        Math.Max(cur.EndMs, next.EndMs));
                } else {
                    merged.Add(cur);
                    cur = next;
                }
            }
            merged.Add(cur);
            return merged;
        }

        /// <summary>
        /// Zero piano-roll display samples over absolute-ms ranges, keeping the rest
        /// of the phrase visible (e.g. during acoustic retake of selected notes).
        /// Does not drop the entry or touch play-seed <see cref="CacheEntry.Samples"/>.
        /// </summary>
        public static bool ClearDisplayRanges(
            ulong phraseHash,
            IEnumerable<(double startMs, double endMs)> absoluteRanges) {
            if (!entries.TryGetValue(phraseHash.ToString(), out var entry)) {
                return false;
            }
            float[] source = entry.WaveformSamples ?? entry.Samples;
            if (source.Length == 0) {
                return false;
            }
            float[] display = (float[])source.Clone();
            bool any = false;
            foreach (var (startMs, endMs) in absoluteRanges) {
                if (!(endMs > startMs)) {
                    continue;
                }
                int start = (int)Math.Floor((startMs - entry.PosMs) * 44100.0 / 1000.0);
                int end = (int)Math.Ceiling((endMs - entry.PosMs) * 44100.0 / 1000.0);
                start = Math.Clamp(start, 0, display.Length);
                end = Math.Clamp(end, 0, display.Length);
                if (end > start) {
                    Array.Clear(display, start, end - start);
                    any = true;
                }
            }
            if (!any) {
                return false;
            }
            entry.WaveformSamples = display;
            Changed?.Invoke();
            return true;
        }

        /// <summary>Zero the entire piano-roll display buffer for a phrase.</summary>
        public static bool ClearDisplayAll(ulong phraseHash) {
            if (!entries.TryGetValue(phraseHash.ToString(), out var entry)) {
                return false;
            }
            float[] source = entry.WaveformSamples ?? entry.Samples;
            if (source.Length == 0) {
                return false;
            }
            float[] display = (float[])source.Clone();
            Array.Clear(display, 0, display.Length);
            entry.WaveformSamples = display;
            Changed?.Invoke();
            return true;
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
                if (pair.Value.Samples.Length == samples.Length) {
                    continuity = true;
                    previousDedicatedWave = pair.Value.WaveformSamples;
                    double age = (DateTime.Now - pair.Value.RenderTime).TotalMilliseconds;
                    renderTime = age >= FadeDurationMs
                        ? DateTime.Now.AddMilliseconds(-FadeDurationMs - 1)
                        : pair.Value.RenderTime;
                }
                if (pair.Key != key) {
                    entries.TryRemove(pair.Key, out _);
                    rendering.TryRemove(pair.Key, out _);
                }
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
            bool clearedRendering = rendering.TryRemove(key, out _);
            if (visualChanged || !continuity || clearedRendering) {
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
                rendering.TryRemove(key, out _);
            }
            return true;
        }

        static float EaseOutCubic(float t) {
            return 1.0f - (float)Math.Pow(1.0 - t, 3);
        }
    }
}
