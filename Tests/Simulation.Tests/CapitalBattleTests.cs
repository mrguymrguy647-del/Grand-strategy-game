using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace GrandStrategy.Simulation.Tests
{
    public class CapitalBattleTests
    {
        static CapitalBattleResult Result(BattleSide winner, int attackerLosses, int defenderLosses = 5_000,
            int attackerManpower = 10_000, int defenderManpower = 10_000, string attacker = "ATK", string defender = "DEF", int capital = 4) =>
            new CapitalBattleResult(attacker, defender, capital, winner, attackerManpower, attackerLosses, defenderManpower, defenderLosses);

        [Fact]
        public void DefaultHeavyLossLineIs65Percent() => Assert.Equal(0.65, new WarRules().heavyLossThreshold);

        [Theory]
        [InlineData(0, CapitalBattleOutcome.TotalVictory)]
        [InlineData(6_499, CapitalBattleOutcome.TotalVictory)]
        [InlineData(6_500, CapitalBattleOutcome.PyrrhicVictory)] // exactly 65% counts as heavy
        [InlineData(9_999, CapitalBattleOutcome.PyrrhicVictory)]
        public void AttackerWinOutcomeDependsOnLosses(int attackerLosses, CapitalBattleOutcome expected) =>
            Assert.Equal(expected, CapitalBattleRules.Evaluate(Result(BattleSide.Attacker, attackerLosses), new WarRules()));

        [Fact]
        public void DefenderWinIsAlwaysCapitalHeld() =>
            Assert.Equal(CapitalBattleOutcome.CapitalHeld,
                CapitalBattleRules.Evaluate(Result(BattleSide.Defender, 9_000, defenderLosses: 9_900), new WarRules()));

        [Fact]
        public void BringingNoTroopsCountsAsTotalLoss() => Assert.Equal(1.0, CapitalBattleRules.LossRatio(0, 0));

        [Fact]
        public void TotalVictoryAnnexesTheWholeCountry()
        {
            var world = TestWorlds.TwoCountries();
            var eliminated = new List<CountryEliminated>();
            world.Events.Subscribe<CountryEliminated>(eliminated.Add);

            var report = CapitalBattleRules.Apply(world, Result(BattleSide.Attacker, attackerLosses: 3_000));

            Assert.Equal(CapitalBattleOutcome.TotalVictory, report.Outcome);
            Assert.Equal(3, report.ProvincesTransferred);
            Assert.True(report.DefenderEliminated);
            Assert.True(world.GetCountry("DEF").IsEliminated);
            Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, world.GetCountry("ATK").ProvinceIds.OrderBy(i => i));
            Assert.Single(eliminated);
            Assert.Equal("ATK", eliminated[0].ByTag);
        }

        [Fact]
        public void PyrrhicVictoryTakesOnlyTheCapitalAndDefenderMovesToLastCapital()
        {
            var world = TestWorlds.TwoCountries();
            var moves = new List<CapitalMoved>();
            world.Events.Subscribe<CapitalMoved>(moves.Add);

            var report = CapitalBattleRules.Apply(world, Result(BattleSide.Attacker, attackerLosses: 7_000));

            var defender = world.GetCountry("DEF");
            Assert.Equal(CapitalBattleOutcome.PyrrhicVictory, report.Outcome);
            Assert.Equal(1, report.ProvincesTransferred);
            Assert.Equal("ATK", world.GetProvince(4).OwnerTag);
            Assert.False(defender.IsEliminated);
            Assert.Equal(6, defender.CapitalProvinceId); // most populous remaining province
            Assert.Equal(6, report.NewDefenderCapitalId);
            Assert.Equal("Province 6", defender.CapitalName);
            Assert.Single(moves);
            Assert.Equal(4, moves[0].OldProvinceId);

            var desperate = defender.FindModifier(CapitalBattleRules.DesperateModifierId);
            Assert.NotNull(desperate);
            Assert.Equal(0.75, defender.Morale, 6);
        }

        [Fact]
        public void PyrrhicVictoryOverALastProvinceEliminatesTheDefender()
        {
            var world = TestWorlds.TwoCountries(defenderProvinces: 1);
            var report = CapitalBattleRules.Apply(world, Result(BattleSide.Attacker, attackerLosses: 8_000));

            Assert.Equal(CapitalBattleOutcome.PyrrhicVictory, report.Outcome);
            Assert.True(report.DefenderEliminated);
            Assert.True(world.GetCountry("DEF").IsEliminated);
            Assert.Empty(world.GetCountry("DEF").Modifiers);
        }

        [Fact]
        public void SecondPyrrhicVictoryHitsTheLastCapitalAndDoesNotStackThePenalty()
        {
            var world = TestWorlds.TwoCountries();
            CapitalBattleRules.Apply(world, Result(BattleSide.Attacker, attackerLosses: 7_000));
            CapitalBattleRules.Apply(world, Result(BattleSide.Attacker, attackerLosses: 7_000, capital: 6));

            var defender = world.GetCountry("DEF");
            Assert.Equal(5, defender.CapitalProvinceId);
            Assert.Single(defender.Modifiers);
            Assert.Equal(0.75, defender.Morale, 6);
        }

        [Fact]
        public void HeldCapitalBoostsDefenderAndPunishesAttacker()
        {
            var world = TestWorlds.TwoCountries();
            var report = CapitalBattleRules.Apply(world, Result(BattleSide.Defender, attackerLosses: 9_000, defenderLosses: 4_000));

            Assert.Equal(CapitalBattleOutcome.CapitalHeld, report.Outcome);
            Assert.Equal(0, report.ProvincesTransferred);
            Assert.Equal("DEF", world.GetProvince(4).OwnerTag);
            Assert.Equal(1.15, world.GetCountry("DEF").Morale, 6);
            Assert.Equal(0.90, world.GetCountry("ATK").Morale, 6);
        }

        [Fact]
        public void RulesWorkTheSameWhenThePlayerIsTheDefender()
        {
            var world = TestWorlds.TwoCountries();
            world.SetPlayer("DEF");
            CapitalBattleRules.Apply(world, Result(BattleSide.Attacker, attackerLosses: 1_000));
            Assert.True(world.Player.IsEliminated); // game over for the player
        }

        [Fact]
        public void ModifiersExpireWithTime()
        {
            var rules = new WarRules { desperateDays = 3 };
            var world = TestWorlds.TwoCountries(rules);
            CapitalBattleRules.Apply(world, Result(BattleSide.Attacker, attackerLosses: 7_000));
            var defender = world.GetCountry("DEF");

            world.Clock.StepDay();
            world.Clock.StepDay();
            Assert.True(defender.HasModifier(CapitalBattleRules.DesperateModifierId));
            world.Clock.StepDay();
            Assert.False(defender.HasModifier(CapitalBattleRules.DesperateModifierId));
            Assert.Equal(1.0, defender.Morale, 6);
        }

        [Fact]
        public void RejectsBattlesThatAreNotAtTheCapital()
        {
            var world = TestWorlds.TwoCountries();
            Assert.Throws<InvalidOperationException>(() =>
                CapitalBattleRules.Apply(world, Result(BattleSide.Attacker, 1_000, capital: 5)));
        }

        [Fact]
        public void RejectsImpossibleLosses()
        {
            var world = TestWorlds.TwoCountries();
            Assert.Throws<ArgumentException>(() =>
                CapitalBattleRules.Apply(world, Result(BattleSide.Attacker, attackerLosses: 20_000)));
        }

        [Fact]
        public void ThresholdIsTunable()
        {
            var rules = new WarRules { heavyLossThreshold = 0.5 };
            Assert.Equal(CapitalBattleOutcome.PyrrhicVictory, CapitalBattleRules.Evaluate(Result(BattleSide.Attacker, 5_000), rules));
            Assert.Throws<ArgumentOutOfRangeException>(() => new WarRules { heavyLossThreshold = 1.5 }.Validate());
        }
    }
}
