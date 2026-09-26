using System;

namespace GrandStrategy.Simulation.Data
{
    // Plain data classes matching the JSON files in StreamingAssets/Data/Map.
    // Public lowerCamelCase fields so Unity's JsonUtility (and System.Text.Json with
    // IncludeFields) can read them without extra attributes.

    [Serializable]
    public sealed class ProvinceFile
    {
        public string source;
        public int width;
        public int height;
        public string projection;
        public double minLatitude;
        public double maxLatitude;
        public ProvinceRecord[] provinces;
    }

    [Serializable]
    public sealed class ProvinceRecord
    {
        public int id;
        public string color;
        public string name;
        public string owner;
        public long population;
        public double gdpMillions;
        public int pixels;
        public int[] center;
        public int[] bbox;
        public bool coastal;
        public int[] neighbors;
    }

    [Serializable]
    public sealed class CountryFile
    {
        public string source;
        public CountryRecord[] countries;
    }

    [Serializable]
    public sealed class CountryRecord
    {
        public string tag;
        public string name;
        public string formalName;
        public string isoA2;
        public string color;
        public int capitalProvince;
        public string capitalName;
        public long population;
        public double gdpMillions;
        public string continent;
        public string subregion;
        public string incomeGroup;
    }
}
