// Clearwater: the sea floor — shape, pebble/sand albedo, underwater lighting and in-scattering.
// Shared by the water surface (which ray-traces the floor analytically), the walkable seabed mesh and the
// underwater fog, so all three agree. Positions are water-space JS coordinates (see ClearwaterCommon.cginc).
#ifndef CLEARWATER_FLOOR_INCLUDED
#define CLEARWATER_FLOOR_INCLUDED

#include "ClearwaterCommon.cginc"

sampler2D _Peb, _Caus, _Rip;
float4 _RipCenter;
float _PatchSize, _Depth, _RipSize, _SunIntensity;

// The coast, baked by the editor (ClearwaterCoastBake):
//  _CoastTex      RG = shore coordinates over _CoastArea (xy = centre in water space, z = size in m):
//                 u = signed distance from the shore line (+ towards the sea), v = distance along it
//  _CoastProfile  the cross-section as a table over u in [_CoastProfileU.x, .y]: R = depth of the relief-free
//                 floor, G = seconds for a wave at u to reach the waterline; _CoastProfileU.z = the waterline's u
sampler2D _CoastTex, _CoastProfile;
float4 _CoastTex_TexelSize, _CoastProfile_TexelSize, _CoastArea, _CoastProfileU;

// seen through the moving water surface the finest rock detail (cracks, crystals) is lost in refraction and
// caustics; the water shader defines this > 1 so that detail fades out (and is skipped) closer to the viewer
#ifndef CW_ROCK_DETAIL_FP_SCALE
#define CW_ROCK_DETAIL_FP_SCALE 1.0
#endif

static const float3 SIG_A = float3(0.40, 0.074, 0.088);
static const float3 SIG_S = float3(0.028, 0.052, 0.068);
static const float3 SIG_T = SIG_A + SIG_S;

inline float3 cwSunColor() { return float3(1.0, 0.90, 0.74) * _SunIntensity; }
inline float3 cwSkyIrr() { return float3(0.62, 0.70, 0.78) * CW_PI * 0.22; }

// polynomial smooth min / max: blends two lines over a width k with no kink (k/4 rounding at the crossing)
float cwSmin(float a, float b, float k) { float h = max(k - abs(a - b), 0.0) / k; return min(a, b) - h * h * k * 0.25; }
float cwSmax(float a, float b, float k) { float h = max(k - abs(a - b), 0.0) / k; return max(a, b) + h * h * k * 0.25; }

// Shore coordinates (u, v) at a water-space point. Outside the baked area the coast carries on the way it leaves
// it: the field is extended along its own slope at the edge (exact for a straight coast).
float2 cwShoreUV(float2 xz)
{
    float2 uv = (xz - _CoastArea.xy) / max(_CoastArea.z, 1e-3) + 0.5;
    float2 h = 0.5 * _CoastTex_TexelSize.xy;
    float2 uvc = clamp(uv, h, 1.0 - h);
    float2 c = tex2Dlod(_CoastTex, float4(uvc, 0, 0)).rg;
    float2 out_ = uv - uvc;
    [branch] if (any(out_ != 0.0))
    {
        float2 s = sign(out_), e = 4.0 * _CoastTex_TexelSize.xy;
        float2 cx = tex2Dlod(_CoastTex, float4(uvc - float2(s.x * e.x, 0), 0, 0)).rg;
        float2 cz = tex2Dlod(_CoastTex, float4(uvc - float2(0, s.y * e.y), 0, 0)).rg;
        c += (c - cx) * abs(out_.x) / e.x + (c - cz) * abs(out_.y) / e.y;
    }
    return c;
}
inline float cwShoreU(float2 xz) { return cwShoreUV(xz).x; } // distance out from the shore line
inline float cwShoreV(float2 xz) { return cwShoreUV(xz).y; } // distance along it

// the cross-section table at u (clamped to its ends: dry land behind, the deep bottom beyond)
inline float2 cwCoastProfile(float u)
{
    float x = saturate((u - _CoastProfileU.x) / max(_CoastProfileU.y - _CoastProfileU.x, 1e-3));
    x = x * (1.0 - _CoastProfile_TexelSize.x) + 0.5 * _CoastProfile_TexelSize.x; // texel centres at the ends
    return tex2Dlod(_CoastProfile, float4(x, 0.5, 0, 0)).rg;
}

