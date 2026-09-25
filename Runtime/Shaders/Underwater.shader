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
            #include "ClearwaterFloor.cginc"

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
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.pos = UnityWorldToClipPos(o.wpos);
                if (_WorldSpaceCameraPos.y > waterY) o.pos = float4(-2, -2, -2, 1);
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

                float raw = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.screenPos.xy / i.screenPos.w);
                float dist = LinearEyeDepth(raw) / max(dot(rdWorld, -UNITY_MATRIX_V[2].xyz), 1e-3);
                dist = min(dist, _MaxDistance);
                if (rd.y > 1e-4) dist = min(dist, -camY / rd.y); // the water surface (not in the depth texture)

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
