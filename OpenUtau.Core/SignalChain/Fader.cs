using System;

namespace OpenUtau.Core.SignalChain {
    public class Fader : ISignalSource {
        const float MuteReadyEpsilon = 1e-4f;

        private readonly ISignalSource source;
        private float pan = 0;
        private float scale = 1;
        private float scaleTarget = 1;
        private float[] scaleBuffer;

        public Fader(ISignalSource source) {
            this.source = source;
        }

        public float Scale {
            get => scaleTarget;
            set => scaleTarget = value;
        }

        public float Pan {
            get => pan;
            set => pan = value;
        }

        public void SetScaleToTarget() {
            scale = scaleTarget;
        }

        public bool IsReady(int position, int count) {
            // Muted tracks must not stall HoldWhenUnready for the whole mix —
            // otherwise solo/mute leaves Pending phrases on silent tracks causing
            // micro-pauses on every render segment.
            if (scaleTarget <= MuteReadyEpsilon && scale <= MuteReadyEpsilon) {
                return true;
            }
            return source.IsReady(position, count);
        }

        public int Mix(int position, float[] buffer, int index, int count) {
            if (scaleBuffer == null || scaleBuffer.Length < count) {
                scaleBuffer = new float[count];
            }
            for (int i = 0; i < count; ++i) {
                scaleBuffer[i] = 0;
            }
            (float volumeLeft, float volumeRight) = MusicMath.PanToChannelVolumes(pan);
            int ret = source.Mix(position, scaleBuffer, 0, count);
            // Mute/solo jumps use a faster ramp (~5–6 ms) so unmute feels snappy
            // without the click of SetScaleToTarget.
            float step = Math.Abs(scaleTarget - scale) > 0.3f ? 0.002f : 0.0005f;
            for (int i = 0; i < count; ++i) {
                if (scaleTarget > scale) {
                    scale = Math.Min(scaleTarget, scale + step);
                } else if (scaleTarget < scale) {
                    scale = Math.Max(scaleTarget, scale - step);
                }
                buffer[index + i] += scaleBuffer[i] * scale * (i % 2 == 0 ? volumeLeft : volumeRight);
            }
            return ret;
        }
    }
}
