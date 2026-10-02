// Clearwater: the clouds, drawn into a dome the sky, the water and the beach read with one fetch (ClearwaterCommon:
// cwCloudDome). A layer of fair-weather cumulus 1.5 to 2.4 km up, as a volume: a weather map says where clouds are and
// how tall they grow; a 3D noise gives their big rounded lobes (Perlin-Worley) and a finer one frays their edges into
// wisps. Each texel marches its direction through the layer from the eye, lit by the sun (a few steps toward it, two
// octaves of scattering: the second, the light scattered many times, softer and much less forward) and by the sky.
// The dome: u = azimuth (JS space, atan2(x, z) / 2pi), rows 0 .. H-2 = the elevation's square root (more of them low
// down, where the clouds are far and small); the top row is bookkeeping (texel 0: which strip is next).
// Double-buffered: each update draws one of Slices strips of columns again and copies the rest (the whole dome on the
// first), so a full turn of the sky takes Slices frames. Its material gets the sky's clouds (ClearwaterController).
Shader "Clearwater/CRT/CloudDome"
{
    Properties
    {
        _CloudCover ("Cover (copied from the sky)", Range(0, 1)) = 0
        _CloudSize ("Size (m)", Float) = 900
        _CloudSpeed ("Drift (m/s)", Float) = 8
        _CloudDir ("Drift direction (deg)", Float) = 60
        _CloudShift ("Drift so far (m)", Float) = 0
        _SunDir ("Sun direction (world, towards the sun; the fixed sky's)", Vector) = (0, 0.5, 0.86, 0)
        [NoScaleOffset] _CloudShape ("The big shape (the package's)", 3D) = "black" {}
        [NoScaleOffset] _CloudDetail ("The fine detail (the package's)", 3D) = "black" {}
        [NoScaleOffset] _CloudWeather ("Where they are (the package's)", 2D) = "black" {}
    }
    SubShader
    {
        Lighting Off Blend Off ZTest Always ZWrite Off Cull Off
        Pass
        {
            CGPROGRAM
            #include "UnityCustomRenderTexture.cginc"
            #include "ClearwaterCommon.cginc"
            #pragma vertex CustomRenderTextureVertexShader
            #pragma fragment frag
            #pragma target 5.0

            sampler3D _CloudShape, _CloudDetail;

            #define SLICES 64
            #define CLOUD_TOP (CW_CLOUD_H + 900.0)
            #define EYE 2.0

            float heightFrac(float y) { return saturate((y - CW_CLOUD_H) / (CLOUD_TOP - CW_CLOUD_H)); }

            // the density at p (m, JS space, from the eye's foot); cheap: the big shape only (for the light toward the sun)
            float density(float3 p, float2 wind, bool cheap)
            {
                float h = heightFrac(p.y);
                float size = max(_CloudSize, 50.0);
                float4 w = _CloudWeather.SampleLevel(cw_trilinear_repeat_sampler, (p.xz - wind) / (size * 12.0), 0);
                // where the weather lets a cloud be (a share of the sky as large as the cover), and how strongly
                float cl = saturate((w.r - (1.0 - _CloudCover)) * 4.0);
                if (cl <= 0.0) return 0.0;
                // cumulus: a flat, sharp base; the stronger ones heaped higher; rounded toward the top
                float topH = lerp(0.35, 1.0, cl * lerp(0.6, 1.0, w.g));
                float grad = smoothstep(0.0, 0.025, h) * (1.0 - smoothstep(topH * 0.55, topH, h));
                if (grad <= 0.0) return 0.0;
                float3 wind3 = float3(wind.x, 0.0, wind.y);
                float4 s = tex3Dlod(_CloudShape, float4((p - wind3) / (size * 1.8), 0.0));
                float shape = s.r * 0.75 + (s.g * 0.7 + s.b * 0.3) * 0.25; // (big rounded lobes; the fine detail is the edges' only)
                shape = lerp(shape, 1.0, 0.25 * cl * (1.0 - smoothstep(0.0, 0.35, h))); // (the lower part filled out: one wide, flat base)
                // even a weak place holds the cores of small clouds; a strong one is filled
                float thr = 0.5 - 0.42 * cl;
                float d = saturate((shape * grad - thr) / (1.0 - thr));
                if (cheap) return d * 0.8;
                if (d <= 0.0) return 0.0;
                // the edges frayed into wisps by the fine detail (the gaps between its cells), less so right at the base;
                // the detail drifts a little faster than the shape and rises, so the edges keep changing
                float3 dp = (p - wind3 * 1.3 - float3(0.0, _Time.y * 3.0, 0.0)) / (size * 0.2);
                float3 dn = tex3Dlod(_CloudDetail, float4(dp, 0.0)).rgb;
                float er = (1.0 - (dn.r * 0.625 + dn.g * 0.25 + dn.b * 0.125)) * lerp(0.35, 1.0, smoothstep(0.0, 0.15, h));
                float e = er * (1.0 - d); // (the edges eroded, the cores kept)
                return saturate((d - e) / (1.0 - e));
            }

            float phase1(float mu) { return lerp(cwPhaseHG(mu, 0.8), cwPhaseHG(mu, -0.3), 0.35); }
            float phase2(float mu) { return lerp(cwPhaseHG(mu, 0.35), cwPhaseHG(mu, -0.15), 0.35); } // (the light scattered many times)

            // the clouds along d (JS space, d.y > 0) from the eye: rgb their light (premultiplied), a how much they hide
            float4 march(float3 d)
            {
                float3 ro = float3(0.0, EYE, 0.0);
                float t0 = (CW_CLOUD_H - EYE) / d.y, t1 = min((CLOUD_TOP - EYE) / d.y, t0 + 30000.0);
                if (t0 > 100000.0) return 0.0; // (past 100 km, under the horizon's haze: none)
                // steps of 40 to 250 m (longer far off), as many as it takes, up to 256
                float dt = clamp((t1 - t0) / 64.0, 60.0, 375.0);
                int n = (int)min(ceil((t1 - t0) / dt), 256.0);
                // the light on them: the sun's (it lights them from its side after it has set for the ground; else the
                // moon's), or the fixed sky's; the sky's from above and round about
                float3 L = normalize(cwToJS(_SunDir.xyz)), sunC = float3(1.0, 0.86, 0.66) * 6.0;
                [branch] if (CW_TOD)
                {
                    L = normalize(cwToJS(_Udon_CWCloudLight.w > 0.5 ? _Udon_CWSun.xyz : _Udon_CWKey.xyz));
                    sunC = _Udon_CWCloudLight.rgb;
                }
                float3 amb = (cwClearSky(float3(0.0, 1.0, 0.0), L) * 1.1 + cwClearSky(normalize(float3(d.x, 0.2, d.z)), L) * 0.4) * 0.85;
                float mu = dot(d, L), ph1 = phase1(mu), ph2 = phase2(mu);
                float size = max(_CloudSize, 50.0);
                float sigma = 0.02 * 900.0 / size; // extinction per m at density 1 (scaled with the cloud size)
                float a = radians(_CloudDir);
                float2 wind = float2(sin(a), -cos(a)) * (_CloudSpeed * _Time.y + _CloudShift);
                float jitter = frac(sin(dot(d.xz, float2(12.9898, 78.233))) * 43758.5453);
                float T = 1.0;
                float3 C = 0.0;
                [loop] for (int i = 0; i < 256; i++)
                {
                    if (i >= n || T < 0.01) break;
                    float3 p = ro + d * (t0 + (i + jitter) * dt);
                    float den = density(p, wind, false);
                    [branch] if (den > 0.001)
                    {
                        // the cloud toward the sun, by its big shape (shaded by the lobes, never marbled by the detail)
                        float tau = 0.0, ls = 60.0;
                        [loop] for (int j = 0; j < 6; j++)
                        {
                            tau += density(p + L * ls * (j + 0.5), wind, true) * ls;
                            ls *= 1.6;
                        }
                        tau *= sigma * 4.0; // (deeper than the cloud seen through: a see-through cloud still shades itself)
                        float powder = 1.0 - exp(-den * sigma * 200.0);
                        float h = heightFrac(p.y);
                        float3 S = sunC * (exp(-tau) * ph1 + 0.6 * exp(-tau * 0.25) * ph2) * lerp(1.0, powder, 0.6) * 10.0
                                 + amb * lerp(0.65, 1.0, h);
                        float at = exp(-den * sigma * dt);
                        C += T * S * (1.0 - at);
                        T *= at;
                    }
                }
                return float4(C, 1.0 - T);
            }

            float4 frag(v2f_customrendertexture IN) : SV_Target
            {
                float W = _CustomRenderTextureWidth, H = _CustomRenderTextureHeight;
                float2 uv = IN.localTexcoord.xy;
                uint2 px = uint2(uv * float2(W, H));
                float count = tex2Dlod(_SelfTexture2D, float4(0.5 / W, (H - 0.5) / H, 0.0, 0.0)).r;
                // the bookkeeping row
                // (1 .. Slices round and round, 0 only before the first: a half float holds no big counts)
                if (px.y >= (uint)H - 1) return px.x == 0 ? float4(fmod(count, SLICES) + 1.0, 0.0, 0.0, 0.0) : 0.0;
                // only this update's strip (all of it the first time)
                uint slice = (uint)fmod(count, SLICES);
                bool mine = count < 0.5 || (px.x * SLICES) / (uint)W == slice;
                if (!mine) return tex2Dlod(_SelfTexture2D, float4(uv, 0.0, 0.0));
                if (_CloudCover <= 0.0) return 0.0;
                float az = uv.x * 2.0 * CW_PI;
                float s = min(uv.y * H / (H - 1.0), 1.0);
                float el = s * s * 0.5 * CW_PI;
                float3 d = float3(cos(el) * sin(az), sin(el), cos(el) * cos(az));
                return march(normalize(d + float3(0.0, 1e-4, 0.0)));
            }
            ENDCG
        }
    }
}
