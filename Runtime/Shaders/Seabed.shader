// Clearwater: the real seabed/beach geometry. A flat grid is displaced on the GPU by the same floor function the
// water traces analytically, so the ground you walk on and the ground you see line up. Underwater it is lit like
// the demo's floor (caustics, absorbed sunlight); above the waterline it is dry, sunlit beach.
// Its ShadowCaster pass puts it in the camera depth texture, which the water and the fog volume read.
Shader "Clearwater/Seabed"
{
    Properties
    {
        _Caus ("Caustics RT", 2D) = "black" {}
        _Rip ("Ripple normals CRT", 2D) = "black" {}
        _Peb ("Pebble bed", 2D) = "grey" {}
        _PatchSize ("Wave patch size (m)", Float) = 4.6
        _Depth ("Caustics depth (m)", Float) = 1.6
        _RipSize ("Ripple window size (m)", Float) = 14
        _RipCenter ("Ripple window centre (xz, water space)", Vector) = (0, 0, 0, 0)
        _SunDir ("Sun direction (world, towards sun)", Vector) = (0.054, 0.515, 0.855, 0)
        _SunIntensity ("Sun intensity", Float) = 6
        [Toggle(_CW_TONEMAP)] _Tonemap ("Tone map in shader", Float) = 1
        _Exposure ("Exposure", Float) = 0.63
        _CoastTex ("Coast: shore coordinates u, v (baked)", 2D) = "black" {}
        _CoastArea ("Coast bake area (centre xz in water space, size m)", Vector) = (0, 0, 512, 0)
        _CoastProfile ("Coast: cross-section depth, wave travel time (baked)", 2D) = "black" {}
        _CoastProfileU ("Cross-section range (u min, u max, waterline u)", Vector) = (-32, 224, -1.4, 0)
        _CoastFarTex ("Coast over the whole sea (baked, coarse)", 2D) = "black" {}
        _CoastFarArea ("Its area (centre xz in water space, size m; 0 = none)", Vector) = (0, 0, 0, 0)
        _RockTex ("Rock heights (baked by Build Scene)", 2D) = "black" {}
        _RockArea ("Rock bake area (centre xz, size)", Vector) = (0, 0, 204.8, 0)
        _StampTex ("Stamps: raise, carve, obstacle heights (baked)", 2D) = "black" {}
        _StampArea ("Stamp area (centre xz, size, 1 = any stamps)", Vector) = (0, 0, 200, 0)
        _WaterOrigin ("Water origin (world)", Vector) = (0, 0, 0, 0)
        _GridCenter ("Grid centre (world xz, set by the controller)", Vector) = (0, 0, 0, 0)
        _SeaHalfSize ("Sea half size (m, set by the coast bake)", Float) = 2500
        _Surf ("Surface (FFT CRT; for a camera at the waterline)", 2D) = "black" {}
        _SwashTrack ("Break timing track (generated)", 2D) = "black" {}
        _SwashClock ("Clock (s, set by the controller from the wave audio)", Float) = 0
        _SwashLoop ("Track length (s)", Float) = 90
        _SwashHeight ("Breaker height (m)", Float) = 0.14
        _SwashRunup ("Run-up (x breaker height)", Float) = 2.2
    }
    CGINCLUDE
    #include "UnityCG.cginc"
    #include "ClearwaterSurface.cginc"
    float4 _WaterOrigin, _GridCenter;
    float _SeaHalfSize;

    // grid vertex (object xz) -> displaced world position
    float3 seabedWorld(float4 vertex)
    {
        // the grid (fine near its centre, ever coarser out to the horizon) travels with the viewer; heights come
        // from world position, so the ground itself stays put
        float3 w = mul(unity_ObjectToWorld, float4(vertex.x, 0, vertex.z, 1)).xyz + float3(_GridCenter.x, 0, _GridCenter.z);
        float2 js = float2(w.x - _WaterOrigin.x, -(w.z - _WaterOrigin.z));
        w.y = _WaterOrigin.y - cwFloorDepth(js);
        return w;
    }
    ENDCG

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 5.0
            #pragma multi_compile_instancing
            #pragma shader_feature_local _CW_TONEMAP

            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float3 wpos : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.wpos = seabedWorld(v.vertex);
                o.pos = UnityWorldToClipPos(o.wpos);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 sun = cwSun();
                float2 p = float2(i.wpos.x - _WaterOrigin.x, -(i.wpos.z - _WaterOrigin.z));
                float2 dpdx = ddx(p), dpdy = ddy(p);
                float footprint = length(fwidth(i.wpos));
                float depth = cwFloorDepth(p);
                // Seen from above the water, submerged ground is always covered by the water surface, which traces
                // the floor itself — skip it. The margin keeps the waterline (waves, ripples) fully shaded.
                // Alpha 0 marks these pixels for the water, so it never mistakes them for a submerged object.
                // (a camera at the waterline decides per pixel: the part of its view that starts under the water
                // sees the ground directly, with the underwater fog over it)
                bool under = cwPixelUnder(normalize(i.wpos - _WorldSpaceCameraPos), _WaterOrigin.xyz);
                [branch] if (_WaterOrigin.y - i.wpos.y > 0.15 && !under)
                {
                    // past the water plane nothing covers it: the haze the water fades into at its edge
                    float2 e = abs(i.wpos.xz - _WaterOrigin.xz);
                    [branch] if (max(e.x, e.y) > _SeaHalfSize)
                    {
                        float3 vh = cwToJS(i.wpos - _WorldSpaceCameraPos);
                        float mh = max(dot(normalize(float3(vh.x, 0.0, vh.z) + 1e-5), sun), 0.0);
                        float3 hz = float3(0.60, 0.71, 0.82) + float3(1.0, 0.86, 0.66) * (0.22 * pow(mh, 6.0) + 0.3 * pow(mh, 64.0));
                        return float4(cwTonemap(hz * 0.95), 1.0);
                    }
                    return float4(0, 0, 0, 0);
                }
                float hgt, rockM;
                float3 alb = cwFloorAlbedo(p, dpdx, dpdy, hgt, rockM);
                float3 L;
                if (depth > 0.0)
                {
                    // explicit gradients (we are inside a branch); x2 = one mip softer, the demo's LOD bias of 1
                    float3 caus = tex2Dgrad(_Caus, cwCausUV(p, hgt, sun), dpdx * (2.0 / _PatchSize), dpdy * (2.0 / _PatchSize)).rgb;
                    float sunShade = 1.0;
                    [branch] if (rockM > 0.0) sunShade = lerp(1.0, cwUnderSunShade(cwFloorNormal(p, rockM), sun), rockM);
                    L = cwFloorRadianceUnder(p, depth, hgt, cwAlgae(alb, rockM), sun, caus, sunShade);
                }
                else
                {
                    // dry beach: sun and sky on the analytic slope; damp sand darkens just above the waterline
                    float3 n = cwFloorNormal(p, rockM);
                    // rock stays dark and damp well above the waterline, where the spray reaches; lichen on the
                    // dry tops; hollows and steep flanks catch less sky
                    alb *= lerp(1.0, lerp(0.6, 1.0, smoothstep(0.0, 0.25, -depth)), rockM);
                    float lichen = smoothstep(0.66, 0.72, cwNoise(p * 4.0 + 21.0)) * smoothstep(0.35, 0.6, cwNoise(p * 0.5 + 8.0))
                                 * smoothstep(0.8, 0.95, n.y) * smoothstep(0.4, 0.8, -depth);
                    alb = lerp(alb, alb * float3(0.96, 0.93, 0.7), lichen * 0.45 * rockM);
                    float ao = lerp(0.6, 1.0, smoothstep(0.08, 0.42, hgt)) * lerp(1.0, lerp(0.55, 1.0, n.y), rockM);
                    // where the swash just drained: a glistening film that soaks in over a few seconds,
                    // with foam left stranded on the stones
                    float film = cwWetFilm(p, -depth);
                    float3 dry = alb * lerp(0.72, 1.0, smoothstep(0.0, 0.3, -depth));
                    float3 albW = dry * (1.0 - 0.5 * film);
                    L = albW / CW_PI * (cwSunColor() * max(dot(n, sun), 0.0) * ao + cwSkyIrr() * 1.3 * ao);
                    [branch] if (film > 0.01)
                    {
                        float3 v = normalize(cwToJS(_WorldSpaceCameraPos - i.wpos));
                        // the film is smoother than the stones under it
                        float3 nf = normalize(n + float3(0, 2.0, 0));
                        float fr = cwFresnel(dot(nf, v), CW_IOR);
                        float3 h = normalize(v + sun);
                        float nh = saturate(dot(nf, h)), a2 = 0.02;
                        float c2 = max(nh * nh, 1e-4);
                        float glint = exp(-(1.0 - c2) / c2 / a2) / (CW_PI * a2 * c2 * c2) * fr * saturate(dot(nf, sun)) * 0.25;
                        // water stands on the tops of the stones and in scattered spots: the glint sparkles, it is no mirror
                        float sparkle = smoothstep(0.3, 0.65, hgt + 0.35 * (cwNoise(p * 26.0) - 0.5));
                        L += film * (fr * cwSkyFw(reflect(-v, nf), sun, 0.002) * lerp(0.5, 1.0, sparkle) + cwSunColor() * min(glint, 60.0) * sparkle);
                        float foam = cwFoam(p, 0.38 * pow(film, 1.9) * cwFoamAlong(cwShoreV(p), _SwashClock), _SwashClock, footprint);
                        L = lerp(L, cwFoamRadiance(n, sun), foam * 0.85);
                    }
                }
                // distant land fades into the same haze as the distant water
                [branch] if (!under)
                {
                    float3 vd = cwToJS(i.wpos - _WorldSpaceCameraPos);
                    float dist = length(vd);
                    float muh = max(dot(normalize(float3(vd.x, 0.0, vd.z) + 1e-5), sun), 0.0);
                    float3 hazeC = float3(0.60, 0.71, 0.82) + float3(1.0, 0.86, 0.66) * (0.22 * pow(muh, 6.0) + 0.3 * pow(muh, 64.0));
                    L = lerp(L, hazeC * 0.95, (1.0 - exp(-dist * 0.004)) * 0.8);
                }
                return float4(cwTonemap(max(L, 0.0)), 1.0);
            }
            ENDCG
        }
        Pass
        {
            Tags { "LightMode" = "ShadowCaster" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 5.0
            #pragma multi_compile_instancing
            #pragma multi_compile_shadowcaster

            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityApplyLinearShadowBias(UnityWorldToClipPos(seabedWorld(v.vertex)));
                return o;
            }
            float4 frag(v2f i) : SV_Target { return 0; }
            ENDCG
        }
    }
    Fallback Off
}
