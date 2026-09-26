using System;

namespace GrandStrategy.Simulation
{
    /// <summary>
    /// Monthly economy: growth, budget, debt, interest, credit rating and debt crises.
    /// Growth effects of policy are measured against each country's starting policy, so the
    /// world starts in balance and the player's choices move it.
    /// </summary>
    public sealed class EconomySystem
    {
        public const string DefaultModifierId = "debt_default";

        readonly GameSimulation _sim;

        internal EconomySystem(GameSimulation sim) => _sim = sim;

        EconomyRules R => _sim.EconomyRules;

        internal void Initialize()
        {
            foreach (var c in _sim.World.Countries)
            {
                var e = c.Economy;
                e.Gdp = _sim.GdpOf(c);
                e.StartTradeEffect = TradeEffect(c);
                e.StartSanctionEffect = SanctionEffect(c);
                e.StartSanctionCost = SanctionCost(c);
                ComputeGrowth(c);
                ComputeBudget(c);
                UpdateRating(c);
            }
        }

        internal void Monthly(GameDate date)
        {
            foreach (var c in _sim.World.Countries)
            {
                if (c.IsEliminated || c.Economy == null)
                    continue;
                TickCountry(c, date);
            }
            if (date.Month == 1)
                foreach (var c in _sim.World.Countries)
                    if (!c.IsEliminated && !_sim.IsPlayer(c))
                        AiFiscalPolicy(c);
        }

        void TickCountry(Country c, GameDate date)
        {
            var e = c.Economy;
            double gdp = _sim.GdpOf(c);
            if (gdp <= 0)
                return;

            e.Cycle = e.Cycle * R.cyclePersistence + NextGaussian() * R.cycleVolatility;
            // High inflation slowly comes down towards a normal level.
            e.Inflation += (R.longRunInflation - e.Inflation) * R.disinflationRate;
            ComputeGrowth(c);

            double nominal = Math.Max(-60, e.RealGrowth + e.Inflation);
            double factor = Math.Pow(1 + nominal / 100.0, 1 / 12.0);
            double popFactor = Math.Pow(1 + e.PopulationGrowth, 1 / 12.0);
            foreach (var id in c.ProvinceIds)
            {
                var p = _sim.World.GetProvince(id);
                p.GdpMillions *= factor;
                p.Population = (long)Math.Round(p.Population * popFactor);
            }
            e.Gdp = gdp * factor;

            ComputeBudget(c);
            e.Treasury += e.MonthlyBalance;
            if (e.Treasury < 0)
            {
                e.Debt += -e.Treasury; // deficits are financed by borrowing
                e.Treasury = 0;
            }

            UpdateInterest(c);
            UpdateRating(c);
            CheckDebtCrisis(c, date);
        }

        // ------------------------------------------------------------------ growth

        /// <summary>Recomputes real growth and its breakdown from current policy (safe to call any time).</summary>
        public void ComputeGrowth(Country c)
        {
            var e = c.Economy;
            var g = e.GrowthFactors;
            g.Clear();
            g.Add("Economic potential", e.PotentialGrowth);

            double taxDiff = (e.TaxRate - e.StartTaxRate) * 100;
            g.Add("Tax level", taxDiff > 0 ? -taxDiff * R.taxDragPerPoint : -taxDiff * R.taxCutBoostPerPoint);

            double infra = (e.GetSpending(SpendingCategory.Infrastructure) - e.GetStartSpending(SpendingCategory.Infrastructure)) * 100;
            g.Add("Infrastructure investment", Clamp(infra * R.infrastructurePerPoint, R.infrastructureCap));
            double edu = (e.GetSpending(SpendingCategory.Education) - e.GetStartSpending(SpendingCategory.Education)) * 100;
            g.Add("Education", Clamp(edu * R.educationPerPoint, R.educationCap));

            if (c.Politics != null)
                g.Add("Stability", (c.Politics.Stability - c.Politics.BaseStability) * R.stabilityGrowthPerPoint);

            double safe = Math.Max(e.SafeDebtToGdp, e.StartDebtToGdp);
            g.Add("Debt burden", -Math.Max(0, e.DebtToGdp - safe) * R.debtOverhangPerUnit);

            g.Add("Trade agreements", TradeEffect(c) - e.StartTradeEffect);
            g.Add("Sanctions against us", -(SanctionEffect(c) - e.StartSanctionEffect));
            g.Add("Cost of our sanctions", -(SanctionCost(c) - e.StartSanctionCost));

            foreach (var m in c.Modifiers)
                if (m.Growth != 0)
                    g.Add(m.Name, m.Growth);

            g.Add("Business cycle", e.Cycle);
            e.RealGrowth = Math.Max(-15, Math.Min(15, g.Total));
        }