// Water depth of the relief-free floor (negative above the waterline) at distance u from the shore line: the
// baked cross-section (e.g. dry land, beach, shallows, a steeper slope, the flat deep bottom). Also drives wave
// travel and shoaling.
inline float cwFloorBaseDepth(float u) { return cwCoastProfile(u).x; }

// ---- rocks ------------------------------------------------------------------------------------------------
// nearest jittered cell point (for pits in the rock)
float cwCellF1Rock(float2 p, out float rnd)
{
    float2 i = floor(p), f = frac(p);
    float d = 8.0; rnd = 0.0;
    [unroll] for (int y = -1; y <= 1; y++)
        [unroll] for (int x = -1; x <= 1; x++)
        {
            float2 g = float2(x, y);
            float2 r = g + float2(cwHash12(i + g), cwHash12(i + g + 17.3)) - f;
            float dd = dot(r, r);
            if (dd < d) { d = dd; rnd = cwHash12(i + g + 41.7); }
        }
    return sqrt(d);
}
// Rocky ground in patches a few tens of metres across: layered ledges and clusters of boulders of mixed sizes.
// They are part of the floor, so the water, the run-up, the breaking waves, the caustics and the walkable
// collider all see them. The shape (cwRockAnalytic) is expensive, so Build Scene bakes it once into a height
// texture over the walkable area (RockBake.shader); everything else reads that texture through cwRock.

// 0..1: how rocky this area is; the spawn beach and the swimming area in front of it are kept clear
float cwRockZone(float2 xz)
{
    float z = smoothstep(0.56, 0.72, cwNoise(xz * 0.022 + float2(13.1, 4.7)));
    return z * smoothstep(18.0, 30.0, length(xz - float2(0.0, 41.0))) * smoothstep(20.0, 32.0, length(xz));
}

// One layer of boulders on a jittered grid of cell size CELL (m). Each cell may hold one: whether it does
// depends on the rocky zone and on a clustering noise, so rocks gather in groups instead of spreading evenly.
// Each is a broken block: turned, stretched, cut by six faces of random steepness under a sloping top, and
// seated partly in the ground.
float cwBoulderLayer(float2 xz, float CELL, float seed, float sizeMin, float sizeMax, float density)
{
    float2 p = xz / CELL, i = floor(p), f = frac(p);
    float h = 0.0;
    [unroll] for (int y = -1; y <= 1; y++)
        [unroll] for (int x = -1; x <= 1; x++)
        {
            float2 c = i + float2(x, y) + seed;
            float h1 = cwHash12(c + 3.7), h2 = cwHash12(c + 11.3), h3 = cwHash12(c + 27.1), h4 = cwHash12(c + 41.9);
            float2 ctr = i + float2(x, y) + float2(cwHash12(c), cwHash12(c + 17.3));
            float2 w = ctr * CELL;
            float gather = smoothstep(0.3, 0.7, cwNoise(w * 0.09 + seed * 3.1));
            if (h1 < cwRockZone(w) * density * gather)
            {
                float2 r = (ctr - p) * CELL; // metres from its centre
                float a = h2 * 6.2831853, cs = cos(a), sn = sin(a);
                r = float2(cs * r.x - sn * r.y, sn * r.x + cs * r.y) * float2(1.0, lerp(1.0, 1.8, h3));
                float rad = lerp(sizeMin, sizeMax, h4 * h4); // mostly smaller ones, a few big
                r += (float2(cwNoise(r * 1.1 + c * 7.3), cwNoise(r * 1.1 + c * 3.1 + 9.0)) - 0.5) * rad * 0.25; // uneven faces
                float H = rad * lerp(0.45, 0.85, h3);
                float hb = H * (1.0 + 0.3 * (dot(r, float2(cos(h4 * 9.0), sin(h4 * 9.0))) / rad));       // sloping top
                [unroll] for (int k = 0; k < 6; k++)
                {
                    float ang = a + float(k) * 1.0472 + (cwHash12(c + float(k) * 5.1) - 0.5) * 0.7;
                    float reach = rad * lerp(0.75, 1.1, cwHash12(c + float(k) * 3.3));
                    float steep = lerp(0.9, 2.4, cwHash12(c + float(k) * 9.7)) * H / rad;
                    hb = min(hb, (reach - dot(r, float2(cos(ang), sin(ang)))) * steep);
                }
                h = max(h, hb - 0.12 * H);
            }
        }
    return h;
}

float cwBoulders(float2 xz)
{
    return max(cwBoulderLayer(xz, 3.6, 0.0, 0.9, 2.3, 0.7), cwBoulderLayer(xz, 1.4, 31.0, 0.25, 0.7, 0.9));
}

