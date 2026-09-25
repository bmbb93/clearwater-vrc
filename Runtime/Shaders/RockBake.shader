// Clearwater: bakes the procedural rock heights (cwRockAnalytic) into a texture over _RockArea.
// Used by Tools > Clearwater > Build Scene; the result drives the water, the seabed and the collider.
Shader "Hidden/Clearwater/RockBake"
{
    SubShader
    {
        ZTest Always Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 5.0
            #include "UnityCG.cginc"
            #include "ClearwaterFloor.cginc"

            float4 frag(v2f_img i) : SV_Target
            {
                float2 xz = _RockArea.xy + (i.uv - 0.5) * _RockArea.z;
                return cwRockAnalytic(xz);
            }
            ENDCG
        }
    }
}