        internal double TradeEffect(Country c)
        {
            var d = _sim.Diplomacy;
            double total = 0;
            foreach (var other in _sim.World.Countries)
            {
                if (other == c || other.IsEliminated || !d.AreTradePartners(c, other) || d.AnySanctions(c, other))
                    continue;
                total += R.tradeBasePerPartner + R.tradePerWorldShare * _sim.GdpShare(other);
            }
            return Math.Min(R.tradeCap, total);
        }

        internal double SanctionEffect(Country c)
        {
            double share = 0;
            foreach (var other in _sim.World.Countries)
                if (other != c && !other.IsEliminated && _sim.Diplomacy.Sanctions(other, c))
                    share += _sim.GdpShare(other);
            return Math.Min(R.sanctionsCap, share * R.sanctionsPerWorldShare);
        }

        internal double SanctionCost(Country c)
        {
            double share = 0;
            foreach (var other in _sim.World.Countries)
                if (other != c && !other.IsEliminated && _sim.Diplomacy.Sanctions(c, other))
                    share += _sim.GdpShare(other);
            return Math.Min(R.sanctionerCostCap, share * R.sanctionerCostPerWorldShare);
        }

        // ------------------------------------------------------------------ budget

        /// <summary>Monthly income and costs at current policy (safe to call any time).</summary>
        public void ComputeBudget(Country c)
        {
            var e = c.Economy;
            double gdp = e.Gdp;
            e.MonthlyIncome.Clear();
            e.MonthlyCosts.Clear();

            double startAdmin = e.GetStartSpending(SpendingCategory.Administration);
            double collection = startAdmin > 0
                ? R.adminCollectionFloor + (1 - R.adminCollectionFloor) * Math.Min(1, e.GetSpending(SpendingCategory.Administration) / startAdmin)
                : 1;
            e.MonthlyIncome.Add("Taxes", gdp * e.TaxRate * collection / 12);
            if (e.ForeignAid > 0)
                e.MonthlyIncome.Add("Foreign aid", gdp * e.ForeignAid / 12);

            e.MonthlyCosts.Add("Military", gdp * e.GetSpending(SpendingCategory.Military) / 12);
            e.MonthlyCosts.Add("Welfare & health", gdp * e.GetSpending(SpendingCategory.Welfare) / 12);
            e.MonthlyCosts.Add("Education", gdp * e.GetSpending(SpendingCategory.Education) / 12);
            e.MonthlyCosts.Add("Infrastructure", gdp * e.GetSpending(SpendingCategory.Infrastructure) / 12);
            e.MonthlyCosts.Add("Administration", gdp * e.GetSpending(SpendingCategory.Administration) / 12);
            e.MonthlyCosts.Add("Interest on debt", e.Debt * e.InterestRate / 12);
        }

        void UpdateInterest(Country c)
        {
            var e = c.Economy;
            double safe = Math.Max(e.SafeDebtToGdp, e.StartDebtToGdp);
            double target = e.BaseInterestRate + Math.Max(0, e.DebtToGdp - safe) * R.riskPremiumPerUnit;
            if (c.Politics != null && c.Politics.Stability < 40)
                target += (40 - c.Politics.Stability) / 40 * R.instabilityPremium;
            foreach (var m in c.Modifiers)
                target += m.InterestPremium;
            e.InterestRate += (target - e.InterestRate) / Math.Max(1, R.interestAdjustMonths);
            e.InterestRate = Math.Max(0, e.InterestRate);
        }

        void UpdateRating(Country c)
        {
            var e = c.Economy;
            if (c.HasModifier(DefaultModifierId))
            {
                e.Rating = CreditRating.D;
                return;
            }
            double safe = Math.Max(e.SafeDebtToGdp, 0.3);
            double stability = c.Politics?.Stability ?? 50;
            double score = e.InterestBurden * 10 + Math.Max(0, e.DebtToGdp / safe - 0.5) * 2 + Math.Max(0, 60 - stability) / 20;
            e.Rating = score < 0.8 ? CreditRating.AAA
                : score < 1.4 ? CreditRating.AA
                : score < 2.0 ? CreditRating.A
                : score < 2.8 ? CreditRating.BBB
                : score < 3.6 ? CreditRating.BB
                : score < 4.6 ? CreditRating.B
                : CreditRating.CCC;
        }

