using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenUtau.Core.Render;
using Serilog;

namespace OpenUtau.Core.Util {

    public static class Preferences {
        public static SerializablePreferences Default;

        static Preferences() {
            Load();
        }
        public class ShortcutBinding {
            public string ActionId { get; set; }
            public string KeyName { get; set; } 
            public string ModifiersName { get; set; }
        }
        public static string ResolveDefaultTrackColor() {
            var name = Default?.DefaultTrackColor;
            return string.IsNullOrWhiteSpace(name) ? "Blue" : name;
        }

        static void EnsureOverlayScrollbars() {
            if (Default != null) {
                Default.UseOverlayScrollbars = true;
            }
        }

        public static void Save() {
            EnsureOverlayScrollbars();
            ClampVoiceColorCurveMax();
            try {
                File.WriteAllText(PathManager.Inst.PrefsFilePath,
                    JsonConvert.SerializeObject(Default, Formatting.Indented),
                    Encoding.UTF8);
            } catch (Exception e) {
                Log.Error(e, "Failed to save prefs.");
            }
        }

        public const int VoiceColorCurveMaxMin = 0;
        public const int VoiceColorCurveMaxMax = 1000;

        public static int GetVoiceColorCurveMax() {
            ClampVoiceColorCurveMax();
            return Default.VoiceColorCurveMax;
        }

        public static void SetVoiceColorCurveMax(int value) {
            Default.VoiceColorCurveMax = Math.Clamp(value, VoiceColorCurveMaxMin, VoiceColorCurveMaxMax);
            Save();
        }

        static void ClampVoiceColorCurveMax() {
            if (Default == null) {
                return;
            }
            Default.VoiceColorCurveMax = Math.Clamp(
                Default.VoiceColorCurveMax, VoiceColorCurveMaxMin, VoiceColorCurveMaxMax);
        }

        public static void Reset() {
            Default = new SerializablePreferences();
            try
            {
                string exePath = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
                string shippedPrefsPath = Path.Combine(exePath, "prefs-default.json");
                if (File.Exists(shippedPrefsPath)) {
                    var shippedPrefs = JsonConvert.DeserializeObject<SerializablePreferences>(
                        File.ReadAllText(shippedPrefsPath, Encoding.UTF8));
                    if (shippedPrefs != null) {
                        Default = shippedPrefs;
                    }
                }
            } catch(Exception e){
                Log.Error(e, "failed to load prefs-default.json");
            }
            EnsureOverlayScrollbars();
            Save();
        }

        public static List<string> GetSingerSearchPaths() {
            return new List<string>(Default.SingerSearchPaths);
        }

        public static void SetSingerSearchPaths(List<string> paths) {
            Default.SingerSearchPaths = new List<string>(paths);
            Save();
        }

        public static void AddRecentFileIfEnabled(string filePath){
            //Users can choose adding .ust, .vsqx and .mid files to recent files or not
            string ext = Path.GetExtension(filePath);
            switch(ext){
                case ".ustx":
                    AddRecentFile(filePath);
                    break;
                case ".mid":
                case ".midi":
                    if(Preferences.Default.RememberMid){
                        AddRecentFile(filePath);
                    }
                    break;
                case ".ust":
                    if(Preferences.Default.RememberUst){
                        AddRecentFile(filePath);
                    }
                    break;
                case ".vsqx":
                    if(Preferences.Default.RememberVsqx){
                        AddRecentFile(filePath);
                    }
                    break;
                default:
                    break;
            }
        }

        private static void AddRecentFile(string filePath) {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) {
                return;
            }
            var recent = Default.RecentFiles;
            recent.RemoveAll(f => f == filePath);
            recent.Insert(0, filePath);
            recent.RemoveAll(f => string.IsNullOrEmpty(f)
                || !File.Exists(f)
                || f.Contains(PathManager.Inst.TemplatesPath));
            if (recent.Count > 16) {
                recent.RemoveRange(16, recent.Count - 16);
            }
            Save();
        }

        static void MigrateLegacyPreferences(string json) {
            if (Default == null) {
                return;
            }
            try {
                var jo = JObject.Parse(json);
                if (jo.ContainsKey("UseClassicScrollbars")
                    && !jo.ContainsKey(nameof(SerializablePreferences.UseOverlayScrollbars))) {
                    Default.UseOverlayScrollbars = !jo["UseClassicScrollbars"]!.Value<bool>();
                }
                if (jo.ContainsKey("UseSolidPlaybackLine")
                    && !jo.ContainsKey(nameof(SerializablePreferences.UseModernPlayhead))) {
                    Default.UseModernPlayhead = jo["UseSolidPlaybackLine"]!.Value<bool>();
                }
                if (jo.ContainsKey("UseClassicPlaybackLine")
                    && !jo.ContainsKey(nameof(SerializablePreferences.UseModernPlayhead))) {
                    Default.UseModernPlayhead = !jo["UseClassicPlaybackLine"]!.Value<bool>();
                }
            } catch {
                // ignore migration errors
            }
        }

