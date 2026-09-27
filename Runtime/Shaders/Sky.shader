// Clearwater: the demo's sky (gradient, sun halo, distant pine-and-limestone headland) as a Unity skybox,
// so the world sky matches what the water reflects.
Shader "Clearwater/Skybox"
{
    Properties
    {
        _SunDir ("Sun direction (world, towards sun)", Vector) = (0.054, 0.515, 0.855, 0)
        _SunIntensity ("Sun intensity", Float) = 6
        [Header(Clouds (the water and the seabed get copies of these))]
        _CloudCover ("Cover (0 = no clouds)", Range(0, 1)) = 0
        _CloudSize ("Size (m across a cloud)", Float) = 900
        _CloudSpeed ("Drift speed (m/s, 0 = still)", Float) = 8
        _CloudDir ("Drift direction (degrees clockwise from +Z)", Range(0, 360)) = 60
        [Toggle(_CW_TONEMAP)] _Tonemap ("Tone map in shader", Float) = 1
        _Exposure ("Exposure", Float) = 0.63
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #pragma multi_compile_instancing
            #pragma shader_feature_local _CW_TONEMAP
            #include "UnityCG.cginc"
            #include "ClearwaterCommon.cginc"

            float _SunIntensity;

            // the sun's disc round direction b (JS space), a degree across (as the fixed sky draws it)
            inline float cwDisc(float3 rd, float3 b) { return smoothstep(0.99996, 0.999985, dot(rd, b)); }

            // the full moon: a disc 0.6 degree across, the dark maria over the bright highlands fixed on it
            float3 cwMoonDisc(float3 rd, float3 m)
            {
                float c = dot(rd, m);
                if (c < 0.99998) return 0;
                float3 t1 = normalize(cross(m, float3(0.0, 1.0, 0.0)) + float3(1e-5, 0.0, 0.0)), t2 = cross(t1, m);
                float2 uv = float2(dot(rd, t1), dot(rd, t2)) / 0.0052; // (the disc radius, radians)
                // (the maria: the lower half of a noise, one side of the disc darker, as the moon's near side is)
                float maria = smoothstep(0.02, 0.12, 0.56 - cwFbm2(uv * 2.2 + float2(3.1, 7.3)) + 0.12 * (uv.x - uv.y));
                return smoothstep(1.0, 0.9, length(uv)) * lerp(1.0, 0.45, maria);
            }

            // the stars (and the band of the Milky Way) where they are on the turning sky: d (JS space) turned back
            // about the celestial pole by the angle the sky has turned, then one star at most in each cell of a cube
            // round it, drawn about a pixel across whatever the headset's resolution (pa: a pixel's angle, from outside
            // the branch it is drawn in)
            float3 cwStars(float3 d, float pa)
            {
                float3 p = normalize(cwToJS(_Udon_CWStars.xyz));
                float ca = cos(_Udon_CWStars.w), sa = sin(_Udon_CWStars.w);
                float3 q = d * ca + cross(p, d) * sa + p * dot(p, d) * (1.0 - ca);
                float3 a = abs(q);
                float2 uv; float face;
                if (a.x >= a.y && a.x >= a.z) { uv = q.yz / a.x; face = q.x > 0.0 ? 0.0 : 1.0; }
                else if (a.y >= a.z) { uv = q.xz / a.y; face = q.y > 0.0 ? 2.0 : 3.0; }
                else { uv = q.xy / a.z; face = q.z > 0.0 ? 4.0 : 5.0; }
                const float N = 96.0; // (cells along a face: some 0.9 degree each)
                float2 g = (uv * 0.5 + 0.5) * N;
                float m = max(a.x, max(a.y, a.z));
                float px = clamp(pa * 0.5 * N / (m * m), 1e-3, 0.3); // (a pixel in cells)
                float3 s = 0;
                // one cell in 8 has a star (some 7,000 round the whole sphere, as many as the eye sees on a dark night), each
                // magnitude three times as many as the one brighter: brightness u^-0.8 of the faintest's, u uniform. Kept
                // off the cell's edges, so the one cell holds its whole star at a headset's resolution
                float2 cell = floor(g), k = cell + face * 1013.0;
                float u = cwHash12(k) * 8.0;
                [branch] if (u < 1.0)
                {
                    float2 at = cell + 0.25 + 0.5 * float2(cwHash12(k + 17.3), cwHash12(k + 41.9));
                    float r2 = dot(g - at, g - at) / (px * px);
                    float3 tint = lerp(float3(1.0, 0.80, 0.60), float3(0.75, 0.84, 1.0), cwHash12(k + 5.1));
                    s = tint * (0.015 * min(pow(max(u, 1e-4), -0.8), 200.0)) * exp(-r2 * 1.6);
                }
                // the Milky Way: a faint mottled band about a great circle tilted 63 degrees to the equator, a dark rift
                // along it (the noise taken in the band's own plane, so it runs along the band); nothing of it 20 degrees off
                float b = dot(q, float3(0.0, 0.454, 0.891));
                [branch] if (abs(b) < 0.33)
                {
                    float2 along = float2(q.x, dot(q, float3(0.0, 0.891, -0.454)));
                    float n = cwFbm2(along * 30.0 + float2(b, -0.7 * b) * 30.0); // (as fine along the band as across it)
                    float rift = exp(-(b - 0.02) * (b - 0.02) / 0.0005) * smoothstep(0.3, 0.6, cwFbm2(along * 3.0 + 11.0));
                    s += exp(-b * b / 0.012) * saturate(0.1 + 1.2 * n - 0.7 * rift) * float3(0.014, 0.015, 0.019);
                }
                return s;
            }

            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 rd = normalize(cwToJS(i.dir));
                float3 sun = cwSun();
                float3 c = cwSky(rd, sun);
                float pa = max(length(ddx(rd)), length(ddy(rd)));
                [branch] if (CW_TOD)
                {
                    // the sun, the moon and the stars of the moment, behind the clouds and dimmed by the thick air low down
                    float3 sd = normalize(cwToJS(_Udon_CWSun.xyz)), md = normalize(cwToJS(_Udon_CWMoon.xyz));
                    float3 add = _Udon_CWSunColor.rgb * 18.0 * cwDisc(rd, sd);
                    [branch] if (_Udon_CWMoonColor.a > 0.0) add += _Udon_CWMoonColor.rgb * 3.0 * cwMoonDisc(rd, md); // (bright, its maria still seen)
                    [branch] if (_Udon_CWNight.x > 0.0 && rd.y > 0.0) add += cwStars(rd, pa) * _Udon_CWNight.x * smoothstep(0.0, 0.12, rd.y);
                    [branch] if (any(add > 0.0)) c += add * cwCloudSunT(rd);
                }
                else
                {
                    float disc = smoothstep(0.99996, 0.999985, dot(rd, sun));
                    [branch] if (disc > 0.0) c += float3(1.0, 0.90, 0.74) * _SunIntensity * 18.0 * disc * cwCloudSunT(rd);
                }
                // below the horizon: the far water's haze colour, so the plane's far edge blends away
                [branch] if (rd.y < -0.0015) c = lerp(cwHazeColor(rd, sun) * 0.95, c, smoothstep(-0.02, -0.0015, rd.y));
                return float4(cwTonemap(max(c, 0.0)), 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
    CustomEditor "ClearwaterSkyGUI"
}
