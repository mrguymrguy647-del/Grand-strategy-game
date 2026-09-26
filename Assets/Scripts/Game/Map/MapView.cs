using System;
using System.Collections.Generic;
using GrandStrategy.Simulation;
using UnityEngine;

namespace GrandStrategy.Game.Map
{
    public enum MapMode
    {
        Political,
        Population,
        Wealth,
    }

    /// <summary>
    /// Draws the world map.
    ///
    /// Normal path (GPU): the WorldMap shader combines the terrain texture, a province-id texture
    /// and a small per-province colour table, drawing smooth borders on the graphics card. Changing
    /// map mode or ownership only rewrites the colour table.
    ///
    /// Fallback path (CPU): if the shader can't run on this machine, the map is recoloured on the
    /// CPU into one texture, with overlay sprites for hover and selection.
    /// </summary>
    public sealed class MapView : MonoBehaviour
    {
        public const float PixelsPerUnit = 100f;
        const int OceanShades = 16;
        const int LutWidth = 256;
        const byte WaterOwner = 255;

        static readonly Color32 UnownedColor = new Color32(138, 136, 124, 255);
        static readonly Color32 ShallowOcean = new Color32(52, 94, 128, 255);
        static readonly Color32 DeepOcean = new Color32(19, 41, 66, 255);

        WorldState _world;
        MapAssets _assets;
        int _width;
        int _height;
        ushort[] _ids;
        byte[] _oceanShade;
        Color32[] _oceanPalette;

        Texture2D _texture;
        Color32[] _pixels;

        // Per province id: fill colour, province-border colour, country-border colour, owner index.
        Color32[] _fill;
        Color32[] _provinceEdge;
        Color32[] _countryEdge;
        int[] _ownerIndex;

        bool _dirty;
        readonly List<IDisposable> _subscriptions = new List<IDisposable>();

        ProvinceHighlight _selection;
        ProvinceHighlight _hover;

        // GPU path
        bool _gpu;
        Material _material;
        Mesh _mesh;
        Texture2D _idTexture;
        Texture2D _terrain;
        Texture2D _lut;
        Color32[] _lutPixels;

        static readonly int TerrainTexId = Shader.PropertyToID("_TerrainTex");
        static readonly int ProvinceTexId = Shader.PropertyToID("_ProvinceTex");
        static readonly int LutTexId = Shader.PropertyToID("_LutTex");
        static readonly int ProvinceTexSizeId = Shader.PropertyToID("_ProvinceTexSize");
        static readonly int LutSizeId = Shader.PropertyToID("_LutSize");
        static readonly int SelectedIdId = Shader.PropertyToID("_SelectedId");
        static readonly int HoverIdId = Shader.PropertyToID("_HoverId");
        static readonly int TintStrengthId = Shader.PropertyToID("_TintStrength");

        public MapMode Mode { get; private set; } = MapMode.Political;

        /// <summary>True when the shader renderer is in use (false = CPU fallback).</summary>
        public bool UsesShader => _gpu;
        public int Width => _width;
        public int Height => _height;

        /// <summary>World-space rectangle covered by the map.</summary>
        public Rect WorldRect => new Rect(-_width / 2f / PixelsPerUnit, -_height / 2f / PixelsPerUnit,
            _width / PixelsPerUnit, _height / PixelsPerUnit);

        public event Action<MapMode> ModeChanged;

        public void Initialize(WorldState world, MapAssets assets)
        {
            _world = world;
            _assets = assets;
            _width = assets.Width;
            _height = assets.Height;
            _ids = assets.PixelProvince;

            int n = world.ProvinceCount + 1;
            _fill = new Color32[n];
            _provinceEdge = new Color32[n];
            _countryEdge = new Color32[n];
            _ownerIndex = new int[n];

            _subscriptions.Add(world.Events.Subscribe<ProvinceOwnerChanged>(_ => _dirty = true));
            _subscriptions.Add(world.Events.Subscribe<PlayerCountryChanged>(_ => _dirty = true));

            var shader = Resources.Load<Shader>("Shaders/WorldMap");
            if (shader != null && shader.isSupported && SystemInfo.graphicsShaderLevel >= 35)
            {
                try
                {
                    InitializeGpu(shader);
                    return;
                }
                catch (Exception e)
                {
                    Debug.LogWarning("Map shader setup failed, using the CPU map instead: " + e.Message);
                    CleanUpGpu();
                }
            }
            else
            {
                Debug.LogWarning("Map shader not available on this machine; using the CPU map.");
            }
            InitializeCpu();
        }

