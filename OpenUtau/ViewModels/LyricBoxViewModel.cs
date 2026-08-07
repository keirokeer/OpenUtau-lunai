using System;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using DynamicData.Binding;
using OpenUtau.Core;
using OpenUtau.Core.DiffSinger;
using OpenUtau.Core.Ustx;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtau.App.ViewModels {
    class LyricBoxViewModel : ViewModelBase {
        public class SuggestionItem {
            public string Alias { get; set; } = string.Empty;
            public string Source { get; set; } = string.Empty;
        }

        [Reactive] public UVoicePart? Part { get; set; }
        [Reactive] public LyricBoxNoteOrPhoneme? NoteOrPhoneme { get; set; }
        [Reactive] public bool IsVisible { get; set; }
        [Reactive] public string? Text { get; set; }
        /// <summary>When editing phoneme: true = editing only tag; commit builds full = Text + "/" + PhonemeOtherPart.</summary>
        [Reactive] public bool EditPhonemeTagOnly { get; set; }
        /// <summary>When editing phoneme: true = editing only phoneme part; commit builds full = (PhonemeOtherPart + "/") + Text.</summary>
        [Reactive] public bool EditPhonemePhonemeOnly { get; set; }
        /// <summary>The other part (phoneme-only when EditPhonemeTagOnly, tag when EditPhonemePhonemeOnly).</summary>
        [Reactive] public string? PhonemeOtherPart { get; set; }
        [Reactive] public string? BlendText { get; set; }
        [Reactive] public int BlendWeight { get; set; }
        /// <summary>When true, suggestion list updates/applies to the blend phoneme field.</summary>
        [Reactive] public bool SuggestionFromBlend { get; set; }
        [Reactive] public SuggestionItem? SelectedSuggestion { get; set; }
        [Reactive] public ObservableCollectionExtended<SuggestionItem> Suggestions { get; set; }

        public bool IsAliasBox => isAliasBox.Value;
        private readonly ObservableAsPropertyHelper<bool> isAliasBox;

        public bool ShowPhonemeBlend => showPhonemeBlend.Value;
        private readonly ObservableAsPropertyHelper<bool> showPhonemeBlend;

        /// <summary>Phoneme edit on a DiffSinger track — tip says Override Phoneme, not Alias.</summary>
        public bool IsDiffSingerPhonemeBox => isDiffSingerPhonemeBox.Value;
        private readonly ObservableAsPropertyHelper<bool> isDiffSingerPhonemeBox;

        public bool ShowAliasOverrideTip => IsAliasBox && !IsDiffSingerPhonemeBox;
        public bool ShowPhonemeOverrideTip => IsAliasBox && IsDiffSingerPhonemeBox;

        public LyricBoxViewModel() {
            Text = string.Empty;
            BlendText = string.Empty;
            Suggestions = new ObservableCollectionExtended<SuggestionItem>();

            this.WhenAnyValue(x => x.Text, x => x.BlendText, x => x.SuggestionFromBlend, x => x.IsVisible)
                .Subscribe(_ => UpdateSuggestion());
            this.WhenAnyValue(x => x.SelectedSuggestion)
                .WhereNotNull()
                .Subscribe(ss => Serilog.Log.Information(ss.Alias));

            isAliasBox = this.WhenAnyValue(x => x.NoteOrPhoneme)
                .Select(v => v is LyricBoxPhoneme)
                .ToProperty(this, x => x.IsAliasBox);

            showPhonemeBlend = this.WhenAnyValue(x => x.Part, x => x.NoteOrPhoneme, x => x.IsVisible)
                .Select(_ => ComputeShowPhonemeBlend())
                .ToProperty(this, x => x.ShowPhonemeBlend);

            isDiffSingerPhonemeBox = this.WhenAnyValue(x => x.Part, x => x.NoteOrPhoneme, x => x.IsVisible)
                .Select(_ => ComputeIsDiffSingerPhonemeBox())
                .ToProperty(this, x => x.IsDiffSingerPhonemeBox);

            this.WhenAnyValue(x => x.IsAliasBox, x => x.IsDiffSingerPhonemeBox)
                .Subscribe(_ => {
                    this.RaisePropertyChanged(nameof(ShowAliasOverrideTip));
                    this.RaisePropertyChanged(nameof(ShowPhonemeOverrideTip));
                });
        }

        USinger? CurrentSinger() {
            if (Part == null || Part.trackNo < 0 || Part.trackNo >= DocManager.Inst.Project.tracks.Count) {
                return null;
            }
            return DocManager.Inst.Project.tracks[Part.trackNo].Singer;
        }

        bool ComputeIsDiffSingerPhonemeBox() {
            if (!IsVisible || NoteOrPhoneme is not LyricBoxPhoneme) {
                return false;
            }
            return CurrentSinger()?.SingerType == USingerType.DiffSinger;
        }

        bool ComputeShowPhonemeBlend() {
            if (!IsVisible || Part == null || NoteOrPhoneme is not LyricBoxPhoneme) {
                return false;
            }
            return DiffSingerPhonemeBlend.Supports(CurrentSinger());
        }

        private void UpdateSuggestion() {
            if (Part == null || NoteOrPhoneme == null) {
                Suggestions.Clear();
                return;
            }
            var singer = CurrentSinger();
            if (singer == null || !singer.Found || !singer.Loaded) {
                Suggestions.Clear();
                Suggestions.Add(new SuggestionItem() {
                    Alias = "No Singer",
                });
                return;
            }
            string query = SuggestionFromBlend ? (BlendText ?? "") : (Text ?? "");
            bool isPhonemeEdit = NoteOrPhoneme is LyricBoxPhoneme;
            var scheduler = TaskScheduler.FromCurrentSynchronizationContext();
            Task.Run(() => singer.GetSuggestions(query).Select(oto => new SuggestionItem() {
                Alias = oto.Alias,
                Source = string.IsNullOrEmpty(oto.Set) ? singer.Id : $"{oto.Set}",
            }).Take(32).ToList()).ContinueWith(task => {
                Suggestions.Clear();
                // Lyrics helpers (romaji→hiragana etc.) are for note lyrics, not phoneme/alias edit.
                if (!isPhonemeEdit
                    && !SuggestionFromBlend
                    && !string.IsNullOrEmpty(Text)
                    && Core.Util.ActiveLyricsHelper.Inst.Current != null) {
                    string text = Core.Util.ActiveLyricsHelper.Inst.Current.Convert(Text);
                    if (Core.Util.Preferences.Default.LyricsHelperBrackets) {
                        text = $"[{text}]";
                    }
                    Suggestions.Add(new SuggestionItem() {
                        Alias = text,
                        Source = Core.Util.ActiveLyricsHelper.Inst.Current.Source,
                    });
                }
                if (!task.IsFaulted) {
                    Suggestions.AddRange(task.Result);
                }
            }, scheduler);
        }

        public void ApplySuggestion(string alias) {
            if (SuggestionFromBlend) {
                BlendText = alias;
            } else {
                Text = alias;
            }
        }

        public void Commit() {
            if (Part == null || NoteOrPhoneme == null || Text == null) {
                return;
            }
            if (!IsAliasBox) {
                var note = NoteOrPhoneme as LyricBoxNote;
                if (Text == note!.Unwrap().lyric) {
                    return;
                }
                DocManager.Inst.StartUndoGroup("command.note.lyric");
                DocManager.Inst.ExecuteCmd(new ChangeNoteLyricCommand(Part, (NoteOrPhoneme as LyricBoxNote)!.Unwrap(), Text));
                DocManager.Inst.EndUndoGroup();
                return;
            }

            var phoneme = (NoteOrPhoneme as LyricBoxPhoneme)!.Unwrap();
            var currentPhoneme = phoneme.phoneme;
            string textToApply = Text!;
            if (EditPhonemeTagOnly && !string.IsNullOrEmpty(PhonemeOtherPart)) {
                textToApply = Text + "/" + PhonemeOtherPart;
            } else if (EditPhonemePhonemeOnly) {
                textToApply = string.IsNullOrEmpty(PhonemeOtherPart) ? Text : (PhonemeOtherPart + "/" + Text);
            }
            bool aliasChanged = textToApply != currentPhoneme;
            bool showBlend = ComputeShowPhonemeBlend();
            string? blendToApply = string.IsNullOrWhiteSpace(BlendText) ? null : BlendText.Trim();
            int? weightToApply = showBlend ? Math.Clamp(BlendWeight, 0, 100) : null;
            if (weightToApply == 0) {
                weightToApply = null;
            }
            if (blendToApply == null) {
                weightToApply = null;
            }
            string? currentBlend = string.IsNullOrWhiteSpace(phoneme.blendPhoneme) ? null : phoneme.blendPhoneme.Trim();
            int? currentWeight = phoneme.blendWeight > 0 ? phoneme.blendWeight : null;
            bool blendChanged = showBlend && (
                !string.Equals(blendToApply, currentBlend, StringComparison.Ordinal)
                || weightToApply != currentWeight);

            if (!aliasChanged && !blendChanged) {
                return;
            }

            Text = textToApply;
            var noteForCmd = phoneme.Parent.Extends ?? phoneme.Parent;
            int index = phoneme.index;
            DocManager.Inst.StartUndoGroup("command.phoneme.edit");
            if (aliasChanged) {
                DocManager.Inst.ExecuteCmd(new ChangePhonemeAliasCommand(Part, noteForCmd, index, Text!));
            }
            if (showBlend && blendChanged) {
                DocManager.Inst.ExecuteCmd(new ChangePhonemeBlendCommand(
                    Part, noteForCmd, index, blendToApply, weightToApply));
            }
            DocManager.Inst.EndUndoGroup();
        }
    }

    public abstract class LyricBoxNoteOrPhoneme { }
    public class LyricBoxNote : LyricBoxNoteOrPhoneme {
        public UNote note;
        public LyricBoxNote(UNote note) { this.note = note; }
        public UNote Unwrap() => note;
    }
    public class LyricBoxPhoneme : LyricBoxNoteOrPhoneme {
        public UPhoneme phoneme;
        public LyricBoxPhoneme(UPhoneme phoneme) { this.phoneme = phoneme; }
        public UPhoneme Unwrap() => phoneme;
    }
}
