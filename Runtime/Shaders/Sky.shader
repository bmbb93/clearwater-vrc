// Clearwater: the demo's sky (gradient, sun halo, distant pine-and-limestone headland) as a Unity skybox,
// so the world sky matches what the water reflects.
Shader "Clearwater/Skybox"
{
    Properties
    {
        _SunDir ("Sun direction (world, towards sun)", Vector) = (0.054, 0.515, 0.855, 0)
        _SunIntensity ("Sun intensity", Float) = 6
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
                c += float3(1.0, 0.90, 0.74) * _SunIntensity * 18.0 * smoothstep(0.99996, 0.999985, dot(rd, sun));
                // below the horizon: the far water's haze colour, so the plane's far edge blends away
                float muh = max(dot(normalize(float3(rd.x, 0.0, rd.z) + 1e-5), sun), 0.0);
                float3 hazeC = float3(0.60, 0.71, 0.82) + float3(1.0, 0.86, 0.66) * (0.22 * pow(muh, 6.0) + 0.3 * pow(muh, 64.0));
                c = lerp(hazeC * 0.95, c, smoothstep(-0.02, -0.0015, rd.y));
                return float4(cwTonemap(max(c, 0.0)), 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
