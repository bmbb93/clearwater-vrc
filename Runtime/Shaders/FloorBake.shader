// Clearwater: evaluates the shaders' own floor (cwFloorDepth: baked coast, relief, rocks) on a square grid, so the
// walkable collider is built from exactly what the water and the seabed draw. Used by the editor's bake.
// _BakeGrid: xy = first sample (Unity x, z relative to the water origin), z = spacing (m), w = samples per side.
Shader "Hidden/Clearwater/FloorBake"
{
    Properties
    {
        // declared so an unset texture reads as "no rock" / nothing, not the engine's grey default
        _RockTex ("Rock heights", 2D) = "black" {}
        _CoastTex ("Coast: shore coordinates u, v", 2D) = "black" {}
        _CoastProfile ("Coast: cross-section", 2D) = "black" {}
        _StampTex ("Stamps", 2D) = "black" {}
        [HideInInspector] _UserTex ("Coast: user terrain heights (baked)", 2D) = "black" {}
        [HideInInspector] _UserArea ("User terrain area (centre, size, on)", Vector) = (0, 0, 0, 0)
        [HideInInspector] _UserMean ("User terrain average colour", Vector) = (0.2, 0.2, 0.2, 1)
    }
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

            float4 _BakeGrid;

            float4 frag(v2f_img i) : SV_Target
            {
                float2 ij = floor(i.uv * _BakeGrid.w);
                float2 w = _BakeGrid.xy + ij * _BakeGrid.z;   // Unity x, z
                return cwFloorDepth(float2(w.x, -w.y));      // water space has z negated
            }
            ENDCG
        }
    }
}
