// Clearwater: the clouds, drawn into a dome the sky, the water and the beach read with one fetch (ClearwaterCommon:
// cwCloudDome). A layer of fair-weather cumulus 1.5 to 2.4 km up, as a volume: a weather map says where clouds are and
// how tall they grow; a 3D noise gives their big rounded lobes (Perlin-Worley) and a finer one frays their edges into
// wisps. Each texel marches its direction through the layer from the eye, lit by the sun (a few steps toward it, two
// octaves of scattering: the second, the light scattered many times, softer and much less forward) and by the sky. It
// keeps how much of each light the clouds pass on (r the sun's, g the sky's) rather than their colour: the readers
// multiply by the lights of the moment (cwCloudLit), which change at once when the day goes by fast.
// The dome: u = azimuth (JS space, atan2(x, z) / 2pi), rows 0 .. H-2 = the elevation's square root (more of them low
// down, where the clouds are far and small); the top row is bookkeeping (texel 0: r the clock's window at the last
// update, mod CW_DOME_WRAP; g 1 once the whole dome is drawn).
// Double-buffered: the strips of columns take turns by the clock (ClearwaterCommon: cwDomeTurn), each update drawing
// again those whose turn came since the last and copying the rest, so a full turn of the sky takes CW_DOME_STRIPS *
// CW_DOME_STRIP_TIME seconds whatever the frame rate. Each strip is drawn as the clouds were at its turn: the readers
// move it on by the wind since then, so the clouds drift smoothly between turns. Its material gets the sky's clouds
// (ClearwaterController).
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

            #define CLOUD_TOP (CW_CLOUD_H + 900.0)
            #define EYE 2.0

            float heightFrac(float y) { return saturate((y - CW_CLOUD_H) / (CLOUD_TOP - CW_CLOUD_H)); }

            // the density at p (m, JS space, from the eye's foot); cheap: the big shape only (for the light toward the sun);
            // slow: the weather's slow fields there (cwCloudSlow)
            float density(float3 p, float2 wind, float time, bool cheap, float2 slow)
            {
                float h = heightFrac(p.y);
                float size = max(_CloudSize, 50.0);
                // where the weather lets a cloud be, and how strongly (x) and how tall it may grow (y)
                float2 w = cwCloudWeather(p.xz - wind, 0.0, slow);
                float cl = w.x;
                if (cl <= 0.0) return 0.0;
                // cumulus: a flat, sharp base; the stronger ones heaped higher; rounded toward the top
                float topH = lerp(0.35, 1.0, cl * lerp(0.6, 1.0, w.y));
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
                // the detail drifts with the shape and rises slowly, so the edges change little by little (faster, they
                // changed visibly at each strip's turn)
                float3 dp = (p - wind3 - float3(0.0, time * 0.7, 0.0)) / (size * 0.2);
                float3 dn = tex3Dlod(_CloudDetail, float4(dp, 0.0)).rgb;
                float er = (1.0 - (dn.r * 0.625 + dn.g * 0.25 + dn.b * 0.125)) * lerp(0.35, 1.0, smoothstep(0.0, 0.15, h));
                float e = er * (1.0 - d); // (the edges eroded, the cores kept)
                return saturate((d - e) / (1.0 - e));
            }

            float phase1(float mu) { return lerp(cwPhaseHG(mu, 0.8), cwPhaseHG(mu, -0.3), 0.35); }
            float phase2(float mu) { return lerp(cwPhaseHG(mu, 0.35), cwPhaseHG(mu, -0.15), 0.35); } // (the light scattered many times)

            // the clouds along d (JS space, d.y > 0) from the eye as they are at time: r how much sunlight they pass on, g how
            // much skylight (each premultiplied), a how much they hide
            float4 march(float3 d, float time)
            {
                float3 ro = float3(0.0, EYE, 0.0);
                float t0 = (CW_CLOUD_H - EYE) / d.y, t1 = min((CLOUD_TOP - EYE) / d.y, t0 + 30000.0);
                if (t0 > 100000.0) return 0.0; // (past 100 km, under the horizon's haze: none)
                // 64 steps across the layer, whatever its length: 14 m overhead to 470 m far off (a GPU waits for its
                // longest ray anyway. With steps of 60 m at least, the near clouds overhead were grainy, a scaly speckle
                // that showed most against the light; and a count of steps that changed with the elevation drew arcs
                // across the clouds where it changed)
                float dt = (t1 - t0) / 64.0;
                const int n = 64;
                float3 L = cwCloudSunDir();
                float mu = dot(d, L), ph1 = phase1(mu), ph2 = phase2(mu);
                float size = max(_CloudSize, 50.0);
                float sigma = 0.02 * 900.0 / size; // extinction per m at density 1 (scaled with the cloud size)
                float2 wind = cwCloudWindAt(time);
                // a low sun's light crosses the layer side on, through the clouds' tops and their neighbours: shaded as
                // deep as overhead, the frayed tops at sunset went grey, dirty smudges on the lit clouds. So the lower the
                // sun, the less the clouds shade themselves and the more of the light scattered many times gets through
                float hi = smoothstep(0.05, 0.3, L.y);
                float tauK = sigma * lerp(2.0, 4.0, hi), msK = lerp(1.0, 0.6, hi), msT = lerp(0.15, 0.25, hi);
                // (no jitter of the steps: a texel's own random start speckled the clouds with a fine grain, a sponge
                // that showed most on the low sun's lit faces. The 64 steps leave no visible slices)
                float T = 1.0;
                float2 C = 0.0;
                [loop] for (int i = 0; i < n; i++)
                {
                    if (T < 0.01) break;
                    float3 p = ro + d * (t0 + (i + 0.5) * dt);
                    float2 slow = cwCloudSlow(p.xz - wind, 0.0);
                    float den = density(p, wind, time, false, slow);
                    [branch] if (den > 0.001)
                    {
                        // the cloud toward the sun, by its big shape (shaded by the lobes, never marbled by the detail)
                        float tau = 0.0, ls = 60.0;
                        [loop] for (int j = 0; j < 6; j++)
                        {
                            tau += density(p + L * ls * (j + 0.5), wind, time, true, slow) * ls;
                            ls *= 1.6;
                        }
                        tau *= tauK; // (deeper than the cloud seen through, x4 overhead: a see-through cloud still shades itself)
                        float powder = 1.0 - exp(-den * sigma * 200.0);
                        float h = heightFrac(p.y);
                        float2 S = float2((exp(-tau) * ph1 + msK * exp(-tau * msT) * ph2) * lerp(1.0, powder, 0.6) * 10.0,
                                          lerp(0.65, 1.0, h));
                        float at = exp(-den * sigma * dt);
                        C += T * S * (1.0 - at);
                        T *= at;
                    }
                }
                return float4(C, 0.0, 1.0 - T);
            }

            float4 frag(v2f_customrendertexture IN) : SV_Target
            {
                float W = _CustomRenderTextureWidth, H = _CustomRenderTextureHeight;
                float2 uv = IN.localTexcoord.xy;
                uint2 px = uint2(uv * float2(W, H));
                float2 last = tex2Dlod(_SelfTexture2D, float4(0.5 / W, (H - 0.5) / H, 0.0, 0.0)).rg;
                float w = cwDomeWindow();
                if (px.y >= (uint)H - 1) return px.x == 0 ? float4(cwModPos(w, CW_DOME_WRAP), 1.0, 0.0, 0.0) : 0.0; // (the bookkeeping row)
                // only the strips whose turn came since the last update (all of the dome the first time, or after a pause)
                float since = last.g > 0.5 ? cwModPos(w - last.r, CW_DOME_WRAP) : CW_DOME_WRAP;
                float turn = cwDomeTurn(uv.x, w);
                if (w - turn >= since) return tex2Dlod(_SelfTexture2D, float4(uv, 0.0, 0.0));
                if (_CloudCover <= 0.0) return 0.0;
                float az = uv.x * 2.0 * CW_PI;
                float s = min(uv.y * H / (H - 1.0), 1.0);
                float el = s * s * 0.5 * CW_PI;
                float3 d = float3(cos(el) * sin(az), sin(el), cos(el) * cos(az));
                return march(normalize(d + float3(0.0, 1e-4, 0.0)), _Time.y - cwDomeSince(turn));
            }
            ENDCG
        }
    }
}
