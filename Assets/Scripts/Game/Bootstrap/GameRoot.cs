using System;
using System.Collections;
using System.Collections.Generic;
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
    /// Owns the running game: loads the world, builds the map, camera, HUD and audio,
    /// runs the clock, and turns player input into game actions.
    /// </summary>
    public sealed class GameRoot : MonoBehaviour
    {
        const int TestManpower = 100_000;
        static readonly Color32 BackgroundColor = new Color32(14, 30, 48, 255);

        readonly List<IDisposable> _subscriptions = new List<IDisposable>();
        int _hoverId = -1;
        bool _infoDirty;

        public WorldState World { get; private set; }
        public MapView Map { get; private set; }
        public MapCameraController CameraController { get; private set; }
        public AudioManager Audio { get; private set; }
        public HudController Hud { get; private set; }
        public GamePhase Phase { get; private set; } = GamePhase.Loading;
        public int SelectedProvinceId { get; private set; }
        public bool BattleInProgress { get; private set; }

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
            _subscriptions.Add(World.Events.Subscribe<ProvinceOwnerChanged>(_ => _infoDirty = true));
            _subscriptions.Add(World.Events.Subscribe<ModifiersChanged>(_ => _infoDirty = true));
            _subscriptions.Add(World.Events.Subscribe<CapitalMoved>(OnCapitalMoved));
            _subscriptions.Add(World.Events.Subscribe<CountryEliminated>(OnCountryEliminated));

            Hud.HideLoading();
            SetPhase(GamePhase.NationSelect);
        }

        void BuildMap(MapAssets assets, Camera cam)
        {
            Map = new GameObject("World Map").AddComponent<MapView>();
            Map.transform.SetParent(transform, false);
            Map.Initialize(World, assets);
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

            if (Phase == GamePhase.Playing && !BattleInProgress)
                World.Clock.Advance(Time.unscaledDeltaTime);

            HandleMapPointer();

            if (_infoDirty)
            {
                _infoDirty = false;
                Hud.RefreshInfo();
            }
        }

        void HandleHotkeys()
        {
            if (GameInput.KeyDown(GameKey.Cancel) && !Hud.CloseOverlay())
                SelectProvince(0);

            if (GameInput.KeyDown(GameKey.ToggleMute))
            {
                Audio.Muted = !Audio.Muted;
                Hud.Toast(Audio.Muted ? "Sound off" : "Sound on", "Press M to toggle.");
            }

            if (GameInput.KeyDown(GameKey.MapMode1)) SetMapMode(MapMode.Political);
            if (GameInput.KeyDown(GameKey.MapMode2)) SetMapMode(MapMode.Population);
            if (GameInput.KeyDown(GameKey.MapMode3)) SetMapMode(MapMode.Wealth);

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
            bool interactive = Phase == GamePhase.Playing || Phase == GamePhase.NationSelect;
            if (interactive && CameraController.LeftClicked)
                SelectProvince(Map.ProvinceAt(CameraController.PointerWorld));
            if (interactive && CameraController.RightClicked)
                SelectProvince(0);

            var pointer = GameInput.PointerPosition;
            int hover = 0;
            if (interactive && GameInput.HasPointer && !CameraController.IsDragging && !Hud.IsPointerOverUI(pointer))
                hover = Map.ProvinceAt(CameraController.PointerWorld);

            if (hover != _hoverId)
            {
                _hoverId = hover;
                Map.SetHover(hover);
            }

            var province = World.GetProvince(hover);
            if (province == null)
            {
                Hud.SetTooltip(null, pointer);
                return;
            }
            var owner = World.GetCountry(province.OwnerTag);
            string text = owner == null ? province.Name : $"{province.Name}  -  {owner.Name}";
            if (owner != null && owner.CapitalProvinceId == province.Id)
                text += "  (capital)";
            Hud.SetTooltip(text, pointer);
        }

        // ------------------------------------------------------------------ actions (called by input and HUD)

        public void SelectProvince(int provinceId)
        {
            if (provinceId == SelectedProvinceId)
                return;
            SelectedProvinceId = provinceId;
            Map.SetSelected(provinceId);
            Hud.RefreshInfo();
            if (provinceId != 0)
                Audio.Play(Sfx.ProvinceSelect);
        }

        public void ChooseNation(string tag)
        {
            var country = World.GetCountry(tag);
            if (country == null || country.IsEliminated)
                return;
            World.SetPlayer(tag);
            SetPhase(GamePhase.Playing);
            Audio.Play(Sfx.NationChosen);
            Audio.PlayMusic(MusicMood.Peace);
            CameraController.FlyTo(Map.ProvinceCenter(country.CapitalProvinceId), 3.2f);
            Hud.Toast($"You lead {country.Name}", $"It is {World.Clock.Date}. Press Space or Play to start time.");
        }

        public void ReturnToNationSelect()
        {
            SelectProvince(0);
            SetPhase(GamePhase.NationSelect);
            Audio.PlayMusic(MusicMood.Menu);
            CameraController.ShowWholeMap();
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
            if (Phase != GamePhase.Playing || BattleInProgress)
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

        void SetPhase(GamePhase phase)
        {
            Phase = phase;
            if (phase != GamePhase.Playing)
                World?.Clock.SetPaused(true);
            Hud.OnPhaseChanged();
        }

        // ------------------------------------------------------------------ world events

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
            _infoDirty = true;
        }

        void OnCountryEliminated(CountryEliminated e)
        {
            _infoDirty = true;
            var fallen = World.GetCountry(e.CountryTag);
            var by = World.GetCountry(e.ByTag);
            if (e.CountryTag == World.PlayerTag)
            {
                SetPhase(GamePhase.GameOver);
                Audio.Play(Sfx.Defeat);
                Audio.PlayMusic(MusicMood.Defeat);
                return;
            }
            Hud.Toast($"{fallen?.Name} has fallen", by != null ? $"It has been annexed by {by.Name}." : null, Ui.Bad);
        }

        // ------------------------------------------------------------------ Capital Battle test

        /// <summary>
        /// Developer test: resolves a Capital Battle with fixed losses so the outcome rules can be
        /// seen on the map before armies and the tactical battle scene exist.
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
            _infoDirty = true;
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
            _infoDirty = true;
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
