using System;

namespace GrandStrategy.Simulation
{
    /// <summary>
    /// Tunable war numbers. Loaded from StreamingAssets/Data/Rules/war.json.
    /// Field names are lowerCamelCase so Unity's JsonUtility can read them directly.
    /// </summary>
    [Serializable]
    public sealed class WarRules
    {
        /// <summary>
        /// If the winner of a Capital Battle lost at least this share of the manpower they brought,
        /// it is a Pyrrhic victory: they only take the capital. Below it they annex the whole country.
        /// </summary>
        public double heavyLossThreshold = 0.65;

        /// <summary>Morale change for a country forced to its Last Capital.</summary>
        public double desperateMorale = -0.25;
        public int desperateDays = 365;

        /// <summary>Morale change for a defender who held their capital.</summary>
        public double heroicDefenseMorale = 0.15;
        public int heroicDefenseDays = 180;

        /// <summary>Morale change for an attacker whose army was routed at the enemy capital.</summary>
        public double routedMorale = -0.10;
        public int routedDays = 90;

        public void Validate()
        {
            if (!(heavyLossThreshold > 0 && heavyLossThreshold <= 1))
                throw new ArgumentOutOfRangeException(nameof(heavyLossThreshold), heavyLossThreshold, "Must be in (0, 1].");
            if (desperateDays < 0 || heroicDefenseDays < 0 || routedDays < 0)
                throw new ArgumentOutOfRangeException(nameof(desperateDays), "Modifier durations cannot be negative.");
        }
    }
}
