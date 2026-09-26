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