// jointed slabs: returns the distance to the nearest cell border (F2 - F1); rel = offset from the nearest
// cell point, id = that cell (for its own tilt and height)
float cwJointCell(float2 p, out float2 rel, out float2 id)
{
    float2 i = floor(p), f = frac(p);
    float d1 = 8.0, d2 = 8.0; rel = 0; id = 0;
    [unroll] for (int y = -1; y <= 1; y++)
        [unroll] for (int x = -1; x <= 1; x++)
        {
            float2 g = float2(x, y);
            float2 r = g + float2(cwHash12(i + g + 5.1), cwHash12(i + g + 23.9)) - f;
            float d = dot(r, r);
            if (d < d1) { d2 = d1; d1 = d; rel = -r; id = i + g; } else if (d < d2) d2 = d;
        }
    return sqrt(d2) - sqrt(d1);
}

// terraces: limestone weathers into stacked beds with short risers
float cwBeds(float h)
{
    float t = h / 0.28;
    return (floor(t) + smoothstep(0.3, 0.7, frac(t))) * 0.28;
}

// height of rock above the sandy floor at a water-space point (0 = no rock) — the full procedural shape
float cwRockAnalytic(float2 xz)
{
    float h = 0.0;
    float zone = cwRockZone(xz);
    [branch] if (zone > 0.0)
    {
        float base = zone * 1.4 * smoothstep(0.5, 0.75, cwFbm2(xz * 0.06 + 7.3));
        // joints: the platform is broken into polygonal slabs (~2 m) along narrow cracks; each slab is tilted and
        // lifted or sunk a little on its own, so the platform steps at the joints instead of draping smoothly
        float2 jq = xz * 0.55 + (float2(cwNoise(xz * 0.3), cwNoise(xz * 0.3 + 5.0)) - 0.5) * 0.8;
        float2 rel, id;
        float jointEdge = cwJointCell(jq, rel, id);
        float ta = cwHash12(id + 1.3) * 6.2831853;
        float slab = (cwHash12(id + 7.9) - 0.5) * 0.3 + dot(rel, float2(cos(ta), sin(ta))) * lerp(0.05, 0.18, cwHash12(id + 19.1));
        float ledge = max(cwBeds(base) + slab * saturate(base * 5.0), 0.0) * step(0.02, base);
        ledge = max(ledge - 0.18 * (1.0 - smoothstep(0.04, 0.16, jointEdge)) * saturate(ledge * 3.0), 0.0);
        h = max(ledge, cwBoulders(xz));
        // weathered surface: ridged bumps give facets and edges rather than soft mounds
        float ridge = 1.0 - abs(2.0 * cwNoise(xz * 0.8 + 2.0) - 1.0);
        h = max(h + (0.10 * ridge - 0.05 + 0.06 * (cwNoise(xz * 2.3) - 0.5)) * saturate(h * 4.0), 0.0);
    }
    return h;
}

// the baked rock heights: _RockArea.xy = centre (water space), .z = size (m); faded out at the edge
sampler2D _RockTex;
float4 _RockArea;
float cwRock(float2 xz)
{
    float2 uv = (xz - _RockArea.xy) / max(_RockArea.z, 1e-3) + 0.5;
    float edge = saturate(min(min(uv.x, uv.y), min(1.0 - uv.x, 1.0 - uv.y)) * 40.0);
    return tex2Dlod(_RockTex, float4(uv, 0, 0)).r * edge;
}

// 0..1 concavity of the rock around a point: crevices, joints, gaps between boulders. The bake has mipmaps, so
// mip 2 is the height averaged over ~40 cm; where the rock sits below that average, it is in a hollow.
float cwRockCavity(float2 xz, float c)
{
    float2 uv = (xz - _RockArea.xy) / max(_RockArea.z, 1e-3) + 0.5;
    float edge = saturate(min(min(uv.x, uv.y), min(1.0 - uv.x, 1.0 - uv.y)) * 40.0);
    return saturate((tex2Dlod(_RockTex, float4(uv, 0, 2)).r * edge - c) * 5.0);
}

// Stamps (ClearwaterStamp, baked over _StampArea: xy = centre in water space, z = size, w = 1 when there are any):
// R = top of "raise ground" brushes, G = top of "carve ground" brushes, B = top of obstacles (props in the water),
// each a height in water space; -50 / +50 / -50 where there is none.
sampler2D _StampTex;
float4 _StampArea;

