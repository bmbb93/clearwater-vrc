// Clearwater: refracted-grid caustics with dispersion (Evan Wallace's method).
// A grid over one wave patch is bent by the refracted sun ray down to the mean floor depth and drawn additively;
// the light intensity is the source area each target pixel receives. One pass per colour channel, each with its
// own index of refraction, so focused lines split into faint rainbow fringes.
//
// Drawn by the orthographic "Caustics Camera" into the caustics render texture. The vertex shader writes clip
// space directly, so it refuses to draw in any camera other than that one (checked via its ortho size).
Shader "Clearwater/Caustics"
{
    Properties
    {
        _Surf ("Surface (FFT)", 2D) = "black" {}
        _PatchSize ("Patch size (m)", Float) = 4.6
        _Depth ("Mean depth (m)", Float) = 1.6
        _IorR ("IOR red", Float) = 1.3315
        _IorG ("IOR green", Float) = 1.3335
        _IorB ("IOR blue", Float) = 1.3365
        _CausRes ("Caustics texture resolution", Float) = 1024
        _CamOrthoSize ("Caustics camera ortho size (guard)", Float) = 7.77
        _SunDir ("Sun direction (world, towards sun)", Vector) = (0.054, 0.515, 0.855, 0)
    }
    CGINCLUDE
    #include "UnityCG.cginc"
    #include "ClearwaterCommon.cginc"

    sampler2D _Surf;
    float _PatchSize, _Depth, _CausRes, _CamOrthoSize;

    struct v2f { float4 pos : SV_POSITION; float2 src : TEXCOORD0; };

    v2f vertCaus(float4 vertex, float ior)
    {
        v2f o;
        float2 aUV = vertex.xy;
        float4 s = tex2Dlod(_Surf, float4(aUV, 0, 0));
        float3 n = normalize(float3(-s.y, 1.0, -s.z));
        float3 sun = cwSun();
        float3 r = refract(-sun, n, 1.0 / ior);
        float3 P = float3(aUV.x * _PatchSize, s.x, aUV.y * _PatchSize);
        float3 F = P + r * ((-_Depth - s.x) / r.y);
        o.src = aUV * _PatchSize;
        float2 c = (F.xz - cwCausShift(sun, _Depth)) / _PatchSize;
        o.pos = float4(c * 2.0 - 1.0, 0.5, 1.0);
        o.pos.y *= _ProjectionParams.x;
        bool ours = unity_OrthoParams.w > 0.5 && abs(unity_OrthoParams.y - _CamOrthoSize) < 1e-3;
        if (!ours) o.pos = float4(-2, -2, -2, 1); // collapse to a point: nothing is drawn
        return o;
    }

    float4 frag(v2f i) : SV_Target
    {
        float2 a = ddx(i.src), b = ddy(i.src);
        float area = abs(a.x * b.y - a.y * b.x);
        float k = _CausRes / _PatchSize;
        return min(area * k * k, 40.0);
    }
    ENDCG

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }
        Blend One One ZWrite Off ZTest Always Cull Off
        Pass
        {
            ColorMask R
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            float _IorR;
            v2f vert(float4 vertex : POSITION) { return vertCaus(vertex, _IorR); }
            ENDCG
        }
        Pass
        {
            ColorMask G
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            float _IorG;
            v2f vert(float4 vertex : POSITION) { return vertCaus(vertex, _IorG); }
            ENDCG
        }
        Pass
        {
            ColorMask B
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            float _IorB;
            v2f vert(float4 vertex : POSITION) { return vertCaus(vertex, _IorB); }
            ENDCG
        }
    }
}
