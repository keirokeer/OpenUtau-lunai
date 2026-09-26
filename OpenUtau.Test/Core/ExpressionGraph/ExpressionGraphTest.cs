using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenUtau.Core.Pipeline;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using Xunit;

namespace OpenUtau.Core.ExpressionGraph {
    public class ExpressionGraphTest {
        const string Renderer = "TEST";

        // Reads every expression of the phrase fixture.
        class CurveRenderer : IRenderer {
            public USingerType SingerType => USingerType.Classic;
            public bool SupportsRenderPitch => false;
            public bool SupportsExpression(UExpressionDescriptor descriptor) => true;
            public RenderResult Layout(RenderPhrase phrase) => new RenderResult() {
                leadingMs = phrase.leadingMs,
                positionMs = phrase.positionMs,
                estimatedLengthMs = phrase.durationMs + phrase.leadingMs,
            };
            public Task<RenderResult> Render(RenderPhrase phrase, Progress progress, int trackNo,
                    System.Threading.CancellationTokenSource cancellation, bool isPreRender = false,
                    RenderPhraseEvents? renderEvents = null) => throw new NotImplementedException();
            public RenderPitchResult LoadRenderedPitch(RenderPhrase phrase) => null;
            public UExpressionDescriptor[] GetSuggestedExpressions(USinger singer, URenderSettings renderSettings) =>
                Array.Empty<UExpressionDescriptor>();
        }

        static UGraphNode Node(int id, string type, params (string key, string value)[] parameters) {
            var node = new UGraphNode { id = id, type = type };
            foreach (var (key, value) in parameters) {
                node.Set(key, value);
            }
            return node;
        }

        static UGraphLink Link(int from, int to, string port = GraphNodeTypes.Value) =>
            new UGraphLink { from = from, to = to, toPort = port };

        static UExpressionGraph Graph(IEnumerable<UGraphNode> nodes, params UGraphLink[] links) =>
            new UExpressionGraph { id = "g", name = "G", renderer = Renderer, nodes = nodes.ToList(), links = links.ToList() };

        /// <summary>
        /// The phrase fixture, plus a numeric flag and an options flag, and per-phoneme values:
        /// volume 80 on the first phoneme, gender 20 on the second and the "Y" option on the third and the fourth,
        /// which extends the third's note.
        /// </summary>
        static (UProject project, UTrack track, UVoicePart part) Fixture(UExpressionGraph graph) {
            var (project, track, part) = PhraseSourceHashTest.BuildFixture(new CurveRenderer());
            track.RendererSettings.renderer = Renderer;
            project.RegisterExpression(new UExpressionDescriptor("gender", "gen", -100, 100, 0, "g"));
            project.RegisterExpression(new UExpressionDescriptor("growl", "grw", true, new[] { "", "Y" }));
            var notes = part.notes.ToArray();
            notes[0].phonemeExpressions.Add(new UExpression(project.expressions["vol"]) { index = 0, value = 80 });
            notes[1].phonemeExpressions.Add(new UExpression(project.expressions["gen"]) { index = 0, value = 20 });
            notes[2].phonemeExpressions.Add(new UExpression(project.expressions["grw"]) { index = 0, value = 1 });
            foreach (var phoneme in part.phonemes) {
                phoneme.Validate(new ValidateOptions(), project, track, part, phoneme.Parent);
            }
            if (graph != null) {
                project.expressionGraphs = new List<UExpressionGraph> { graph };
                project.defaultExpressionGraphs = new Dictionary<string, string> { [Renderer] = graph.id };
            }
            return (project, track, part);
        }

        static Dictionary<string, float[]> Evaluate(UExpressionGraph graph, UProject project, UTrack track, UVoicePart part, int[] ticks) {
            var program = ExpressionGraphProgram.Compile(graph, out var error);
            Assert.True(program != null, error);
            var source = PhraseSource.FromPart(project, track, part, 0);
            return program!.Evaluate(new GraphContext(source), ticks);
        }

