// Clearwater: photoreal shallow water surface — the demo's main shader, run per pixel on a flat plane.
// The plane only marks where water is; each pixel re-intersects the displaced height field along the view ray.
// From above: Fresnel reflection of the sky, sun glints, and the refracted floor lit by the caustics. The floor is
// traced analytically (as in the demo); anything standing in the water (avatars) is taken from the grab texture
// and attenuated by its path through the water, using the camera depth texture.
// From below: Snell's window onto the sky, total internal reflection outside it (the fog volume adds the water).
Shader "Clearwater/Water"
{
    Properties
    {
        [Header(Simulation inputs)]
        _Surf ("Surface (FFT CRT)", 2D) = "black" {}
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

        [Header(Look)]
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
        [Toggle(_CW_TONEMAP)] _Tonemap ("Tone map in shader (turn off when using post-process tone mapping)", Float) = 1
        _Exposure ("Exposure", Float) = 0.63
        _SeaHalfSize ("Sea half size (m, set by the coast bake): the edge fades into the haze", Float) = 2500

        [Header(Floor shape)]
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
        [HideInInspector] _Calm ("Pool: 1 - its wave strength (0 = the sea)", Float) = 0
        [HideInInspector] _Indoor ("Pool: indoors (no sun; the room's reflection probe for the sky)", Float) = 0
        [HideInInspector] _EnvGain ("Pool indoors: the room's brightness in it", Float) = 1
        [HideInInspector] _PoolMaskArea ("Its area (world x, z corner, size, 1 = any pools)", Vector) = (0, 0, 0, 0)
        [HideInInspector] _BodyArea ("Pool: its footprint (world x, z min, x, z max; set by its bake)", Vector) = (0, 0, 0, 0)
        [HideInInspector] _BodyFloor ("Pool: its floor (world y), 1 (0 = the sea)", Vector) = (0, 0, 0, 0)
        _StampArea ("Stamp area (centre xz, size, 1 = any stamps)", Vector) = (0, 0, 200, 0)

        [Header(Shoreline waves)]
        _SwashTrack ("Break timing track (generated)", 2D) = "black" {}
        _SwashBreaks ("Breaks: time, strength (generated)", 2D) = "black" {}
        _SwashIdx ("Last break per moment (generated)", 2D) = "black" {}
        _SwashCount ("Number of breaks (generated)", Float) = 1
        _SwashSlope ("Beach face slope over the swash zone (set by the coast bake)", Float) = 0.1
        _SwashClock ("Clock (s, set by the controller from the wave audio)", Float) = 0
        _SwashLoop ("Track length (s)", Float) = 90
        _SwashHeight ("Breaker height (m)", Float) = 0.14
        _SwashRunup ("Run-up (x breaker height)", Float) = 2.6
        [Range(0.5, 6)] _SwellDepth ("Swell start depth (m): the swell rolling in shows where the water is this shallow, full height 0.8 m shallower; deeper than the coast's cross-section reaches, it covers the whole sea (and costs more)", Float) = 2.6
        [ToggleUI] _ShoreWaves ("Shore waves (set by the coast bake)", Float) = 1
        _FoamRelief ("Foam relief (m): the densest foam's height, for its light and shade; 0 = flat (costs ~1 ms/eye close up)", Float) = 0
        _FoamLift ("Whitewater height (m): the run-up's front lip and the breaking roller stand up this far; 0 = flat", Float) = 0.03
        [Enum(Off, 0, On, 1)] _CWZWrite ("Depth write (Off: avatars' see-through parts after the water are not hidden under it)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" "ForceNoShadowCasting" = "True" }
        GrabPass { "_CWGrabWater" }
        // The views from above and from under the water are two passes of the same code (ClearwaterWater.cginc):
        // a camera draws only the one for its side (the other's triangles are collapsed in the vertex shader), and
        // the large under-water path is compiled into its own pass only.

        // seen from above
        Pass
        {
            Cull Off ZWrite [_CWZWrite]
            CGPROGRAM
            #pragma vertex vertAbove
            #pragma fragment fragAbove
            #pragma target 5.0
            #pragma multi_compile_instancing
            #pragma shader_feature_local _CW_TONEMAP
            #define CW_WATER_BELOW 0
            #include "ClearwaterWater.cginc"
            v2f vertAbove(appdata v) { return vertSide(v); }
            float4 fragAbove(v2f i) : SV_Target { return fragSide(i); }
            ENDCG
        }

        // seen from under the water
        Pass
        {
            Cull Off ZWrite [_CWZWrite]
            CGPROGRAM
            #pragma vertex vertBelow
            #pragma fragment fragBelow
            #pragma target 5.0
            #pragma multi_compile_instancing
            #pragma shader_feature_local _CW_TONEMAP
            #define CW_WATER_BELOW 1
            #include "ClearwaterWater.cginc"
            v2f vertBelow(appdata v) { return vertSide(v); }
            float4 fragBelow(v2f i) : SV_Target { return fragSide(i); }
            ENDCG
        }
    }
    Fallback Off
}
