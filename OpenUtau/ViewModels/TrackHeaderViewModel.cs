using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using OpenUtau.Api;
using OpenUtau.App;
using OpenUtau.App.Views;
using OpenUtau.Core;
using OpenUtau.Core.DiffSinger;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;
using ReactiveUI.SourceGenerators;
using Serilog;

namespace OpenUtau.App.ViewModels {
    public partial class TrackHeaderViewModel : ViewModelBase, IActivatableViewModel {
        public int TrackNo => track.TrackNo + 1;
        public USinger Singer => track.Singer;
        public Phonemizer Phonemizer => track.Phonemizer;
        public string PhonemizerTag => track.Phonemizer.Tag;
        public ReactiveCommand<USinger, RxVoid> SelectSingerCommand { get; }
        public ReactiveCommand<USinger?, RxVoid> AllSetSingerCommand { get; }
        public IReadOnlyList<MenuItemViewModel>? PhonemizerMenuItems { get; set; }
        public ReactiveCommand<PhonemizerFactory, RxVoid> SelectPhonemizerCommand { get; }
        public ReactiveCommand<PhonemizerFactory, RxVoid> AllSetPhonemizerCommand { get; }
        [Reactive] public partial string TrackName { get; set; } = string.Empty;
        [Reactive] public partial SolidColorBrush TrackAccentColor { get; set; } = ThemeManager.GetTrackColor("Blue").AccentColor;
        [Reactive] public partial TrackColor TrackColor { get; set; } = ThemeManager.GetTrackColor("Blue");
        [Reactive] public partial double Volume { get; set; }
        [Reactive] public partial double Pan { get; set; }
        [Reactive] public partial bool Mute { get; set; }
        [Reactive] public partial bool Muted { get; set; }
        [Reactive] public partial bool Solo { get; set; }
        [Reactive] public partial bool IsSelected { get; set; }
        [Reactive] public partial bool IsOpenInPianoRoll { get; set; }
        [Reactive] public partial Bitmap? Avatar { get; set; }
        [Reactive] public partial bool IsSingerVisible { get; set; }
        [Reactive] public partial bool IsPhonemizerVisible { get; set; }
        [Reactive] public partial bool IsTrackSettingsVisible { get; set; }
        [Reactive] public partial bool MixFxEnabled { get; set; }
        [Reactive] public partial IBrush HeaderBorderBrush { get; set; } = ThemeManager.NeutralAccentBrushSemi;
        [Reactive] public partial IBrush HeaderBackgroundBrush { get; set; } = Brushes.Transparent;

        public ViewModelActivator Activator { get; }

        private readonly UTrack track;
        // Parameterless constructor for Avalonia preview only.
        public TrackHeaderViewModel() {
            SelectSingerCommand = ReactiveCommand.Create<USinger>(_ => { });
            AllSetSingerCommand = ReactiveCommand.Create<USinger?>(_ => { });
            SelectPhonemizerCommand = ReactiveCommand.Create<PhonemizerFactory>(_ => { });
            AllSetPhonemizerCommand = ReactiveCommand.Create<PhonemizerFactory>(_ => { });
            Activator = new ViewModelActivator();
            track = new UTrack(DocManager.Inst.Project);
            SubscribeSelectionStyle();
            RefreshSelectionStyle();
        }

