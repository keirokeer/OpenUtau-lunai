using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenUtau.Core.Pipeline;
using OpenUtau.Core.Ustx;
using Serilog;

namespace OpenUtau.Core.ExpressionGraph {
    /// <summary>A node with its parameters copied out of the document.</summary>
    public sealed class CompiledNode {
        public readonly int Id;
        public readonly GraphNodeType Type;
        readonly Dictionary<string, string> parameters;
        // For each port: the index of the node feeding it in evaluation order, or -1 for a constant.
        internal readonly int[] sources;
        internal readonly float[] constants;

        internal CompiledNode(UGraphNode node, GraphNodeType type) {
            Id = node.id;
            Type = type;
            parameters = new Dictionary<string, string>(node.parameters);
            sources = Enumerable.Repeat(-1, type.Ports.Length).ToArray();
            constants = type.Ports.Select((port, i) => GetFloat(port, type.PortDefaults[i])).ToArray();
        }

        public string? GetString(string key) => parameters.TryGetValue(key, out var value) ? value : null;

        public float GetFloat(string key, float fallback) =>
            parameters.TryGetValue(key, out var text)
                && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
                ? value : fallback;

        public bool GetBool(string key) => GetString(key) is "true" or "True" or "1";
    }

    /// <summary>What nodes read from the part being rendered.</summary>
    public sealed class GraphContext {
        public readonly TimeAxis Axis;
        public readonly int PartPosition;
        public readonly int Resolution;
        readonly Dictionary<string, CurveSource> curves;
        readonly Dictionary<string, int> defaults;
        readonly NoteSource[] notes;

        public GraphContext(TimeAxis axis, int partPosition, int resolution,
                IEnumerable<CurveSource> curves, IReadOnlyDictionary<string, int> defaults, IEnumerable<NoteSource> notes) {
            Axis = axis;
            PartPosition = partPosition;
            Resolution = resolution;
            this.curves = new Dictionary<string, CurveSource>();
            foreach (var curve in curves) {
                this.curves.TryAdd(curve.Abbr, curve);
            }
            this.defaults = new Dictionary<string, int>(defaults);
            this.notes = notes.OrderBy(n => n.Position).ToArray();
        }

        internal GraphContext(PhraseSource source)
            : this(source.Axis, source.PartPosition, source.Resolution, source.Curves, source.CurveDefaults, source.Notes) { }

        /// <summary>A curve's value at a part-relative tick, exactly as the renderer reads it without a graph.</summary>
        public float SampleCurve(string? abbr, int tick) {
            if (abbr != null && curves.TryGetValue(abbr, out var curve)) {
                return curve.Sample(tick);
            }
            return abbr != null && defaults.TryGetValue(abbr, out int y) ? y : 0;
        }

        /// <summary>The note covering a part-relative tick, if any.</summary>
        public NoteSource? NoteAt(int tick) {
            int lo = 0, hi = notes.Length - 1, found = -1;
            while (lo <= hi) {
                int mid = (lo + hi) / 2;
                if (notes[mid].Position <= tick) {
                    found = mid;
                    lo = mid + 1;
                } else {
                    hi = mid - 1;
                }
            }
            return found >= 0 && tick < notes[found].End ? notes[found] : null;
        }
    }

    /// <summary>
    /// A validated graph, ready to evaluate off the UI thread. Immutable once compiled.
    /// </summary>
    public sealed class ExpressionGraphProgram {
        // Only the nodes that feed an output, in evaluation order.
        readonly CompiledNode[] order;
        readonly Dictionary<string, int> curveOutputs;

        ExpressionGraphProgram(CompiledNode[] order, Dictionary<string, int> curveOutputs) {
            this.order = order;
            this.curveOutputs = curveOutputs;
        }

        /// <summary>The curves this graph drives. Every other curve reaches the renderer as drawn.</summary>
        public IReadOnlyCollection<string> CurveOutputs => curveOutputs.Keys;

        public bool DrivesCurve(string abbr) => curveOutputs.ContainsKey(abbr);

        /// <summary>Evaluates every driven curve at the given part-relative ticks.</summary>
        public Dictionary<string, float[]> Evaluate(GraphContext context, int[] ticks) {
            var values = new float[order.Length][];
            for (int i = 0; i < order.Length; ++i) {
                var node = order[i];
                var inputs = new float[node.sources.Length][];
                for (int p = 0; p < inputs.Length; ++p) {
                    if (node.sources[p] >= 0) {
                        inputs[p] = values[node.sources[p]];
                    } else {
                        inputs[p] = new float[ticks.Length];
                        Array.Fill(inputs[p], node.constants[p]);
                    }
                }
                values[i] = node.Type.Evaluate(new NodeArgs(inputs, node, context, ticks));
            }
            return curveOutputs.ToDictionary(kv => kv.Key, kv => values[kv.Value]);
        }

