using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media.TextFormatting;
using OpenUtau.App.Controls;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;

namespace OpenUtau.App.ViewModels {
    public struct NoteHitInfo {
        public UNote note;
        public UPhoneme phoneme;
        public bool hitBody;
        public bool hitResizeArea;
        public bool hitResizeAreaFromStart;
        public bool hitX;
    }

    public struct PitchPointHitInfo {
        public UNote Note;
        public int Index;
        public bool OnPoint;
        public float X;
        public float Y;
    }

    public struct VibratoHitInfo {
        public UNote note;
        public bool hit;
        public bool hitToggle;
        public bool hitStart;
        public bool hitIn;
        public bool hitOut;
        public bool hitDepth;
        public bool hitShift;
        public bool hitPeriod;
        public Point point;
        public float initialShift;
    }

    public struct PhonemeHitInfo {
        public UPhoneme phoneme;
        public bool hit;
        public bool hitPosition;
        public bool hitPreutter;
        public bool hitOverlap;
        public bool hitAttackTime;
        public bool hitReleaseTime;
        public Point point;
    }

    public struct AliasHitInfo {
        public UPhoneme phoneme;
        public bool hit;
        /// <summary>When true, tag line was hit; when false (and hit), phoneme line was hit. Only in DiffSinger phoneme panel mode.</summary>
        public bool hitTagLine;
        public Point point;
    }

    class NotesViewModelHitTest {
        private readonly NotesViewModel viewModel;

        public NotesViewModelHitTest(NotesViewModel viewModel) {
            this.viewModel = viewModel;
        }

        public NoteHitInfo HitTestNote(Point point) {
            if (viewModel.Part == null) {
                return default;
            }
            NoteHitInfo result = default;
            int tick = viewModel.PointToTick(point);
            foreach (UNote note in viewModel.Part.notes) {
                if (note.position > tick || note.End < tick) {
                    continue;
                }
                result.note = note;
                result.hitX = true;
                var tone = viewModel.PointToToneDouble(point);
                if (tone > note.AdjustedTone + 0.5 || tone < note.AdjustedTone - 0.5) {
                    continue;
                }
                result.hitBody = true;
                double x1 = viewModel.TickToneToPoint(note.position, note.AdjustedTone).X;
                double x2 = viewModel.TickToneToPoint(note.End, tone).X;
                var hitLeftResizeArea = point.X >= x1 && point.X < x1 + ViewConstants.ResizeMargin;
                var hitRightResizeArea = point.X <= x2 && point.X > x2 - ViewConstants.ResizeMargin;
                result.hitResizeAreaFromStart = hitLeftResizeArea && !hitRightResizeArea;  // prefer resizing from end
                result.hitResizeArea = hitLeftResizeArea || hitRightResizeArea;  // hit either of the areas
                break;
            }
            return result;
        }

        public NoteHitInfo HitTestExp(Point point) {
            if (viewModel.Part == null) {
                return default;
            }
            int tick = viewModel.PointToTick(point);
            foreach (var phoneme in viewModel.Part.phonemes) {
                double leftBound = phoneme.position;
                double rightBound = phoneme.End;
                var note = phoneme.Parent;
                if (leftBound > tick || rightBound < tick) {
                    continue;
                }
                int left = phoneme.position;
                int right = phoneme.End;
                if (left <= tick && tick <= right) {
                    return new NoteHitInfo {
                        note = note,
                        phoneme = phoneme,
                        hitX = true,
                    };
                }
            }
            return default;
        }

        public List<NoteHitInfo> HitTestExpRange(Point point1, Point point2) {
            var hits = new List<NoteHitInfo>();
            if (viewModel.Part == null) {
                return hits;
            }
            int tick1 = viewModel.PointToTick(point1);
            int tick2 = viewModel.PointToTick(point2);
            foreach (var phoneme in viewModel.Part.phonemes) {
                double leftBound = phoneme.position;
                double rightBound = phoneme.End;
                var note = phoneme.Parent;
                if (leftBound > tick2 || rightBound < tick1) {
                    continue;
                }
                int left = phoneme.position;
                int right = phoneme.End;
                if (left <= tick2 && tick1 <= right) {
                    hits.Add(new NoteHitInfo {
                        note = note,
                        phoneme = phoneme,
                        hitX = true,
                    });
                }
            }
            return hits;
        }

