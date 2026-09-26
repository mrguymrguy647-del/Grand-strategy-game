using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GrandStrategy.Game.Audio;
using GrandStrategy.Game.Map;
using GrandStrategy.Game.UI;
using GrandStrategy.Simulation;
using GrandStrategy.Simulation.Data;
using UnityEngine;
using UnityEngine.Rendering;

namespace GrandStrategy.Game
{
    public enum GamePhase
    {
        Loading,
        NationSelect,
        Playing,
        GameOver,
        Error,
    }

    /// <summary>Outcomes for the developer Capital Battle test buttons.</summary>
    public enum CapitalBattleTestOutcome
    {
        CleanWin,
        CostlyWin,
        Repelled,
    }

    /// <summary>
    /// Owns the running game: loads the world and the nation systems, builds the map, camera,
    /// HUD and audio, runs the clock, and turns player input into game actions.
    /// </summary>
    public sealed class GameRoot : MonoBehaviour
    {
        const int TestManpower = 100_000;
        static readonly Color32 BackgroundColor = new Color32(14, 30, 48, 255);

        readonly List<IDisposable> _subscriptions = new List<IDisposable>();
        int _hoverId = -1;

        public WorldState World { get; private set; }
        public GameSimulation Sim { get; private set; }
        public MapView Map { get; private set; }
        public MapCameraController CameraController { get; private set; }
        public AudioManager Audio { get; private set; }
        public HudController Hud { get; private set; }
        public GamePhase Phase { get; private set; } = GamePhase.Loading;
        public Country SelectedCountry { get; private set; }
        public int SelectedProvinceId { get; private set; }
        public bool BattleInProgress { get; private set; }

        /// <summary>Time can run: playing, and no event is waiting for the player's decision.</summary>
        public bool CanRunTime => Phase == GamePhase.Playing && !BattleInProgress && (Hud == null || !Hud.PopupOpen) &&
                                  (Sim == null || Sim.Events.Pending.Count == 0);

        IEnumerator Start()
        {
            ErrorConsole.Ensure();
            var pipeline = GraphicsSettings.currentRenderPipeline;
            Debug.Log($"Grand Strategy starting: Unity {Application.unityVersion}, {SystemInfo.graphicsDeviceType} " +
                      $"(shader level {SystemInfo.graphicsShaderLevel}), input: {GameInput.BackendName}, " +
                      $"render pipeline: {(pipeline == null ? "Built-in" : pipeline.name)}, colour space: {QualitySettings.activeColorSpace}");

            Audio = new GameObject("Audio").AddComponent<AudioManager>();
            Audio.transform.SetParent(transform, false);
            Ui.Audio = Audio;
            Audio.PlayMusic(MusicMood.Menu);

            var cam = SetUpCamera();

            Hud = new GameObject("HUD").AddComponent<HudController>();
            Hud.transform.SetParent(transform, false);
            Hud.Initialize(this);
            Hud.ShowLoading("Loading the world...");
            yield return null; // let the loading screen draw before the heavy work

            MapAssets assets;
            try
            {
                assets = MapAssets.Load();
                World = WorldFactory.Create(assets.Provinces, assets.Countries, assets.WarRules, WorldFactory.DefaultStartDate);
                Sim = new GameSimulation(World,
                    ReadData<NationFile>("World", "nations.json"),
                    ReadData<DiplomacyFile>("World", "diplomacy.json"),
                    ReadData<EconomyRules>("Rules", "economy.json", optional: true),
                    ReadData<PoliticsRules>("Rules", "politics.json", optional: true),
                    ReadData<DiplomacyRules>("Rules", "diplomacy.json", optional: true),
                    seed: Environment.TickCount);
                foreach (var warning in World.LoadWarnings)
                    Debug.LogWarning(warning);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                SetPhase(GamePhase.Error);
                Hud.ShowError("Could not load the game data.\n" + e.Message);
                yield break;
            }

            Hud.ShowLoading($"Drawing {World.ProvinceCount} provinces of {World.Countries.Count} nations...");
            yield return null;

            try
            {
                BuildMap(assets, cam);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                SetPhase(GamePhase.Error);
                Hud.ShowError("Could not draw the map.\n" + e.Message);
                yield break;
            }

            // The monthly chime would be every half second at the top speeds.
            World.Clock.MonthPassed += _ =>
            {
                if (World.Clock.Speed <= 3)
                    Audio.Play(Sfx.NewMonth, 0.8f);
            };
            World.Clock.YearPassed += OnYearPassed;
            _subscriptions.Add(World.Events.Subscribe<ProvinceOwnerChanged>(_ => Hud.MarkPanelDirty()));
            _subscriptions.Add(World.Events.Subscribe<ModifiersChanged>(_ => Hud.MarkPanelDirty()));
            _subscriptions.Add(World.Events.Subscribe<SimulationTicked>(_ => Hud.MarkPanelDirty()));
            _subscriptions.Add(World.Events.Subscribe<DiplomacyChanged>(_ => Hud.MarkPanelDirty()));
            _subscriptions.Add(World.Events.Subscribe<PolicyChanged>(_ => Hud.RefreshPanelLive()));
            _subscriptions.Add(World.Events.Subscribe<NewsPublished>(e => Hud.AddNews(e.Item)));
            _subscriptions.Add(World.Events.Subscribe<NationalEventRaised>(OnNationalEvent));
            _subscriptions.Add(World.Events.Subscribe<GovernmentOverthrown>(OnGovernmentOverthrown));
            _subscriptions.Add(World.Events.Subscribe<CapitalMoved>(OnCapitalMoved));
            _subscriptions.Add(World.Events.Subscribe<CountryEliminated>(OnCountryEliminated));

            Hud.HideLoading();
            SetPhase(GamePhase.NationSelect);
        }

