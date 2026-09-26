using System;
using System.Collections.Generic;

namespace GrandStrategy.Simulation
{
    public enum BlocKind
    {
        /// <summary>Mutual defence (NATO, bilateral pacts). Members will be called to war in Phase 2.</summary>
        Alliance,
        /// <summary>Political and economic union (EU): counts as trade partners too.</summary>
        Union,
        /// <summary>Free-trade area (USMCA, ASEAN...).</summary>
        Trade,
        /// <summary>Club of countries with shared interests (G7, BRICS...).</summary>
        Forum,
    }

    public sealed class Bloc
    {
        public Bloc(string id, string name, BlocKind kind, IEnumerable<string> members)
        {
            Id = id;
            Name = name;
            Kind = kind;
            Members = new HashSet<string>(members);
        }

        public string Id { get; }
        public string Name { get; }
        public BlocKind Kind { get; }
        public HashSet<string> Members { get; }

        public bool IsAlliance => Kind == BlocKind.Alliance;
        public bool IsTradeArea => Kind == BlocKind.Trade || Kind == BlocKind.Union;
    }

    /// <summary>Relations, agreements, sanctions and blocs between all countries.</summary>
    public sealed class DiplomacyState
    {
        readonly int _n;
        readonly float[] _relations;
        readonly float[] _goodwill;
        readonly float[] _override;   // NaN when the pair has no hand-set starting relation
        readonly HashSet<int> _tradeDeals = new HashSet<int>();      // bilateral, key = min*n+max
        readonly HashSet<int> _sanctions = new HashSet<int>();       // directed, key = by*n+target
        readonly HashSet<int> _startSanctions = new HashSet<int>();
        readonly Dictionary<long, GameDate> _cooldowns = new Dictionary<long, GameDate>();

        public DiplomacyState(int countryCount)
        {
            _n = countryCount;
            _relations = new float[_n * _n];
            _goodwill = new float[_n * _n];
            _override = new float[_n * _n];
            for (int i = 0; i < _override.Length; i++)
                _override[i] = float.NaN;
        }

        public List<Bloc> Blocs { get; } = new List<Bloc>();

        // Cached per-pair bloc data; rebuilt after blocs change (see MarkBlocsChanged).
        float[] _membership;
        bool[] _tradeArea;
        Dictionary<string, int> _index;

        /// <summary>Call after adding/removing blocs or members.</summary>
        public void MarkBlocsChanged() => _membership = null;

        internal void SetIndex(Dictionary<string, int> index) => _index = index;

        void RebuildBlocCache(DiplomacyRules rules)
        {
            _membership = new float[_n * _n];
            _tradeArea = new bool[_n * _n];
            foreach (var bloc in Blocs)
            {
                float bonus = (float)(bloc.Kind == BlocKind.Alliance ? rules.allianceBonus
                    : bloc.Kind == BlocKind.Union ? rules.unionBonus
                    : bloc.Kind == BlocKind.Trade ? rules.tradeBlocBonus
                    : rules.forumBonus);
                var members = new List<int>();
                foreach (var tag in bloc.Members)
                    if (_index != null && _index.TryGetValue(tag, out int i))
                        members.Add(i);
                foreach (int a in members)
                    foreach (int b in members)
                    {
                        if (a == b)
                            continue;
                        _membership[Pair(a, b)] += bonus;
                        if (bloc.IsTradeArea)
                            _tradeArea[Pair(a, b)] = true;
                    }
            }
            for (int i = 0; i < _membership.Length; i++)
                _membership[i] = (float)Math.Min(rules.membershipCap, _membership[i]);
        }

        internal double MembershipBonus(int a, int b, DiplomacyRules rules)
        {
            if (_membership == null)
                RebuildBlocCache(rules);
            return _membership[Pair(a, b)];
        }

        internal bool InTradeArea(int a, int b, DiplomacyRules rules)
        {
            if (_membership == null)
                RebuildBlocCache(rules);
            return _tradeArea[Pair(a, b)];
        }

        int Pair(int a, int b) => a * _n + b;
        int Unordered(int a, int b) => a < b ? a * _n + b : b * _n + a;

        public double Relations(Country a, Country b) => a == b ? 100 : _relations[Pair(a.Index, b.Index)];

