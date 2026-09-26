using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace OpenUtau.Core.ExpressionGraph {
    /// <summary>What a node computes from, at each tick of the grid.</summary>
    public readonly struct NodeArgs {
        public readonly float[][] Inputs;
        public readonly CompiledNode Node;
        public readonly GraphContext Context;
        /// <summary>Part-relative ticks.</summary>
        public readonly int[] Ticks;

        public NodeArgs(float[][] inputs, CompiledNode node, GraphContext context, int[] ticks) {
            Inputs = inputs;
            Node = node;
            Context = context;
            Ticks = ticks;
        }
    }

    public enum GraphNodeRole { Input, Process, CurveOutput }

    /// <summary>
    /// A node type. An unconnected input port takes the node's parameter of the same name, else the port's default.
    /// </summary>
    public sealed class GraphNodeType {
        public readonly string Name;
        public readonly GraphNodeRole Role;
        public readonly string[] Ports;
        public readonly float[] PortDefaults;
        readonly Func<NodeArgs, float[]> evaluate;

        public GraphNodeType(string name, GraphNodeRole role, string[] ports, float[] portDefaults, Func<NodeArgs, float[]> evaluate) {
            Name = name;
            Role = role;
            Ports = ports;
            PortDefaults = portDefaults;
            this.evaluate = evaluate;
        }

        internal float[] Evaluate(NodeArgs args) => evaluate(args);
    }

    public static class GraphNodeTypes {
        public const string Constant = "constant";
        public const string CurveInput = "curve_input";
        public const string CurveOutput = "curve_output";
        public const string Add = "add";
        public const string Subtract = "subtract";
        public const string Multiply = "multiply";
        public const string Divide = "divide";
        public const string Min = "min";
        public const string Max = "max";
        public const string Mix = "mix";
        public const string Abs = "abs";
        public const string MapRange = "map_range";
        public const string Clamp = "clamp";
        public const string Lfo = "lfo";
        public const string NotePosition = "note_position";
        public const string NoteEnvelope = "note_envelope";

        /// <summary>The port of single-input nodes.</summary>
        public const string Value = "value";

        static readonly string[] none = Array.Empty<string>();
        static readonly string[] value = { Value };
        static readonly string[] ab = { "a", "b" };

        static readonly Dictionary<string, GraphNodeType> types = new[] {
            new GraphNodeType(Constant, GraphNodeRole.Input, none, new float[0],
                a => Fill(a, a.Node.GetFloat("value", 0))),
            new GraphNodeType(CurveInput, GraphNodeRole.Input, none, new float[0],
                a => Map(a, tick => a.Context.SampleCurve(a.Node.GetString("abbr"), tick))),
            new GraphNodeType(CurveOutput, GraphNodeRole.CurveOutput, value, new float[] { 0 },
                a => (float[])a.Inputs[0].Clone()),

            Binary(Add, 0, 0, (x, y) => x + y),
            Binary(Subtract, 0, 0, (x, y) => x - y),
            Binary(Multiply, 1, 1, (x, y) => x * y),
            Binary(Divide, 0, 1, (x, y) => y == 0 ? 0 : x / y),
            Binary(Min, 0, 0, Math.Min),
            Binary(Max, 0, 0, Math.Max),
            // A to B by the factor, held within 0 to 1: e.g. a 0/1 mask picks one input or the other.
            new GraphNodeType(Mix, GraphNodeRole.Process, new[] { "a", "b", "factor" }, new float[] { 0, 0, 0.5f }, a => {
                var x = a.Inputs[0];
                var y = a.Inputs[1];
                var t = a.Inputs[2];
                var result = new float[a.Ticks.Length];
                for (int i = 0; i < result.Length; ++i) {
                    float f = Math.Clamp(t[i], 0, 1);
                    result[i] = x[i] + (y[i] - x[i]) * f;
                }
                return result;
            }),
            new GraphNodeType(Abs, GraphNodeRole.Process, value, new float[] { 0 },
                a => Unary(a, Math.Abs)),

            new GraphNodeType(MapRange, GraphNodeRole.Process, value, new float[] { 0 }, a => {
                float inMin = a.Node.GetFloat("in_min", 0);
                float inMax = a.Node.GetFloat("in_max", 1);
                float outMin = a.Node.GetFloat("out_min", 0);
                float outMax = a.Node.GetFloat("out_max", 1);
                bool clamp = a.Node.GetBool("clamp");
                float lo = Math.Min(outMin, outMax);
                float hi = Math.Max(outMin, outMax);
                return Unary(a, x => {
                    float y = inMax == inMin ? outMin : outMin + (x - inMin) * (outMax - outMin) / (inMax - inMin);
                    return clamp ? Math.Clamp(y, lo, hi) : y;
                });
            }),
            new GraphNodeType(Clamp, GraphNodeRole.Process, value, new float[] { 0 }, a => {
                float min = a.Node.GetFloat("min", float.NegativeInfinity);
                float max = a.Node.GetFloat("max", float.PositiveInfinity);
                return Unary(a, x => Math.Max(min, Math.Min(max, x)));
            }),

            new GraphNodeType(Lfo, GraphNodeRole.Input, none, new float[0], a => {
                float rate = a.Node.GetFloat("rate", 5);
                bool perBeat = a.Node.GetString("unit") == "beat";
                double phase = a.Node.GetFloat("phase", 0);
                float amplitude = a.Node.GetFloat("amplitude", 1);
                float offset = a.Node.GetFloat("offset", 0);
                var context = a.Context;
                // Phase counts from the start of the project, so it doesn't depend on how notes group into phrases.
                return Map(a, tick => {
                    int absTick = context.PartPosition + tick;
                    double cycles = perBeat
                        ? absTick / (double)context.Resolution * rate
                        : context.Axis.TickPosToMsPos(absTick) / 1000.0 * rate;
                    return offset + amplitude * (float)Math.Sin(2 * Math.PI * (cycles + phase));
                });
            }),
            new GraphNodeType(NotePosition, GraphNodeRole.Input, none, new float[0], a => {
                var context = a.Context;
                return Map(a, tick => {
                    var note = context.NoteAt(tick);
                    return note == null ? 0 : (float)(tick - note.Position) / note.Duration;
                });
            }),
            new GraphNodeType(NoteEnvelope, GraphNodeRole.Input, none, new float[0], a => {
                double attack = a.Node.GetFloat("attack_ms", 0);
                double release = a.Node.GetFloat("release_ms", 0);
                var context = a.Context;
                return Map(a, tick => {
                    var note = context.NoteAt(tick);
                    if (note == null) {
                        return 0;
                    }
                    double ms = context.Axis.TickPosToMsPos(context.PartPosition + tick);
                    double start = context.Axis.TickPosToMsPos(context.PartPosition + note.Position);
                    double end = context.Axis.TickPosToMsPos(context.PartPosition + note.End);
                    double y = 1;
                    if (attack > 0) {
                        y = Math.Min(y, (ms - start) / attack);
                    }
                    if (release > 0) {
                        y = Math.Min(y, (end - ms) / release);
                    }
                    return (float)Math.Clamp(y, 0, 1);
                });
            }),
        }.ToDictionary(t => t.Name);

        public static IReadOnlyDictionary<string, GraphNodeType> All => types;

        public static bool TryGet(string? name, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out GraphNodeType? type) {
            type = null;
            return name != null && types.TryGetValue(name, out type);
        }

        static GraphNodeType Binary(string name, float aDefault, float bDefault, Func<float, float, float> op) =>
            new GraphNodeType(name, GraphNodeRole.Process, ab, new[] { aDefault, bDefault }, a => {
                var x = a.Inputs[0];
                var y = a.Inputs[1];
                var result = new float[a.Ticks.Length];
                for (int i = 0; i < result.Length; ++i) {
                    result[i] = op(x[i], y[i]);
                }
                return result;
            });

        static float[] Unary(NodeArgs a, Func<float, float> op) {
            var x = a.Inputs[0];
            var result = new float[a.Ticks.Length];
            for (int i = 0; i < result.Length; ++i) {
                result[i] = op(x[i]);
            }
            return result;
        }

        static float[] Map(NodeArgs a, Func<int, float> f) {
            var result = new float[a.Ticks.Length];
            for (int i = 0; i < result.Length; ++i) {
                result[i] = f(a.Ticks[i]);
            }
            return result;
        }

        internal static float[] Fill(NodeArgs a, float value) {
            var result = new float[a.Ticks.Length];
            Array.Fill(result, value);
            return result;
        }
    }
}
