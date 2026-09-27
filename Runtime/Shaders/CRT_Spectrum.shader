// Clearwater: time-evolved ocean spectrum H(k,t), packed so that one inverse FFT yields h, dh/dx, dh/dz.
Shader "Clearwater/CRT/Spectrum"
{
    Properties
    {
        _H0 ("H0 (h0(k), conj h0(-k))", 2D) = "black" {}
        _PatchSize ("Patch size (m)", Float) = 4.6
        _TimeScale ("Time scale", Float) = 0.9
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

            Texture2D<float4> _H0;
            float _PatchSize, _TimeScale;
            // how fast the waves go as set while the world runs (x, as against as built) and the shift that keeps them
            // in shape when it changes (y), w 1 once set (ClearwaterController.SetRippleSpeed)
            float4 _Udon_CWRipple;

            float2 cmul(float2 a, float2 b) { return float2(a.x * b.x - a.y * b.y, a.x * b.y + a.y * b.x); }

            float4 frag(v2f_customrendertexture IN) : SV_Target
            {
                float2 size = float2(_CustomRenderTextureWidth, _CustomRenderTextureHeight);
                int2 id = int2(IN.localTexcoord.xy * size);
                float4 s = _H0.Load(int3(id, 0));
                float2 n = float2(id); n -= step(size * 0.5, n) * size;
                float2 k = 6.28318530718 * n / _PatchSize; float kl = length(k);
                float w = sqrt(9.81 * kl + 7.4e-5 * kl * kl * kl);
                // gentle dispersion quantisation keeps the loop seamless over 60 s
                float w0 = 6.28318530718 / 60.0; w = floor(w / w0) * w0;
                float2 rs = _Udon_CWRipple.w > 0.5 ? _Udon_CWRipple.xy : float2(1.0, 0.0);
                float t = fmod((_Time.y * rs.x + rs.y) * _TimeScale, 60.0);
                float c = cos(w * t), sn = sin(w * t);
                float2 H = cmul(s.xy, float2(c, sn)) + cmul(s.zw, float2(c, -sn));
                float2 C1 = H - k.x * H;                  // h + i*dh/dx
                float2 C2 = float2(-k.y * H.y, k.y * H.x); // dh/dz
                return float4(C1, C2);
            }
            ENDCG
        }
    }
}