        [Fact]
        public void NodesAreStoredAsFlatMappings() {
            var graph = Graph(new[] {
                Node(1, GraphNodeTypes.CurveInput, ("abbr", "pwr")),
                Node(2, GraphNodeTypes.MapRange, ("in_min", "0"), ("in_max", "100"), ("out_min", "-20"), ("out_max", "20"), ("clamp", "true")),
                Node(3, GraphNodeTypes.CurveOutput, ("abbr", "tenc")),
                // A node from a newer version, with a parameter that needs quoting.
                Node(4, "future_node", ("label", "a: b"), ("empty", "")),
            }, Link(1, 2), Link(2, 3));
            graph.nodes[0].x = 40;
            graph.nodes[0].y = 80.5f;

            var yaml = Yaml.DefaultSerializer.Serialize(graph);
            Assert.Contains("{id: 1, type: curve_input, abbr: pwr, x: 40, y: 80.5}", yaml);
            Assert.Contains("{id: 2, type: map_range, in_min: 0, in_max: 100, out_min: -20, out_max: 20, clamp: true, x: 0, y: 0}", yaml);
            Assert.Contains("{from: 1, to: 2, to_port: value}", yaml);

            var read = Yaml.DefaultDeserializer.Deserialize<UExpressionGraph>(yaml);
            Assert.Equal(yaml, Yaml.DefaultSerializer.Serialize(read));
            Assert.Equal("a: b", read.nodes[3].GetString("label"));
            Assert.Equal("", read.nodes[3].GetString("empty"));
            Assert.Equal(80.5f, read.nodes[0].y);
        }

        [Fact]
        public void ProjectsKeepTheirGraphs() {
            var (project, track, _) = Fixture(Graph(new[] { Node(1, GraphNodeTypes.Constant, ("value", "1")) }));
            track.ExpressionGraph = "g";
            var yaml = Yaml.DefaultSerializer.Serialize(project);
            var read = Yaml.DefaultDeserializer.Deserialize<UProject>(yaml);
            Assert.Equal("g", Assert.Single(read.expressionGraphs).id);
            Assert.Equal("g", read.defaultExpressionGraphs[Renderer]);
            Assert.Equal("g", read.tracks[0].ExpressionGraph);

            // A project without graphs doesn't mention them.
            yaml = Yaml.DefaultSerializer.Serialize(new UProject());
            Assert.DoesNotContain("expression_graph", yaml);
        }

        [Fact]
        public void BrokenGraphsDoNotCompile() {
            string Error(UExpressionGraph graph) {
                Assert.Null(ExpressionGraphProgram.Compile(graph, out var error));
                return error;
            }
            var input = Node(1, GraphNodeTypes.CurveInput, ("abbr", "dyn"));
            Assert.Contains("unknown type", Error(Graph(new[] { input, Node(2, "future_node") })));
            Assert.Contains("no input", Error(Graph(new[] { input, Node(2, GraphNodeTypes.Abs) }, Link(1, 2, "x"))));
            Assert.Contains("missing node", Error(Graph(new[] { input }, Link(1, 9))));
            Assert.Contains("two links", Error(Graph(
                new[] { input, Node(2, GraphNodeTypes.Constant), Node(3, GraphNodeTypes.Abs) },
                Link(1, 3), Link(2, 3))));
            Assert.Contains("Two outputs", Error(Graph(new[] {
                input,
                Node(2, GraphNodeTypes.CurveOutput, ("abbr", "tenc")),
                Node(3, GraphNodeTypes.CurveOutput, ("abbr", "tenc")),
            }, Link(1, 2), Link(1, 3))));
            Assert.Contains("cycle", Error(Graph(
                new[] { Node(1, GraphNodeTypes.Add), Node(2, GraphNodeTypes.Abs) },
                Link(1, 2), Link(2, 1, "a"))));
        }

        [Fact]
        public void RefusesLinksThatCloseACycle() {
            var graph = Graph(
                new[] { Node(1, GraphNodeTypes.Abs), Node(2, GraphNodeTypes.Abs), Node(3, GraphNodeTypes.Abs) },
                Link(1, 2), Link(2, 3));
            Assert.True(ExpressionGraphProgram.WouldCreateCycle(graph, 3, 1));
            Assert.True(ExpressionGraphProgram.WouldCreateCycle(graph, 2, 2));
            Assert.False(ExpressionGraphProgram.WouldCreateCycle(graph, 1, 3));
        }

