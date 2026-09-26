using GrandStrategy.Game.Map;
using GrandStrategy.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace GrandStrategy.Game.UI
{
    /// <summary>
    /// Builds and owns the whole interface (UI Toolkit, built in code, styled by Resources/UI/Game.uss):
    /// top bar, country panel, news, map modes, popups, tooltips and full-screen screens.
    /// </summary>
    public sealed class HudController : MonoBehaviour
    {
        const int MaxToasts = 4;
        const long ToastMilliseconds = 6000;

        GameRoot _game;
        UIDocument _document;
        VisualElement _root;
        TooltipManager _tips;
        TopBar _topBar;
        CountryPanel _panel;
        NotificationLog _news;
        MapModeBar _modes;
        NationSelectBanner _banner;
        EventPopup _popup;
        SettingsPanel _settings;
        GameOverScreen _gameOver;
        LoadingScreen _loading;
        DevPanel _dev;
        VisualElement _toasts;
        VisualElement _hints;
        Label _mapTooltip;

        /// <summary>Full-screen layer behind every panel, used for country names on the map.</summary>
        public VisualElement MapLabelLayer { get; private set; }

        public bool PopupOpen => _popup != null && _popup.IsOpen;
        public Country PanelCountry => _panel?.Country;

        public void Initialize(GameRoot game)
        {
            _game = game;
            CreateDocument();

            var vignette = Ui.Element();
            Ui.Fill(vignette);
            var vt = Resources.Load<Texture2D>("UI/Textures/vignette");
            if (vt != null)
                vignette.style.backgroundImage = new StyleBackground(vt);
            _root.Add(vignette);

            MapLabelLayer = Ui.Element();
            Ui.Fill(MapLabelLayer);
            _root.Add(MapLabelLayer);

            _tips = new TooltipManager(_root);
            _panel = new CountryPanel(game, _tips);
            _root.Add(_panel.Root);
            _news = new NotificationLog(game, _tips);
            _root.Add(_news.Root);
            _modes = new MapModeBar(game, _tips);
            _root.Add(_modes.Root);
            _hints = Ui.Label("Space pause   1-5 speed   WASD / drag move   Scroll zoom   F1-F7 map modes   Esc close   F12 dev tools",
                13, new Color(1, 1, 1, 0.5f));
            Ui.Absolute(_hints, 16, null, null, 18);
            _root.Add(_hints);
            _banner = new NationSelectBanner(game, _tips);
            _root.Add(_banner.Root);
            _topBar = new TopBar(game, _tips);
            _root.Add(_topBar.Root);

            _toasts = Ui.Column();
            Ui.Absolute(_toasts, null, null, 12, 70);
            _toasts.style.width = 360;
            _toasts.style.flexDirection = FlexDirection.ColumnReverse;
            _root.Add(_toasts);

            _dev = new DevPanel(game);
            _root.Add(_dev.Root);
            _popup = new EventPopup(game, _tips);
            _root.Add(_popup.Root);
            _settings = new SettingsPanel(game);
            _root.Add(_settings.Root);
            _gameOver = new GameOverScreen(game);
            _root.Add(_gameOver.Root);

            _mapTooltip = Ui.Label("", 14, null, Ui.Weight.SemiBold, "gs-tooltip");
            _mapTooltip.style.minWidth = 0;
            _mapTooltip.style.whiteSpace = WhiteSpace.NoWrap;
            Ui.Show(_mapTooltip, false);
            _root.Add(_mapTooltip);

            _loading = new LoadingScreen();
            _root.Add(_loading.Root);
            OnPhaseChanged();
        }

        void CreateDocument()
        {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.name = "HUD Panel Settings";
            settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("UI/DefaultRuntimeTheme");
            if (settings.themeStyleSheet == null)
                Debug.LogWarning("UI theme Resources/UI/DefaultRuntimeTheme.tss not found; UI may render without default styles.");
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1920, 1080);
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = 0.5f;
            settings.sortingOrder = 100;

            var go = new GameObject("HUD Document");
            go.transform.SetParent(transform, false);
            go.SetActive(false); // assign settings before OnEnable builds the panel
            _document = go.AddComponent<UIDocument>();
            _document.panelSettings = settings;
            go.SetActive(true);

            _root = _document.rootVisualElement;
            _root.pickingMode = PickingMode.Ignore;
            Ui.Fill(_root);
            var sheet = Resources.Load<StyleSheet>("UI/Game");
            if (sheet != null)
                _root.styleSheets.Add(sheet);
            else
                Debug.LogWarning("Stylesheet Resources/UI/Game.uss not found; the UI will look plain.");
            _root.AddToClassList("gs-root");
            Ui.SetFont(_root, Ui.Weight.Regular);
        }

        // ------------------------------------------------------------------ frame

        void Update()
        {
            if (_game == null || _game.World == null)
                return;
            _topBar.Refresh();
            _panel.Tick();
        }

        // ------------------------------------------------------------------ API used by GameRoot

        public void ShowLoading(string message) => _loading.Show(message);
        public void ShowError(string message) => _loading.Show(message, true);
        public void HideLoading() => _loading.Hide();

        /// <summary>True if the screen position is over a UI element that should block map input.</summary>
        public bool IsPointerOverUI(Vector2 screenPosition)
        {
            var panel = _root?.panel;
            if (panel == null)
                return false;
            var panelPos = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screenPosition.x, Screen.height - screenPosition.y));
            // Only elements inside this HUD count; the panel's own root is not a blocker.
            var picked = panel.Pick(panelPos);
            for (var e = picked; e != null; e = e.parent)
                if (e == _root)
                    return picked != _root;
            return false;
        }

        /// <summary>Small tooltip next to the cursor while hovering the map.</summary>
        public void SetTooltip(string text, Vector2 screenPosition)
        {
            if (string.IsNullOrEmpty(text) || _root.panel == null)
            {
                Ui.Show(_mapTooltip, false);
                return;
            }
            var p = RuntimePanelUtils.ScreenToPanel(_root.panel, new Vector2(screenPosition.x, Screen.height - screenPosition.y));
            _mapTooltip.text = text;
            _mapTooltip.style.left = p.x + 18;
            _mapTooltip.style.top = p.y + 14;
            Ui.Show(_mapTooltip, true);
        }

        public void Toast(string title, string body, Color? accent = null)
        {
            while (_toasts.childCount >= MaxToasts)
                _toasts.RemoveAt(0);
            var toast = Ui.Panel();
            toast.pickingMode = PickingMode.Ignore;
            Ui.Padding(toast, 10);
            toast.style.marginTop = 6;
            toast.style.borderLeftWidth = 4;
            toast.style.borderLeftColor = accent ?? Ui.Gold;
            toast.Add(Ui.Label(title, 16, accent ?? Ui.Gold, Ui.Weight.Bold));
            if (!string.IsNullOrEmpty(body))
            {
                var b = Ui.Label(body, 14);
                b.style.marginTop = 2;
                toast.Add(b);
            }
            _toasts.Add(toast);
            toast.schedule.Execute(() => toast.RemoveFromHierarchy()).StartingIn(ToastMilliseconds);
        }

        public void ToggleSettings() => _settings.Toggle();
        public void ToggleDevPanel() => _dev.Toggle();

        /// <summary>Closes the top-most closable overlay. Returns false if nothing was open.</summary>
        public bool CloseOverlay()
        {
            if (Ui.IsShown(_settings.Root))
            {
                Ui.Show(_settings.Root, false);
                return true;
            }
            if (Ui.IsShown(_dev.Root))
            {
                Ui.Show(_dev.Root, false);
                return true;
            }
            return false;
        }

        public void OnPhaseChanged()
        {
            var phase = _game.Phase;
            bool play = phase == GamePhase.Playing;
            bool select = phase == GamePhase.NationSelect;
            Ui.Show(_banner.Root, select && _panel.Country == null);
            Ui.Show(_modes.Root, play || select);
            Ui.Show(_hints, play || select);
            Ui.Show(_news.Root, play || select);
            if (phase != GamePhase.GameOver)
                _gameOver.Hide();
            RefreshModeButtons();
            _panel.MarkDirty();
        }

        public void RefreshModeButtons()
        {
            if (_game.Map != null)
                _modes.Refresh(_game.Map.Mode);
        }

        public void ShowCountry(Country country, int provinceId)
        {
            _panel.Show(country, provinceId);
            Ui.Show(_banner.Root, _game.Phase == GamePhase.NationSelect && country == null);
        }

        public void OpenCountryTab(string tab) => _panel.OpenTab(tab);
        public void MarkPanelDirty() => _panel.MarkDirty();
        public void RefreshPanelLive() => _panel.RefreshLive();
        public void ShowEvent(NationalEvent evt) => _popup.ShowEvent(evt);

        public void ShowMessage(string icon, string title, string text, Breakdown reasons = null, bool good = true) =>
            _popup.ShowMessage(icon, title, text, reasons, good);

        public void AddNews(NewsItem item) => _news.Add(item);

        public void ShowGameOver(string title, string text) => _gameOver.Show(title, text);
    }
}