        public TrackHeaderViewModel(UTrack track) {
            this.track = track;
            SelectSingerCommand = ReactiveCommand.Create<USinger>(singer => {
                if (track.Singer != singer) {
                    DocManager.Inst.StartUndoGroup("command.track.singer");
                    ApplySingerToTrack(track, singer);
                    DocManager.Inst.ExecuteCmd(new VoiceColorRemappingNotification(track.TrackNo, true));
                    DocManager.Inst.EndUndoGroup();
                    UpdateRecentSingers(singer);
                    Preferences.Save();
                    MessageBus.Current.SendMessage(new PianorollRefreshEvent("Part"));
                }
                MessageBus.Current.SendMessage(new TracksRefreshEvent());
                this.RaisePropertyChanged(nameof(Singer));
                RefreshAvatar();
                UpdateTrackSettingsVisibility();
            });
            AllSetSingerCommand = ReactiveCommand.Create<USinger?>(singer => {
                var targetTracks = GetBatchTargetTracks();
                DocManager.Inst.StartUndoGroup("command.track.singer");
                foreach (var targetTrack in targetTracks) {
                    ApplySingerToTrack(targetTrack, singer);
                }
                DocManager.Inst.ExecuteCmd(new VoiceColorRemappingNotification(-1, true));
                DocManager.Inst.EndUndoGroup();
                UpdateRecentSingers(singer);
                Preferences.Save();
                MessageBus.Current.SendMessage(new PianorollRefreshEvent("Part"));
                MessageBus.Current.SendMessage(new TracksRefreshEvent());
                this.RaisePropertyChanged(nameof(Singer));
                RefreshAvatar();
                UpdateTrackSettingsVisibility();
            });
            SelectPhonemizerCommand = ReactiveCommand.Create<PhonemizerFactory>(factory => {
                if (track.Phonemizer.GetType() != factory.type) {
                    DocManager.Inst.StartUndoGroup("command.track.setting");
                    var phonemizer = factory.Create();
                    Log.Information($"Loading Phonemizer: {phonemizer.ToString()}");
                    DocManager.Inst.ExecuteCmd(new TrackChangePhonemizerCommand(DocManager.Inst.Project, track, phonemizer));
                    DocManager.Inst.EndUndoGroup();
                    var name = phonemizer.GetType().FullName!;
                    if (!string.IsNullOrEmpty(Singer?.Id) && phonemizer != null) {
                        Preferences.Default.SingerPhonemizers[Singer.Id] = name;
                    }
                    Preferences.Default.RecentPhonemizers.Remove(name);
                    Preferences.Default.RecentPhonemizers.Insert(0, name);
                    while (Preferences.Default.RecentPhonemizers.Count > 8) {
                        Preferences.Default.RecentPhonemizers.RemoveRange(
                            8, Preferences.Default.RecentPhonemizers.Count - 8);
                    }
                    Preferences.Save();
                }
                this.RaisePropertyChanged(nameof(Phonemizer));
                this.RaisePropertyChanged(nameof(PhonemizerTag));
            });
            AllSetPhonemizerCommand = ReactiveCommand.Create<PhonemizerFactory>(factory => {
                if (factory == null) {
                    return;
                }
                var name = factory.type.FullName!;
                Log.Information($"Loading Phonemizer: {factory}");
                var targetTracks = GetBatchTargetTracks();
                DocManager.Inst.StartUndoGroup("command.track.setting");
                foreach (var targetTrack in targetTracks) {
                    if (!IsPhonemizerAllowedForTrack(factory, targetTrack)) {
                        continue;
                    }
                    var phonemizer = factory.Create();
                    if (phonemizer != null) {
                        DocManager.Inst.ExecuteCmd(new TrackChangePhonemizerCommand(DocManager.Inst.Project, targetTrack, phonemizer));
                    }
                    var targetSingerId = targetTrack.Singer?.Id;
                    if (!string.IsNullOrEmpty(targetSingerId) && targetTrack.Singer?.Found == true && phonemizer != null) {
                        Preferences.Default.SingerPhonemizers[targetSingerId] = name;
                    }
                }
                Preferences.Default.RecentPhonemizers.Remove(name);
                Preferences.Default.RecentPhonemizers.Insert(0, name);
                DocManager.Inst.EndUndoGroup();
                Preferences.Save();
                MessageBus.Current.SendMessage(new PianorollRefreshEvent("Part"));
                MessageBus.Current.SendMessage(new TracksRefreshEvent());
                this.RaisePropertyChanged(nameof(Phonemizer));
                this.RaisePropertyChanged(nameof(PhonemizerTag));
            });
            Activator = new ViewModelActivator();

            TrackName = track.TrackName;
            TrackAccentColor = ThemeManager.GetTrackColor(track.TrackColor).AccentColor;
            TrackColor = Preferences.Default.UseTrackColor
                ? ThemeManager.GetTrackColor(track.TrackColor)
                : ThemeManager.GetTrackColor("Blue");
            Volume = track.Volume;
            Pan = track.Pan;
            Mute = track.Mute;
            Muted = track.Muted;
            Solo = track.Solo;
            MixFxEnabled = track.MixFx?.Enabled ?? false;
            this.WhenAnyValue(x => x.Volume)
                .Subscribe(volume => {
                    track.Volume = volume;
                    DocManager.Inst.ExecuteCmd(new VolumeChangeNotification(track.TrackNo, Muted ? -24 : volume));
                });
            this.WhenAnyValue(x => x.Pan)
                .Subscribe(pan => {
                    track.Pan = pan;
                    DocManager.Inst.ExecuteCmd(new PanChangeNotification(track.TrackNo, pan));
                });
            this.WhenAnyValue(x => x.Mute)
                .Subscribe(mute => {
                    track.Mute = mute;
                });
            this.WhenAnyValue(x => x.Muted)
                .Subscribe(muted => {
                    track.Muted = muted;
                    DocManager.Inst.ExecuteCmd(new VolumeChangeNotification(track.TrackNo, muted ? -24 : Volume));
                });
            this.WhenAnyValue(x => x.Solo)
                .Subscribe(solo => {
                    track.Solo = solo;
                });
            this.WhenAnyValue(x => x.MixFxEnabled)
                .Subscribe(enabled => {
                    if (track.MixFx != null) {
                        track.MixFx.Enabled = enabled;
                    } else if (enabled) {
                        track.MixFx = new UMixFx { Enabled = true };
                    }
                });
            SubscribeSelectionStyle();

            RefreshAvatar();
            RefreshSelectionStyle();
            UpdateTrackSettingsVisibility();
        }

