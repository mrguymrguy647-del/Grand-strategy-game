// World map: terrain + political colours + smooth, anti-aliased province and country borders.
//
// _ProvinceTex holds a province id per texel (id = r + g * 256, point sampled).
// _LutTex holds one texel per province: rgb = map-mode colour, a = owner index / 255
// (0 = unowned land, 255 = water). Borders are found by comparing neighbouring ids over a
// 4x4 texel footprint with a tent filter, which turns the blocky pixel map into smooth lines.
// All colour maths happens in gamma space so the map looks the same in Gamma and Linear projects.
Shader "GrandStrategy/WorldMap"
{
    Properties
    {
        _TerrainTex ("Terrain", 2D) = "gray" {}
        _ProvinceTex ("Province ids", 2D) = "black" {}
        _LutTex ("Province colours", 2D) = "black" {}
        _ProvinceTexSize ("Province map size (w, h, 1/w, 1/h)", Vector) = (4096, 2048, 0.000244, 0.000488)
        _LutSize ("Lookup size (w, h, 1/w, 1/h)", Vector) = (256, 16, 0.0039, 0.0625)
        _SelectedId ("Selected province", Float) = 0
        _HoverId ("Hovered province", Float) = 0
        _TintStrength ("Colour strength", Range(0, 1)) = 0.78
        _SmoothRadius ("Border smoothing (texels)", Range(1, 2)) = 1.5
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "IgnoreProjector" = "True" }
        LOD 100

        Pass
        {
            Cull Off
            ZWrite Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"

            sampler2D _TerrainTex;
            sampler2D _ProvinceTex;
            sampler2D _LutTex;
            float4 _ProvinceTexSize;
            float4 _LutSize;
            float _SelectedId;
            float _HoverId;
            float _TintStrength;
            float _SmoothRadius;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float ProvinceAt(float2 texel)
            {
                float2 uv = (texel + 0.5) * _ProvinceTexSize.zw;
                float4 c = tex2Dlod(_ProvinceTex, float4(uv, 0, 0));
                return round(c.r * 255.0) + round(c.g * 255.0) * 256.0;
            }

            float4 Lookup(float id)
            {
                float x = fmod(id, _LutSize.x);
                float y = floor(id / _LutSize.x);
                return tex2Dlod(_LutTex, float4((float2(x, y) + 0.5) * _LutSize.zw, 0, 0));
            }

            float3 ToGamma(float3 c)
            {
            #ifdef UNITY_COLORSPACE_GAMMA
                return c;
            #else
                return LinearToGammaSpace(c);
            #endif
            }

            float3 FromGamma(float3 c)
            {
            #ifdef UNITY_COLORSPACE_GAMMA
                return c;
            #else
                return GammaToLinearSpace(c);
            #endif
            }

            float Line(float width, float dist)
            {
                return 1.0 - smoothstep(width - 0.75, width + 0.75, dist);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 terrain = ToGamma(tex2D(_TerrainTex, i.uv).rgb);

                float2 p = i.uv * _ProvinceTexSize.xy - 0.5;
                float2 ip = floor(p);
                float2 fp = p - ip;
                float texelsPerPixel = max(fwidth(p.x), 1e-5);
                float pixelsPerTexel = 1.0 / texelsPerPixel;

                float ids[16];
                float weights[16];
                float total = 0;
                bool allSame = true;
                [unroll] for (int oy = 0; oy < 4; oy++)
                {
                    [unroll] for (int ox = 0; ox < 4; ox++)
                    {
                        int k = oy * 4 + ox;
                        float2 o = float2(ox - 1, oy - 1);
                        ids[k] = ProvinceAt(ip + o);
                        float2 w = max(0, 1 - abs(o - fp) / _SmoothRadius);
                        weights[k] = w.x * w.y;
                        total += weights[k];
                        allSame = allSame && ids[k] == ids[0];
                    }
                }

                float win = ids[5];
                float run = ids[5];
                float f = 1.0;
                if (!allSame)
                {
                    // Tent-filtered majority: the province with the largest share wins this pixel,
                    // the next largest is the neighbour across the nearest border.
                    float scores[16];
                    float best = -1;
                    [unroll] for (int a = 0; a < 16; a++)
                    {
                        float s = 0;
                        [unroll] for (int b = 0; b < 16; b++)
                            s += ids[b] == ids[a] ? weights[b] : 0;
                        scores[a] = s / total;
                        if (scores[a] > best) { best = scores[a]; win = ids[a]; }
                    }
                    float second = -1;
                    [unroll] for (int c = 0; c < 16; c++)
                    {
                        if (ids[c] != win && scores[c] > second) { second = scores[c]; run = ids[c]; }
                    }
                    f = best;
                }

                // Distance to the border in screen pixels. Derivatives are taken outside the branch.
                float fw = max(fwidth(f), 1e-4);
                float dist = max(f - 0.5, 0) / fw;

                float4 lutWin = Lookup(win);
                float4 lutRun = Lookup(run);
                float ownerWin = round(lutWin.a * 255.0);
                float ownerRun = round(lutRun.a * 255.0);
                bool isLand = win > 0.5;

                float3 col = terrain;
                if (isLand)
                {
                    float lum = dot(terrain, float3(0.3, 0.59, 0.11));
                    float shade = clamp(lum / 0.62, 0.35, 1.15);
                    float3 tinted = ToGamma(lutWin.rgb) * (0.35 + 0.65 * shade);
                    float strength = ownerWin < 0.5 ? 0.25 : _TintStrength;
                    col = lerp(terrain, tinted, strength);
                    if (abs(win - _HoverId) < 0.5) col = col * 1.15 + 0.03;
                    if (abs(win - _SelectedId) < 0.5) col += (1 - col) * 0.22;
                }

                bool hasBorder = abs(run - win) > 0.5;
                bool coast = hasBorder && (win < 0.5 || run < 0.5);
                bool country = hasBorder && !coast && abs(ownerWin - ownerRun) > 0.5;
                bool province = hasBorder && !coast && !country;
                float zoom = saturate(pixelsPerTexel / 4.0);

                if (province)
                {
                    float fade = smoothstep(0.45, 1.2, pixelsPerTexel);
                    col *= 1 - Line(0.55 + 0.25 * zoom, dist) * 0.3 * fade;
                }
                if (country)
                {
                    float width = 1.1 + 0.9 * zoom;
                    col *= 1 - (1 - smoothstep(width, width + 5, dist)) * 0.25; // soft inner shadow
                    col = lerp(col, float3(0.06, 0.05, 0.05), Line(width, dist) * 0.92);
                }
                if (coast)
                    col = lerp(col, float3(0.08, 0.13, 0.18), Line(0.8 + 0.4 * zoom, dist) * 0.55);

                if (_SelectedId > 0.5 && hasBorder && (abs(win - _SelectedId) < 0.5 || abs(run - _SelectedId) < 0.5))
                    col = lerp(col, float3(1.0, 0.95, 0.75), Line(1.6, dist));

                return fixed4(FromGamma(saturate(col)), 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