        internal void SetRelations(int a, int b, double value)
        {
            float v = (float)Math.Max(-100, Math.Min(100, value));
            _relations[Pair(a, b)] = v;
            _relations[Pair(b, a)] = v;
        }

        internal double RawRelations(int a, int b) => _relations[Pair(a, b)];

        public double Goodwill(Country a, Country b) => _goodwill[Pair(a.Index, b.Index)];

        internal void AddGoodwill(int a, int b, double amount)
        {
            float v = (float)Math.Max(-80, Math.Min(80, _goodwill[Pair(a, b)] + amount));
            _goodwill[Pair(a, b)] = v;
            _goodwill[Pair(b, a)] = v;
        }

        internal void DecayGoodwill(double rate)
        {
            float keep = (float)(1 - rate);
            for (int i = 0; i < _goodwill.Length; i++)
                _goodwill[i] *= keep;
        }

        internal void SetOverride(int a, int b, double value)
        {
            _override[Pair(a, b)] = (float)value;
            _override[Pair(b, a)] = (float)value;
        }

        internal bool TryGetOverride(int a, int b, out double value)
        {
            float v = _override[Pair(a, b)];
            value = v;
            return !float.IsNaN(v);
        }

        // --- trade -------------------------------------------------------------------------

        public bool HasTradeDeal(Country a, Country b) => _tradeDeals.Contains(Unordered(a.Index, b.Index));
        internal bool AddTradeDeal(Country a, Country b) => _tradeDeals.Add(Unordered(a.Index, b.Index));
        internal bool RemoveTradeDeal(Country a, Country b) => _tradeDeals.Remove(Unordered(a.Index, b.Index));

        /// <summary>True if the two trade freely: a shared trade area / union, or a bilateral deal.</summary>
        public bool AreTradePartners(Country a, Country b)
        {
            if (a == b)
                return false;
            if (HasTradeDeal(a, b))
                return true;
            if (_membership != null)
                return _tradeArea[Pair(a.Index, b.Index)];
            foreach (var bloc in Blocs)
                if (bloc.IsTradeArea && bloc.Members.Contains(a.Tag) && bloc.Members.Contains(b.Tag))
                    return true;
            return false;
        }

        // --- sanctions ---------------------------------------------------------------------

        public bool Sanctions(Country by, Country target) => _sanctions.Contains(Pair(by.Index, target.Index));
        internal bool AddSanctions(Country by, Country target) => _sanctions.Add(Pair(by.Index, target.Index));
        internal bool RemoveSanctions(Country by, Country target) => _sanctions.Remove(Pair(by.Index, target.Index));
        internal void MarkStartSanctions() => _startSanctions.UnionWith(_sanctions);
        internal bool IsStartSanction(int by, int target) => _startSanctions.Contains(Pair(by, target));
        internal bool HasSanctions(int by, int target) => _sanctions.Contains(Pair(by, target));

        public bool AnySanctions(Country a, Country b) => Sanctions(a, b) || Sanctions(b, a);

        // --- blocs -------------------------------------------------------------------------

        public IEnumerable<Bloc> BlocsOf(Country c)
        {
            foreach (var bloc in Blocs)
                if (bloc.Members.Contains(c.Tag))
                    yield return bloc;
        }

        public IEnumerable<Bloc> SharedBlocs(Country a, Country b)
        {
            foreach (var bloc in Blocs)
                if (bloc.Members.Contains(a.Tag) && bloc.Members.Contains(b.Tag))
                    yield return bloc;
        }

        public bool AreAllied(Country a, Country b)
        {
            foreach (var bloc in SharedBlocs(a, b))
                if (bloc.IsAlliance)
                    return true;
            return false;
        }

        public Bloc FindBloc(string id)
        {
            foreach (var bloc in Blocs)
                if (bloc.Id == id)
                    return bloc;
            return null;
        }

        // --- cooldowns ---------------------------------------------------------------------

        internal bool OnCooldown(int a, int b, DiplomaticAction action, GameDate today, out GameDate readyOn)
        {
            long key = ((long)Pair(a, b) << 8) | (long)action;
            if (_cooldowns.TryGetValue(key, out readyOn) && today < readyOn)
                return true;
            return false;
        }

        internal void StartCooldown(int a, int b, DiplomaticAction action, GameDate readyOn)
        {
            long key = ((long)Pair(a, b) << 8) | (long)action;
            _cooldowns[key] = readyOn;
        }
    }
}
