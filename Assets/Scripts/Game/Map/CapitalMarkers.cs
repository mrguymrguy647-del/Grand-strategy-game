using System;
using System.Collections.Generic;
using GrandStrategy.Simulation;
using UnityEngine;

namespace GrandStrategy.Game.Map
{
    /// <summary>
    /// Draws a marker on every capital. Capitals matter: reaching one starts a Capital Battle.
    /// Markers keep a constant size on screen and the player's capital is gold.
    /// </summary>
    public sealed class CapitalMarkers : MonoBehaviour
    {
        const float ScreenFraction = 0.012f; // marker size relative to view height

        WorldState _world;
        MapView _map;
        Camera _camera;
        Sprite _sprite;
        readonly Dictionary<string, SpriteRenderer> _markers = new Dictionary<string, SpriteRenderer>();
        readonly List<IDisposable> _subscriptions = new List<IDisposable>();

        static readonly Color NormalColor = new Color(0.97f, 0.97f, 0.95f, 0.95f);
        static readonly Color PlayerColor = new Color(1f, 0.82f, 0.30f, 1f);
        static readonly Color DesperateColor = new Color(1f, 0.45f, 0.40f, 1f);

        public void Initialize(WorldState world, MapView map, Camera cam)
        {
            _world = world;
            _map = map;
            _camera = cam;
            _sprite = CreateMarkerSprite();

            _subscriptions.Add(world.Events.Subscribe<CapitalMoved>(_ => Rebuild()));
            _subscriptions.Add(world.Events.Subscribe<CountryEliminated>(_ => Rebuild()));
            _subscriptions.Add(world.Events.Subscribe<PlayerCountryChanged>(_ => Rebuild()));
            _subscriptions.Add(world.Events.Subscribe<ModifiersChanged>(_ => Rebuild()));
            Rebuild();
        }

        void OnDestroy()
        {
            foreach (var s in _subscriptions)
                s.Dispose();
            if (_sprite != null)
            {
                Destroy(_sprite.texture);
                Destroy(_sprite);
            }
        }

        void Rebuild()
        {
            var alive = new HashSet<string>();
            foreach (var country in _world.Countries)
            {
                if (country.IsEliminated || country.CapitalProvinceId == 0)
                    continue;
                alive.Add(country.Tag);
                if (!_markers.TryGetValue(country.Tag, out var marker))
                {
                    var go = new GameObject("Capital " + country.Tag);
                    go.transform.SetParent(transform, false);
                    marker = go.AddComponent<SpriteRenderer>();
                    marker.sprite = _sprite;
                    marker.sortingOrder = 30;
                    _markers.Add(country.Tag, marker);
                }
                // Slightly towards the camera so it always draws over the map.
                marker.transform.position = _map.ProvinceCenter(country.CapitalProvinceId) + new Vector3(0f, 0f, -0.01f);
                marker.color = country.Tag == _world.PlayerTag ? PlayerColor
                    : country.HasModifier(CapitalBattleRules.DesperateModifierId) ? DesperateColor
                    : NormalColor;
            }

            var dead = new List<string>();
            foreach (var kv in _markers)
                if (!alive.Contains(kv.Key))
                    dead.Add(kv.Key);
            foreach (var tag in dead)
            {
                Destroy(_markers[tag].gameObject);
                _markers.Remove(tag);
            }
        }

        void LateUpdate()
        {
            if (_camera == null)
                return;
            float size = _camera.orthographicSize * 2f * ScreenFraction;
            var scale = new Vector3(size, size, 1f);
            foreach (var marker in _markers.Values)
                marker.transform.localScale = scale;
        }

        /// <summary>A white ring with a dark outline, drawn in code so no art assets are needed.</summary>
        static Sprite CreateMarkerSprite()
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                name = "CapitalMarker",
            };
            var pixels = new Color32[size * size];
            float c = (size - 1) / 2f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                    // Layers from the centre: white dot, dark gap, white ring, dark outline.
                    float a;
                    Color32 col;
                    if (d < 0.38f) { col = new Color32(255, 255, 255, 255); a = 1f; }
                    else if (d < 0.55f) { col = new Color32(20, 20, 24, 255); a = 0.85f; }
                    else if (d < 0.80f) { col = new Color32(255, 255, 255, 255); a = 1f; }
                    else if (d < 1.0f) { col = new Color32(20, 20, 24, 255); a = Mathf.Clamp01((1f - d) / 0.08f); }
                    else { col = new Color32(0, 0, 0, 0); a = 0f; }
                    col.a = (byte)(a * 255);
                    pixels[y * size + x] = col;
                }
            tex.SetPixels32(pixels);
            tex.Apply(true);
            // Pixels per unit = size, so the sprite is 1 world unit wide before scaling.
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }
    }
}
