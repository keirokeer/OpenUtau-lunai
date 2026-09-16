using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using OpenUtau.Core;

namespace OpenUtau.App.Controls {
    public partial class RenderProgressPanel : UserControl {
        public static readonly StyledProperty<double> ProgressProperty =
            AvaloniaProperty.Register<RenderProgressPanel, double>(nameof(Progress));

        public static readonly StyledProperty<string?> ProgressTextProperty =
            AvaloniaProperty.Register<RenderProgressPanel, string?>(nameof(ProgressText));

        public double Progress {
            get => GetValue(ProgressProperty);
            set => SetValue(ProgressProperty, value);
        }

        public string? ProgressText {
            get => GetValue(ProgressTextProperty);
            set => SetValue(ProgressTextProperty, value);
        }

        bool pointerOver;

        static RenderProgressPanel() {
            ProgressProperty.Changed.AddClassHandler<RenderProgressPanel>((panel, _) => {
                panel.UpdateFillLayout();
                panel.UpdateCancelVisibility();
            });
            ProgressTextProperty.Changed.AddClassHandler<RenderProgressPanel>((panel, _) =>
                panel.UpdateCancelVisibility());
        }

        public RenderProgressPanel() {
            InitializeComponent();
            IsHitTestVisible = false;
            HostGrid.SizeChanged += (_, _) => UpdateFillLayout();
            ProgressRow.SizeChanged += (_, _) => UpdateFillLayout();
            PointerEntered += (_, _) => {
                pointerOver = true;
                UpdateCancelVisibility();
            };
            PointerExited += (_, _) => {
                pointerOver = false;
                UpdateCancelVisibility();
            };
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnAttachedToVisualTree(e);
            UpdateFillLayout();
            UpdateCancelVisibility();
        }

        void OnCancelClick(object? sender, RoutedEventArgs e) {
            PlaybackManager.Inst.CancelActiveRender();
            e.Handled = true;
        }

        void UpdateCancelVisibility() {
            if (CancelButton == null) {
                return;
            }
            bool hasProgress = Progress > 0.01
                || !string.IsNullOrWhiteSpace(ProgressText);
            // Only capture pointer hits while rendering — otherwise this strip
            // blocks the bottom of docked panels (note params, etc.).
            IsHitTestVisible = hasProgress;
            bool show = pointerOver && hasProgress;
            CancelButton.Opacity = show ? 1 : 0;
            CancelButton.IsHitTestVisible = show;
        }

        void UpdateFillLayout() {
            if (FillClip == null || ProgressRow == null) {
                return;
            }
            double width = ProgressRow.Bounds.Width;
            if (width <= 0 || double.IsNaN(width)) {
                FillClip.Width = 0;
                return;
            }
            FillClip.Width = Math.Max(0, width * Math.Clamp(Progress, 0, 100) / 100.0);
        }
    }
}