// x = depth of the ground (the coast, its relief, rocks, raise/carve brushes); y = the depth the waves feel (the
// ground, or an obstacle's top where one stands higher)
float2 cwFloorDepth2(float2 xz)
{
    float d = cwFloorBaseDepth(cwShoreU(xz)) + 0.30 * (cwNoise(xz * 0.22) - 0.5) + 0.10 * (cwNoise(xz * 0.9 + 7.0) - 0.5) - cwRock(xz);
    float dw = d;
    [branch] if (_StampArea.w > 0.0)
    {
        float2 uv = (xz - _StampArea.xy) / _StampArea.z + 0.5;
        if (all(uv > 0.0) && all(uv < 1.0))
        {
            float3 s = tex2Dlod(_StampTex, float4(uv, 0, 0)).rgb;
            d = -max(min(-d, s.g), s.r);
            dw = min(d, max(-s.b, 0.02)); // an obstacle leaves at least 2 cm of water for the waves (no dry ring round it)
        }
    }
    return float2(d, dw);
}
inline float cwFloorDepth(float2 xz) { return cwFloorDepth2(xz).x; }

// weathered granite: pale grey with a faint warm cast, broad mottling, and the salt-and-pepper grain of its
// crystals; ledges show faint bedding. fp = metres per pixel (the grain fades out before it would alias).
// Lichen, the wet band and cavity darkening depend on height / normal and are added by the shading.
float3 cwRockAlbedo(float2 p, float h, float fp)
{
    float n1 = 0.67 * cwNoise(p * 0.6) + 0.33 * cwNoise(p * 1.25 + 17.1), n2 = cwNoise(p * 2.7 + 9.0);
    float3 c = lerp(float3(0.36, 0.36, 0.355), float3(0.57, 0.56, 0.54), n1) * lerp(0.8, 1.1, n2);
    c = lerp(c, c * float3(1.05, 0.99, 0.94), smoothstep(0.55, 0.75, cwNoise(p * 0.35 + 31.0)));  // warmer patches
    // hairline fractures (~40 cm network), antialiased by the pixel footprint and faded out with distance
    // (the cell searches below are skipped once their detail has faded: most rock pixels are far or under water)
    float cfp = fp * 2.5;
    [branch] if (cfp < 0.05)
    {
        float2 crel, cid;
        float2 cq = p * 2.5 + (float2(cwNoise(p * 1.7), cwNoise(p * 1.7 + 4.0)) - 0.5) * 0.9;
        float ce = cwJointCell(cq, crel, cid);
        float crackM = step(0.6, cwHash12(cid + 2.2)) * smoothstep(0.45, 0.65, cwNoise(p * 1.1 + 70.0)) * saturate(1.5 - cfp * 30.0);
        c *= lerp(1.0, lerp(0.6, 1.0, smoothstep(0.0, 0.02 + 1.5 * cfp, ce)), crackM);
    }
    c *= lerp(1.0, 0.68, smoothstep(0.6, 0.8, cwNoise(p * 0.9 + 50.0)));                          // dark weathering stains
    c *= lerp(0.94, 1.03, 0.5 + 0.5 * sin(h * 22.0 + 3.0 * n1));                                   // faint bedding
    [branch] if (fp < 0.005)
    {
        float pr; float grain = cwCellF1Rock(p * 140.0, pr);                                       // crystals, a few mm
        float g = pr < 0.3 ? 0.72 : (pr > 0.88 ? 1.12 : 1.0);
        c *= lerp(1.0, lerp(1.0, g, smoothstep(0.5, 0.25, grain)), saturate(2.0 - fp * 400.0));
    }
    return c * c * 1.3; // to linear-ish, like the pebble texture
}

// small-scale bumps of the rock surface as a slope (dh/dx, dh/dz), for shading only
float2 cwRockDetailSlope(float2 p)
{
    const float e = 0.03;
    float2 q1 = p * 1.3, q2 = p * 4.1 + 3.0;
    float c1 = cwNoise(q1), c2 = cwNoise(q2);
    float2 g1 = float2(cwNoise(q1 + float2(e, 0)) - c1, cwNoise(q1 + float2(0, e)) - c1) / e;
    float2 g2 = float2(cwNoise(q2 + float2(e, 0)) - c2, cwNoise(q2 + float2(0, e)) - c2) / e;
    return g1 * 1.3 * 0.12 + g2 * 4.1 * 0.03;
}

