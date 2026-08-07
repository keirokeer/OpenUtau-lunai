using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace OpenUtau.Core.DiffSinger {
    /// <summary>
    /// Context for filling optional ONNX inputs (retake, gt_mel, tokens_b, blend, noise)
    /// so newer voicebanks load without missing-input errors.
    /// </summary>
    public sealed class DiffSingerOnnxFillContext {
        public int TotalFrames { get; init; }
        public int MelBins { get; init; } = 128;
        public int HiddenSize { get; init; } = 256;
        public int NumVariances { get; init; } = 1;
        public long[]? Tokens { get; init; }
        public Tensor<float>? EncoderOut { get; init; }
        public Tensor<float>? GtMel { get; init; }
        public bool[]? RetakeMask { get; init; }
        public float[]? BlendWeights { get; init; }
        public long[]? TokensB { get; init; }
        public int BlendLength { get; init; }
        /// <summary>Optional diffusion noise seed; when unset a stable default is used.</summary>
        public uint? NoiseSeed { get; init; }
        public ulong NoiseStage { get; init; } = DiffSingerNoise.StageAcoustic;
    }

    public static class DiffSingerOnnxExtras {
        /// <summary>
        /// Adds any session inputs that are not already present, using safe defaults
        /// so VerifyInputNames succeeds for newer models without breaking old banks.
        /// </summary>
        public static void FillMissingInputs(
            InferenceSession session,
            List<NamedOnnxValue> inputs,
            DiffSingerOnnxFillContext ctx) {
            // Prefer explicit context; otherwise reuse tensors already in the input list.
            if (ctx.EncoderOut == null) {
                var enc = inputs.FirstOrDefault(v => v.Name == "encoder_out");
                if (enc != null) {
                    ctx = CloneContext(ctx, encoderOut: enc.AsTensor<float>());
                }
            }
            if (ctx.Tokens == null) {
                var tok = inputs.FirstOrDefault(v => v.Name == "tokens");
                if (tok != null) {
                    ctx = CloneContext(ctx, tokens: tok.AsTensor<long>().ToArray());
                }
            }
            var given = inputs.Select(v => v.Name).ToHashSet(StringComparer.Ordinal);
            foreach (var name in session.InputNames) {
                if (given.Contains(name)) {
                    continue;
                }
                if (!session.InputMetadata.TryGetValue(name, out var meta)) {
                    continue;
                }
                var filled = TryCreateDefault(name, meta, ctx);
                if (filled != null) {
                    inputs.Add(filled);
                    given.Add(name);
                }
            }
        }

        static DiffSingerOnnxFillContext CloneContext(
            DiffSingerOnnxFillContext ctx,
            Tensor<float>? encoderOut = null,
            long[]? tokens = null) {
            return new DiffSingerOnnxFillContext {
                TotalFrames = ctx.TotalFrames,
                MelBins = ctx.MelBins,
                HiddenSize = ctx.HiddenSize,
                NumVariances = ctx.NumVariances,
                Tokens = tokens ?? ctx.Tokens,
                EncoderOut = encoderOut ?? ctx.EncoderOut,
                GtMel = ctx.GtMel,
                RetakeMask = ctx.RetakeMask,
                BlendWeights = ctx.BlendWeights,
                TokensB = ctx.TokensB,
                BlendLength = ctx.BlendLength,
                NoiseSeed = ctx.NoiseSeed,
                NoiseStage = ctx.NoiseStage,
            };
        }

        static NamedOnnxValue? TryCreateDefault(
            string name,
            NodeMetadata meta,
            DiffSingerOnnxFillContext ctx) {
            int t = Math.Max(1, ctx.TotalFrames);
            return name switch {
                "retake" => CreateRetake(meta, ctx, t),
                "gt_mel" => CreateGtMel(meta, ctx, t),
                "tokens_b" => CreateTokensB(meta, ctx),
                "blend" => CreateBlend(meta, ctx, t),
                "encoder_out_b" => CreateEncoderOutB(meta, ctx),
                "noise" => CreateNoise(meta, ctx, t),
                "shift_mouth_opening" => NamedOnnxValue.CreateFromTensor(
                    name,
                    new DenseTensor<float>(Enumerable.Repeat(0f, t).ToArray(), new[] { 1, t })),
                "mouth_opening" => NamedOnnxValue.CreateFromTensor(
                    name,
                    new DenseTensor<float>(Enumerable.Repeat(0.5f, t).ToArray(), new[] { 1, t })),
                _ => null,
            };
        }

        static NamedOnnxValue CreateRetake(NodeMetadata meta, DiffSingerOnnxFillContext ctx, int t) {
            var dims = ResolveDims(meta.Dimensions, fallback: new[] { 1, t }, (i, d) => {
                if (d > 0) {
                    return d;
                }
                if (i == 0) {
                    return 1;
                }
                if (i == 1) {
                    return t;
                }
                return Math.Max(1, ctx.NumVariances);
            });
            int count = Product(dims);
            bool[] values;
            if (ctx.RetakeMask != null && ctx.RetakeMask.Length == t && dims.Length == 2) {
                values = ctx.RetakeMask;
            } else if (ctx.RetakeMask != null && dims.Length == 3 && dims[2] > 0) {
                int n = dims[2];
                values = new bool[t * n];
                for (int i = 0; i < t; i++) {
                    bool keep = ctx.RetakeMask.Length == t ? ctx.RetakeMask[i] : true;
                    for (int c = 0; c < n; c++) {
                        values[i * n + c] = keep;
                    }
                }
            } else {
                values = Enumerable.Repeat(true, count).ToArray();
            }
            if (values.Length != count) {
                values = Enumerable.Repeat(true, count).ToArray();
            }
            return NamedOnnxValue.CreateFromTensor(
                "retake",
                new DenseTensor<bool>(values, dims, false));
        }

        static NamedOnnxValue CreateGtMel(NodeMetadata meta, DiffSingerOnnxFillContext ctx, int t) {
            var dims = ResolveDims(meta.Dimensions, fallback: new[] { 1, t, ctx.MelBins }, (i, d) => {
                if (d > 0) {
                    return d;
                }
                return i switch {
                    0 => 1,
                    1 => t,
                    _ => Math.Max(1, ctx.MelBins),
                };
            });
            if (ctx.GtMel != null
                && ctx.GtMel.Dimensions.Length == dims.Length
                && Enumerable.Range(0, dims.Length).All(i => ctx.GtMel.Dimensions[i] == dims[i])) {
                return NamedOnnxValue.CreateFromTensor("gt_mel", ctx.GtMel);
            }
            return NamedOnnxValue.CreateFromTensor(
                "gt_mel",
                new DenseTensor<float>(new float[Product(dims)], dims));
        }

        static NamedOnnxValue CreateTokensB(NodeMetadata meta, DiffSingerOnnxFillContext ctx) {
            var tokens = ctx.TokensB ?? ctx.Tokens
                ?? throw new InvalidOperationException("tokens_b required but no tokens provided.");
            int n = tokens.Length;
            var dims = NormalizeDims(meta.Dimensions, fallback: new[] { 1, n });
            if (dims.Length == 2) {
                int s = dims[0] <= 0 ? 1 : dims[0];
                int tok = dims[1] <= 0 ? n : dims[1];
                var data = new long[s * tok];
                for (int i = 0; i < s; i++) {
                    for (int j = 0; j < tok; j++) {
                        data[i * tok + j] = j < tokens.Length ? tokens[j] : 0;
                    }
                }
                return NamedOnnxValue.CreateFromTensor(
                    "tokens_b",
                    new DenseTensor<long>(data, new[] { s, tok }, false));
            }
            return NamedOnnxValue.CreateFromTensor(
                "tokens_b",
                new DenseTensor<long>(tokens, new[] { 1, n }, false));
        }

        static NamedOnnxValue CreateBlend(NodeMetadata meta, DiffSingerOnnxFillContext ctx, int t) {
            var dims = NormalizeDims(meta.Dimensions, fallback: new[] { 1, t });
            if (dims.Length == 2) {
                int s = dims[0] <= 0 ? 1 : dims[0];
                int len;
                if (dims[1] > 0) {
                    len = dims[1];
                } else if (ctx.BlendLength > 0) {
                    len = ctx.BlendLength;
                } else if (t > 0) {
                    len = t;
                } else {
                    len = ctx.Tokens?.Length ?? 1;
                }
                var data = new float[s * len];
                if (ctx.BlendWeights != null && ctx.BlendWeights.Length == len && s >= 1) {
                    Array.Copy(ctx.BlendWeights, 0, data, 0, len);
                    NormalizeBlendSlots(data, s, len);
                }
                return NamedOnnxValue.CreateFromTensor(
                    "blend",
                    new DenseTensor<float>(data, new[] { s, len }));
            }
            return NamedOnnxValue.CreateFromTensor(
                "blend",
                new DenseTensor<float>(new float[Product(dims)], dims));
        }

        static NamedOnnxValue CreateEncoderOutB(NodeMetadata meta, DiffSingerOnnxFillContext ctx) {
            if (ctx.EncoderOut == null) {
                throw new InvalidOperationException("encoder_out_b required but encoder_out is missing.");
            }
            var src = ctx.EncoderOut;
            int t = src.Dimensions.Length >= 2 ? src.Dimensions[1] : ctx.TotalFrames;
            int h = src.Dimensions.Length >= 3 ? src.Dimensions[2] : ctx.HiddenSize;
            var dims = NormalizeDims(meta.Dimensions, fallback: new[] { 1, t, h });
            int s = dims[0] <= 0 ? 1 : dims[0];
            var flat = src.ToArray();
            var data = new float[s * flat.Length];
            for (int i = 0; i < s; i++) {
                Array.Copy(flat, 0, data, i * flat.Length, flat.Length);
            }
            return NamedOnnxValue.CreateFromTensor(
                "encoder_out_b",
                new DenseTensor<float>(data, new[] { s, t, h }));
        }

        /// <summary>Gaussian noise with the model's expected layout (zeros break diffusion quality).</summary>
        static NamedOnnxValue CreateNoise(NodeMetadata meta, DiffSingerOnnxFillContext ctx, int t) {
            int defaultBins = Math.Max(1, ctx.MelBins);
            int rank = meta.Dimensions != null && meta.Dimensions.Length > 0 ? meta.Dimensions.Length : 4;
            var dims = ResolveDims(meta.Dimensions, fallback: new[] { 1, 1, defaultBins, t }, (i, d) => {
                if (d > 0) {
                    return d;
                }
                if (i == rank - 1) {
                    return t;
                }
                if (rank == 4 && i == 1) {
                    return Math.Max(1, ctx.NumVariances);
                }
                if (rank == 4 && i == 2) {
                    return defaultBins;
                }
                return 1;
            });
            // Expected layout [1, feats, outDims, T] for tlb-style predictors.
            int feats = dims.Length >= 2 ? dims[1] : 1;
            int outDims = dims.Length >= 3 ? dims[2] : defaultBins;
            int nFrames = dims.Length >= 4 ? dims[3] : t;
            uint seed = ctx.NoiseSeed ?? 1u;
            var tensor = DiffSingerNoise.Create(ctx.NoiseStage, seed, feats, outDims, nFrames);
            // If ONNX rank/layout differs from the standard 4D tensor, fall back to flat Gaussian fill.
            if (dims.Length != 4 || dims[0] != 1 || dims[1] != feats || dims[2] != outDims || dims[3] != nFrames) {
                var data = new float[Product(dims)];
                for (int i = 0; i < data.Length; i++) {
                    data[i] = DiffSingerNoise.Gaussian(ctx.NoiseStage, seed, (ulong)(i / Math.Max(1, nFrames)), 0, (ulong)(i % Math.Max(1, nFrames)));
                }
                return NamedOnnxValue.CreateFromTensor("noise", new DenseTensor<float>(data, dims));
            }
            return NamedOnnxValue.CreateFromTensor("noise", tensor);
        }

        public static void NormalizeBlendSlots(float[] data, int slots, int length) {
            for (int j = 0; j < length; j++) {
                float sum = 0;
                for (int i = 0; i < slots; i++) {
                    float v = Math.Clamp(data[i * length + j], 0f, 1f);
                    data[i * length + j] = v;
                    sum += v;
                }
                if (sum > 1f && sum > 1e-6f) {
                    for (int i = 0; i < slots; i++) {
                        data[i * length + j] /= sum;
                    }
                }
            }
        }

        static int[] NormalizeDims(int[] dims, int[] fallback) {
            if (dims == null || dims.Length == 0) {
                return (int[])fallback.Clone();
            }
            var result = new int[dims.Length];
            for (int i = 0; i < dims.Length; i++) {
                result[i] = dims[i];
            }
            return result;
        }

        static int[] ResolveDims(int[] dims, int[] fallback, Func<int, int, int> resolve) {
            var raw = NormalizeDims(dims, fallback);
            var result = new int[raw.Length];
            for (int i = 0; i < raw.Length; i++) {
                result[i] = resolve(i, raw[i]);
                if (result[i] <= 0) {
                    result[i] = 1;
                }
            }
            return result;
        }

        static int Product(int[] dims) {
            int p = 1;
            foreach (var d in dims) {
                if (d <= 0) {
                    throw new InvalidOperationException(
                        $"Refusing to allocate ONNX tensor with non-positive dim {d} in [{string.Join(",", dims)}]");
                }
                p *= d;
            }
            return p;
        }
    }
}
