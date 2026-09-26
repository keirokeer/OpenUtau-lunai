using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using ReactiveUI;

namespace OpenUtau.App.Controls {
    public partial class SingerFlyout : UserControl {
        const double ListWidth = 260;
        const double TileHeight = 40;
        const double SearchHeight = 32;
        const double TypeFilterHeight = 28;
        const int MinVisibleRows = 4;
        // Flyout presenter padding/border + footer + search row + type filters.
        const double ChromeHeight = 50 + SearchHeight + TypeFilterHeight;

        private int maxRows = int.MaxValue;
        private IDisposable? itemsSubscription;
        private Control? pressedTile;

        public SingerFlyout() {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        void OnLoaded(object? sender, RoutedEventArgs e) {
            WorkspaceScrollbarHelper.ApplyScrollViewer(
                TileScroller, WorkspaceScrollbarHelper.UseClassicScrollbars);
            Dispatcher.UIThread.Post(() => SearchBox.Focus(), DispatcherPriority.Loaded);
        }

        protected override void OnDataContextChanged(EventArgs e) {
            base.OnDataContextChanged(e);
            itemsSubscription?.Dispose();
            itemsSubscription = null;
            if (DataContext is SingerFlyoutViewModel viewModel) {
                itemsSubscription = viewModel.WhenAnyValue(x => x.Items)
                    .Subscribe(_ => UpdateSize());
            }
        }

        /// <summary>
        /// Sizes the list to fit between the anchor and the nearer screen/window edge.
        /// Opens below the anchor when there is room, otherwise above.
        /// </summary>
        public void FitToScreen(Visual anchor) {
            maxRows = int.MaxValue;
            var topLevel = TopLevel.GetTopLevel(anchor);
            var screen = topLevel?.Screens?.ScreenFromVisual(anchor);
            var anchorTop = anchor.PointToScreen(new Point(0, 0));
            var anchorBottom = anchor.PointToScreen(new Point(0, anchor.Bounds.Height));
            if (screen != null) {
                var area = screen.WorkingArea;
                int RowsFor(double pixels) => (int)((pixels / screen.Scaling - ChromeHeight) / TileHeight);
                int below = RowsFor(area.Bottom - anchorBottom.Y);
                int above = RowsFor(anchorTop.Y - area.Y);
                maxRows = Math.Max(MinVisibleRows, below >= MinVisibleRows ? below : Math.Max(below, above));
            }
            UpdateSize();
            WorkspaceScrollbarHelper.ApplyScrollViewer(
                TileScroller, WorkspaceScrollbarHelper.UseClassicScrollbars);
        }

        void UpdateSize() {
            int count = CountVisibleRows();
            int rows = Math.Clamp(count, 1, Math.Max(1, maxRows));
            TileScroller.Height = rows * TileHeight;
            TileScroller.Width = ListWidth;
            TileScroller.Offset = new Vector(0, 0);
        }

        int CountVisibleRows() {
            if (DataContext is not SingerFlyoutViewModel viewModel) {
                return 0;
            }
            int n = 0;
            foreach (var item in viewModel.Items) {
                if (item is SingerTileViewModel || item is SingerFlyoutSeparatorViewModel) {
                    n++;
                }
            }
            return Math.Max(n, 1);
        }

        void TypeFilterClicked(object? sender, RoutedEventArgs e) {
            if (sender is ToggleButton { DataContext: SingerTypeFilterViewModel filter } button &&
                DataContext is SingerFlyoutViewModel viewModel) {
                viewModel.SelectTypeFilter(filter);
                button.IsChecked = filter.IsSelected;
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
    }
}
