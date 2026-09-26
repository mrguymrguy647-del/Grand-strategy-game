using System;
using System.Collections.Generic;
using System.Linq;

namespace GrandStrategy.Simulation
{
    public sealed class EventOption
    {
        public EventOption(string label, string effects, Action apply)
        {
            Label = label;
            Effects = effects;
            Apply = apply;
        }

        public string Label { get; }
        /// <summary>Plain-language summary of what the choice does.</summary>
        public string Effects { get; }
        internal Action Apply { get; }
    }

    /// <summary>A situation that needs a decision (or at least the player's attention).</summary>
    public sealed class NationalEvent
    {
        internal NationalEvent(string id, string title, string text, Country country, Country other, string icon)
        {
            Id = id;
            Title = title;
            Text = text;
            Country = country;
            Other = other;
            Icon = icon;
        }

        public string Id { get; }
        public string Title { get; }
        public string Text { get; }
        public Country Country { get; }
        public Country Other { get; }
        /// <summary>Icon name for the UI (e.g. "protest", "ballot", "handshake").</summary>
        public string Icon { get; }
        public List<EventOption> Options { get; } = new List<EventOption>();
        public bool Resolved { get; internal set; }
    }

    /// <summary>
    /// Creates events with choices. The player's events wait in <see cref="Pending"/> until
    /// <see cref="Choose"/> is called; AI countries pick the first option immediately.
    /// </summary>
    public sealed class NationalEvents
    {
        readonly GameSimulation _sim;
        readonly List<NationalEvent> _pending = new List<NationalEvent>();

        internal NationalEvents(GameSimulation sim) => _sim = sim;

        public IReadOnlyList<NationalEvent> Pending => _pending;

        public void Choose(NationalEvent evt, int optionIndex)
        {
            if (evt == null || evt.Resolved || optionIndex < 0 || optionIndex >= evt.Options.Count)
                return;
            evt.Resolved = true;
            _pending.Remove(evt);
            evt.Options[optionIndex].Apply?.Invoke();
            if (evt.Country != null && !evt.Country.IsEliminated && evt.Country.Economy != null)
                _sim.Economy.Refresh(evt.Country);
            _sim.World.Events.Publish(new ModifiersChanged(evt.Country?.Tag));
        }

        void Raise(NationalEvent evt)
        {
            if (_sim.IsPlayer(evt.Country))
            {
                _pending.Add(evt);
                _sim.World.Events.Publish(new NationalEventRaised(evt));
            }
            else
            {
                Choose(evt, 0);
            }
        }

        internal void Monthly(GameDate date)
        {
            var player = _sim.World.Player;
            if (player == null || player.IsEliminated || player.Politics == null || player.Politics.Overthrown)
                return;
            if (_pending.Count == 0 && _sim.Rng.NextDouble() < _sim.PoliticsRules.randomEventChance)
                RaiseRandom(player);
        }

        // ------------------------------------------------------------------ helpers

        double Gdp(Country c) => _sim.GdpOf(c);
        static string Money(double millions) =>
            millions >= 1_000_000 ? $"${millions / 1_000_000:0.0}T" : millions >= 1000 ? $"${millions / 1000:0.0}B" : $"${millions:0}M";

        void Mod(Country c, string id, string name, int days, double approval = 0, double stability = 0, double growth = 0)
        {
            c.AddOrRefreshModifier(new Modifier(id, name, name, 0, days) { Approval = approval, Stability = stability, Growth = growth });
        }

        // ------------------------------------------------------------------ politics

        public void RaiseProtests(Country c)
        {
            double cost = Gdp(c) * 0.003;
            var e = new NationalEvent("protests", $"Mass protests in {c.Name}",
                "Crowds fill the streets of the capital, angry at the government. The police chiefs await orders.", c, null, "protest");
            e.Options.Add(new EventOption("Negotiate with the protesters", $"Costs {Money(cost)}. Approval +8, stability +6 for 6 months.", () =>
            {
                _sim.Economy.Spend(c, cost);
                Mod(c, "protest_talks", "Talks with protesters", 180, approval: 8, stability: 6);
            }));
            bool democracy = c.Politics.Government.IsDemocracy();
            e.Options.Add(new EventOption("Crack down", democracy
                ? "Stability +10 for 6 months. Approval -15 for a year: voters hate it."
                : "Stability +12 for 6 months. Approval -8 for a year.", () =>
            {
                Mod(c, "protest_crackdown", "Protest crackdown", 365, approval: democracy ? -15 : -8);
                Mod(c, "protest_order", "Order restored by force", 180, stability: democracy ? 10 : 12);
            }));
            e.Options.Add(new EventOption("Wait it out", "Stability -4 for 6 months.", () =>
                Mod(c, "protest_ignored", "Protests ignored", 180, stability: -4)));
            Raise(e);
        }

