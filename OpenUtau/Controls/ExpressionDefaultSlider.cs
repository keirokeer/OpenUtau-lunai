using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenUtau.App.ViewModels;
using ReactiveUI;

namespace OpenUtau.App.Controls {
        /// <summary>
        /// Slider for project/track expression default with a white track highlight for playhead deviation.
        /// Track layers (bottom → top): dim track → accent fill → white deviation → thumb.
        /// </summary>
        public class ExpressionDefaultSlider : UserControl {
        public static readonly StyledProperty<ExpressionDefaultItem?> ItemProperty =
            AvaloniaProperty.Register<ExpressionDefaultSlider, ExpressionDefaultItem?>(nameof(Item));

        public ExpressionDefaultItem? Item {
            get => GetValue(ItemProperty);
            set => SetValue(ItemProperty, value);
        }

        readonly Slider slider;
        readonly Border trackBg;
        readonly Border deviationBand;
        readonly Border accentFill;
        bool editing;
        bool suppressSync;
        IDisposable? itemSub;

        const double TrackHeight = 2;
        const double HorizontalInset = 8;

        public ExpressionDefaultSlider() {
            trackBg = new Border {
                Height = TrackHeight,
                CornerRadius = new CornerRadius(TrackHeight / 2),
                IsHitTestVisible = false,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(HorizontalInset, 0),
                ZIndex = 0,
            };
            accentFill = new Border {
                Height = TrackHeight,
                CornerRadius = new CornerRadius(TrackHeight / 2),
                IsHitTestVisible = false,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                IsVisible = false,
                ZIndex = 1,
            };
            deviationBand = new Border {
                Height = TrackHeight,
                CornerRadius = new CornerRadius(TrackHeight / 2),
                Background = Brushes.White,
                IsHitTestVisible = false,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                IsVisible = false,
                ZIndex = 2,
            };
            slider = new Slider {
                Classes = { "fader", "expDefaultTrack" },
                Focusable = true,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center,
                MinHeight = 24,
                Background = Brushes.Transparent,
                ZIndex = 3,
            };
            var root = new Grid();
            root.Children.Add(trackBg);
            root.Children.Add(accentFill);
            root.Children.Add(deviationBand);
            root.Children.Add(slider);
            Content = root;

            slider.PropertyChanged += OnSliderPropertyChanged;
            AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
            AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
            AddHandler(PointerCaptureLostEvent, OnCaptureLost, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
            SizeChanged += (_, _) => UpdateBands();
            AttachedToVisualTree += (_, _) => UpdateBands();
            ToolTip.SetTip(this, ThemeManager.GetString("workspace.panel.expressions.resettooltip"));
            DataContextChanged += (_, _) => SyncResetTooltip();
        }

        void SyncResetTooltip() {
            var vm = FindViewModel();
            if (vm != null && !string.IsNullOrEmpty(vm.ResetTooltip)) {
                ToolTip.SetTip(this, vm.ResetTooltip);
            }
        }

        ExpressionDefaultsViewModel? FindViewModel() {
            return this.GetVisualAncestors()
                    .Select(v => (v as StyledElement)?.DataContext)
                    .OfType<ExpressionDefaultsViewModel>()
                    .FirstOrDefault()
                ?? DataContext as ExpressionDefaultsViewModel;
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
            base.OnPropertyChanged(change);
            if (change.Property == ItemProperty) {
                BindItem(change.GetNewValue<ExpressionDefaultItem?>());
            }
        }

        void BindItem(ExpressionDefaultItem? item) {
            itemSub?.Dispose();
            itemSub = null;
            if (item == null) {
                deviationBand.IsVisible = false;
                accentFill.IsVisible = false;
                return;
            }
            suppressSync = true;
            slider.Minimum = item.Min;
            slider.Maximum = item.Max;
            slider.Value = item.DefaultValue;
            suppressSync = false;
            itemSub = item.WhenAnyValue(
                    x => x.DefaultValue,
                    x => x.Min,
                    x => x.Max,
                    x => x.PlayheadValue,
                    x => x.ShowPlayheadMarker,
                    x => x.HasTrackOverride)
                .Subscribe(_ => Dispatcher.UIThread.Post(ApplyItemToSlider));
            SyncResetTooltip();
            ApplyItemToSlider();
        }

        void ApplyItemToSlider() {
            var item = Item;
            if (item == null || editing) {
                UpdateBands();
                return;
            }
            suppressSync = true;
            if (Math.Abs(slider.Minimum - item.Min) > 0.0001 || Math.Abs(slider.Maximum - item.Max) > 0.0001) {
                slider.Minimum = item.Min;
                slider.Maximum = item.Max;
            }
            if (Math.Abs(slider.Value - item.DefaultValue) > 0.0001) {
                slider.Value = item.DefaultValue;
            }
            suppressSync = false;
            UpdateBands();
        }

        void OnSliderPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e) {
            if (e.Property == Avalonia.Controls.Slider.ForegroundProperty) {
                UpdateBands();
                return;
            }
            if (suppressSync || e.Property != RangeBase.ValueProperty || Item == null) {
                return;
            }
            float value = (float)slider.Value;
            var vm = FindViewModel();
            if (!editing) {
                editing = true;
                vm?.BeginEdit(Item);
            }
            if (vm != null) {
                vm.PreviewEdit(Item, value);
            } else {
                Item.DefaultValue = value;
            }
            UpdateBands();
        }