        [Fact]
        public void MapsACurveToAnother() {
            // tenc = dyn mapped from [-12, 0] dB to [0, 100], clamped.
            var graph = Graph(new[] {
                Node(1, GraphNodeTypes.CurveInput, ("abbr", "dyn")),
                Node(2, GraphNodeTypes.MapRange, ("in_min", "-12"), ("in_max", "0"), ("out_min", "0"), ("out_max", "100"), ("clamp", "true")),
                Node(3, GraphNodeTypes.CurveOutput, ("abbr", "tenc")),
            }, Link(1, 2), Link(2, 3));
            var (project, track, part) = Fixture(graph);
            // The fixture's dyn: -6 at 0, 0 at 960, -12 at 1920, and its default of 0 after the last point.
            var outputs = Evaluate(graph, project, track, part, new[] { 0, 480, 960, 1920, 3000 });
            Assert.Equal(new[] { "tenc" }, outputs.Keys);
            Assert.Equal(new[] { 50f, 75f, 100f, 0f, 100f }, outputs["tenc"]);
        }

        [Fact]
        public void MixBlendsByTheFactor() {
            // 10 to 20 by the dyn curve mapped to a factor: -12 dB gives 0, -6 dB gives 1, and 0 dB gives 2, held at 1.
            var graph = Graph(new[] {
                Node(1, GraphNodeTypes.CurveInput, ("abbr", "dyn")),
                Node(2, GraphNodeTypes.MapRange, ("in_min", "-12"), ("in_max", "-6")),
                Node(3, GraphNodeTypes.Mix, ("a", "10"), ("b", "20")),
                Node(4, GraphNodeTypes.CurveOutput, ("abbr", "tenc")),
                Node(5, GraphNodeTypes.Mix, ("a", "10"), ("b", "20")),
                Node(6, GraphNodeTypes.CurveOutput, ("abbr", "genc")),
            }, Link(1, 2), Link(2, 3, "factor"), Link(3, 4), Link(5, 6));
            var (project, track, part) = Fixture(graph);
            // The fixture's dyn: -6 at 0, 0 at 960, -12 at 1920; and a map range without a clamp.
            var outputs = Evaluate(graph, project, track, part, new[] { 0, 960, 1920 });
            Assert.Equal(new[] { 20f, 20f, 10f }, outputs["tenc"]);
            // Unlinked, the factor is halfway.
            Assert.Equal(new[] { 15f, 15f, 15f }, outputs["genc"]);
        }

        [Fact]
        public void UnconnectedPortsTakeTheirParameter() {
            var graph = Graph(new[] {
                Node(1, GraphNodeTypes.Constant, ("value", "10")),
                Node(2, GraphNodeTypes.Multiply, ("b", "0.5")),
                Node(3, GraphNodeTypes.CurveOutput, ("abbr", "tenc")),
                // Not connected to anything: not evaluated, doesn't drive gender.
                Node(4, GraphNodeTypes.CurveOutput, ("abbr", "genc")),
            }, Link(1, 2, "a"), Link(2, 3));
            var (project, track, part) = Fixture(graph);
            var outputs = Evaluate(graph, project, track, part, new[] { 0, 5 });
            Assert.Equal(new[] { 5f, 5f }, outputs["tenc"]);
            Assert.False(outputs.ContainsKey("genc"));
        }