        private static void Load() {
            try {
                PathManager.Inst.TryMigrateFromLegacyOpenUtau();
                if (File.Exists(PathManager.Inst.PrefsFilePath)) {
                    var json = File.ReadAllText(PathManager.Inst.PrefsFilePath, Encoding.UTF8);
                    Default = JsonConvert.DeserializeObject<SerializablePreferences>(json);
                    if(Default == null) {
                        Reset();
                        return;
                    }

                    if (!ValidString(new Action(() => CultureInfo.GetCultureInfo(Default.Language)))) Default.Language = string.Empty;
                    if (!ValidString(new Action(() => CultureInfo.GetCultureInfo(Default.SortingOrder)))) Default.SortingOrder = string.Empty;
                    Default.NoteCornerRadius = Math.Clamp(Default.NoteCornerRadius, 0, 12);
                    Default.NoteOpacity = Math.Clamp(Default.NoteOpacity, 0.05, 1);
                    Default.NoteBorderThickness = Math.Clamp(Default.NoteBorderThickness, 0.5, 4);
                    Default.ThemeTemperature = Math.Clamp(Default.ThemeTemperature, -100, 100);
                    Default.ThemeTintAmount = Math.Clamp(Default.ThemeTintAmount, 0, 100);
                    if (string.IsNullOrWhiteSpace(Default.ThemeTintColor)) {
                        Default.ThemeTintColor = "#7271C9";
                    }
                    // Warm/Cold presets replaced by ThemeTemperature slider.
                    if (string.Equals(Default.ThemeName, "Warm", StringComparison.OrdinalIgnoreCase)) {
                        Default.ThemeName = "Dark";
                        if (Default.ThemeTemperature == 0) {
                            Default.ThemeTemperature = 50;
                        }
                    } else if (string.Equals(Default.ThemeName, "Cold", StringComparison.OrdinalIgnoreCase)) {
                        Default.ThemeName = "Dark";
                        if (Default.ThemeTemperature == 0) {
                            Default.ThemeTemperature = -50;
                        }
                    }
                    Default.PlaybackPitchFollowSemitoneThreshold = Math.Clamp(Default.PlaybackPitchFollowSemitoneThreshold, 0, 24);
                    Default.PlaybackPitchFollowVerticalPosition = Math.Clamp(Default.PlaybackPitchFollowVerticalPosition, 0.15, 0.85);
                    Default.PlaybackPitchFollowFrameSmoothing = Math.Clamp(Default.PlaybackPitchFollowFrameSmoothing, 0.05, 1);
                    Default.AppearancePanelWidth = Math.Clamp(Default.AppearancePanelWidth, 300, 450);
                    ClampVoiceColorCurveMax();
                    Default.ThemeEditorPanelWidth = Math.Clamp(Default.ThemeEditorPanelWidth, 300, 450);
                    Default.NotePropertiesPanelWidth = Math.Clamp(Default.NotePropertiesPanelWidth, 350, 450);
                    if (!Renderers.getRendererOptions().Contains(Default.DefaultRenderer)) Default.DefaultRenderer = string.Empty;
                    if (!Onnx.getRunnerOptions().Contains(Default.OnnxRunner)) Default.OnnxRunner = string.Empty;
                    if (Default.Theme != null) {
                        Default.ThemeName = Default.Theme switch {
                            1 => "Dark",
                            _ => "Light"
                        };
                        Default.Theme = null;
                    }
                    Default.MigrateRealTimePitchMode();
                    MigrateLegacyPreferences(json);
                } else {
                    Reset();
                }
            } catch (Exception e) {
                Log.Error(e, "Failed to load prefs.");
                Default = new SerializablePreferences();
            }
            EnsureOverlayScrollbars();
        }

        private static bool ValidString(Action action) {
            try {
                action();
                return true;
            } catch {
                return false;
            }
        }