        public PitchPointHitInfo HitTestPitchPoint(Point point, bool pitchPointTool = false) {
            if (viewModel.Part == null || !viewModel.ShowPitch) {
                return default;
            }
            double leftTick = viewModel.TickOffset - 480;
            double rightTick = leftTick + viewModel.ViewportTicks + 480;
            foreach (var note in viewModel.Part.notes) {
                if (note.LeftBound >= rightTick || note.RightBound <= leftTick || note.Error) {
                    continue;
                }
                if (Preferences.Default.LockUnselectedNotesPitch && viewModel.Selection.Count > 0 && !viewModel.Selection.Contains(note)) {
                    continue;
                }
                double lastX = 0, lastY = 0;
                double x_1 = 0, y_1 = 0;
                PitchPointShape lastShape = PitchPointShape.l;
                var timeAxis = viewModel.Project.timeAxis;
                for (int i = 0; i < note.pitch.data.Count; i++) {
                    var pit = note.pitch.data[i];
                    int posTick = timeAxis.MsPosToTickPos(note.PositionMs + pit.X) - viewModel.Part.position;
                    double tone = note.AdjustedTone + pit.Y / 10;
                    var pitPoint = viewModel.TickToneToPoint(posTick, tone);
                    double x = pitPoint.X;
                    double y = pitPoint.Y + viewModel.TrackHeight / 2;
                    if (Math.Abs(point.X - x) < 4 && Math.Abs(point.Y - y) < 4)
                        return new PitchPointHitInfo() {
                            Note = note,
                            Index = i,
                            OnPoint = true,
                        };
                    else if (point.X < x && i > 0 && point.X > lastX) {
                        // Hit test curve
                        double castY;
                        CubicSplineSegment? curve = null;
                        if (pitchPointTool) {
                            double msX = timeAxis.TickPosToMsPos(viewModel.PointToTick(point) + viewModel.Part.position) - note.PositionMs;
                            double decCentY = (viewModel.PointToToneDouble(point) - note.AdjustedTone) * 10;
                            return new PitchPointHitInfo() {
                                Note = note,
                                Index = i - 1,
                                OnPoint = false,
                                X = (float)msX,
                                Y = (float)decCentY,
                            };
                        } else if (note.pitch.data.Count > 2 && note.pitch.data[i - 1].shape == PitchPointShape.sp) {
                            double x2 = x, y2 = y;
                            if (i == 1) {
                                if (note.pitch.data[0].X > 0) {
                                    var pitPoint_1 = viewModel.TickToneToPoint(note.position, note.AdjustedTone);
                                    x2 = pitPoint_1.X;
                                } else {
                                    x_1 = lastX;
                                }
                                y_1 = lastY;
                            }
                            if (i < note.pitch.data.Count - 1) {
                                var pit2 = note.pitch.data[i + 1];
                                int posTick2 = timeAxis.MsPosToTickPos(note.PositionMs + pit2.X) - viewModel.Part.position;
                                double tone2 = note.AdjustedTone + pit2.Y / 10;
                                var pitPoint2 = viewModel.TickToneToPoint(posTick2, tone2);
                                x2 = pitPoint2.X;
                                y2 = pitPoint2.Y + viewModel.TrackHeight / 2;
                            } else if (note.pitch.data[i].X < note.DurationMs) {
                                var pitPoint2 = viewModel.TickToneToPoint(note.End, note.AdjustedTone);
                                x2 = pitPoint2.X;
                                y2 = pitPoint2.Y + viewModel.TrackHeight / 2;
                            }
                            curve = new CubicSplineSegment(
                                        x_1, y_1,
                                        lastX, lastY,
                                        x, y,
                                        x2, y2);
                            castY = curve.GetY(point.X) - point.Y;
                        } else {
                            if (y >= lastY) {
                                if (point.Y - y > 3 || lastY - point.Y > 3) break;
                            } else {
                                if (y - point.Y > 3 || point.Y - lastY > 3) break;
                            }
                            castY = MusicMath.InterpolateShape(lastX, x, lastY, y, point.X, lastShape) - point.Y;
                        }
                        double castX = (curve?.GetX(point.Y) ?? MusicMath.InterpolateShapeX(lastX, x, lastY, y, point.Y, lastShape)) - point.X;
                        double dis = double.IsNaN(castX) ? Math.Abs(castY) : Math.Cos(Math.Atan2(Math.Abs(castY), Math.Abs(castX))) * Math.Abs(castY);
                        if (dis < 3) {
                            double msX = timeAxis.TickPosToMsPos(viewModel.PointToTick(point) + viewModel.Part.position) - note.PositionMs;
                            double decCentY = (viewModel.PointToToneDouble(point) - note.AdjustedTone) * 10;
                            return new PitchPointHitInfo() {
                                Note = note,
                                Index = i - 1,
                                OnPoint = false,
                                X = (float)msX,
                                Y = (float)decCentY,
                            };
                        } else break;
                    }
                    x_1 = lastX;
                    y_1 = lastY;
                    lastX = x;
                    lastY = y;
                    lastShape = pit.shape;
                }
            }
            return default;
        }

