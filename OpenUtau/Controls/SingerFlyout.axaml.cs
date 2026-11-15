using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using ReactiveUI;

namespace OpenUtau.App.Controls {
    public partial class SingerFlyout : UserControl {
        const double ListWidth = 260;
        const double GridTileWidth = 180;
        const double TileHeight = 40;
        const double SearchHeight = 32;
        const double TypeFilterHeight = 28;
        const int MinVisibleRows = 4;
        const double BaseChromeHeight = 50 + SearchHeight;
        const double ChromeWidth = 20;
        const double WheelStep = 60;

        private int maxRows = int.MaxValue;
        private int maxColumns = 1;
        private Visual? fitAnchor;
        private IDisposable? sizeSubscription;
        private Control? pressedTile;
        private bool layoutExpanded;

        public SingerFlyout() {
            InitializeComponent();
            // Mark overlay before the flyout is shown so the first paint is never classic.
            TileScroller.Classes.Set("overlay", true);
            WorkspaceScrollbarHelper.ApplyScrollViewer(
                TileScroller, WorkspaceScrollbarHelper.UseClassicScrollbars);
            Loaded += OnLoaded;
            TileScroller.AddHandler(PointerWheelChangedEvent, TileScrollerWheel, RoutingStrategies.Tunnel);
        }

        void OnLoaded(object? sender, RoutedEventArgs e) {
            ApplyLayoutMode(force: true);
            UpdateSize();
            WorkspaceScrollbarHelper.ApplyScrollViewer(
                TileScroller, WorkspaceScrollbarHelper.UseClassicScrollbars);
            WorkspaceScrollbarHelper.RefreshScrollBars(TileScroller);
            Dispatcher.UIThread.Post(() => SearchBox.Focus(), DispatcherPriority.Loaded);
        }

        protected override void OnDataContextChanged(EventArgs e) {
            base.OnDataContextChanged(e);
            sizeSubscription?.Dispose();
            sizeSubscription = null;
            if (DataContext is SingerFlyoutViewModel viewModel) {
                sizeSubscription = viewModel.WhenAnyValue(
                        x => x.ListSizeCount, x => x.GridTileCount, x => x.ShowTypeFilter, x => x.ExpandedGrid)
                    .Subscribe(_ => {
                        ApplyLayoutMode();
                        UpdateSize();
                    });
            }
        }

        public void FitToScreen(Visual anchor) {
            fitAnchor = anchor;
            maxRows = int.MaxValue;
            maxColumns = 1;
            var topLevel = TopLevel.GetTopLevel(anchor);
            var screen = topLevel?.Screens?.ScreenFromVisual(anchor);
            var anchorTop = anchor.PointToScreen(new Point(0, 0));
            var anchorBottom = anchor.PointToScreen(new Point(0, anchor.Bounds.Height));
            if (screen != null) {
                var area = screen.WorkingArea;
                double chrome = BaseChromeHeight + (ShowTypeChips() ? TypeFilterHeight : 0);
                int RowsFor(double pixels) => (int)((pixels / screen.Scaling - chrome) / TileHeight);
                int below = RowsFor(area.Bottom - anchorBottom.Y);
                int above = RowsFor(anchorTop.Y - area.Y);
                maxRows = Math.Max(MinVisibleRows, below >= MinVisibleRows ? below : Math.Max(below, above));
                int right = area.Right;
                if (topLevel != null) {
                    right = Math.Min(right, topLevel.PointToScreen(new Point(topLevel.Bounds.Width, 0)).X);
                }
                double width = (right - anchorTop.X) / screen.Scaling - ChromeWidth;
                maxColumns = Math.Max(1, (int)(width / GridTileWidth));
            }
            ApplyLayoutMode(force: true);
            UpdateSize();
            WorkspaceScrollbarHelper.ApplyScrollViewer(
                TileScroller, WorkspaceScrollbarHelper.UseClassicScrollbars);
            WorkspaceScrollbarHelper.RefreshScrollBars(TileScroller);
        }

        bool ShowTypeChips() => (DataContext as SingerFlyoutViewModel)?.ShowTypeFilter ?? false;

        bool IsExpanded() => (DataContext as SingerFlyoutViewModel)?.ExpandedGrid == true;

        void ApplyLayoutMode(bool force = false) {
            bool expanded = IsExpanded();
            Classes.Set("expanded", expanded);
            if (ExpandButton != null) {
                ExpandButton.Classes.Set("on", expanded);
                ToolTip.SetTip(ExpandButton, ThemeManager.GetString(expanded
                    ? "tracks.singerflyout.collapse"
                    : "tracks.singerflyout.expand"));
            }

            if (force || layoutExpanded != expanded) {
                layoutExpanded = expanded;
                if (TilesControl != null) {
                    // List: vertical stack (stretch tiles). Expanded: vertical wrap → columns.
                    TilesControl.ItemsPanel = expanded
                        ? new FuncTemplate<Panel?>(() => new WrapPanel {
                            Orientation = Orientation.Vertical,
                        })
                        : new FuncTemplate<Panel?>(() => new StackPanel());
                    TilesControl.ClearValue(WidthProperty);
                }
            }

            if (expanded) {
                TileScroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
                TileScroller.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            } else {
                TileScroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
                TileScroller.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            }
            TileScroller.Offset = new Vector(0, 0);
            // H/V bars are created lazily; restyle after visibility flips (expand/collapse).
            Dispatcher.UIThread.Post(
                () => WorkspaceScrollbarHelper.RefreshScrollBars(TileScroller),
                DispatcherPriority.Loaded);
        }

