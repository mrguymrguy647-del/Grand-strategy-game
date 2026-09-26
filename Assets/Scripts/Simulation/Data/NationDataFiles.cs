using System;

namespace GrandStrategy.Simulation.Data
{
    // JSON files in StreamingAssets/Data/World, written by Tools/mapgen/generate_nations.py.

    [Serializable]
    public sealed class NationFile
    {
        public string note;
        public NationRecord[] nations;
    }

    /// <summary>Starting situation of one country. Shares are fractions of GDP (0.30 = 30%).</summary>
    [Serializable]
    public sealed class NationRecord
    {
        public string tag;
        public string government;
        public double taxRate;
        public double debtToGdp;
        public double interestRate;
        public double safeDebtToGdp;
        public double foreignAid;
        public double militarySpending;
        public double welfareSpending;
        public double educationSpending;
        public double infrastructureSpending;
        public double adminSpending;
        public double realGrowth;       // % per year
        public double inflation;        // % per year
        public double populationGrowth; // fraction per year
        public double stability;        // 0..100
        public double approval;         // 0..100
        public int electionYear;        // 0 = no competitive elections
        public int electionMonth;
        public int electionInterval;    // years
    }

    [Serializable]
    public sealed class DiplomacyFile
    {
        public string note;
        public BlocRecord[] blocs;
        public RelationRecord[] relations;
        public SanctionRecord[] sanctions;
    }

    [Serializable]
    public sealed class BlocRecord
    {
        public string id;
        public string name;
        public string kind; // alliance, union, trade, forum
        public string[] members;
    }

    [Serializable]
    public sealed class RelationRecord
    {
        public string a;
        public string b;
        public int value;
    }

    [Serializable]
    public sealed class SanctionRecord
    {
        public string target;
        public string[] by;
    }
}