        void CheckDebtCrisis(Country c, GameDate date)
        {
            var e = c.Economy;
            if (c.HasModifier(DefaultModifierId))
            {
                e.MonthsOverBurden = 0;
                return;
            }
            e.MonthsOverBurden = e.InterestBurden > R.defaultBurden ? e.MonthsOverBurden + 1 : 0;
            if (e.MonthsOverBurden >= R.defaultMonths)
                Default(c);
        }

        /// <summary>The country can't pay its creditors: debt is cut, but the economy and government suffer.</summary>
        public void Default(Country c)
        {
            var e = c.Economy;
            e.MonthsOverBurden = 0;
            e.Debt *= 1 - R.defaultHaircut;
            c.AddOrRefreshModifier(new Modifier(DefaultModifierId, "Debt default",
                "We could not pay our creditors. Markets and voters punish us.", 0, 3 * 365)
            {
                InterestPremium = R.defaultPremium,
            });
            c.AddOrRefreshModifier(new Modifier("default_shock", "Default shock",
                "The collapse of confidence hits the economy and the government.", 0, 365)
            {
                Growth = -R.defaultGrowthPenalty,
                Approval = -20,
                Stability = -15,
            });
            UpdateRating(c);
            _sim.World.Events.Publish(new ModifiersChanged(c.Tag));
            _sim.News(_sim.ImportanceOf(c), $"{c.Name} defaults on its debt",
                "Interest payments became unbearable. Creditors take a 30% loss, the economy is in shock.", c);
            _sim.Events.RaiseDebtDefault(c);
        }

        // ------------------------------------------------------------------ actions

        public void SetTaxRate(Country c, double rate)
        {
            var e = c.Economy;
            e.TaxRate = Math.Max(R.minTaxRate, Math.Min(R.maxTaxRate, rate));
            Refresh(c);
        }

        public void SetSpending(Country c, SpendingCategory category, double share)
        {
            var e = c.Economy;
            e.Spending[(int)category] = Math.Max(0, Math.Min(R.maxSpendingShare, share));
            Refresh(c);
        }

        /// <summary>Borrows money into the treasury (adds to debt).</summary>
        public void Borrow(Country c, double amountMillions)
        {
            if (amountMillions <= 0)
                return;
            c.Economy.Debt += amountMillions;
            c.Economy.Treasury += amountMillions;
            Refresh(c);
        }

        /// <summary>Pays back debt from the treasury. Returns the amount repaid.</summary>
        public double Repay(Country c, double amountMillions)
        {
            var e = c.Economy;
            double paid = Math.Max(0, Math.Min(amountMillions, Math.Min(e.Treasury, e.Debt)));
            e.Treasury -= paid;
            e.Debt -= paid;
            Refresh(c);
            return paid;
        }

        /// <summary>Spends from the treasury, borrowing whatever is missing.</summary>
        internal void Spend(Country c, double amountMillions)
        {
            var e = c.Economy;
            e.Treasury -= amountMillions;
            if (e.Treasury < 0)
            {
                e.Debt += -e.Treasury;
                e.Treasury = 0;
            }
        }

        /// <summary>Updates the previews (budget, growth, political targets) after a policy change.</summary>
        internal void Refresh(Country c)
        {
            ComputeBudget(c);
            ComputeGrowth(c);
            _sim.Politics.ComputeTargets(c);
            _sim.World.Events.Publish(new PolicyChanged(c.Tag));
        }

        // ------------------------------------------------------------------ AI

        /// <summary>Once a year AI governments tighten the budget when debt gets out of hand.</summary>
        void AiFiscalPolicy(Country c)
        {
            var e = c.Economy;
            double safe = Math.Max(e.SafeDebtToGdp, e.StartDebtToGdp);
            double gdp = e.Gdp;
            if (gdp <= 0)
                return;
            double yearlyBalance = e.MonthlyBalance * 12 / gdp;
            if (e.InterestBurden > 0.35 || e.DebtToGdp > safe * 1.1)
            {
                e.TaxRate = Math.Min(Math.Min(R.maxTaxRate, e.StartTaxRate + 0.05), e.TaxRate + 0.005);
                e.Spending[(int)SpendingCategory.Welfare] *= 0.98;
                e.Spending[(int)SpendingCategory.Administration] *= 0.98;
            }
            else if (yearlyBalance > 0.01)
            {
                e.TaxRate = Math.Max(e.StartTaxRate - 0.03, e.TaxRate - 0.003);
            }
            ComputeBudget(c);
        }

        double NextGaussian()
        {
            double u1 = 1.0 - _sim.Rng.NextDouble();
            double u2 = _sim.Rng.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
        }

        static double Clamp(double v, double cap) => Math.Max(-cap, Math.Min(cap, v));
    }
}
