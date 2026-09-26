using System;
using GrandStrategy.Simulation.Data;

namespace GrandStrategy.Simulation
{
    /// <summary>
    /// Runs the nation systems on top of a <see cref="WorldState"/>. Every in-game month it
    /// updates, in order: economy, politics, diplomacy, events. All player actions go through
    /// the systems exposed here, and AI countries use the same rules.
    /// </summary>
    public sealed class GameSimulation
    {
        readonly double[] _gdp;
        bool _overthrowReported;

        public GameSimulation(WorldState world, NationFile nations, DiplomacyFile diplomacy,
            EconomyRules economyRules = null, PoliticsRules politicsRules = null, DiplomacyRules diplomacyRules = null,
            int seed = 2026)
        {
            World = world ?? throw new ArgumentNullException(nameof(world));
            EconomyRules = economyRules ?? new EconomyRules();
            PoliticsRules = politicsRules ?? new PoliticsRules();
            DiplomacyRules = diplomacyRules ?? new DiplomacyRules();
            Rng = new Random(seed);
            _gdp = new double[world.Countries.Count];
            Diplomacy = new DiplomacyState(world.Countries.Count);

            Economy = new EconomySystem(this);
            Politics = new PoliticsSystem(this);
            DiplomacySystem = new DiplomacySystem(this);
            Events = new NationalEvents(this);
            Decisions = new DecisionSystem(this);

            NationSetup.Apply(this, nations, diplomacy);
            RefreshGdp();
            Economy.Initialize();
            Politics.Initialize();
            DiplomacySystem.Initialize();

            World.Clock.MonthPassed += RunMonth;
        }

        public WorldState World { get; }
        public EconomyRules EconomyRules { get; }
        public PoliticsRules PoliticsRules { get; }
        public DiplomacyRules DiplomacyRules { get; }
        public Random Rng { get; }

        public DiplomacyState Diplomacy { get; }
        public EconomySystem Economy { get; }
        public PoliticsSystem Politics { get; }
        public DiplomacySystem DiplomacySystem { get; }
        public NationalEvents Events { get; }
        public DecisionSystem Decisions { get; }

        public GameDate Date => World.Clock.Date;
        public double WorldGdp { get; private set; }

        /// <summary>GDP in US$ millions, as of the last monthly update.</summary>
        public double GdpOf(Country c) => _gdp[c.Index];

        /// <summary>Share of world GDP, 0..1.</summary>
        public double GdpShare(Country c) => WorldGdp > 0 ? _gdp[c.Index] / WorldGdp : 0;

        public bool IsPlayer(Country c) => c != null && c.Tag == World.PlayerTag;

        internal void RefreshGdp()
        {
            double total = 0;
            foreach (var c in World.Countries)
            {
                double g = c.IsEliminated ? 0 : World.GdpOf(c);
                _gdp[c.Index] = g;
                total += g;
            }
            WorldGdp = total;
        }

        /// <summary>One month of simulation. Called by the clock; public so tests can drive it.</summary>
        public void RunMonth(GameDate date)
        {
            RefreshGdp();
            Economy.Monthly(date);
            RefreshGdp();
            Politics.Monthly(date);
            DiplomacySystem.Monthly(date);
            Events.Monthly(date);

            var player = World.Player;
            if (player?.Politics != null && player.Politics.Overthrown && !_overthrowReported)
            {
                _overthrowReported = true;
                World.Events.Publish(new GovernmentOverthrown(player.Tag, true));
            }
            World.Events.Publish(new SimulationTicked(date));
        }

        internal void News(NewsImportance importance, string title, string text, Country a = null, Country b = null)
        {
            if (IsPlayer(a) || IsPlayer(b))
                importance = NewsImportance.Player;
            World.Events.Publish(new NewsPublished(new NewsItem(Date, importance, title, text, a?.Tag, b?.Tag)));
        }

        /// <summary>Countries that matter for world news: the player and big economies.</summary>
        internal NewsImportance ImportanceOf(Country a, Country b = null)
        {
            if (IsPlayer(a) || IsPlayer(b))
                return NewsImportance.Player;
            if (GdpShare(a) >= 0.01 || (b != null && GdpShare(b) >= 0.01))
                return NewsImportance.Notable;
            return NewsImportance.Minor;
        }
    }
}
