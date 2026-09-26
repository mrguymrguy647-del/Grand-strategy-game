using System;

namespace GrandStrategy.Simulation
{
    // Tunable numbers for the nation systems. Loaded from StreamingAssets/Data/Rules/*.json;
    // every field has a sensible default so a missing file or field never breaks the game.
    // Most effects are measured against a country's STARTING policy, so the world begins in
    // balance and changes come from decisions and events.

    [Serializable]
    public sealed class EconomyRules
    {
        public double taxDragPerPoint = 0.12;          // growth pp lost per tax pp above the start
        public double taxCutBoostPerPoint = 0.05;      // growth pp gained per tax pp below the start
        public double infrastructurePerPoint = 0.35;   // growth pp per pp of GDP of extra infrastructure
        public double infrastructureCap = 1.5;
        public double educationPerPoint = 0.20;
        public double educationCap = 1.0;
        public double stabilityGrowthPerPoint = 0.03;  // growth pp per stability point above the start
        public double debtOverhangPerUnit = 1.5;       // growth pp lost per 1.0 of debt/GDP above the safe level
        public double tradeBasePerPartner = 0.03;      // growth pp per trade partner
        public double tradePerWorldShare = 2.0;        // extra growth pp per partner share of world GDP
        public double tradeCap = 2.0;
        public double sanctionsPerWorldShare = 6.0;    // growth pp lost per share of world GDP sanctioning you
        public double sanctionsCap = 3.0;
        public double sanctionerCostPerWorldShare = 1.5;
        public double sanctionerCostCap = 0.6;
        public double longRunInflation = 3.0;          // % per year that inflation drifts towards
        public double disinflationRate = 0.015;        // share of the gap closed each month
        public double cycleVolatility = 0.15;          // monthly shock (pp) of the business cycle
        public double cyclePersistence = 0.95;
        public double riskPremiumPerUnit = 0.03;       // extra interest per 1.0 of debt/GDP above the safe level
        public double instabilityPremium = 0.03;       // extra interest at stability 0 (from 40 downwards)
        public double interestAdjustMonths = 36;       // debt rolls over slowly
        public double adminCollectionFloor = 0.85;     // tax collected with no administration budget
        public double defaultBurden = 0.55;            // interest / revenue that starts a debt crisis
        public int defaultMonths = 6;
        public double defaultHaircut = 0.30;
        public double defaultGrowthPenalty = 4.0;
        public double defaultPremium = 0.05;
        public double minTaxRate = 0.05;
        public double maxTaxRate = 0.60;
        public double maxSpendingShare = 0.35;
    }

    [Serializable]
    public sealed class PoliticsRules
    {
        public double approvalPerGrowthPoint = 3.0;
        public double approvalGrowthCap = 15;
        public double approvalPerTaxPoint = 1.2;       // approval lost per tax pp above the start
        public double approvalPerWelfarePoint = 2.0;   // approval gained per welfare pp above the start
        public double approvalPerEducationPoint = 0.6;
        public double approvalAdjustRate = 0.12;       // share of the gap closed each month
        public double stabilityPerApprovalPoint = 0.5;
        public double angerThreshold = 25;             // below this approval, anger eats stability fast
        public double angerPerPoint = 1.2;
        public double stabilityPerMilitaryPointAutocracy = 3.0;
        public double stabilityPerMilitaryPointDemocracy = 0.5;
        public double stabilityPerAdminPoint = 2.0;
        public double stabilityAdjustRate = 0.08;
        public double protestThreshold = 35;
        public double protestBaseChance = 0.05;
        public double protestScaleChance = 0.25;
        public int protestCooldownMonths = 6;
        public double revolutionThreshold = 10;
        public int revolutionMonths = 3;
        public double democracyWinApproval = 45;
        public double hybridWinApproval = 30;
        public double randomEventChance = 0.12;        // per month, player only
    }

    [Serializable]
    public sealed class DiplomacyRules
    {
        public double driftRate = 0.05;                // share of the gap to the target closed each month
        public double goodwillDecay = 0.02;            // per month
        public double allianceBonus = 40;
        public double unionBonus = 25;
        public double tradeBlocBonus = 15;
        public double forumBonus = 8;
        public double membershipCap = 60;
        public double democraciesBonus = 10;
        public double regimeClashPenalty = 10;
        public double autocraciesBonus = 3;
        public double sanctionsPenalty = 35;
        public double tradeAgreementBonus = 10;
        public double improveGoodwill = 15;
        public double improveImmediate = 5;
        public double improveCostShare = 0.0005;       // of own GDP
        public double improveMinCost = 20;             // $ millions
        public int improveCooldownMonths = 6;
        public double denounceGoodwill = -25;
        public double denounceImmediate = -15;
        public double tradeAcceptScore = 10;
        public double pactAcceptScore = 20;
        public double pactMinRelations = 40;
        public double joinMinAverage = 45;
        public double aiTradeOfferChance = 0.15;       // per month, to the player
        public double aiSanctionChance = 0.01;         // per month, per hostile pair
        public double aiRetaliationChance = 0.5;
    }
}
