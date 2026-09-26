using System;
using System.Collections.Generic;
using GrandStrategy.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace GrandStrategy.Game.Map
{
    /// <summary>
    /// Country names written across each country, sized and rotated to fit its shape (like
    /// the big names in grand strategy games). Small countries appear as you zoom in; names
    /// move when borders change.
    /// </summary>
    public sealed class MapLabels : MonoBehaviour
    {
        const float MinFontPx = 10f;
        const float MaxFontPx = 220f;
        const double MaxAngleDegrees = 40;

        sealed class Entry
        {
            public Label Label;
            public double X;        // centre in map pixels (y up)
            public double Y;
            public float Angle;     // degrees, counter-clockwise on the map
            public double Size;     // font size in map pixels
        }

        WorldState _world;
        MapView _map;
        MapAssets _assets;
        Camera _camera;
        VisualElement _layer;
        readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>();
        readonly List<IDisposable> _subscriptions = new List<IDisposable>();
        bool _shapeDirty = true;
        bool _layoutDirty = true;
        Vector3 _lastCameraPos;
        float _lastCameraSize;
        Vector2Int _lastScreen;

        public void Initialize(WorldState world, MapView map, MapAssets assets, Camera cam, VisualElement layer)
        {
            _world = world;
            _map = map;
            _assets = assets;
            _camera = cam;
            _layer = layer;
            _subscriptions.Add(world.Events.Subscribe<ProvinceOwnerChanged>(_ => _shapeDirty = true));
            _subscriptions.Add(world.Events.Subscribe<CountryEliminated>(_ => _shapeDirty = true));
        }

        void OnDestroy()
        {
            foreach (var s in _subscriptions)
                s.Dispose();
        }

        void LateUpdate()
        {
            if (_world == null || _layer?.panel == null)
                return;
            if (_shapeDirty)
            {
                _shapeDirty = false;
                RecomputeShapes();
                _layoutDirty = true;
            }

            var screen = new Vector2Int(Screen.width, Screen.height);
            if (_layoutDirty || _camera.transform.position != _lastCameraPos ||
                !Mathf.Approximately(_camera.orthographicSize, _lastCameraSize) || screen != _lastScreen)
            {
                _layoutDirty = false;
                _lastCameraPos = _camera.transform.position;
                _lastCameraSize = _camera.orthographicSize;
                _lastScreen = screen;
                Layout();
            }
        }

        /// <summary>Finds each country's biggest connected area and fits a label to it.</summary>
        void RecomputeShapes()
        {
            var moments = _assets.Moments;
            var seen = new HashSet<int>();
            var stack = new Stack<int>();
            var alive = new HashSet<string>();

            foreach (var country in _world.Countries)
            {
                if (country.IsEliminated)
                    continue;

                // Split the country into connected areas; remember the biggest and the capital's.
                ProvinceMoments biggest = default, capitalArea = default;
                bool capitalIsBiggest = true;
                seen.Clear();
                foreach (var start in country.ProvinceIds)
                {
                    if (!seen.Add(start))
                        continue;
                    ProvinceMoments part = default;
                    bool hasCapital = false;
                    stack.Push(start);
                    while (stack.Count > 0)
                    {
                        int id = stack.Pop();
                        if (id < moments.Length)
                            part.Add(moments[id]);
                        hasCapital |= id == country.CapitalProvinceId;
                        foreach (var n in _world.GetProvince(id).Neighbors)
                            if (_world.GetProvince(n)?.OwnerTag == country.Tag && seen.Add(n))
                                stack.Push(n);
                    }
                    if (hasCapital)
                        capitalArea = part;
                    if (part.Count > biggest.Count)
                    {
                        biggest = part;
                        capitalIsBiggest = hasCapital;
                    }
                }
                if (biggest.Count < 1)
                    continue;

                string text = country.Name.ToUpperInvariant();
                Place(country.Tag, text, biggest, alive);
                // Countries whose biggest area isn't the home one (Denmark and Greenland)
                // get a second label at home.
                if (!capitalIsBiggest && capitalArea.Count >= 1)
                    Place(country.Tag + "#home", text, capitalArea, alive);
            }

            var dead = new List<string>();
            foreach (var kv in _entries)
                if (!alive.Contains(kv.Key))
                    dead.Add(kv.Key);
            foreach (var key in dead)
            {
                _entries[key].Label.RemoveFromHierarchy();
                _entries.Remove(key);
            }
        }

        void Place(string key, string text, in ProvinceMoments area, HashSet<string> alive)
        {
            double cx = area.SumX / area.Count, cy = area.SumY / area.Count;
            double vxx = area.SumXX / area.Count - cx * cx;
            double vyy = area.SumYY / area.Count - cy * cy;
            double vxy = area.SumXY / area.Count - cx * cy;
            double angle = 0.5 * Math.Atan2(2 * vxy, vxx - vyy);
            double tr = vxx + vyy, det = vxx * vyy - vxy * vxy;
            double disc = Math.Sqrt(Math.Max(0, tr * tr / 4 - det));
            double along = Math.Sqrt(12 * Math.Max(1, tr / 2 + disc));
            double across = Math.Sqrt(12 * Math.Max(1, tr / 2 - disc));

            double degrees = angle * 180 / Math.PI;
            if (degrees > 90) degrees -= 180;
            if (degrees < -90) degrees += 180;
            // Steep countries (Chile, Norway) keep their names closer to horizontal.
            if (Math.Abs(degrees) > MaxAngleDegrees)
            {
                degrees = Math.Sign(degrees) * MaxAngleDegrees;
                along = Math.Min(along, across * 1.6);
            }

            int letters = Math.Max(3, text.Length);
            double size = Math.Min(0.8 * along / (0.72 * letters), 0.55 * across);
            // The text must also fit the real area: scattered islands (e.g. Kiribati across the
            // date line) have a huge spread but almost no land, so they get small labels.
            size = Math.Min(size, Math.Sqrt(0.45 * area.Count / (0.72 * letters)));
            size = Math.Min(size, 70);

            if (!_entries.TryGetValue(key, out var entry))
            {
                entry = new Entry { Label = CreateLabel(text) };
                _entries.Add(key, entry);
                _layer.Add(entry.Label);
            }
            entry.Label.text = text;
            entry.X = cx;
            entry.Y = cy;
            entry.Angle = (float)degrees;
            entry.Size = size;
            entry.Label.style.rotate = new Rotate(new Angle(-entry.Angle, AngleUnit.Degree));
            alive.Add(key);
        }

        static Label CreateLabel(string text)
        {
            var l = new Label(text) { pickingMode = PickingMode.Ignore };
            l.style.position = Position.Absolute;
            l.style.whiteSpace = WhiteSpace.NoWrap;
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.color = new Color(1f, 1f, 1f, 0.88f);
            l.style.unityTextOutlineColor = new Color(0.05f, 0.05f, 0.08f, 0.85f);
            l.style.unityTextOutlineWidth = 1.2f;
            l.style.unityTextAlign = TextAnchor.MiddleCenter;
            l.style.marginLeft = 0;
            l.style.marginRight = 0;
            l.style.paddingLeft = 0;
            l.style.paddingRight = 0;
            l.style.paddingTop = 0;
            l.style.paddingBottom = 0;
            l.style.translate = new Translate(Length.Percent(-50), Length.Percent(-50), 0);
            return l;
        }

        void Layout()
        {
            float layoutHeight = _layer.panel.visualTree.layout.height;
            if (float.IsNaN(layoutHeight) || layoutHeight < 1)
            {
                _layoutDirty = true; // panel not laid out yet
                return;
            }
            float panelScale = Screen.height / layoutHeight;
            float screenPxPerMapPx = Screen.height / (2f * _camera.orthographicSize) / MapView.PixelsPerUnit;
            float margin = 200f;

            foreach (var e in _entries.Values)
            {
                float fontPx = (float)(e.Size * screenPxPerMapPx / panelScale);
                var screen = _camera.WorldToScreenPoint(_map.PixelToWorld((float)e.X, (float)e.Y));
                bool onScreen = screen.x > -margin && screen.x < Screen.width + margin &&
                                screen.y > -margin && screen.y < Screen.height + margin;
                if (fontPx < MinFontPx || fontPx > MaxFontPx || !onScreen)
                {
                    e.Label.style.display = DisplayStyle.None;
                    continue;
                }

                float fadeIn = Mathf.InverseLerp(MinFontPx, MinFontPx + 4f, fontPx);
                float fadeOut = 1f - Mathf.InverseLerp(MaxFontPx * 0.7f, MaxFontPx, fontPx);
                e.Label.style.display = DisplayStyle.Flex;
                e.Label.style.opacity = Mathf.Min(fadeIn, fadeOut);
                e.Label.style.fontSize = fontPx;
                e.Label.style.letterSpacing = fontPx * 0.12f;
                e.Label.style.left = screen.x / panelScale;
                e.Label.style.top = (Screen.height - screen.y) / panelScale;
            }
        }
    }
}
