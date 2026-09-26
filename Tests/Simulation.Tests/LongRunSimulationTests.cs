using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace GrandStrategy.Simulation.Tests
{
    /// <summary>
    /// Runs the real world for ten years with nobody touching anything and checks that it stays
    /// believable. Catches runaway numbers long before a player would see them.
    /// </summary>
    public class LongRunSimulationTests
    {
        readonly ITestOutputHelper _output;

        public LongRunSimulationTests(ITestOutputHelper output) => _output = output;

        [Theory]
        [InlineData(2026)]
        [InlineData(7)]
        public void TenYearsStayBelievable(int seed)
        {
            var sim = WorldDataTests.LoadSimulation(seed);
            var world = sim.World;
            world.SetPlayer("FRA");
            int revolutions = 0, defaults = 0, elections = 0;
            world.Events.Subscribe<GovernmentOverthrown>(_ => revolutions++);
            world.Events.Subscribe<NewsPublished>(n =>
            {
                if (n.Item.Title.Contains("defaults")) defaults++;
                if (n.Item.Title.Contains("elect")) elections++;
            });

            double growthSum = 0;
            int months = 120;
            for (int m = 0; m < months; m++)
            {
                NationWorld.RunMonths(sim, 1);
                // The player makes no choices in this test.
                foreach (var e in sim.Events.Pending.ToList())
                    sim.Events.Choose(e, 0);
                double weighted = world.Countries.Where(c => !c.IsEliminated)
                    .Sum(c => c.Economy.RealGrowth * sim.GdpShare(c));
                growthSum += weighted;
            }

            var alive = world.Countries.Where(c => !c.IsEliminated).ToList();
            foreach (var c in alive)
            {
                Assert.False(double.IsNaN(c.Economy.Debt) || double.IsNaN(c.Economy.RealGrowth), c.Name);
                Assert.False(double.IsNaN(c.Politics.Approval) || double.IsNaN(c.Politics.Stability), c.Name);
                Assert.InRange(c.Politics.Approval, 0, 100);
                Assert.InRange(c.Politics.Stability, 0, 100);
            }
            foreach (var a in alive)
                foreach (var b in alive)
                    Assert.InRange(sim.Diplomacy.Relations(a, b), -100, 100);

            double worldGrowth = growthSum / months;
            var debts = alive.Select(c => c.Economy.DebtToGdp).OrderBy(d => d).ToList();
            double medianDebt = debts[debts.Count / 2];
            var stabilities = alive.Select(c => c.Politics.Stability).OrderBy(s => s).ToList();

            _output.WriteLine($"world growth {worldGrowth:0.00}%/yr, median debt {medianDebt:P0}, max debt {debts.Last():P0}");
            _output.WriteLine($"revolutions {revolutions}, defaults {defaults}, elections {elections}");
            _output.WriteLine($"stability: min {stabilities.First():0}, median {stabilities[stabilities.Count / 2]:0}");
            foreach (var tag in new[] { "USA", "CHN", "DEU", "IND", "RUS", "JPN", "BRA", "EGY", "PAK" })
            {
                var c = world.GetCountry(tag);
                _output.WriteLine($"{tag}: growth {c.Economy.RealGrowth:0.0}% debt {c.Economy.DebtToGdp:P0} rating {c.Economy.Rating} " +
                                  $"approval {c.Politics.Approval:0} stability {c.Politics.Stability:0}");
            }

            Assert.InRange(worldGrowth, 1.5, 4.5);
            Assert.InRange(medianDebt, 0.2, 1.5);
            Assert.True(revolutions < 20, $"{revolutions} revolutions");
            Assert.True(defaults < 30, $"{defaults} defaults");
            Assert.True(elections > 50, $"only {elections} elections");
        }
    

        [Fact]
        public void CrushingTaxesAndNoWelfareEndInRevolution()
        {
            var sim = WorldDataTests.LoadSimulation();
            var world = sim.World;
            world.SetPlayer("FRA");
            var fra = world.GetCountry("FRA");
            sim.Economy.SetTaxRate(fra, 0.60);
            sim.Economy.SetSpending(fra, SpendingCategory.Welfare, 0);
            bool overthrown = false;
            world.Events.Subscribe<GovernmentOverthrown>(e => overthrown |= e.IsPlayer);
            int month = 0;
            for (; month < 48 && !overthrown; month++)
            {
                NationWorld.RunMonths(sim, 1);
                foreach (var e in sim.Events.Pending.ToList())
                    sim.Events.Choose(e, e.Options.Count - 1); // always the passive choice
            }
            _output.WriteLine($"overthrown after {month} months, approval {fra.Politics.Approval:0}, stability {fra.Politics.Stability:0}");
            Assert.True(overthrown, $"approval {fra.Politics.Approval:0}, stability {fra.Politics.Stability:0}");
            Assert.InRange(month, 6, 36);
        }
    }
}
