using System;
using System.Collections.Generic;
using GrandStrategy.Game.Map;
using GrandStrategy.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace GrandStrategy.Game.UI
{
    /// <summary>Map-mode buttons (bottom right).</summary>
    public sealed class MapModeBar
    {
        readonly GameRoot _game;
        readonly Dictionary<MapMode, Button> _buttons = new Dictionary<MapMode, Button>();

        public VisualElement Root { get; }

        public static readonly (MapMode mode, string icon, string name, string help)[] Modes =
        {
            (MapMode.Political, "political", "Political", "Who owns what. (F1)"),
            (MapMode.Diplomatic, "diplomacy", "Diplomatic", "Relations with your nation: allies blue, friends green, rivals red, sanctions dark red. (F2)"),
            (MapMode.Wealth, "wealth", "Wealth", "GDP per person. (F3)"),
            (MapMode.Growth, "growth", "Growth", "Real GDP growth this year. (F4)"),
            (MapMode.Stability, "stability", "Stability", "How stable each government is. (F5)"),
            (MapMode.Government, "government", "Government", "Blue: democracies. Yellow: hybrid regimes. Red: authoritarian. Purple: absolute monarchies. (F6)"),
            (MapMode.Population, "population", "Population", "People per area. (F7)"),
        };

        public MapModeBar(GameRoot game, TooltipManager tips)
        {
            _game = game;
            Root = Ui.Panel();
            Ui.Absolute(Root, null, null, 12, 12);
            Root.style.flexDirection = FlexDirection.Row;
            Root.style.alignItems = Align.Center;
            Root.style.paddingLeft = 6;
            Root.style.paddingRight = 6;
            Root.style.paddingTop = 4;
            Root.style.paddingBottom = 4;
            foreach (var (mode, icon, name, help) in Modes)
            {
                var m = mode;
                var b = Ui.IconButton(icon, () => _game.SetMapMode(m), 20);
                b.style.width = 38;
                b.style.height = 38;
                tips.Attach(b, name + " map", help);
                _buttons[mode] = b;
                Root.Add(b);
            }
        }

        public void Refresh(MapMode current)
        {
            foreach (var kv in _buttons)
                kv.Value.EnableInClassList("gs-btn--active", kv.Key == current);
        }
    }

    /// <summary>Banner shown while choosing a nation, with quick picks.</summary>
    public sealed class NationSelectBanner
    {
        public VisualElement Root { get; }

        public NationSelectBanner(GameRoot game, TooltipManager tips)
        {
            Root = Ui.Element();
            Ui.Absolute(Root, 0, 80, 0);
            Root.style.alignItems = Align.Center;

            var panel = Ui.Panel();
            panel.style.alignItems = Align.Center;
            panel.style.paddingLeft = 28;
            panel.style.paddingRight = 28;
            panel.style.paddingTop = 12;
            panel.style.paddingBottom = 12;
            panel.Add(Ui.Title("CHOOSE YOUR NATION", 32));
            var sub = Ui.Label("Click any country on the map, or pick one below. Then press  Play as ...", 16, Ui.TextDim);
            sub.style.marginTop = 2;
            sub.style.marginBottom = 8;
            panel.Add(sub);
            var picks = Ui.Row();
            foreach (var tag in new[] { "USA", "CHN", "RUS", "IND", "DEU", "GBR", "FRA", "JPN", "BRA", "TUR", "SAU", "UKR" })
            {
                var t = tag;
                var flag = Ui.FlagElement(tag);
                flag.pickingMode = PickingMode.Position;
                flag.style.marginLeft = 4;
                flag.style.marginRight = 4;
                flag.RegisterCallback<ClickEvent>(_ =>
                {
                    Ui.Audio?.Play(Audio.Sfx.UiClick);
                    var c = game.World?.GetCountry(t);
                    if (c != null)
                    {
                        game.SelectCountry(c);
                        game.CameraController?.FlyTo(game.Map.ProvinceCenter(c.CapitalProvinceId), 4f);
                    }
                });
                tips.Attach(flag, () =>
                {
                    var c = game.World?.GetCountry(t);
                    return c == null ? null : new TooltipContent(c.Name, $"{c.Politics?.Government.DisplayName()}. Population {Ui.FormatPopulation(game.World.PopulationOf(c))}.");
                });
                picks.Add(flag);
            }
            panel.Add(picks);
            Root.Add(panel);
        }
    }

    /// <summary>Settings: volumes and diagnostics.</summary>
    public sealed class SettingsPanel
    {
        readonly GameRoot _game;
        readonly Label _diagnostics;

        public VisualElement Root { get; }

        public SettingsPanel(GameRoot game)
        {
            _game = game;
            Root = Ui.Element("gs-backdrop", true);
            var panel = Ui.Panel();
            panel.style.width = 500;
            var header = Ui.Header();
            header.Add(Ui.IconElement("settings", 26));
            var t = Ui.Title("Settings", 26);
            t.style.marginLeft = 10;
            header.Add(t);
            panel.Add(header);
            var body = Ui.Element("gs-panel__body");

            var audio = game.Audio;
            body.Add(Slider("speaker", "Music volume", audio.MusicVolume, v => audio.MusicVolume = v));
            body.Add(Slider("speaker", "Effects volume", audio.SfxVolume, v =>
            {
                audio.SfxVolume = v;
                audio.Play(Audio.Sfx.UiClick);
            }));
            var mute = new Toggle("Mute all sound (M)") { value = audio.Muted, focusable = false };
            mute.labelElement.style.color = Ui.Text;
            Ui.SetFont(mute.labelElement, Ui.Weight.Medium);
            mute.labelElement.style.minWidth = 180;
            mute.style.marginTop = 10;
            mute.RegisterValueChangedCallback(e => audio.Muted = e.newValue);
            body.Add(mute);

            var note = Ui.Label("Music and sound effects are generated in-game. Drop your own files into Assets/Resources/Audio to replace them.", 13, Ui.TextDim);
            note.style.marginTop = 12;
            body.Add(note);
            _diagnostics = Ui.Label("", 12, new Color(1, 1, 1, 0.45f));
            _diagnostics.style.marginTop = 10;
            body.Add(_diagnostics);
            var close = Ui.Button("Close", () => Ui.Show(Root, false), true, 16);
            close.style.marginTop = 16;
            close.style.alignSelf = Align.FlexEnd;
            close.style.minWidth = 120;
            body.Add(close);
            panel.Add(body);
            Root.Add(panel);
            Ui.Show(Root, false);
        }

        static VisualElement Slider(string icon, string label, float value, Action<float> onChange)
        {
            var box = Ui.Element("gs-slider-row", true);
            var head = Ui.Row();
            head.Add(Ui.IconElement(icon, 16));
            var l = Ui.Label(label, 15, null, Ui.Weight.SemiBold);
            l.style.marginLeft = 6;
            head.Add(l);
            box.Add(head);
            var s = new Slider(0f, 1f) { value = value, focusable = false };
            s.RegisterValueChangedCallback(e => onChange(e.newValue));
            box.Add(s);
            return box;
        }

        public void Toggle()
        {
            bool show = !Ui.IsShown(Root);
            if (show)
                _diagnostics.text = $"Unity {Application.unityVersion}  |  map: {(_game.Map == null ? "-" : _game.Map.UsesShader ? "shader" : "CPU fallback")}" +
                                    $"  |  input: {GameInput.BackendName}  |  {SystemInfo.graphicsDeviceType}";
            Ui.Show(Root, show);
            if (show)
                Root.BringToFront();
        }
    }

    /// <summary>Full-screen loading / error screen.</summary>
    public sealed class LoadingScreen
    {
        readonly Label _text;

        public VisualElement Root { get; }

        public LoadingScreen()
        {
            Root = Ui.Element(null, true);
            Ui.Fill(Root);
            Root.style.backgroundColor = new Color(0.04f, 0.055f, 0.08f, 1f);
            Root.style.alignItems = Align.Center;
            Root.style.justifyContent = Justify.Center;
            Root.Add(Ui.Title("GRAND STRATEGY", 64));
            var sub = Ui.Label("THE WORLD IN 2026", 18, Ui.Gold, Ui.Weight.SemiBold);
            sub.style.letterSpacing = 4;
            sub.style.marginBottom = 24;
            Root.Add(sub);
            _text = Ui.Label("Loading the world...", 18, Ui.TextDim);
            _text.style.maxWidth = 900;
            _text.style.unityTextAlign = TextAnchor.MiddleCenter;
            Root.Add(_text);
        }

        public void Show(string message, bool error = false)
        {
            _text.text = message;
            _text.style.color = error ? Ui.Bad : Ui.TextDim;
            Ui.Show(Root, true);
            Root.BringToFront();
        }

        public void Hide() => Ui.Show(Root, false);
    }

    /// <summary>Game over: your nation was annexed or your government overthrown.</summary>
    public sealed class GameOverScreen
    {
        readonly GameRoot _game;

        public VisualElement Root { get; }

        public GameOverScreen(GameRoot game)
        {
            _game = game;
            Root = Ui.Element("gs-backdrop", true);
            Root.style.backgroundColor = new Color(0.12f, 0f, 0f, 0.7f);
            Ui.Show(Root, false);
        }

        public void Show(string title, string text)
        {
            Root.Clear();
            var panel = Ui.Panel();
            panel.style.width = 620;
            panel.style.alignItems = Align.Center;
            Ui.Padding(panel, 28);
            panel.Add(Ui.IconElement("crown", 56, Ui.Bad, "gs-icon--large"));
            var t = Ui.Title(title, 40);
            t.style.color = Ui.Bad;
            t.style.marginTop = 8;
            panel.Add(t);
            var body = Ui.Label(text, 17);
            body.style.marginTop = 10;
            body.style.unityTextAlign = TextAnchor.MiddleCenter;
            panel.Add(body);
            var again = Ui.Button("New game", () => _game.RestartGame(), true, 18);
            again.style.marginTop = 22;
            again.style.minWidth = 260;
            panel.Add(again);
            Root.Add(panel);
            Ui.Show(Root, true);
            Root.BringToFront();
        }

        public void Hide() => Ui.Show(Root, false);
    }

    /// <summary>Developer tools (F12): test events and the Capital Battle rules.</summary>
    public sealed class DevPanel
    {
        readonly GameRoot _game;
        readonly VisualElement _body;

        public VisualElement Root { get; }

        public DevPanel(GameRoot game)
        {
            _game = game;
            Root = Ui.Panel();
            Ui.Absolute(Root, null, null, 12, 70);
            Root.style.width = 360;
            var header = Ui.Header();
            header.Add(Ui.Title("Developer tools (F12)", 20));
            Root.Add(header);
            _body = Ui.Element("gs-panel__body");
            Root.Add(_body);
            Ui.Show(Root, false);
        }

        public void Toggle()
        {
            bool show = !Ui.IsShown(Root);
            if (show)
                Rebuild();
            Ui.Show(Root, show);
        }

        public void RefreshIfShown()
        {
            if (Ui.IsShown(Root))
                Rebuild();
        }

        void Rebuild()
        {
            _body.Clear();
            var selfTest = Ui.Button("Run self-test", () => _game.RunSelfTest(), true, 15, "trophy");
            selfTest.style.marginBottom = 8;
            _body.Add(selfTest);
            _body.Add(Ui.Label("Plays through every screen and action (about 30 seconds) and reports anything that breaks. Starts a new game afterwards.", 13, Ui.TextDim));
            _body.Add(Ui.SectionTitle("Events and cheats"));
            _body.Add(Ui.Label("Shortcuts for testing. Not part of normal play.", 13, Ui.TextDim));
            var row = Ui.Row();
            row.style.flexWrap = Wrap.Wrap;
            row.Add(Ui.Button("Protests", () => _game.DevTrigger("protests"), false, 13));
            row.Add(Ui.Button("Random event", () => _game.DevTrigger("random"), false, 13));
            row.Add(Ui.Button("Trade offer", () => _game.DevTrigger("trade_offer"), false, 13));
            row.Add(Ui.Button("+$50B", () => _game.DevTrigger("money"), false, 13));
            row.Add(Ui.Button("Stability -30", () => _game.DevTrigger("unrest"), false, 13));
            _body.Add(row);

            var target = _game.SelectedCountry;
            _body.Add(Ui.SectionTitle("Capital Battle test"));
            if (target == null || target == _game.World?.Player || _game.Phase != GamePhase.Playing)
            {
                _body.Add(Ui.Label("Select another country while playing.", 13, Ui.TextDim));
                return;
            }
            _body.Add(Ui.Label($"We attack {target.Name}:", 13));
            var ours = Ui.Row();
            ours.style.flexWrap = Wrap.Wrap;
            ours.Add(Ui.Button("Win, 40% lost", () => _game.RunCapitalBattleTest(target.Tag, true, CapitalBattleTestOutcome.CleanWin), false, 13));
            ours.Add(Ui.Button("Win, 72% lost", () => _game.RunCapitalBattleTest(target.Tag, true, CapitalBattleTestOutcome.CostlyWin), false, 13));
            ours.Add(Ui.Button("Repelled", () => _game.RunCapitalBattleTest(target.Tag, true, CapitalBattleTestOutcome.Repelled), false, 13));
            _body.Add(ours);
            _body.Add(Ui.Label($"{target.Name} attacks us:", 13));
            var theirs = Ui.Row();
            theirs.style.flexWrap = Wrap.Wrap;
            theirs.Add(Ui.Button("They win, 30% lost", () => _game.RunCapitalBattleTest(target.Tag, false, CapitalBattleTestOutcome.CleanWin), false, 13));
            theirs.Add(Ui.Button("They win, 80% lost", () => _game.RunCapitalBattleTest(target.Tag, false, CapitalBattleTestOutcome.CostlyWin), false, 13));
            theirs.Add(Ui.Button("We hold", () => _game.RunCapitalBattleTest(target.Tag, false, CapitalBattleTestOutcome.Repelled), false, 13));
            _body.Add(theirs);
        }
    }
}