        void SubscribeSelectionStyle() {
            this.WhenAnyValue(x => x.IsOpenInPianoRoll)
                .Subscribe(_ => RefreshSelectionStyle());
            MessageBus.Current.Listen<ThemeChangedEvent>()
                .Subscribe(_ => RefreshSelectionStyle());
            MessageBus.Current.Listen<PianoRollOpenPartChangedEvent>()
                .Subscribe(e => IsOpenInPianoRoll = e.Part?.trackNo == track.TrackNo);
        }

        public void RefreshSelectionStyle() {
            HeaderBackgroundBrush = IsOpenInPianoRoll
                ? ThemeManager.TrackBackgroundAltBrush
                : ThemeManager.WorkspaceCardBrush;
        }

        public void ToggleSolo() {
            MessageBus.Current.SendMessage(new TracksSoloEvent(track.TrackNo, !track.Solo, false));
        }

        public void SoloAdditionally() {
            MessageBus.Current.SendMessage(new TracksSoloEvent(track.TrackNo, !track.Solo, true));
        }

        public void UnsoloAll() {
            MessageBus.Current.SendMessage(new TracksSoloEvent(-1, false, false));
        }

        public void ToggleMute() {
            Mute = !Mute;
            this.RaisePropertyChanged(nameof(Mute));
            JudgeMuted();
        }

        // SetMute: do not overload ToggleMute — Avalonia 12 MethodToCommandConverter
        // prefers the single-parameter overload and NREs when CommandParameter is null.
        public void SetMute(bool mute) {
            if (mute) {
                Mute = true;
            } else {
                Mute = false;
            }
            this.RaisePropertyChanged(nameof(Mute));
            JudgeMuted();
        }

        public void MuteOnly() {
            MessageBus.Current.SendMessage(new TracksMuteEvent(-1, false));
            ToggleMute();
        }

        public void MuteAllOthers() {
            MessageBus.Current.SendMessage(new TracksMuteEvent(-1, true));
            ToggleMute();
        }

        public void UnmuteAll() {
            MessageBus.Current.SendMessage(new TracksMuteEvent(-1, false));
        }

        public void JudgeMuted() {
            bool muted = UTrack.ComputeMuted(
                Solo, Mute, DocManager.Inst.Project.SoloTrackExist);
            // Keep model + VM in sync even when the VM value is unchanged
            // (otherwise a stale track.Muted can survive into the next Play).
            track.Muted = muted;
            Muted = muted;
            DocManager.Inst.ExecuteCmd(new VolumeChangeNotification(
                track.TrackNo, muted ? -24 : Volume));
            this.RaisePropertyChanged(nameof(Muted));
        }

        static bool IsTrackSelected(UTrack projectTrack) {
            var tracksViewModel = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
                ?.MainWindow?.DataContext as MainWindowViewModel;
            return tracksViewModel?.TracksViewModel.SelectedTracks.Contains(projectTrack) == true;
        }

        List<UTrack> GetBatchTargetTracks() {
            var targetTracks = DocManager.Inst.Project.tracks
                .Where(projectTrack => projectTrack != null)
                .Where(IsTrackSelected)
                .ToList();
            if (targetTracks.Count <= 1) {
                targetTracks = DocManager.Inst.Project.tracks.ToList();
            }
            return targetTracks;
        }

