using System;

namespace GrandStrategy.Simulation
{
    /// <summary>
    /// Monthly politics: approval and stability drift towards targets driven by the economy,
    /// policy and events; elections; protests; revolutions.
    /// </summary>
    public sealed class PoliticsSystem
    {
        readonly GameSimulation _sim;

        internal PoliticsSystem(GameSimulation sim) => _sim = sim;

        PoliticsRules R => _sim.PoliticsRules;

        internal void Initialize()
        {
            foreach (var c in _sim.World.Countries)
                ComputeTargets(c);
        }

        /// <summary>Recomputes what approval and stability are heading towards, with reasons.</summary>
        public void ComputeTargets(Country c)
        {
            var p = c.Politics;
            var e = c.Economy;
            if (p == null || e == null)
                return;

            var a = p.ApprovalTarget;
            a.Clear();
            a.Add("Baseline", p.BaseApproval);
            double growth = e.RealGrowth - e.PotentialGrowth; // better or worse than normal for this country
            a.Add("Economy", Math.Max(-R.approvalGrowthCap, Math.Min(R.approvalGrowthCap, growth * R.approvalPerGrowthPoint)));
            a.Add("Taxes", -(e.TaxRate - e.StartTaxRate) * 100 * R.approvalPerTaxPoint);
            a.Add("Welfare & health", Diff(e, SpendingCategory.Welfare) * R.approvalPerWelfarePoint);
            a.Add("Education", Diff(e, SpendingCategory.Education) * R.approvalPerEducationPoint);
            foreach (var m in c.Modifiers)
                if (m.Approval != 0)
                    a.Add(m.Name, m.Approval);

            var s = p.StabilityTarget;
            s.Clear();
            s.Add("Baseline", p.BaseStability);
            s.Add("Public approval", (p.Approval - p.BaseApproval) * R.stabilityPerApprovalPoint);
            double angerLine = Math.Min(R.angerThreshold, p.BaseApproval - 5);
            if (p.Approval < angerLine)
                s.Add("Public anger", -(angerLine - p.Approval) * R.angerPerPoint);
            double security = p.Government.IsDemocracy() ? R.stabilityPerMilitaryPointDemocracy : R.stabilityPerMilitaryPointAutocracy;
            s.Add("Security forces", Diff(e, SpendingCategory.Military) * security);
            s.Add("Administration", Diff(e, SpendingCategory.Administration) * R.stabilityPerAdminPoint);
            foreach (var m in c.Modifiers)
                if (m.Stability != 0)
                    s.Add(m.Name, m.Stability);
        }

        static double Diff(EconomyState e, SpendingCategory cat) => (e.GetSpending(cat) - e.GetStartSpending(cat)) * 100;

        internal void Monthly(GameDate date)
        {
            foreach (var c in _sim.World.Countries)
            {
                var p = c.Politics;
                if (c.IsEliminated || p == null || p.Overthrown)
                    continue;

                ComputeTargets(c);
                p.Approval = Clamp100(p.Approval + (Clamp100(p.ApprovalTarget.Total) - p.Approval) * R.approvalAdjustRate);
                p.Stability = Clamp100(p.Stability + (Clamp100(p.StabilityTarget.Total) - p.Stability) * R.stabilityAdjustRate);

                if (p.NextElection.HasValue && p.NextElection.Value.Year == date.Year && p.NextElection.Value.Month == date.Month)
                    HoldElection(c, date);

                if (p.ProtestCooldown > 0)
                    p.ProtestCooldown--;
                else if (p.Stability < R.protestThreshold)
                {
                    double chance = R.protestBaseChance + R.protestScaleChance * (R.protestThreshold - p.Stability) / R.protestThreshold;
                    if (_sim.Rng.NextDouble() < chance)
                    {
                        p.ProtestCooldown = R.protestCooldownMonths;
                        _sim.Events.RaiseProtests(c);
                    }
                }

                p.MonthsInCrisis = p.Stability < R.revolutionThreshold ? p.MonthsInCrisis + 1 : 0;
                if (p.MonthsInCrisis >= R.revolutionMonths)
                    Revolution(c, date);
            }
        }

