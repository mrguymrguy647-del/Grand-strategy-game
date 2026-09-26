using System;
using System.Collections.Generic;

namespace GrandStrategy.Simulation
{
    /// <summary>A nation. Owns provinces and carries national modifiers.</summary>
    public sealed class Country
    {
        public const double MinMorale = 0.1;
        public const double MaxMorale = 2.0;

        readonly HashSet<int> _provinceIds = new HashSet<int>();
        readonly List<Modifier> _modifiers = new List<Modifier>();

        public Country(string tag, string name, string formalName, int colorRgb)
        {
            if (string.IsNullOrEmpty(tag))
                throw new ArgumentException("A country needs a tag.", nameof(tag));
            Tag = tag;
            Name = name;
            FormalName = formalName;
            ColorRgb = colorRgb;
        }

        public string Tag { get; }

        /// <summary>Position in <see cref="WorldState.Countries"/>; used by per-pair tables.</summary>
        public int Index { get; internal set; } = -1;

        /// <summary>Budget, debt and growth. Null until a <see cref="GameSimulation"/> is created.</summary>
        public EconomyState Economy { get; internal set; }

        /// <summary>Government, stability, approval and elections. Null until a simulation exists.</summary>
        public PoliticsState Politics { get; internal set; }

        public string Name { get; }
        public string FormalName { get; }

        /// <summary>Map colour (0xRRGGBB).</summary>
        public int ColorRgb { get; }

        public string IsoA2 { get; set; } = "";
        public string Continent { get; set; } = "";
        public string Subregion { get; set; } = "";
        public string IncomeGroup { get; set; } = "";

        /// <summary>Name of the capital city.</summary>
        public string CapitalName { get; internal set; } = "";

        /// <summary>Province that holds the capital, 0 if the country has none.</summary>
        public int CapitalProvinceId { get; internal set; }

        public IReadOnlyCollection<int> ProvinceIds => _provinceIds;
        public IReadOnlyList<Modifier> Modifiers => _modifiers;

        public bool IsEliminated => _provinceIds.Count == 0;

        /// <summary>National morale multiplier: 1.0 is normal.</summary>
        public double Morale
        {
            get
            {
                double morale = 1.0;
                foreach (var m in _modifiers)
                    morale += m.Morale;
                return Math.Max(MinMorale, Math.Min(MaxMorale, morale));
            }
        }

        public bool HasModifier(string id) => FindModifier(id) != null;

        public Modifier FindModifier(string id)
        {
            foreach (var m in _modifiers)
                if (m.Id == id)
                    return m;
            return null;
        }

        /// <summary>Adds a modifier, or replaces an existing one with the same id (no stacking).</summary>
        public void AddOrRefreshModifier(Modifier modifier)
        {
            if (modifier == null)
                throw new ArgumentNullException(nameof(modifier));
            _modifiers.RemoveAll(m => m.Id == modifier.Id);
            _modifiers.Add(modifier);
        }

        public bool RemoveModifier(string id) => _modifiers.RemoveAll(m => m.Id == id) > 0;

        internal bool AddProvince(int id) => _provinceIds.Add(id);
        internal bool RemoveProvince(int id) => _provinceIds.Remove(id);

        /// <summary>Counts down timed modifiers. Returns true if any expired.</summary>
        internal bool TickModifiers()
        {
            bool expired = false;
            for (int i = _modifiers.Count - 1; i >= 0; i--)
            {
                var m = _modifiers[i];
                if (m.IsPermanent)
                    continue;
                m.DaysRemaining--;
                if (m.DaysRemaining <= 0)
                {
                    _modifiers.RemoveAt(i);
                    expired = true;
                }
            }
            return expired;
        }

        public override string ToString() => $"{Name} ({Tag})";
    }
}