        static T ReadData<T>(string folder, string file, bool optional = false) where T : class, new()
        {
            var path = Path.Combine(MapAssets.DataRoot, folder, file);
            if (!File.Exists(path))
            {
                if (optional)
                    return new T();
                throw new FileNotFoundException($"Missing game data file: {path}");
            }
            return JsonUtility.FromJson<T>(File.ReadAllText(path)) ?? new T();
        }

        void BuildMap(MapAssets assets, Camera cam)
        {
            Map = new GameObject("World Map").AddComponent<MapView>();
            Map.transform.SetParent(transform, false);
            Map.Initialize(World, assets, Sim);
            Map.ModeChanged += _ => Hud.RefreshModeButtons();
            Debug.Log($"Map renderer: {(Map.UsesShader ? "shader" : "CPU fallback")}, {Map.Width}x{Map.Height} province map.");

            var markers = new GameObject("Capitals").AddComponent<CapitalMarkers>();
            markers.transform.SetParent(transform, false);
            markers.Initialize(World, Map, cam);

            var labels = new GameObject("Country Names").AddComponent<MapLabels>();
            labels.transform.SetParent(transform, false);
            labels.Initialize(World, Map, assets, cam, Hud.MapLabelLayer);

            CameraController = cam.gameObject.GetComponent<MapCameraController>();
            if (CameraController == null)
                CameraController = cam.gameObject.AddComponent<MapCameraController>();
            CameraController.Initialize(cam, Map.WorldRect, Hud.IsPointerOverUI);
        }

        void OnDestroy()
        {
            foreach (var s in _subscriptions)
                s.Dispose();
            _subscriptions.Clear();
            if (World != null)
                World.Clock.YearPassed -= OnYearPassed;
        }

