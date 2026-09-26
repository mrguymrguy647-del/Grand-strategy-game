using System;
using System.Collections.Generic;

namespace GrandStrategy.Simulation
{
    /// <summary>One province on the world map.</summary>
    public sealed class Province
    {
        public Province(int id, string name, int idColorRgb, long population, double gdpMillions,
            int centerX, int centerY, int pixels, bool isCoastal, int[] neighbors)
        {
            Id = id;
            Name = name;
            IdColorRgb = idColorRgb;
            Population = population;
            GdpMillions = gdpMillions;
            CenterX = centerX;
            CenterY = centerY;
            Pixels = pixels;
            IsCoastal = isCoastal;
            Neighbors = neighbors ?? Array.Empty<int>();
        }

        public int Id { get; }
        public string Name { get; }

        /// <summary>The unique colour of this province in provinces.png (0xRRGGBB).</summary>
        public int IdColorRgb { get; }

        /// <summary>Tag of the owning country, or null if nobody owns it.</summary>
        public string OwnerTag { get; internal set; }

        public long Population { get; internal set; }
        public double GdpMillions { get; internal set; }

        /// <summary>Label/unit anchor in map texture space (y up).</summary>
        public int CenterX { get; }
        public int CenterY { get; }

        /// <summary>Size on the map in pixels.</summary>
        public int Pixels { get; }

        public bool IsCoastal { get; }
        public IReadOnlyList<int> Neighbors { get; }

        public override string ToString() => $"{Name} (#{Id}, {OwnerTag ?? "unowned"})";
    }
}