        // ------------------------------------------------------------------ GPU path

        void InitializeGpu(Shader shader)
        {
            _gpu = true;
            _terrain = MapAssets.LoadTerrain();
            if (_terrain == null)
            {
                _terrain = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                _terrain.SetPixels32(new[] { DeepOcean, DeepOcean, DeepOcean, DeepOcean });
                _terrain.Apply();
            }

            _idTexture = CreateIdTexture();

            int lutHeight = (_world.ProvinceCount + 1 + LutWidth - 1) / LutWidth;
            _lut = new Texture2D(LutWidth, lutHeight, TextureFormat.RGBA32, false, false)
            {
                name = "ProvinceColours",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            _lutPixels = new Color32[LutWidth * lutHeight];

            _material = new Material(shader) { name = "WorldMap" };
            _material.SetTexture(TerrainTexId, _terrain);
            _material.SetTexture(ProvinceTexId, _idTexture);
            _material.SetTexture(LutTexId, _lut);
            _material.SetVector(ProvinceTexSizeId, new Vector4(_width, _height, 1f / _width, 1f / _height));
            _material.SetVector(LutSizeId, new Vector4(LutWidth, lutHeight, 1f / LutWidth, 1f / lutHeight));
            _material.SetFloat(SelectedIdId, 0);
            _material.SetFloat(HoverIdId, 0);

            _mesh = CreateQuad(WorldRect);
            gameObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var mr = gameObject.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.sortingOrder = 0;

            UpdateLut();
        }

        Texture2D CreateIdTexture()
        {
            Texture2D tex;
            if (SystemInfo.SupportsTextureFormat(TextureFormat.RG16))
            {
                // Little-endian ushort = low byte in R, high byte in G: exactly what the shader decodes.
                tex = new Texture2D(_width, _height, TextureFormat.RG16, false, true);
                tex.SetPixelData(_ids, 0);
            }
            else
            {
                tex = new Texture2D(_width, _height, TextureFormat.RGBA32, false, true);
                var px = new Color32[_ids.Length];
                for (int i = 0; i < px.Length; i++)
                    px[i] = new Color32((byte)(_ids[i] & 255), (byte)(_ids[i] >> 8), 0, 255);
                tex.SetPixels32(px);
            }
            tex.name = "ProvinceIds";
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.Apply(false, true);
            return tex;
        }

        static Mesh CreateQuad(Rect r)
        {
            var mesh = new Mesh { name = "WorldMapQuad" };
            mesh.vertices = new[]
            {
                new Vector3(r.xMin, r.yMin, 0), new Vector3(r.xMax, r.yMin, 0),
                new Vector3(r.xMin, r.yMax, 0), new Vector3(r.xMax, r.yMax, 0),
            };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            mesh.RecalculateBounds();
            return mesh;
        }

        void UpdateLut()
        {
            _dirty = false;
            ComputeProvinceColors();
            _lutPixels[0] = new Color32(0, 0, 0, WaterOwner);
            for (int id = 1; id < _fill.Length; id++)
            {
                var c = _fill[id];
                int owner = _ownerIndex[id];
                // Owner 0 = unowned land; country indexes wrap below the water marker.
                c.a = owner <= 0 ? (byte)0 : (byte)(1 + (owner - 1) % (WaterOwner - 1));
                _lutPixels[id] = c;
            }
            _lut.SetPixels32(_lutPixels);
            _lut.Apply(false);
            _material.SetFloat(TintStrengthId, Mode == MapMode.Political ? 0.78f : 0.9f);
        }

        void CleanUpGpu()
        {
            _gpu = false;
            var mr = GetComponent<MeshRenderer>();
            if (mr != null) Destroy(mr);
            var mf = GetComponent<MeshFilter>();
            if (mf != null) Destroy(mf);
            if (_material != null) Destroy(_material);
            if (_mesh != null) Destroy(_mesh);
            if (_idTexture != null) Destroy(_idTexture);
            if (_terrain != null) Destroy(_terrain);
            if (_lut != null) Destroy(_lut);
            _material = null;
        }

        // ------------------------------------------------------------------ CPU fallback path

        void InitializeCpu()
        {
            _gpu = false;
            BuildOceanShading();

            _pixels = new Color32[_width * _height];
            _texture = new Texture2D(_width, _height, TextureFormat.RGBA32, true, false)
            {
                name = "WorldMap",
                filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 4,
            };

            // On a child object so it never clashes with the GPU path's MeshRenderer.
            var cpuMap = new GameObject("Map (CPU)");
            cpuMap.transform.SetParent(transform, false);
            var sr = cpuMap.AddComponent<SpriteRenderer>();
            sr.sprite = Sprite.Create(_texture, new Rect(0, 0, _width, _height), new Vector2(0.5f, 0.5f),
                PixelsPerUnit, 0, SpriteMeshType.FullRect);
            sr.sortingOrder = 0;

            _selection = ProvinceHighlight.Create(transform, "Selection", 20, new Color32(255, 255, 255, 70), new Color32(255, 244, 200, 255));
            _hover = ProvinceHighlight.Create(transform, "Hover", 10, new Color32(255, 255, 255, 35), new Color32(255, 255, 255, 140));

            Redraw();
        }

        void OnDestroy()
        {
            foreach (var s in _subscriptions)
                s.Dispose();
            _subscriptions.Clear();
            if (_texture != null)
                Destroy(_texture);
            CleanUpGpu();
        }

        void LateUpdate()
        {
            if (!_dirty)
                return;
            if (_gpu)
                UpdateLut();
            else
                Redraw();
        }

        public void SetMode(MapMode mode)
        {
            if (mode == Mode)
                return;
            Mode = mode;
            if (_gpu)
                UpdateLut();
            else
                Redraw();
            ModeChanged?.Invoke(mode);
        }

        /// <summary>Province under a world position, 0 for ocean or off-map.</summary>
        public int ProvinceAt(Vector3 worldPosition)
        {
            if (_ids == null)
                return 0;
            int x = Mathf.FloorToInt(worldPosition.x * PixelsPerUnit + _width / 2f);
            int y = Mathf.FloorToInt(worldPosition.y * PixelsPerUnit + _height / 2f);
            if (x < 0 || y < 0 || x >= _width || y >= _height)
                return 0;
            return _ids[y * _width + x];
        }

        public Vector3 PixelToWorld(float x, float y) =>
            new Vector3((x + 0.5f - _width / 2f) / PixelsPerUnit, (y + 0.5f - _height / 2f) / PixelsPerUnit, 0f);

        public Vector3 ProvinceCenter(int provinceId)
        {
            var p = _world.GetProvince(provinceId);
            return p == null ? Vector3.zero : PixelToWorld(p.CenterX, p.CenterY);
        }

        public void SetSelected(int provinceId)
        {
            if (_gpu)
            {
                _material.SetFloat(SelectedIdId, provinceId);
            }
            else
            {
                _selection.Show(this, provinceId);
            }
        }

        public void SetHover(int provinceId)
        {
            if (_gpu)
            {
                _material.SetFloat(HoverIdId, provinceId);
            }
            else
            {
                _hover.Show(this, provinceId);
            }
        }


        // ------------------------------------------------------------------ drawing

        void Redraw()
        {
            _dirty = false;
            ComputeProvinceColors();

            int w = _width;
            int h = _height;
            var ids = _ids;
            var px = _pixels;
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                for (int x = 0; x < w; x++)
                {
                    int i = row + x;
                    int id = ids[i];
                    if (id == 0)
                    {
                        px[i] = _oceanPalette[_oceanShade[i]];
                        continue;
                    }

                    int right = x + 1 < w ? ids[i + 1] : id;
                    int up = y + 1 < h ? ids[i + w] : id;
                    int left = x > 0 ? ids[i - 1] : id;
                    int down = y > 0 ? ids[i - w] : id;

                    if (right == id && up == id && left == id && down == id)
                    {
                        px[i] = _fill[id];
                        continue;
                    }

                    int owner = _ownerIndex[id];
                    if (_ownerIndex[right] != owner || _ownerIndex[up] != owner ||
                        _ownerIndex[left] != owner || _ownerIndex[down] != owner)
                        px[i] = _countryEdge[id];
                    else if (right != id || up != id)
                        px[i] = _provinceEdge[id];
                    else
                        px[i] = _fill[id];
                }
            }

            _texture.SetPixels32(px);
            _texture.Apply(true);
        }