        void UpdateSize() {
            var vm = DataContext as SingerFlyoutViewModel;
            bool expanded = vm?.ExpandedGrid == true;
            int count = expanded
                ? (vm?.GridTileCount ?? 1)
                : (vm?.ListSizeCount ?? 1);
            count = Math.Max(1, count);

            if (!expanded) {
                // Same sizing as before the expand feature.
                int rows = Math.Clamp(count, 1, Math.Max(1, maxRows));
                TileScroller.Height = rows * TileHeight;
                TileScroller.MinWidth = ListWidth;
                TileScroller.ClearValue(MaxWidthProperty);
                TileScroller.Width = ListWidth;
                if (TypeChips is { IsVisible: true } && TypeChips.Bounds.Width > ListWidth) {
                    TileScroller.Width = TypeChips.Bounds.Width;
                }
                if (TilesControl != null) {
                    TilesControl.ClearValue(WidthProperty);
                }
                TileScroller.Offset = new Vector(0, 0);
                return;
            }

            // Upstream-style: fill height with rows, grow columns; MaxWidth caps the viewport so
            // extra columns scroll horizontally (do not set Width to the full content width).
            int rowsNeeded = Math.Clamp(count, 1, Math.Max(1, maxRows));
            int columnsNeeded = Math.Max(1, (count + rowsNeeded - 1) / rowsNeeded);
            int visibleColumns = Math.Min(columnsNeeded, Math.Max(1, maxColumns));
            TileScroller.Height = rowsNeeded * TileHeight;
            TileScroller.MinWidth = visibleColumns * GridTileWidth;
            TileScroller.MaxWidth = Math.Max(1, maxColumns) * GridTileWidth;
            // Prefer content width up to the screen cap; overflow columns use H-scroll.
            TileScroller.Width = visibleColumns * GridTileWidth;
            if (TilesControl != null) {
                TilesControl.ClearValue(WidthProperty);
            }
            TileScroller.Offset = new Vector(0, 0);
            Dispatcher.UIThread.Post(
                () => WorkspaceScrollbarHelper.RefreshScrollBars(TileScroller),
                DispatcherPriority.Loaded);
        }

        void TileScrollerWheel(object? sender, PointerWheelEventArgs e) {
            if (!IsExpanded()) {
                return;
            }
            // Vertical wheel → horizontal pan while expanded (vertical bar is disabled).
            double delta = Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y) ? e.Delta.X : e.Delta.Y;
            double max = Math.Max(0, TileScroller.Extent.Width - TileScroller.Viewport.Width);
            if (max <= 0) {
                return;
            }
            TileScroller.Offset = new Vector(
                Math.Clamp(TileScroller.Offset.X - delta * WheelStep, 0, max),
                0);
            e.Handled = true;
        }

        void ToggleExpandClicked(object? sender, RoutedEventArgs e) {
            if (DataContext is SingerFlyoutViewModel viewModel) {
                viewModel.ToggleExpandedGrid();
                if (fitAnchor != null) {
                    FitToScreen(fitAnchor);
                } else {
                    ApplyLayoutMode(force: true);
                    UpdateSize();
                }
                e.Handled = true;
            }
        }

        void TypeChipClicked(object? sender, RoutedEventArgs e) {
            if (sender is Control { DataContext: SingerTypeChipViewModel chip } &&
                DataContext is SingerFlyoutViewModel viewModel) {
                viewModel.ToggleTypeFilter(chip.Type);
                e.Handled = true;
            }
        }

        void TilePressed(object? sender, PointerPressedEventArgs e) {
            if (sender is Control tile && e.GetCurrentPoint(tile).Properties.IsLeftButtonPressed) {
                pressedTile = tile;
                e.Pointer.Capture(tile);
                e.Handled = true;
            }
        }

        void TileReleased(object? sender, PointerReleasedEventArgs e) {
            if (sender is not Control tile || tile != pressedTile || e.InitialPressMouseButton != MouseButton.Left) {
                return;
            }
            pressedTile = null;
            e.Pointer.Capture(null);
            e.Handled = true;
            if (new Rect(tile.Bounds.Size).Contains(e.GetPosition(tile)) &&
                tile.DataContext is SingerTileViewModel tileViewModel &&
                DataContext is SingerFlyoutViewModel viewModel) {
                var cmdKey = OS.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
                bool applyToAll = (e.KeyModifiers & cmdKey) == cmdKey;
                viewModel.Select(tileViewModel, applyToAll);
            }
        }

        void FavStarPressed(object? sender, PointerPressedEventArgs e) {
            if (sender is Control { DataContext: SingerTileViewModel tile } star &&
                e.GetCurrentPoint(star).Properties.IsLeftButtonPressed) {
                tile.IsFavourite = !tile.IsFavourite;
                e.Handled = true;
            }
        }

        void TileContextRequested(object? sender, ContextRequestedEventArgs e) {
            e.Handled = true;
            if (sender is not Control { DataContext: SingerTileViewModel tile } control ||
                DataContext is not SingerFlyoutViewModel viewModel || tile.IsMissing) {
                return;
            }
            MenuItem Item(string header, Action onClick) {
                var item = new MenuItem() { Header = this.FindResource(header) };
                item.Click += (_, _) => onClick();
                return item;
            }
            var items = new List<MenuItem> {
                Item("tracks.openlocation", () => viewModel.OpenLocation(tile)),
                Item("tracks.searchterms.edit", () => viewModel.EditSearchTerms(tile)),
            };
            if (viewModel.IsRecent(tile)) {
                items.Add(Item("tracks.removefromrecent", () => viewModel.RemoveFromRecent(tile)));
            }
            var menu = new ContextMenu() { ItemsSource = items };
            menu.Open(control);
        }
    }
}