        [Fact]
        public void TimeNodes() {
            var (project, track, part) = Fixture(null);
            // Part at 960, 120 BPM. A 1 Hz sine is at its peak a quarter second (240 ticks) after a whole second.
            var graph = Graph(new[] {
                Node(1, GraphNodeTypes.Lfo, ("rate", "1")),
                Node(2, GraphNodeTypes.CurveOutput, ("abbr", "a")),
                Node(3, GraphNodeTypes.Lfo, ("rate", "0.25"), ("unit", "beat"), ("amplitude", "2")),
                Node(4, GraphNodeTypes.CurveOutput, ("abbr", "b")),
                Node(5, GraphNodeTypes.NotePosition),
                Node(6, GraphNodeTypes.CurveOutput, ("abbr", "c")),
                Node(7, GraphNodeTypes.NoteEnvelope, ("attack_ms", "100"), ("release_ms", "50")),
                Node(8, GraphNodeTypes.CurveOutput, ("abbr", "d")),
            }, Link(1, 2), Link(3, 4), Link(5, 6), Link(7, 8));
            var outputs = Evaluate(graph, project, track, part, new[] { 0, 240, 360, 1100 });
            // Absolute ticks 960, 1200, 1320, 2060: 1, 1.25, 1.375 and 2.1458 s.
            Assert.Equal(new[] { 0f, 1f, (float)Math.Sin(2 * Math.PI * 1.375), (float)Math.Sin(2 * Math.PI * 2.14583333) },
                outputs["a"], new ToleranceComparer(1e-4f));
            // A quarter cycle per beat: beats 2, 2.5, 2.75 at the first three ticks.
            Assert.Equal(new[] { 0f, 2 * (float)Math.Sin(2 * Math.PI * 0.625), 2 * (float)Math.Sin(2 * Math.PI * 0.6875) },
                outputs["b"].Take(3), new ToleranceComparer(1e-4f));
            // Notes at 0-480, 480-960 and, after a gap, 1200-1680.
            Assert.Equal(new[] { 0f, 0.5f, 0.75f, 0f }, outputs["c"]);
            // 240 ticks is 250 ms into a 500 ms note, past the attack and before the release.
            Assert.Equal(new[] { 0f, 1f, 1f, 0f }, outputs["d"]);
        }

        class ToleranceComparer : IEqualityComparer<float> {
            readonly float tolerance;
            public ToleranceComparer(float tolerance) => this.tolerance = tolerance;
            public bool Equals(float x, float y) => Math.Abs(x - y) <= tolerance;
            public int GetHashCode(float obj) => 0;
        }

        [Fact]
        public void TracksUseTheirRenderersDefaultOrTheirOverride() {
            var (project, track, _) = Fixture(Graph(new[] { Node(1, GraphNodeTypes.Constant) }));
            var other = Graph(new[] { Node(1, GraphNodeTypes.Constant) });
            other.id = "other";
            project.expressionGraphs!.Add(other);
            Assert.Equal("g", ExpressionGraphProgram.GetEffectiveGraph(project, track)!.id);
            track.ExpressionGraph = "other";
            Assert.Equal("other", ExpressionGraphProgram.GetEffectiveGraph(project, track)!.id);
            // Made for another renderer: not used.
            other.renderer = "OTHER";
            Assert.Null(ExpressionGraphProgram.GetEffectiveGraph(project, track));
            track.ExpressionGraph = null;
            track.RendererSettings.renderer = "OTHER";
            Assert.Null(ExpressionGraphProgram.GetEffectiveGraph(project, track));
        }

        static string Snapshot(IEnumerable<RenderPhrase> phrases) {
            var lines = new List<string>();
            foreach (var phrase in phrases) {
                lines.Add($"p {phrase.hash:x16} {phrase.preEffectHash:x16}");
                foreach (var array in new[] { phrase.pitches, phrase.dynamics, phrase.tension, phrase.xsy }) {
                    lines.Add(string.Join(",", array.Select(v => BitConverter.SingleToInt32Bits(v))));
                }
                foreach (var curve in phrase.curves) {
                    lines.Add(curve.Item1 + " " + string.Join(",", curve.Item2.Select(v => BitConverter.SingleToInt32Bits(v))));
                }
                foreach (var phone in phrase.phones) {
                    lines.Add($"h {phone.hash:x16} {Flags(phone)} {phone.volume} {phone.velocity} {phone.modulation} "
                        + string.Join(" ", phone.envelope.Select(p => $"{p.X},{p.Y}")));
                }
            }
            return string.Join("\n", lines);
        }

        static string Flags(RenderPhone phone) => string.Join(" ", phone.flags.Select(f => f.Item1 + f.Item2));

        /// <summary>Each expression wired straight to itself.</summary>
        static void AddIdentity(List<UGraphNode> nodes, List<UGraphLink> links, IEnumerable<string> abbrs, string input, string output) {
            foreach (var abbr in abbrs) {
                int id = nodes.Count + 1;
                nodes.Add(Node(id, input, ("abbr", abbr)));
                nodes.Add(Node(id + 1, output, ("abbr", abbr)));
                links.Add(Link(id, id + 1));
            }
        }

