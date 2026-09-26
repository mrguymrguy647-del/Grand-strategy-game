using System.Collections.Generic;
using System.Linq;
using GrandStrategy.Simulation.Data;

namespace GrandStrategy.Simulation.Tests
{
    /// <summary>
    /// A small world for nation-system tests:
    /// DEM (big democracy, the player), ALY (friendly democracy, same bloc),
    /// AUT (hostile autocracy), SML (small neutral democracy).
    /// </summary>
    static class NationWorld
    {
        public static GameSimulation Create(System.Action<Dictionary<string, NationRecord>> tweak = null,
            EconomyRules economy = null, PoliticsRules politics = null, DiplomacyRules diplomacy = null, bool player = true)
        {
            var provinces = new List<ProvinceRecord>();
            var countries = new[]
            {
                ("DEM", 2_000_000.0, 50_000_000L), ("ALY", 800_000.0, 20_000_000L),
                ("AUT", 1_000_000.0, 60_000_000L), ("SML", 50_000.0, 3_000_000L),
            };
            int id = 1;
            foreach (var (tag, gdp, pop) in countries)
            {
                for (int i = 0; i < 2; i++)
                {
                    provinces.Add(new ProvinceRecord
                    {
                        id = id,
                        name = $"{tag} {i}",
                        color = "#0000" + id.ToString("x2"),
                        owner = tag,
                        population = pop / 2,
                        gdpMillions = gdp / 2,
                        pixels = 100,
                        center = new[] { id, id },
                        neighbors = new[] { id == 1 ? 2 : id - 1 },
                    });
                    id++;
                }
            }
            var provinceFile = new ProvinceFile { width = 10, height = 10, provinces = provinces.ToArray() };
            var countryFile = new CountryFile
            {
                countries = countries.Select((c, i) => new CountryRecord
                {
                    tag = c.Item1,
                    name = c.Item1 + "land",
                    color = "#10203" + i,
                    capitalProvince = i * 2 + 1,
                }).ToArray(),
            };
            var world = WorldFactory.Create(provinceFile, countryFile, new WarRules(), WorldFactory.DefaultStartDate);

            var nations = new Dictionary<string, NationRecord>
            {
                ["DEM"] = Nation("DEM", "FullDemocracy", 2028, 11),
                ["ALY"] = Nation("ALY", "FullDemocracy", 2027, 5),
                ["AUT"] = Nation("AUT", "Authoritarian", 0, 0),
                ["SML"] = Nation("SML", "FlawedDemocracy", 2029, 3),
            };
            tweak?.Invoke(nations);

            var diplomacyFile = new DiplomacyFile
            {
                blocs = new[]
                {
                    new BlocRecord { id = "WEST", name = "Western Alliance", kind = "alliance", members = new[] { "DEM", "ALY" } },
                    new BlocRecord { id = "MKT", name = "Common Market", kind = "union", members = new[] { "DEM", "ALY" } },
                },
                relations = new[] { new RelationRecord { a = "DEM", b = "AUT", value = -60 } },
                sanctions = new SanctionRecord[0],
            };

            var sim = new GameSimulation(world, new NationFile { nations = nations.Values.ToArray() }, diplomacyFile,
                economy, politics, diplomacy, seed: 7);
            if (player)
                world.SetPlayer("DEM");
            return sim;
        }

        public static NationRecord Nation(string tag, string government, int electionYear, int electionMonth) => new NationRecord
        {
            tag = tag,
            government = government,
            taxRate = 0.35,
            debtToGdp = 0.6,
            interestRate = 0.03,
            safeDebtToGdp = 1.0,
            militarySpending = 0.02,
            welfareSpending = 0.16,
            educationSpending = 0.05,
            infrastructureSpending = 0.04,
            adminSpending = 0.06,
            realGrowth = 2,
            inflation = 2,
            populationGrowth = 0.005,
            stability = 60,
            approval = 50,
            electionYear = electionYear,
            electionMonth = electionMonth,
            electionInterval = 4,
        };

        public static void RunMonths(GameSimulation sim, int months)
        {
            for (int i = 0; i < months; i++)
            {
                var clock = sim.World.Clock;
                int m = clock.Date.Month;
                while (clock.Date.Month == m)
                    clock.StepDay();
            }
        }
    }
}
