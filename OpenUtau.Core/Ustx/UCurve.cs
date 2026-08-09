using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using SharpCompress;
using YamlDotNet.Serialization;

namespace OpenUtau.Core.Ustx {
    public class UCurve {
        public const int interval = 5;

        [YamlIgnore] public UExpressionDescriptor descriptor;
        public List<int> xs = new List<int>();
        public List<int> ys = new List<int>();
        /// <summary>
        /// Tick positions that break linear interpolation. Sample returns CustomDefaultValue
        /// across a break (same as an unedited gap). Used when RMB-erasing curve regions.
        /// </summary>
        public List<int> breaks = new List<int>();
        [YamlIgnore] public List<int> realXs = new List<int>();
        [YamlIgnore] public List<int> realYs = new List<int>();
        public string abbr;

        [YamlIgnore] public bool IsEmpty => xs.Count == 0 || ys.All(y => y == 0);

        public UCurve(UExpressionDescriptor descriptor) {
            Trace.Assert(descriptor != null);
            this.descriptor = descriptor;
            abbr = descriptor.abbr;
        }

        public UCurve() { }

        public UCurve(string abbr) {
            this.abbr = abbr;
        }

        public UCurve Clone() {
            return new UCurve(descriptor) {
                xs = xs.ToList(),
                ys = ys.ToList(),
                breaks = breaks?.ToList() ?? new List<int>(),
            };
        }

        public bool IsEmptyBetween(int x0, int x1, int defaultValue) {
            if (Sample(x0) != defaultValue || Sample(x1) != defaultValue) {
                return false;
            }
            int idx = xs.BinarySearch(x0);
            if (idx < 0) {
                idx = ~idx;
            }
            while (idx < xs.Count && xs[idx] <= x1) {
                if (ys[idx] != defaultValue) {
                    return false;
                }
                idx++;
            }
            return true;
        }

        public int Sample(int x) {
            return Sample(x, descriptor != null ? (int)descriptor.CustomDefaultValue : 0);
        }

        public int Sample(int x, int emptyValue) {
            int idx = xs.BinarySearch(x);
            if (idx >= 0) {
                return ys[idx];
            }
            idx = ~idx;
            if (idx > 0 && idx < xs.Count) {
                if (HasBreakBetween(xs[idx - 1], xs[idx])) {
                    return emptyValue;
                }
                return (int)Math.Round(MusicMath.Linear(xs[idx - 1], xs[idx], ys[idx - 1], ys[idx], x));
            }
            return emptyValue;
        }

        public bool HasBreakBetween(int x0, int x1) {
            if (breaks == null || breaks.Count == 0 || x0 >= x1) {
                return false;
            }
            // A break at tick t severs interpolation for any open interval covering t.
            int lo = breaks.BinarySearch(x0 + 1);
            if (lo < 0) {
                lo = ~lo;
            }
            return lo < breaks.Count && breaks[lo] < x1;
        }

        private void Insert(int x, int y) {
            int idx = xs.BinarySearch(x);
            if (idx >= 0) {
                ys[idx] = y;
                return;
            }
            idx = ~idx;
            xs.Insert(idx, x);
            ys.Insert(idx, y);
        }

        public void Set(int x, int y, int lastX, int lastY) {
            int empty = descriptor != null ? (int)descriptor.CustomDefaultValue : 0;
            Set(x, y, lastX, lastY, empty);
        }

        public void Set(int x, int y, int lastX, int lastY, int emptyValue) {
            x = (int)Math.Round((float)x / interval) * interval;
            lastX = (int)Math.Round((float)lastX / interval) * interval;
            int minX = Math.Min(x, lastX);
            int maxX = Math.Max(x, lastX);
            RemoveBreaksBetween(minX, maxX);
            if (x == lastX) {
                int leftY = Sample(x - interval, emptyValue);
                int rightY = Sample(x + interval, emptyValue);
                Insert(x - interval, leftY);
                Insert(x, y);
                Insert(x + interval, rightY);
            } else if (x < lastX) {
                int leftY = Sample(x - interval, emptyValue);
                DeleteBetweenExclusive(x, lastX);
                Insert(x - interval, leftY);
                Insert(x, y);
            } else {
                int rightY = Sample(x + interval, emptyValue);
                DeleteBetweenExclusive(lastX, x);
                Insert(x, y);
                Insert(x + interval, rightY);
            }
        }

        /// <summary>
        /// Remove authored points in [x, lastX] so that range samples as empty
        /// (Sample → emptyValue), without writing default values into the curve.
        /// Re-anchors neighbors so a flat plateau is not wiped outside the drag.
        /// </summary>
        public void Erase(int x, int lastX) {
            int empty = descriptor != null ? (int)descriptor.CustomDefaultValue : 0;
            Erase(x, lastX, empty);
        }

