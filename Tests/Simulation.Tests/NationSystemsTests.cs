using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace GrandStrategy.Simulation.Tests
{
    public class EconomyTests
    {
        [Fact]
        public void StartsInBalance()
        {
            var sim = NationWorld.Create();
            var dem = sim.World.GetCountry("DEM");
            Assert.Equal(2.0, dem.Economy.RealGrowth, 1);
            Assert.Equal(0.6, dem.Economy.DebtToGdp, 3);
            Assert.Equal(50, dem.Politics.ApprovalTarget.Total, 3);
        }

        [Fact]
        public void DeficitsBecomeDebt()
        {
            var sim = NationWorld.Create();
            var dem = sim.World.GetCountry("DEM");
            sim.Economy.SetTaxRate(dem, 0.10);
            double before = dem.Economy.Debt;
            NationWorld.RunMonths(sim, 3);
            Assert.True(dem.Economy.Debt > before, "a big deficit should add debt");
            Assert.Equal(0, dem.Economy.Treasury, 3);
        }

        [Fact]
        public void HighTaxesSlowGrowthAndAngerVoters()
        {
            var sim = NationWorld.Create();
            var dem = sim.World.GetCountry("DEM");
            sim.Economy.SetTaxRate(dem, 0.45);
            Assert.Contains(dem.Economy.GrowthFactors.Factors, f => f.Label == "Tax level" && f.Value < -1);
            Assert.Contains(dem.Politics.ApprovalTarget.Factors, f => f.Label == "Taxes" && f.Value < -10);
            Assert.True(dem.Economy.MonthlyBalance > 0);
        }

        [Fact]
        public void InfrastructureBoostsGrowth()
        {
            var sim = NationWorld.Create();
            var dem = sim.World.GetCountry("DEM");
            sim.Economy.SetSpending(dem, SpendingCategory.Infrastructure, 0.06);
            Assert.Contains(dem.Economy.GrowthFactors.Factors, f => f.Label == "Infrastructure investment" && f.Value > 0.5);
        }

        [Fact]
        public void GrowthMovesProvinceGdp()
        {
            var sim = NationWorld.Create();
            var dem = sim.World.GetCountry("DEM");
            double before = sim.World.GdpOf(dem);
            NationWorld.RunMonths(sim, 12);
            double after = sim.World.GdpOf(dem);
            Assert.InRange(after / before, 1.02, 1.06); // ~2% real + 2% inflation
        }

        [Fact]
        public void UnpayableInterestLeadsToDefault()
        {
            var sim = NationWorld.Create(n =>
            {
                n["SML"].debtToGdp = 3.0;
                n["SML"].interestRate = 0.12;
                n["SML"].taxRate = 0.2;
                n["SML"].safeDebtToGdp = 0.5;
            });
            var sml = sim.World.GetCountry("SML");
            NationWorld.RunMonths(sim, 8);
            Assert.Equal(CreditRating.D, sml.Economy.Rating);
            Assert.True(sml.HasModifier(EconomySystem.DefaultModifierId));
            Assert.True(sml.Economy.DebtToGdp < 3.0 * 0.8);
        }

        [Fact]
        public void BorrowAndRepay()
        {
            var sim = NationWorld.Create();
            var dem = sim.World.GetCountry("DEM");
            double debt = dem.Economy.Debt;
            sim.Economy.Borrow(dem, 1000);
            Assert.Equal(debt + 1000, dem.Economy.Debt, 3);
            double paid = sim.Economy.Repay(dem, 500);
            Assert.Equal(500, paid, 3);
            Assert.Equal(debt + 500, dem.Economy.Debt, 3);
        }
    }

    public class PoliticsTests
    {
        [Fact]
        public void ApprovalFollowsPolicy()
        {
            var sim = NationWorld.Create();
            var dem = sim.World.GetCountry("DEM");
            sim.Economy.SetSpending(dem, SpendingCategory.Welfare, 0.20);
            NationWorld.RunMonths(sim, 12);
            Assert.True(dem.Politics.Approval > 55, $"approval was {dem.Politics.Approval}");
        }

        [Fact]
        public void ElectionsHappenOnScheduleAndReschedule()
        {
            var sim = NationWorld.Create(n => { n["ALY"].electionYear = 2026; n["ALY"].electionMonth = 3; });
            var aly = sim.World.GetCountry("ALY");
            var news = new List<NewsItem>();
            sim.World.Events.Subscribe<NewsPublished>(e => news.Add(e.Item));
            NationWorld.RunMonths(sim, 3);
            Assert.Contains(news, n => n.CountryTag == "ALY" && n.Title.Contains("elect"));
            Assert.Equal(new GameDate(2030, 3, 1), aly.Politics.NextElection);
        }

        [Fact]
        public void PlayerIsOverthrownAfterThreeMonthsOfChaos()
        {
            var sim = NationWorld.Create();
            var dem = sim.World.GetCountry("DEM");
            var fallen = new List<GovernmentOverthrown>();
            sim.World.Events.Subscribe<GovernmentOverthrown>(fallen.Add);
            dem.AddOrRefreshModifier(new Modifier("chaos", "Chaos", "", 0, Modifier.Permanent) { Stability = -200 });
            dem.Politics.Stability = 5;
            NationWorld.RunMonths(sim, 4);
            Assert.True(dem.Politics.Overthrown);
            Assert.Contains(fallen, f => f.CountryTag == "DEM" && f.IsPlayer);
        }

        [Fact]
        public void AiRevolutionChangesTheRegime()
        {
            var sim = NationWorld.Create();
            var aut = sim.World.GetCountry("AUT");
            aut.AddOrRefreshModifier(new Modifier("chaos", "Chaos", "", 0, 100) { Stability = -200 });
            aut.Politics.Stability = 5;
            NationWorld.RunMonths(sim, 4);
            Assert.False(aut.Politics.Overthrown);
            Assert.True(aut.HasModifier("new_regime"));
        }

        [Fact]
        public void DemocraciesCannotCrackDown()
        {
            var sim = NationWorld.Create();
            var info = sim.Decisions.Describe(sim.World.GetCountry("DEM"), DecisionId.Crackdown);
            Assert.False(info.Available);
            Assert.True(sim.Decisions.Describe(sim.World.GetCountry("AUT"), DecisionId.Crackdown).Available);
        }

        [Fact]
        public void DecisionsHaveCooldowns()
        {
            var sim = NationWorld.Create();
            var dem = sim.World.GetCountry("DEM");
            Assert.True(sim.Decisions.Execute(dem, DecisionId.StimulusPackage, out _));
            Assert.True(dem.HasModifier("stimulus"));
            Assert.False(sim.Decisions.Execute(dem, DecisionId.StimulusPackage, out var why));
            Assert.Contains("recently", why);
        }

        [Fact]
        public void PlayerEventsWaitForAChoice()
        {
            var sim = NationWorld.Create();
            var dem = sim.World.GetCountry("DEM");
            sim.Events.RaiseProtests(dem);
            Assert.Single(sim.Events.Pending);
            var evt = sim.Events.Pending[0];
            sim.Events.Choose(evt, 1); // crack down
            Assert.Empty(sim.Events.Pending);
            Assert.True(dem.HasModifier("protest_crackdown"));
        }
    }

    public class DiplomacyTests
    {
        static Country C(GameSimulation s, string tag) => s.World.GetCountry(tag);

        [Fact]
        public void StartingRelationsComeFromHistoryAndBlocs()
        {
            var sim = NationWorld.Create();
            Assert.Equal(-60, sim.Diplomacy.Relations(C(sim, "DEM"), C(sim, "AUT")), 1);
            Assert.True(sim.Diplomacy.Relations(C(sim, "DEM"), C(sim, "ALY")) >= 60);
            Assert.True(sim.Diplomacy.AreAllied(C(sim, "DEM"), C(sim, "ALY")));
            Assert.True(sim.Diplomacy.AreTradePartners(C(sim, "DEM"), C(sim, "ALY")));
        }

        [Fact]
        public void HostileCountriesRefuseTradeAndExplainWhy()
        {
            var sim = NationWorld.Create();
            var preview = sim.DiplomacySystem.Preview(C(sim, "DEM"), C(sim, "AUT"), DiplomaticAction.ProposeTrade);
            Assert.True(preview.Available);
            Assert.False(preview.WillAccept);
            Assert.Contains(preview.Opinion.Factors, f => f.Label == "Our relations" && f.Value < 0);
            var result = sim.DiplomacySystem.Execute(C(sim, "DEM"), C(sim, "AUT"), DiplomaticAction.ProposeTrade);
            Assert.False(result.Accepted);
            Assert.False(sim.Diplomacy.HasTradeDeal(C(sim, "DEM"), C(sim, "AUT")));
        }

        [Fact]
        public void TradeDealsRaiseGrowth()
        {
            var sim = NationWorld.Create();
            var dem = C(sim, "DEM");
            var sml = C(sim, "SML");
            var result = sim.DiplomacySystem.Execute(dem, sml, DiplomaticAction.ProposeTrade);
            Assert.True(result.Accepted, string.Join(", ", result.Preview.Opinion.Factors));
            Assert.Contains(sml.Economy.GrowthFactors.Factors, f => f.Label == "Trade agreements" && f.Value > 0.5);
        }

        [Fact]
        public void SanctionsHurtTheTargetAndRelations()
        {
            var sim = NationWorld.Create();
            var dem = C(sim, "DEM");
            var aut = C(sim, "AUT");
            sim.DiplomacySystem.Execute(dem, aut, DiplomaticAction.ImposeSanctions);
            Assert.True(sim.Diplomacy.Sanctions(dem, aut));
            Assert.Contains(aut.Economy.GrowthFactors.Factors, f => f.Label == "Sanctions against us" && f.Value < -1);
            Assert.Contains(dem.Economy.GrowthFactors.Factors, f => f.Label == "Cost of our sanctions" && f.Value < 0);
            NationWorld.RunMonths(sim, 6);
            Assert.True(sim.Diplomacy.Relations(dem, aut) < -60);
        }

        [Fact]
        public void ImprovingRelationsCostsMoneyAndWorks()
        {
            var sim = NationWorld.Create();
            var dem = C(sim, "DEM");
            var sml = C(sim, "SML");
            double before = sim.Diplomacy.Relations(dem, sml);
            double treasury = dem.Economy.Treasury;
            var result = sim.DiplomacySystem.Execute(dem, sml, DiplomaticAction.ImproveRelations);
            Assert.True(result.Done);
            Assert.True(dem.Economy.Treasury < treasury);
            NationWorld.RunMonths(sim, 3);
            Assert.True(sim.Diplomacy.Relations(dem, sml) > before + 5);
            Assert.False(sim.DiplomacySystem.Preview(dem, sml, DiplomaticAction.ImproveRelations).Available);
        }

        [Fact]
        public void FriendsAcceptPactsAndCanBeLeft()
        {
            var sim = NationWorld.Create();
            var dem = C(sim, "DEM");
            var sml = C(sim, "SML");
            sim.Diplomacy.AddGoodwill(dem.Index, sml.Index, 70);
            NationWorld.RunMonths(sim, 24);
            var result = sim.DiplomacySystem.Execute(dem, sml, DiplomaticAction.ProposePact);
            Assert.True(result.Accepted, string.Join(", ", result.Preview.Opinion.Factors));
            Assert.True(sim.Diplomacy.AreAllied(dem, sml));
            sim.DiplomacySystem.Execute(dem, sml, DiplomaticAction.LeaveAlliance);
            Assert.False(sim.Diplomacy.AreAllied(dem, sml));
        }

        [Fact]
        public void WarIsNotAvailableYet()
        {
            var sim = NationWorld.Create();
            var preview = sim.DiplomacySystem.Preview(C(sim, "DEM"), C(sim, "AUT"), DiplomaticAction.DeclareWar);
            Assert.False(preview.Available);
            Assert.Contains("Phase 2", preview.Reason);
        }

        [Fact]
        public void EveryActionHasATitle()
        {
            var sim = NationWorld.Create();
            foreach (DiplomaticAction a in System.Enum.GetValues(typeof(DiplomaticAction)))
                Assert.False(string.IsNullOrEmpty(sim.DiplomacySystem.Preview(C(sim, "DEM"), C(sim, "SML"), a).Title), a.ToString());
        }
    }
}
