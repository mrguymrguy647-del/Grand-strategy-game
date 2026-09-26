using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using GrandStrategy.Game.Map;
using GrandStrategy.Game.UI;
using GrandStrategy.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace GrandStrategy.Game
{
    /// <summary>
    /// Plays through every screen and action once and reports what broke (F12 → Run self-test).
    /// Unity catches exceptions thrown inside UI callbacks and only logs them, so the test also
    /// listens to the log and counts every error written while it runs. It changes the world
    /// freely (policies, wars, annexations), so it finishes by offering a new game.
    /// </summary>
    public sealed class SelfTest
    {
        const int DaysPerFrame = 5;
        const int MonthsToRun = 24;
        const float BattleTimeoutSeconds = 10f;

        readonly GameRoot _game;
        readonly List<string> _failures = new List<string>();
        readonly List<string> _notes = new List<string>();
        readonly List<string> _logErrors = new List<string>();
        int _passed;
        string _stage = "start";

        public SelfTest(GameRoot game)
        {
            _game = game;
        }

        HudController Hud => _game.Hud;
        WorldState World => _game.World;
        GameSimulation Sim => _game.Sim;

        // ------------------------------------------------------------------ run

        public IEnumerator Run()
        {
            float started = Time.realtimeSinceStartup;
            Application.logMessageReceived += OnLog;
            Hud.CloseOverlay(); // the developer panel that started us
            Hud.Toast("Self-test running", "Please don't click until the report appears (about 30 seconds).", Ui.Warn);
            World.Clock.SetPaused(true);

            Stage("Resources");
            Do("stylesheet, fonts, icons and flags", CheckResources);
            yield return null;

            if (_game.Phase == GamePhase.NationSelect)
            {
                Stage("Nation select");
                foreach (var c in SampleCountries())
                    yield return InspectCountry(c);
                yield return AllMapModes();
            }

            Stage("Choose a nation");
            var player = World.Player ?? World.GetCountry("FRA") ?? World.Countries.First(c => !c.IsEliminated);
            Do($"play as {player.Name}", () => _game.ChooseNation(player.Tag));
            yield return Frames(2);
            Check(_game.Phase == GamePhase.Playing, "the game is in the Playing phase after choosing a nation");
            yield return AnswerPopups();
            yield return BuildTooltips("top bar", Hud.TopBarRoot);
            yield return InspectCountry(player);

            if (Playing())
            {
                Stage("Economy");
                var e = player.Economy;
                Do("change taxes and every spending area", () =>
                {
                    Sim.Economy.SetTaxRate(player, e.TaxRate + 0.02);
                    foreach (SpendingCategory cat in Enum.GetValues(typeof(SpendingCategory)))
                        Sim.Economy.SetSpending(player, cat, e.GetSpending(cat) + 0.005);
                });
                Do("borrow and repay", () =>
                {
                    Sim.Economy.Borrow(player, 1000);
                    Sim.Economy.Repay(player, 500);
                });
                Hud.OpenCountryTab("Economy");
                yield return Frames(2);
                Check(!double.IsNaN(e.MonthlyBalance) && !double.IsNaN(e.RealGrowth), "budget and growth are numbers after policy changes");
                yield return BuildTooltips("economy tab", Hud.CountryPanelRoot);

                Stage("Decisions");
                foreach (DecisionId id in Enum.GetValues(typeof(DecisionId)))
                {
                    Do($"decision {id}", () => _game.EnactDecision(id));
                    yield return AnswerPopups();
                }
            }

            if (Playing())
            {
                Stage("Diplomacy");
                foreach (var tag in new[] { "DEU", "JPN", "IND", "VEN", "USA", "RUS" })
                {
                    var target = World.GetCountry(tag);
                    if (target == null || target.IsEliminated || target == World.Player)
                        continue;
                    _game.SelectCountry(target);
                    Hud.OpenCountryTab("Diplomacy");
                    yield return Frames(2);
                    yield return BuildTooltips($"diplomacy with {target.Name}", Hud.CountryPanelRoot);
                    foreach (DiplomaticAction action in Enum.GetValues(typeof(DiplomaticAction)))
                    {
                        if (action == DiplomaticAction.DeclareWar || !Playing())
                            continue;
                        ActionPreview preview = null;
                        Do($"preview {action} with {target.Name}", () => preview = Sim.DiplomacySystem.Preview(World.Player, target, action));
                        if (preview == null || !preview.Available)
                            continue;
                        Do($"{action} with {target.Name}", () => _game.DoDiplomacy(target, action));
                        yield return AnswerPopups();
                    }
                }
            }

            if (Playing())
            {
                Stage("Events");
                foreach (var what in new[] { "protests", "random", "random", "random", "trade_offer", "money" })
                {
                    Do($"developer event '{what}'", () => _game.DevTrigger(what));
                    yield return Frames(1);
                    if (what == "protests")
                        Check(Hud.PopupOpen, "the protest event opens a popup with choices");
                    yield return AnswerPopups();
                }
                Check(Sim.Events.Pending.Count == 0, "no event is left waiting after answering every popup");
            }

            if (Playing())
            {
                Stage("Time");
                yield return RunMonths(MonthsToRun);
                CheckNumbers();
            }

            if (Playing())
            {
                Stage("Capital Battle rules");
                var small = World.Countries
                    .Where(c => !c.IsEliminated && c != World.Player && c.ProvinceIds.Count >= 2)
                    .OrderBy(c => World.PopulationOf(c))
                    .Take(3)
                    .ToList();
                var outcomes = new[] { CapitalBattleTestOutcome.Repelled, CapitalBattleTestOutcome.CostlyWin, CapitalBattleTestOutcome.CleanWin };
                for (int i = 0; i < small.Count && i < outcomes.Length && Playing(); i++)
                {
                    var target = small[i];
                    var outcome = outcomes[i];
                    int capitalBefore = target.CapitalProvinceId;
                    _game.SelectCountry(target);
                    Do($"{outcome} against {target.Name}", () => _game.RunCapitalBattleTest(target.Tag, true, outcome));
                    yield return Frames(1);
                    float until = Time.realtimeSinceStartup + BattleTimeoutSeconds;
                    while (_game.BattleInProgress && Time.realtimeSinceStartup < until)
                        yield return null;
                    Check(!_game.BattleInProgress, $"the {outcome} battle against {target.Name} finishes");
                    if (outcome == CapitalBattleTestOutcome.CleanWin)
                        Check(target.IsEliminated, $"a clean win annexes {target.Name}");
                    if (outcome == CapitalBattleTestOutcome.CostlyWin)
                        Check(target.IsEliminated || target.CapitalProvinceId != capitalBefore, $"a costly win moves {target.Name}'s capital");
                    yield return AnswerPopups();
                }
                yield return AllMapModes();
            }

            Stage("Screens");
            Do("settings open and close", () => Hud.ToggleSettings());
            yield return Frames(1);
            Do("settings close", () => Hud.ToggleSettings());
            if (_game.Phase != GamePhase.GameOver)
            {
                Do("game over screen", () => Hud.ShowGameOver("SELF-TEST", "Checking that this screen can be shown."));
                yield return Frames(1);
                Check(Hud.GameOverShown, "the game over screen appears");
                Do("hide game over screen", () => Hud.HideGameOver());
            }
            else
            {
                _notes.Add("The government fell during the test, so the game over screen was seen for real.");
            }

            Stage("Summary");
            var missing = Ui.MissingImages.ToList();
            Check(missing.Count == 0, missing.Count == 0 ? "every image the interface asked for was found"
                : $"{missing.Count} images are missing: {string.Join(", ", missing.Take(10))}");

            Application.logMessageReceived -= OnLog;
            Check(_logErrors.Count == 0, _logErrors.Count == 0 ? "no errors were logged"
                : $"{_logErrors.Count} errors were logged (see the list below)");

            ShowReport(Time.realtimeSinceStartup - started);
        }

        // ------------------------------------------------------------------ stages

        IEnumerable<Country> SampleCountries()
        {
            var picks = new List<Country>();
            foreach (var tag in new[] { "USA", "FRA", "CHN", "DNK" })
            {
                var c = World.GetCountry(tag);
                if (c != null)
                    picks.Add(c);
            }
            var tiny = World.Countries.Where(c => !c.IsEliminated).OrderBy(c => World.PopulationOf(c)).FirstOrDefault();
            if (tiny != null && !picks.Contains(tiny))
                picks.Add(tiny);
            return picks;
        }

        IEnumerator InspectCountry(Country c)
        {
            Do($"select {c.Name}", () => _game.SelectCountry(c, c.CapitalProvinceId));
            foreach (var tab in new[] { "Overview", "Economy", "Politics", "Diplomacy" })
            {
                Do($"{c.Name}: {tab} tab", () => Hud.OpenCountryTab(tab));
                yield return Frames(2);
                var panel = Hud.CountryPanelRoot;
                Check(panel != null && panel.layout.width > 100 && panel.layout.height > 100,
                    $"{c.Name}: {tab} tab has a real size ({panel?.layout.width:0}x{panel?.layout.height:0})");
                yield return BuildTooltips($"{c.Name} {tab} tab", panel);
            }
        }

        IEnumerator AllMapModes()
        {
            foreach (MapMode mode in Enum.GetValues(typeof(MapMode)))
            {
                Do($"map mode {mode}", () => _game.SetMapMode(mode));
                yield return Frames(1);
            }
            _game.SetMapMode(MapMode.Political);
        }

        IEnumerator BuildTooltips(string where, VisualElement root)
        {
            if (root == null)
                yield break;
            var errors = new List<string>();
            int built = Hud.Tooltips.BuildAllForTest(root, errors);
            if (errors.Count == 0)
                _passed++;
            foreach (var e in errors)
                Fail($"{where}: {e}");
            if (built == 0)
                _notes.Add($"No tooltips found on {where}.");
            yield return null;
        }

        IEnumerator RunMonths(int months)
        {
            var now = World.Clock.Date;
            int total = now.Month - 1 + months;
            var end = new GameDate(now.Year + total / 12, total % 12 + 1, 1);
            int guard = months * 40;
            while (World.Clock.Date < end && Playing() && guard-- > 0)
            {
                Do("advance time", () =>
                {
                    for (int d = 0; d < DaysPerFrame; d++)
                        World.Clock.StepDay();
                });
                yield return null;
                yield return AnswerPopups();
            }
            if (Playing())
                Check(World.Clock.Date >= end, $"{months} months pass (now {World.Clock.Date})");
            else
                _notes.Add($"The game ended during the time run ({World.Clock.Date}).");
        }

        void CheckNumbers()
        {
            var bad = World.Countries.Where(c => !c.IsEliminated && c.Economy != null && (
                    double.IsNaN(c.Economy.Gdp) || double.IsInfinity(c.Economy.Gdp) ||
                    double.IsNaN(c.Economy.Debt) || double.IsNaN(c.Economy.RealGrowth) ||
                    double.IsNaN(c.Politics.Stability) || double.IsNaN(c.Politics.Approval)))
                .Select(c => c.Tag).ToList();
            Check(bad.Count == 0, bad.Count == 0 ? "every country's economy and politics are valid numbers"
                : $"invalid numbers in: {string.Join(", ", bad.Take(10))}");
        }

        IEnumerator AnswerPopups()
        {
            for (int i = 0; i < 25 && Hud.PopupOpen; i++)
            {
                Do("answer popup", () => Hud.AnswerPopupForTest());
                yield return null;
            }
            Check(!Hud.PopupOpen, "every popup can be answered");
        }

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++)
                yield return null;
        }

        void CheckResources()
        {
            Check(Hud.StylesheetLoaded, "stylesheet Resources/UI/Game.uss is loaded");
            foreach (Ui.Weight w in Enum.GetValues(typeof(Ui.Weight)))
                Check(Ui.FontFor(w) != null, $"font for {w} is loaded");

            string iconDir = Path.Combine(Ui.ImageRoot, "Icons");
            var icons = Directory.Exists(iconDir) ? Directory.GetFiles(iconDir, "*.png") : new string[0];
            Check(icons.Length > 0, $"icons exist in {iconDir}");
            var brokenIcons = icons.Select(Path.GetFileNameWithoutExtension)
                .Where(n => { var t = Ui.Icon(n); return t == null || t.width != 64 || t.height != 64; })
                .ToList();
            Check(brokenIcons.Count == 0, brokenIcons.Count == 0 ? $"all {icons.Length} icons load at 64x64"
                : $"icons that don't load: {string.Join(", ", brokenIcons.Take(10))}");

            var noFlag = World.Countries.Where(c => Ui.Flag(c.Tag) == null).Select(c => c.Tag).ToList();
            Check(noFlag.Count == 0, noFlag.Count == 0 ? $"all {World.Countries.Count} flags load"
                : $"{noFlag.Count} countries have no flag: {string.Join(", ", noFlag.Take(10))}");

            if (_game.Map != null && !_game.Map.UsesShader)
                _notes.Add("The map uses the CPU fallback renderer (the shader is not supported on this GPU).");
        }

        // ------------------------------------------------------------------ bookkeeping

        bool Playing() => _game.Phase == GamePhase.Playing && World.Player != null && !World.Player.IsEliminated;

        void Stage(string name)
        {
            _stage = name;
            Debug.Log($"[Self-test] {name}");
        }

        void Do(string what, Action action)
        {
            try
            {
                action();
                _passed++;
            }
            catch (Exception e)
            {
                Fail($"{what}: {e.GetType().Name}: {e.Message}\n    {FirstStackLine(e.StackTrace)}");
            }
        }

        void Check(bool ok, string what)
        {
            if (ok)
                _passed++;
            else
                Fail(what);
        }

        void Fail(string what)
        {
            string entry = $"[{_stage}] {what}";
            _failures.Add(entry);
            Debug.LogWarning("[Self-test] FAILED " + entry);
        }

        void OnLog(string message, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)
                return;
            _logErrors.Add($"[{_stage}] {message}\n    {FirstStackLine(stackTrace)}");
        }

        static string FirstStackLine(string stack)
        {
            if (string.IsNullOrEmpty(stack))
                return "";
            var lines = stack.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).Take(3);
            return string.Join("\n    ", lines);
        }

        // ------------------------------------------------------------------ report

        void ShowReport(float seconds)
        {
            var report = new StringBuilder();
            report.AppendLine($"Grand Strategy self-test: {_passed} checks passed, {_failures.Count} failed, " +
                              $"{_logErrors.Count} errors logged ({seconds:0} s).");
            report.AppendLine($"Unity {Application.unityVersion}, {SystemInfo.operatingSystem}, {SystemInfo.graphicsDeviceName} " +
                              $"({SystemInfo.graphicsDeviceType}), input: {GameInput.BackendName}, " +
                              $"map: {(_game.Map != null && _game.Map.UsesShader ? "shader" : "CPU")}");
            if (_failures.Count > 0)
            {
                report.AppendLine().AppendLine("FAILED:");
                foreach (var f in _failures)
                    report.AppendLine(" - " + f);
            }
            if (_logErrors.Count > 0)
            {
                report.AppendLine().AppendLine("ERRORS LOGGED:");
                foreach (var e in _logErrors.Take(40))
                    report.AppendLine(" - " + e);
            }
            if (_notes.Count > 0)
            {
                report.AppendLine().AppendLine("Notes:");
                foreach (var n in _notes)
                    report.AppendLine(" - " + n);
            }
            string text = report.ToString();
            Debug.Log(text);

            bool ok = _failures.Count == 0;
            Hud.ShowCustomPopup(ok ? "trophy" : "war", ok ? "Self-test passed" : "Self-test found problems", body =>
            {
                body.Add(Ui.Label(ok
                        ? $"All {_passed} checks passed. Everything the test could reach works."
                        : $"{_failures.Count} of {_passed + _failures.Count} checks failed. Press Copy report and send it to your developer.",
                    16, ok ? Ui.Good : Ui.Bad, Ui.Weight.SemiBold));
                if (!ok)
                {
                    var list = new ScrollView(ScrollViewMode.Vertical);
                    list.style.maxHeight = 260;
                    list.style.marginTop = 8;
                    foreach (var f in _failures.Concat(_logErrors).Take(30))
                        list.Add(Ui.Label(f, 13, Ui.TextDim));
                    body.Add(list);
                }
                foreach (var n in _notes)
                {
                    var note = Ui.Label(n, 13, Ui.TextDim);
                    note.style.marginTop = 6;
                    body.Add(note);
                }
                var buttons = Ui.Row();
                buttons.style.marginTop = 14;
                buttons.style.justifyContent = Justify.FlexEnd;
                buttons.Add(Ui.Button("Copy report", () =>
                {
                    GUIUtility.systemCopyBuffer = text;
                    Hud.Toast("Report copied", "Paste it into your message.");
                }, false, 15));
                var fresh = Ui.Button("New game", () => _game.RestartGame(), true, 15);
                fresh.style.minWidth = 120;
                buttons.Add(fresh);
                body.Add(buttons);
            });
        }
    }
}
