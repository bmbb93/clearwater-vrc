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
        _Peb ("Pebble bed", 2D) = "grey" {}
        _PatchSize ("Wave patch size (m)", Float) = 4.6
        _Depth ("Caustics depth (m)", Float) = 1.6
        _RipSize ("Ripple window size (m)", Float) = 14
        _RipCenter ("Ripple window centre (xz, water space)", Vector) = (0, 0, 0, 0)

        [Header(Look)]
        _SunDir ("Sun direction (world, towards sun)", Vector) = (0.054, 0.515, 0.855, 0)
        _SunIntensity ("Sun intensity", Float) = 6
        [Toggle(_CW_TONEMAP)] _Tonemap ("Tone map in shader (turn off when using post-process tone mapping)", Float) = 1
        _Exposure ("Exposure", Float) = 0.63
        _SeaHalfSize ("Sea half size (m, set by the coast bake): the edge fades into the haze", Float) = 2500

        [Header(Floor shape)]
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

        [Header(Shoreline waves)]
        _SwashTrack ("Break timing track (generated)", 2D) = "black" {}
        _SwashClock ("Clock (s, set by the controller from the wave audio)", Float) = 0
        _SwashLoop ("Track length (s)", Float) = 90
        _SwashHeight ("Breaker height (m)", Float) = 0.14
        _SwashRunup ("Run-up (x breaker height)", Float) = 2.2
        [ToggleUI] _ShoreWaves ("Shore waves (set by the coast bake)", Float) = 1
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
            Cull Off ZWrite On
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
            Cull Off ZWrite On
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
