// Clearwater: renders ClearwaterStamp meshes from above into the stamp texture: the highest point of each kind at
// every texel (BlendOp Max), in water space. Pass = the stamp's mode: 0 obstacle -> B, 1 raise -> R, 2 carve -> G
// (stored negated so that Max keeps the highest carve surface; the bake flips it back). Cleared to -50 = nothing.
// Pass 3 = the coast's user terrain: its top in R, 1 in G where it is (cleared to -50, 0).
// Vertices are placed straight into the stamp area (_StampArea: xy = centre, z = size, in water space).
Shader "Hidden/Clearwater/StampBake"
{
    CGINCLUDE
    #include "UnityCG.cginc"
    float4 _StampArea;
    float4 _WaterOrigin;
    struct v2f { float4 pos : SV_POSITION; float y : TEXCOORD0; };
    v2f vert(float4 vertex : POSITION)
    {
        v2f o;
        float3 w = mul(unity_ObjectToWorld, vertex).xyz;
        float2 js = float2(w.x - _WaterOrigin.x, -(w.z - _WaterOrigin.z));
        float2 ndc = (js - _StampArea.xy) / (0.5 * _StampArea.z);
        o.pos = mul(UNITY_MATRIX_VP, float4(ndc, 0.5, 1.0));
        o.y = w.y - _WaterOrigin.y;
        return o;
    }
    ENDCG
    SubShader
    {
        ZTest Always ZWrite Off Cull Off
        Blend One One
        BlendOp Max
        Pass // obstacle
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 frag(v2f i) : SV_Target { return float4(-50, -50, i.y, 0); }
            ENDCG
        }
        Pass // raise ground
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 frag(v2f i) : SV_Target { return float4(i.y, -50, -50, 0); }
            ENDCG
        }
        Pass // carve ground
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 frag(v2f i) : SV_Target { return float4(-50, -i.y, -50, 0); }
            ENDCG
        }
        Pass // user terrain
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 frag(v2f i) : SV_Target { return float4(i.y, 1, 0, 0); }
            ENDCG
        }
    }
}