        [Serializable]
        public class SerializablePreferences {
            public WindowSize MainWindowSize = new WindowSize();
            public WindowSize PianorollWindowSize = new WindowSize();
            public int UndoLimit = 100;
            public List<string> SingerSearchPaths = new List<string>();
            public string PlaybackDevice = string.Empty;
            public int PlaybackDeviceNumber;
            public int? PlaybackDeviceIndex;
            public bool ShowPrefs = true;
            public bool ShowTips = true;
            public bool MetronomeEnabled = false;
            public int MetronomeVolume = 60;
            public int MetronomeHighFrequency = 2200;
            public int MetronomeLowFrequency = 1320;
            public string ThemeName = "Dark";
            /// <summary>Color temperature cast: -100 cold … 0 neutral … +100 warm.</summary>
            public int ThemeTemperature = 0;
            /// <summary>Blend toward <see cref="ThemeTintColor"/>: 0 off … 100 strong.</summary>
            public int ThemeTintAmount = 0;
            /// <summary>Target color for theme tint (#RRGGBB).</summary>
            public string ThemeTintColor = "#7271C9";
            /// <summary>
            /// JSON catalog URL for remote themes (GitHub raw). Empty = default oulunai-themes repo.
            /// </summary>
            public string ThemeHubCatalogUrl = "";
            public bool PenPlusDefault = false;
            public int DegreeStyle;
            public bool ShowKeyScaleOnPianoRoll = false;
            public bool UseTrackColor = true;
            public bool TintPianoRollBackgroundWithTrackColor = false;
            public string DefaultTrackColor = "Blue";
            public bool ClearCacheOnQuit = false;
            public bool PreRender = false;
            public bool UseModernPlayhead = true;
            public int NumRenderThreads = 2;
            public string DefaultRenderer = string.Empty;
            public int WorldlineR = 0;
            public string OnnxRunner = string.Empty;
            public int OnnxGpu = 0;
            public double DiffSingerDepth = 1.0;
            public int DiffSingerSteps = 20;
            public int DiffSingerStepsVariance = 20;
            public int DiffSingerStepsPitch = 10;
            public bool DiffSingerTensorCache = true;
            public bool DiffSingerVarianceLocalPitchPatch = true;
            public bool DiffSingerUnvoicedConsonantAcousticF0Interpolate = true;
            public bool DiffSingerShowAcousticF0PatchPreview = false;
            public bool DiffSingerLangCodeHide = false;
            public bool DiffSingerPhonemePanelMode = true;
            public bool DiffSingerPhonemePanelAuto = true;
            public bool DiffSingerLocalRetaking = true;
            public bool DiffSingerShowRenderPhraseBoundaries = false;
            public bool SkipRenderingMutedTracks = false;
            public string Language = string.Empty;
            public string? SortingOrder = null;
            public List<string> RecentFiles = new List<string>();
            public string SkipUpdate = string.Empty;
            public string AdditionalSingerPath = string.Empty;
            public bool InstallToAdditionalSingersPath = true;
            public bool LoadDeepFolderSinger = true;
            public bool PreferCommaSeparator = false;
            public bool ResamplerLogging = false;
            public List<string> RecentSingers = new List<string>();
            public List<string> FavoriteSingers = new List<string>();
            public Dictionary<string, string> SingerPhonemizers = new Dictionary<string, string>();
            public List<string> RecentPhonemizers = new List<string>();
            public bool PreferPortAudio = false;
            public bool UseSystemDefaultAudioDevice = true;
            public double PlayPosMarkerMargin = 0.9;
            public int LockStartTime = 0;
            public int PlaybackAutoScroll = 2;
            /// <summary>When true, piano roll vertically follows note pitch during playback using a pre-planned smooth path.</summary>
            public bool PlaybackPitchFollowEnabled = false;
            /// <summary>Minimum semitone difference between note groups before vertical scroll moves.</summary>
            public int PlaybackPitchFollowSemitoneThreshold = 2;
            /// <summary>Vertical position (0–1) where followed notes are placed in the viewport.</summary>
            public double PlaybackPitchFollowVerticalPosition = 0.5;
            /// <summary>Per-frame lerp factor (0–1) toward the pre-planned pitch-follow path.</summary>
            public double PlaybackPitchFollowFrameSmoothing = 0.1;
            /// <summary>When true, draw the planned pitch-follow camera center path on the piano roll.</summary>
            public bool PlaybackPitchFollowShowPath = false;
            /// <summary>When true, edits invalidate the paused playback mix so the next Play re-renders.</summary>
            public bool ExperimentalInvalidatePlaybackOnEdit = true;
            public bool ReverseLogOrder = true;
            public bool ShowPortrait = true;
            public bool ShowIcon = true;
            public bool ShowGhostNotes = true;
            /// <summary>When true, piano-roll notes (including rests/triggers) are drawn with a border stroke.</summary>
            public bool ShowNoteBorder = true;
            /// <summary>Stroke thickness in pixels for piano-roll note borders.</summary>
            public double NoteBorderThickness = 1;
            /// <summary>When true, tick/timeline grid lines between bar lines are solid; when false, dashed (piano roll, tracks, expression panel).</summary>
            public bool SolidTickGridLines = true;
            /// <summary>When true, dashed default-value and preview curves in the expression panel are drawn solid.</summary>
            public bool SolidExpPanelGridLines = true;
            /// <summary>Always true; classic scrollbars are not exposed in the UI yet.</summary>
            public bool UseOverlayScrollbars = true;
            /// <summary>Corner radius in pixels for piano-roll notes and phonemizer badges (0 = square).</summary>
            public double NoteCornerRadius = 6;
            /// <summary>Piano-roll note fill opacity. Default 35%.</summary>
            public double NoteOpacity = 0.35;
            public EditTool EditTool = new EditTool();
            public bool PlayTone = false;
            /// <summary>Legacy; migrated to <see cref="RealTimePitchMode"/> on load.</summary>
            public bool RealTimePitchGeneration = false;
            public int RealTimePitchMode = (int)LivePitchMode.Off;
            public bool ShowVibrato = true;
            public bool ShowPitch = true;
            public bool ShowFinalPitch = true;
            public bool ShowWaveform = true;
            public bool ShowPhoneme = true;
            public bool ShowExpressions = true;
            public bool ShowRealCurves = false;
            public bool ShowPhonemizerTags = true;
            public bool ShowNoteParams = true;
            public bool ShowAppearancePanel = false;
            public bool ShowDiffSingerPanel = false;
            public bool ShowExpressionDefaultsPanel = false;
            public double AppearancePanelWidth = 300;
            public double ThemeEditorPanelWidth = 300;
            public double NotePropertiesPanelWidth = 350;
            public Dictionary<string, string> DefaultResamplers = new Dictionary<string, string>();
            public Dictionary<string, string> DefaultWavtools = new Dictionary<string, string>();
            public string LyricHelper = string.Empty;
            public bool LyricsHelperBrackets = false;
            public int OtoEditor = 0;
            public string VLabelerPath = string.Empty;
            public string SetParamPath = string.Empty;
            public bool Beta = false;
            /// <summary>Ceiling for DiffSinger voice-color curves (0–1000). Default 100.</summary>
            public int VoiceColorCurveMax = 100;
            public bool RememberMid = false;
            public bool RememberUst = true;
            public bool RememberVsqx = true;
            public string WinePath = string.Empty;
            public string PhoneticAssistant = string.Empty;
            public string RecentOpenSingerDirectory = string.Empty;
            public string RecentOpenProjectDirectory = string.Empty;
            public bool LockUnselectedNotesPitch = true;
            public bool LockUnselectedNotesVibrato = true;
            public bool LockUnselectedNotesExpressions = true;
            [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<ShortcutBinding> Shortcuts { get; set; } = new List<ShortcutBinding> {
                // Playback & Selection
                new ShortcutBinding { ActionId = "PlayOrPause", KeyName = "Space", ModifiersName = "None" },
                new ShortcutBinding { ActionId = "PlaySelection", KeyName = "Space", ModifiersName = "Alt" },
                new ShortcutBinding { ActionId = "ClearSelection", KeyName = "Escape", ModifiersName = "None" },
                new ShortcutBinding { ActionId = "SelectAll", KeyName = "A", ModifiersName = "Control" },
                new ShortcutBinding { ActionId = "DeselectAll", KeyName = "D", ModifiersName = "Control" },

                // UI & Windows
                new ShortcutBinding { ActionId = "HideDetachedWindow", KeyName = "F4", ModifiersName = "Alt" },
                new ShortcutBinding { ActionId = "FullScreen", KeyName = "F11", ModifiersName = "None" },
                new ShortcutBinding { ActionId = "OpenPluginMenu", KeyName = "N", ModifiersName = "None" },

                // Lyrics
                new ShortcutBinding { ActionId = "EditLyrics", KeyName = "Enter", ModifiersName = "None" },

                // Tools
                new ShortcutBinding { ActionId = "ToolSelect1", KeyName = "D1", ModifiersName = "None" },
                new ShortcutBinding { ActionId = "ToolSelect2Main", KeyName = "D2", ModifiersName = "None" },
                new ShortcutBinding { ActionId = "ToolSelect2Alt", KeyName = "D2", ModifiersName = "Control" },
                new ShortcutBinding { ActionId = "ToolSelect3", KeyName = "D3", ModifiersName = "None" },
                new ShortcutBinding { ActionId = "ToolSelect4Main", KeyName = "D4", ModifiersName = "None" },
                new ShortcutBinding { ActionId = "ToolSelect4Overwrite", KeyName = "D4", ModifiersName = "Control" },
                new ShortcutBinding { ActionId = "ToolSelect5", KeyName = "D5", ModifiersName = "None" },
                new ShortcutBinding { ActionId = "ToolSelectPitchLine", KeyName = "D1", ModifiersName = "Shift" },
                new ShortcutBinding { ActionId = "ToolSelectPitchLineOverwrite", KeyName = "D1", ModifiersName = "Control, Shift" },
                new ShortcutBinding { ActionId = "ToolSelectPitchSCurve", KeyName = "D2", ModifiersName = "Shift" },
                new ShortcutBinding { ActionId = "ToolSelectPitchSine", KeyName = "D3", ModifiersName = "Shift" },
                new ShortcutBinding { ActionId = "ToolSelectPitchSmoothen", KeyName = "D4", ModifiersName = "Shift" },

                // Expressions
                new ShortcutBinding { ActionId = "ExpSelect1", KeyName = "D1", ModifiersName = "Alt" },
                new ShortcutBinding { ActionId = "ExpSelect2", KeyName = "D2", ModifiersName = "Alt" },
                new ShortcutBinding { ActionId = "ExpSelect3", KeyName = "D3", ModifiersName = "Alt" },
                new ShortcutBinding { ActionId = "ExpSelect4", KeyName = "D4", ModifiersName = "Alt" },
                new ShortcutBinding { ActionId = "ExpSelect5", KeyName = "D5", ModifiersName = "Alt" },
                new ShortcutBinding { ActionId = "ExpSelect6", KeyName = "D6", ModifiersName = "Alt" },
                new ShortcutBinding { ActionId = "ExpSelect7", KeyName = "D7", ModifiersName = "Alt" },
                new ShortcutBinding { ActionId = "ExpSelect8", KeyName = "D8", ModifiersName = "Alt" },
                new ShortcutBinding { ActionId = "ExpSelect9", KeyName = "D9", ModifiersName = "Alt" },
                new ShortcutBinding { ActionId = "ExpSelect10", KeyName = "D0", ModifiersName = "Alt" },

                // Toggles
                new ShortcutBinding { ActionId = "ToggleFinalPitch", KeyName = "R", ModifiersName = "None" },
                new ShortcutBinding { ActionId = "ToggleTips", KeyName = "T", ModifiersName = "None" },
                new ShortcutBinding { ActionId = "ToggleVibrato", KeyName = "U", ModifiersName = "None" },
                new ShortcutBinding { ActionId = "TogglePitch", KeyName = "I", ModifiersName = "None" },
                new ShortcutBinding { ActionId = "TogglePhoneme", KeyName = "O", ModifiersName = "None" },
                new ShortcutBinding { ActionId = "ToggleExpressions", KeyName = "L", ModifiersName = "None" },
                new ShortcutBinding { ActionId = "ToggleSnap", KeyName = "P", ModifiersName = "None" },
                new ShortcutBinding { ActionId = "OpenSnapMenu", KeyName = "P", ModifiersName = "Alt" },
                new ShortcutBinding { ActionId = "ToggleNoteParams", KeyName = "OemPipe", ModifiersName = "None" },
                new ShortcutBinding { ActionId = "TogglePlayTone", KeyName = "Y", ModifiersName = "None" },
                new ShortcutBinding { ActionId = "ToggleWaveform", KeyName = "W", ModifiersName = "None" },

                // Transposition
                new ShortcutBinding { ActionId = "TransposeUp", KeyName = "Up", ModifiersName = "None" },
                new ShortcutBinding { ActionId = "pianoroll.menu.notes.octaveup", KeyName = "Up", ModifiersName = "Control" },
                new ShortcutBinding { ActionId = "TransposeDown", KeyName = "Down", ModifiersName = "None" },
                new ShortcutBinding { ActionId = "pianoroll.menu.notes.octavedown", KeyName = "Down", ModifiersName = "Control" },

                // Note Movement & Sizing
                new ShortcutBinding { ActionId = "MoveCursorLeft", KeyName = "Left", ModifiersName = "None" },
                new ShortcutBinding { ActionId = "ResizeNotesLeft", KeyName = "Left", ModifiersName = "Alt" },
                new ShortcutBinding { ActionId = "MoveNotesLeft", KeyName = "Left", ModifiersName = "Control" },
                new ShortcutBinding { ActionId = "ExtendSelectionLeft", KeyName = "Left", ModifiersName = "Shift" },
                new ShortcutBinding { ActionId = "MoveCursorRight", KeyName = "Right", ModifiersName = "None" },
                new ShortcutBinding { ActionId = "ResizeNotesRight", KeyName = "Right", ModifiersName = "Alt" },
                new ShortcutBinding { ActionId = "MoveNotesRight", KeyName = "Right", ModifiersName = "Control" },
                new ShortcutBinding { ActionId = "ExtendSelectionRight", KeyName = "Right", ModifiersName = "Shift" },

                // Edit Operations
                new ShortcutBinding { ActionId = "Undo", KeyName = "Z", ModifiersName = "Control" },
                new ShortcutBinding { ActionId = "Redo", KeyName = "Y", ModifiersName = "Control" },
                new ShortcutBinding { ActionId = "Copy", KeyName = "C", ModifiersName = "Control" },
                new ShortcutBinding { ActionId = "Cut", KeyName = "X", ModifiersName = "Control" },
                new ShortcutBinding { ActionId = "Paste", KeyName = "V", ModifiersName = "Control" },
                new ShortcutBinding { ActionId = "PastePlain", KeyName = "V", ModifiersName = "Control, Shift" },
                new ShortcutBinding { ActionId = "PasteParameters", KeyName = "V", ModifiersName = "Alt" },
                new ShortcutBinding { ActionId = "InsertNote", KeyName = "Insert", ModifiersName = "None" },
                new ShortcutBinding { ActionId = "DeleteNotes", KeyName = "Delete", ModifiersName = "None" },
                new ShortcutBinding { ActionId = "MergeNotes", KeyName = "U", ModifiersName = "Control" },

                // Playhead & Timeline Navigation
                new ShortcutBinding { ActionId = "PlayheadHome", KeyName = "Home", ModifiersName = "None" },
                new ShortcutBinding { ActionId = "SelectToStart", KeyName = "Home", ModifiersName = "Shift" },
                new ShortcutBinding { ActionId = "PlayheadEnd", KeyName = "End", ModifiersName = "None" },
                new ShortcutBinding { ActionId = "SelectToEnd", KeyName = "End", ModifiersName = "Shift" },
                new ShortcutBinding { ActionId = "PlayheadLeft", KeyName = "OemOpenBrackets", ModifiersName = "None" },
                new ShortcutBinding { ActionId = "PlayheadToSelectionStart", KeyName = "OemOpenBrackets", ModifiersName = "Control" },
                new ShortcutBinding { ActionId = "PlayheadToViewStart", KeyName = "OemOpenBrackets", ModifiersName = "Shift" },
                new ShortcutBinding { ActionId = "PlayheadRight", KeyName = "OemCloseBrackets", ModifiersName = "None" },
                new ShortcutBinding { ActionId = "PlayheadToSelectionEnd", KeyName = "OemCloseBrackets", ModifiersName = "Control" },
                new ShortcutBinding { ActionId = "PlayheadToViewEnd", KeyName = "OemCloseBrackets", ModifiersName = "Shift" },

                // Scrolling & Zooming
                new ShortcutBinding { ActionId = "ScrollLeft", KeyName = "A", ModifiersName = "None" },
                new ShortcutBinding { ActionId = "ScrollRight", KeyName = "D", ModifiersName = "None" },
                new ShortcutBinding { ActionId = "ScrollUp", KeyName = "W", ModifiersName = "Alt" },
                new ShortcutBinding { ActionId = "ScrollDown", KeyName = "S", ModifiersName = "Alt" },
                new ShortcutBinding { ActionId = "ZoomIn", KeyName = "E", ModifiersName = "None" },
                new ShortcutBinding { ActionId = "ZoomOut", KeyName = "Q", ModifiersName = "None" },

                // Track & Project Operations
                new ShortcutBinding { ActionId = "SaveProject", KeyName = "S", ModifiersName = "Control" },
                new ShortcutBinding { ActionId = "SoloTrack", KeyName = "S", ModifiersName = "Shift" },
                new ShortcutBinding { ActionId = "MuteTrack", KeyName = "M", ModifiersName = "Shift" },
                new ShortcutBinding { ActionId = "FocusSelection", KeyName = "F", ModifiersName = "None" },
                new ShortcutBinding { ActionId = "SearchNote", KeyName = "F", ModifiersName = "Control" },

                // Parts Navigation
                new ShortcutBinding { ActionId = "MoveToNextPartUp", KeyName = "PageUp", ModifiersName = "None" },
                new ShortcutBinding { ActionId = "MoveToNextPartDown", KeyName = "PageDown", ModifiersName = "None" },
                
                new ShortcutBinding { ActionId = "pianoroll.menu.notes.loadrenderedpitch", KeyName = "R", ModifiersName = "Control" },
                new ShortcutBinding { ActionId = "pianoroll.menu.notes.refreshrealcurves", KeyName = "R", ModifiersName = "Control, Shift" },
                new ShortcutBinding { ActionId = "pianoroll.menu.notes.bakepitch", KeyName = "K", ModifiersName = "Alt" },

                // Tails and Overlap
                new ShortcutBinding { ActionId = "pianoroll.menu.notes.addtaildash", KeyName = "OemMinus", ModifiersName = "Alt" },
                new ShortcutBinding { ActionId = "pianoroll.menu.notes.addtailrest", KeyName = "R", ModifiersName = "Alt, Shift" },
                new ShortcutBinding { ActionId = "pianoroll.menu.notes.removetaildash", KeyName = "OemMinus", ModifiersName = "Control, Alt" },
                new ShortcutBinding { ActionId = "pianoroll.menu.notes.removetailrest", KeyName = "R", ModifiersName = "Control, Alt" },
                new ShortcutBinding { ActionId = "pianoroll.menu.notes.fixoverlap", KeyName = "F", ModifiersName = "Alt" },
                new ShortcutBinding { ActionId = "pianoroll.menu.notes.autolegato", KeyName = "A", ModifiersName = "Alt" },

                // Common notes
                new ShortcutBinding { ActionId = "pianoroll.menu.notes.commonnotecopy", KeyName = "C", ModifiersName = "Control, Shift" },
                new ShortcutBinding { ActionId = "pianoroll.menu.notes.commonnotepaste", KeyName = "P", ModifiersName = "Control, Shift" },

                // Timings
                new ShortcutBinding { ActionId = "pianoroll.menu.notes.randomizetiming", KeyName = "T", ModifiersName = "Alt" },
                new ShortcutBinding { ActionId = "pianoroll.menu.notes.randomizeoffset", KeyName = "T", ModifiersName = "Control, Alt" },

                // Lang
                new ShortcutBinding { ActionId = "pianoroll.menu.lyrics.romajitohiragana", KeyName = "J", ModifiersName = "Control, Shift" },
                new ShortcutBinding { ActionId = "pianoroll.menu.lyrics.hiraganatoromaji", KeyName = "J", ModifiersName = "Control, Alt" },
                new ShortcutBinding { ActionId = "pianoroll.menu.lyrics.javcvtocv", KeyName = "K", ModifiersName = "Control, Shift" },
                new ShortcutBinding { ActionId = "pianoroll.menu.notes.hanzitopinyin", KeyName = "H", ModifiersName = "Control, Alt" },

                // Suffixes and Phonetic Hints
                new ShortcutBinding { ActionId = "pianoroll.menu.lyrics.removetonesuffix", KeyName = "S", ModifiersName = "Control, Alt" },
                new ShortcutBinding { ActionId = "pianoroll.menu.lyrics.removelettersuffix", KeyName = "S", ModifiersName = "Control, Shift" },
                new ShortcutBinding { ActionId = "pianoroll.menu.lyrics.movesuffixtovoicecolor", KeyName = "C", ModifiersName = "Control, Alt" },
                new ShortcutBinding { ActionId = "pianoroll.menu.lyrics.removephonetichint", KeyName = "P", ModifiersName = "Control, Alt" },

                // Dash and Slur
                new ShortcutBinding { ActionId = "pianoroll.menu.lyrics.dashtoplus", KeyName = "OemPlus", ModifiersName = "Alt" },
                new ShortcutBinding { ActionId = "pianoroll.menu.lyrics.dashtoplustilda", KeyName = "OemPlus", ModifiersName = "Control, Alt" },
                new ShortcutBinding { ActionId = "pianoroll.menu.lyrics.insertslur", KeyName = "I", ModifiersName = "Alt" },

                // Reset
                new ShortcutBinding { ActionId = "pianoroll.menu.notes.reset.all", KeyName = "Delete", ModifiersName = "Control, Shift" },
                new ShortcutBinding { ActionId = "pianoroll.menu.notes.reset.allparameters", KeyName = "I", ModifiersName = "Control, Alt" },
                new ShortcutBinding { ActionId = "pianoroll.menu.notes.reset.exps", KeyName = "E", ModifiersName = "Control, Shift" },
                new ShortcutBinding { ActionId = "pianoroll.menu.notes.clear.vibratos", KeyName = "V", ModifiersName = "Control, Alt" },
                new ShortcutBinding { ActionId = "pianoroll.menu.notes.reset.vibratos", KeyName = "U", ModifiersName = "Control, Shift" },
                new ShortcutBinding { ActionId = "pianoroll.menu.notes.reset.pitchbends", KeyName = "B", ModifiersName = "Control, Alt" },
                new ShortcutBinding { ActionId = "pianoroll.menu.notes.reset.phonemetimings", KeyName = "T", ModifiersName = "Control, Shift" },
                new ShortcutBinding { ActionId = "pianoroll.menu.notes.reset.aliases", KeyName = "A", ModifiersName = "Control, Alt" },

                // other toggles
                new ShortcutBinding { ActionId = "Lock Pitch Points", KeyName = "L", ModifiersName = "Control, Shift" },
                new ShortcutBinding { ActionId = "Lock Vibrato", KeyName = "U", ModifiersName = "Control, Alt" },
                new ShortcutBinding { ActionId = "Lock Expressions", KeyName = "E", ModifiersName = "Control, Alt" },
                new ShortcutBinding { ActionId = "Show Portrait", KeyName = "P", ModifiersName = "Alt, Shift" },
                new ShortcutBinding { ActionId = "Show Ghost Notes", KeyName = "G", ModifiersName = "Alt" },
                new ShortcutBinding { ActionId = "Use Track Color", KeyName = "C", ModifiersName = "Alt" },
                new ShortcutBinding { ActionId = "Detach Piano Roll", KeyName = "D", ModifiersName = "Alt, Shift" },
                new ShortcutBinding { ActionId = "Hide Piano Roll", KeyName = "H", ModifiersName = "Alt, Shift" },
                new ShortcutBinding { ActionId = "lyricsreplace.replace", KeyName = "H", ModifiersName = "Control" },
                new ShortcutBinding { ActionId = "Quantize Notes", KeyName = "Q", ModifiersName = "Alt" },
                new ShortcutBinding { ActionId = "Randomize Tuning", KeyName = "R", ModifiersName = "Alt" },
                new ShortcutBinding { ActionId = "Lengthen Crossfade", KeyName = "L", ModifiersName = "Alt" },
                new ShortcutBinding { ActionId = "Add Breath", KeyName = "B", ModifiersName = "Alt" },
                new ShortcutBinding { ActionId = "Edit Note Defaults", KeyName = "N", ModifiersName = "Alt" },
                new ShortcutBinding { ActionId = "Open Singers Window", KeyName = "O", ModifiersName = "Alt" },
                new ShortcutBinding { ActionId = "Open Expressions", KeyName = "E", ModifiersName = "Alt" },
            };
            public bool LyricLivePreview = true;
            public bool LyricApplySelectionOnly = true;
            public bool VoicebankPublishUseIgnore = true;
            public string VoicebankPublishIgnores = @"#Adobe Audition
*.pkf

#UTAU Engines
*.ctspec
*.d4c
*.dio
*.frc
*.frt
#*.frq
*.harvest
*.lessaudio
*.llsm
*.mrq
*.pitchtier
*.pkf
*.platinum
*.pmk
*.sc.npz
*.star
*.uspec
*.vs4ufrq

#UTAU related tools
\$read
*.setParam-Scache
*.lbp
*.lbp.caches/*

#OpenUtau
errors.txt
";
            public string RecoveryPath = string.Empty;
            public bool DetachPianoRoll = false;

            // ----- Mix FX (post-processing) -----
            // Per-track FX state lives in UTrack.MixFx and the project ustx.
            // Preferences only stores the global "apply on mixdown export" toggle
            // and the user preset library (named full-rack snapshots).
            public bool MixFxApplyOnExportMixdown = true;
            public List<MixFxUserPreset> MixFxUserPresets = new List<MixFxUserPreset>();

            // Legacy
            [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
            public int? Theme;

            public void MigrateRealTimePitchMode() {
                if (RealTimePitchGeneration && RealTimePitchMode == (int)LivePitchMode.Off) {
                    RealTimePitchMode = (int)LivePitchMode.Normal;
                }
                RealTimePitchGeneration = false;
                if (RealTimePitchMode < (int)LivePitchMode.Off
                    || RealTimePitchMode > (int)LivePitchMode.SuperFast) {
                    RealTimePitchMode = (int)LivePitchMode.Off;
                }
            }
        }

        /// <summary>
        /// Named full-rack FX snapshot (EQ + Comp + Reverb together).
        /// Persisted in Preferences so users can save and recall their own presets.
        /// </summary>
        public class MixFxUserPreset {
            public string Name { get; set; } = string.Empty;
            public Ustx.UMixFx Fx { get; set; } = new Ustx.UMixFx();
        }
    }
}