        public void Erase(int x, int lastX, int emptyValue) {
            x = (int)Math.Round((float)x / interval) * interval;
            lastX = (int)Math.Round((float)lastX / interval) * interval;
            if (x > lastX) {
                (x, lastX) = (lastX, x);
            }
            // Capture values just outside the erased range before deleting points.
            bool keepLeft = xs.Count > 0 && xs[0] < x;
            bool keepRight = xs.Count > 0 && xs[xs.Count - 1] > lastX;
            int leftAnchorX = x - interval;
            int rightAnchorX = lastX + interval;
            int leftY = keepLeft ? Sample(leftAnchorX, emptyValue) : emptyValue;
            int rightY = keepRight ? Sample(rightAnchorX, emptyValue) : emptyValue;

            DeleteBetweenInclusive(x, lastX);
            if (xs.Count == 0) {
                breaks?.Clear();
                return;
            }

            // Re-anchor so Sample left/right of the hole keeps prior shape.
            // Breaks only apply when both sides remain (a hole in the middle).
            if (keepLeft) {
                Insert(leftAnchorX, leftY);
            }
            if (keepRight) {
                Insert(rightAnchorX, rightY);
            }
            if (keepLeft && keepRight) {
                AddBreak(x);
                if (lastX != x) {
                    AddBreak(lastX);
                }
            } else {
                // Truncating one end: drop breaks that fell inside the deleted range.
                RemoveBreaksBetween(x, lastX);
            }
        }

        private void AddBreak(int x) {
            if (breaks == null) {
                breaks = new List<int>();
            }
            int idx = breaks.BinarySearch(x);
            if (idx < 0) {
                breaks.Insert(~idx, x);
            }
        }

        private void RemoveBreaksBetween(int x0, int x1) {
            if (breaks == null || breaks.Count == 0 || x0 > x1) {
                return;
            }
            int li = breaks.BinarySearch(x0);
            if (li < 0) {
                li = ~li;
            }
            int ri = breaks.BinarySearch(x1);
            if (ri < 0) {
                ri = ~ri - 1;
            }
            if (ri >= li) {
                breaks.RemoveRange(li, ri - li + 1);
            }
        }

        private void DeleteBetweenExclusive(int x1, int x2) {
            int li = xs.BinarySearch(x1);
            if (li >= 0) {
                li++;
            } else {
                li = ~li;
            }
            int ri = xs.BinarySearch(x2);
            if (ri >= 0) {
                ri--;
            } else {
                ri = ~ri - 1;
            }
            if (ri >= li) {
                xs.RemoveRange(li, ri - li + 1);
                ys.RemoveRange(li, ri - li + 1);
            }
        }

        private void DeleteBetweenInclusive(int x1, int x2) {
            int li = xs.BinarySearch(x1);
            if (li < 0) {
                li = ~li;
            }
            int ri = xs.BinarySearch(x2);
            if (ri < 0) {
                ri = ~ri - 1;
            }
            if (ri >= li) {
                xs.RemoveRange(li, ri - li + 1);
                ys.RemoveRange(li, ri - li + 1);
            }
        }
        public void Simplify() {
            if (xs == null || xs.Count < 3) {
                return;
            }
            var anchors = new SortedSet<int> { 0, xs.Count - 1 };
            // Keep endpoints that bound an erase-break so Simplify cannot reopen the hole.
            if (breaks != null) {
                foreach (int b in breaks) {
                    int pos = xs.BinarySearch(b);
                    int left = pos >= 0 ? pos - 1 : ~pos - 1;
                    int right = pos >= 0 ? pos + 1 : ~pos;
                    if (left >= 0) {
                        anchors.Add(left);
                    }
                    if (right < xs.Count) {
                        anchors.Add(right);
                    }
                }
            }
            double tolerance = Math.Min(5, (descriptor.max - descriptor.min) * 0.005);
            var toKeep = new List<int>(anchors);
            var segments = anchors.ToList();
            for (int i = 0; i < segments.Count - 1; i++) {
                Simplify(segments[i], segments[i + 1], tolerance, toKeep);
            }
            toKeep = toKeep.Distinct().OrderBy(i => i).ToList();
            var newXs = new List<int>();
            var newYs = new List<int>();
            foreach (int index in toKeep) {
                newXs.Add(xs[index]);
                newYs.Add(ys[index]);
            }
            xs = newXs;
            ys = newYs;
        }

        public void Simplify(int first, int last, double tolerance, List<int> toKeep) {
            double maxHeight = 0;
            int maxHeightIdx = 0;
            for (int index = first; index < last; index++) {
                double height = PerpendicularDistance(
                    xs[first], ys[first], xs[last], ys[last], xs[index], ys[index]);
                if (height > maxHeight) {
                    maxHeight = height;
                    maxHeightIdx = index;
                }
            }
            if (maxHeight > tolerance && maxHeightIdx != 0) {
                toKeep.Add(maxHeightIdx);
                Simplify(first, maxHeightIdx, tolerance, toKeep);
                Simplify(maxHeightIdx, last, tolerance, toKeep);
            }
        }