        public double? SamplePitch(Point point) {
            if (viewModel.Part == null) {
                return null;
            }
            double tick = viewModel.PointToTick(point);
            var note = viewModel.Part.notes.FirstOrDefault(n => n.End >= tick);
            if (note == null && viewModel.Part.notes.Count > 0) {
                note = viewModel.Part.notes.Last();
            }
            if (note == null) {
                return null;
            }
            double pitch = note.AdjustedTone * 100;
            pitch += note.pitch.Sample(viewModel.Project, viewModel.Part, note, tick) ?? 0;
            if (note.Next != null && note.Next.position == note.End) {
                double? delta = note.Next.pitch.Sample(viewModel.Project, viewModel.Part, note.Next, tick);
                if (delta != null) {
                    pitch += delta.Value + note.Next.AdjustedTone * 100 - note.AdjustedTone * 100;
                }
            }
            return pitch;
        }

        public double? SampleOverwritePitch(Point point) {
            if (viewModel.Part == null || viewModel.Part.renderPhrases.Count == 0) {
                return null;
            }
            double tick = viewModel.PointToTick(point);
            double absTick = tick + viewModel.Part.position;

            var phrase = viewModel.Part.renderPhrases.FirstOrDefault(p => p.end >= absTick);
            if (phrase == null) {
                phrase = viewModel.Part.renderPhrases.Last();
            }
            if (phrase == null || phrase.pitchesBeforeDeviation.Length == 0) {
                return null;
            }
            var curve = phrase.pitchesBeforeDeviation;
            int phraseStartRel = phrase.position - viewModel.Part.position;
            var pitchIndex = (int)Math.Round((tick - phraseStartRel + phrase.leading) / 5.0);
            pitchIndex = Math.Clamp(pitchIndex, 0, curve.Length - 1);
            return curve[pitchIndex];
        }

        public VibratoHitInfo HitTestVibrato(Point mousePos) {
            if (viewModel.Part == null || !viewModel.ShowVibrato) {
                return default;
            }
            VibratoHitInfo result = default;
            result.point = mousePos;
            foreach (var note in viewModel.Part.notes) {
                if (Preferences.Default.LockUnselectedNotesVibrato && viewModel.Selection.Count > 0 && !viewModel.Selection.Contains(note)) {
                    continue;
                }
                result.note = note;
                UVibrato vibrato = note.vibrato;
                Point toggle = viewModel.TickToneToPoint(vibrato.GetToggle(note));
                toggle = toggle.WithX(toggle.X - 10);
                if (WithIn(toggle, mousePos, 5)) {
                    result.hit = true;
                    result.hitToggle = true;
                    return result;
                }
                if (vibrato.length == 0) {
                    continue;
                }
                Point start = viewModel.TickToneToPoint(vibrato.GetEnvelopeStart(note));
                Point fadeIn = viewModel.TickToneToPoint(vibrato.GetEnvelopeFadeIn(note));
                Point fadeOut = viewModel.TickToneToPoint(vibrato.GetEnvelopeFadeOut(note));
                if (WithIn(start, mousePos, 3)) {
                    result.hit = true;
                    result.hitStart = true;
                } else if (WithIn(fadeIn, mousePos, 3)) {
                    result.hit = true;
                    result.hitIn = true;
                } else if (WithIn(fadeOut, mousePos, 3)) {
                    result.hit = true;
                    result.hitOut = true;
                } else if (Math.Abs(fadeIn.Y - mousePos.Y) < 3 && fadeIn.X < mousePos.X && mousePos.X < fadeOut.X) {
                    result.hit = true;
                    result.hitDepth = true;
                }

                vibrato.GetPeriodStartEnd(DocManager.Inst.Project, note, out var periodStartPos, out var periodEndPos);
                Point periodStart = viewModel.TickToneToPoint(periodStartPos);
                Point periodEnd = viewModel.TickToneToPoint(periodEndPos);
                if (Math.Abs(mousePos.Y - periodEnd.Y) < viewModel.TrackHeight / 6) {
                    if (Math.Abs(mousePos.X - periodEnd.X) < 3) {
                        result.hit = true;
                        result.hitPeriod = true;
                    } else if (mousePos.X > periodStart.X && mousePos.X < periodEnd.X) {
                        result.hit = true;
                        result.hitShift = true;
                        result.initialShift = vibrato.shift;
                    }
                }
                if (result.hit) {
                    return result;
                }
            }
            return default;
        }