        private void ApplySingerToTrack(UTrack targetTrack, USinger? singer) {
            if (singer is USinger selectedSinger) {
                Log.Information($"Loading Singer: {selectedSinger.Name}");
                DocManager.Inst.ExecuteCmd(new TrackChangeSingerCommand(DocManager.Inst.Project, targetTrack, selectedSinger));
                if (!string.IsNullOrEmpty(selectedSinger.Id) &&
                    Preferences.Default.SingerPhonemizers.TryGetValue(selectedSinger.Id, out var phonemizerName) &&
                    TryChangePhonemizer(targetTrack, phonemizerName)) {
                } else if (!string.IsNullOrEmpty(selectedSinger.DefaultPhonemizer)) {
                    TryChangePhonemizer(targetTrack, selectedSinger.DefaultPhonemizer);
                }
                if (!selectedSinger.Found || selectedSinger.SingerType != targetTrack.RendererSettings.Renderer?.SingerType) {
                    var settings = new URenderSettings();
                    if (selectedSinger.Found) {
                        settings = new URenderSettings {
                            renderer = Core.Render.Renderers.GetDefaultRenderer(selectedSinger.SingerType),
                        };
                    }
                    DocManager.Inst.ExecuteCmd(new TrackChangeRenderSettingCommand(DocManager.Inst.Project, targetTrack, settings));
                }
            } else {
                DocManager.Inst.ExecuteCmd(new TrackChangeSingerCommand(DocManager.Inst.Project, targetTrack, USinger.CreateMissing(string.Empty)));
                var settings = new URenderSettings();
                DocManager.Inst.ExecuteCmd(new TrackChangeRenderSettingCommand(DocManager.Inst.Project, targetTrack, settings));
            }
        }

        private void UpdateRecentSingers(USinger? singer) {
            if (!string.IsNullOrEmpty(singer?.Id) && singer.Found) {
                Preferences.Default.RecentSingers.Remove(singer.Id);
                Preferences.Default.RecentSingers.Insert(0, singer.Id);
                if (Preferences.Default.RecentSingers.Count > 16) {
                    Preferences.Default.RecentSingers.RemoveRange(
                        16, Preferences.Default.RecentSingers.Count - 16);
                }
            }
        }

        private bool TryChangePhonemizer(UTrack targetTrack, string phonemizerName) {
            try {
                var factory = FindPhonemizerByName(phonemizerName);
                if (!IsPhonemizerAllowedForTrack(factory, targetTrack)) {
                    factory = null;
                }
                factory ??= GetFallbackPhonemizerFactory(targetTrack);
                var phonemizer = factory?.Create();
                if (phonemizer != null) {
                    DocManager.Inst.ExecuteCmd(new TrackChangePhonemizerCommand(DocManager.Inst.Project, targetTrack, phonemizer));
                    return true;
                }
            } catch (Exception e) {
                Log.Error(e, $"Failed to load phonemizer {phonemizerName}");
            }
            return false;
        }

        static bool IsPhonemizerAllowedForTrack(PhonemizerFactory? factory, UTrack targetTrack) {
            if (factory == null) {
                return false;
            }
            return PhonemizerFactory.EnumerateForSinger(targetTrack.Singer).Any(f => f.type == factory.type);
        }

        static PhonemizerFactory? GetFallbackPhonemizerFactory(UTrack targetTrack) {
            var singer = targetTrack.Singer;
            if (singer != null && singer.Found && !string.IsNullOrEmpty(singer.DefaultPhonemizer)) {
                var singerDefault = PhonemizerFactory.Get(singer.DefaultPhonemizer);
                if (IsPhonemizerAllowedForTrack(singerDefault, targetTrack)) {
                    return singerDefault;
                }
            }
            var firstForSinger = PhonemizerFactory.EnumerateForSinger(singer).FirstOrDefault();
            if (firstForSinger != null) {
                return firstForSinger;
            }
            return singer?.SingerType switch {
                USingerType.DiffSinger => PhonemizerFactory.Get(typeof(DiffSingerPhonemizer)),
                USingerType.Voicevox => PhonemizerFactory.Get(typeof(Core.Voicevox.VoicevoxPhonemizer)),
                _ => PhonemizerFactory.Get(typeof(DefaultPhonemizer)),
            };
        }

