using System;
using System.Collections.Generic;
using GrandStrategy.Simulation.Data;

namespace GrandStrategy.Simulation
{
    /// <summary>Turns nations.json and diplomacy.json into country and diplomacy state.</summary>
    internal static class NationSetup
    {
        internal static void Apply(GameSimulation sim, NationFile nations, DiplomacyFile diplomacy)
        {
            var world = sim.World;
            var records = new Dictionary<string, NationRecord>();
            if (nations?.nations != null)
                foreach (var r in nations.nations)
                    if (!string.IsNullOrEmpty(r.tag))
                        records[r.tag] = r;

            foreach (var c in world.Countries)
            {
                if (!records.TryGetValue(c.Tag, out var r))
                {
                    r = Default(c.Tag);
                    world.AddLoadWarning($"No nation data for {c.Name} ({c.Tag}); using defaults.");
                }
                SetUp(sim, c, r);
            }

            var d = sim.Diplomacy;
            if (diplomacy?.blocs != null)
            {
                foreach (var b in diplomacy.blocs)
                {
                    if (b?.members == null)
                        continue;
                    var kind = ParseKind(b.kind);
                    var members = new List<string>();
                    foreach (var m in b.members)
                        if (world.GetCountry(m) != null)
                            members.Add(m);
                    d.Blocs.Add(new Bloc(b.id, b.name ?? b.id, kind, members));
                }
            }
            if (diplomacy?.relations != null)
            {
                foreach (var rel in diplomacy.relations)
                {
                    var a = world.GetCountry(rel.a);
                    var b = world.GetCountry(rel.b);
                    if (a != null && b != null && a != b)
                        d.SetOverride(a.Index, b.Index, Math.Max(-100, Math.Min(100, rel.value)));
                }
            }
            if (diplomacy?.sanctions != null)
            {
                foreach (var s in diplomacy.sanctions)
                {
                    var target = world.GetCountry(s.target);
                    if (target == null || s.by == null)
                        continue;
                    foreach (var tag in s.by)
                    {
                        var by = world.GetCountry(tag);
                        if (by != null && by != target)
                            d.AddSanctions(by, target);
                    }
                }
            }
            d.MarkStartSanctions();
        }

        static void SetUp(GameSimulation sim, Country c, NationRecord r)
        {
            double gdp = sim.World.GdpOf(c);
            var e = new EconomyState
            {
                TaxRate = r.taxRate,
                StartTaxRate = r.taxRate,
                PotentialGrowth = r.realGrowth,
                Inflation = r.inflation,
                PopulationGrowth = r.populationGrowth,
                SafeDebtToGdp = r.safeDebtToGdp > 0 ? r.safeDebtToGdp : 0.6,
                ForeignAid = r.foreignAid,
                BaseInterestRate = r.interestRate,
                InterestRate = r.interestRate,
                Debt = r.debtToGdp * gdp,
                StartDebtToGdp = r.debtToGdp,
                Treasury = gdp * r.taxRate / 12,
                Gdp = gdp,
            };
            SetSpending(e, SpendingCategory.Military, r.militarySpending);
            SetSpending(e, SpendingCategory.Welfare, r.welfareSpending);
            SetSpending(e, SpendingCategory.Education, r.educationSpending);
            SetSpending(e, SpendingCategory.Infrastructure, r.infrastructureSpending);
            SetSpending(e, SpendingCategory.Administration, r.adminSpending);
            c.Economy = e;

            GovernmentTypes.TryParse(r.government, out var government);
            var p = new PoliticsState
            {
                Government = government,
                Stability = Clamp(r.stability),
                Approval = Clamp(r.approval),
                BaseStability = Clamp(r.stability),
                BaseApproval = Clamp(r.approval),
                ElectionIntervalYears = Math.Max(0, r.electionInterval),
            };
            if (government.HoldsElections() && r.electionYear > 0)
            {
                int interval = Math.Max(1, r.electionInterval);
                int year = r.electionYear;
                int month = Math.Max(1, Math.Min(12, r.electionMonth));
                var start = sim.World.Clock.Date;
                while (year < start.Year || (year == start.Year && month <= start.Month))
                    year += interval;
                p.NextElection = new GameDate(year, month, 1);
                p.ElectionIntervalYears = interval;
            }
            c.Politics = p;
        }

        static void SetSpending(EconomyState e, SpendingCategory cat, double share)
        {
            e.Spending[(int)cat] = Math.Max(0, share);
            e.StartSpending[(int)cat] = Math.Max(0, share);
        }

        static NationRecord Default(string tag) => new NationRecord
        {
            tag = tag,
            government = "FlawedDemocracy",
            taxRate = 0.2,
            debtToGdp = 0.5,
            interestRate = 0.06,
            safeDebtToGdp = 0.6,
            militarySpending = 0.015,
            welfareSpending = 0.07,
            educationSpending = 0.04,
            infrastructureSpending = 0.035,
            adminSpending = 0.05,
            realGrowth = 3,
            inflation = 3,
            populationGrowth = 0.01,
            stability = 55,
            approval = 50,
        };

        static BlocKind ParseKind(string kind)
        {
            switch ((kind ?? "").ToLowerInvariant())
            {
                case "alliance": return BlocKind.Alliance;
                case "union": return BlocKind.Union;
                case "trade": return BlocKind.Trade;
                default: return BlocKind.Forum;
            }
        }

        static double Clamp(double v) => Math.Max(0, Math.Min(100, v));
    }
}