        public PhonemeHitInfo HitTestPhoneme(Point mousePos) {
            var part = viewModel.Part;
            if (part == null || !viewModel.ShowPhoneme) {
                return default;
            }
            var timeAxis = viewModel.Project.timeAxis;
            PhonemeHitInfo result = default;
            result.point = mousePos;
            double leftTick = viewModel.TickOffset - 480;
            double rightTick = leftTick + viewModel.ViewportTicks + 480;
            foreach (var phoneme in part.phonemes) {
                double leftBound = timeAxis.MsPosToTickPos(phoneme.PositionMs - phoneme.preutter) - part.position;
                double rightBound = phoneme.End;
                var note = phoneme.Parent;
                if (leftBound >= rightTick || rightBound <= leftTick || note.Error || note.OverlapError) {
                    continue;
                }
                Point point;
                // In DiffSinger phoneme panel mode, preutter/overlap handles are not shown or draggable
                if (!Preferences.Default.DiffSingerPhonemePanelMode) {
                    var (barY, barHeight) = ViewConstants.GetClassicPhonemeEnvelopeLayout();
                    int p0Tick = timeAxis.MsPosToTickPos(phoneme.PositionMs + phoneme.envelope.data[0].X) - part.position;
                    double p0x = viewModel.TickToneToPoint(p0Tick, 0).X;
                    var pt = new Point(p0x, barY + (1 - phoneme.envelope.data[0].Y / 100) * barHeight - 1);
                    if (WithIn(pt, mousePos, 3)) {
                        result.phoneme = phoneme;
                        result.hit = true;
                        result.hitPreutter = true;
                        return result;
                    }
                    int p1Tick = timeAxis.MsPosToTickPos(phoneme.PositionMs + phoneme.envelope.data[1].X) - part.position;
                    double p1x = viewModel.TickToneToPoint(p1Tick, 0).X;
                    pt = new Point(p1x, barY + (1 - phoneme.envelope.data[1].Y / 100) * barHeight);
                    if (WithIn(pt, mousePos, 3)) {
                        result.phoneme = phoneme;
                        result.hit = true;
                        result.hitAttackTime = true;
                        return result;
                    }
                    int p3Tick = timeAxis.MsPosToTickPos(phoneme.PositionMs + phoneme.envelope.data[3].X) - part.position;
                    double p3x = viewModel.TickToneToPoint(p3Tick, 0).X;
                    pt = new Point(p3x, barY + (1 - phoneme.envelope.data[3].Y / 100) * barHeight);
                    if (WithIn(pt, mousePos, 3)) {
                        result.phoneme = phoneme;
                        result.hit = true;
                        result.hitReleaseTime = true;
                        return result;
                    }
                    int p4Tick = timeAxis.MsPosToTickPos(phoneme.PositionMs + phoneme.envelope.data[4].X) - part.position;
                    double p4x = viewModel.TickToneToPoint(p4Tick, 0).X;
                    pt = new Point(p4x, barY + (1 - phoneme.envelope.data[4].Y / 100) * barHeight - 1);
                    if (phoneme.Next != null && phoneme.Next.position == phoneme.End && WithIn(pt, mousePos, 3)) {
                        result.phoneme = phoneme;
                        result.hit = true;
                        result.hitOverlap = true;
                        return result;
                    }
                }
                point = viewModel.TickToneToPoint(phoneme.position, 0);
                double xLeft = point.X;
                // DiffSinger: use rect-based hit (10px wide strip centered on bar) within bar Y bounds for reliable grabbing
                if (Preferences.Default.DiffSingerPhonemePanelMode) {
                    double tagStripH = 0;
                    if (part.trackNo < viewModel.Project.tracks.Count) {
                        var track = viewModel.Project.tracks[part.trackNo];
                        if (!PhonemeUIRender.ShouldHideLangPrefixForDisplay(track)) {
                            tagStripH = ViewConstants.PhonemeTagStripHeight;
                        }
                    }
                    double barY = tagStripH;
                    double barH = viewModel.PhonemePanelHeight;
                    const double grabWidth = 5;
                    var grabRect = new Rect(xLeft - grabWidth, barY, grabWidth * 2, barH);
                    if (grabRect.Contains(mousePos)) {
                        result.phoneme = phoneme;
                        result.hit = true;
                        result.hitPosition = true;
                        return result;
                    }
                } else if (Math.Abs(xLeft - mousePos.X) < 3) {
                    result.phoneme = phoneme;
                    result.hit = true;
                    result.hitPosition = true;
                    return result;
                }
            }
            return result;
        }