        static MenuItemViewModel MenuLineSeparator() => new PhonemizerMenuSeparatorViewModel();

        public string GetPhonemizerGroupHeader(string key) {
            if (key is null) {
                return "General";
            }
            if (ThemeManager.TryGetString($"languages.{key.ToLowerInvariant()}", out var value)) {
                return $"{key}: {value}";
            }
            return key;
        }

        PhonemizerFactory? FindPhonemizerByName(string name) {
            return PhonemizerFactory.Get(name);
        }

        public void RefreshPhonemizers() {
            var items = new List<MenuItemViewModel>();
            var available = PhonemizerFactory.EnumerateForSinger(track.Singer).ToArray();
            var availableTypes = available.Select(factory => factory.type).ToHashSet();
            bool flatMenu = PhonemizerFactory.UsesFlatPhonemizerMenu(track.Singer);
            //Singer default
            if (track.Singer != null && track.Singer.Found) {
                var factory = FindPhonemizerByName(track.Singer.DefaultPhonemizer);
                if (factory != null && availableTypes.Contains(factory.type)) {
                    items.Add(new PhonemizerMenuItemViewModel(
                        factory,
                        SelectPhonemizerCommand,
                        ThemeManager.GetString("tracks.singerdefault"),
                        AllSetPhonemizerCommand));
                }
            }
            //Recently used phonemizers
            var recentItems = Preferences.Default.RecentPhonemizers
                .Select(name => FindPhonemizerByName(name))
                .Where(factory => factory != null && availableTypes.Contains(factory.type))
                .OrderBy(factory => factory!.tag)
                .Select(factory => new PhonemizerMenuItemViewModel(factory!, SelectPhonemizerCommand, null, AllSetPhonemizerCommand))
                .ToArray();
            items.AddRange(recentItems);
            if (flatMenu) {
                var listed = items
                    .Select(item => item.CommandParameter)
                    .OfType<PhonemizerFactory>()
                    .Select(factory => factory.type)
                    .ToHashSet();
                var remaining = available
                    .Where(factory => !listed.Contains(factory.type))
                    .OrderBy(factory => factory.tag)
                    .ThenBy(factory => factory.name)
                    .Select(factory => new PhonemizerMenuItemViewModel(factory, SelectPhonemizerCommand, null, AllSetPhonemizerCommand))
                    .ToArray();
                if (remaining.Length > 0 && items.Count > 0) {
                    items.Add(MenuLineSeparator());
                }
                items.AddRange(remaining);
            } else {
                // Classic UTAU: language groups directly under the menu
                if (items.Count > 0) {
                    items.Add(MenuLineSeparator());
                }
                items.AddRange(available.GroupBy(factory => factory.language)
                    .OrderBy(group => group.Key)
                    .Select(group => new MenuItemViewModel() {
                        Header = GetPhonemizerGroupHeader(group.Key),
                        Items = group.OrderBy(factory => factory.tag).ThenBy(factory => factory.name)
                            .Select(factory => new PhonemizerMenuItemViewModel(factory, SelectPhonemizerCommand, null, AllSetPhonemizerCommand))
                            .ToArray(),
                    }));
            }
            PhonemizerMenuItems = items.ToArray();
            this.RaisePropertyChanged(nameof(PhonemizerMenuItems));
        }

        // Keeps the avatar column's width while there is no avatar to show.
        private static Bitmap? emptyAvatar;
        private static Bitmap EmptyAvatar => emptyAvatar ??= new RenderTargetBitmap(new PixelSize(1, 1));

        public void RefreshAvatar() {
            var singer = track?.Singer;
            if (singer == null) {
                Avatar = EmptyAvatar;
                return;
            }
            // Cached bitmaps are shared, so they are never disposed here.
            Avatar = SingerAvatarCache.Get(singer, bitmap => {
                if (ReferenceEquals(track?.Singer, singer)) {
                    Avatar = bitmap ?? EmptyAvatar;
                }
            }) ?? EmptyAvatar;
        }

        void UpdateTrackSettingsVisibility() {
            IsTrackSettingsVisible = track.Singer is { Found: true, SingerType: USingerType.Classic };
        }