        void ComputeProvinceColors()
        {
            var countryIndex = new Dictionary<string, int>();
            for (int c = 0; c < _world.Countries.Count; c++)
                countryIndex[_world.Countries[c].Tag] = c + 1;

            // Index 0 is the ocean; unowned land gets its own index so coastlines and borders show.
            _ownerIndex[0] = -1;
            double maxDensity = 1, maxWealth = 1, minWealth = double.MaxValue;
            foreach (var p in _world.Provinces)
            {
                _ownerIndex[p.Id] = p.OwnerTag != null && countryIndex.TryGetValue(p.OwnerTag, out int idx) ? idx : 0;
                maxDensity = Math.Max(maxDensity, Density(p));
                if (p.Population > 0)
                {
                    double wpc = WealthPerCapita(p);
                    maxWealth = Math.Max(maxWealth, wpc);
                    minWealth = Math.Min(minWealth, wpc);
                }
            }
            if (minWealth == double.MaxValue)
                minWealth = 1;

            foreach (var p in _world.Provinces)
            {
                Color32 fill;
                switch (Mode)
                {
                    case MapMode.Population:
                        fill = Gradient(Math.Log(1 + Density(p)) / Math.Log(1 + maxDensity), PopulationRamp);
                        break;
                    case MapMode.Wealth:
                        fill = p.Population <= 0
                            ? UnownedColor
                            : Gradient((Math.Log(WealthPerCapita(p)) - Math.Log(minWealth)) / Math.Max(1e-6, Math.Log(maxWealth) - Math.Log(minWealth)), WealthRamp);
                        break;
                    default:
                        var owner = _world.GetCountry(p.OwnerTag);
                        fill = owner == null ? UnownedColor : Rgb(owner.ColorRgb);
                        break;
                }
                _fill[p.Id] = fill;
                _provinceEdge[p.Id] = Scale(fill, 0.80f);
                _countryEdge[p.Id] = Scale(fill, 0.38f);
            }
        }

