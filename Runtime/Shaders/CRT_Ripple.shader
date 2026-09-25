// Clearwater: interactive ripples — damped wave equation on a local window that follows the viewer.
// Double-buffered CRT: r = height, g = velocity. ClearwaterController (Udon) feeds _Shift and _Drop0..3 each frame.
Shader "Clearwater/CRT/Ripple"
{
    Properties
    {
        _Shift ("Window shift (uv, whole texels)", Vector) = (0, 0, 0, 0)
        _Drop0 ("Drop 0 (uv.xy, radius uv, strength)", Vector) = (0, 0, 0, 0)
        _Drop1 ("Drop 1", Vector) = (0, 0, 0, 0)
        _Drop2 ("Drop 2", Vector) = (0, 0, 0, 0)
        _Drop3 ("Drop 3", Vector) = (0, 0, 0, 0)
    }
    SubShader
    {
        Lighting Off Blend Off ZTest Always ZWrite Off Cull Off
        Pass
        {
            CGPROGRAM
            #include "UnityCustomRenderTexture.cginc"
            #pragma vertex CustomRenderTextureVertexShader
            #pragma fragment frag
            #pragma target 4.5

            float4 _Shift, _Drop0, _Drop1, _Drop2, _Drop3;

            float H(float2 uv) { return tex2Dlod(_SelfTexture2D, float4(uv, 0, 0)).r; }

            void drop(inout float h, float2 vUv, float4 d)
            {
                if (d.w != 0.0)
                {
                    float r = length(vUv - d.xy);
                    if (r < d.z) h -= d.w * (0.5 + 0.5 * cos(3.14159 * r / d.z));
                }
            }

            float4 frag(v2f_customrendertexture IN) : SV_Target
            {
                float2 vUv = IN.localTexcoord.xy;
                float2 px = 1.0 / float2(_CustomRenderTextureWidth, _CustomRenderTextureHeight);
                float2 uv = vUv + _Shift.xy;
                float4 c = tex2Dlod(_SelfTexture2D, float4(uv, 0, 0));
                float avg = 0.25 * (H(uv + float2(px.x, 0)) + H(uv - float2(px.x, 0)) + H(uv + float2(0, px.y)) + H(uv - float2(0, px.y)));
                float v = c.g + (avg - c.r) * 0.9;
                v *= 0.9955;
                float h = c.r + v;
                h *= 0.9985;
                drop(h, vUv, _Drop0); drop(h, vUv, _Drop1); drop(h, vUv, _Drop2); drop(h, vUv, _Drop3);
                // fade out near borders so the local field blends into open water
                float2 e = min(vUv, 1.0 - vUv); float edge = smoothstep(0.0, 0.06, min(e.x, e.y));
                h *= lerp(0.9, 1.0, edge); v *= lerp(0.9, 1.0, edge);
                if (uv.x < 0. || uv.y < 0. || uv.x > 1. || uv.y > 1.) { h = 0.; v = 0.; }
                return float4(h, v, 0, 1);
            }
            ENDCG
        }
    }
}
