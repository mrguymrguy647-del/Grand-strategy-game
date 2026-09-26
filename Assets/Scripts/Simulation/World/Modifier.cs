namespace GrandStrategy.Simulation
{
    /// <summary>
    /// A temporary (or permanent) effect on a country, e.g. "Desperate" or "Stimulus package".
    /// Each effect is optional; set the ones that apply with an object initializer.
    /// </summary>
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

        /// <summary>Real GDP growth, in percentage points per year.</summary>
        public double Growth { get; set; }

        /// <summary>Shifts the approval the population drifts towards (points).</summary>
        public double Approval { get; set; }

        /// <summary>Shifts the stability the country drifts towards (points).</summary>
        public double Stability { get; set; }

        /// <summary>Extra interest markets demand on government debt (e.g. 0.02 = +2%).</summary>
        public double InterestPremium { get; set; }

        /// <summary>Days left, or <see cref="Permanent"/>.</summary>
        public int DaysRemaining { get; internal set; }

        public bool IsPermanent => DaysRemaining == Permanent;
    }
}