// dxdx / dxdy = screen derivatives of x, passed in so callers can use this inside dynamic branches
float3 cwPebbles(float2 x, float2 dxdx, float2 dxdy, float sc, out float hgt)
{
    float2 uv = x / (float2(0.78, 0.78) * sc);
    float2 dx = dxdx / (0.78 * sc), dy = dxdy / (0.78 * sc);
    float k = cwNoise(x * 0.85);
    float l = k * 8.0; float ia = floor(l), f = frac(l);
    float2 oa = sin(float2(3.0, 7.0) * ia), ob = sin(float2(3.0, 7.0) * (ia + 1.0));
    float3 a = tex2Dgrad(_Peb, uv + oa, dx, dy).rgb, b = tex2Dgrad(_Peb, uv + ob, dx, dy).rgb;
    float s = dot(a - b, float3(1, 1, 1));
    float m = smoothstep(0.2, 0.8, f - 0.1 * s);
    // coarse luminance as pseudo-height (pale stone tops, dark gaps)
    float3 ca = tex2Dgrad(_Peb, uv + oa, dx * 6.0, dy * 6.0).rgb, cb = tex2Dgrad(_Peb, uv + ob, dx * 6.0, dy * 6.0).rgb;
    hgt = dot(lerp(ca, cb, m), float3(0.3, 0.55, 0.15));
    return lerp(a, b, m);
}

// bottom made of zones: fine pebbles, coarse cobbles, and sand that fills the gaps first
// dpdx / dpdy = screen derivatives of p (explicit, so this works inside dynamic branches)
float3 cwFloorAlbedo(float2 p, float2 dpdx, float2 dpdy, out float hgt, out float rockM)
{
    float hgt2;
    float3 pf = cwPebbles(p, dpdx, dpdy, 1.0, hgt);
    float3 pc = cwPebbles(p.yx * float2(-1.0, 1.0) + 5.3, dpdx.yx * float2(-1.0, 1.0), dpdy.yx * float2(-1.0, 1.0), 1.7, hgt2);
    float coarse = smoothstep(0.45, 0.62, cwFbm2(p * 0.21 + 40.0));
    float3 alb = lerp(pf, pc, coarse); hgt = lerp(hgt, hgt2, coarse);
    float zone = cwFbm2(p * 0.16 + 3.0) + 0.10 * (cwNoise(p * 2.5) - 0.5);
    float sandM = smoothstep(hgt + 0.02, hgt + 0.16, (zone - 0.46) * 1.6);
    float fp = max(length(dpdx), length(dpdy)); // metres per pixel
    float marks = lerp(0.5, 0.5 + 0.5 * sin(dot(p, float2(0.93, 0.37)) * 16.0 + 3.0 * cwNoise(p * 0.8)), saturate(2.0 - fp * 8.0));
    float grain = lerp(0.5, cwNoise(p * 40.0), saturate(2.0 - fp * 60.0));
    float3 sand = float3(0.60, 0.55, 0.44) * (0.82 + 0.22 * grain + 0.10 * marks);
    sand = sand * sand * 1.4; // to linear-ish, matching the texture
    alb = lerp(alb, sand, sandM); hgt = lerp(hgt, 0.42 + 0.05 * marks, sandM);
    // rock, with a band of gathered pebbles and shadow around its foot
    float rh = cwRock(p);
    rockM = smoothstep(0.03, 0.12, rh);
    alb *= lerp(1.0, 0.8, smoothstep(0.0, 0.03, rh) * (1.0 - rockM));
    hgt = lerp(hgt, 0.6, rockM);
    alb = lerp(dot(alb, float3(0.3, 0.55, 0.15)).xxx, alb, 0.8) * float3(1.10, 1.0, 0.86);
    // large-scale variation: sun-bleached patches, darker weedy hollows, a hint of olive film
    float big = cwNoise(p * 0.45) * 0.65 + cwNoise(p * 1.3 + 3.1) * 0.35;
    float weed = smoothstep(0.55, 0.85, cwNoise(p * 0.32 + 11.0));
    alb *= lerp(0.62, 1.22, big);
    alb = lerp(alb, alb * float3(0.55, 0.62, 0.40), weed * 0.7);
    alb = lerp(float3(0.30, 0.29, 0.27), pow(alb, float3(1.2, 1.2, 1.2)), 0.72) * 0.6;
    // the rock keeps its own colour (the grading above is for the loose floor)
    float3 rockCol = alb; float cav = 0.0;
    [branch] if (rh > 0.0)
    {
        cav = cwRockCavity(p, rh);
        rockCol = cwRockAlbedo(p, rh, fp * CW_ROCK_DETAIL_FP_SCALE) * 0.75 * lerp(1.0, 0.45, cav);
    }
    return lerp(alb, rockCol, max(rockM, smoothstep(0.0, 0.05, rh) * cav));
}

