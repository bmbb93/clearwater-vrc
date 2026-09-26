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
        _Peb ("Bed look: colour (set by the coast from its bed look)", 2D) = "grey" {}
        [HideInInspector] _BedHeight ("Bed look: height", 2D) = "grey" {}
        [HideInInspector] _BedTile ("Bed look: tile size (m)", Float) = 0.78
        [HideInInspector] _BedHasHeight ("Bed look: has a height texture", Float) = 0
        [HideInInspector] _BedCoarse ("Bed look: larger patches", Float) = 1
        [HideInInspector] _BedSandFill ("Bed look: sand between the stones", Float) = 1
        [HideInInspector] _BedSandColor ("Bed look: that sand", Vector) = (0.60, 0.55, 0.44, 1)
        [HideInInspector] _BedSandBed ("Bed look: the bed is sand", Float) = 0
        [HideInInspector] _BedRipple ("Bed look: ripple marks", Float) = 1
        [HideInInspector] _BedWeed ("Bed look: weed", Float) = 0.7
        [HideInInspector] _BedSat ("Bed look: saturation", Float) = 0.8
        [HideInInspector] _BedTint ("Bed look: tint", Vector) = (1.10, 1.0, 0.86, 1)
        [HideInInspector] _BedVar ("Bed look: patchiness", Float) = 1
        [HideInInspector] _BedGrade ("Bed look: muted grade", Float) = 1
        [HideInInspector] _BedBright ("Bed look: brightness", Float) = 1
        [HideInInspector] _BedGloss ("Bed look: smoothness of the dry ground (as the Standard shader's)", Float) = 0
        [HideInInspector] _BedMean ("Bed look: average colour", Vector) = (0.085, 0.085, 0.075, 1)
        _PatchSize ("Wave patch size (m)", Float) = 4.6
        _Depth ("Caustics depth (m)", Float) = 1.6
        _RipSize ("Ripple window size (m)", Float) = 14
        _RipCenter ("Ripple window centre (xz, water space)", Vector) = (0, 0, 0, 0)
        _SunDir ("Sun direction (world, towards sun)", Vector) = (0.054, 0.515, 0.855, 0)
        _SunIntensity ("Sun intensity", Float) = 6
        [HideInInspector] _CloudCover ("Clouds: cover (copied from the sky)", Range(0, 1)) = 0
        [HideInInspector] _CloudSize ("Clouds: size (m)", Float) = 900
        [HideInInspector] _CloudSpeed ("Clouds: drift (m/s)", Float) = 8
        [HideInInspector] _CloudDir ("Clouds: drift direction (deg)", Float) = 60
        [Toggle(_CW_TONEMAP)] _Tonemap ("Tone map in shader", Float) = 1
        _Exposure ("Exposure", Float) = 0.63
        _CoastTex ("Coast: shore coordinates u, v (baked)", 2D) = "black" {}
        _CoastArea ("Coast bake area (centre xz in water space, size m)", Vector) = (0, 0, 512, 0)
        _CoastProfile ("Coast: cross-section depth, wave travel time (baked)", 2D) = "black" {}
        _CoastProfileU ("Cross-section range (u min, u max, waterline u)", Vector) = (-32, 224, -1.4, 0)
        [HideInInspector] _ShoreExposure ("Coast: how exposed the shore is to the swell, along it (baked)", 2D) = "white" {}
        [HideInInspector] _ShoreExposureV ("Its range along the shore (first v, span, closed, baked)", Vector) = (0, 1, 0, 0)
        _CoastFarTex ("Coast over the whole sea (baked, coarse)", 2D) = "black" {}
        _CoastFarArea ("Its area (centre xz in water space, size m; 0 = none)", Vector) = (0, 0, 0, 0)
        _RockTex ("Rock heights (baked by Build Scene)", 2D) = "black" {}
        _RockArea ("Rock bake area (centre xz, size)", Vector) = (0, 0, 204.8, 0)
        _StampTex ("Stamps: raise, carve, obstacle heights (baked)", 2D) = "black" {}
        [HideInInspector] _UserTex ("Coast: user terrain heights (baked)", 2D) = "black" {}
        [HideInInspector] _UserArea ("User terrain area (centre, size, on)", Vector) = (0, 0, 0, 0)
        [HideInInspector] _UserMean ("User terrain average colour", Vector) = (0.2, 0.2, 0.2, 1)
        [HideInInspector] _PoolMask ("Pools: their surfaces and floors, cut out of the sea (baked)", 2D) = "black" {}
        [HideInInspector] _PoolMaskArea ("Its area (world x, z corner, size, 1 = any pools)", Vector) = (0, 0, 0, 0)
        _StampArea ("Stamp area (centre xz, size, 1 = any stamps)", Vector) = (0, 0, 200, 0)
        _WaterOrigin ("Water origin (world)", Vector) = (0, 0, 0, 0)
        _GridCenter ("Grid centre (world xz, set by the controller)", Vector) = (0, 0, 0, 0)
        _SeaHalfSize ("Sea half size (m, set by the coast bake)", Float) = 2500
        _Surf ("Surface (FFT CRT; for a camera at the waterline)", 2D) = "black" {}
        _SwashTrack ("Break timing track (generated)", 2D) = "black" {}
        _SwashBreaks ("Breaks: time, strength (generated)", 2D) = "black" {}
        _SwashIdx ("Last break per moment (generated)", 2D) = "black" {}
        _SwashCount ("Number of breaks (generated)", Float) = 1
        _SwashSlope ("Beach face slope over the swash zone (set by the coast bake)", Float) = 0.1
        _SwashClock ("Clock (s, set by the controller from the wave audio)", Float) = 0
        _SwashLoop ("Track length (s)", Float) = 90
        _SwashHeight ("Breaker height (m)", Float) = 0.14
        _SwashRunup ("Run-up (x breaker height)", Float) = 2.6
        [ToggleUI] _ShoreWaves ("Shore waves (set by the coast bake)", Float) = 1
        _FoamRelief ("Foam relief (m): the densest foam's height, for its light and shade; 0 = flat (costs ~2 ms/eye close up)", Float) = 0
        _FoamLift ("Whitewater height (m): the run-up's front lip and the breaking roller stand up this far; 0 = flat", Float) = 0.03
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
            #pragma multi_compile_fwdbase nolightmap nodynlightmap nodirlightmap novertexlight
            #pragma shader_feature_local _CW_TONEMAP
            #include "AutoLight.cginc"

            // The sun's straight shadow at a world point, read from the screen's: off screen, at its edge (the shadow
            // carried on out past it); own (this pixel's) behind the camera, or with no shadows
            float cwScreenShadowAt(float3 w, float own)
            {
            #if defined(SHADOWS_SCREEN) && !defined(UNITY_NO_SCREENSPACE_SHADOWS)
                float4 c = UnityWorldToClipPos(w);
                [branch] if (c.w <= 0.0) return own;
                c.xy = clamp(c.xy, -0.995 * c.w, 0.995 * c.w);
                float4 s = ComputeScreenPos(c);
                float2 uv = s.xy / s.w;
            #if defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED)
                float at = UNITY_SAMPLE_TEX2DARRAY_LOD(_ShadowMapTexture, float3(uv, unity_StereoEyeIndex), 0).r;
            #else
                float at = tex2Dlod(_ShadowMapTexture, float4(uv, 0, 0)).r;
            #endif
                return at;
            #else
                return own;
            #endif
            }

            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            // (the sun's shadows on the dry beach: avatars and whatever stands on it)
            struct v2f { float4 pos : SV_POSITION; float3 wpos : TEXCOORD0; SHADOW_COORDS(1) UNITY_VERTEX_OUTPUT_STEREO };

            float _BedGloss;
            float4 _LightColor0;

            // The specular of Unity's Standard shader (a dielectric, as its BRDF1): the reflection probe's sky blurred
            // by the roughness, and the scene's sun light. For a bed look matching the user terrain, so the ground
            // past the user's meshes has their sheen; it adds to the tone-mapped colour, as the Standard shader's
            // output is not tone mapped by the shaders either. (world-space n, v)
            float3 cwStandardSpecular(float3 n, float3 v, float smoothness, float shadow)
            {
                float pr = 1.0 - smoothness, r = max(pr * pr, 0.002);
                float nv = saturate(dot(n, v));
                float3 rd = reflect(-v, n);
                float mip = pr * (1.7 - 0.7 * pr) * 6.0; // (UNITY_SPECCUBE_LOD_STEPS)
                float3 env = DecodeHDR(UNITY_SAMPLE_TEXCUBE_LOD(unity_SpecCube0, rd, mip), unity_SpecCube0_HDR);
                float grazing = saturate(smoothness + 0.04);
                float3 ind = env / (r * r + 1.0) * lerp(0.04, grazing, pow(1.0 - nv, 5.0));
                float3 l = normalize(_WorldSpaceLightPos0.xyz), h = normalize(l + v);
                float nl = saturate(dot(n, l)), nh = saturate(dot(n, h)), lh = saturate(dot(l, h));
                float vis = 0.5 / (nl * (nv * (1.0 - r) + r) + nv * (nl * (1.0 - r) + r) + 1e-5);
                float a2 = r * r, d = (nh * a2 - nh) * nh + 1.0;
                float spec = max(0.0, vis * a2 / (d * d + 1e-7) * nl);
                return ind + spec * shadow * _LightColor0.rgb * (0.04 + 0.96 * pow(1.0 - lh, 5.0));
            }

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.wpos = seabedWorld(v.vertex);
                o.pos = UnityWorldToClipPos(o.wpos);
                TRANSFER_SHADOW(o);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 sun = cwSun();
                float2 p = float2(i.wpos.x - _WaterOrigin.x, -(i.wpos.z - _WaterOrigin.z));
                clip(0.985 - cwUserTerrain(p).y); // (on the user terrain the ground is its own mesh)
                clip(cwInPool(i.wpos) ? -1.0 : 1.0); // (nor any in a pool's basin)
                float2 dpdx = ddx(p), dpdy = ddy(p);
                float footprint = length(fwidth(i.wpos));
                float depth = cwFloorDepth(p);
                float shadow = SHADOW_ATTENUATION(i); // (the sun's shadows: avatars' and whatever stands here)
                // Seen from above the water, submerged ground is always covered by the water surface, which traces
                // the floor itself — skip it. The margin keeps the waterline (waves, ripples) fully shaded.
                // Alpha 0 marks these pixels for the water, so it never mistakes them for a submerged object; their red
                // is the sun's shadow here, for the water to darken the floor it traces with (an avatar's in the shallows;
                // shallower, the alpha carries it: see the end), their green 1 tells them from pixels nothing was drawn
                // on (a VR eye's masked-off corners are black, alpha 0 too).
                // (a camera at the waterline decides per pixel: the part of its view that starts under the water
                // sees the ground directly, with the underwater fog over it)
                bool under = cwPixelUnder(normalize(i.wpos - _WorldSpaceCameraPos), _WaterOrigin.xyz);
                [branch] if (_WaterOrigin.y - i.wpos.y > 0.25 && !under)
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
                    return float4(shadow, 1, 0, 0);
                }
                float hgt, rockM;
                float3 alb = cwFloorAlbedo(p, dpdx, dpdy, hgt, rockM);
                float3 L, spec = 0.0;
                // Seen from above, sand under less than 25 cm of water is shaded like the beach (wet, the swash's film
                // and foam, the caustics growing in with depth) and the water draws its floor from this: sand in and
                // out of the water is one surface, with no change of look at the still-water line. Deeper, and from
                // under the water, the underwater lighting.
                if (depth > 0.0 && (under || depth > 0.25))
                {
                    // explicit gradients (we are inside a branch); x2 = one mip softer, the demo's LOD bias of 1
                    float3 caus = tex2Dgrad(_Caus, cwCausUV(p, hgt, sun), dpdx * (2.0 / _PatchSize), dpdy * (2.0 / _PatchSize)).rgb;
                    // (the shadow where the light's path through the waves calls for, as the water reads it for its floor)
                    float2 q = cwShadowReadXZ(p, depth, sun);
                    float sunShade = cwScreenShadowAt(_WaterOrigin.xyz + cwToJS(float3(q.x, -depth, q.y)), shadow);
                    [branch] if (rockM > 0.0) sunShade *= lerp(1.0, cwUnderSunShade(cwFloorNormal(p, rockM), sun), rockM);
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
                    L = albW / CW_PI * (cwSunColor() * max(dot(n, sun), 0.0) * shadow * ao + cwSkyIrr() * 1.3 * ao);
                    // the dry ground's own sheen (the bed look's smoothness; none on the rock, and the swash's film
                    // has its own)
                    [branch] if (_BedGloss > 0.0 && !under)
                        spec = cwStandardSpecular(cwToJS(n), normalize(_WorldSpaceCameraPos - i.wpos), _BedGloss, shadow)
                             * (1.0 - rockM) * (1.0 - film) * smoothstep(0.03, 0.0, depth);
                    [branch] if (depth > 0.0)
                    {
                        // under the water line it becomes, by 15 cm, exactly the floor the water traces itself (its
                        // caustics, its absorption), so the water can hand over from this render to its own floor
                        // without a seam
                        float3 caus = tex2Dgrad(_Caus, cwCausUV(p, hgt, sun), dpdx * (2.0 / _PatchSize), dpdy * (2.0 / _PatchSize)).rgb;
                        float3 Lu = cwFloorRadianceUnder(p, depth, hgt, cwAlgae(alb, rockM), sun, caus, shadow);
                        L = lerp(L, Lu, smoothstep(0.03, 0.15, depth));
                    }
                    // water standing here now: under it, the water surface draws the reflections and the foam
                    float2 suvS = cwShoreUV(p);
                    float wetNow = cwSwashNow(suvS).level + cwSwashLobes(_SwashClock, p) + depth;
                    float dryNow = smoothstep(0.08, 0.03, wetNow); // (only where the water surely covers it: 3 cm and more)
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
                        L += film * dryNow * (fr * cwSkyFw(reflect(-v, nf), sun, 0.002) * lerp(0.8, 1.0, sparkle) + cwSunColor() * min(glint, 60.0) * shadow * sparkle);
                        // the run-up's own foam, left behind as the sheet drains: the same lace as on the water (so
                        // it carries on across the water's edge) as dense as at the sheet's edge, fading as it soaks in
                        float fc = 0.45 * pow(film, 1.3) * cwFoamAlong(suvS.y, _SwashClock);
                        fc *= smoothstep(0.05, 0.02, wetNow);
                        // stranded: it stays where the water left it at the top of the run-up (a typical wave's reach),
                        // it does not slide back down with the backwash
                        float runTop = _SwashHeight * _SwashRunup * cwSwashAmpVar(suvS.y) / max(_SwashSlope, 0.02);
                        float foam = cwRunupFoam(suvS, fc, runTop, 1.0, footprint);
                        float2 fg = 0;
                        float reliefW = saturate(1.5 - footprint * 12.0);
                        [branch] if (foam > 0.01 && _FoamRelief > 0.0 && reliefW > 0.0)
                        {
                            float e = max(0.04, footprint);
                            fg = float2(cwRunupFoam(cwShoreUV(p + float2(e, 0)), fc, runTop, 1.0, footprint) - foam,
                                        cwRunupFoam(cwShoreUV(p + float2(0, e)), fc, runTop, 1.0, footprint) - foam) / e * reliefW;
                        }
                        L = lerp(L, cwFoamLit(foam, fg, n, sun, v), saturate(foam * 1.15) * 0.9);
                    }
                }
                // distant land fades into the same haze as the distant water
                [branch] if (!under)
                {
                    float3 vd = cwToJS(i.wpos - _WorldSpaceCameraPos);
                    float dist = length(vd);
                    float muh = max(dot(normalize(float3(vd.x, 0.0, vd.z) + 1e-5), sun), 0.0);
                    float3 hazeC = float3(0.60, 0.71, 0.82) + float3(1.0, 0.86, 0.66) * (0.22 * pow(muh, 6.0) + 0.3 * pow(muh, 64.0));
                    float hazeW = (1.0 - exp(-dist * 0.004)) * 0.8;
                    L = lerp(L, hazeC * 0.95, hazeW);
                    spec *= 1.0 - hazeW;
                }
                // (ground the water can cover, up to the run-up's reach, which the water draws over from this or from its
                // own trace: its alpha carries the sun's shadow here too, 0.5 to 1, for the trace; the marked pixels above
                // are 0, anything else 1. The water tells them apart at 0.25 and 0.4, well clear of an 8-bit screen's
                // rounding)
                float reach = _SwashHeight * _SwashRunup * 1.1 + 0.05;
                return float4(cwTonemap(max(L, 0.0)) + spec, (!under && depth > -reach) ? 0.5 + 0.5 * shadow : 1.0);
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
            struct v2f { float4 pos : SV_POSITION; float2 p : TEXCOORD0; float3 wpos : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                // Only the camera's depth texture needs it (the water reads the ground there): a light's shadow map
                // (drawn with the light's bias, which the depth texture is not) gets no ground - it would shade only
                // itself, which its own shading does - so its vertices collapse there before their floor is worked out
                // (~0.2 ms/eye)
                [branch] if (any(unity_LightShadowBias != 0.0))
                {
                    o.pos = float4(0.0, 0.0, 0.0, 1.0);
                    return o;
                }
                float3 w = seabedWorld(v.vertex);
                o.pos = UnityApplyLinearShadowBias(UnityWorldToClipPos(w));
                o.p = float2(w.x - _WaterOrigin.x, -(w.z - _WaterOrigin.z));
                o.wpos = w;
                return o;
            }
            // (on the user terrain the depth is its own mesh's; in a pool's basin, the basin's)
            float4 frag(v2f i) : SV_Target { clip(0.985 - cwUserTerrain(i.p).y); clip(cwInPool(i.wpos) ? -1.0 : 1.0); return 0; }
            ENDCG
        }
    }
    Fallback Off
}
