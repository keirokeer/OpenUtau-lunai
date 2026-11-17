using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenUtau.Core.Util;

namespace OpenUtau.App.Controls {
    /// <summary>Applies classic vs overlay scrollbar layout and classes for workspace panels.</summary>
    public static class WorkspaceScrollbarHelper {
        public const double OverlayThumbThickness = 8;
        public const double OverlayBarThickness = 10;

        /// <summary>
        /// Opt out of automatic overlay styling (sync-only viewports, custom sibling bars).
        /// </summary>
        public const string NoOverlayScrollClass = "noOverlayScroll";

        /// <summary>Classic dedicated-track scrollbars are disabled; overlay mode is always used.</summary>
        public static bool UseClassicScrollbars => false;

        static bool classHandlersRegistered;

        sealed class DockPanelScrollTag {
            public bool Classic;
        }

        public static bool IsInVisualTree(Control? control) =>
            control != null && control.IsAttachedToVisualTree();

        /// <summary>
        /// Register once at app start so plain ScrollViewers get overlay thumbs
        /// (same look as DiffSinger / piano-roll dock panels), unless opted out.
        /// </summary>
        public static void RegisterClassHandlers() {
            if (classHandlersRegistered) {
                return;
            }
            classHandlersRegistered = true;
            ScrollViewer.LoadedEvent.AddClassHandler<ScrollViewer>((sv, _) => {
                TryApplyDefaultOverlay(sv);
            });
        }

        /// <summary>
        /// Promote a ScrollViewer to Lunai overlay scrollbars when safe.
        /// Skips hidden/disabled sync viewports and <see cref="NoOverlayScrollClass"/>.
        /// </summary>
        public static void TryApplyDefaultOverlay(ScrollViewer scrollViewer) {
            if (scrollViewer == null || !ShouldAutoApplyOverlay(scrollViewer)) {
                return;
            }
            // TextBox/ComboBox/ListBox internals: overlay thumb only — do not mark
            // workspaceScroll so SmoothScrollViewer won't treat them as nested panels.
            if (!IsEmbeddedChromeScroll(scrollViewer)
                && !scrollViewer.Classes.Contains("workspaceScroll")) {
                scrollViewer.Classes.Add("workspaceScroll");
            }
            scrollViewer.AllowAutoHide = false;
            ApplyScrollViewer(scrollViewer, UseClassicScrollbars);
        }

        static bool ShouldAutoApplyOverlay(ScrollViewer scrollViewer) {
            if (scrollViewer.Classes.Contains(NoOverlayScrollClass)) {
                return false;
            }
            // Hidden vertical bar: sync-only viewport or custom sibling ScrollBar.
            if (scrollViewer.VerticalScrollBarVisibility == ScrollBarVisibility.Hidden) {
                return false;
            }
            // No interactive bars at all (e.g. ExpSelector popup with Disabled/Disabled).
            if (scrollViewer.VerticalScrollBarVisibility == ScrollBarVisibility.Disabled
                && scrollViewer.HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled) {
                return false;
            }
            return true;
        }

        static bool IsEmbeddedChromeScroll(ScrollViewer scrollViewer) {
            for (Visual? p = scrollViewer.GetVisualParent(); p != null; p = p.GetVisualParent()) {
                if (p is TextBox or ComboBox or ListBox or AutoCompleteBox or DataGrid or TreeView) {
                    return true;
                }
                // Stop at the nearest outer ScrollViewer (panel scroller).
                if (p is ScrollViewer) {
                    break;
                }
            }
            return false;
        }

        public static void ApplyHorizontalScrollBar(ScrollBar bar, bool classic) {
            if (!IsInVisualTree(bar)) {
                return;
            }
            bar.Classes.Set("overlay", !classic);
            bar.Classes.Set("music", classic);
            bar.AllowAutoHide = false;
            if (classic) {
                bar.HorizontalAlignment = HorizontalAlignment.Stretch;
                bar.VerticalAlignment = VerticalAlignment.Stretch;
                bar.Width = double.NaN;
                bar.Height = double.NaN;
                bar.Margin = new Thickness(0, 4, 0, 0);
                bar.ZIndex = 0;
            } else {
                bar.HorizontalAlignment = HorizontalAlignment.Stretch;
                bar.VerticalAlignment = VerticalAlignment.Bottom;
                bar.Width = double.NaN;
                bar.Height = OverlayBarThickness;
                bar.ZIndex = 350;
            }
        }

        public static void ApplyVerticalScrollBar(ScrollBar bar, bool classic, ScrollViewer? hostScrollViewer = null) {
            if (!IsInVisualTree(bar)) {
                return;
            }
            bar.Classes.Set("overlay", !classic);
            bar.Classes.Set("music", classic);
            bar.AllowAutoHide = false;
            if (classic) {
                bar.HorizontalAlignment = HorizontalAlignment.Stretch;
                bar.VerticalAlignment = VerticalAlignment.Stretch;
                bar.Width = double.NaN;
                bar.Height = double.NaN;
                bar.Margin = new Thickness(4, 0, 4, 0);
                bar.ZIndex = 0;
            } else {
                bool dockPanelScroll = hostScrollViewer?.Classes.Contains("workspaceDockPanelScroll") == true;
                bar.HorizontalAlignment = HorizontalAlignment.Right;
                bar.VerticalAlignment = VerticalAlignment.Stretch;
                bar.Width = OverlayBarThickness;
                bar.Height = double.NaN;
                bar.Margin = dockPanelScroll ? new Thickness(0) : new Thickness(0, 0, 3, 0);
                bar.ZIndex = 400;
            }
        }