        [Fact]
        public void IdentityGraphChangesNothing() {
            var (project, track, part) = Fixture(null);
            var withoutGraph = Snapshot(RenderPhrase.FromPart(project, track, part));

            // Every curve and every numerical per-phoneme expression, wired straight to itself.
            var nodes = new List<UGraphNode>();
            var links = new List<UGraphLink>();
            AddIdentity(nodes, links,
                project.expressions.Values.Where(d => d.type == UExpressionType.Curve).Select(d => d.abbr),
                GraphNodeTypes.CurveInput, GraphNodeTypes.CurveOutput);
            AddIdentity(nodes, links,
                project.expressions.Values.Where(d => d.type == UExpressionType.Numerical).Select(d => d.abbr),
                GraphNodeTypes.PhonemeInput, GraphNodeTypes.PhonemeOutput);
            var graph = Graph(nodes, links.ToArray());
            (project, track, part) = Fixture(graph);
            Assert.NotNull(ExpressionGraphProgram.ForTrack(project, track));
            Assert.Equal(withoutGraph, Snapshot(RenderPhrase.FromPart(project, track, part)));
        }

        [Fact]
        public void DrivenCurvesReachThePhrase() {
            var (project, track, part) = Fixture(null);
            var before = RenderPhrase.FromPart(project, track, part);

            // tenc = custom curve * 3 + 10. The custom curve has a single point, 7 at tick 720, and is 0 elsewhere.
            var graph = Graph(new[] {
                Node(1, GraphNodeTypes.CurveInput, ("abbr", "cstm")),
                Node(2, GraphNodeTypes.Multiply, ("b", "3")),
                Node(3, GraphNodeTypes.Add, ("b", "10")),
                Node(4, GraphNodeTypes.CurveOutput, ("abbr", "tenc")),
            }, Link(1, 2, "a"), Link(2, 3, "a"), Link(3, 4));
            (project, track, part) = Fixture(graph);
            var after = RenderPhrase.FromPart(project, track, part);

            Assert.Contains(31f, after[0].tension);
            Assert.All(after[1].tension, v => Assert.Equal(10f, v));
            Assert.NotEqual(before[0].hash, after[0].hash);
            // Curves the graph doesn't drive are untouched.
            Assert.Equal(before[0].dynamics, after[0].dynamics);
            Assert.Equal(before[0].curves.Single(c => c.Item1 == "cstm").Item2, after[0].curves.Single(c => c.Item1 == "cstm").Item2);
        }

        [Fact]
        public void DrivenCurvesStayInTheirRange() {
            var graph = Graph(new[] {
                Node(1, GraphNodeTypes.Constant, ("value", "500")),
                Node(2, GraphNodeTypes.CurveOutput, ("abbr", "tenc")),
            }, Link(1, 2));
            var (project, track, part) = Fixture(graph);
            Assert.All(RenderPhrase.FromPart(project, track, part)[0].tension, v => Assert.Equal(100f, v));
        }
        [Fact]
        public void DrivenPhonemeValuesReachThePhones() {
            // Volume = dyn + 100 at each phoneme's position; gender = 30 everywhere.
            var graph = Graph(new[] {
                Node(1, GraphNodeTypes.CurveInput, ("abbr", "dyn")),
                Node(2, GraphNodeTypes.Add, ("b", "100")),
                Node(3, GraphNodeTypes.PhonemeOutput, ("abbr", "vol")),
                Node(4, GraphNodeTypes.Constant, ("value", "30.7")),
                Node(5, GraphNodeTypes.PhonemeOutput, ("abbr", "gen")),
            }, Link(1, 2, "a"), Link(2, 3), Link(4, 5));
            var (project, track, part) = Fixture(graph);
            var phones = RenderPhrase.FromPart(project, track, part).SelectMany(p => p.phones).ToArray();

            // The fixture's dyn at the phonemes (0, 480, 1200, 1680): -6, -3, -3 and -9.
            Assert.Equal(new[] { 94f, 97f, 97f, 91f }.Select(v => v * 0.01f), phones.Select(p => p.volume));
            // The envelope's levels follow: the fixture's attack and decay are both 100.
            Assert.Equal(new[] { 94f, 94f, 0f }, phones[0].envelope.Skip(1).Take(3).Select(p => p.Y));
            // Flags keep their order and the (int) conversion; the options flag is untouched (its first option is empty).
            Assert.Equal(new[] { "g30 ", "g30 ", "g30 Y", "g30 Y" }, phones.Select(Flags));
        }

