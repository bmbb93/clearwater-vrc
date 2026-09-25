// Clearwater: ripple height -> (h, dh/dx, dh/dz, laplacian) in metres, for shading and caustic lensing.
Shader "Clearwater/CRT/RippleNormals"
{
    Properties
    {
        _Src ("Ripple sim", 2D) = "black" {}
        _RipSize ("Window size (m)", Float) = 14
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

            sampler2D _Src; float4 _Src_TexelSize;
            float _RipSize;

            float H(float2 uv) { return tex2Dlod(_Src, float4(uv, 0, 0)).r; }

            float4 frag(v2f_customrendertexture IN) : SV_Target
            {
                float2 vUv = IN.localTexcoord.xy;
                float2 px = _Src_TexelSize.xy;
                float texel = _RipSize * _Src_TexelSize.x; // metres per texel
                float r = H(vUv + float2(px.x, 0)), l = H(vUv - float2(px.x, 0));
                float u = H(vUv + float2(0, px.y)), d = H(vUv - float2(0, px.y));
                float h = H(vUv);
                float lap = (r + l + u + d - 4.0 * h) / (texel * texel);
                return float4(h, (r - l) / (2.0 * texel), (u - d) / (2.0 * texel), lap);
            }
            ENDCG
        }
    }
}
