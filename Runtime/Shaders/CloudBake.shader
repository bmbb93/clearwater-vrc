// The clouds' maps, baked once in the editor (ClearwaterCloudBake; the result ships with the package): a tile of
// fair-weather cumulus, seamless both ways, PERIOD cloud sizes across and up to YMAX of one tall, as a height field.
//   Pass 0: the cloud's density in one slice (y) of a 3D field: flat bases, heaped tops, puffs over them.
//   Pass 1 (map A): for each column, the top of the cloud, its base (both 0..1 of YMAX), its opacity seen through
//     from below; and room for the cloud's own number (ClearwaterCloudBake numbers the clouds after: the cover keeps
//     the clouds whose number is under it, whole).
//   Pass 2 (map B): the slope of the top (x, z: the shaders light the heap by it), the sunlight from straight above
//     that comes down through the column (its base's light), and the puffs' bumps on the top.
Shader "Hidden/Clearwater/CloudBake"
{
    Properties { _Density ("Density (3D)", 3D) = "black" {} _MapA ("Map A", 2D) = "black" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        CGINCLUDE
        #include "UnityCG.cginc"
        #define PERIOD 8.0
        #define YMAX 1.0
        float _Slice;      // pass 0: y of the slice, 0..1 of YMAX
        float _Threshold;  // strength below which there is no cloud
        sampler3D _Density;
        float _Sigma;      // extinction at density 1, per cloud size

        struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
        v2f vert(appdata_img v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord; return o; }

        // ---- noise that repeats every PERIOD cloud sizes across (x, z) -------------------------------------------
        float hash13(float3 p) { p = frac(p * 0.1031); p += dot(p, p.zyx + 31.32); return frac((p.x + p.y) * p.z); }
        float3 hash33(float3 p) { p = frac(p * float3(0.1031, 0.1030, 0.0973)); p += dot(p, p.yxz + 33.33); return frac((p.xxy + p.yxx) * p.zyx); }
        // cells wrapped round n per tile across
        float3 wrapCell(float3 c, float n) { c.xz = c.xz - n * floor(c.xz / n); return c; }

        float valueNoise3(float3 p, float n)
        {
            float3 i = floor(p), f = frac(p); f = f * f * (3.0 - 2.0 * f);
            float a = hash13(wrapCell(i, n)), b = hash13(wrapCell(i + float3(1, 0, 0), n));
            float c = hash13(wrapCell(i + float3(0, 1, 0), n)), d = hash13(wrapCell(i + float3(1, 1, 0), n));
            float e = hash13(wrapCell(i + float3(0, 0, 1), n)), g = hash13(wrapCell(i + float3(1, 0, 1), n));
            float h = hash13(wrapCell(i + float3(0, 1, 1), n)), k = hash13(wrapCell(i + float3(1, 1, 1), n));
            return lerp(lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y), lerp(lerp(e, g, f.x), lerp(h, k, f.x), f.y), f.z);
        }
        // 1 at a puff's middle, falling to 0 between them (inverted cellular noise)
        float puffs(float3 p, float n)
        {
            float3 i = floor(p), f = frac(p);
            float d = 8.0;
            [unroll] for (int z = -1; z <= 1; z++)
            [unroll] for (int y = -1; y <= 1; y++)
            [unroll] for (int x = -1; x <= 1; x++)
            {
                float3 g = float3(x, y, z);
                float3 r = g + hash33(wrapCell(i + g, n)) - f;
                d = min(d, dot(r, r));
            }
            return 1.0 - saturate(sqrt(d));
        }
        // the strength of the cloud over (x, z) (in cloud sizes): which columns hold one and how big it grows
        float strength(float2 xz)
        {
            // round clusters (the middles of cells), of mixed sizes, with smaller heads about them
            float3 p = float3(xz.x, 0.5, xz.y); // (x and z repeat; y does not)
            float s = 0.55 * puffs(p * 0.75, PERIOD * 0.75) + 0.27 * puffs(p * 1.5 + 7.1, PERIOD * 1.5)
                    + 0.18 * valueNoise3(p * 3.0 + 5.1, PERIOD * 3.0);
            return saturate((s - 0.30) / 0.55);
        }
        float density(float3 p) // p in cloud sizes, y up from the base
        {
            float s = strength(p.xz);
            float c = saturate((s - _Threshold) / (1.0 - _Threshold));
            if (c <= 0.0) return 0.0;
            float top = 0.12 + 0.75 * pow(c, 0.6);    // heaped about as high as they are wide, the big ones higher
            float base = 0.03 * (1.0 - c);            // flat bases, a little lifted at the thin edges
            float y = p.y;
            if (y < base || y > top) return 0.0;
            float h = (y - base) / max(top - base, 1e-3);     // 0 at the base, 1 at the top
            // a heap: wide low down, rounding in toward the top
            float body = c * saturate((y - base) / 0.025) * saturate(1.0 - h * h) * 1.6;
            // puffs on it, bigger and deeper the higher (the cauliflower tops), finer near the flat base
            float pf = 0.62 * puffs(p * 3.0, PERIOD * 3.0) + 0.38 * puffs(p * 7.0 + 3.7, PERIOD * 7.0);
            float wisp = valueNoise3(p * 13.0, PERIOD * 13.0);
            float erode = lerp(0.25, 0.75, h) * (1.0 - pf) + 0.12 * (1.0 - wisp);
            return saturate((body - erode) * 3.0);
        }
        ENDCG

        Pass // 0: one slice of the density field
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 5.0
            float4 frag(v2f i) : SV_Target
            {
                float3 p = float3(i.uv.x * PERIOD, _Slice * YMAX, i.uv.y * PERIOD);
                return density(p);
            }
            ENDCG
        }

        CGINCLUDE
        #define VIEW_STEPS 96
        sampler2D _MapA;
        float4 _MapA_TexelSize;
        float sampleD(float3 p) // p in cloud sizes
        {
            if (p.y < 0.0 || p.y > YMAX) return 0.0;
            return tex3Dlod(_Density, float4(p.x / PERIOD, p.z / PERIOD, p.y / YMAX, 0)).r; // (the slices are y; each slice x, z)
        }
        // the column at uv: its top and base (0..1 of YMAX: where the density passes a half), its opacity seen
        // through from below, and the sunlight from straight above that reaches its base (diffusing through: about
        // 1 / (1 + 0.1 tau) for cloud droplets, besides what goes straight through)
        void column(float2 uv, out float top, out float base, out float alpha, out float down)
        {
            float2 xz = uv * PERIOD;
            float dy = YMAX / VIEW_STEPS, tau = 0.0;
            top = 0.0; base = 1.0;
            [loop] for (int s = 0; s < VIEW_STEPS; s++)
            {
                float y = (s + 0.5) * dy;
                float d = sampleD(float3(xz.x, y, xz.y));
                tau += d * _Sigma * dy;
                if (d > 0.5) { top = max(top, y / YMAX); base = min(base, y / YMAX); }
            }
            if (top <= 0.0) base = 0.0;
            alpha = 1.0 - exp(-tau);
            down = exp(-tau) + (1.0 - exp(-tau)) / (1.0 + 0.1 * tau);
        }
        ENDCG

        Pass // 1: A = top, base, opacity, the cloud's number
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 5.0
            float4 frag(v2f i) : SV_Target
            {
                float top, base, alpha, down; column(i.uv, top, base, alpha, down);
                return float4(top, base, alpha, 0.0); // (a: each cloud's number, put in by ClearwaterCloudBake)
            }
            ENDCG
        }
        Pass // 2: B = the top's slope (x, z), the light at the base, the puffs
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 5.0
            float4 frag(v2f i) : SV_Target
            {
                // the slope of the top in cloud sizes per cloud size, over a couple of texels (smoothed a little)
                float2 e = _MapA_TexelSize.xy * 2.0;
                float hx = tex2Dlod(_MapA, float4(i.uv + float2(e.x, 0), 0, 1)).r - tex2Dlod(_MapA, float4(i.uv - float2(e.x, 0), 0, 1)).r;
                float hz = tex2Dlod(_MapA, float4(i.uv + float2(0, e.y), 0, 1)).r - tex2Dlod(_MapA, float4(i.uv - float2(0, e.y), 0, 1)).r;
                float2 g = float2(hx, hz) * YMAX / (2.0 * e * PERIOD);
                float top, base, alpha, down; column(i.uv, top, base, alpha, down);
                float3 p = float3(i.uv.x * PERIOD, top * YMAX, i.uv.y * PERIOD);
                float bumps = 0.6 * puffs(p * 7.0 + 3.7, PERIOD * 7.0) + 0.4 * puffs(p * 15.0 + 1.3, PERIOD * 15.0);
                return float4(saturate(g / 8.0 + 0.5), down, bumps);
            }
            ENDCG
        }
    }
}