        internal void RaiseElectionResult(Country c, bool won)
        {
            if (!_sim.IsPlayer(c))
                return;
            var e = won
                ? new NationalEvent("election_won", "Election victory!",
                    $"The government won the national election with {c.Politics.Approval:0}% approval. Approval +5 for 6 months.", c, null, "ballot")
                : new NationalEvent("election_lost", "The opposition wins the election",
                    "Voters have chosen new leaders. You keep leading the nation, and the new cabinet enjoys a honeymoon: approval +10 for a year, stability -3 for 6 months.",
                    c, null, "ballot");
            e.Options.Add(new EventOption("Understood", "", null));
            Raise(e);
        }

        internal void RaiseDebtDefault(Country c)
        {
            var e = new NationalEvent("default", "We have defaulted on our debt",
                "Interest took too much of our revenue. Creditors lose 30%, but markets shut us out: growth -4 for a year, approval -20, stability -15.",
                c, null, "bank");
            e.Options.Add(new EventOption("Accept an IMF programme", "Taxes +3 points, welfare -10%. Interest premium falls sooner. Approval -5 for a year.", () =>
            {
                var econ = c.Economy;
                _sim.Economy.SetTaxRate(c, econ.TaxRate + 0.03);
                _sim.Economy.SetSpending(c, SpendingCategory.Welfare, econ.GetSpending(SpendingCategory.Welfare) * 0.9);
                c.AddOrRefreshModifier(new Modifier("imf", "IMF programme", "Reforms reassure creditors.", 0, 365)
                {
                    Approval = -5,
                    InterestPremium = -0.03,
                });
            }));
            e.Options.Add(new EventOption("Go it alone", "No conditions, but no help either.", null));
            Raise(e);
        }

        // ------------------------------------------------------------------ diplomacy

        public void RaiseTradeOffer(Country player, Country from)
        {
            var e = new NationalEvent("trade_offer", $"{from.Name} proposes a trade agreement",
                $"The government of {from.Name} wants to lower trade barriers between our countries. Both economies would grow faster.",
                player, from, "handshake");
            e.Options.Add(new EventOption("Sign the agreement", "Faster growth for both; relations improve.", () =>
            {
                if (!_sim.Diplomacy.AreTradePartners(player, from) && !_sim.Diplomacy.AnySanctions(player, from))
                    _sim.DiplomacySystem.SignTrade(player, from);
            }));
            e.Options.Add(new EventOption("Decline", "Relations -5.", () =>
                _sim.Diplomacy.AddGoodwill(player.Index, from.Index, -5)));
            Raise(e);
        }

        internal void RaiseSanctioned(Country player, Country by)
        {
            var e = new NationalEvent("sanctioned", $"{by.Name} imposes sanctions on us",
                $"{by.Name} has restricted trade and finance with our country. Our growth will suffer.", player, by, "sanctions");
            e.Options.Add(new EventOption("Retaliate with our own sanctions", "Their growth falls too; relations collapse.", () =>
            {
                if (!_sim.Diplomacy.Sanctions(player, by))
                    _sim.DiplomacySystem.ImposeSanctions(player, by);
            }));
            e.Options.Add(new EventOption("Seek dialogue", "Relations improve a little. Hawks at home call it weak: approval -3 for 6 months.", () =>
            {
                _sim.Diplomacy.AddGoodwill(player.Index, by.Index, 10);
                Mod(player, "sanction_dialogue", "Seen as weak on sanctions", 180, approval: -3);
            }));
            Raise(e);
        }

        // ------------------------------------------------------------------ random

        /// <summary>Raises one random event now (used by the developer panel). Does nothing while an event is waiting.</summary>
        public void RaiseRandomEvent(Country c)
        {
            if (c == null || c.IsEliminated || c.Politics == null || c.Politics.Overthrown || _pending.Count > 0)
                return;
            RaiseRandom(c);
        }

