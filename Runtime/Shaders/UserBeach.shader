// Clearwater: the beach on the coast's user terrain (its own meshes, drawn in their own materials: ADR 0001). A
// Projector straight down over the user terrain's area draws this on its meshes, as the seabed shades the generated
// beach: the damp band just above the still water and the swash's film darkening them, the film's sheen and the foam
// left stranded as the sheet drains - in one pass (Blend One SrcAlpha: the colour underneath times alpha, plus what
// is drawn over it), so the film is worked out once. Only on the user terrain itself,
// between just under the still water and the swash's reach; where the water covers the ground now, the water
// surface draws it instead. Its properties are the seabed material's, copied by the coast bake.
Shader "Clearwater/UserBeach"
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
        [HideInInspector] _CloudShift ("Clouds: drift so far (m)", Float) = 0
        [HideInInspector] _CloudClassic ("Clouds: as in 1.2 (copied from the sky)", Float) = 0
        [HideInInspector] [NoScaleOffset] _CloudDome ("Clouds: the scene's dome (made with the scene)", 2D) = "black" {}
        [HideInInspector] [NoScaleOffset] _CloudWeather ("Clouds: where they are (the package's)", 2D) = "black" {}
        [HideInInspector] _LandCover ("Distant land: share of the horizon (copied from the sky)", Range(0, 1)) = 0.5
        [HideInInspector] _LandSetback ("Distant land: how far inland it rises (copied from the sky)", Range(0, 1500)) = 0
        [HideInInspector] _LandHeight ("Distant land: height (copied from the sky)", Range(0, 2)) = 1
        [HideInInspector] _SeaDir ("Which way the sea is (world xz, set by the coast bake)", Vector) = (0, 1, 0, 0)
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
        _StampArea ("Stamp area (centre xz, size, 1 = any stamps)", Vector) = (0, 0, 200, 0)
        _WaterOrigin ("Water origin (world)", Vector) = (0, 0, 0, 0)
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
        [HideInInspector] _SwellDepth ("Swell start depth (m, copied from the water by the controller)", Float) = 2.6
        [ToggleUI] _ShoreWaves ("Shore waves (set by the coast bake)", Float) = 1
        _FoamRelief ("Foam relief (m): the densest foam's height, for its light and shade; 0 = flat (costs ~1 ms/eye close up)", Float) = 0
        _FoamLift ("Whitewater height (m): the run-up's front lip and the breaking roller stand up this far; 0 = flat", Float) = 0.03
    }
    CGINCLUDE
    #include "UnityCG.cginc"
    #include "ClearwaterSurface.cginc"
    float4 _WaterOrigin;

    struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
    struct v2f { float4 pos : SV_POSITION; float3 wpos : TEXCOORD0; float3 wn : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };

    v2f vert(appdata v)
    {
        v2f o;
        UNITY_SETUP_INSTANCE_ID(v);
        UNITY_INITIALIZE_OUTPUT(v2f, o);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
        o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
        o.wn = UnityObjectToWorldNormal(v.normal);
        o.pos = UnityWorldToClipPos(o.wpos);
        return o;
    }

    struct CwBeach
    {
        float2 p;       // water space
        float above;    // m above the still water
        float film;     // 0..1, the swash's film still soaking in
        float damp;     // darkening of the damp sand just above the still water
    };

    // the beach's state at this point, or false where there is nothing to draw
    bool cwUserBeach(float3 wpos, out CwBeach b)
    {
        float3 p3 = cwToJS(wpos - _WaterOrigin.xyz);
        b.p = p3.xz; b.above = p3.y;
        b.film = 0; b.damp = 1;
        float reach = max(_SwashHeight * _SwashRunup * 1.1 + 0.05, 0.3); // (the film's reach, the damp band's)
        if (_ShoreWaves < 0.5 || b.above < -0.03 || b.above > reach || cwUserTerrain(b.p).y < 0.97) return false;
        b.film = cwWetFilm(b.p, b.above);
        b.damp = lerp(0.72, 1.0, smoothstep(0.0, 0.3, b.above));
        return true;
    }
    ENDCG

    SubShader
    {
        Tags { "Queue" = "AlphaTest+48" "IgnoreProjector" = "True" }
        Pass
        {
            ZWrite Off
            Blend One SrcAlpha
            Offset -1, -1
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 5.0
            #pragma multi_compile_instancing
            #pragma shader_feature_local _CW_TONEMAP
            float4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                cwCloudShade = cwCloudShadow(i.wpos); // (the clouds' shadow, here)
                CwBeach b;
                if (!cwUserBeach(i.wpos, b)) clip(-1);
                // is the swash's water standing on it now? Then the water surface draws it, from the screen: darkened
                // here as well, the ground under the run-up would show through it as a dark band along the still
                // water's line, so the damp and the film only darken the ground the water has left
                float2 suv = cwShoreUV(b.p);
                float wetNow = cwSwashNow(suv).level + cwSwashLobes(_SwashClock, b.p) - b.above;
                float dryNow = smoothstep(0.08, 0.03, wetNow);
                float m = lerp(1.0, b.damp * (1.0 - 0.5 * b.film), dryNow);
            #if defined(_CW_TONEMAP)
                m = pow(m, 0.7); // (on colours already tone mapped: about what the darkening is in the scene's light)
            #endif
                [branch] if (b.film <= 0.01) return float4(0, 0, 0, m);
                float3 sun = cwSun();
                float3 v = normalize(cwToJS(_WorldSpaceCameraPos - i.wpos));
                float3 n = normalize(cwToJS(i.wn));
                float footprint = length(fwidth(i.wpos));
                // the film: smoother than the ground under it, a sheen of sky and glints of the sun in scattered spots
                float3 nf = normalize(n + float3(0, 2.0, 0));
                float fr = cwFresnel(dot(nf, v), CW_IOR);
                float3 h = normalize(v + sun);
                float nh = saturate(dot(nf, h)), a2 = 0.02;
                float c2 = max(nh * nh, 1e-4);
                float glint = exp(-(1.0 - c2) / c2 / a2) / (CW_PI * a2 * c2 * c2) * fr * saturate(dot(nf, sun)) * 0.25;
                // (the spots and the stranded foam are patterns laid out across the ground seen from above: on an upright
                // face, a quay wall's, they would be drawn out into streaks down it, so they fade out there)
                float flat = smoothstep(0.45, 0.75, n.y);
                float sparkle = lerp(0.5, smoothstep(0.3, 0.65, 0.5 + 0.35 * (cwNoise(b.p * 26.0) - 0.5)), flat);
                float3 sheen = b.film * dryNow * (fr * cwSkyFw(reflect(-v, nf), sun, 0.002) * lerp(0.8, 1.0, sparkle) + cwSunColor() * min(glint, 60.0) * sparkle);
                // the run-up's foam, stranded where the water left it at the top of the run-up, soaking away
                float fc = 0.45 * pow(b.film, 1.3) * cwFoamAlong(suv.y, _SwashClock) * smoothstep(0.05, 0.02, wetNow) * flat;
                float runTop = _SwashHeight * _SwashRunup * cwSwashAmpVar(suv.y) / max(_SwashSlope, 0.02);
                float foam = cwRunupFoam(suv, fc, runTop, 1.0, footprint);
                float a = cwFoamAlpha(foam);
                float3 foamCol = cwTonemap(cwFoamLit(foam, float2(0, 0), n, sun, v, cwLightVolumesIrr(i.wpos, n)));
                // the foam over it, the sheen on the darkened ground (the ground under the foam hidden)
                return float4(foamCol * a + cwTonemap(sheen) * (1.0 - a), (1.0 - a) * m);
            }
            ENDCG
        }
    }
    Fallback Off
}
