using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenUtau.App;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using ReactiveUI;

namespace OpenUtau.App.Controls {
    public partial class LyricBox : UserControl {
        private LyricBoxViewModel viewModel;
        private TextBox box;
        private TextBox? blendBox;
        private ListBox listBox;
        private DispatcherTimer? focusTimer;
        private int scrollStyleApplyGeneration;

        public LyricBox() {
            InitializeComponent();
            DataContext = viewModel = new LyricBoxViewModel();
            box = PART_Box;
            blendBox = PART_BlendBox;
            listBox = PART_Suggestions;
            IsVisible = false;
            AttachedToVisualTree += (_, _) => ScheduleApplyScrollStyle();
            Loaded += (_, _) => ScheduleApplyScrollStyle();
            DetachedFromVisualTree += (_, _) => scrollStyleApplyGeneration++;
            MessageBus.Current.Listen<ScrollbarsStyleChangedEvent>()
                .Subscribe(_ => ScheduleApplyScrollStyle());
        }

        void ScheduleApplyScrollStyle() {
            if (!WorkspaceScrollbarHelper.IsInVisualTree(this)) {
                return;
            }
            int generation = ++scrollStyleApplyGeneration;
            Dispatcher.UIThread.Post(() => {
                if (generation != scrollStyleApplyGeneration || !WorkspaceScrollbarHelper.IsInVisualTree(this)) {
                    return;
                }
                ApplyScrollStyle();
            }, DispatcherPriority.Loaded);
        }

        void ApplyScrollStyle() {
            if (!WorkspaceScrollbarHelper.IsInVisualTree(this)) {
                return;
            }
            var scrollViewer = listBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            if (scrollViewer == null) {
                return;
            }
            scrollViewer.Classes.Add("workspaceScroll");
            scrollViewer.AllowAutoHide = false;
            WorkspaceScrollbarHelper.ApplyScrollViewer(scrollViewer, WorkspaceScrollbarHelper.UseClassicScrollbars);
        }

        private void Box_GotFocus(object? sender, GotFocusEventArgs e) {
            viewModel.SuggestionFromBlend = false;
            box.SelectAll();
        }

        private void Box_LostFocus(object? sender, RoutedEventArgs e) {
            box.CaretIndex = 0;
        }

        private void BlendBox_GotFocus(object? sender, GotFocusEventArgs e) {
            viewModel.SuggestionFromBlend = true;
            if (blendBox != null) {
                blendBox.SelectAll();
            }
        }

        private void BlendBox_LostFocus(object? sender, RoutedEventArgs e) {
            if (blendBox != null) {
                blendBox.CaretIndex = 0;
            }
        }

        private void ListBox_KeyDown(object? sender, KeyEventArgs e) {
            switch (e.Key) {
                case Key.Enter:
                    if (listBox.SelectedItem is LyricBoxViewModel.SuggestionItem item) {
                        viewModel.ApplySuggestion(item.Alias);
                    }
                    EndEdit(true);
                    e.Handled = true;
                    break;
                case Key.Escape:
                    EndEdit(false);
                    e.Handled = true;
                    break;
                case Key.Tab:
                    if (!viewModel.IsAliasBox) {
                        if (listBox.SelectedItem is LyricBoxViewModel.SuggestionItem item1) {
                            viewModel.ApplySuggestion(item1.Alias);
                        }
                        OnTab(e.KeyModifiers);
                    }
                    e.Handled = true;
                    break;
                case Key.Up:
                    ListBoxSelect(listBox.SelectedIndex - 1);
                    e.Handled = true;
                    break;
                case Key.Down:
                    ListBoxSelect(listBox.SelectedIndex + 1);
                    e.Handled = true;
                    break;
                case Key.PageUp:
                    ListBoxSelect(listBox.SelectedIndex - 8);
                    e.Handled = true;
                    break;
                case Key.PageDown:
                    ListBoxSelect(listBox.SelectedIndex + 8);
                    e.Handled = true;
                    break;
                default:
                    break;
            }
        }

        private void ListBoxSelect(int index) {
            if (index < 0) {
                if (listBox.SelectedIndex == 0) {
                    index = listBox.ItemCount - 1;
                } else {
                    index = 0;
                }
            } else if (index >= listBox.ItemCount) {
                if (listBox.SelectedIndex == listBox.ItemCount - 1) {
                    index = 0;
                } else {
                    index = listBox.ItemCount - 1;
                }
            }
            listBox.SelectedIndex = index;
        }