        void RaiseRandom(Country c)
        {
            int pick = _sim.Rng.Next(6);
            double gdp = Gdp(c);
            NationalEvent e;
            switch (pick)
            {
                case 0:
                    e = new NationalEvent("scandal", "Corruption scandal",
                        "Journalists reveal that ministers took bribes from a construction company.", c, null, "scandal");
                    e.Options.Add(new EventOption("Launch an independent inquiry", "Stability -5 for 6 months, approval +4 for a year.", () =>
                    {
                        Mod(c, "inquiry_turmoil", "Corruption inquiry", 180, stability: -5);
                        Mod(c, "inquiry_trust", "Seen fighting corruption", 365, approval: 4);
                    }));
                    e.Options.Add(new EventOption("Bury the story", "Approval -8 for 6 months.", () =>
                        Mod(c, "coverup", "Cover-up", 180, approval: -8)));
                    break;

                case 1:
                    double relief = gdp * 0.005;
                    e = new NationalEvent("disaster", "Natural disaster",
                        "Floods and landslides have destroyed homes and roads in several provinces.", c, null, "disaster");
                    e.Options.Add(new EventOption("Fund full relief", $"Costs {Money(relief)}. Approval +6 for 6 months.", () =>
                    {
                        _sim.Economy.Spend(c, relief);
                        Mod(c, "relief", "Disaster relief", 180, approval: 6);
                    }));
                    e.Options.Add(new EventOption("Limited response", "Approval -8 for 6 months, growth -0.3 for 6 months.", () =>
                        Mod(c, "poor_relief", "Poor disaster response", 180, approval: -8, growth: -0.3)));
                    break;

                case 2:
                    double invest = gdp * 0.003;
                    e = new NationalEvent("tech_boom", "Technology boom",
                        "Our tech start-ups are attracting investors from all over the world.", c, null, "chip");
                    e.Options.Add(new EventOption("Invest in the sector", $"Costs {Money(invest)}. Growth +0.8 for 2 years.", () =>
                    {
                        _sim.Economy.Spend(c, invest);
                        Mod(c, "tech_investment", "Tech investment", 730, growth: 0.8);
                    }));
                    e.Options.Add(new EventOption("Let markets decide", "Growth +0.3 for a year.", () =>
                        Mod(c, "tech_boom", "Tech boom", 365, growth: 0.3)));
                    break;

                case 3:
                    double subsidy = gdp * 0.008;
                    e = new NationalEvent("energy_shock", "Energy prices surge",
                        "A crisis abroad has sent oil and gas prices soaring. Households and factories are hit.", c, null, "energy");
                    e.Options.Add(new EventOption("Subsidise energy bills", $"Costs {Money(subsidy)}. Approval +2 for 6 months.", () =>
                    {
                        _sim.Economy.Spend(c, subsidy);
                        Mod(c, "energy_subsidy", "Energy subsidies", 180, approval: 2);
                    }));
                    e.Options.Add(new EventOption("Let prices rise", "Approval -7 for 6 months, growth -0.5 for a year.", () =>
                    {
                        Mod(c, "energy_anger", "Energy bills anger", 180, approval: -7);
                        Mod(c, "energy_shock", "Energy shock", 365, growth: -0.5);
                    }));
                    break;

                case 4:
                    double deal = gdp * 0.004;
                    e = new NationalEvent("strike", "General strike",
                        "Unions have called a nationwide strike over wages and pensions.", c, null, "strike");
                    e.Options.Add(new EventOption("Meet the unions' demands", $"Costs {Money(deal)}. Approval +5 for 6 months.", () =>
                    {
                        _sim.Economy.Spend(c, deal);
                        Mod(c, "union_deal", "Deal with unions", 180, approval: 5);
                    }));
                    e.Options.Add(new EventOption("Refuse", "Growth -0.6 for 3 months, stability -4 for 6 months.", () =>
                    {
                        Mod(c, "strike_losses", "Strike losses", 90, growth: -0.6);
                        Mod(c, "strike_unrest", "Labour unrest", 180, stability: -4);
                    }));
                    break;

                default:
                    var investor = _sim.World.Countries
                        .Where(o => o != c && !o.IsEliminated && _sim.GdpShare(o) > 0.02 && _sim.Diplomacy.Relations(o, c) > 20)
                        .OrderBy(_ => _sim.Rng.Next()).FirstOrDefault();
                    if (investor == null)
                        return;
                    e = new NationalEvent("investment", $"Investment offer from {investor.Name}",
                        $"Companies from {investor.Name} want to build factories in our country.", c, investor, "factory");
                    e.Options.Add(new EventOption("Welcome the investment", $"Growth +0.5 for 2 years. Relations with {investor.Name} improve.", () =>
                    {
                        Mod(c, "fdi", $"Investment from {investor.Name}", 730, growth: 0.5);
                        _sim.Diplomacy.AddGoodwill(c.Index, investor.Index, 10);
                    }));
                    e.Options.Add(new EventOption("Protect local industry", "Approval +2 for 6 months. Relations -5.", () =>
                    {
                        Mod(c, "protectionism", "Protecting local industry", 180, approval: 2);
                        _sim.Diplomacy.AddGoodwill(c.Index, investor.Index, -5);
                    }));
                    break;
            }
            Raise(e);
        }
    }
}
