using System.Collections.ObjectModel;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtau.App.ViewModels;

public class ExpressionStyleValueItem : ViewModelBase {
    public string Abbr { get; }
    public string Name { get; }
    public double Min { get; }
    public double Max { get; }
    public double FactoryValue { get; }
    public bool IsOptions { get; }
    public ObservableCollection<string> Options { get; } = new();
    [Reactive] public double Value { get; set; }
    [Reactive] public int SelectedOptionIndex { get; set; }

    public ExpressionStyleValueItem(UExpressionDescriptor descriptor, float value, string[]? options = null) {
        Abbr = descriptor.abbr;
        Name = ExpressionSuggestionSync.GetPanelDisplayName(descriptor);
        Min = descriptor.min;
        Max = descriptor.max;
        FactoryValue = descriptor.max >= descriptor.min
            ? System.Math.Clamp(descriptor.defaultValue, descriptor.min, descriptor.max)
            : descriptor.defaultValue;
        Value = value;
        if (options != null && options.Length > 0) {
            IsOptions = true;
            foreach (var option in options) {
                Options.Add(option);
            }
            SelectedOptionIndex = (int)System.Math.Clamp(
                System.Math.Round(value), 0, options.Length - 1);
        }
    }

    public float EffectiveValue =>
        IsOptions ? SelectedOptionIndex : (float)Value;

    public void ResetToFactory() {
        if (IsOptions) {
            int maxIndex = System.Math.Max(0, Options.Count - 1);
            SelectedOptionIndex = (int)System.Math.Clamp(System.Math.Round(FactoryValue), 0, maxIndex);
            return;
        }
        Value = FactoryValue;
    }
}

public class SaveExpressionStyleViewModel : ViewModelBase {
    public ObservableCollection<ExpressionStyleValueItem> Items { get; } = new();

    [Reactive] public string StyleName { get; set; } = string.Empty;
    [Reactive] public string SingerName { get; set; } = string.Empty;
    [Reactive] public string ErrorMessage { get; set; } = string.Empty;
    public bool HasSingerName => !string.IsNullOrWhiteSpace(SingerName);
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public void LoadFromPanel(
        string suggestedName,
        string singerName,
        System.Collections.Generic.IEnumerable<(string Abbr, string Name, float Value)> parameters,
        System.Collections.Generic.IEnumerable<(string Abbr, string Name, float Value)> voiceColors,
        UExpressionDescriptor? clrDescriptor,
        int selectedVoiceColorIndex,
        string[]? voiceColorOptions) {
        StyleName = suggestedName ?? string.Empty;
        SingerName = singerName ?? string.Empty;
        ErrorMessage = string.Empty;
        this.RaisePropertyChanged(nameof(HasSingerName));
        this.RaisePropertyChanged(nameof(HasError));
        Items.Clear();

        foreach (var item in parameters) {
            if (!DocManagerHasDescriptor(item.Abbr, out var descriptor)) {
                continue;
            }
            Items.Add(new ExpressionStyleValueItem(descriptor!, item.Value));
        }
        if (clrDescriptor != null && voiceColorOptions != null && voiceColorOptions.Length > 0) {
            Items.Add(new ExpressionStyleValueItem(
                clrDescriptor, selectedVoiceColorIndex, voiceColorOptions));
        }
        foreach (var item in voiceColors) {
            if (!DocManagerHasDescriptor(item.Abbr, out var descriptor)) {
                continue;
            }
            Items.Add(new ExpressionStyleValueItem(descriptor!, item.Value));
        }
    }

    static bool DocManagerHasDescriptor(string abbr, out UExpressionDescriptor? descriptor) {
        return Core.DocManager.Inst.Project.expressions.TryGetValue(abbr, out descriptor);
    }

    public void SetError(string messageKey) {
        var localized = ThemeManager.GetString(messageKey);
        ErrorMessage = localized == messageKey ? messageKey : localized;
        this.RaisePropertyChanged(nameof(HasError));
    }

    public void ClearError() {
        ErrorMessage = string.Empty;
        this.RaisePropertyChanged(nameof(HasError));
    }

    public ExpressionStyleYaml? BuildStyle() {
        ClearError();
        var name = (StyleName ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(name)) {
            SetError("workspace.panel.expressions.styles.empty");
            return null;
        }
        if (Items.Count == 0) {
            SetError("workspace.panel.expressions.styles.novalues");
            return null;
        }
        var style = new ExpressionStyleYaml {
            Name = name,
            SingerName = (SingerName ?? string.Empty).Trim(),
            Values = new System.Collections.Generic.Dictionary<string, float>(),
        };
        foreach (var item in Items) {
            float value = item.EffectiveValue;
            if (item.Max >= item.Min) {
                value = (float)System.Math.Clamp(value, item.Min, item.Max);
            }
            style.Values[item.Abbr] = value;
        }
        return style;
    }
}
