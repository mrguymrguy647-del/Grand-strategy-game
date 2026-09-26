using System;
using System.Collections.Generic;
using System.IO;
using GrandStrategy.Simulation;
using GrandStrategy.Simulation.Data;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GrandStrategy.Game.Map
{
    /// <summary>Pixel statistics of one province, used to place country names.</summary>
    public struct ProvinceMoments
    {
        public double Count;
        public double SumX;
        public double SumY;
        public double SumXX;
        public double SumYY;
        public double SumXY;

        public void Add(in ProvinceMoments o)
        {
            Count += o.Count;
            SumX += o.SumX;
            SumY += o.SumY;
            SumXX += o.SumXX;
            SumYY += o.SumYY;
            SumXY += o.SumXY;
        }
    }

    /// <summary>Everything loaded from StreamingAssets/Data that the game needs to start.</summary>
    public sealed class MapAssets
    {
        public ProvinceFile Provinces;
        public CountryFile Countries;
        public WarRules WarRules;

        public int Width;
        public int Height;

        /// <summary>Province id for every map pixel (row 0 = bottom). 0 = water.</summary>
        public ushort[] PixelProvince;

        /// <summary>Bounding box per province id, in pixels.</summary>
        public RectInt[] ProvinceBounds;

        /// <summary>Pixel moments per province id.</summary>
        public ProvinceMoments[] Moments;

        public static string DataRoot => Path.Combine(Application.streamingAssetsPath, "Data");
        public static string TerrainPath => Path.Combine(DataRoot, "Map", "terrain.jpg");

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
                if (p.bbox != null && p.bbox.Length == 4 && p.id > 0 && p.id < assets.ProvinceBounds.Length)
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

                int n = Provinces.provinces.Length + 1;
                var lookup = new Dictionary<int, ushort>(n);
                foreach (var p in Provinces.provinces)
                    lookup[WorldFactory.ParseHexColor(p.color)] = (ushort)p.id;

                // GetPixels32 returns rows bottom-up, the same texture space the JSON uses.
                var pixels = texture.GetPixels32();
                var ids = new ushort[pixels.Length];
                var moments = new ProvinceMoments[n];
                int lastKey = -1;
                ushort lastId = 0;
                int unknown = 0;
                for (int y = 0; y < Height; y++)
                {
                    int row = y * Width;
                    for (int x = 0; x < Width; x++)
                    {
                        var c = pixels[row + x];
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
                        if (lastId == 0)
                            continue;
                        ids[row + x] = lastId;
                        ref var m = ref moments[lastId];
                        m.Count += 1;
                        m.SumX += x;
                        m.SumY += y;
                        m.SumXX += (double)x * x;
                        m.SumYY += (double)y * y;
                        m.SumXY += (double)x * y;
                    }
                }
                if (unknown > 0)
                    Debug.LogWarning($"provinces.png has {unknown} pixel runs with colours not listed in provinces.json.");
                PixelProvince = ids;
                Moments = moments;
            }
            finally
            {
                Object.Destroy(texture);
            }
        }

        /// <summary>
        /// Loads terrain.jpg as a mipmapped, block-compressed texture. Returns null if it is missing.
        /// </summary>
        public static Texture2D LoadTerrain()
        {
            var path = TerrainPath;
            if (!File.Exists(path))
            {
                Debug.LogWarning($"Terrain texture not found at {path}; the map will be drawn without terrain.");
                return null;
            }

            var decoded = new Texture2D(2, 2, TextureFormat.RGB24, false, false);
            try
            {
                if (!decoded.LoadImage(File.ReadAllBytes(path)))
                {
                    Debug.LogWarning($"Could not decode {path}.");
                    return null;
                }

                // Copy into a texture with a mip chain so the map stays smooth when zoomed out.
                var terrain = new Texture2D(decoded.width, decoded.height, decoded.format, true, false)
                {
                    name = "Terrain",
                    filterMode = FilterMode.Trilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    anisoLevel = 4,
                };
                terrain.SetPixelData(decoded.GetRawTextureData<byte>(), 0);
                terrain.Apply(true, false);
                if (terrain.width % 4 == 0 && terrain.height % 4 == 0)
                    terrain.Compress(false);
                terrain.Apply(false, true); // upload and free the CPU copy
                return terrain;
            }
            catch (Exception e)
            {
                Debug.LogWarning("Terrain texture could not be loaded: " + e.Message);
                return null;
            }
            finally
            {
                Object.Destroy(decoded);
            }
        }
    }
}
