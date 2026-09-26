using System.Collections.Generic;

namespace GrandStrategy.Simulation
{
    public enum GovernmentType
    {
        FullDemocracy,
        FlawedDemocracy,
        HybridRegime,
        Authoritarian,
        AbsoluteMonarchy,
    }

    public static class GovernmentTypes
    {
        public static bool IsDemocracy(this GovernmentType g) =>
            g == GovernmentType.FullDemocracy || g == GovernmentType.FlawedDemocracy;

        public static bool IsAutocracy(this GovernmentType g) =>
            g == GovernmentType.Authoritarian || g == GovernmentType.AbsoluteMonarchy;

        /// <summary>Countries whose leaders face (more or less) competitive elections.</summary>
        public static bool HoldsElections(this GovernmentType g) => g.IsDemocracy() || g == GovernmentType.HybridRegime;

        public static string DisplayName(this GovernmentType g)
        {
            switch (g)
            {
                case GovernmentType.FullDemocracy: return "Full democracy";
                case GovernmentType.FlawedDemocracy: return "Flawed democracy";
                case GovernmentType.HybridRegime: return "Hybrid regime";
                case GovernmentType.Authoritarian: return "Authoritarian regime";
                default: return "Absolute monarchy";
            }
        }

        public static bool TryParse(string s, out GovernmentType g)
        {
            foreach (GovernmentType v in System.Enum.GetValues(typeof(GovernmentType)))
            {
                if (string.Equals(v.ToString(), s, System.StringComparison.OrdinalIgnoreCase))
                {
                    g = v;
                    return true;
                }
            }
            g = GovernmentType.FlawedDemocracy;
            return false;
        }
    }

    /// <summary>A country's government and how its people feel about it.</summary>
    public sealed class PoliticsState
    {
        public GovernmentType Government { get; internal set; }

        /// <summary>How secure the state and its institutions are, 0..100. Below 10 for too long = revolution.</summary>
        public double Stability { get; internal set; }

        /// <summary>Public support for the government, 0..100.</summary>
        public double Approval { get; internal set; }

        // Starting levels: targets are measured from them, so the world begins in balance.
        public double BaseStability { get; internal set; }
        public double BaseApproval { get; internal set; }

        public Breakdown StabilityTarget { get; } = new Breakdown();
        public Breakdown ApprovalTarget { get; } = new Breakdown();

        /// <summary>Next national election, or null if there are none.</summary>
        public GameDate? NextElection { get; internal set; }
        public int ElectionIntervalYears { get; internal set; }

        public int MonthsInCrisis { get; internal set; }
        public int ProtestCooldown { get; internal set; }
        public bool Overthrown { get; internal set; }

        internal readonly Dictionary<string, GameDate> DecisionReadyOn = new Dictionary<string, GameDate>();
    }
}
