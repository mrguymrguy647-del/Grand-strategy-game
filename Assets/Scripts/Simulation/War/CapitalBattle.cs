using System;
using System.Linq;

namespace GrandStrategy.Simulation
{
    public enum BattleSide
    {
        Attacker,
        Defender,
    }

    public enum CapitalBattleOutcome
    {
        /// <summary>Attacker won losing less than the heavy-loss threshold: the whole defender is annexed.</summary>
        TotalVictory,

        /// <summary>Attacker won but lost heavily: only the capital falls, the defender retreats to a Last Capital.</summary>
        PyrrhicVictory,

        /// <summary>Defender won: the capital holds and the war goes on.</summary>
        CapitalHeld,
    }

    /// <summary>What the Capital Battle (tactical scene or auto-resolve) hands back to the world.</summary>
    public readonly struct CapitalBattleResult
    {
        public readonly string AttackerTag;
        public readonly string DefenderTag;
        public readonly int CapitalProvinceId;
        public readonly BattleSide Winner;
        public readonly int AttackerManpower;
        public readonly int AttackerLosses;
        public readonly int DefenderManpower;
        public readonly int DefenderLosses;

        public CapitalBattleResult(string attackerTag, string defenderTag, int capitalProvinceId, BattleSide winner,
            int attackerManpower, int attackerLosses, int defenderManpower, int defenderLosses)
        {
            AttackerTag = attackerTag;
            DefenderTag = defenderTag;
            CapitalProvinceId = capitalProvinceId;
            Winner = winner;
            AttackerManpower = attackerManpower;
            AttackerLosses = attackerLosses;
            DefenderManpower = defenderManpower;
            DefenderLosses = defenderLosses;
        }

        public int WinnerManpower => Winner == BattleSide.Attacker ? AttackerManpower : DefenderManpower;
        public int WinnerLosses => Winner == BattleSide.Attacker ? AttackerLosses : DefenderLosses;
        public double WinnerLossRatio => CapitalBattleRules.LossRatio(WinnerManpower, WinnerLosses);
    }

    /// <summary>What happened to the world after a Capital Battle.</summary>
    public sealed class CapitalBattleReport
    {
        public CapitalBattleReport(CapitalBattleResult result, CapitalBattleOutcome outcome)
        {
            Result = result;
            Outcome = outcome;
        }

        public CapitalBattleResult Result { get; }
        public CapitalBattleOutcome Outcome { get; }
        public int ProvincesTransferred { get; internal set; }
        public bool DefenderEliminated { get; internal set; }

        /// <summary>The defender's Last Capital after a Pyrrhic victory, otherwise 0.</summary>
        public int NewDefenderCapitalId { get; internal set; }
    }

    /// <summary>Published on the world's event bus after a Capital Battle has been applied.</summary>
    public readonly struct CapitalBattleResolved
    {
        public readonly CapitalBattleReport Report;

        public CapitalBattleResolved(CapitalBattleReport report) => Report = report;
    }

    /// <summary>
    /// The rules that turn a Capital Battle result into consequences. See docs/DESIGN.md, section 2.3.
    /// They work the same whichever side the player is on.
    /// </summary>
    public static class CapitalBattleRules
    {
        public const string DesperateModifierId = "desperate_last_capital";
        public const string HeroicDefenseModifierId = "heroic_defense";
        public const string RoutedModifierId = "routed_at_capital";

        /// <summary>Share of manpower lost, 0..1. Bringing no troops counts as losing everything.</summary>
        public static double LossRatio(int manpower, int losses)
        {
            if (manpower <= 0)
                return 1.0;
            return Math.Max(0.0, Math.Min(1.0, (double)losses / manpower));
        }

        public static CapitalBattleOutcome Evaluate(CapitalBattleResult result, WarRules rules)
        {
            if (result.Winner == BattleSide.Defender)
                return CapitalBattleOutcome.CapitalHeld;
            return result.WinnerLossRatio >= rules.heavyLossThreshold
                ? CapitalBattleOutcome.PyrrhicVictory
                : CapitalBattleOutcome.TotalVictory;
        }

        /// <summary>Applies a Capital Battle result to the world and returns what happened.</summary>
        public static CapitalBattleReport Apply(WorldState world, CapitalBattleResult result)
        {
            var attacker = world.GetCountry(result.AttackerTag) ?? throw new ArgumentException($"Unknown attacker {result.AttackerTag}.");
            var defender = world.GetCountry(result.DefenderTag) ?? throw new ArgumentException($"Unknown defender {result.DefenderTag}.");
            if (attacker == defender)
                throw new ArgumentException("A country cannot fight a Capital Battle against itself.");
            if (defender.IsEliminated)
                throw new InvalidOperationException($"{defender.Name} no longer exists.");
            if (defender.CapitalProvinceId != result.CapitalProvinceId)
                throw new InvalidOperationException(
                    $"Province {result.CapitalProvinceId} is not the capital of {defender.Name} (capital is {defender.CapitalProvinceId}).");
            ValidateTroops(result.AttackerManpower, result.AttackerLosses, "attacker");
            ValidateTroops(result.DefenderManpower, result.DefenderLosses, "defender");

            var rules = world.WarRules;
            var report = new CapitalBattleReport(result, Evaluate(result, rules));

            switch (report.Outcome)
            {
                case CapitalBattleOutcome.TotalVictory:
                    foreach (var id in defender.ProvinceIds.ToArray())
                    {
                        world.TransferProvince(id, attacker.Tag);
                        report.ProvincesTransferred++;
                    }
                    report.DefenderEliminated = true;
                    break;

                case CapitalBattleOutcome.PyrrhicVictory:
                    world.TransferProvince(result.CapitalProvinceId, attacker.Tag);
                    report.ProvincesTransferred = 1;
                    report.DefenderEliminated = defender.IsEliminated;
                    if (!defender.IsEliminated)
                    {
                        report.NewDefenderCapitalId = defender.CapitalProvinceId;
                        defender.AddOrRefreshModifier(new Modifier(DesperateModifierId, "Desperate",
                            "Our capital has fallen. We fight on from our Last Capital.",
                            rules.desperateMorale, rules.desperateDays));
                        world.Events.Publish(new ModifiersChanged(defender.Tag));
                    }
                    break;

                case CapitalBattleOutcome.CapitalHeld:
                    defender.AddOrRefreshModifier(new Modifier(HeroicDefenseModifierId, "Heroic Defense",
                        "Our capital held against the enemy's assault.",
                        rules.heroicDefenseMorale, rules.heroicDefenseDays));
                    attacker.AddOrRefreshModifier(new Modifier(RoutedModifierId, "Routed at the Capital",
                        "Our assault on the enemy capital was thrown back.",
                        rules.routedMorale, rules.routedDays));
                    world.Events.Publish(new ModifiersChanged(defender.Tag));
                    world.Events.Publish(new ModifiersChanged(attacker.Tag));
                    break;
            }

            world.Events.Publish(new CapitalBattleResolved(report));
            return report;
        }

        static void ValidateTroops(int manpower, int losses, string side)
        {
            if (manpower < 0 || losses < 0)
                throw new ArgumentException($"The {side}'s manpower and losses cannot be negative.");
            if (losses > manpower)
                throw new ArgumentException($"The {side} cannot lose more troops ({losses}) than it brought ({manpower}).");
        }
    }
}