        void OnPointerPressed(object? sender, PointerPressedEventArgs e) {
            if (Item == null) {
                return;
            }
            if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed) {
                editing = false;
                FindViewModel()?.ResetSliderDefault(Item);
                e.Handled = true;
                return;
            }
            editing = true;
            FindViewModel()?.BeginEdit(Item);
        }

        void OnPointerReleased(object? sender, PointerReleasedEventArgs e) {
            FinishEdit();
        }

        void OnCaptureLost(object? sender, PointerCaptureLostEventArgs e) {
            FinishEdit();
        }

        void FinishEdit() {
            if (!editing || Item == null) {
                editing = false;
                return;
            }
            editing = false;
            var vm = FindViewModel();
            if (vm != null) {
                vm.PreviewEdit(Item, (float)slider.Value);
                vm.EndEdit(Item);
            }
            UpdateBands();
        }

        IBrush ResolveAccentBrush() {
            if (slider.Foreground != null && !Equals(slider.Foreground, Brushes.Transparent)) {
                return slider.Foreground;
            }
            if (this.TryGetResource("SelectedTrackAccentBrush", ActualThemeVariant, out var a) && a is IBrush ab) {
                return ab;
            }
            if (this.TryGetResource("AccentBrush1", ActualThemeVariant, out var b) && b is IBrush bb) {
                return bb;
            }
            return Brushes.Gray;
        }

        IBrush ResolveTrackBgBrush() {
            if (this.TryGetResource("SliderTrackFill", ActualThemeVariant, out var a) && a is IBrush ab) {
                return ab;
            }
            if (this.TryGetResource("NeutralAccentBrushSemi", ActualThemeVariant, out var b) && b is IBrush bb) {
                return bb;
            }
            return new SolidColorBrush(Color.FromArgb(60, 128, 128, 128));
        }

        void UpdateBands() {
            var item = Item;
            if (item == null || Bounds.Width <= 0) {
                deviationBand.IsVisible = false;
                accentFill.IsVisible = false;
                return;
            }
            double range = item.Max - item.Min;
            if (range <= 0) {
                deviationBand.IsVisible = false;
                accentFill.IsVisible = false;
                return;
            }

            double trackWidth = Math.Max(0, Bounds.Width - HorizontalInset * 2);
            double tDefault = Math.Clamp((item.DefaultValue - item.Min) / range, 0, 1);

            trackBg.Background = ResolveTrackBgBrush();

            // White deviation sits above accent fill so leftward offsets stay visible.
            accentFill.Background = ResolveAccentBrush();
            accentFill.Width = Math.Max(0, trackWidth * tDefault);
            accentFill.Margin = new Thickness(HorizontalInset, 0, 0, 0);
            accentFill.IsVisible = accentFill.Width > 0.5;

            if (!item.ShowPlayheadMarker) {
                deviationBand.IsVisible = false;
                return;
            }

            double tPlayhead = Math.Clamp((item.PlayheadValue - item.Min) / range, 0, 1);
            double leftT = Math.Min(tDefault, tPlayhead);
            double rightT = Math.Max(tDefault, tPlayhead);
            deviationBand.Width = Math.Max(2, trackWidth * (rightT - leftT));
            deviationBand.Margin = new Thickness(HorizontalInset + trackWidth * leftT, 0, 0, 0);
            deviationBand.Background = Brushes.White;
            deviationBand.IsVisible = true;
        }
    }
}