        public AliasHitInfo HitTestAlias(Point mousePos) {
            var part = viewModel.Part;
            if (part == null || !viewModel.ShowPhoneme) {
                return default;
            }
            AliasHitInfo result = default;
            result.point = mousePos;
            double lastTextEndX = double.NegativeInfinity;
            bool raiseText = false;
            double leftTick = viewModel.TickOffset - 480;
            double rightTick = leftTick + viewModel.ViewportTicks + 480;
            string langCode = PhonemeUIRender.getLangCode(part);
            // TODO: Rewrite with a faster searching algorithm, such as binary search.
            foreach (var phoneme in part.phonemes) {
                double leftBound = viewModel.Project.timeAxis.MsPosToTickPos(phoneme.PositionMs - phoneme.preutter) - part.position;
                double rightBound = phoneme.End;
                var note = phoneme.Parent;
                if (leftBound >= rightTick || rightBound <= leftTick) {
                    continue;
                }
                if (note.OverlapError) {
                    continue;
                }
                if (viewModel.TickWidth <= ViewConstants.PianoRollTickWidthShowDetails) {
                    continue;
                }
                // DiffSinger mode: tag above bars (tag strip +20px, hidden when DiffSingerLangCodeHide), phoneme inside bars; bar height = PhonemePanelHeight
                if (Preferences.Default.DiffSingerPhonemePanelMode) {
                    double tagStripHeight = 0;
                    if (part.trackNo < viewModel.Project.tracks.Count) {
                        var track = viewModel.Project.tracks[part.trackNo];
                        if (!PhonemeUIRender.ShouldHideLangPrefixForDisplay(track)) {
                            tagStripHeight = ViewConstants.PhonemeTagStripHeight;
                        }
                    }
                    double barY = tagStripHeight;
                    double barHeight = viewModel.PhonemePanelHeight;
                    const double leftEdgeWidth = 5;
                    double xLeft = viewModel.TickToneToPoint(phoneme.position, 0).X;
                    double xRight = viewModel.TickToneToPoint(phoneme.End, 0).X;
                    double w = Math.Max(0, xRight - xLeft - leftEdgeWidth);
                    var tagRect = new Rect(xLeft + leftEdgeWidth, 0, w, tagStripHeight);
                    var phonemeRect = new Rect(xLeft + leftEdgeWidth, barY, w, barHeight);
                    if (tagRect.Contains(mousePos)) {
                        result.phoneme = phoneme;
                        result.hit = true;
                        result.hitTagLine = true;
                        return result;
                    }
                    if (phonemeRect.Contains(mousePos)) {
                        result.phoneme = phoneme;
                        result.hit = true;
                        result.hitTagLine = false;
                        return result;
                    }
                    continue;
                }
                string phonemeText = !string.IsNullOrEmpty(phoneme.phonemeMapped) ? phoneme.phonemeMapped : phoneme.phoneme;
                if (string.IsNullOrEmpty(phonemeText)) {
                    continue;
                }
                (double textX, double textY, Size size, TextLayout textLayout)
                    = PhonemeUIRender.AliasPosition(viewModel, phoneme, langCode, ref lastTextEndX, ref raiseText);
                var rect = new Rect(new Point(textX - 2, textY + 1.5), size);
                if (rect.Contains(mousePos)) {
                    result.phoneme = phoneme;
                    result.hit = true;
                    return result;
                }
            }
            return result;
        }

        private bool WithIn(Point p0, Point p1, double dist) {
            return Math.Abs(p0.X - p1.X) < dist && Math.Abs(p0.Y - p1.Y) < dist;
        }
    }
}