        private void Box_KeyDown(object? sender, KeyEventArgs e) {
            switch (e.Key) {
                case Key.Enter:
                    EndEdit(true);
                    e.Handled = true;
                    break;
                case Key.Escape:
                    EndEdit(false);
                    e.Handled = true;
                    break;
                case Key.Tab:
                    if (!viewModel.IsAliasBox) {
                        OnTab(e.KeyModifiers);
                    }
                    e.Handled = true;
                    break;
                case Key.Up:
                case Key.Down:
                case Key.PageUp:
                case Key.PageDown:
                    listBox.Focus();
                    listBox.SelectedIndex = 0;
                    e.Handled = true;
                    break;
                case Key.Left:
                    if (sender is TextBox tbLeft && tbLeft.SelectionStart < tbLeft.SelectionEnd)
                        tbLeft.SelectionEnd = tbLeft.SelectionStart;
                    break;
                case Key.Right:
                    if (sender is TextBox tbRight && tbRight.SelectionStart > tbRight.SelectionEnd)
                        tbRight.SelectionEnd = tbRight.SelectionStart;
                    break;
                default:
                    break;
            }
        }

        private void Save_Click(object? sender, RoutedEventArgs e) {
            EndEdit(true);
        }

        private void OnTab(KeyModifiers keyModifiers) {
            UVoicePart? part = viewModel.Part;
            UNote? tabTo = null;
            var tabFrom = viewModel.NoteOrPhoneme as LyricBoxNote;
            if (keyModifiers == KeyModifiers.None) {
                tabTo = tabFrom?.Unwrap().Next;
            } else if (keyModifiers == KeyModifiers.Shift) {
                tabTo = tabFrom?.Unwrap().Prev;
            }
            EndEdit(true);
            if (tabTo != null && part != null) {
                DocManager.Inst.ExecuteCmd(new FocusNoteNotification(part, tabTo));
                Show(part, new LyricBoxNote(tabTo), tabTo.lyric);
            }
        }

        public void ListBox_PointerPressed(object sender, PointerPressedEventArgs args) {
            if (sender is Control { DataContext: LyricBoxViewModel.SuggestionItem item }) {
                viewModel.ApplySuggestion(item.Alias);
            }
            EndEdit(true);
        }

        public void Show(UVoicePart part, LyricBoxNoteOrPhoneme noteOrPhoneme, string text) {
            Show(part, noteOrPhoneme, text, false, false, null);
        }

        /// <param name="editTagOnly">When true, text is tag only; commit builds full = text + "/" + otherPart.</param>
        /// <param name="editPhonemeOnly">When true, text is phoneme only; commit builds full = (otherPart + "/") + text.</param>
        /// <param name="otherPart">The other part (phoneme-only when editTagOnly, tag when editPhonemeOnly).</param>
        public void Show(UVoicePart part, LyricBoxNoteOrPhoneme noteOrPhoneme, string text, bool editTagOnly, bool editPhonemeOnly, string? otherPart) {
            viewModel.Part = part;
            viewModel.NoteOrPhoneme = noteOrPhoneme;
            viewModel.Text = text;
            viewModel.EditPhonemeTagOnly = editTagOnly;
            viewModel.EditPhonemePhonemeOnly = editPhonemeOnly;
            viewModel.PhonemeOtherPart = otherPart ?? string.Empty;
            viewModel.SuggestionFromBlend = false;
            if (noteOrPhoneme is LyricBoxPhoneme lyricPhoneme) {
                var ph = lyricPhoneme.Unwrap();
                viewModel.BlendText = ph.blendPhoneme ?? string.Empty;
                viewModel.BlendWeight = Math.Clamp(ph.blendWeight, 0, 100);
            } else {
                viewModel.BlendText = string.Empty;
                viewModel.BlendWeight = 0;
            }
            viewModel.IsVisible = true;
            box.SelectAll();
            ScheduleApplyScrollStyle();
            focusTimer = new DispatcherTimer(
                TimeSpan.FromMilliseconds(15),
                DispatcherPriority.Normal,
                FocusTimer_Tick);
            focusTimer.Start();
        }

        private void FocusTimer_Tick(object? sender, System.EventArgs e) {
            box.Focus();
            if (focusTimer != null) {
                focusTimer.Tick -= FocusTimer_Tick;
                focusTimer.Stop();
                focusTimer = null;
            }
        }

        /// <summary>
        /// Closes the lyric/phoneme editor.
        /// Pass true to commit (Save / Enter). Default discards (click outside / Escape).
        /// </summary>
        public void EndEdit(bool commit = false) {
            if (!viewModel.IsVisible) {
                return;
            }
            if (commit) {
                // Sync TextBox text before commit in case binding hasn't flushed yet.
                viewModel.Text = box.Text;
                if (blendBox != null) {
                    viewModel.BlendText = blendBox.Text;
                }
                viewModel.Commit();
            }
            viewModel.Part = null;
            viewModel.NoteOrPhoneme = null;
            viewModel.IsVisible = false;
            viewModel.Text = string.Empty;
            viewModel.BlendText = string.Empty;
            viewModel.BlendWeight = 0;
            viewModel.SuggestionFromBlend = false;
            viewModel.EditPhonemeTagOnly = false;
            viewModel.EditPhonemePhonemeOnly = false;
            viewModel.PhonemeOtherPart = null;
            this.Focus();
        }
    }
}
