using System.Collections.Generic;
using GrandStrategy.Game.Audio;
using GrandStrategy.Game.Map;
using GrandStrategy.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace GrandStrategy.Game.UI
{
    /// <summary>
    /// The in-game interface, built entirely in code with UI Toolkit:
    /// top bar (nation, date, speed), nation-select banner, province/nation panel,
    /// map modes, tooltip, notifications, settings and game-over screens.
    /// </summary>
    public sealed class HudController : MonoBehaviour
    {
        const int MaxToasts = 4;
        const long ToastMilliseconds = 6000;

        GameRoot _game;
        UIDocument _document;
        VisualElement _root;

        VisualElement _topBar;
        VisualElement _playerSwatch;
        Label _playerName;
        Label _playerStats;
        Label _dateLabel;
        Button _pauseButton;
        readonly VisualElement[] _speedPips = new VisualElement[GameClock.MaxSpeed];

        VisualElement _banner;
        VisualElement _info;
        VisualElement _modeBar;
        readonly Dictionary<MapMode, Button> _modeButtons = new Dictionary<MapMode, Button>();
        VisualElement _hints;
        Label _tooltip;
        VisualElement _toasts;
        VisualElement _settings;
        Label _diagnostics;
        VisualElement _gameOver;
        VisualElement _loading;
        Label _loadingText;

        /// <summary>Full-screen layer behind every panel, used for country names on the map.</summary>
        public VisualElement MapLabelLayer { get; private set; }

        string _shownDate;
        int _shownSpeed = -1;
        bool? _shownPaused;
        float _nextStatsRefresh;

        public void Initialize(GameRoot game)
        {
            _game = game;
            CreateDocument();
            BuildTopBar();
            BuildBanner();
            BuildInfoPanel();
            BuildMapModes();
            BuildHints();
            BuildTooltip();
            BuildToasts();
            BuildSettings();
            BuildGameOver();
            BuildLoading();
            OnPhaseChanged();
        }

        // ------------------------------------------------------------------ setup

        void CreateDocument()
        {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.name = "HUD Panel Settings";
            settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("UI/DefaultRuntimeTheme");
            if (settings.themeStyleSheet == null)
                Debug.LogWarning("UI theme Resources/UI/DefaultRuntimeTheme.tss not found; UI may render without default styles.");
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1920, 1080);
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = 0.5f;
            settings.sortingOrder = 100;

            var go = new GameObject("HUD Document");
            go.transform.SetParent(transform, false);
            go.SetActive(false); // assign settings before OnEnable builds the panel
            _document = go.AddComponent<UIDocument>();
            _document.panelSettings = settings;
            go.SetActive(true);

            _root = _document.rootVisualElement;
            _root.pickingMode = PickingMode.Ignore;
            Ui.Fill(_root);

            // Added first so it sits behind every panel.
            MapLabelLayer = Ui.Element("map-labels");
            Ui.Fill(MapLabelLayer);
            _root.Add(MapLabelLayer);
        }

        void BuildTopBar()
        {
            _topBar = Ui.Element("top-bar", true);
            Ui.Absolute(_topBar, 0, 0, 0);
            _topBar.style.height = 58;
            _topBar.style.flexDirection = FlexDirection.Row;
            _topBar.style.alignItems = Align.Center;
            _topBar.style.paddingLeft = 18;
            _topBar.style.paddingRight = 14;
            _topBar.style.backgroundColor = Ui.PanelColor;
            _topBar.style.borderBottomWidth = 2;
            _topBar.style.borderBottomColor = Ui.PanelBorder;

            _playerSwatch = Ui.Swatch(Color.gray, 30, 20);
            _playerSwatch.style.marginRight = 10;
            _topBar.Add(_playerSwatch);

            _playerName = Ui.Label("Choose your nation", 21, Ui.Text, true);
            _playerName.style.marginRight = 22;
            _topBar.Add(_playerName);

            _playerStats = Ui.Label("", 15, Ui.TextDim);
            _topBar.Add(_playerStats);

            var right = Ui.Row();
            right.style.marginLeft = new StyleLength(StyleKeyword.Auto);
            _topBar.Add(right);

            _dateLabel = Ui.Label("", 20, Ui.Accent, true);
            _dateLabel.style.minWidth = 200;
            _dateLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            _dateLabel.style.marginRight = 14;
            right.Add(_dateLabel);

            right.Add(Ui.Button("-", () => _game.SlowDown(), false, 18));

            var pips = Ui.Row();
            pips.style.marginLeft = 4;
            pips.style.marginRight = 4;
            for (int i = 0; i < _speedPips.Length; i++)
            {
                var pip = Ui.Element();
                pip.style.width = 8;
                pip.style.height = 10 + i * 3;
                pip.style.marginLeft = 2;
                pip.style.alignSelf = Align.FlexEnd;
                Ui.Radius(pip, 2);
                _speedPips[i] = pip;
                pips.Add(pip);
            }
            right.Add(pips);

            right.Add(Ui.Button("+", () => _game.SpeedUp(), false, 18));

            _pauseButton = Ui.Button("Play", () => _game.TogglePause(), true, 16);
            _pauseButton.style.minWidth = 92;
            right.Add(_pauseButton);

            var settingsButton = Ui.Button("Settings", ToggleSettings);
            settingsButton.style.marginLeft = 12;
            right.Add(settingsButton);

            _root.Add(_topBar);
        }

        void BuildBanner()
        {
            var holder = Ui.Element("banner-holder");
            Ui.Absolute(holder, 0, 78, 0);
            holder.style.alignItems = Align.Center;

            _banner = Ui.Panel(16);
            _banner.style.alignItems = Align.Center;
            _banner.style.paddingLeft = 32;
            _banner.style.paddingRight = 32;
            _banner.Add(Ui.Label("CHOOSE YOUR NATION", 26, Ui.Accent, true));
            var sub = Ui.Label("Click any country on the map, then press  Play as ...  in the panel on the left.", 16, Ui.TextDim);
            sub.style.marginTop = 4;
            _banner.Add(sub);
            holder.Add(_banner);
            _root.Add(holder);
        }

        void BuildInfoPanel()
        {
            _info = Ui.Panel(16);
            Ui.Absolute(_info, 16, 74);
            _info.style.width = 370;
            Ui.Show(_info, false);
            _root.Add(_info);
        }

        void BuildMapModes()
        {
            _modeBar = Ui.Panel(6);
            Ui.Absolute(_modeBar, null, null, 16, 16);
            _modeBar.style.flexDirection = FlexDirection.Row;
            _modeBar.style.alignItems = Align.Center;
            var label = Ui.Label("Map", 14, Ui.TextDim, true);
            label.style.marginLeft = 8;
            label.style.marginRight = 6;
            _modeBar.Add(label);
            AddModeButton(MapMode.Political, "Political  F1");
            AddModeButton(MapMode.Population, "Population  F2");
            AddModeButton(MapMode.Wealth, "Wealth  F3");
            _root.Add(_modeBar);
        }

        void AddModeButton(MapMode mode, string text)
        {
            var b = Ui.Button(text, () => _game.SetMapMode(mode), false, 14);
            _modeButtons[mode] = b;
            _modeBar.Add(b);
        }

        void BuildHints()
        {
            _hints = Ui.Label(
                "Space pause   1-5 / +- speed   WASD or drag to move   Scroll or Q/E to zoom   F1-F3 map modes   M mute   Esc close",
                13, new Color(1, 1, 1, 0.55f));
            Ui.Absolute(_hints, 18, null, null, 20);
            _root.Add(_hints);
        }

        void BuildTooltip()
        {
            _tooltip = Ui.Label("", 14, Ui.Text);
            _tooltip.style.position = Position.Absolute;
            _tooltip.style.backgroundColor = new Color(0.03f, 0.04f, 0.06f, 0.92f);
            Ui.Border(_tooltip, Ui.PanelBorder, 1);
            Ui.Radius(_tooltip, 4);
            _tooltip.style.paddingLeft = 8;
            _tooltip.style.paddingRight = 8;
            _tooltip.style.paddingTop = 4;
            _tooltip.style.paddingBottom = 4;
            _tooltip.style.whiteSpace = WhiteSpace.NoWrap;
            Ui.Show(_tooltip, false);
            _root.Add(_tooltip);
        }

        void BuildToasts()
        {
            _toasts = Ui.Column();
            Ui.Absolute(_toasts, null, 74, 16);
            _toasts.style.width = 360;
            _root.Add(_toasts);
        }

        void BuildSettings()
        {
            _settings = Ui.Element("settings-overlay", true);
            Ui.Fill(_settings);
            _settings.style.backgroundColor = new Color(0, 0, 0, 0.55f);
            _settings.style.alignItems = Align.Center;
            _settings.style.justifyContent = Justify.Center;

            var panel = Ui.Panel(22);
            panel.style.width = 460;
            panel.Add(Ui.Label("Settings", 24, Ui.Accent, true));
            panel.Add(Ui.DividerLine());

            var audio = _game.Audio;
            panel.Add(VolumeSlider("Music volume", audio.MusicVolume, v => audio.MusicVolume = v));
            panel.Add(VolumeSlider("Effects volume", audio.SfxVolume, v =>
            {
                audio.SfxVolume = v;
                audio.Play(Sfx.UiClick);
            }));

            var mute = new Toggle("Mute all sound (M)") { value = audio.Muted };
            mute.name = "mute-toggle";
            mute.focusable = false;
            mute.labelElement.style.color = Ui.Text;
            mute.labelElement.style.minWidth = 160;
            mute.style.marginTop = 10;
            mute.RegisterValueChangedCallback(e => audio.Muted = e.newValue);
            panel.Add(mute);

            var note = Ui.Label("All music and sound effects are generated in-game. Drop your own files into " +
                                "Assets/Resources/Audio to replace them (see README.txt there).", 13, Ui.TextDim);
            note.style.marginTop = 14;
            panel.Add(note);

            _diagnostics = Ui.Label("", 12, new Color(1, 1, 1, 0.45f));
            _diagnostics.style.marginTop = 12;
            panel.Add(_diagnostics);

            var close = Ui.Button("Close", ToggleSettings, true);
            close.style.marginTop = 18;
            close.style.alignSelf = Align.FlexEnd;
            panel.Add(close);

            _settings.Add(panel);
            Ui.Show(_settings, false);
            _root.Add(_settings);
        }

        static VisualElement VolumeSlider(string label, float value, System.Action<float> onChange)
        {
            var slider = new Slider(label, 0f, 1f) { value = value, focusable = false };
            slider.labelElement.style.color = Ui.Text;
            slider.labelElement.style.minWidth = 160;
            slider.style.marginTop = 8;
            slider.RegisterValueChangedCallback(e => onChange(e.newValue));
            return slider;
        }

        void BuildGameOver()
        {
            _gameOver = Ui.Element("game-over", true);
            Ui.Fill(_gameOver);
            _gameOver.style.backgroundColor = new Color(0.1f, 0f, 0f, 0.65f);
            _gameOver.style.alignItems = Align.Center;
            _gameOver.style.justifyContent = Justify.Center;
            Ui.Show(_gameOver, false);
            _root.Add(_gameOver);
        }

        void BuildLoading()
        {
            _loading = Ui.Element("loading", true);
            Ui.Fill(_loading);
            _loading.style.backgroundColor = new Color(0.04f, 0.06f, 0.09f, 1f);
            _loading.style.alignItems = Align.Center;
            _loading.style.justifyContent = Justify.Center;
            _loading.Add(Ui.Label("GRAND STRATEGY", 40, Ui.Accent, true));
            _loadingText = Ui.Label("Loading the world...", 18, Ui.TextDim);
            _loadingText.style.marginTop = 10;
            _loadingText.style.maxWidth = 900;
            _loadingText.style.unityTextAlign = TextAnchor.MiddleCenter;
            _loading.Add(_loadingText);
            _root.Add(_loading);
        }

        // ------------------------------------------------------------------ public API

        public void ShowLoading(string message)
        {
            _loadingText.text = message;
            _loadingText.style.color = Ui.TextDim;
            Ui.Show(_loading, true);
        }

        public void ShowError(string message)
        {
            _loadingText.text = message;
            _loadingText.style.color = Ui.Bad;
            Ui.Show(_loading, true);
        }

        public void HideLoading() => Ui.Show(_loading, false);

        /// <summary>True if the screen position is over a UI element that should block map input.</summary>
        public bool IsPointerOverUI(Vector2 screenPosition)
        {
            var panel = _root?.panel;
            if (panel == null)
                return false;
            var panelPos = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screenPosition.x, Screen.height - screenPosition.y));
            // Only elements inside this HUD count; the panel's own root is not a blocker.
            var picked = panel.Pick(panelPos);
            for (var e = picked; e != null; e = e.parent)
                if (e == _root)
                    return picked != _root;
            return false;
        }

        public void SetTooltip(string text, Vector2 screenPosition)
        {
            if (string.IsNullOrEmpty(text) || _root.panel == null)
            {
                Ui.Show(_tooltip, false);
                return;
            }
            var p = RuntimePanelUtils.ScreenToPanel(_root.panel, new Vector2(screenPosition.x, Screen.height - screenPosition.y));
            _tooltip.text = text;
            _tooltip.style.left = p.x + 18;
            _tooltip.style.top = p.y + 14;
            Ui.Show(_tooltip, true);
        }

        public void Toast(string title, string body, Color? accent = null)
        {
            while (_toasts.childCount >= MaxToasts)
                _toasts.RemoveAt(0);

            var toast = Ui.Panel(12);
            toast.pickingMode = PickingMode.Ignore;
            toast.style.marginBottom = 8;
            toast.style.borderLeftWidth = 4;
            toast.style.borderLeftColor = accent ?? Ui.Accent;
            toast.Add(Ui.Label(title, 16, accent ?? Ui.Accent, true));
            if (!string.IsNullOrEmpty(body))
            {
                var b = Ui.Label(body, 14, Ui.Text);
                b.style.marginTop = 3;
                toast.Add(b);
            }
            _toasts.Add(toast);
            toast.schedule.Execute(() => toast.RemoveFromHierarchy()).StartingIn(ToastMilliseconds);
        }

        public void ToggleSettings()
        {
            bool show = _settings.style.display == DisplayStyle.None;
            if (show)
                _diagnostics.text = $"Unity {Application.unityVersion}  |  map: {(_game.Map == null ? "-" : _game.Map.UsesShader ? "shader" : "CPU fallback")}" +
                                    $"  |  input: {GameInput.BackendName}  |  {SystemInfo.graphicsDeviceType}";
            Ui.Show(_settings, show);
        }

        /// <summary>Closes the top-most overlay. Returns false if nothing was open.</summary>
        public bool CloseOverlay()
        {
            if (_settings.style.display != DisplayStyle.None)
            {
                Ui.Show(_settings, false);
                return true;
            }
            return false;
        }

        public void OnPhaseChanged()
        {
            var phase = _game.Phase;
            bool playing = phase == GamePhase.Playing;
            Ui.Show(_banner, phase == GamePhase.NationSelect);
            Ui.Show(_modeBar, playing || phase == GamePhase.NationSelect);
            Ui.Show(_hints, playing || phase == GamePhase.NationSelect);
            Ui.Show(_gameOver, phase == GamePhase.GameOver);
            if (phase == GamePhase.GameOver)
                BuildGameOverContent();
            _shownDate = null;
            _shownSpeed = -1;
            _shownPaused = null;
            RefreshModeButtons();
            RefreshInfo();
            RefreshPlayer();
        }

        public void RefreshModeButtons()
        {
            if (_game.Map == null)
                return;
            foreach (var kv in _modeButtons)
            {
                bool active = kv.Key == _game.Map.Mode;
                Ui.Border(kv.Value, active ? Ui.Accent : new Color(1f, 1f, 1f, 0.12f), active ? 2 : 1);
                kv.Value.style.color = active ? Ui.Accent : Ui.Text;
            }
        }

        // ------------------------------------------------------------------ per-frame

        void Update()
        {
            if (_game == null || _game.World == null)
                return;
            var clock = _game.World.Clock;

            string date = clock.Date.ToString();
            if (date != _shownDate)
            {
                _shownDate = date;
                _dateLabel.text = date;
            }

            if (clock.Speed != _shownSpeed || clock.IsPaused != _shownPaused)
            {
                _shownSpeed = clock.Speed;
                _shownPaused = clock.IsPaused;
                for (int i = 0; i < _speedPips.Length; i++)
                    _speedPips[i].style.backgroundColor = i < clock.Speed
                        ? (clock.IsPaused ? new Color(0.55f, 0.55f, 0.55f) : Ui.Accent)
                        : new Color(1f, 1f, 1f, 0.12f);
                _pauseButton.text = clock.IsPaused ? "Play" : "Pause";
                _pauseButton.SetEnabled(_game.Phase == GamePhase.Playing);
            }

            if (Time.unscaledTime >= _nextStatsRefresh)
            {
                _nextStatsRefresh = Time.unscaledTime + 0.5f;
                RefreshPlayer();
            }
        }

        void RefreshPlayer()
        {
            var world = _game.World;
            var player = world?.Player;
            if (player == null || _game.Phase == GamePhase.NationSelect)
            {
                _playerName.text = "Choose your nation";
                _playerSwatch.style.backgroundColor = new Color(0.4f, 0.4f, 0.4f);
                _playerStats.text = "";
                return;
            }
            _playerName.text = player.Name;
            _playerSwatch.style.backgroundColor = Ui.ToColor(player.ColorRgb);
            _playerStats.text = player.IsEliminated
                ? "Fallen"
                : $"Population {Ui.FormatPopulation(world.PopulationOf(player))}     GDP {Ui.FormatMoneyMillions(world.GdpOf(player))}" +
                  $"     Provinces {player.ProvinceIds.Count}     Morale {Ui.FormatPercent(player.Morale)}";
        }

        // ------------------------------------------------------------------ info panel

        /// <summary>Rebuilds the province / nation panel for the current selection.</summary>
        public void RefreshInfo()
        {
            _info.Clear();
            var world = _game.World;
            var province = world?.GetProvince(_game.SelectedProvinceId);
            if (province == null || _game.Phase == GamePhase.GameOver)
            {
                Ui.Show(_info, false);
                return;
            }
            Ui.Show(_info, true);

            var header = Ui.Row();
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.alignItems = Align.FlexStart;
            var title = Ui.Label(province.Name, 22, Ui.Text, true);
            title.style.flexShrink = 1;
            header.Add(title);
            var close = Ui.Button("X", () => _game.SelectProvince(0), false, 13);
            close.style.paddingLeft = 8;
            close.style.paddingRight = 8;
            header.Add(close);
            _info.Add(header);

            var owner = world.GetCountry(province.OwnerTag);
            bool isCapital = owner != null && owner.CapitalProvinceId == province.Id;
            var ownerRow = Ui.Row();
            ownerRow.style.marginTop = 4;
            ownerRow.Add(Ui.Swatch(owner != null ? Ui.ToColor(owner.ColorRgb) : Color.gray));
            var ownerName = Ui.Label(owner != null ? owner.Name : "Unclaimed land", 17, Ui.Text);
            ownerName.style.marginLeft = 8;
            ownerRow.Add(ownerName);
            if (isCapital)
            {
                var tag = Ui.Label("CAPITAL", 12, Ui.Accent, true);
                tag.style.marginLeft = 10;
                Ui.Border(tag, Ui.Accent, 1);
                Ui.Radius(tag, 3);
                tag.style.paddingLeft = 5;
                tag.style.paddingRight = 5;
                ownerRow.Add(tag);
            }
            _info.Add(ownerRow);

            _info.Add(Ui.DividerLine());
            _info.Add(Ui.StatRow("Population", Ui.FormatPopulation(province.Population)));
            _info.Add(Ui.StatRow("GDP (est.)", Ui.FormatMoneyMillions(province.GdpMillions)));
            _info.Add(Ui.StatRow("Coastal", province.IsCoastal ? "Yes" : "No"));
            _info.Add(Ui.StatRow("Neighbouring provinces", province.Neighbors.Count.ToString()));

            if (owner == null)
                return;

            _info.Add(Ui.DividerLine());
            _info.Add(Ui.Label(owner.FormalName.ToUpperInvariant(), 13, Ui.Accent, true));
            var capital = world.GetProvince(owner.CapitalProvinceId);
            _info.Add(Ui.StatRow("Capital", string.IsNullOrEmpty(owner.CapitalName) ? capital?.Name ?? "-" : owner.CapitalName));
            _info.Add(Ui.StatRow("Population", Ui.FormatPopulation(world.PopulationOf(owner))));
            _info.Add(Ui.StatRow("GDP", Ui.FormatMoneyMillions(world.GdpOf(owner))));
            _info.Add(Ui.StatRow("Provinces", owner.ProvinceIds.Count.ToString()));
            _info.Add(Ui.StatRow("Region", owner.Subregion));
            var morale = owner.Morale;
            _info.Add(Ui.StatRow("Morale", Ui.FormatPercent(morale), morale < 0.999 ? Ui.Bad : morale > 1.001 ? Ui.Good : Ui.Text));
            foreach (var m in owner.Modifiers)
            {
                var line = Ui.Label($"{m.Name}  ({(m.Morale >= 0 ? "+" : "")}{Ui.FormatPercent(m.Morale)} morale, " +
                                    $"{(m.IsPermanent ? "permanent" : m.DaysRemaining + " days")})",
                    13, m.Morale < 0 ? Ui.Bad : Ui.Good);
                line.tooltip = m.Description;
                _info.Add(line);
            }

            BuildActions(owner);
        }

        void BuildActions(Country owner)
        {
            var world = _game.World;
            if (_game.Phase == GamePhase.NationSelect)
            {
                var play = Ui.Button($"Play as {owner.Name}", () => _game.ChooseNation(owner.Tag), true, 17);
                play.style.marginTop = 16;
                _info.Add(play);
                return;
            }
            if (_game.Phase != GamePhase.Playing || world.Player == null || world.Player.IsEliminated)
                return;

            _info.Add(Ui.DividerLine());
            if (owner.Tag == world.PlayerTag)
            {
                _info.Add(Ui.Label("This is your nation.", 14, Ui.Good, true));
                return;
            }

            // Developer test for the Capital Battle rules until armies and the battle scene exist.
            _info.Add(Ui.Label("CAPITAL BATTLE TEST", 13, Ui.Accent, true));
            var explain = Ui.Label(
                "Real Capital Battles arrive with the war milestones. These buttons apply the outcome rules now, " +
                $"so you can see them on the map. Winning while losing {Ui.FormatPercent(world.WarRules.heavyLossThreshold)} " +
                "or more of your troops only takes the capital.", 12, Ui.TextDim);
            explain.style.marginTop = 2;
            explain.style.marginBottom = 6;
            _info.Add(explain);

            _info.Add(Ui.Label($"We attack {owner.Name}:", 13, Ui.Text));
            var ours = Ui.Row();
            ours.style.flexWrap = Wrap.Wrap;
            ours.Add(TestButton("Win, 40% lost", true, CapitalBattleTestOutcome.CleanWin, owner));
            ours.Add(TestButton("Win, 72% lost", true, CapitalBattleTestOutcome.CostlyWin, owner));
            ours.Add(TestButton("Repelled", true, CapitalBattleTestOutcome.Repelled, owner));
            _info.Add(ours);

            _info.Add(Ui.Label($"{owner.Name} attacks us:", 13, Ui.Text));
            var theirs = Ui.Row();
            theirs.style.flexWrap = Wrap.Wrap;
            theirs.Add(TestButton("They win, 30% lost", false, CapitalBattleTestOutcome.CleanWin, owner));
            theirs.Add(TestButton("They win, 80% lost", false, CapitalBattleTestOutcome.CostlyWin, owner));
            theirs.Add(TestButton("We hold", false, CapitalBattleTestOutcome.Repelled, owner));
            _info.Add(theirs);
        }

        Button TestButton(string text, bool playerAttacks, CapitalBattleTestOutcome outcome, Country other)
        {
            var b = Ui.Button(text, () => _game.RunCapitalBattleTest(other.Tag, playerAttacks, outcome), false, 13);
            b.style.paddingLeft = 8;
            b.style.paddingRight = 8;
            b.SetEnabled(!_game.BattleInProgress);
            return b;
        }

        void BuildGameOverContent()
        {
            _gameOver.Clear();
            var panel = Ui.Panel(28);
            panel.style.alignItems = Align.Center;
            panel.style.width = 560;
            panel.Add(Ui.Label("YOUR NATION HAS FALLEN", 30, Ui.Bad, true));
            var name = _game.World.Player?.Name ?? "Your nation";
            var text = Ui.Label($"{name} has been annexed. History will remember the defence of its capital.", 16, Ui.Text);
            text.style.marginTop = 10;
            text.style.unityTextAlign = TextAnchor.MiddleCenter;
            panel.Add(text);
            var again = Ui.Button("Choose another nation", () => _game.ReturnToNationSelect(), true, 17);
            again.style.marginTop = 20;
            panel.Add(again);
            _gameOver.Add(panel);
        }
    }
}
