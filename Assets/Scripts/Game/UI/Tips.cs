using System.Linq;
using GrandStrategy.Simulation;
using UnityEngine;

namespace GrandStrategy.Game.UI
{
    /// <summary>Tooltip builders shared by the top bar and the country panel.</summary>
    public static class Tips
    {
        public static TooltipContent Budget(Country c)
        {
            if (c?.Economy == null) return null;
            var e = c.Economy;
            var t = new TooltipContent("Monthly budget", "When the treasury runs out, deficits are covered by borrowing.");
            foreach (var f in e.MonthlyIncome.Factors)
                t.Row(f.Label, Ui.FormatSignedMoney(f.Value), Ui.Good);
            foreach (var f in e.MonthlyCosts.Factors)
                t.Row(f.Label, Ui.FormatSignedMoney(-f.Value), Ui.Bad);
            t.WithTotal("Balance per month", Ui.FormatSignedMoney(e.MonthlyBalance), e.MonthlyBalance >= 0 ? Ui.Good : Ui.Bad);
            return t;
        }

        public static TooltipContent Growth(Country c)
        {
            if (c?.Economy == null) return null;
            var e = c.Economy;
            return new TooltipContent("Real GDP growth", $"Per year, after inflation ({e.Inflation:0.0}%). Effects of policy are compared with the 2026 starting point.")
                .Factors(e.GrowthFactors, v => Ui.FormatGrowth(v))
                .WithTotal("Real growth", Ui.FormatGrowth(e.RealGrowth), e.RealGrowth >= 0 ? Ui.Good : Ui.Bad);
        }

        public static TooltipContent Debt(Country c)
        {
            if (c?.Economy == null) return null;
            var e = c.Economy;
            return new TooltipContent("Government debt", "If interest takes more than 55% of tax revenue for 6 months, the country defaults.")
                .Row("Debt", Ui.FormatMoneyMillions(e.Debt))
                .Row("Debt / GDP", Ui.FormatPercent(e.DebtToGdp))
                .Row("Interest rate", Ui.FormatPercent1(e.InterestRate))
                .Row("Interest / tax revenue", Ui.FormatPercent(e.InterestBurden), e.InterestBurden > 0.35 ? Ui.Bad : Ui.Text)
                .WithTotal("Credit rating", e.Rating.ToString());
        }

        public static TooltipContent Stability(Country c)
        {
            if (c?.Politics == null) return null;
            var p = c.Politics;
            return new TooltipContent($"Stability {p.Stability:0}", "Below 35: protests. Below 10 for 3 months: revolution.")
                .Factors(p.StabilityTarget, v => Ui.FormatSigned(v, "0"))
                .WithTotal("Heading towards", Mathf.Clamp((float)p.StabilityTarget.Total, 0, 100).ToString("0"));
        }

        public static TooltipContent Approval(Country c)
        {
            if (c?.Politics == null) return null;
            var p = c.Politics;
            var t = new TooltipContent($"Approval {p.Approval:0}%", "Support for the government. Very low approval (anger) drags stability down.")
                .Factors(p.ApprovalTarget, v => Ui.FormatSigned(v, "0"))
                .WithTotal("Heading towards", Mathf.Clamp((float)p.ApprovalTarget.Total, 0, 100).ToString("0") + "%");
            if (p.NextElection.HasValue)
                t.Footer = $"Next election: {p.NextElection.Value.MonthName} {p.NextElection.Value.Year}.";
            return t;
        }

        public static TooltipContent Relations(GameSimulation sim, Country a, Country b)
        {
            if (sim == null || a == null || b == null) return null;
            var target = sim.DiplomacySystem.RelationsTarget(a, b);
            return new TooltipContent($"Relations {sim.Diplomacy.Relations(a, b):+0;-0;0}", "Relations drift slowly towards their target.")
                .Factors(target, v => Ui.FormatSigned(v, "0"))
                .WithTotal("Heading towards", Mathf.Clamp((float)target.Total, -100, 100).ToString("+0;-0;0"));
        }

        public static TooltipContent Opinion(ActionPreview p, string country)
        {
            if (p?.Opinion == null) return null;
            var t = new TooltipContent(p.WillAccept ? $"{country} will accept" : $"{country} will refuse",
                "Their reasons (the total must be 0 or more):");
            foreach (var f in p.Opinion.Factors.Where(f => System.Math.Abs(f.Value) >= 0.5))
                t.Row(f.Label, Ui.FormatSigned(f.Value, "0"), f.Value >= 0 ? Ui.Good : Ui.Bad);
            foreach (var f in p.Opinion.Factors.Where(f => System.Math.Abs(f.Value) < 0.5))
                t.Row(f.Label, "required", Ui.Warn);
            t.WithTotal("Total", Ui.FormatSigned(p.Opinion.Total, "0"), p.WillAccept ? Ui.Good : Ui.Bad);
            return t;
        }

        public static TooltipContent Modifier(Modifier m)
        {
            var t = new TooltipContent(m.Name, m.Description);
            if (m.Growth != 0) t.Row("Growth", Ui.FormatGrowth(m.Growth), m.Growth > 0 ? Ui.Good : Ui.Bad);
            if (m.Approval != 0) t.Row("Approval", Ui.FormatSigned(m.Approval, "0"), m.Approval > 0 ? Ui.Good : Ui.Bad);
            if (m.Stability != 0) t.Row("Stability", Ui.FormatSigned(m.Stability, "0"), m.Stability > 0 ? Ui.Good : Ui.Bad);
            if (m.InterestPremium != 0) t.Row("Interest rate", Ui.FormatSigned(m.InterestPremium * 100, "0.0") + " pts", m.InterestPremium < 0 ? Ui.Good : Ui.Bad);
            if (m.Morale != 0) t.Row("Morale", Ui.FormatSigned(m.Morale * 100, "0") + "%", m.Morale > 0 ? Ui.Good : Ui.Bad);
            t.Footer = m.IsPermanent ? "Permanent" : $"{m.DaysRemaining} days left";
            return t;
        }
    }
}
