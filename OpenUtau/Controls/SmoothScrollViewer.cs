using System;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using OpenUtau.Core.Util;

namespace OpenUtau.App.Controls {
    /// <summary>
    /// Applies the same wheel glide as <see cref="SmoothViewport"/> to any
    /// <see cref="ScrollViewer"/> (menus, singer flyout, preference panes, …).
    /// Honors <see cref="Preferences.SerializablePreferences.ReduceAnimations"/>.
    /// Precision touchpads keep native scrolling; notched mouse wheels glide.
    /// Uses tunnel routing on both the ScrollViewer and its ScrollContentPresenter
    /// so Avalonia's native (unsmoothed) wheel scroll is preempted — including the
    /// case where Shift is not held (presenter remaps Shift→X and otherwise jumps Y).
    /// </summary>
    public static class SmoothScrollViewer {
        const double LineStep = 48;
        const double FastLineStep = 72;

        /// <summary>Larger wheel step (e.g. track singer list with many entries).</summary>
        public const string FastWheelClass = "fastWheelScroll";

        static readonly ConditionalWeakTable<ScrollViewer, State> States = new();

        /// <summary>
        /// Kept for App startup call sites. Does not auto-attach to every ScrollViewer —
        /// piano roll / tracks keep port-commit SmoothViewport on ScrollBars only.
        /// Call <see cref="Attach"/> (via WorkspaceScrollbarHelper / SingerFlyout) where needed.
        /// </summary>
        public static void RegisterClassHandlers() {
        }

        public static void Attach(ScrollViewer scrollViewer) {
            if (scrollViewer == null) {
                return;
            }
            States.GetValue(scrollViewer, sv => new State(sv));
        }

        /// <summary>
        /// Programmatic wheel-equivalent scroll (e.g. singer flyout remaps vertical→horizontal).
        /// </summary>
        public static void ScrollBy(ScrollViewer scrollViewer, double deltaX, double deltaY, bool animate) {
            Attach(scrollViewer);
            if (!States.TryGetValue(scrollViewer, out var state)) {
                return;
            }
            state.ScrollBy(deltaX, deltaY, animate && !Preferences.Default.ReduceAnimations);
        }

        sealed class State {
            readonly ScrollViewer scrollViewer;
            readonly SmoothViewport viewport;
            readonly ValueGlide xGlide;
            readonly ValueGlide yGlide;
            ScrollContentPresenter? presenter;

            public State(ScrollViewer scrollViewer) {
                this.scrollViewer = scrollViewer;
                viewport = new SmoothViewport(scrollViewer);
                xGlide = viewport.Value(
                    () => scrollViewer.Offset.X,
                    v => scrollViewer.Offset = new Vector(v, scrollViewer.Offset.Y),
                    () => 0,
                    MaxX);
                yGlide = viewport.Value(
                    () => scrollViewer.Offset.Y,
                    v => scrollViewer.Offset = new Vector(scrollViewer.Offset.X, v),
                    () => 0,
                    MaxY);
                // Native ScrollContentPresenter wheel must not cancel our glide mid-frame.
                xGlide.IgnoreExternalMoves = true;
                yGlide.IgnoreExternalMoves = true;
                // Tunnel on ScrollViewer: run before bubbling native handlers on descendants.
                scrollViewer.AddHandler(
                    InputElement.PointerWheelChangedEvent,
                    OnWheel,
                    RoutingStrategies.Tunnel,
                    handledEventsToo: false);
                // Also hook the presenter itself: its OnPointerWheelChanged is the actual
                // unsmoothed scroller. Tunnel here runs before that bubble class handler.
                HookPresenter();
                scrollViewer.TemplateApplied += (_, _) => HookPresenter();
            }