        [Fact]
        public void TimingExpressionsCannotBeDriven() {
            var (project, track, part) = Fixture(null);
            var before = Snapshot(RenderPhrase.FromPart(project, track, part));
            // Velocity, alternate and tone shift change phonemizing and timing, before the graph runs.
            var graph = Graph(new[] {
                Node(1, GraphNodeTypes.Constant, ("value", "50")),
                Node(2, GraphNodeTypes.PhonemeOutput, ("abbr", "vel")),
                Node(3, GraphNodeTypes.PhonemeOutput, ("abbr", "shft")),
                // Options expressions aren't values either.
                Node(4, GraphNodeTypes.PhonemeOutput, ("abbr", "grw")),
            }, Link(1, 2), Link(1, 3), Link(1, 4));
            (project, track, part) = Fixture(graph);
            Assert.Equal(before, Snapshot(RenderPhrase.FromPart(project, track, part)));
        }

        [Fact]
        public void PhonemeValuesDriveCurves() {
            var graph = Graph(new[] {
                Node(1, GraphNodeTypes.PhonemeInput, ("abbr", "vol")),
                Node(2, GraphNodeTypes.CurveOutput, ("abbr", "a")),
                Node(3, GraphNodeTypes.PhonemeInput, ("abbr", "vol"), ("interpolation", "linear")),
                Node(4, GraphNodeTypes.CurveOutput, ("abbr", "b")),
                // Options expressions read as nothing.
                Node(5, GraphNodeTypes.PhonemeInput, ("abbr", "grw")),
                Node(6, GraphNodeTypes.CurveOutput, ("abbr", "c")),
            }, Link(1, 2), Link(3, 4), Link(5, 6));
            var (project, track, part) = Fixture(graph);
            // Volume 80 on the phoneme at 0 and 100 on the rest, at 480, 1200 and 1680.
            var outputs = Evaluate(graph, project, track, part, new[] { -100, 0, 240, 480, 840, 2000 });
            Assert.Equal(new[] { 80f, 80f, 80f, 100f, 100f, 100f }, outputs["a"]);
            Assert.Equal(new[] { 80f, 80f, 90f, 100f, 100f, 100f }, outputs["b"]);
            Assert.All(outputs["c"], v => Assert.Equal(0f, v));
        }

        [Fact]
        public void InterpolatesBetweenAnchors() {
            var anchors = new PhonemeAnchors(new[] { 0, 100, 100, 300, 400 },
                new Dictionary<string, float[]> { ["x"] = new[] { 0f, 50f, 60f, 100f, 100f } });
            // Each phoneme's own value, even where two share a position.
            Assert.Equal(50f, anchors.At("x", 1));
            Assert.Equal(60f, anchors.At("x", 2));
            foreach (var mode in new[] { AnchorInterpolation.Step, AnchorInterpolation.Linear, AnchorInterpolation.Cubic }) {
                // Through the anchors (the last of a shared position), flat beyond the ends.
                Assert.Equal(new[] { 0f, 0f, 60f, 100f, 100f, 100f },
                    new[] { -50, 0, 100, 300, 400, 500 }.Select(t => anchors.Sample("x", t, mode)));
            }
            Assert.Equal(0f, anchors.Sample("x", 50, AnchorInterpolation.Step));
            Assert.Equal(30f, anchors.Sample("x", 50, AnchorInterpolation.Linear));
            Assert.Equal(80f, anchors.Sample("x", 200, AnchorInterpolation.Linear));
            // Cubic stays between its neighbours and never goes back down.
            var cubic = Enumerable.Range(0, 401).Select(t => anchors.Sample("x", t, AnchorInterpolation.Cubic)).ToArray();
            Assert.All(cubic, v => Assert.InRange(v, 0f, 100f));
            Assert.All(cubic.Zip(cubic.Skip(1)), p => Assert.True(p.Second >= p.First - 1e-4f, $"{p.First} > {p.Second}"));
            Assert.Equal(0f, anchors.Sample("y", 50, AnchorInterpolation.Linear));
        }
    }
}