        /// <summary>
        /// Checks a graph and prepares it for evaluation. Returns null with the reason when the graph can't run:
        /// an unknown node type, a broken link, two outputs for the same curve, or a cycle.
        /// </summary>
        public static ExpressionGraphProgram? Compile(UExpressionGraph graph, out string? error) {
            error = null;
            var nodes = new Dictionary<int, (UGraphNode node, GraphNodeType type)>();
            foreach (var node in graph.nodes) {
                if (!GraphNodeTypes.TryGet(node.type, out var type)) {
                    error = $"Node {node.id} has unknown type \"{node.type}\".";
                    return null;
                }
                if (!nodes.TryAdd(node.id, (node, type))) {
                    error = $"Two nodes have id {node.id}.";
                    return null;
                }
                if ((type.Name == GraphNodeTypes.CurveInput || type.Name == GraphNodeTypes.CurveOutput)
                        && string.IsNullOrEmpty(node.GetString("abbr"))) {
                    error = $"Node {node.id} has no curve.";
                    return null;
                }
            }
            // Port links, by target node.
            var incoming = nodes.Keys.ToDictionary(id => id, _ => new Dictionary<int, int>());
            foreach (var link in graph.links) {
                if (!nodes.ContainsKey(link.from) || !nodes.TryGetValue(link.to, out var target)) {
                    error = $"A link connects missing node {(nodes.ContainsKey(link.from) ? link.to : link.from)}.";
                    return null;
                }
                if (nodes[link.from].type.Role == GraphNodeRole.CurveOutput || link.fromPort != null) {
                    error = $"Node {link.from} has no such output.";
                    return null;
                }
                int port = Array.IndexOf(target.type.Ports, link.toPort);
                if (port < 0) {
                    error = $"Node {link.to} has no input \"{link.toPort}\".";
                    return null;
                }
                if (!incoming[link.to].TryAdd(port, link.from)) {
                    error = $"Input \"{link.toPort}\" of node {link.to} has two links.";
                    return null;
                }
            }
            if (HasCycle(nodes.Keys, incoming)) {
                error = "The graph has a cycle.";
                return null;
            }

            // Outputs with a connected value, then everything they depend on.
            var outputs = new Dictionary<string, int>();
            foreach (var (node, type) in nodes.Values.Where(n => n.type.Role == GraphNodeRole.CurveOutput)) {
                string abbr = node.GetString("abbr")!;
                if (outputs.ContainsKey(abbr)) {
                    error = $"Two outputs drive \"{abbr}\".";
                    return null;
                }
                outputs[abbr] = node.id;
            }
            var connectedOutputs = outputs.Where(kv => incoming[kv.Value].Count > 0).ToDictionary(kv => kv.Key, kv => kv.Value);

            var ordered = new List<int>();
            var visited = new HashSet<int>();
            void Visit(int id) {
                if (!visited.Add(id)) {
                    return;
                }
                foreach (var from in incoming[id].OrderBy(kv => kv.Key).Select(kv => kv.Value)) {
                    Visit(from);
                }
                ordered.Add(id);
            }
            foreach (var id in connectedOutputs.Values.OrderBy(id => id)) {
                Visit(id);
            }
            var indexOf = ordered.Select((id, i) => (id, i)).ToDictionary(t => t.id, t => t.i);
            var compiled = ordered.Select(id => new CompiledNode(nodes[id].node, nodes[id].type)).ToArray();
            for (int i = 0; i < compiled.Length; ++i) {
                foreach (var (port, from) in incoming[ordered[i]]) {
                    compiled[i].sources[port] = indexOf[from];
                }
            }
            return new ExpressionGraphProgram(compiled,
                connectedOutputs.ToDictionary(kv => kv.Key, kv => indexOf[kv.Value]));
        }

        static bool HasCycle(IEnumerable<int> ids, Dictionary<int, Dictionary<int, int>> incoming) {
            var state = new Dictionary<int, int>(); // 1: visiting, 2: done
            bool Visit(int id) {
                state.TryGetValue(id, out int s);
                if (s == 1) {
                    return true;
                }
                if (s == 2) {
                    return false;
                }
                state[id] = 1;
                foreach (var from in incoming[id].Values) {
                    if (Visit(from)) {
                        return true;
                    }
                }
                state[id] = 2;
                return false;
            }
            return ids.Any(Visit);
        }

        /// <summary>Whether linking <paramref name="from"/> into <paramref name="to"/> would close a cycle.</summary>
        public static bool WouldCreateCycle(UExpressionGraph graph, int from, int to) {
            if (from == to) {
                return true;
            }
            // A cycle appears if "to" already feeds "from".
            var stack = new Stack<int>();
            var seen = new HashSet<int>();
            stack.Push(from);
            while (stack.Count > 0) {
                int id = stack.Pop();
                if (id == to) {
                    return true;
                }
                if (!seen.Add(id)) {
                    continue;
                }
                foreach (var link in graph.links.Where(l => l.to == id)) {
                    stack.Push(link.from);
                }
            }
            return false;
        }

        /// <summary>
        /// The graph a track renders with: its override, else the project's default for its renderer.
        /// A graph made for another renderer is not used.
        /// </summary>
        public static UExpressionGraph? GetEffectiveGraph(UProject project, UTrack track) {
            string? renderer = track.RendererSettings?.renderer;
            if (project.expressionGraphs == null || project.expressionGraphs.Count == 0 || string.IsNullOrEmpty(renderer)) {
                return null;
            }
            string? id = track.ExpressionGraph;
            if (id == null && project.defaultExpressionGraphs != null) {
                project.defaultExpressionGraphs.TryGetValue(renderer, out id);
            }
            var graph = id == null ? null : project.expressionGraphs.FirstOrDefault(g => g.id == id);
            return graph?.renderer == renderer ? graph : null;
        }

        /// <summary>Compiles the track's effective graph, or null when it has none or it can't run.</summary>
        public static ExpressionGraphProgram? ForTrack(UProject project, UTrack track) {
            var graph = GetEffectiveGraph(project, track);
            return graph == null ? null : Compile(graph, out _);
        }

        /// <summary>Logs every graph in the project that can't run.</summary>
        public static void LogProblems(UProject project) {
            foreach (var graph in project.expressionGraphs ?? Enumerable.Empty<UExpressionGraph>()) {
                if (Compile(graph, out var error) == null) {
                    Log.Warning($"Expression graph \"{graph.name ?? graph.id}\" is ignored: {error}");
                }
            }
        }
    }
}
