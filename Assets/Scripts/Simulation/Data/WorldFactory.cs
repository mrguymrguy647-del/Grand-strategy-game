using System;
using System.Collections.Generic;
using System.Globalization;

namespace GrandStrategy.Simulation.Data
{
    /// <summary>Builds a <see cref="WorldState"/> from the loaded data files.</summary>
    public static class WorldFactory
    {
        public static readonly GameDate DefaultStartDate = new GameDate(2026, 1, 1);

        public static WorldState Create(ProvinceFile provinceFile, CountryFile countryFile, WarRules warRules, GameDate start)
        {
            if (provinceFile?.provinces == null)
                throw new ArgumentException("Province file has no provinces.", nameof(provinceFile));
            if (countryFile?.countries == null)
                throw new ArgumentException("Country file has no countries.", nameof(countryFile));

            var provinces = new List<Province>(provinceFile.provinces.Length);
            foreach (var r in provinceFile.provinces)
            {
                provinces.Add(new Province(
                    r.id,
                    string.IsNullOrEmpty(r.name) ? $"Province {r.id}" : r.name,
                    ParseHexColor(r.color),
                    r.population,
                    r.gdpMillions,
                    r.center != null && r.center.Length >= 2 ? r.center[0] : 0,
                    r.center != null && r.center.Length >= 2 ? r.center[1] : 0,
                    r.pixels,
                    r.coastal,
                    r.neighbors));
            }

            var countries = new List<Country>(countryFile.countries.Length);
            foreach (var r in countryFile.countries)
            {
                countries.Add(new Country(r.tag, r.name ?? r.tag, r.formalName ?? r.name ?? r.tag, ParseHexColor(r.color))
                {
                    IsoA2 = r.isoA2 ?? "",
                    Continent = r.continent ?? "",
                    Subregion = r.subregion ?? "",
                    IncomeGroup = r.incomeGroup ?? "",
                    CapitalName = r.capitalName ?? "",
                });
            }

            var world = new WorldState(provinces, countries, warRules, start);

            foreach (var r in provinceFile.provinces)
            {
                if (string.IsNullOrEmpty(r.owner))
                    continue;
                var province = world.GetProvince(r.id);
                var owner = world.GetCountry(r.owner);
                if (owner == null)
                {
                    world.AddLoadWarning($"Province {r.id} ({province.Name}) has unknown owner '{r.owner}'; left unowned.");
                    continue;
                }
                world.AssignInitialOwner(province, owner);
            }

            foreach (var r in countryFile.countries)
            {
                var country = world.GetCountry(r.tag);
                if (country.IsEliminated)
                {
                    world.AddLoadWarning($"{country.Name} ({country.Tag}) owns no provinces.");
                    continue;
                }
                var capital = world.GetProvince(r.capitalProvince);
                if (capital == null || capital.OwnerTag != country.Tag)
                {
                    world.AddLoadWarning($"{country.Name} capital province {r.capitalProvince} is not owned by it; picking another.");
                    capital = world.ChooseNewCapital(country);
                    country.CapitalName = capital.Name;
                }
                country.CapitalProvinceId = capital.Id;
            }

            return world;
        }

        /// <summary>Parses "#rrggbb" (or "rrggbb") into 0xRRGGBB.</summary>
        public static int ParseHexColor(string hex)
        {
            if (string.IsNullOrEmpty(hex))
                return 0x808080;
            var s = hex[0] == '#' ? hex.Substring(1) : hex;
            if (s.Length != 6 || !int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
                throw new FormatException($"'{hex}' is not a #rrggbb colour.");
            return rgb;
        }
    }
}