        static double Density(Province p) => p.Pixels > 0 ? (double)p.Population / p.Pixels : 0;
        static double WealthPerCapita(Province p) => p.Population > 0 ? Math.Max(1.0, p.GdpMillions * 1e6 / p.Population) : 1.0;

        static readonly Color32[] PopulationRamp =
        {
            new Color32(38, 30, 66, 255), new Color32(84, 44, 110, 255), new Color32(160, 60, 110, 255),
            new Color32(228, 110, 70, 255), new Color32(250, 200, 90, 255),
        };

        static readonly Color32[] WealthRamp =
        {
            new Color32(150, 40, 40, 255), new Color32(210, 120, 60, 255), new Color32(225, 205, 95, 255),
            new Color32(120, 190, 90, 255), new Color32(40, 140, 90, 255),
        };

        static Color32 Gradient(double t, Color32[] ramp)
        {
            t = Math.Max(0, Math.Min(1, t)) * (ramp.Length - 1);
            int i = Math.Min(ramp.Length - 2, (int)t);
            return Color32.Lerp(ramp[i], ramp[i + 1], (float)(t - i));
        }

        public static Color32 Rgb(int rgb) => new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);

        static Color32 Scale(Color32 c, float f) =>
            new Color32((byte)(c.r * f), (byte)(c.g * f), (byte)(c.b * f), 255);

