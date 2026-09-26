namespace GrandStrategy.Simulation
{
    /// <summary>A temporary (or permanent) effect on a country, e.g. "Desperate".</summary>
    public sealed class Modifier
    {
        public const int Permanent = -1;

        public Modifier(string id, string name, string description, double morale, int days)
        {
            Id = id;
            Name = name;
            Description = description;
            Morale = morale;
            DaysRemaining = days;
        }

        public string Id { get; }
        public string Name { get; }
        public string Description { get; }

        /// <summary>Added to the country's morale multiplier (e.g. -0.25 = -25%).</summary>
        public double Morale { get; }

        /// <summary>Days left, or <see cref="Permanent"/>.</summary>
        public int DaysRemaining { get; internal set; }

        public bool IsPermanent => DaysRemaining == Permanent;
    }
}
