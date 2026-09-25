// Clearwater: the caustics on avatars (and anything else on the player layers) standing in the water.
// Drawn by a Projector that only sees the player layers, so it works whatever shader the avatar uses: each
// surface below the water is multiplied by the same caustic pattern the floor shows (2x multiply blend, so the
// bright lines can lift the colour up to twice and the gaps darken it; above the water nothing changes).
// The pattern is looked up where the refracted sun ray through the point reaches the floor, so it lines up
// with the caustics on the floor at the avatar's feet and streaks along the sun rays on upright surfaces.
Shader "Clearwater/AvatarCaustics"
{
    Properties
    {
        _Caus ("Caustics RT", 2D) = "grey" {}
        _PatchSize ("Wave patch size (m)", Float) = 4.6
        _Depth ("Caustics depth (m)", Float) = 1.6
        _SunDir ("Sun direction (world, towards sun)", Vector) = (0.054, 0.515, 0.855, 0)
        _WaterOrigin ("Water origin (world)", Vector) = (0, 0, 0, 0)
        _Strength ("Strength", Range(0, 1)) = 0.8

        [Header(Coast (for lining up with the floor))]
        _CoastTex ("Coast: shore coordinates u, v (baked)", 2D) = "black" {}
        _CoastArea ("Coast bake area (centre xz in water space, size m)", Vector) = (0, 0, 512, 0)
        _CoastProfile ("Coast: cross-section depth, wave travel time (baked)", 2D) = "black" {}
        _CoastProfileU ("Cross-section range (u min, u max, waterline u)", Vector) = (-32, 224, -1.4, 0)
        _RockTex ("Rock heights (baked by Build Scene)", 2D) = "black" {}
        _RockArea ("Rock bake area (centre xz, size)", Vector) = (0, 0, 204.8, 0)
        _StampTex ("Stamps: raise, carve, obstacle heights (baked)", 2D) = "black" {}
        _StampArea ("Stamp area (centre xz, size, 1 = any stamps)", Vector) = (0, 0, 200, 0)
    }
    SubShader
    {
        // after opaque avatars, before the water's GrabPass (so avatars seen through the surface carry it too)
        Tags { "Queue" = "AlphaTest+49" "IgnoreProjector" = "True" }
        Pass
        {
            ZWrite Off
            ZTest LEqual
            ColorMask RGB
            Blend DstColor SrcColor
            Offset -1, -1
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 5.0
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "ClearwaterFloor.cginc"

            float4 _WaterOrigin;
            float _Strength;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 wpos : TEXCOORD0;
                float3 wn : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

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

            float4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 p = cwToJS(i.wpos - _WaterOrigin.xyz); // water space: y = 0 on the mean surface
                float depth = -p.y;
                clip(depth - 0.02);
                float3 sun = cwSun(), sunT = cwSunT(sun);
                // follow the refracted sun ray on to the floor below, and read the floor's caustics there
                float below = max(cwFloorDepth(p.xz) - depth, 0.0);
                float2 uv = (p.xz + cwCausShift(sun, below) - cwCausShift(sun, _Depth)) / _PatchSize;
                float3 caus = tex2Dbias(_Caus, float4(uv, 0, 1.0)).rgb;
                // lit side only; the lines only form a little below the surface
                float facing = saturate(dot(normalize(cwToJS(i.wn)), -sunT));
                float w = _Strength * facing * smoothstep(0.03, 0.5, depth);
                float3 m = lerp(1.0, caus, w);
                return float4(saturate(0.5 * m), 1.0);
            }
            ENDCG
        }
    }
}