        /// <summary>Precomputes a coastal glow: ocean pixels near land are lighter.</summary>
        void BuildOceanShading()
        {
            int w = _width, h = _height;
            var dist = new byte[w * h];
            const byte far = OceanShades - 1;
            for (int i = 0; i < dist.Length; i++)
                dist[i] = _ids[i] != 0 ? (byte)0 : far;

            // Two-pass chamfer distance transform, capped at OceanShades - 1 pixels.
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    int d = dist[i];
                    if (x > 0) d = Math.Min(d, dist[i - 1] + 1);
                    if (y > 0) d = Math.Min(d, dist[i - w] + 1);
                    dist[i] = (byte)d;
                }
            for (int y = h - 1; y >= 0; y--)
                for (int x = w - 1; x >= 0; x--)
                {
                    int i = y * w + x;
                    int d = dist[i];
                    if (x < w - 1) d = Math.Min(d, dist[i + 1] + 1);
                    if (y < h - 1) d = Math.Min(d, dist[i + w] + 1);
                    dist[i] = (byte)d;
                }

            _oceanShade = dist;
            _oceanPalette = new Color32[OceanShades];
            for (int d = 0; d < OceanShades; d++)
            {
                float t = Mathf.Pow(d / (float)(OceanShades - 1), 0.6f);
                _oceanPalette[d] = Color32.Lerp(ShallowOcean, DeepOcean, t);
            }
        }

        // ------------------------------------------------------------------ highlights

        /// <summary>A sprite that covers one province's bounding box and tints just that province.</summary>
        sealed class ProvinceHighlight
        {
            const int Margin = 1;

            SpriteRenderer _renderer;
            Texture2D _texture;
            Color32 _fill;
            Color32 _edge;
            int _provinceId;

            public static ProvinceHighlight Create(Transform parent, string name, int order, Color32 fill, Color32 edge)
            {
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                var h = new ProvinceHighlight
                {
                    _renderer = go.AddComponent<SpriteRenderer>(),
                    _fill = fill,
                    _edge = edge,
                };
                h._renderer.sortingOrder = order;
                go.SetActive(false);
                return h;
            }

            public void Show(MapView map, int provinceId)
            {
                if (provinceId == _provinceId)
                    return;
                _provinceId = provinceId;

                if (_texture != null)
                {
                    Destroy(_renderer.sprite);
                    Destroy(_texture);
                    _texture = null;
                }
                if (provinceId <= 0 || provinceId >= map._assets.ProvinceBounds.Length)
                {
                    _renderer.gameObject.SetActive(false);
                    return;
                }

                var b = map._assets.ProvinceBounds[provinceId];
                int x0 = Math.Max(0, b.xMin - Margin), y0 = Math.Max(0, b.yMin - Margin);
                int x1 = Math.Min(map._width, b.xMax + Margin), y1 = Math.Min(map._height, b.yMax + Margin);
                int tw = x1 - x0, th = y1 - y0;
                if (tw <= 0 || th <= 0)
                {
                    _renderer.gameObject.SetActive(false);
                    return;
                }

                var colors = new Color32[tw * th];
                var ids = map._ids;
                int w = map._width, h = map._height;
                for (int y = 0; y < th; y++)
                    for (int x = 0; x < tw; x++)
                    {
                        int mx = x0 + x, my = y0 + y;
                        int i = my * w + mx;
                        if (ids[i] != provinceId)
                            continue;
                        bool edge = mx == 0 || my == 0 || mx == w - 1 || my == h - 1 ||
                                    ids[i - 1] != provinceId || ids[i + 1] != provinceId ||
                                    ids[i - w] != provinceId || ids[i + w] != provinceId;
                        colors[y * tw + x] = edge ? _edge : _fill;
                    }

                _texture = new Texture2D(tw, th, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                };
                _texture.SetPixels32(colors);
                _texture.Apply(false);
                _renderer.sprite = Sprite.Create(_texture, new Rect(0, 0, tw, th), Vector2.zero, PixelsPerUnit, 0, SpriteMeshType.FullRect);
                _renderer.transform.localPosition = new Vector3((x0 - w / 2f) / PixelsPerUnit, (y0 - h / 2f) / PixelsPerUnit, 0f);
                _renderer.gameObject.SetActive(true);
            }
        }
    }
}