        private double PerpendicularDistance(int x, int y, int x1, int y1, int x2, int y2) {
            double area = 0.5 * Math.Abs(x1 * (y2 - y) + x2 * (y - y1) + x * (y1 - y2));
            double bottom = Math.Sqrt(Math.Pow(x1 - x2, 2) + Math.Pow(y1 - y2, 2));
            return area / bottom * 2;
        }
        public static List<UCurve> MergeCurves(params List<UCurve>[] merging) {
            var merged = new Dictionary<UExpressionDescriptor, UCurve>();
            foreach (var curves in merging) {
                foreach (var curve in curves) {
                    if (curve.descriptor == null) continue;
                    if (!merged.TryGetValue(curve.descriptor, out var existing)) {
                        merged[curve.descriptor] = curve.Clone();
                    } else {
                        // Merge xs and ys, keeping them sorted by xs
                        var xs = existing.xs.Concat(curve.xs).ToList();
                        var ys = existing.ys.Concat(curve.ys).ToList();
                        var zipped = xs.Zip(ys, (x, y) => (x, y)).ToList();
                        zipped.Sort((a, b) => a.x.CompareTo(b.x));
                        existing.xs = zipped.Select(z => z.x).ToList();
                        existing.ys = zipped.Select(z => z.y).ToList();
                        existing.breaks = (existing.breaks ?? new List<int>())
                            .Concat(curve.breaks ?? Enumerable.Empty<int>())
                            .Distinct()
                            .OrderBy(b => b)
                            .ToList();
                    }
                }
            }
            return merged.Values.ToList();
        }
    }

    public class CurveSelection {
        public string? Abbr { get; private set; }
        public (int x, int y) StartPoint { get; set; } = (0, 0);
        public (int x, int y) EndPoint { get; set; } = (0, 0);
        private List<int> xs = new List<int>(); // tick from part start
        private List<int> ys = new List<int>();

        public CurveSelection() { }

        public bool HasValue(string? abbr = null) {
            return Abbr != null && (abbr == null || Abbr == abbr);
        }

        public void Clear() {
            Abbr = null;
            StartPoint = (0, 0);
            EndPoint = (0, 0);
            xs.Clear();
            ys.Clear();
        }

        public void Add (string abbr, (int x, int y) startPoint, (int x, int y) endPoint, IEnumerable<int> xs, IEnumerable<int> ys) {
            Abbr = abbr;
            StartPoint = startPoint;
            EndPoint = endPoint;
            this.xs.AddRange(xs);
            this.ys.AddRange(ys);
        }

        public void GetWholeCurveAndSelection(string abbr, UCurve? curve, out List<int> wholeXs, out List<int> wholeYs) {
            wholeXs = new List<int>();
            wholeYs = new List<int>();
            if (curve != null) {
                wholeXs.AddRange(curve.xs);
                wholeYs.AddRange(curve.ys);
            }
            if (HasValue(abbr)) {
                bool flag = false;
                for (int i = 0; i < wholeXs.Count; i++) {
                    int x = wholeXs[i];
                    if (StartPoint.x < x) {
                        wholeXs.Insert(i, StartPoint.x);
                        wholeYs.Insert(i, StartPoint.y);
                        flag = true;
                        break;
                    }
                }
                if (!flag) {
                    wholeXs.Add(StartPoint.x);
                    wholeYs.Add(StartPoint.y);
                }

                if (StartPoint.x != EndPoint.x) {
                    flag = false;
                    for (int i = 0; i < wholeXs.Count; i++) {
                        int x = wholeXs[i];
                        if (EndPoint.x < x) {
                            wholeXs.Insert(i, EndPoint.x);
                            wholeYs.Insert(i, EndPoint.y);
                            flag = true;
                            break;
                        }
                    }
                    if (!flag) {
                        wholeXs.Add(EndPoint.x);
                        wholeYs.Add(EndPoint.y);
                    }
                }
            }
        }

        public void GetSelectedRange(string abbr, out List<int> xs, out List<int> ys) {
            xs = new List<int>();
            ys = new List<int>();
            if (!HasValue(abbr)) {
                return;
            }
            xs.Add(StartPoint.x);
            ys.Add(StartPoint.y);
            xs.AddRange(this.xs);
            ys.AddRange(this.ys);
            xs.Add(EndPoint.x);
            ys.Add(EndPoint.y);
        }

        public CurveSelection Clone() {
            return new CurveSelection() {
                Abbr = Abbr,
                StartPoint = StartPoint,
                EndPoint = EndPoint,
                xs = new List<int>(xs),
                ys = new List<int>(ys)
            };
        }
    }
}
