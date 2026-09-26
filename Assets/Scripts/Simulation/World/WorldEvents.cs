namespace GrandStrategy.Simulation
{
    // Events published on WorldState.Events. Kept as small immutable structs.

    public readonly struct ProvinceOwnerChanged
    {
        public readonly int ProvinceId;
        public readonly string OldOwner;
        public readonly string NewOwner;

        public ProvinceOwnerChanged(int provinceId, string oldOwner, string newOwner)
        {
            ProvinceId = provinceId;
            OldOwner = oldOwner;
            NewOwner = newOwner;
        }
    }

    public readonly struct CapitalMoved
    {
        public readonly string CountryTag;
        public readonly int OldProvinceId;
        public readonly int NewProvinceId;

        public CapitalMoved(string countryTag, int oldProvinceId, int newProvinceId)
        {
            CountryTag = countryTag;
            OldProvinceId = oldProvinceId;
            NewProvinceId = newProvinceId;
        }
    }

    public readonly struct CountryEliminated
    {
        public readonly string CountryTag;
        public readonly string ByTag;

        public CountryEliminated(string countryTag, string byTag)
        {
            CountryTag = countryTag;
            ByTag = byTag;
        }
    }

    public readonly struct PlayerCountryChanged
    {
        public readonly string CountryTag;

        public PlayerCountryChanged(string countryTag) => CountryTag = countryTag;
    }

    public readonly struct ModifiersChanged
    {
        public readonly string CountryTag;

        public ModifiersChanged(string countryTag) => CountryTag = countryTag;
    }
}

namespace GrandStrategy.Simulation
{
    public enum NewsImportance
    {
        Minor,
        Notable,
        Player,
    }

    public sealed class NewsItem
    {
        public NewsItem(GameDate date, NewsImportance importance, string title, string text, string countryTag, string otherTag)
        {
            Date = date;
            Importance = importance;
            Title = title;
            Text = text;
            CountryTag = countryTag;
            OtherTag = otherTag;
        }

        public GameDate Date { get; }
        public NewsImportance Importance { get; }
        public string Title { get; }
        public string Text { get; }
        public string CountryTag { get; }
        public string OtherTag { get; }
    }

    /// <summary>Something happened that belongs in the news feed.</summary>
    public readonly struct NewsPublished
    {
        public readonly NewsItem Item;

        public NewsPublished(NewsItem item) => Item = item;
    }

    /// <summary>A decision event for the player (protests, trade offers...), waiting for a choice.</summary>
    public readonly struct NationalEventRaised
    {
        public readonly NationalEvent Event;

        public NationalEventRaised(NationalEvent evt) => Event = evt;
    }

    /// <summary>The monthly update finished; UI can refresh numbers.</summary>
    public readonly struct SimulationTicked
    {
        public readonly GameDate Date;

        public SimulationTicked(GameDate date) => Date = date;
    }

    /// <summary>A government fell to revolution. For the player this is game over.</summary>
    public readonly struct GovernmentOverthrown
    {
        public readonly string CountryTag;
        public readonly bool IsPlayer;

        public GovernmentOverthrown(string countryTag, bool isPlayer)
        {
            CountryTag = countryTag;
            IsPlayer = isPlayer;
        }
    }

    /// <summary>Relations, agreements, sanctions or blocs changed between two countries.</summary>
    public readonly struct DiplomacyChanged
    {
        public readonly string A;
        public readonly string B;

        public DiplomacyChanged(string a, string b)
        {
            A = a;
            B = b;
        }
    }

    /// <summary>A country's policies (taxes, spending) or finances changed outside the monthly tick.</summary>
    public readonly struct PolicyChanged
    {
        public readonly string CountryTag;

        public PolicyChanged(string countryTag) => CountryTag = countryTag;
    }
}
