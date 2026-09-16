using System;
using NAudio.Wave;

namespace OpenUtau.Core.SignalChain {
    class MasterAdapter : ISampleProvider {
        private const int SampleRate = 44100;
        private const int Channels = 2;
        // Short edge fades prevent clicks without mixing audio past the playback end.
        private const int FadeMilliseconds = 5;
        private const int FadeFrames = SampleRate * FadeMilliseconds / 1000;

        private readonly WaveFormat waveFormat;
        private readonly ISignalSource source;
        private readonly int endPosition;
        private int position;
        private int startPosition;

        public WaveFormat WaveFormat => waveFormat;
        public int Waited { get; private set; }
        public bool IsWaiting { get; private set; }

        /// <summary>
        /// Hold mode (default): while the source is not ready the
        /// adapter returns silence and accumulates <see cref="Waited"/> so the playhead
        /// stays put until the streaming render catches up.
        /// Passthrough mode (loop playback): missing audio is simply silence and the
        /// position advances, so the loop keeps its tempo and late audio pops in on the
        /// next pass.
        /// </summary>
        public bool HoldWhenUnready { get; set; } = true;
        public MasterAdapter(ISignalSource source, double endMs = double.PositiveInfinity) {
            waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels);
            this.source = source;
            endPosition = double.IsPositiveInfinity(endMs)
                ? -1
                : (int)(endMs * SampleRate / 1000) * Channels;
        }

        public int Read(float[] buffer, int offset, int count) {
            if (endPosition >= 0) {
                count = Math.Min(count, endPosition - position);
                if (count <= 0) {
                    return 0;
                }
            }
            for (int i = offset; i < offset + count; ++i) {
                buffer[i] = 0;
            }

            if (!HoldWhenUnready) {
                return ReadPassthrough(buffer, offset, count);
            }

            int readyCount = source.IsReady(position, count)
                ? count
                : LargestReadyCount(source, position, count);

            if (readyCount <= 0) {
                Waited += count;
                IsWaiting = true;
                return count;
            }

            // Silence → audio (render caught up): edge fade so the first
            // non-silent samples do not click under the playhead.
            if (IsWaiting) {
                startPosition = position;
            }
            int readPosition = position;
            int pos = source.Mix(position, buffer, offset, readyCount);
            int n = Math.Max(0, pos - position);
            position = pos;
            ApplyEdgeGains(buffer, offset, n, readPosition);

            if (readyCount < count) {
                // Buffer straddles into Pending: play the ready prefix, fade out,
                // hold the rest until that segment renders (clock must not skip it).
                if (n > 0) {
                    ApplyTailFadeOut(buffer, offset, n);
                }
                Waited += count - n;
                IsWaiting = true;
                return count;
            }

            IsWaiting = false;
            return n;
        }

        int ReadPassthrough(float[] buffer, int offset, int count) {
            int readPosition = position;
            int pos = source.Mix(position, buffer, offset, count);
            int n = Math.Max(0, pos - position);
            position = pos;
            ApplyEdgeGains(buffer, offset, n, readPosition);
            IsWaiting = false;
            return n;
        }

        void ApplyEdgeGains(float[] buffer, int offset, int n, int readPosition) {
            int startFrame = startPosition / Channels;
            int endFrame = endPosition / Channels;
            for (int i = 0; i < n; ++i) {
                int frame = (readPosition + i) / Channels;
                int elapsedFrames = frame - startFrame;
                float gain = Math.Clamp(
                    elapsedFrames / (float)Math.Max(1, FadeFrames - 1),
                    0,
                    1);
                if (endPosition >= 0) {
                    int remainingFrames = endFrame - frame;
                    if (remainingFrames <= FadeFrames) {
                        gain = Math.Min(
                            gain,
                            Math.Clamp(
                                (remainingFrames - 1f) / Math.Max(1, FadeFrames - 1),
                                0,
                                1));
                    }
                }
                if (gain < 1) {
                    buffer[offset + i] *= gain;
                }
            }
        }

        static void ApplyTailFadeOut(float[] buffer, int offset, int n) {
            int fadeSamples = Math.Min(n, FadeFrames * Channels);
            int start = offset + n - fadeSamples;
            for (int i = 0; i < fadeSamples; ++i) {
                float gain = 1f - (i / (float)Math.Max(1, fadeSamples - 1));
                buffer[start + i] *= gain;
            }
        }

        /// <summary>
        /// Largest frame-aligned count in [0, count] for which the source is ready
        /// from <paramref name="position"/>. Avoids holding an entire device buffer
        /// when only its tail overlaps a still-Pending phrase.
        /// </summary>
        internal static int LargestReadyCount(ISignalSource source, int position, int count) {
            if (count <= 0) {
                return 0;
            }
            if (source.IsReady(position, count)) {
                return count;
            }
            count -= count % Channels;
            if (count <= 0) {
                return 0;
            }
            if (!source.IsReady(position, Channels)) {
                return 0;
            }
            int lo = Channels;
            int hi = count;
            while (lo + Channels < hi) {
                int mid = ((lo + hi) / 2 / Channels) * Channels;
                if (mid <= lo) {
                    mid = lo + Channels;
                }
                if (source.IsReady(position, mid)) {
                    lo = mid;
                } else {
                    hi = mid;
                }
            }
            return lo;
        }

        public void SetPosition(int position) {
            this.position = position;
            startPosition = position;
            Waited = 0;
            IsWaiting = false;
        }
    }
}