        public static void ApplyScrollViewer(ScrollViewer scrollViewer, bool classic) {
            // Always mark overlay up front so the first paint is not classic, even when the
            // viewer is not in the visual tree yet (e.g. flyout content before ShowAt).
            scrollViewer.Classes.Set("overlay", !classic);
            SmoothScrollViewer.Attach(scrollViewer);
            if (!IsInVisualTree(scrollViewer)) {
                void onAttached(object? s, VisualTreeAttachmentEventArgs e) {
                    scrollViewer.AttachedToVisualTree -= onAttached;
                    ApplyScrollViewer(scrollViewer, classic);
                }
                scrollViewer.AttachedToVisualTree += onAttached;
                return;
            }

            void apply() {
                ApplyDockPanelScrollLayout(scrollViewer, classic);
                foreach (var bar in scrollViewer.GetVisualDescendants().OfType<ScrollBar>()) {
                    if (bar.Orientation == Orientation.Horizontal) {
                        ApplyHorizontalScrollBar(bar, classic);
                    } else {
                        ApplyVerticalScrollBar(bar, classic, scrollViewer);
                    }
                }
                Dispatcher.UIThread.Post(
                    () => UpdateDockPanelContentInsets(scrollViewer, classic),
                    DispatcherPriority.Loaded);
            }

            if (scrollViewer.IsInitialized) {
                apply();
            } else {
                scrollViewer.Loaded += (_, _) => apply();
            }
        }

        static void ApplyDockPanelScrollLayout(ScrollViewer scrollViewer, bool classic) {
            if (!scrollViewer.Classes.Contains("workspaceDockPanelScroll")) {
                return;
            }
            scrollViewer.Padding = new Thickness(0);
            scrollViewer.ClipToBounds = false;
            if (scrollViewer.Content is not Control content) {
                return;
            }
            content.HorizontalAlignment = HorizontalAlignment.Stretch;
            WireDockPanelScrollInset(scrollViewer, classic);
            UpdateDockPanelContentInsets(scrollViewer, classic);
        }

        static void WireDockPanelScrollInset(ScrollViewer scrollViewer, bool classic) {
            if (scrollViewer.Tag is not DockPanelScrollTag tag) {
                tag = new DockPanelScrollTag();
                scrollViewer.Tag = tag;
                scrollViewer.SizeChanged += (_, _) => {
                    if (scrollViewer.Tag is DockPanelScrollTag state) {
                        UpdateDockPanelContentInsets(scrollViewer, state.Classic);
                    }
                };
                scrollViewer.ScrollChanged += (_, _) => {
                    if (scrollViewer.Tag is DockPanelScrollTag state) {
                        UpdateDockPanelContentInsets(scrollViewer, state.Classic);
                    }
                };
            }
            tag.Classic = classic;
        }

        static void UpdateDockPanelContentInsets(ScrollViewer scrollViewer, bool classic) {
            if (!scrollViewer.Classes.Contains("workspaceDockPanelScroll")) {
                return;
            }
            if (scrollViewer.Content is not Control content) {
                return;
            }
            bool needsVerticalScroll = scrollViewer.Extent.Height > scrollViewer.Viewport.Height + 0.5;
            bool needsHorizontalScroll = scrollViewer.Extent.Width > scrollViewer.Viewport.Width + 0.5;
            double inset = scrollViewer.Classes.Contains("tightOverlayInset")
                ? WorkspaceDockPanelMetrics.TightOverlayContentRightMargin
                : WorkspaceDockPanelMetrics.OverlayContentRightMargin;
            double right = (!classic && needsVerticalScroll) ? inset : 0;
            double bottom = (!classic && needsHorizontalScroll) ? inset : 0;
            var margin = content.Margin;
            if (Math.Abs(margin.Right - right) > 0.01 || Math.Abs(margin.Bottom - bottom) > 0.01) {
                content.Margin = new Thickness(margin.Left, margin.Top, right, bottom);
            }
        }

        /// <summary>Re-apply bar layout after a ScrollViewer switches H/V visibility (e.g. flyout expand).</summary>
        public static void RefreshScrollBars(ScrollViewer scrollViewer) {
            if (!IsInVisualTree(scrollViewer)) {
                return;
            }
            bool classic = !scrollViewer.Classes.Contains("overlay");
            foreach (var bar in scrollViewer.GetVisualDescendants().OfType<ScrollBar>()) {
                if (bar.Orientation == Orientation.Horizontal) {
                    ApplyHorizontalScrollBar(bar, classic);
                } else {
                    ApplyVerticalScrollBar(bar, classic, scrollViewer);
                }
            }
            UpdateDockPanelContentInsets(scrollViewer, classic);
        }
    }
}
