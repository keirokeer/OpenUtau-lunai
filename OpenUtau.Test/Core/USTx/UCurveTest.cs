using System;
using System.Linq;
using OpenUtau.Core.Ustx;
using Xunit;

namespace OpenUtau.Test.Core.USTx {
    public class UCurveTest {
        static readonly UExpressionDescriptor Descriptor = new UExpressionDescriptor("test", "tst", 0, 100, 0);

        static UCurve MakeCurve(int[] xs, int[] ys) {
            return new UCurve(Descriptor) { xs = xs.ToList(), ys = ys.ToList() };
        }

        static void AssertSameValues(UCurve expected, UCurve actual, int from, int to) {
            for (int tick = from; tick <= to; tick++) {
                Assert.True(expected.Sample(tick) == actual.Sample(tick),
                    $"tick {tick}: expected {expected.Sample(tick)}, actual {actual.Sample(tick)}");
            }
        }

        [Fact]
        public void ReplaceRangeRemovesPointsInsideRangeIncludingBoundaries() {
            // Neighbours are within UCurve.interval of the range, so no anchors are added.
            var (xs, ys) = UCurve.ReplaceRange(
                new[] { 0, 395, 400, 700, 1000, 1005, 1440 }, new[] { 10, 20, 30, 40, 50, 60, 70 },
                400, 1000,
                new[] { (700, 99) },
                Descriptor);

            Assert.Equal(new[] { 0, 395, 700, 1005, 1440 }, xs);
            Assert.Equal(new[] { 10, 20, 99, 60, 70 }, ys);
        }

        [Fact]
        public void ReplaceRangeInsertsPointsInTickOrder() {
            // Neighbours are within UCurve.interval of the range, so no anchors are added.
            var (xs, ys) = UCurve.ReplaceRange(
                new[] { 395, 1005 }, new[] { 10, 40 },
                400, 1000,
                new[] { (900, 60), (500, 50) },
                Descriptor);

            Assert.Equal(new[] { 395, 500, 900, 1005 }, xs);
            Assert.Equal(new[] { 10, 50, 60, 40 }, ys);
        }

        [Fact]
        public void ReplaceRangeLaterPointOverwritesSameTick() {
            var (xs, ys) = UCurve.ReplaceRange(
                new[] { 395, 1005 }, new[] { 10, 40 },
                400, 1000,
                new[] { (500, 50), (500, 60) },
                Descriptor);

            Assert.Equal(new[] { 395, 500, 1005 }, xs);
            Assert.Equal(new[] { 10, 60, 40 }, ys);
        }

        [Fact]
        public void ReplaceRangeClampsValues() {
            var (_, ys) = UCurve.ReplaceRange(
                new[] { 395, 1005 }, new[] { 10, 40 },
                400, 1000,
                new[] { (500, 150), (600, -10) },
                Descriptor);

            Assert.Equal(new[] { 10, 100, 0, 40 }, ys);
        }

        [Fact]
        public void ReplaceRangeAddsDefaultAnchorsToEmptyCurve() {
            // Without anchors the edited points would be the first and last points, and the
            // piano roll draws a slope from them towards the edges of the view.
            var descriptor = new UExpressionDescriptor("test", "tst", 0, 100, 20);

            var (xs, ys) = UCurve.ReplaceRange(
                new int[0], new int[0],
                800, 1200,
                new[] { (800, 50), (1200, 50) },
                descriptor);

            Assert.Equal(new[] { 795, 800, 1200, 1205 }, xs);
            Assert.Equal(new[] { 20, 50, 50, 20 }, ys);
        }

        [Fact]
        public void ReplaceRangeDoesNotModifyInput() {
            var baseXs = new[] { 0, 480, 960 };
            var baseYs = new[] { 10, 20, 30 };

            UCurve.ReplaceRange(baseXs, baseYs, 400, 1000, new[] { (500, 50) }, Descriptor);

            Assert.Equal(new[] { 0, 480, 960 }, baseXs);
            Assert.Equal(new[] { 10, 20, 30 }, baseYs);
        }

