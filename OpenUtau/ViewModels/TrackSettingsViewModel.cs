using System;
using System.Collections.Generic;
using System.Linq;
using DynamicData.Binding;
using OpenUtau.Classic;
using OpenUtau.Core;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace OpenUtau.App.ViewModels {
    public sealed partial class TrackRendererOption {
        public string Id { get; }
        public string DisplayName { get; }

        public TrackRendererOption(string id, string displayName) {
            Id = id;
            DisplayName = displayName;
        }

        public override string ToString() => DisplayName;
    }

    partial class TrackSettingsViewModel : ViewModelBase {
        public UTrack Track { get; private set; }
        public IReadOnlyList<TrackRendererOption> RendererOptions { get; }
        [Reactive] public partial TrackRendererOption? SelectedRenderer { get; set; }
        public ObservableCollectionExtended<IResampler> Resamplers => resamplers;
        [Reactive] public partial IResampler? Resampler { get; set; }
        [Reactive] public partial bool NeedsResampler { get; set; }
        public ObservableCollectionExtended<IWavtool> Wavtools => wavtools;
        [Reactive] public partial IWavtool? Wavtool { get; set; }
        [Reactive] public partial bool NeedsWavtool { get; set; }

        readonly ObservableCollectionExtended<IResampler> resamplers =
            new ObservableCollectionExtended<IResampler>();
        readonly ObservableCollectionExtended<IWavtool> wavtools =
            new ObservableCollectionExtended<IWavtool>();

        public TrackSettingsViewModel(UTrack track) {
            ToolsManager.Inst.Initialize();
            Track = track;
            RendererOptions = Renderers.GetSupportedRenderers(USingerType.Classic)
                .Select(id => new TrackRendererOption(id, FormatRendererName(id)))
                .ToArray();

            var currentRenderer = string.IsNullOrEmpty(Track.RendererSettings.renderer)
                ? Renderers.GetDefaultRenderer(USingerType.Classic)
                : Track.RendererSettings.renderer;
            SelectedRenderer = RendererOptions.FirstOrDefault(option =>
                string.Equals(option.Id, currentRenderer, StringComparison.OrdinalIgnoreCase))
                ?? RendererOptions.FirstOrDefault();

            resamplers.AddRange(ToolsManager.Inst.Resamplers);
            string? resamplerName = Track.RendererSettings.resampler;
            if (string.IsNullOrEmpty(resamplerName)) {
                if (!Preferences.Default.DefaultResamplers.TryGetValue(Renderers.CLASSIC, out resamplerName)) {
                    resamplerName = string.Empty;
                }
            }
            Resampler = ToolsManager.Inst.GetResampler(resamplerName);
            wavtools.AddRange(Renderers.GetSupportedWavtools(Resampler));
            string? wavtoolName = Track.RendererSettings.wavtool;
            if (string.IsNullOrEmpty(wavtoolName)) {
                if (!Preferences.Default.DefaultWavtools.TryGetValue(Renderers.CLASSIC, out wavtoolName)) {
                    wavtoolName = string.Empty;
                }
            }
            Wavtool = ToolsManager.Inst.GetWavtool(wavtoolName);

            this.WhenAnyValue(x => x.SelectedRenderer)
                .Subscribe(option => UpdateClassicToolsVisibility(option?.Id));
            UpdateClassicToolsVisibility(SelectedRenderer?.Id);

            this.WhenAnyValue(x => x.Resampler)
                .Subscribe(resampler => {
                    resampler?.CheckPermissions();
                    var wavtool = Wavtool;
                    wavtools.Clear();
                    wavtools.AddRange(Renderers.GetSupportedWavtools(resampler));
                    if (wavtool != null && wavtools.Contains(wavtool)) {
                        Wavtool = wavtool;
                    } else {
                        Wavtool = wavtools.FirstOrDefault();
                    }
                });
            this.WhenAnyValue(x => x.Wavtool)
                .Subscribe(wavtool => {
                    wavtool?.CheckPermissions();
                });
        }

        static string FormatRendererName(string id) {
            return id switch {
                Renderers.CLASSIC => "Classic",
                Renderers.WORLDLINE_R => "WORLDLINE-R",
                _ => id,
            };
        }

        void UpdateClassicToolsVisibility(string? rendererId) {
            bool classic = rendererId == Renderers.CLASSIC;
            NeedsResampler = classic;
            NeedsWavtool = classic;
        }

        public void OpenResamplerLocation() {
            OS.OpenFolder(PathManager.Inst.ResamplersPath);
        }

        public void SetDefaultResampler() {
            if (Resampler != null) {
                Preferences.Default.DefaultResamplers[Renderers.CLASSIC] = Resampler.ToString() ?? string.Empty;
                Preferences.Save();
            }
        }

        public void OpenWavtoolLocation() {
            OS.OpenFolder(PathManager.Inst.WavtoolsPath);
        }

        public void SetDefaultWavtool() {
            if (Wavtool != null) {
                Preferences.Default.DefaultWavtools[Renderers.CLASSIC] = Wavtool.ToString() ?? string.Empty;
                Preferences.Save();
            }
        }

        public void Finish() {
            if (Track.Singer?.SingerType != USingerType.Classic) {
                return;
            }
            DocManager.Inst.StartUndoGroup("command.track.setting");
            var settings = Track.RendererSettings.Clone();
            settings.renderer = SelectedRenderer?.Id ?? settings.renderer;
            if (settings.renderer == Renderers.CLASSIC) {
                settings.resampler = Resampler?.ToString() ?? string.Empty;
                settings.wavtool = Wavtool?.ToString() ?? string.Empty;
            }
            DocManager.Inst.ExecuteCmd(new TrackChangeRenderSettingCommand(DocManager.Inst.Project, Track, settings));
            DocManager.Inst.EndUndoGroup();
            MessageBus.Current.SendMessage(new TracksRefreshEvent());
        }
    }
}
