using System;
using System.Collections.Generic;
using System.IO;
using GrandStrategy.Simulation;
using GrandStrategy.Simulation.Data;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GrandStrategy.Game.Map
{
    /// <summary>Everything loaded from StreamingAssets/Data that the game needs to start.</summary>
    public sealed class MapAssets
    {
        public ProvinceFile Provinces;
        public CountryFile Countries;
        public WarRules WarRules;

        public int Width;
        public int Height;

        /// <summary>Province id for every map pixel (row 0 = bottom). 0 = ocean.</summary>
        public ushort[] PixelProvince;

        /// <summary>Bounding box per province id, in pixels.</summary>
        public RectInt[] ProvinceBounds;

        public static string DataRoot => Path.Combine(Application.streamingAssetsPath, "Data");

        public static MapAssets Load()
        {
            var assets = new MapAssets
            {
                Provinces = ReadJson<ProvinceFile>("Map", "provinces.json"),
                Countries = ReadJson<CountryFile>("Map", "countries.json"),
                WarRules = ReadJson<WarRules>("Rules", "war.json"),
            };
            if (assets.Provinces.provinces == null || assets.Provinces.provinces.Length == 0)
                throw new InvalidDataException("provinces.json has no provinces.");
            if (assets.Provinces.provinces.Length >= ushort.MaxValue)
                throw new InvalidDataException("Too many provinces for the 16-bit province map.");

            assets.LoadProvinceMap(Path.Combine(DataRoot, "Map", "provinces.png"));

            assets.ProvinceBounds = new RectInt[assets.Provinces.provinces.Length + 1];
            foreach (var p in assets.Provinces.provinces)
            {
                if (p.bbox != null && p.bbox.Length == 4)
                    assets.ProvinceBounds[p.id] = new RectInt(p.bbox[0], p.bbox[1], p.bbox[2] - p.bbox[0] + 1, p.bbox[3] - p.bbox[1] + 1);
            }
            return assets;
        }

        static T ReadJson<T>(params string[] parts)
        {
            var path = Path.Combine(DataRoot, Path.Combine(parts));
            if (!File.Exists(path))
                throw new FileNotFoundException($"Missing game data file: {path}");
            var data = JsonUtility.FromJson<T>(File.ReadAllText(path));
            if (data == null)
                throw new InvalidDataException($"Could not read {path}.");
            return data;
        }

        void LoadProvinceMap(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"Missing province map: {path}");

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                if (!texture.LoadImage(File.ReadAllBytes(path)))
                    throw new InvalidDataException($"Could not decode {path}.");
                Width = texture.width;
                Height = texture.height;
                if (Width != Provinces.width || Height != Provinces.height)
                    Debug.LogWarning($"provinces.png is {Width}x{Height} but provinces.json says {Provinces.width}x{Provinces.height}.");

                var lookup = new Dictionary<int, ushort>(Provinces.provinces.Length);
                foreach (var p in Provinces.provinces)
                    lookup[WorldFactory.ParseHexColor(p.color)] = (ushort)p.id;

                // GetPixels32 returns rows bottom-up, the same texture space the JSON uses.
                var pixels = texture.GetPixels32();
                var ids = new ushort[pixels.Length];
                int lastKey = -1;
                ushort lastId = 0;
                int unknown = 0;
                for (int i = 0; i < pixels.Length; i++)
                {
                    var c = pixels[i];
                    int key = (c.r << 16) | (c.g << 8) | c.b;
                    if (key == 0)
                        continue;
                    if (key != lastKey)
                    {
                        lastKey = key;
                        if (!lookup.TryGetValue(key, out lastId))
                        {
                            lastId = 0;
                            unknown++;
                        }
                    }
                    ids[i] = lastId;
                }
                if (unknown > 0)
                    Debug.LogWarning($"provinces.png has {unknown} pixel runs with colours not listed in provinces.json.");
                PixelProvince = ids;
            }
            finally
            {
                Object.Destroy(texture);
            }
        }
    }
}