            void HookPresenter() {
                var next = scrollViewer.GetVisualDescendants()
                    .OfType<ScrollContentPresenter>()
                    .FirstOrDefault();
                if (next == null || ReferenceEquals(next, presenter)) {
                    return;
                }
                presenter = next;
                presenter.AddHandler(
                    InputElement.PointerWheelChangedEvent,
                    OnWheel,
                    RoutingStrategies.Tunnel,
                    handledEventsToo: false);
            }

            double MaxX() => Math.Max(0, scrollViewer.Extent.Width - scrollViewer.Viewport.Width);
            double MaxY() => Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);

            public void ScrollBy(double deltaX, double deltaY, bool animate) {
                if (Math.Abs(deltaX) > 1e-6 && MaxX() > 0.5) {
                    xGlide.By(deltaX, animate);
                }
                if (Math.Abs(deltaY) > 1e-6 && MaxY() > 0.5) {
                    yGlide.By(deltaY, animate);
                }
            }

            void OnWheel(object? sender, PointerWheelEventArgs e) {
                if (e.Handled) {
                    return;
                }
                bool canX = MaxX() > 0.5;
                bool canY = MaxY() > 0.5;
                if (!canX && !canY) {
                    return;
                }

                bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
                double dx = e.Delta.X;
                double dy = e.Delta.Y;
                // Same rule as Avalonia ScrollContentPresenter: Shift remaps wheel to X
                // only when horizontal scrolling is possible. Vertical-only panes
                // (Project Expressions, DiffSinger settings, …) keep vertical glide
                // even with Shift held.
                bool horizontal = canX && (shift || Math.Abs(dx) > Math.Abs(dy) || !canY);
                double delta = horizontal ? (Math.Abs(dx) > 1e-6 ? dx : dy) : dy;
                if (Math.Abs(delta) < 1e-6) {
                    return;
                }

                // Only defer to intentional nested workspace panel scrollers — not TextBox /
                // ComboBox / ListBox chrome ScrollViewers (those also get overlay styling).
                if (HasInnerPanelScrollableUnderPointer(e, horizontal)) {
                    return;
                }

                bool wheelStep = SmoothViewport.IsWheelStep(delta);
                // Touchpads already stream smooth fractional deltas — leave native scroll.
                if (!wheelStep) {
                    return;
                }

                double lineStep = scrollViewer.Classes.Contains(FastWheelClass)
                    ? FastLineStep
                    : LineStep;
                double amount = -delta * lineStep;
                // At the end of this scroller, don't swallow the wheel so a parent can scroll.
                if (horizontal) {
                    double x = scrollViewer.Offset.X;
                    double maxX = MaxX();
                    if ((amount < 0 && x <= 0.5) || (amount > 0 && x >= maxX - 0.5)) {
                        return;
                    }
                    // Mark handled before mutating offset so the presenter's bubble
                    // OnPointerWheelChanged does not also apply a raw notch.
                    e.Handled = true;
                    xGlide.By(amount, animate: !Preferences.Default.ReduceAnimations);
                } else {
                    double y = scrollViewer.Offset.Y;
                    double maxY = MaxY();
                    if ((amount < 0 && y <= 0.5) || (amount > 0 && y >= maxY - 0.5)) {
                        return;
                    }
                    e.Handled = true;
                    yGlide.By(amount, animate: !Preferences.Default.ReduceAnimations);
                }
            }

            bool HasInnerPanelScrollableUnderPointer(PointerWheelEventArgs e, bool horizontal) {
                var current = e.Source as Visual;
                while (current != null && !ReferenceEquals(current, scrollViewer)) {
                    if (current is ScrollViewer inner
                        && inner.Classes.Contains("workspaceDockPanelScroll")) {
                        bool can = horizontal
                            ? inner.Extent.Width > inner.Viewport.Width + 0.5
                            : inner.Extent.Height > inner.Viewport.Height + 0.5;
                        if (can) {
                            return true;
                        }
                    }
                    current = current.GetVisualParent();
                }
                return false;
            }
        }
    }
}
