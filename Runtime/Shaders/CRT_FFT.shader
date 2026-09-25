// Clearwater: 256-point inverse DFT along one axis, as a 16x16 four-step FFT (two passes per axis).
//   n = 16a + b, k = c + 16d:  X[k] = sum_b e^{2pi i bd/16} * ( e^{2pi i bc/256} * sum_a x[16a+b] e^{2pi i ac/16} )
// Stage 0 writes the bracket at index 16c + b; stage 1 finishes the sum. Four CRTs (x0, x1, y0, y1) give the
// full 2-D transform — the WebGL version needed 16 radix-2 passes. Two complex signals are carried in xy / zw.
Shader "Clearwater/CRT/FFT"
{
    Properties
    {
        _Src ("Source", 2D) = "black" {}
        [Enum(Horizontal,0,Vertical,1)] _Axis ("Axis", Float) = 0
        [Enum(Stage0,0,Stage1,1)] _Stage ("Stage", Float) = 0
        [Toggle] _Resolve ("Resolve to (h, dh/dx, dh/dz, slope^2)", Float) = 0
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

            Texture2D<float4> _Src;
            float _Axis, _Stage, _Resolve;

            float2 cmul(float2 a, float2 b) { return float2(a.x * b.x - a.y * b.y, a.x * b.y + a.y * b.x); }
            float2 cis(float turns) { float s, c; sincos(6.28318530718 * turns, s, c); return float2(c, s); }

            float4 frag(v2f_customrendertexture IN) : SV_Target
            {
                int2 id = int2(IN.localTexcoord.xy * float2(_CustomRenderTextureWidth, _CustomRenderTextureHeight));
                bool vert = _Axis > 0.5;
                int j = vert ? id.y : id.x;
                float4 acc = 0;
                if (_Stage < 0.5)
                {
                    int c = j >> 4, b = j & 15;
                    [unroll] for (int a = 0; a < 16; a++)
                    {
                        int n = 16 * a + b;
                        float4 x = _Src.Load(int3(vert ? int2(id.x, n) : int2(n, id.y), 0));
                        float2 w = cis(float((a * c) & 15) / 16.0);
                        acc += float4(cmul(w, x.xy), cmul(w, x.zw));
                    }
                    float2 tw = cis(float(b * c) / 256.0);
                    acc = float4(cmul(tw, acc.xy), cmul(tw, acc.zw));
                }
                else
                {
                    int c = j & 15, d = j >> 4;
                    [unroll] for (int b = 0; b < 16; b++)
                    {
                        int n = 16 * c + b;
                        float4 x = _Src.Load(int3(vert ? int2(id.x, n) : int2(n, id.y), 0));
                        float2 w = cis(float((b * d) & 15) / 16.0);
                        acc += float4(cmul(w, x.xy), cmul(w, x.zw));
                    }
                }
                if (_Resolve > 0.5)
                {
                    float2 sl = acc.yz;
                    acc = float4(acc.x, sl, dot(sl, sl));
                }
                return acc;
            }
            ENDCG
        }
    }
}
