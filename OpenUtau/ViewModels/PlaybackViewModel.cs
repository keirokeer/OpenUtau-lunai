using System;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace OpenUtau.App.ViewModels {
    public partial class TimeAxisChangedEvent { }
    public partial class PlaybackViewModel : ViewModelBase, ICmdSubscriber {
        UProject Project => DocManager.Inst.Project;
        public int BeatPerBar => Project.timeSignatures[0].beatPerBar;
        public int BeatUnit => Project.timeSignatures[0].beatUnit;
        public double Bpm => Project.tempos[0].bpm;
        public int Key => Project.key;
        public bool KeyIsMajor => Project.keyIsMajor;
        public string KeyName => KeySignatureHelper.FormatProjectKey(Project);
        public int Resolution => Project.resolution;
        public int PlayPosTick => DocManager.Inst.playPosTick;
        public TimeSpan PlayPosTime => TimeSpan.FromMilliseconds((int)Project.timeAxis.TickPosToMsPos(DocManager.Inst.playPosTick));
        [Reactive] public partial bool MetronomeEnabled { get; set; } = Preferences.Default.MetronomeEnabled;

        bool playbackActiveLast;

        public bool IsPlaybackActive =>
            PlaybackManager.Inst.PlayingMaster || PlaybackManager.Inst.StartingToPlay;
        public bool HasRangeSelection => DocManager.Inst.rangeEndTick > DocManager.Inst.rangeStartTick;
        public bool IsPlaying => PlaybackManager.Inst.PlayingMaster;
        public bool ShowPlayPosHighlight => !IsPlaying || HasRangeSelection;

        public PlaybackViewModel() {
            playbackActiveLast = IsPlaybackActive;
            DocManager.Inst.AddSubscriber(this);

            this.WhenAnyValue(x => x.MetronomeEnabled)
             .Subscribe(metronomeEnabled => {
                 Preferences.Default.MetronomeEnabled = metronomeEnabled;
                 Preferences.Save();
                 PlaybackManager.Inst.PlayMetronome(MetronomeEnabled);
             });
        }

        public void SeekStart() {
            Pause();
            DocManager.Inst.ExecuteCmd(new SeekPlayPosTickNotification(0));
            NotifyPlaybackActiveChanged();
        }
        public void SeekEnd() {
            Pause();
            DocManager.Inst.ExecuteCmd(new SeekPlayPosTickNotification(Project.EndTick));
            NotifyPlaybackActiveChanged();
        }
        public void PlayOrPause(int tick = -1, int endTick = -1, int trackNo = -1) {
            bool wasPlaying = PlaybackManager.Inst.PlayingMaster;
            PlaybackManager.Inst.PlayOrPause(tick: tick, endTick: endTick, trackNo: trackNo);
            if (wasPlaying && !PlaybackManager.Inst.PlayingMaster && !PlaybackManager.Inst.StartingToPlay
                && Preferences.Default.LockStartTime != 0) {
                DocManager.Inst.ExecuteCmd(new SetPlayPosTickNotification(
                    PlaybackManager.Inst.PlaybackStartTick, pause: true));
            }
            NotifyPlaybackActiveChanged();
        }
        public void Pause() {
            bool wasPlaying = PlaybackManager.Inst.PlayingMaster;
            PlaybackManager.Inst.PausePlayback();
            if (wasPlaying && Preferences.Default.LockStartTime != 0) {
                DocManager.Inst.ExecuteCmd(new SetPlayPosTickNotification(
                    PlaybackManager.Inst.PlaybackStartTick, pause: true));
            }
            NotifyPlaybackActiveChanged();
        }

        public void PollPlaybackActiveChanged() {
            bool active = IsPlaybackActive;
            if (active == playbackActiveLast) {
                return;
            }
            playbackActiveLast = active;
            this.RaisePropertyChanged(nameof(IsPlaybackActive));
            this.RaisePropertyChanged(nameof(IsPlaying));
            this.RaisePropertyChanged(nameof(ShowPlayPosHighlight));
        }

        void NotifyPlaybackActiveChanged() {
            playbackActiveLast = IsPlaybackActive;
            this.RaisePropertyChanged(nameof(IsPlaybackActive));
            this.RaisePropertyChanged(nameof(IsPlaying));
            this.RaisePropertyChanged(nameof(ShowPlayPosHighlight));
        }

        public void MovePlayPos(int tick) {
            if (DocManager.Inst.playPosTick != tick) {
                DocManager.Inst.ExecuteCmd(new SeekPlayPosTickNotification(Math.Max(0, tick), pause: true));
            }
        }

        public void SetTimeSignature(int beatPerBar, int beatUnit) {
            if (beatPerBar > 1 && (beatUnit == 2 || beatUnit == 4 || beatUnit == 8 || beatUnit == 16)) {
                DocManager.Inst.StartUndoGroup("command.project.timesignature");
                DocManager.Inst.ExecuteCmd(new TimeSignatureCommand(Project, beatPerBar, beatUnit));
                DocManager.Inst.EndUndoGroup();
            }
        }

        public void SetBpm(double bpm) {
            if (bpm == DocManager.Inst.Project.tempos[0].bpm) {
                return;
            }
            DocManager.Inst.StartUndoGroup("command.project.tempo");
            DocManager.Inst.ExecuteCmd(new BpmCommand(Project, bpm));
            DocManager.Inst.EndUndoGroup();
        }

        public void SetKey(int key) => SetKeySignature(key, Project.keyIsMajor);

        public void SetKeySignature(int key, bool isMajor) {
            if (key == DocManager.Inst.Project.key && isMajor == DocManager.Inst.Project.keyIsMajor) {
                return;
            }
            DocManager.Inst.StartUndoGroup("command.project.key");
            DocManager.Inst.ExecuteCmd(new KeyCommand(Project, key, isMajor));
            DocManager.Inst.EndUndoGroup();
        }

        public void OnNext(UCommand cmd, bool isUndo) {
            if (cmd is BpmCommand ||
                cmd is TimeSignatureCommand ||
                cmd is AddTempoChangeCommand ||
                cmd is DelTempoChangeCommand ||
                cmd is AddTimeSigCommand ||
                cmd is DelTimeSigCommand ||
                cmd is KeyCommand ||
                cmd is LoadProjectNotification) {
                this.RaisePropertyChanged(nameof(BeatPerBar));
                this.RaisePropertyChanged(nameof(BeatUnit));
                this.RaisePropertyChanged(nameof(Bpm));
                this.RaisePropertyChanged(nameof(Key));
                this.RaisePropertyChanged(nameof(KeyName));
                this.RaisePropertyChanged(nameof(KeyIsMajor));
                MessageBus.Current.SendMessage(new TimeAxisChangedEvent());
                if (cmd is LoadProjectNotification) {
                    DocManager.Inst.ExecuteCmd(new SetPlayPosTickNotification(0));
                    this.RaisePropertyChanged(nameof(ShowPlayPosHighlight));
                    this.RaisePropertyChanged(nameof(IsPlaying));
                }
            } else if (cmd is SeekPlayPosTickNotification ||
                cmd is SetPlayPosTickNotification) {
                this.RaisePropertyChanged(nameof(PlayPosTick));
                this.RaisePropertyChanged(nameof(PlayPosTime));
            } else if (cmd is SetRangeSelectionNotification) {
                this.RaisePropertyChanged(nameof(ShowPlayPosHighlight));
            }
        }
    }
}
