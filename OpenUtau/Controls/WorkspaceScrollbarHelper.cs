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

        /// <summary>Classic dedicated-track scrollbars are disabled; overlay mode is always used.</summary>
        public static bool UseClassicScrollbars => false;

        sealed class DockPanelScrollTag {
            public bool Classic;
        }

        public static bool IsInVisualTree(Control? control) =>
            control != null && control.IsAttachedToVisualTree();

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
            if (!IsInVisualTree(scrollViewer)) {
                return;
            }
            scrollViewer.Classes.Set("overlay", !classic);

            void apply() {
                ApplyDockPanelScrollLayout(scrollViewer, classic);
                foreach (var bar in scrollViewer.GetVisualDescendants().OfType<ScrollBar>()) {
                    ApplyVerticalScrollBar(bar, classic, scrollViewer);
                }
                Dispatcher.UIThread.Post(
                    () => UpdateDockPanelContentRightMargin(scrollViewer, classic),
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
            UpdateDockPanelContentRightMargin(scrollViewer, classic);
        }

        static void WireDockPanelScrollInset(ScrollViewer scrollViewer, bool classic) {
            if (scrollViewer.Tag is not DockPanelScrollTag tag) {
                tag = new DockPanelScrollTag();
                scrollViewer.Tag = tag;
                scrollViewer.SizeChanged += (_, _) => {
                    if (scrollViewer.Tag is DockPanelScrollTag state) {
                        UpdateDockPanelContentRightMargin(scrollViewer, state.Classic);
                    }
                };
                scrollViewer.ScrollChanged += (_, _) => {
                    if (scrollViewer.Tag is DockPanelScrollTag state) {
                        UpdateDockPanelContentRightMargin(scrollViewer, state.Classic);
                    }
                };
            }
            tag.Classic = classic;
        }

        static void UpdateDockPanelContentRightMargin(ScrollViewer scrollViewer, bool classic) {
            if (!scrollViewer.Classes.Contains("workspaceDockPanelScroll")) {
                return;
            }
            if (scrollViewer.Content is not Control content) {
                return;
            }
            bool needsVerticalScroll = scrollViewer.Extent.Height > scrollViewer.Viewport.Height + 0.5;
            double right = !classic && needsVerticalScroll
                ? WorkspaceDockPanelMetrics.OverlayContentRightMargin
                : 0;
            var margin = content.Margin;
            if (Math.Abs(margin.Right - right) > 0.01) {
                content.Margin = new Thickness(margin.Left, margin.Top, right, margin.Bottom);
            }
        }
    }
}