        // ------------------------------------------------------------------ elections

        /// <summary>Chance the government wins an election held now.</summary>
        public double ReelectionChance(Country c)
        {
            var p = c.Politics;
            double needed = p.Government == GovernmentType.HybridRegime ? R.hybridWinApproval : R.democracyWinApproval;
            return Math.Max(0.05, Math.Min(0.95, 0.5 + (p.Approval - needed) / 25.0));
        }

        internal void HoldElection(Country c, GameDate date)
        {
            var p = c.Politics;
            bool won = _sim.Rng.NextDouble() < ReelectionChance(c);
            p.NextElection = new GameDate(date.Year + Math.Max(1, p.ElectionIntervalYears), date.Month, 1);

            if (won)
            {
                c.AddOrRefreshModifier(new Modifier("mandate", "Renewed mandate", "Voters gave the government another term.", 0, 180)
                {
                    Approval = 5,
                });
                _sim.News(_sim.ImportanceOf(c), $"{c.Name}: government re-elected",
                    $"The governing party won the election with {p.Approval:0}% approval.", c);
            }
            else
            {
                c.AddOrRefreshModifier(new Modifier("new_government", "New government", "A new government took office after the election.", 0, 365)
                {
                    Approval = 10,
                    Stability = -3,
                });
                if (!_sim.IsPlayer(c))
                {
                    // A new AI government shifts policy a little.
                    double shift = (_sim.Rng.NextDouble() - 0.5) * 0.02;
                    c.Economy.TaxRate = Math.Max(_sim.EconomyRules.minTaxRate, c.Economy.TaxRate + shift);
                }
                _sim.News(_sim.ImportanceOf(c), $"{c.Name}: opposition wins the election",
                    "Voters punished the government. A new cabinet takes over.", c);
            }
            _sim.Events.RaiseElectionResult(c, won);
            _sim.World.Events.Publish(new ModifiersChanged(c.Tag));
        }

        // ------------------------------------------------------------------ revolution

        void Revolution(Country c, GameDate date)
        {
            var p = c.Politics;
            p.MonthsInCrisis = 0;
            if (_sim.IsPlayer(c))
            {
                p.Overthrown = true;
                _sim.News(NewsImportance.Player, $"Revolution in {c.Name}!",
                    "Months of chaos ended with the government overthrown.", c);
                return;
            }

            var old = p.Government;
            double roll = _sim.Rng.NextDouble();
            p.Government = old.IsDemocracy()
                ? (roll < 0.5 ? GovernmentType.HybridRegime : GovernmentType.Authoritarian)
                : old == GovernmentType.HybridRegime
                    ? (roll < 0.6 ? GovernmentType.Authoritarian : GovernmentType.FlawedDemocracy)
                    : (roll < 0.5 ? GovernmentType.HybridRegime : GovernmentType.Authoritarian);
            p.BaseStability = 40;
            p.BaseApproval = 50;
            p.Stability = 35;
            p.Approval = 55;
            p.NextElection = p.Government.HoldsElections() ? new GameDate(date.Year + 2, date.Month, 1) : (GameDate?)null;
            p.ElectionIntervalYears = p.Government.HoldsElections() ? 4 : 0;
            c.AddOrRefreshModifier(new Modifier("new_regime", "New regime", "A new regime is consolidating power.", 0, 365)
            {
                Stability = 10,
            });
            _sim.World.Events.Publish(new GovernmentOverthrown(c.Tag, false));
            _sim.News(_sim.ImportanceOf(c), $"Revolution in {c.Name}",
                $"The government was overthrown. New system: {p.Government.DisplayName()}.", c);
        }

        static double Clamp100(double v) => Math.Max(0, Math.Min(100, v));
    }
}
