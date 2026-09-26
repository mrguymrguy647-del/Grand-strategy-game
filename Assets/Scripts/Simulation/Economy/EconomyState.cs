namespace GrandStrategy.Simulation
{
    public enum SpendingCategory
    {
        Military,
        Welfare,
        Education,
        Infrastructure,
        Administration,
    }

    public enum CreditRating
    {
        AAA,
        AA,
        A,
        BBB,
        BB,
        B,
        CCC,
        D,
    }

    /// <summary>
    /// A country's public finances. Money is in US$ millions per year unless the name says
    /// monthly. Rates and spending are shares of GDP (0.30 = 30%). GDP itself lives in the
    /// provinces (so conquests move it); see <see cref="WorldState.GdpOf"/>.
    /// </summary>
    public sealed class EconomyState
    {
        public const int CategoryCount = 5;

        // Policy (what the government decides).
        public double TaxRate { get; internal set; }
        internal readonly double[] Spending = new double[CategoryCount];

        // Starting policy: most effects are measured against it.
        public double StartTaxRate { get; internal set; }
        internal readonly double[] StartSpending = new double[CategoryCount];
        public double StartDebtToGdp { get; internal set; }

        // Structural parameters.
        public double PotentialGrowth { get; internal set; }   // % real growth per year at start
        public double Inflation { get; internal set; }         // % per year
        public double PopulationGrowth { get; internal set; }  // fraction per year
        public double SafeDebtToGdp { get; internal set; }
        public double ForeignAid { get; internal set; }        // grants, share of GDP
        public double BaseInterestRate { get; internal set; }

        // State.
        public double Debt { get; internal set; }
        public double Treasury { get; internal set; }
        public double InterestRate { get; internal set; }
        public CreditRating Rating { get; internal set; }
        public int MonthsOverBurden { get; internal set; }
        public bool InDefault { get; internal set; }
        internal double Cycle;
        internal double StartTradeEffect;
        internal double StartSanctionEffect;
        internal double StartSanctionCost;

        // Last monthly result, for the UI.
        public double Gdp { get; internal set; }
        public double RealGrowth { get; internal set; }
        public Breakdown GrowthFactors { get; } = new Breakdown();
        public Breakdown MonthlyIncome { get; } = new Breakdown();
        public Breakdown MonthlyCosts { get; } = new Breakdown();
        public double MonthlyBalance => MonthlyIncome.Total - MonthlyCosts.Total;

        public double GetSpending(SpendingCategory c) => Spending[(int)c];
        public double GetStartSpending(SpendingCategory c) => StartSpending[(int)c];

        public double TotalSpendingShare
        {
            get
            {
                double s = 0;
                for (int i = 0; i < CategoryCount; i++)
                    s += Spending[i];
                return s;
            }
        }

        public double DebtToGdp => Gdp > 0 ? Debt / Gdp : 0;

        /// <summary>Share of tax revenue that goes to interest.</summary>
        public double InterestBurden
        {
            get
            {
                double revenue = Gdp * TaxRate;
                return revenue > 0 ? Debt * InterestRate / revenue : 0;
            }
        }
    }
}
