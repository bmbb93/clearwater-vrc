// Clearwater: underwater view. An inside-out box drawn over everything while the camera is below the surface:
// each pixel's path through the water (to the depth-texture surface, or up to the water surface) absorbs the
// scene behind it and adds sunlit in-scattering, with the demo's water coefficients.
// ClearwaterController enables this renderer only while the local head is under water; the vertex shader
// also collapses it for any camera above the surface.
Shader "Clearwater/Underwater"
{
    Properties
    {
        _SunDir ("Sun direction (world, towards sun)", Vector) = (0.054, 0.515, 0.855, 0)
        _SunIntensity ("Sun intensity", Float) = 6
        [Toggle(_CW_TONEMAP)] _Tonemap ("Tone map in shader", Float) = 1
        _Exposure ("Exposure", Float) = 0.63
        _Depth ("Caustics depth (m)", Float) = 1.6
        _MaxDistance ("Max fog distance (m)", Float) = 200

        [Header(The water surface for a camera at the waterline (set by Build Scene the coast bake and the controller))]
        _Surf ("Surface (FFT CRT)", 2D) = "black" {}
        _Rip ("Ripple normals CRT", 2D) = "black" {}
        _PatchSize ("Wave patch size (m)", Float) = 4.6
        _RipSize ("Ripple window size (m)", Float) = 14
        _RipCenter ("Ripple window centre (xz, water space)", Vector) = (0, 0, 0, 0)
        _CoastTex ("Coast: shore coordinates u, v (baked)", 2D) = "black" {}
        _CoastArea ("Coast bake area (centre xz in water space, size m)", Vector) = (0, 0, 512, 0)
        _CoastProfile ("Coast: cross-section depth, wave travel time (baked)", 2D) = "black" {}
        _CoastProfileU ("Cross-section range (u min, u max, waterline u)", Vector) = (-32, 224, -1.4, 0)
        [HideInInspector] _ShoreExposure ("Coast: how exposed the shore is to the swell, along it (baked)", 2D) = "white" {}
        [HideInInspector] _ShoreExposureV ("Its range along the shore (first v, span, closed, baked)", Vector) = (0, 1, 0, 0)
        _CoastFarTex ("Coast over the whole sea (baked, coarse)", 2D) = "black" {}
        _CoastFarArea ("Its area (centre xz in water space, size m; 0 = none)", Vector) = (0, 0, 0, 0)
        _RockTex ("Rock heights", 2D) = "black" {}
        _RockArea ("Rock bake area (centre xz, size)", Vector) = (0, 0, 204.8, 0)
        _StampTex ("Stamps: raise, carve, obstacle heights (baked)", 2D) = "black" {}
        [HideInInspector] _UserTex ("Coast: user terrain heights (baked)", 2D) = "black" {}
        [HideInInspector] _UserArea ("User terrain area (centre, size, on)", Vector) = (0, 0, 0, 0)
        [HideInInspector] _UserMean ("User terrain average colour", Vector) = (0.2, 0.2, 0.2, 1)
        _StampArea ("Stamp area (centre xz, size, 1 = any stamps)", Vector) = (0, 0, 200, 0)
        _SwashTrack ("Break timing track (generated)", 2D) = "black" {}
        _SwashBreaks ("Breaks: time, strength (generated)", 2D) = "black" {}
        _SwashIdx ("Last break per moment (generated)", 2D) = "black" {}
        _SwashCount ("Number of breaks (generated)", Float) = 1
        _SwashSlope ("Beach face slope over the swash zone (set by the coast bake)", Float) = 0.1
        _SwashClock ("Clock (s, set by the controller)", Float) = 0
        _SwashLoop ("Track length (s)", Float) = 90
        _SwashHeight ("Breaker height (m, copied from the water by the controller)", Float) = 0.14
        _SwashRunup ("Run-up (x breaker height, copied from the water)", Float) = 2.6
        [ToggleUI] _ShoreWaves ("Shore waves (set by the coast bake)", Float) = 1
        _FoamRelief ("Foam relief (m): the densest foam's height, for its light and shade; 0 = flat (costs ~2 ms/eye close up)", Float) = 0
        _FoamLift ("Whitewater height (m): the run-up's front lip and the breaking roller stand up this far; 0 = flat", Float) = 0.03
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+50" "IgnoreProjector" = "True" "ForceNoShadowCasting" = "True" }
        GrabPass { "_CWGrabUnder" }
        Pass
        {
            Cull Front ZWrite Off ZTest Always
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 5.0
            #pragma multi_compile_instancing
            #pragma shader_feature_local _CW_TONEMAP
            #include "UnityCG.cginc"
            #include "ClearwaterSurface.cginc"

            float _MaxDistance;
            UNITY_DECLARE_SCREENSPACE_TEXTURE(_CWGrabUnder);
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);

            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 wpos : TEXCOORD0;
                float4 grabPos : TEXCOORD1;
                float4 screenPos : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float waterY = unity_ObjectToWorld._m13;
                // a 2 m box around this camera, whatever the object's size (that only keeps it from being culled
                // anywhere in the sea), so it never reaches the far clip
                o.wpos = _WorldSpaceCameraPos + v.vertex.xyz * 2.0;
                o.pos = UnityWorldToClipPos(o.wpos);
                // (near the surface the fragments pick their own pixels: a camera at the waterline)
                if (_WorldSpaceCameraPos.y - waterY >= CW_SURFACE_BAND) o.pos = float4(-2, -2, -2, 1);
                o.grabPos = ComputeGrabScreenPos(o.pos);
                o.screenPos = ComputeScreenPos(o.pos);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 sun = cwSun();
                float camY = _WorldSpaceCameraPos.y - unity_ObjectToWorld._m13;
                float3 rdWorld = normalize(i.wpos - _WorldSpaceCameraPos);
                float3 rd = cwToJS(rdWorld);
                // a camera at the waterline: only the part of the view that starts under the water
                [branch] if (cwCameraNearSurface(unity_ObjectToWorld._m13))
                    clip(cwPixelUnder(rdWorld, float3(unity_ObjectToWorld._m03, unity_ObjectToWorld._m13, unity_ObjectToWorld._m23)) ? 1.0 : -1.0);

                float raw = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.screenPos.xy / i.screenPos.w);
                float dist = LinearEyeDepth(raw) / max(dot(rdWorld, -UNITY_MATRIX_V[2].xyz), 1e-3);
                dist = min(dist, _MaxDistance);
                if (rd.y > 1e-4) dist = min(dist, max(-camY / rd.y, 0.0)); // the water surface (not in the depth texture)

                float3 scene = cwInvTonemap(UNITY_SAMPLE_SCREENSPACE_TEXTURE(_CWGrabUnder, i.grabPos.xy / i.grabPos.w).rgb);
                float meanDepth = max(-(camY + rd.y * dist * 0.5), 0.0);
                float3 col = scene * exp(-SIG_T * dist) + cwInscatter(meanDepth, dist, rd, sun);
                return float4(cwTonemap(max(col, 0.0)), 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
