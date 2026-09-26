using GrandStrategy.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace GrandStrategy.Game.UI
{
    /// <summary>
    /// Top bar: your flag and nation, then resource chips (treasury, GDP, debt, stability,
    /// approval) with breakdown tooltips, then the date and speed controls.
    /// </summary>
    public sealed class TopBar
    {
        readonly GameRoot _game;
        readonly VisualElement _flag;
        readonly Label _name;
        readonly Label _government;
        readonly VisualElement _chips;
        readonly Label _treasury, _treasuryDelta, _gdp, _gdpDelta, _debt, _debtDelta, _stability, _approval, _population;
        readonly Label _date;
        readonly Button _pause;
        readonly VisualElement[] _pips = new VisualElement[GameClock.MaxSpeed];
        string _shownDate;
        int _shownSpeed = -1;
        bool? _shownPaused;

        public VisualElement Root { get; }

        public TopBar(GameRoot game, TooltipManager tips)
        {
            _game = game;
            Root = Ui.Element("gs-topbar", true);

            _flag = Ui.FlagElement(null);
            _flag.style.marginRight = 10;
            Root.Add(_flag);
            var names = Ui.Column();
            names.style.marginRight = 16;
            names.style.minWidth = 150;
            _name = Ui.Label("Choose your nation", 22, new Color(0.96f, 0.93f, 0.87f), Ui.Weight.Title);
            _government = Ui.Label("", 13, Ui.TextDim, Ui.Weight.Medium);
            names.Add(_name);
            names.Add(_government);
            Root.Add(names);
            tips.Attach(names, () =>
            {
                var p = _game.World?.Player;
                if (p == null) return new TooltipContent("No nation yet", "Click a country on the map and press Play.");
                return new TooltipContent(p.FormalName, $"{p.Politics.Government.DisplayName()}. Capital: {p.CapitalName}.")
                    .Row("Population", Ui.FormatPopulation(_game.World.PopulationOf(p)))
                    .Row("Provinces", p.ProvinceIds.Count.ToString());
            });

            _chips = Ui.Row();
            Root.Add(_chips);
            _treasury = Chip("treasury", out _treasuryDelta, tips, TreasuryTip);
            _gdp = Chip("gdp", out _gdpDelta, tips, GrowthTip);
            _debt = Chip("debt", out _debtDelta, tips, DebtTip);
            _stability = Chip("stability", out _, tips, StabilityTip);
            _approval = Chip("approval", out _, tips, ApprovalTip);
            _population = Chip("population", out _, tips, () => new TooltipContent("Population",
                "People living in all our provinces. Grows slowly every month."));

            Root.Add(Ui.Spacer());

            var cal = Ui.IconElement("calendar", 20);
            cal.style.marginRight = 6;
            Root.Add(cal);
            _date = Ui.Label("", 20, null, Ui.Weight.Title, "gs-date");
            Root.Add(_date);

            var slower = Ui.Button("-", () => _game.SlowDown(), false, 18);
            tips.Attach(slower, "Slower", "Keyboard: - or 1-5");
            Root.Add(slower);
            var pips = Ui.Row();
            pips.style.marginLeft = 4;
            pips.style.marginRight = 4;
            pips.style.height = 22;
            pips.style.alignItems = Align.FlexEnd;
            for (int i = 0; i < _pips.Length; i++)
            {
                var pip = Ui.Element("gs-pip");
                pip.style.height = 8 + i * 3.5f;
                _pips[i] = pip;
                pips.Add(pip);
            }
            Root.Add(pips);
            var faster = Ui.Button("+", () => _game.SpeedUp(), false, 18);
            tips.Attach(faster, "Faster", "Keyboard: + or 1-5");
            Root.Add(faster);

            _pause = Ui.Button("Play", () => _game.TogglePause(), true, 16);
            _pause.style.minWidth = 96;
            tips.Attach(_pause, "Pause / play", "Keyboard: Space");
            Root.Add(_pause);

            var settings = Ui.IconButton("settings", () => _game.Hud.ToggleSettings(), 18);
            settings.style.marginLeft = 10;
            tips.Attach(settings, "Settings", "Volume and diagnostics.");
            Root.Add(settings);
        }

        Label Chip(string icon, out Label delta, TooltipManager tips, System.Func<TooltipContent> tip)
        {
            var chip = Ui.Element("gs-chip", true);
            chip.Add(Ui.IconElement(icon, 20));
            var value = Ui.Label("-", 17, null, Ui.Weight.SemiBold, "gs-chip__value");
            chip.Add(value);
            delta = Ui.Label("", 13, Ui.TextDim, Ui.Weight.Medium, "gs-chip__delta");
            chip.Add(delta);
            tips.Attach(chip, tip);
            _chips.Add(chip);
            return value;
        }

        public void Refresh()
        {
            var world = _game.World;
            var clock = world?.Clock;
            if (clock == null)
                return;

            string date = clock.Date.ToString();
            if (date != _shownDate)
            {
                _shownDate = date;
                _date.text = date;
            }
            if (clock.Speed != _shownSpeed || clock.IsPaused != _shownPaused)
            {
                _shownSpeed = clock.Speed;
                _shownPaused = clock.IsPaused;
                for (int i = 0; i < _pips.Length; i++)
                {
                    _pips[i].EnableInClassList("gs-pip--on", i < clock.Speed && !clock.IsPaused);
                    _pips[i].EnableInClassList("gs-pip--paused", i < clock.Speed && clock.IsPaused);
                }
                _pause.text = clock.IsPaused ? "Play" : "Pause";
            }
            _pause.SetEnabled(_game.Phase == GamePhase.Playing && _game.CanRunTime);

            var p = world.Player;
            bool show = p != null && _game.Phase != GamePhase.NationSelect && p.Economy != null;
            Ui.Show(_chips, show);
            if (!show)
            {
                _name.text = "Choose your nation";
                _government.text = "Click any country on the map";
                Ui.SetFlag(_flag, null);
                _flag.style.backgroundColor = new Color(0.25f, 0.25f, 0.28f);
                return;
            }

            _name.text = p.Name;
            _government.text = p.Politics.Government.DisplayName();
            Ui.SetFlag(_flag, p.Tag);

            var e = p.Economy;
            _treasury.text = Ui.FormatMoneyMillions(e.Treasury);
            double balance = e.MonthlyBalance;
            _treasuryDelta.text = Ui.FormatSignedMoney(balance) + "/mo";
            _treasuryDelta.style.color = balance >= 0 ? Ui.Good : Ui.Bad;

            _gdp.text = Ui.FormatMoneyMillions(e.Gdp);
            _gdpDelta.text = Ui.FormatGrowth(e.RealGrowth);
            _gdpDelta.style.color = e.RealGrowth >= 1 ? Ui.Good : e.RealGrowth >= 0 ? Ui.Warn : Ui.Bad;

            _debt.text = Ui.FormatPercent(e.DebtToGdp);
            _debtDelta.text = e.Rating.ToString();
            _debtDelta.style.color = e.Rating <= CreditRating.A ? Ui.Good : e.Rating <= CreditRating.BB ? Ui.Warn : Ui.Bad;

            _stability.text = p.Politics.Stability.ToString("0");
            _stability.style.color = Ui.ScoreColor(p.Politics.Stability, 30, 55);
            _approval.text = p.Politics.Approval.ToString("0") + "%";
            _approval.style.color = Ui.ScoreColor(p.Politics.Approval, 30, 50);
            _population.text = Ui.FormatPopulation(world.PopulationOf(p));
        }

        // ------------------------------------------------------------------ tooltips

        Country Player => _game.World?.Player;

        TooltipContent TreasuryTip()
        {
            var t = Tips.Budget(Player);
            if (t != null)
            {
                t.Title = "Treasury";
                t.Footer = "Change taxes and spending in your country's Economy tab.";
            }
            return t;
        }

        TooltipContent GrowthTip() => Tips.Growth(Player);
        TooltipContent DebtTip() => Tips.Debt(Player);
        TooltipContent StabilityTip() => Tips.Stability(Player);
        TooltipContent ApprovalTip() => Tips.Approval(Player);
    }
}
