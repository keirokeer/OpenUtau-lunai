using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;

namespace OpenUtau.App.Controls {
  /// <summary>Text field for workspace panels; keeps canvas background and applies disabled chrome over Fluent.</summary>
  public class WorkspacePanelTextBox : TextBox {
    protected override Type StyleKeyOverride => typeof(TextBox);

    Border? border;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e) {
      base.OnApplyTemplate(e);
      border = e.NameScope.Find<Border>("PART_BorderElement");
      ApplyChrome();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
      base.OnPropertyChanged(change);
      if (change.Property == IsEnabledProperty) {
        ApplyChrome();
      }
    }

    protected override void OnGotFocus(FocusChangedEventArgs e) {
      base.OnGotFocus(e);
      ApplyChrome();
    }

    protected override void OnLostFocus(FocusChangedEventArgs e) {
      base.OnLostFocus(e);
      ApplyChrome();
    }

    void ApplyChrome() {
      if (border == null) {
        return;
      }

      var canvas = GetThemeBrush("WorkspaceCanvasBrush");
      border.SetValue(Border.BackgroundProperty, canvas);

      if (!IsEnabled) {
        SetValue(ForegroundProperty, GetThemeBrush("TextControlForegroundDisabled"));
        SetValue(OpacityProperty, 1d);
        border.SetValue(Border.BorderBrushProperty, GetThemeBrush("TextControlBorderBrushDisabled", "TextControlForegroundDisabled"));
        return;
      }

      ClearValue(ForegroundProperty);
      ClearValue(OpacityProperty);
      border.ClearValue(Border.BorderBrushProperty);
    }

    IBrush GetThemeBrush(string key, string? fallbackKey = null) {
      var theme = ActualThemeVariant ?? ThemeVariant.Default;
      if (Application.Current?.TryFindResource(key, theme, out var resource) == true
          && resource is IBrush brush) {
        return brush;
      }

      if (fallbackKey != null
          && Application.Current?.TryFindResource(fallbackKey, theme, out resource) == true
          && resource is IBrush fallback) {
        return fallback;
      }

      return Brushes.Transparent;
    }
  }
}
