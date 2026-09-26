using System;
using System.Collections.Generic;

namespace GrandStrategy.Simulation
{
    public enum DecisionId
    {
        StimulusPackage,
        PropagandaCampaign,
        ReformProgram,
        Crackdown,
        EarlyElection,
    }

    public sealed class DecisionInfo
    {
        public DecisionId Id;
        public string Name = "";
        public string Description = "";
        public string Effects = "";
        public double CostMillions;
        public bool Available;
        public string Reason = "";
    }

    /// <summary>One-off government decisions with costs, effects and cooldowns.</summary>
    public sealed class DecisionSystem
    {
        readonly GameSimulation _sim;

        internal DecisionSystem(GameSimulation sim) => _sim = sim;

        public IReadOnlyList<DecisionInfo> For(Country c)
        {
            var list = new List<DecisionInfo>();
            foreach (DecisionId id in Enum.GetValues(typeof(DecisionId)))
                list.Add(Describe(c, id));
            return list;
        }

        public DecisionInfo Describe(Country c, DecisionId id)
        {
            double gdp = _sim.GdpOf(c);
            var gov = c.Politics.Government;
            var info = new DecisionInfo { Id = id, Available = true };
            switch (id)
            {
                case DecisionId.StimulusPackage:
                    info.Name = "Stimulus package";
                    info.Description = "Borrow to spend on jobs and investment.";
                    info.CostMillions = gdp * 0.02;
                    info.Effects = "Growth +1.2 and approval +4 for a year.";
                    break;
                case DecisionId.PropagandaCampaign:
                    info.Name = "Media campaign";
                    info.Description = "Flood the airwaves with the government's achievements.";
                    info.CostMillions = gdp * 0.001;
                    info.Effects = gov.IsAutocracy() ? "Approval +8 for 6 months." : "Approval +6 for 6 months.";
                    break;
                case DecisionId.ReformProgram:
                    info.Name = "Institutional reform";
                    info.Description = "Modernise courts, police and the civil service.";
                    info.CostMillions = gdp * 0.005;
                    info.Effects = "Stability +8 for 18 months, growth +0.3 for 2 years. Approval -4 for 6 months.";
                    break;
                case DecisionId.Crackdown:
                    info.Name = "Crack down on dissent";
                    info.Description = "Arrest opposition figures and silence critics.";
                    info.Effects = "Stability +12 for 6 months. Approval -10 for 6 months. Democracies distrust us.";
                    if (gov.IsDemocracy())
                        Block(info, "Not possible in a democracy.");
                    break;
                case DecisionId.EarlyElection:
                    info.Name = "Call an early election";
                    info.Description = "Ask voters for a fresh mandate next month.";
                    info.Effects = $"Chance to win now: {_sim.Politics.ReelectionChance(c) * 100:0}%.";
                    if (!gov.HoldsElections() || !c.Politics.NextElection.HasValue)
                        Block(info, "This country has no competitive elections.");
                    else if (MonthsUntil(c.Politics.NextElection.Value) <= 6)
                        Block(info, "An election is already less than 6 months away.");
                    break;
            }
            if (info.Available && c.Politics.DecisionReadyOn.TryGetValue(id.ToString(), out var ready) && _sim.Date < ready)
                Block(info, $"Used recently. Available again on {ready}.");
            if (info.Available && info.CostMillions > 0 && id != DecisionId.StimulusPackage && c.Economy.Treasury < info.CostMillions)
                Block(info, "Not enough money in the treasury.");
            return info;
        }

        static void Block(DecisionInfo info, string reason)
        {
            info.Available = false;
            info.Reason = reason;
        }

        int MonthsUntil(GameDate date) => (date.Year - _sim.Date.Year) * 12 + date.Month - _sim.Date.Month;

        public bool Execute(Country c, DecisionId id, out string message)
        {
            var info = Describe(c, id);
            if (!info.Available)
            {
                message = info.Reason;
                return false;
            }
            int cooldownMonths = 12;
            switch (id)
            {
                case DecisionId.StimulusPackage:
                    _sim.Economy.Spend(c, info.CostMillions);
                    c.AddOrRefreshModifier(new Modifier("stimulus", "Stimulus package", info.Effects, 0, 365) { Growth = 1.2, Approval = 4 });
                    cooldownMonths = 24;
                    break;
                case DecisionId.PropagandaCampaign:
                    _sim.Economy.Spend(c, info.CostMillions);
                    c.AddOrRefreshModifier(new Modifier("propaganda", "Media campaign", info.Effects, 0, 180)
                    {
                        Approval = c.Politics.Government.IsAutocracy() ? 8 : 6,
                    });
                    break;
                case DecisionId.ReformProgram:
                    _sim.Economy.Spend(c, info.CostMillions);
                    c.AddOrRefreshModifier(new Modifier("reform", "Institutional reform", info.Effects, 0, 540) { Stability = 8 });
                    c.AddOrRefreshModifier(new Modifier("reform_growth", "Reform dividend", info.Effects, 0, 730) { Growth = 0.3 });
                    c.AddOrRefreshModifier(new Modifier("reform_pain", "Reform backlash", info.Effects, 0, 180) { Approval = -4 });
                    cooldownMonths = 24;
                    break;
                case DecisionId.Crackdown:
                    c.AddOrRefreshModifier(new Modifier("crackdown", "Crackdown on dissent", info.Effects, 0, 180) { Stability = 12, Approval = -10 });
                    foreach (var other in _sim.World.Countries)
                        if (other != c && !other.IsEliminated && other.Politics.Government.IsDemocracy())
                            _sim.Diplomacy.AddGoodwill(c.Index, other.Index, -10);
                    break;
                case DecisionId.EarlyElection:
                    var next = _sim.Date.AddDays(31);
                    c.Politics.NextElection = new GameDate(next.Year, next.Month, 1);
                    cooldownMonths = 24;
                    break;
            }
            c.Politics.DecisionReadyOn[id.ToString()] = _sim.Date.AddDays(30 * cooldownMonths);
            _sim.Economy.Refresh(c);
            _sim.World.Events.Publish(new ModifiersChanged(c.Tag));
            message = $"{info.Name}: done.";
            return true;
        }
    }
}