        public void ManuallyRaise() {
            TrackName = track.TrackName;
            TrackAccentColor = ThemeManager.GetTrackColor(track.TrackColor).AccentColor;
            TrackColor = Preferences.Default.UseTrackColor
                ? ThemeManager.GetTrackColor(track.TrackColor)
                : ThemeManager.GetTrackColor("Blue");
            Mute = track.Mute;
            Muted = track.Muted;
            Solo = track.Solo;
            Volume = track.Volume;
            Pan = track.Pan;
            RefreshSelectionStyle();
            this.RaisePropertyChanged(nameof(Singer));
            this.RaisePropertyChanged(nameof(TrackNo));
            this.RaisePropertyChanged(nameof(TrackName));
            this.RaisePropertyChanged(nameof(TrackAccentColor));
            this.RaisePropertyChanged(nameof(TrackColor));
            this.RaisePropertyChanged(nameof(Phonemizer));
            this.RaisePropertyChanged(nameof(PhonemizerTag));
            UpdateTrackSettingsVisibility();
            this.RaisePropertyChanged(nameof(Mute));
            this.RaisePropertyChanged(nameof(Muted));
            this.RaisePropertyChanged(nameof(Solo));
            MixFxEnabled = track.MixFx?.Enabled ?? false;
            this.RaisePropertyChanged(nameof(MixFxEnabled));
            this.RaisePropertyChanged(nameof(Volume));
            this.RaisePropertyChanged(nameof(Pan));
            RefreshAvatar();
        }

        public void Remove() {
            DocManager.Inst.StartUndoGroup("command.track.delete");
            var targetTracks = DocManager.Inst.Project.tracks
                .Where(projectTrack => projectTrack != null)
                .Where(IsTrackSelected)
                .ToList();
            if (targetTracks.Count < 2) {
                targetTracks = new List<UTrack> { track };
            }
            foreach (var target in targetTracks) {
                DocManager.Inst.ExecuteCmd(new RemoveTrackCommand(DocManager.Inst.Project, target));
            }
            DocManager.Inst.EndUndoGroup();
        }

        public void MoveUp() {
            if (track == DocManager.Inst.Project.tracks.First()) {
                return;
            }
            DocManager.Inst.StartUndoGroup("command.track.order");
            var targetTracks = DocManager.Inst.Project.tracks
                .Where(projectTrack => projectTrack != null)
                .Where(IsTrackSelected)
                .ToList();
            if (targetTracks.Count < 2) {
                targetTracks = new List<UTrack> { track };
            } else {
                targetTracks = targetTracks
                    .Where(target => target != DocManager.Inst.Project.tracks.First())
                    .OrderBy(target => target.TrackNo)
                    .ToList();
            }
            foreach (var target in targetTracks) {
                DocManager.Inst.ExecuteCmd(new MoveTrackCommand(DocManager.Inst.Project, target, true));
            }
            DocManager.Inst.EndUndoGroup();
        }

        public void MoveDown() {
            if (track == DocManager.Inst.Project.tracks.Last()) {
                return;
            }
            DocManager.Inst.StartUndoGroup("command.track.order");
            var targetTracks = DocManager.Inst.Project.tracks
                .Where(projectTrack => projectTrack != null)
                .Where(IsTrackSelected)
                .ToList();
            if (targetTracks.Count < 2) {
                targetTracks = new List<UTrack> { track };
            } else {
                targetTracks = targetTracks
                    .Where(target => target != DocManager.Inst.Project.tracks.Last())
                    .OrderByDescending(target => target.TrackNo)
                    .ToList();
            }
            foreach (var target in targetTracks) {
                DocManager.Inst.ExecuteCmd(new MoveTrackCommand(DocManager.Inst.Project, target, false));
            }
            DocManager.Inst.EndUndoGroup();
        }

        public void Rename() {
            var dialog = new TypeInDialog();
            dialog.Title = ThemeManager.GetString("tracks.rename");
            dialog.SetText(track.TrackName);
            dialog.onFinish = name => {
                if (!string.IsNullOrWhiteSpace(name) && name != track.TrackName) {
                    DocManager.Inst.StartUndoGroup("command.track.setting");
                    var targetTracks = DocManager.Inst.Project.tracks
                        .Where(projectTrack => projectTrack != null)
                        .Where(IsTrackSelected)
                        .ToList();
                    if (targetTracks.Count < 2) {
                        TrackName = name;
                        DocManager.Inst.ExecuteCmd(new RenameTrackCommand(DocManager.Inst.Project, track, name));
                    } else {
                        for (int i = 0; i < targetTracks.Count; i++) {
                            string batchName = $"{name}_{i:000}";
                            DocManager.Inst.ExecuteCmd(new RenameTrackCommand(DocManager.Inst.Project, targetTracks[i], batchName));
                        }
                    }
                    DocManager.Inst.EndUndoGroup();
                }
            };
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null) {
                dialog.ShowDialog(desktop.MainWindow);
            }
        }

