// Clearwater: bakes the shore coordinates over _CoastArea from the shore line (a polyline in water space).
//   u = signed distance from the line, positive on the sea side (to the right of the line's direction in water
//       space, i.e. to the left of it seen from above in Unity), v = distance along the line.
// An open line carries on straight past its two ends; a closed one (an island, a lake) wraps round.
// Used by the editor's coast bake; the result is _CoastTex (see ClearwaterFloor.cginc).
Shader "Hidden/Clearwater/CoastBake"
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

            #define CW_MAX_SHORE 512
            float4 _ShorePts[CW_MAX_SHORE]; // xy = point (water space), z = v at that point
            int _ShoreCount;
            float _ShoreClosed;
            float4 _CoastArea;

            float2 nrm(float2 a, float2 b) { float2 d = b - a; return normalize(float2(d.y, -d.x)); }

            float4 frag(v2f_img i) : SV_Target
            {
                float2 p = _CoastArea.xy + (i.uv - 0.5) * _CoastArea.z;
                int n = _ShoreCount;
                bool closed = _ShoreClosed > 0.5;
                int segs = closed ? n : n - 1;
                float best = 1e20, u = 0, v = 0;
                // segments (their inner part; an open line's end segments reach out forever)
                [loop] for (int k = 0; k < segs; k++)
                {
                    float3 A = _ShorePts[k].xyz, B = _ShorePts[(k + 1) % n].xyz;
                    float2 ab = B.xy - A.xy;
                    float len2 = max(dot(ab, ab), 1e-8);
                    float t = dot(p - A.xy, ab) / len2;
                    float lo = (!closed && k == 0) ? -1e9 : 0.0, hi = (!closed && k == segs - 1) ? 1e9 : 1.0;
                    if (t >= lo && t <= hi)
                    {
                        float2 q = A.xy + ab * t;
                        float d = length(p - q);
                        if (d < best)
                        {
                            best = d;
                            u = d * (dot(p - q, nrm(A.xy, B.xy)) >= 0.0 ? 1.0 : -1.0);
                            v = A.z + t * sqrt(len2);
                        }
                    }
                }
                // corners: the side is taken from the two segments' normals together
                int v0 = closed ? 0 : 1, v1 = closed ? n : n - 1;
                [loop] for (int j = v0; j < v1; j++)
                {
                    float3 P = _ShorePts[j].xyz;
                    float2 pn = nrm(_ShorePts[(j + n - 1) % n].xy, P.xy) + nrm(P.xy, _ShorePts[(j + 1) % n].xy);
                    float d = length(p - P.xy);
                    if (d < best)
                    {
                        best = d;
                        u = d * (dot(p - P.xy, pn) >= 0.0 ? 1.0 : -1.0);
                        v = P.z;
                    }
                }
                return float4(u, v, 0, 1);
            }
            ENDCG
        }
    }
}
