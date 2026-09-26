using System;
using System.Collections.Generic;

namespace GrandStrategy.Simulation
{
    /// <summary>The whole game state: provinces, countries, date and rules.</summary>
    public sealed class WorldState
    {
        readonly Province[] _provinces; // indexed by id, [0] is unused (ocean)
        readonly Dictionary<string, Country> _countries = new Dictionary<string, Country>();
        readonly List<Country> _countryList = new List<Country>();
        readonly List<string> _loadWarnings = new List<string>();

        public WorldState(IReadOnlyList<Province> provinces, IEnumerable<Country> countries, WarRules warRules, GameDate start)
        {
            if (provinces == null)
                throw new ArgumentNullException(nameof(provinces));

            _provinces = new Province[provinces.Count + 1];
            foreach (var p in provinces)
            {
                if (p.Id < 1 || p.Id > provinces.Count || _provinces[p.Id] != null)
                    throw new ArgumentException($"Province ids must be unique and run from 1 to {provinces.Count} (bad id {p.Id}).");
                _provinces[p.Id] = p;
            }

            foreach (var c in countries)
            {
                if (_countries.ContainsKey(c.Tag))
                    throw new ArgumentException($"Duplicate country tag {c.Tag}.");
                _countries.Add(c.Tag, c);
                c.Index = _countryList.Count;
                _countryList.Add(c);
            }

            WarRules = warRules ?? new WarRules();
            WarRules.Validate();
            Clock = new GameClock(start);
            Clock.DayPassed += OnDayPassed;
        }

        public GameClock Clock { get; }
        public EventBus Events { get; } = new EventBus();
        public WarRules WarRules { get; }

        public int ProvinceCount => _provinces.Length - 1;
        public IReadOnlyList<Country> Countries => _countryList;

        /// <summary>Problems found while loading data (e.g. unknown owner tags).</summary>
        public IReadOnlyList<string> LoadWarnings => _loadWarnings;

        public string PlayerTag { get; private set; }
        public Country Player => PlayerTag == null ? null : GetCountry(PlayerTag);

        public Province GetProvince(int id) => id >= 1 && id < _provinces.Length ? _provinces[id] : null;

        public IEnumerable<Province> Provinces
        {
            get
            {
                for (int i = 1; i < _provinces.Length; i++)
                    yield return _provinces[i];
            }
        }

        public Country GetCountry(string tag) =>
            tag != null && _countries.TryGetValue(tag, out var c) ? c : null;

        public Country OwnerOf(int provinceId) => GetCountry(GetProvince(provinceId)?.OwnerTag);

        public long PopulationOf(Country country)
        {
            long total = 0;
            foreach (var id in country.ProvinceIds)
                total += _provinces[id].Population;
            return total;
        }

        public double GdpOf(Country country)
        {
            double total = 0;
            foreach (var id in country.ProvinceIds)
                total += _provinces[id].GdpMillions;
            return total;
        }

        public void SetPlayer(string tag)
        {
            var country = GetCountry(tag);
            if (country == null)
                throw new ArgumentException($"Unknown country {tag}.", nameof(tag));
            if (country.IsEliminated)
                throw new InvalidOperationException($"{country.Name} no longer exists.");
            PlayerTag = tag;
            Events.Publish(new PlayerCountryChanged(tag));
        }

        /// <summary>
        /// Used while building the world: gives a province to a country without firing events.
        /// </summary>
        internal void AssignInitialOwner(Province province, Country country)
        {
            province.OwnerTag = country?.Tag;
            country?.AddProvince(province.Id);
        }

        internal void AddLoadWarning(string warning) => _loadWarnings.Add(warning);

        /// <summary>
        /// Moves a province to a new owner (null = unowned). If it was the old owner's capital,
        /// the old owner moves its capital; if it was their last province they are eliminated.
        /// </summary>
        public void TransferProvince(int provinceId, string newOwnerTag)
        {
            var province = GetProvince(provinceId) ?? throw new ArgumentException($"Unknown province {provinceId}.");
            var newOwner = newOwnerTag == null ? null : GetCountry(newOwnerTag) ?? throw new ArgumentException($"Unknown country {newOwnerTag}.");
            var oldOwner = GetCountry(province.OwnerTag);
            if (oldOwner == newOwner)
                return;

            oldOwner?.RemoveProvince(provinceId);
            newOwner?.AddProvince(provinceId);
            province.OwnerTag = newOwner?.Tag;
            Events.Publish(new ProvinceOwnerChanged(provinceId, oldOwner?.Tag, newOwner?.Tag));

            if (oldOwner == null)
                return;
            if (oldOwner.IsEliminated)
            {
                oldOwner.CapitalProvinceId = 0;
                Events.Publish(new CountryEliminated(oldOwner.Tag, newOwner?.Tag));
            }
            else if (oldOwner.CapitalProvinceId == provinceId)
            {
                var newCapital = ChooseNewCapital(oldOwner);
                MoveCapital(oldOwner, newCapital.Id);
            }
        }

        /// <summary>The province a country falls back to when it loses its capital: its most populous one.</summary>
        public Province ChooseNewCapital(Country country)
        {
            Province best = null;
            foreach (var id in country.ProvinceIds)
            {
                var p = _provinces[id];
                if (id == country.CapitalProvinceId)
                    continue;
                if (best == null || p.Population > best.Population || (p.Population == best.Population && p.Id < best.Id))
                    best = p;
            }
            return best ?? GetProvince(country.CapitalProvinceId);
        }

        public void MoveCapital(Country country, int provinceId)
        {
            var province = GetProvince(provinceId);
            if (province == null || province.OwnerTag != country.Tag)
                throw new ArgumentException($"{country.Name} does not own province {provinceId}.");
            int old = country.CapitalProvinceId;
            if (old == provinceId)
                return;
            country.CapitalProvinceId = provinceId;
            country.CapitalName = province.Name;
            Events.Publish(new CapitalMoved(country.Tag, old, provinceId));
        }

        void OnDayPassed(GameDate date)
        {
            foreach (var country in _countryList)
                if (country.Modifiers.Count > 0 && country.TickModifiers())
                    Events.Publish(new ModifiersChanged(country.Tag));
        }
    }
}