        public async void SelectTrackColor() {
            var targetTracks = DocManager.Inst.Project.tracks
                .Where(projectTrack => projectTrack != null)
                .Where(IsTrackSelected)
                .ToList();
            if (targetTracks.Count < 2) {
                targetTracks = new List<UTrack> { track };
            }
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop
                || desktop.MainWindow == null) {
                return;
            }
            foreach (var target in targetTracks) {
                var dialog = new TrackColorDialog {
                    DataContext = new TrackColorViewModel(target),
                };
                await dialog.ShowDialog(desktop.MainWindow);
            }
            TrackAccentColor = ThemeManager.GetTrackColor(track.TrackColor).AccentColor;
            TrackColor = Preferences.Default.UseTrackColor
                ? ThemeManager.GetTrackColor(track.TrackColor)
                : ThemeManager.GetTrackColor("Blue");
            RefreshSelectionStyle();
        }

        public void Duplicate() {
            var targetTracks = DocManager.Inst.Project.tracks
                .Where(projectTrack => projectTrack != null)
                .Where(IsTrackSelected)
                .ToList();
            if (targetTracks.Count < 2) {
                targetTracks = new List<UTrack> { track };
            } else {
                targetTracks = targetTracks
                    .OrderByDescending(target => target.TrackNo)
                    .ToList();
            }
            DocManager.Inst.StartUndoGroup("command.track.duplicate");
            foreach (var source in targetTracks) {
                var sourceTrackNo = source.TrackNo;
                var newTrack = new UTrack(source.TrackName + "_copy") {
                    TrackNo = sourceTrackNo + 1,
                    Singer = source.Singer,
                    Phonemizer = source.Phonemizer,
                    RendererSettings = source.RendererSettings,
                    Mute = source.Mute,
                    Muted = source.Muted,
                    Solo = false,
                    Volume = source.Volume,
                    Pan = source.Pan,
                    TrackColor = source.TrackColor,
                    TrackExpressions = source.TrackExpressions.Select(exp => exp.Clone()).ToList(),
                    ExpressionDefaultOverrides = OpenUtau.Core.Util.ExpressionDefaultResolver.CloneOverrides(source),
                };
                DocManager.Inst.ExecuteCmd(new AddTrackCommand(DocManager.Inst.Project, newTrack));
                var parts = DocManager.Inst.Project.parts
                    .Where(part => part.trackNo == sourceTrackNo)
                    .Select(part => part.Clone()).ToList();
                foreach (var part in parts) {
                    part.trackNo = newTrack.TrackNo;
                    DocManager.Inst.ExecuteCmd(new AddPartCommand(DocManager.Inst.Project, part));
                }
            }
            DocManager.Inst.EndUndoGroup();
        }

        public void DuplicateSettings() {
            var targetTracks = DocManager.Inst.Project.tracks
                .Where(projectTrack => projectTrack != null)
                .Where(IsTrackSelected)
                .ToList();
            if (targetTracks.Count < 2) {
                targetTracks = new List<UTrack> { track };
            } else {
                targetTracks = targetTracks
                    .OrderByDescending(target => target.TrackNo)
                    .ToList();
            }
            DocManager.Inst.StartUndoGroup("command.track.duplicate");
            foreach (var source in targetTracks) {
                DocManager.Inst.ExecuteCmd(new AddTrackCommand(DocManager.Inst.Project, new UTrack(source.TrackName + "_copy") {
                    TrackNo = source.TrackNo + 1,
                    Singer = source.Singer,
                    Phonemizer = source.Phonemizer,
                    RendererSettings = source.RendererSettings,
                    Mute = source.Mute,
                    Muted = source.Muted,
                    Solo = false,
                    Volume = source.Volume,
                    Pan = source.Pan,
                    TrackColor = source.TrackColor,
                    TrackExpressions = source.TrackExpressions.Select(exp => exp.Clone()).ToList(),
                    ExpressionDefaultOverrides = OpenUtau.Core.Util.ExpressionDefaultResolver.CloneOverrides(source),
                }));
            }
            DocManager.Inst.EndUndoGroup();
        }

        public void StandardizeSettings() {
            var targetTracks = DocManager.Inst.Project.tracks
                .Where(projectTrack => projectTrack != null)
                .Where(IsTrackSelected)
                .ToList();
            if (targetTracks.Count <= 1) {
                targetTracks = DocManager.Inst.Project.tracks.ToList();
            }
            var phonemizerFactory = PhonemizerFactory.Get(track.Phonemizer.GetType());
            DocManager.Inst.StartUndoGroup("command.track.setting");
            foreach (var targetTrack in targetTracks) {
                if (targetTrack == track) {
                    continue;
                }
                if (track.Singer != targetTrack.Singer) {
                    ApplySingerToTrack(targetTrack, track.Singer);
                }
                if (targetTrack.Phonemizer.GetType() != track.Phonemizer.GetType()) {
                    var phonemizer = phonemizerFactory?.Create();
                    if (phonemizer != null && IsPhonemizerAllowedForTrack(phonemizerFactory, targetTrack)) {
                        DocManager.Inst.ExecuteCmd(new TrackChangePhonemizerCommand(DocManager.Inst.Project, targetTrack, phonemizer));
                    }
                }
                DocManager.Inst.ExecuteCmd(new TrackChangeRenderSettingCommand(
                    DocManager.Inst.Project,
                    targetTrack,
                    track.RendererSettings.Clone()));
                DocManager.Inst.ExecuteCmd(new TrackChangeSettingsCommand(
                    DocManager.Inst.Project,
                    targetTrack,
                    track.Mute,
                    track.Volume,
                    track.Pan));
                DocManager.Inst.ExecuteCmd(new ChangeTrackColorCommand(
                    DocManager.Inst.Project,
                    targetTrack,
                    track.TrackColor));
                DocManager.Inst.ExecuteCmd(new ConfigureExpressionsCommand(
                    DocManager.Inst.Project,
                    DocManager.Inst.Project.expressions.Values.ToArray(),
                    targetTrack,
                    track.TrackExpressions.Select(exp => exp.Clone()).ToArray()));
            }
            DocManager.Inst.EndUndoGroup();
            MessageBus.Current.SendMessage(new TracksRefreshEvent());
            MessageBus.Current.SendMessage(new PianorollRefreshEvent("TrackColor"));
        }

        public void RotateSelectedTrackSingers() {
            var targetTracks = DocManager.Inst.Project.tracks
                .Where(projectTrack => projectTrack != null)
                .Where(IsTrackSelected)
                .OrderBy(targetTrack => targetTrack.TrackNo)
                .ToList();
            if (targetTracks.Count <= 1) {
                targetTracks = DocManager.Inst.Project.tracks.ToList();
            }
            var singers = targetTracks
                .Select(targetTrack => targetTrack.Singer)
                .ToList();
            DocManager.Inst.StartUndoGroup("command.track.singer");
            for (int i = 0; i < targetTracks.Count; i++) {
                var singer = singers[(i + 1) % singers.Count];
                ApplySingerToTrack(targetTracks[i], singer);
            }
            DocManager.Inst.EndUndoGroup();
            DocManager.Inst.ExecuteCmd(new VoiceColorRemappingNotification(-1, true));
            MessageBus.Current.SendMessage(new TracksRefreshEvent());
            MessageBus.Current.SendMessage(new PianorollRefreshEvent("Part"));
        }

        public void VoiceColorRemapping() {
            var targetTracks = DocManager.Inst.Project.tracks
                .Where(projectTrack => projectTrack != null)
                .Where(IsTrackSelected)
                .ToList();
            if (targetTracks.Count < 2) {
                targetTracks = new List<UTrack> { track };
            }
            foreach (var target in targetTracks) {
                if (target.Singer != null && target.Singer.Found && target.VoiceColorExp != null) {
                    DocManager.Inst.ExecuteCmd(new VoiceColorRemappingNotification(target.TrackNo, false));
                }
            }
        }

        public void OpenMixFxDialog() {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null) {
                var dialog = new MixFxDialog(track);
                dialog.ShowDialog(desktop.MainWindow);
            }
        }
    }
}