        [Fact]
        public void ReplaceRangeKeepsValuesOutsideRange() {
            // Vertical shift of [805, 1195].
            var before = MakeCurve(new[] { 0, 1000, 2000 }, new[] { 0, 100, 0 });

            var (xs, ys) = UCurve.ReplaceRange(
                before.xs, before.ys,
                805, 1195,
                new[] { (805, 100), (1000, 100), (1195, 100) },
                Descriptor);
            var after = MakeCurve(xs, ys);

            AssertSameValues(before, after, 0, 805 - UCurve.interval);
            AssertSameValues(before, after, 1195 + UCurve.interval, 2000);
        }

        [Fact]
        public void ReplaceRangeKeepsValuesOutsideMovedRange() {
            // Horizontal shift of [805, 1195] by +300: the original and the destination range are cleared.
            var before = MakeCurve(new[] { 0, 1000, 2000 }, new[] { 0, 100, 0 });

            var (xs, ys) = UCurve.ReplaceRange(
                before.xs, before.ys,
                805, 1495,
                new[] { (1105, 81), (1300, 100), (1495, 81) },
                Descriptor);
            var after = MakeCurve(xs, ys);

            AssertSameValues(before, after, 0, 805 - UCurve.interval);
            AssertSameValues(before, after, 1495 + UCurve.interval, 2000);
        }

        [Fact]
        public void ReplaceRangeKeepsDefaultBeforeFirstPoint() {
            var before = MakeCurve(new[] { 2000 }, new[] { 30 });

            var (xs, ys) = UCurve.ReplaceRange(
                before.xs, before.ys,
                800, 1200,
                new[] { (800, 50), (1200, 50) },
                Descriptor);
            var after = MakeCurve(xs, ys);

            AssertSameValues(before, after, 1200 + UCurve.interval, 2100);
        }

        [Fact]
        public void ReplaceRangeKeepsDefaultAfterLastPoint() {
            var before = MakeCurve(new[] { 0 }, new[] { 30 });

            var (xs, ys) = UCurve.ReplaceRange(
                before.xs, before.ys,
                800, 1200,
                new[] { (800, 50), (1200, 50) },
                Descriptor);
            var after = MakeCurve(xs, ys);

            AssertSameValues(before, after, 0, 800 - UCurve.interval);
        }

        [Fact]
        public void SetMatchesUpstreamContinuousStroke() {
            // Upstream OpenUtau Set: continuous x>lastX deletes between and anchors right.
            var curve = new UCurve(Descriptor);
            curve.Set(100, 10, 100, 10);
            curve.Set(200, 40, 100, 10);
            Assert.Equal(10, curve.Sample(100));
            Assert.Equal(40, curve.Sample(200));
            Assert.Equal(25, curve.Sample(150));
        }

        [Fact]
        public void SetStillConnectsAcrossAuthoredPlateau() {
            // Upstream Set does not re-write lastY at lastX; endpoints that already
            // exist keep their prior values, and the stroke only updates x.
            var curve = new UCurve(Descriptor);
            for (int t = 0; t <= 400; t += UCurve.interval) {
                curve.Set(t, 61, t, 61);
            }
            curve.Set(400, 90, 0, 90);
            Assert.Equal(61, curve.Sample(0));
            Assert.Equal(90, curve.Sample(400));
            Assert.True(curve.Sample(200) > 61 && curve.Sample(200) < 90);
        }

        [Fact]
        public void ContinuousStrokeDoesNotDeleteAheadOfBrush() {
            var curve = new UCurve(Descriptor);
            for (int t = 2000; t <= 4000; t += UCurve.interval) {
                curve.Set(t, 50, t, 50);
            }
            for (int t = 1000; t <= 1200; t += UCurve.interval) {
                int prev = Math.Max(1000, t - UCurve.interval);
                curve.Set(t, 80, prev, 80);
            }

            Assert.Equal(80, curve.Sample(1100));
            Assert.Equal(50, curve.Sample(2500));
            Assert.Equal(50, curve.Sample(3500));
        }
    }
}
