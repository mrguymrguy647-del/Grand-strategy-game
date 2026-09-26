using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GrandStrategy.Simulation.Data;
using Xunit;

namespace GrandStrategy.Simulation.Tests
{
    /// <summary>Checks the generated map data that ships with the game.</summary>
    public class WorldDataTests
    {
        internal static readonly Lazy<(ProvinceFile provinces, CountryFile countries, WarRules rules)> Files = new(() =>
            (Read<ProvinceFile>("Map", "provinces.json"), Read<CountryFile>("Map", "countries.json"), Read<WarRules>("Rules", "war.json")));

        internal static T Read<T>(params string[] path)
        {
            var options = new JsonSerializerOptions { IncludeFields = true };
            return JsonSerializer.Deserialize<T>(File.ReadAllText(
                Path.Combine(new[] { FindRepoRoot(), "Assets", "StreamingAssets", "Data" }.Concat(path).ToArray())), options);
        }

        /// <summary>The full game world with the nation systems, as the game builds it.</summary>
        internal static GameSimulation LoadSimulation(int seed = 2026)
        {
            var world = LoadWorld();
            return new GameSimulation(world, Read<NationFile>("World", "nations.json"), Read<DiplomacyFile>("World", "diplomacy.json"),
                Read<EconomyRules>("Rules", "economy.json"), Read<PoliticsRules>("Rules", "politics.json"),
                Read<DiplomacyRules>("Rules", "diplomacy.json"), seed);
        }

        static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Assets", "StreamingAssets")))
                dir = dir.Parent;
            return dir?.FullName ?? throw new DirectoryNotFoundException("Could not find the Unity project root.");
        }

        internal static WorldState LoadWorld() =>
            WorldFactory.Create(Files.Value.provinces, Files.Value.countries, Files.Value.rules, WorldFactory.DefaultStartDate);

        [Fact]
        public void LoadsWithoutWarnings()
        {
            var world = LoadWorld();
            Assert.Empty(world.LoadWarnings);
            Assert.True(world.ProvinceCount > 2000);
            Assert.True(world.Countries.Count > 190);
        }

        [Fact]
        public void EveryCountryHasNationDataAndValidBlocs()
        {
            var sim = LoadSimulation();
            Assert.Empty(sim.World.LoadWarnings);
            foreach (var c in sim.World.Countries)
            {
                Assert.NotNull(c.Economy);
                Assert.NotNull(c.Politics);
                Assert.InRange(c.Economy.TaxRate, 0.05, 0.6);
                Assert.InRange(c.Economy.TotalSpendingShare, 0.03, 1.0);
            }
            var diplomacy = Read<DiplomacyFile>("World", "diplomacy.json");
            foreach (var bloc in diplomacy.blocs)
                foreach (var tag in bloc.members)
                    Assert.NotNull(sim.World.GetCountry(tag));
            Assert.Equal(32, sim.Diplomacy.FindBloc("NATO").Members.Count);
        }

        [Fact]
        public void WarRulesFileUsesThe65PercentLine() => Assert.Equal(0.65, Files.Value.rules.heavyLossThreshold);

        [Fact]
        public void EveryCountryOwnsItsCapital()
        {
            var world = LoadWorld();
            foreach (var c in world.Countries)
            {
                Assert.False(c.IsEliminated, c.Name);
                Assert.Equal(c.Tag, world.GetProvince(c.CapitalProvinceId).OwnerTag);
            }
        }

        [Fact]
        public void NeighboursAreSymmetric()
        {
            var world = LoadWorld();
            foreach (var p in world.Provinces)
                foreach (var n in p.Neighbors)
                    Assert.Contains(p.Id, world.GetProvince(n).Neighbors);
        }

        [Fact]
        public void ProvinceColoursAreUniqueAndNotOcean()
        {
            var seen = new HashSet<int>();
            foreach (var p in LoadWorld().Provinces)
            {
                Assert.NotEqual(0, p.IdColorRgb);
                Assert.True(seen.Add(p.IdColorRgb), $"Duplicate colour for {p}");
            }
        }

        [Fact]
        public void EveryCountryHasAFlagAndIconsAreValidPngs()
        {
            string ui = Path.Combine(FindRepoRoot(), "Assets", "StreamingAssets", "UI");
            var noFlag = Files.Value.countries.countries
                .Where(c => !File.Exists(Path.Combine(ui, "Flags", c.tag + ".png")))
                .Select(c => c.tag).ToList();
            Assert.True(noFlag.Count == 0, "No flag for: " + string.Join(", ", noFlag));

            var icons = Directory.GetFiles(Path.Combine(ui, "Icons"), "*.png");
            Assert.True(icons.Length >= 40, $"Only {icons.Length} icons");
            foreach (var file in icons.Concat(Directory.GetFiles(Path.Combine(ui, "Flags"), "*.png")))
            {
                var (width, height) = PngSize(file);
                bool isIcon = file.Contains(Path.DirectorySeparatorChar + "Icons" + Path.DirectorySeparatorChar);
                Assert.True(isIcon ? width == 64 && height == 64 : width == 128 && height == 96,
                    $"{Path.GetFileName(file)} is {width}x{height}");
            }
        }

        /// <summary>Reads the size from a PNG header (and fails if the file is not a PNG).</summary>
        static (int width, int height) PngSize(string path)
        {
            var b = new byte[24];
            using (var f = File.OpenRead(path))
                Assert.Equal(24, f.Read(b, 0, 24));
            Assert.True(b[1] == 'P' && b[2] == 'N' && b[3] == 'G', $"{path} is not a PNG");
            return ((b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19], (b[20] << 24) | (b[21] << 16) | (b[22] << 8) | b[23]);
        }

        [Theory]
        [InlineData("USA", "Washington, D.C.")]
        [InlineData("CHN", "Beijing")]
        [InlineData("GBR", "London")]
        [InlineData("FRA", "Paris")]
        [InlineData("RUS", "Moscow")]
        [InlineData("ZAF", "Pretoria")]
        public void MajorCapitalsAreRight(string tag, string capital) =>
            Assert.Equal(capital, LoadWorld().GetCountry(tag).CapitalName);
    }
}
