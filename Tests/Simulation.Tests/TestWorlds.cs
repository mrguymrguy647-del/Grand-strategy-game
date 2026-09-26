using System.Collections.Generic;
using GrandStrategy.Simulation.Data;

namespace GrandStrategy.Simulation.Tests
{
    /// <summary>Small hand-made worlds for rule tests.</summary>
    static class TestWorlds
    {
        /// <summary>
        /// Two countries side by side.
        /// ATK: provinces 1 (capital), 2, 3.  DEF: provinces 4 (capital), 5, 6 — province 6 is its most populous
        /// after the capital, so it becomes the Last Capital.
        /// </summary>
        public static WorldState TwoCountries(WarRules rules = null, int defenderProvinces = 3)
        {
            var provinces = new List<ProvinceRecord>
            {
                P(1, "ATK", 5_000_000, 2),
                P(2, "ATK", 1_000_000, 1, 3),
                P(3, "ATK", 1_000_000, 2, 4),
                P(4, "DEF", 6_000_000, 3, 5),
            };
            if (defenderProvinces >= 2) provinces.Add(P(5, "DEF", 1_000_000, 4, 6));
            if (defenderProvinces >= 3) provinces.Add(P(6, "DEF", 2_000_000, 5));

            var provinceFile = new ProvinceFile { width = 10, height = 10, provinces = provinces.ToArray() };
            var countryFile = new CountryFile
            {
                countries = new[]
                {
                    new CountryRecord { tag = "ATK", name = "Attackia", color = "#aa0000", capitalProvince = 1, capitalName = "Attack City" },
                    new CountryRecord { tag = "DEF", name = "Defendia", color = "#0000aa", capitalProvince = 4, capitalName = "Defend City" },
                },
            };
            return WorldFactory.Create(provinceFile, countryFile, rules ?? new WarRules(), WorldFactory.DefaultStartDate);
        }

        static ProvinceRecord P(int id, string owner, long population, params int[] neighbors) => new ProvinceRecord
        {
            id = id,
            name = "Province " + id,
            color = "#0000" + id.ToString("x2"),
            owner = owner,
            population = population,
            gdpMillions = population / 1000.0,
            pixels = 100,
            center = new[] { id, id },
            neighbors = neighbors,
        };
    }
}