        static Camera SetUpCamera()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = go.AddComponent<Camera>();
            }
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = BackgroundColor;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 100f;
            cam.transform.SetPositionAndRotation(new Vector3(0f, 0f, -10f), Quaternion.identity);
            if (FindAnyObjectByType<AudioListener>() == null)
                cam.gameObject.AddComponent<AudioListener>();
            return cam;
        }

        // ------------------------------------------------------------------ frame loop

        void Update()
        {
            if (World == null || Map == null || CameraController == null ||
                Phase == GamePhase.Loading || Phase == GamePhase.Error)
                return;

            HandleHotkeys();

            if (!CanRunTime && !World.Clock.IsPaused)
                World.Clock.SetPaused(true); // events and battles stop the clock
            if (CanRunTime)
                World.Clock.Advance(Time.unscaledDeltaTime);

            HandleMapPointer();
        }

        void HandleHotkeys()
        {
            if (GameInput.KeyDown(GameKey.Cancel) && !Hud.CloseOverlay())
                SelectCountry(null);

            if (GameInput.KeyDown(GameKey.ToggleMute))
            {
                Audio.Muted = !Audio.Muted;
                Hud.Toast(Audio.Muted ? "Sound off" : "Sound on", "Press M to toggle.");
            }
            if (GameInput.KeyDown(GameKey.DevPanel))
                Hud.ToggleDevPanel();

            if (GameInput.KeyDown(GameKey.MapMode1)) SetMapMode(MapMode.Political);
            if (GameInput.KeyDown(GameKey.MapMode2)) SetMapMode(MapMode.Diplomatic);
            if (GameInput.KeyDown(GameKey.MapMode3)) SetMapMode(MapMode.Wealth);
            if (GameInput.KeyDown(GameKey.MapMode4)) SetMapMode(MapMode.Growth);
            if (GameInput.KeyDown(GameKey.MapMode5)) SetMapMode(MapMode.Stability);
            if (GameInput.KeyDown(GameKey.MapMode6)) SetMapMode(MapMode.Government);
            if (GameInput.KeyDown(GameKey.MapMode7)) SetMapMode(MapMode.Population);

            if (Phase != GamePhase.Playing)
                return;
            if (GameInput.KeyDown(GameKey.Pause)) TogglePause();
            if (GameInput.KeyDown(GameKey.Speed1)) SetSpeed(1);
            if (GameInput.KeyDown(GameKey.Speed2)) SetSpeed(2);
            if (GameInput.KeyDown(GameKey.Speed3)) SetSpeed(3);
            if (GameInput.KeyDown(GameKey.Speed4)) SetSpeed(4);
            if (GameInput.KeyDown(GameKey.Speed5)) SetSpeed(5);
            if (GameInput.KeyDown(GameKey.SpeedUp)) SpeedUp();
            if (GameInput.KeyDown(GameKey.SpeedDown)) SlowDown();
        }

        void HandleMapPointer()
        {
            bool interactive = (Phase == GamePhase.Playing || Phase == GamePhase.NationSelect) && !Hud.PopupOpen;
            if (interactive && CameraController.LeftClicked)
            {
                int id = Map.ProvinceAt(CameraController.PointerWorld);
                var province = World.GetProvince(id);
                SelectCountry(World.GetCountry(province?.OwnerTag), id);
            }
            if (interactive && CameraController.RightClicked)
                SelectCountry(null);

            var pointer = GameInput.PointerPosition;
            int hover = 0;
            if (interactive && GameInput.HasPointer && !CameraController.IsDragging && !Hud.IsPointerOverUI(pointer))
                hover = Map.ProvinceAt(CameraController.PointerWorld);

            if (hover != _hoverId)
            {
                _hoverId = hover;
                Map.SetHover(hover);
            }

            var p = World.GetProvince(hover);
            if (p == null)
            {
                Hud.SetTooltip(null, pointer);
                return;
            }
            var owner = World.GetCountry(p.OwnerTag);
            string text = owner == null ? p.Name : $"{owner.Name}  -  {p.Name}";
            if (owner != null && owner.CapitalProvinceId == p.Id)
                text += "  (capital)";
            if (owner != null && Phase == GamePhase.Playing && World.Player != null && owner != World.Player)
                text += $"   |   relations {Sim.Diplomacy.Relations(World.Player, owner):+0;-0;0}";
            Hud.SetTooltip(text, pointer);
        }

        // ------------------------------------------------------------------ actions (called by input and HUD)

        /// <summary>Opens the country panel (null closes it). The clicked province is shown in Overview.</summary>
        public void SelectCountry(Country country, int provinceId = 0)
        {
            bool changed = country != SelectedCountry;
            SelectedCountry = country;
            SelectedProvinceId = country == null ? 0 : provinceId;
            Map.SetSelectedCountry(country);
            Map.SetSelected(SelectedProvinceId);
            Map.SetDiplomacyReference(Phase == GamePhase.Playing && World.Player != null ? World.Player : country);
            Hud.ShowCountry(country, SelectedProvinceId);
            if (country != null && changed)
                Audio.Play(Sfx.ProvinceSelect);
        }

        public void ChooseNation(string tag)
        {
            var country = World.GetCountry(tag);
            if (country == null || country.IsEliminated)
                return;
            World.SetPlayer(tag);
            SetPhase(GamePhase.Playing);
            Map.SetPlayerCountry(country);
            Map.SetDiplomacyReference(country);
            Audio.Play(Sfx.NationChosen);
            Audio.PlayMusic(MusicMood.Peace);
            CameraController.FlyTo(Map.ProvinceCenter(country.CapitalProvinceId), 3.2f);
            SelectCountry(country, country.CapitalProvinceId);
            Hud.ShowMessage("crown", $"You lead {country.Name}",
                $"It is {World.Clock.Date}. Your country's panel is open on the left: check the Economy and Politics tabs, " +
                "then click other countries for diplomacy. Hover any number to see why it is what it is.\n\n" +
                "Press Space (or Play) to start time. Keep stability above 10, or your government will fall.");
        }

        /// <summary>Throws this world away and loads a fresh one (used after a game over).</summary>
        public void RestartGame()
        {
            World?.Clock.SetPaused(true);
            new GameObject("Game").AddComponent<GameRoot>();
            Destroy(gameObject);
        }

        public void SetMapMode(MapMode mode)
        {
            if (Map == null || Map.Mode == mode)
                return;
            Map.SetMode(mode);
            Audio.Play(Sfx.UiClick);
        }

        public void TogglePause()
        {
            if (Phase != GamePhase.Playing || !CanRunTime && World.Clock.IsPaused)
                return;
            World.Clock.TogglePause();
            Audio.Play(World.Clock.IsPaused ? Sfx.Pause : Sfx.Resume);
        }

        public void SetSpeed(int speed)
        {
            if (Phase != GamePhase.Playing)
                return;
            int old = World.Clock.Speed;
            World.Clock.SetSpeed(speed);
            if (World.Clock.Speed != old)
                Audio.Play(World.Clock.Speed > old ? Sfx.SpeedUp : Sfx.SpeedDown);
        }

        public void SpeedUp() => SetSpeed(World.Clock.Speed + 1);
        public void SlowDown() => SetSpeed(World.Clock.Speed - 1);

        /// <summary>Runs a diplomatic action from the player and shows the outcome.</summary>
        public void DoDiplomacy(Country target, DiplomaticAction action)
        {
            var player = World.Player;
            if (player == null || target == null || Phase != GamePhase.Playing)
                return;
            var result = Sim.DiplomacySystem.Execute(player, target, action);
            if (!result.Done)
            {
                Hud.Toast("Not possible", result.Message, Ui.Bad);
                return;
            }
            if (result.Preview.NeedsConsent)
            {
                Hud.ShowMessage(result.Accepted ? "handshake" : "denounce",
                    result.Accepted ? "Proposal accepted" : "Proposal refused", result.Message,
                    result.Preview.Opinion, result.Accepted);
                Audio.Play(result.Accepted ? Sfx.NationChosen : Sfx.Notification);
            }
            else
            {
                Hud.Toast(result.Preview.Title, result.Message);
                Audio.Play(action == DiplomaticAction.ImposeSanctions || action == DiplomaticAction.Denounce ? Sfx.WarDeclared : Sfx.Notification);
            }
            Hud.MarkPanelDirty();
        }

        public void EnactDecision(DecisionId id)
        {
            var player = World.Player;
            if (player == null || Phase != GamePhase.Playing)
                return;
            if (Sim.Decisions.Execute(player, id, out var message))
            {
                Hud.Toast("Decision enacted", message);
                Audio.Play(Sfx.Notification);
            }
            else
            {
                Hud.Toast("Not possible", message, Ui.Bad);
            }
            Hud.MarkPanelDirty();
        }

        public void ResolveEvent(NationalEvent evt, int option)
        {
            Sim.Events.Choose(evt, option);
            Hud.MarkPanelDirty();
        }

        void SetPhase(GamePhase phase)
        {
            Phase = phase;
            if (phase != GamePhase.Playing)
                World?.Clock.SetPaused(true);
            Hud.OnPhaseChanged();
        }

        // ------------------------------------------------------------------ world events

        void OnNationalEvent(NationalEventRaised e)
        {
            World.Clock.SetPaused(true);
            Hud.ShowEvent(e.Event);
        }

        void OnYearPassed(GameDate date)
        {
            Audio.Play(Sfx.NewYear);
            Hud.Toast($"The year {date.Year} begins", null);
        }

        void OnCapitalMoved(CapitalMoved e)
        {
            var country = World.GetCountry(e.CountryTag);
            var province = World.GetProvince(e.NewProvinceId);
            if (country == null || province == null)
                return;
            Hud.Toast($"{country.Name} moves its capital", $"The government flees to {province.Name}.", Ui.Bad);
            Hud.MarkPanelDirty();
        }

        void OnCountryEliminated(CountryEliminated e)
        {
            Hud.MarkPanelDirty();
            var fallen = World.GetCountry(e.CountryTag);
            var by = World.GetCountry(e.ByTag);
            if (e.CountryTag == World.PlayerTag)
            {
                GameOver("YOUR NATION HAS FALLEN",
                    $"{fallen?.Name} has been annexed{(by != null ? " by " + by.Name : "")}. History will remember the defence of its capital.");
                return;
            }
            Hud.Toast($"{fallen?.Name} has fallen", by != null ? $"It has been annexed by {by.Name}." : null, Ui.Bad);
        }

        void OnGovernmentOverthrown(GovernmentOverthrown e)
        {
            if (!e.IsPlayer)
                return;
            var p = World.Player;
            GameOver("OVERTHROWN",
                $"Months of chaos in {p?.Name} ended in revolution. Your government has fallen after " +
                $"{MonthsPlayed()} months in power.\n\nKeep an eye on stability: low approval turns into anger, protests, then revolution.");
        }

        int MonthsPlayed()
        {
            var d = World.Clock.Date;
            return (d.Year - 2026) * 12 + d.Month - 1;
        }

        void GameOver(string title, string text)
        {
            SetPhase(GamePhase.GameOver);
            Audio.Play(Sfx.Defeat);
            Audio.PlayMusic(MusicMood.Defeat);
            Hud.ShowGameOver(title, text);
        }

        // ------------------------------------------------------------------ developer tools

        public void DevTrigger(string what)
        {
            var player = World?.Player;
            if (player == null || Phase != GamePhase.Playing)
            {
                Hud.Toast("Developer tools", "Choose a nation first.", Ui.Warn);
                return;
            }
            switch (what)
            {
                case "protests":
                    Sim.Events.RaiseProtests(player);
                    break;
                case "random":
                    Sim.Events.RaiseRandomEvent(player);
                    break;
                case "trade_offer":
                    var friend = World.Countries.Where(c => c != player && !c.IsEliminated && !Sim.Diplomacy.AreTradePartners(c, player))
                        .OrderByDescending(c => Sim.Diplomacy.Relations(c, player)).FirstOrDefault();
                    if (friend != null)
                        Sim.Events.RaiseTradeOffer(player, friend);
                    break;
                case "money":
                    Sim.Economy.Borrow(player, 50_000);
                    break;
                case "unrest":
                    player.AddOrRefreshModifier(new Modifier("dev_unrest", "Unrest (developer test)", "Added from the developer panel.", 0, 180) { Stability = -30 });
                    Sim.Economy.Refresh(player);
                    break;
            }
            Hud.MarkPanelDirty();
        }

        /// <summary>
        /// Developer test: resolves a Capital Battle with fixed losses so the outcome rules can be
        /// seen on the map before armies and the tactical battle scene exist (Phase 2).
        /// </summary>
        public void RunCapitalBattleTest(string otherTag, bool playerAttacks, CapitalBattleTestOutcome outcome)
        {
            if (BattleInProgress || Phase != GamePhase.Playing)
                return;
            var player = World.Player;
            var other = World.GetCountry(otherTag);
            if (player == null || other == null || player.IsEliminated || other.IsEliminated || player == other)
                return;
            StartCoroutine(CapitalBattleTestRoutine(player, other, playerAttacks, outcome));
        }

        IEnumerator CapitalBattleTestRoutine(Country player, Country other, bool playerAttacks, CapitalBattleTestOutcome outcome)
        {
            BattleInProgress = true;
            var attacker = playerAttacks ? player : other;
            var defender = playerAttacks ? other : player;
            string capitalName = defender.CapitalName;

            Hud.Toast("CAPITAL BATTLE", $"{attacker.Name} storms {capitalName}, capital of {defender.Name}!", Ui.Bad);
            Audio.Play(Sfx.CapitalBattleStart);
            CameraController.FlyTo(Map.ProvinceCenter(defender.CapitalProvinceId), 2.4f);
            yield return new WaitForSecondsRealtime(2.4f);

            double attackerLossShare, defenderLossShare;
            var winner = BattleSide.Attacker;
            switch (outcome)
            {
                case CapitalBattleTestOutcome.CleanWin:
                    attackerLossShare = playerAttacks ? 0.40 : 0.30;
                    defenderLossShare = 0.90;
                    break;
                case CapitalBattleTestOutcome.CostlyWin:
                    attackerLossShare = playerAttacks ? 0.72 : 0.80;
                    defenderLossShare = 0.95;
                    break;
                default:
                    winner = BattleSide.Defender;
                    attackerLossShare = 0.85;
                    defenderLossShare = 0.45;
                    break;
            }

            var result = new CapitalBattleResult(attacker.Tag, defender.Tag, defender.CapitalProvinceId, winner,
                TestManpower, (int)(TestManpower * attackerLossShare), TestManpower, (int)(TestManpower * defenderLossShare));

            CapitalBattleReport report = null;
            try
            {
                report = CapitalBattleRules.Apply(World, result);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            BattleInProgress = false;
            Hud.MarkPanelDirty();
            if (report != null)
                AnnounceCapitalBattle(report, attacker, defender, capitalName);
        }

        void AnnounceCapitalBattle(CapitalBattleReport report, Country attacker, Country defender, string capitalName)
        {
            string losses = Ui.FormatPercent(report.Result.WinnerLossRatio);
            string threshold = Ui.FormatPercent(World.WarRules.heavyLossThreshold);
            bool playerWon = (report.Outcome == CapitalBattleOutcome.CapitalHeld) == (defender.Tag == World.PlayerTag);

            switch (report.Outcome)
            {
                case CapitalBattleOutcome.TotalVictory:
                    Hud.Toast($"{attacker.Name} conquers {defender.Name}",
                        $"{capitalName} fell and the winner lost only {losses} of its troops (under {threshold}). " +
                        $"All {report.ProvincesTransferred} provinces are annexed.", playerWon ? Ui.Good : Ui.Bad);
                    break;
                case CapitalBattleOutcome.PyrrhicVictory:
                    var lastCapital = World.GetProvince(report.NewDefenderCapitalId);
                    Hud.Toast($"{capitalName} falls, at a terrible price",
                        $"{attacker.Name} lost {losses} of its troops ({threshold} or more), so it takes only the capital. " +
                        (report.DefenderEliminated
                            ? $"{defender.Name} had nowhere left to go."
                            : $"{defender.Name} fights on from its Last Capital, {lastCapital?.Name}, and is Desperate " +
                              $"({Ui.FormatPercent(World.WarRules.desperateMorale)} morale)."),
                        playerWon ? Ui.Good : Ui.Bad);
                    break;
                default:
                    Hud.Toast($"{capitalName} holds!",
                        $"{defender.Name} threw back the assault and gains Heroic Defense; {attacker.Name} is Routed.",
                        playerWon ? Ui.Good : Ui.Bad);
                    break;
            }

            if (Phase == GamePhase.Playing)
                Audio.Play(playerWon ? Sfx.Victory : Sfx.Defeat);
        }
    }
}