inline float3 cwSunT(float3 sun) { return refract(-sun, float3(0, 1, 0), 1.0 / CW_IOR); }

// floor normal (JS space) from the floor shape, plus the rock's small bumps where there is rock
float3 cwFloorNormal(float2 p, float rockM)
{
    const float e = 0.07;
    float d0 = cwFloorDepth(p);
    float2 s = float2(cwFloorDepth(p + float2(e, 0)) - d0, cwFloorDepth(p + float2(0, e)) - d0) / e; // floor y = -depth, so the normal leans along +d depth
    [branch] if (rockM > 0.0) s -= rockM * cwRockDetailSlope(p);
    return normalize(float3(s.x, 1.0, s.y));
}

// how the refracted sun falls on a (rock) face under water, relative to flat sand: 1 on flat ground
inline float cwUnderSunShade(float3 n, float3 sun)
{
    float3 toSun = -cwSunT(sun);
    return saturate(dot(n, toSun)) / toSun.y;
}

// algae on rock below the waterline
inline float3 cwAlgae(float3 alb, float rockM) { return lerp(alb, alb * float3(0.62, 0.78, 0.5), rockM * 0.6); }

// caustics texture coordinate for a floor point
// (relief: nudge the lookup by pseudo height along the sun path; darkens gaps a little)
float2 cwCausUV(float2 FP, float hgt, float3 sun)
{
    float3 sunT = cwSunT(sun);
    return (FP - cwCausShift(sun, _Depth) + sunT.xz / (-sunT.y) * (hgt - 0.35) * 0.05) / _PatchSize;
}

// radiance leaving a submerged floor point; caus = the caustics texture sampled at cwCausUV
// (the caller samples it: implicitly with a LOD bias of 1 like the demo, or with explicit gradients in a branch)
float3 cwFloorRadianceUnder(float2 FP, float depthHere, float hgt, float3 alb, float3 sun, float3 caus, float sunShade)
{
    float3 SUN = cwSunColor();
    float3 sunT = cwSunT(sun);
    float Ts = 1.0 - cwFresnel(sun.y, CW_IOR);
    // touch ripples focus light too: first-order lensing from the local curvature where the sun ray entered
    float2 S = FP - sunT.xz * depthHere / (-sunT.y);
    float lap = tex2Dlod(_Rip, float4((S - _RipCenter.xy) / _RipSize + 0.5, 0, 0)).a;
    caus *= clamp(1.0 / (1.0 + 0.12 * depthHere * lap), 0.45, 3.0);
    float ao = lerp(0.55, 1.0, smoothstep(0.08, 0.42, hgt));
    float3 Esun = SUN * Ts * exp(-SIG_T * depthHere / (-sunT.y)) * caus * (-sunT.y) * lerp(0.75, 1.0, ao) * sunShade;
    float3 Esky = cwSkyIrr() * exp(-(SIG_A + 0.4 * SIG_S) * depthHere * 1.25) * ao;
    return alb / CW_PI * (Esun + Esky);
}

// light scattered toward the viewer along a water path of length s (direction tr, away from the viewer),
// at a representative depth depthHere
float3 cwInscatter(float depthHere, float s, float3 tr, float3 sun)
{
    float3 SUN = cwSunColor();
    float3 sunT = cwSunT(sun);
    float Ts = 1.0 - cwFresnel(sun.y, CW_IOR);
    float3 Tv = exp(-SIG_T * s);
    float cosS = dot(sunT, -tr);
    float g = 0.8; float ph = (1.0 - g * g) / (4.0 * CW_PI * pow(1.0 + g * g - 2.0 * g * cosS, 1.5));
    float3 Lmid = SUN * Ts * exp(-SIG_T * depthHere * 0.5 / (-sunT.y)) * (ph + 0.02) + cwSkyIrr() * exp(-SIG_A * depthHere * 0.6) / (4.0 * CW_PI);
    return SIG_S / SIG_T * Lmid * (1.0 - Tv) * 3.2;
}

#endif
