using System.Collections.Generic;

namespace GrandStrategy.Simulation
{
    /// <summary>One labelled contribution to a number, used to explain values in tooltips.</summary>
    public readonly struct Factor
    {
        public readonly string Label;
        public readonly double Value;

        public Factor(string label, double value)
        {
            Label = label;
            Value = value;
        }

        public override string ToString() => $"{Label}: {Value:+0.##;-0.##;0}";
    }

    /// <summary>A number together with the factors that make it up.</summary>
    public sealed class Breakdown
    {
        readonly List<Factor> _factors = new List<Factor>();

        public IReadOnlyList<Factor> Factors => _factors;
        public double Total { get; private set; }

        public void Clear()
        {
            _factors.Clear();
            Total = 0;
        }

        /// <summary>Adds a factor. Near-zero values are counted but not listed.</summary>
        public void Add(string label, double value)
        {
            Total += value;
            if (System.Math.Abs(value) >= 0.005)
                _factors.Add(new Factor(label, value));
        }
    }
}
