// Clearwater: the sea floor — shape, pebble/sand albedo, underwater lighting and in-scattering.
// Shared by the water surface (which ray-traces the floor analytically), the walkable seabed mesh and the
// underwater fog, so all three agree. Positions are water-space JS coordinates (see ClearwaterCommon.cginc).
#ifndef CLEARWATER_FLOOR_INCLUDED
#define CLEARWATER_FLOOR_INCLUDED

#include "ClearwaterCommon.cginc"

sampler2D _Peb, _Caus, _Rip;
// The baked data (rock, stamps, user terrain, the shore's exposure) share one sampler, and the bed look's height
// another: the water shader would run past the 16 samplers a pixel shader has with one each. (The light volumes read
// their textures with the first too: the include below declares it, under the name it gives its own.)
#define sampler_UdonLightVolume cw_linear_clamp_sampler
#include "ThirdParty/LightVolumes.cginc"
SamplerState cw_trilinear_repeat_sampler;
// The bed look (ClearwaterBedLook, copied onto the materials by the coast; the defaults are the pebbles):
//  _Peb the colour texture, _BedHeight its height (when _BedHasHeight; else guessed from the colour's brightness),
//  one tile over _BedTile m; _BedCoarse = share of the same texture at 1.7x the size in patches; _BedSandFill = sand
//  gathered between the stones (_BedSandColor); _BedSandBed = 1: the bed is sand itself (ripples form all over);
//  _BedRipple = ripple marks where there is sand; _BedWeed = olive weed film; then the colour: _BedSat, _BedTint,
//  _BedVar (large patches of lighter and darker), _BedGrade (the pebbles' muted grade), _BedBright.
//  _BedMean = its average colour (for what is too far or too blurred to texture).
Texture2D _BedHeight;
float _BedTile, _BedHasHeight, _BedCoarse, _BedSandFill, _BedSandBed, _BedRipple, _BedWeed, _BedSat, _BedVar, _BedGrade, _BedBright;
float4 _BedSandColor, _BedTint, _BedMean;
float4 _RipCenter;
float _PatchSize, _Depth, _RipSize, _SunIntensity;

// The coast, baked by the editor (ClearwaterCoastBake):
//  _CoastTex      RG = shore coordinates over _CoastArea (xy = centre in water space, z = size in m):
//                 u = signed distance from the shore line (+ towards the sea), v = distance along it
//  _CoastFarTex   the same, coarser, over the whole sea (_CoastFarArea; z = 0: none, the fine field is extended),
//                 so the coast drawn far outside the walkable area shapes the distant shore too
//  _CoastProfile  the cross-section as a table over u in [_CoastProfileU.x, .y]: R = depth of the relief-free
//                 floor, G = seconds for a wave at u to reach the waterline; _CoastProfileU.z = the waterline's u
sampler2D _CoastTex, _CoastProfile; // (_CoastFarTex and cwCoastField: ClearwaterCommon, for the distant land too)
float4 _CoastTex_TexelSize, _CoastProfile_TexelSize, _CoastArea, _CoastProfileU;

// The pools (ClearwaterPool, each its own water) cut through the sea's water and ground: _PoolMask over _PoolMaskArea
// (xy = world x, z of its corner, z = size in m, w = 1 when there are pools; the sea's materials only) holds, per
// texel, R = the highest pool surface there and G = the lowest pool floor (world y). Read texel by texel (no sampler).
Texture2D _PoolMask;
float4 _PoolMask_TexelSize, _PoolMaskArea;
// A pool's own materials: _BodyArea = its footprint (world x, z min, x, z max), _BodyFloor.x = its floor (world y),
// .w = 1 (0: the sea's materials). Is a world point in its water (or above it)?
float4 _BodyArea, _BodyFloor;
bool cwInBody(float3 w) { return all(w.xz >= _BodyArea.xy) && all(w.xz <= _BodyArea.zw) && w.y > _BodyFloor.x; }
bool cwInPool(float3 wpos)
{
    [branch] if (_PoolMaskArea.w <= 0.0) return false;
    float2 uv = (wpos.xz - _PoolMaskArea.xy) / _PoolMaskArea.z;
    if (any(uv < 0.0) || any(uv >= 1.0)) return false;
    float2 s = _PoolMask.Load(int3(uv * _PoolMask_TexelSize.zw, 0)).rg;
    return wpos.y < s.x && wpos.y > s.y;
}

// seen through the moving water surface the finest rock detail (cracks, crystals) is lost in refraction and
// caustics; the water shader defines this > 1 so that detail fades out (and is skipped) closer to the viewer
#ifndef CW_ROCK_DETAIL_FP_SCALE
#define CW_ROCK_DETAIL_FP_SCALE 1.0
#endif

static const float3 SIG_A = float3(0.40, 0.074, 0.088);
static const float3 SIG_S = float3(0.028, 0.052, 0.068);
static const float3 SIG_T = SIG_A + SIG_S;

// the light that shades (none indoors): the time of day's (the sun, or the moon at night; Sun intensity scales it as
// against its 6), or the fixed sky's sun
// (the sky of the time of day: none while the sun, or the moon, is behind the distant headland round the horizon)
inline float3 cwSunColor()
{
    float3 c = (CW_TOD ? cwKeyColor() * (_SunIntensity / 6.0) : float3(1.0, 0.90, 0.74) * _SunIntensity) * (1.0 - _Indoor);
    [branch] if (CW_TOD && _Udon_CWKey.y < 0.08) c *= 1.0 - cwHeadlandCover(cwSun(), 0.003);
    return c;
}
// the light from the whole sky on a level surface (indoors: from the room, the probe's broadest look upward)
inline float3 cwSkyIrr()
{
    [branch] if (_Indoor > 0.5) return cwRoom(float3(0, 1, 0), 1.0) * CW_PI;
    return cwAmbientIrr();
}

// VRC Light Volumes (RED_SIM's, MIT: its shader include, as it comes, in ThirdParty/, included at the top). In a
// world that has them, their additive volumes and point light volumes light the ground, the floor under the water and
// the foam too, as they light the avatars there: lamps, a fire, light that is the same at any hour. Not the other
// volumes: they hold the light of the hour they were baked at, where the sky of the time of day is the light here.
// Without them (_UdonLightVolumeEnabled 0: the world has no Light Volume Manager, or the package is not there) none of
// it runs.
// Buildings (Clearwater Sky's Sky Light Volumes: their boxes, as world-to-box matrices onto -0.5..0.5) keep their
// volumes to themselves: in a box the ground takes no additive volume (there they hold the sky, which it has already),
// and nowhere a lamp inside one (its light would come through the walls: the volumes do not know them).
uniform float4x4 _Udon_CWBuildings[4];
uniform float _Udon_CWBuildingCount;
bool cwInBuilding(float3 wpos)
{
    uint n = min((uint)_Udon_CWBuildingCount, 4u);
    [loop] for (uint i = 0; i < n; i++)
    {
        float3 b = mul(_Udon_CWBuildings[i], float4(wpos, 1.0)).xyz;
        if (all(abs(b) <= 0.5)) return true;
    }
    return false;
}
// the volumes' light at a world point (as LightVolumeAdditiveSH, less the buildings')
void cwLightVolumesSH(float3 wpos, out float3 L0, out float3 L1r, out float3 L1g, out float3 L1b)
{
    L0 = 0; L1r = 0; L1g = 0; L1b = 0;
    float4 occlusion = 1;
    [branch] if (!cwInBuilding(wpos)) LV_LightVolumeAdditiveSH(wpos, L0, L1r, L1g, L1b, occlusion);
    uint pointCount = min((uint)_UdonPointLightVolumeCount, VRCLV_MAX_LIGHTS_COUNT);
    uint maxOverdraw = min((uint)_UdonLightVolumeAdditiveMaxOverdraw, VRCLV_MAX_LIGHTS_COUNT);
    uint count = 0;
    [loop] for (uint pid = 0; pid < pointCount && count < maxOverdraw; pid++)
    {
        [branch] if (cwInBuilding(_UdonPointLightVolumePosition[pid].xyz)) continue;
        LV_PointLight(pid, wpos, occlusion, L0, L1r, L1g, L1b, count);
    }
}
// water space's origin in the world, for points given in water space (set by the shader that lights them)
static float3 cwLvOrigin = 0.0;
// their light on a surface facing n (water space) at a world point: irradiance, as cwSkyIrr's
float3 cwLightVolumesIrr(float3 wpos, float3 n)
{
    float3 E = 0.0;
    [branch] if (_UdonLightVolumeEnabled != 0)
    {
        float3 L0, L1r, L1g, L1b;
        cwLightVolumesSH(wpos, L0, L1r, L1g, L1b);
        E = max(LightVolumeEvaluate(cwToJS(n), L0, L1r, L1g, L1b), 0.0) * CW_PI;
    }
    return E;
}
inline float3 cwLightVolumesIrrAt(float3 p, float3 n) { return cwLightVolumesIrr(cwLvOrigin + cwToJS(p), n); } // (p in water space)

// polynomial smooth min / max: blends two lines over a width k with no kink (k/4 rounding at the crossing)
float cwSmin(float a, float b, float k) { float h = max(k - abs(a - b), 0.0) / k; return min(a, b) - h * h * k * 0.25; }
float cwSmax(float a, float b, float k) { float h = max(k - abs(a - b), 0.0) / k; return max(a, b) + h * h * k * 0.25; }

// A baked shore field at a water-space point. Outside its area the coast carries on the way it leaves it: the
// field is extended along its own slope at the edge (exact for a straight coast).
// Shore coordinates (u, v) at a water-space point: the fine field around the walkable area, the coarse one over
// the rest of the sea (blended over the fine area's outer 10%).
float2 cwShoreUV(float2 xz)
{
    float2 q = abs(xz - _CoastArea.xy) / max(_CoastArea.z, 1e-3);
    float edge = 2.0 * max(q.x, q.y); // 0 at the fine area's centre, 1 at its edge
    [branch] if (_CoastFarArea.z <= 0.0 || edge < 0.9)
        return cwCoastField(_CoastTex, _CoastTex_TexelSize, _CoastArea, xz);
    float2 far = cwCoastField(_CoastFarTex, _CoastFarTex_TexelSize, _CoastFarArea, xz);
    [branch] if (edge >= 1.0) return far;
    float2 fine = cwCoastField(_CoastTex, _CoastTex_TexelSize, _CoastArea, xz);
    return lerp(fine, far, smoothstep(0.9, 1.0, edge));
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
Texture2D _RockTex;
float4 _RockArea;
float cwRock(float2 xz)
{
    float2 uv = (xz - _RockArea.xy) / max(_RockArea.z, 1e-3) + 0.5;
    float edge = saturate(min(min(uv.x, uv.y), min(1.0 - uv.x, 1.0 - uv.y)) * 40.0);
    return _RockTex.SampleLevel(cw_linear_clamp_sampler, uv, 0).r * edge;
}

// 0..1 concavity of the rock around a point: crevices, joints, gaps between boulders. The bake has mipmaps, so
// mip 2 is the height averaged over ~40 cm; where the rock sits below that average, it is in a hollow.
float cwRockCavity(float2 xz, float c)
{
    float2 uv = (xz - _RockArea.xy) / max(_RockArea.z, 1e-3) + 0.5;
    float edge = saturate(min(min(uv.x, uv.y), min(1.0 - uv.x, 1.0 - uv.y)) * 40.0);
    return saturate((_RockTex.SampleLevel(cw_linear_clamp_sampler, uv, 2).r * edge - c) * 5.0);
}

// Stamps (ClearwaterStamp, baked over _StampArea: xy = centre in water space, z = size, w = 1 when there are any):
// R = top of "raise ground" brushes, G = top of "carve ground" brushes, B = top of obstacles (props in the water),
// each a height in water space; -50 / +50 / -50 where there is none.
Texture2D _StampTex;
float4 _StampArea;

// User terrain (the coast's own mesh round the walkable area, baked over _UserArea: xy = centre in water space,
// z = size, w = 1 when there is one): R = the mesh's top (a height in water space; past its edge the edge's height
// carried outward), G = how much the ground is the mesh's: 1 on it, falling to 0 across the seam round it, where the
// generated terrain is brought to meet its edge. _UserMean = its average colour (where it cannot be seen on screen).
Texture2D _UserTex;
float4 _UserArea, _UserMean;

// x = the user terrain's height, y = its weight (0 outside it and its seam)
float2 cwUserTerrain(float2 xz)
{
    [branch] if (_UserArea.w <= 0.0) return float2(0.0, 0.0);
    float2 uv = (xz - _UserArea.xy) / _UserArea.z + 0.5;
    if (any(uv <= 0.0) || any(uv >= 1.0)) return float2(0.0, 0.0);
    return _UserTex.SampleLevel(cw_linear_clamp_sampler, uv, 0).rg;
}

// x = depth of the ground (the coast, its relief, rocks, raise/carve brushes, or the user terrain); y = the depth the
// waves feel (the ground, or an obstacle's top where one stands higher)
float2 cwFloorDepth2(float2 xz)
{
    float d0 = cwFloorBaseDepth(cwShoreU(xz));
    // the relief: mounds and hollows on the bed; the beach face the swash washes (and the land above it) is planed
    // to a third of it, else the run-up's edge stranded dry islands on the mounds and pools in the hollows
    float relief = lerp(0.33, 1.0, smoothstep(0.3, 1.0, d0));
    float d = d0 + relief * (0.30 * (cwNoise(xz * 0.22) - 0.5) + 0.10 * (cwNoise(xz * 0.9 + 7.0) - 0.5)) - cwRock(xz);
    float3 s = float3(-50.0, 50.0, -50.0);
    [branch] if (_StampArea.w > 0.0)
    {
        float2 uv = (xz - _StampArea.xy) / _StampArea.z + 0.5;
        if (all(uv > 0.0) && all(uv < 1.0))
        {
            s = _StampTex.SampleLevel(cw_linear_clamp_sampler, uv, 0).rgb;
            d = -max(min(-d, s.g), s.r);
        }
    }
    // the user terrain replaces the ground on it, and the generated ground is brought to its edge round it
    // (the seam's weight starts at 0.96 by the mesh, so the shaders can tell the mesh from it: brought back to 1, so
    // the ground meets the mesh's edge with no step)
    float2 ut = cwUserTerrain(xz);
    d = lerp(d, -ut.x, saturate(ut.y * (1.0 / 0.96)));
    float dw = min(d, max(-s.b, 0.02)); // an obstacle leaves at least 2 cm of water for the waves (no dry ring round it)
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

// the bed look's texture at x, its tiling broken up by shifting it between a few offsets in patches; sc scales its
// size. dxdx / dxdy = screen derivatives of x, passed in so callers can use this inside dynamic branches
float3 cwBedTexture(float2 x, float2 dxdx, float2 dxdy, float sc, out float hgt)
{
    float tile = max(_BedTile, 0.01) * sc;
    float2 uv = x / tile;
    float2 dx = dxdx / tile, dy = dxdy / tile;
    float k = cwNoise(x * 0.85);
    float l = k * 8.0; float ia = floor(l), f = frac(l);
    float2 oa = sin(float2(3.0, 7.0) * ia), ob = sin(float2(3.0, 7.0) * (ia + 1.0));
    float3 a = tex2Dgrad(_Peb, uv + oa, dx, dy).rgb, b = tex2Dgrad(_Peb, uv + ob, dx, dy).rgb;
    float s = dot(a - b, float3(1, 1, 1));
    float m = smoothstep(0.2, 0.8, f - 0.1 * s);
    [branch] if (_BedHasHeight > 0.5)
    {
        // its own height (slightly softened, as the caustics and shading want it)
        hgt = lerp(_BedHeight.SampleGrad(cw_trilinear_repeat_sampler, uv + oa, dx * 2.0, dy * 2.0).r,
                   _BedHeight.SampleGrad(cw_trilinear_repeat_sampler, uv + ob, dx * 2.0, dy * 2.0).r, m);
    }
    else
    {
        // coarse luminance as pseudo-height (pale stone tops, dark gaps)
        float3 ca = tex2Dgrad(_Peb, uv + oa, dx * 6.0, dy * 6.0).rgb, cb = tex2Dgrad(_Peb, uv + ob, dx * 6.0, dy * 6.0).rgb;
        hgt = dot(lerp(ca, cb, m), float3(0.3, 0.55, 0.15));
    }
    return lerp(a, b, m);
}

// the bed (its look set by the bed look): the texture, with a larger scale of it in patches, sand gathered in the
// low parts, ripple marks where there is sand, and the colour grading
// dpdx / dpdy = screen derivatives of p (explicit, so this works inside dynamic branches)
float3 cwFloorAlbedo(float2 p, float2 dpdx, float2 dpdy, out float hgt, out float rockM)
{
    float3 alb = cwBedTexture(p, dpdx, dpdy, 1.0, hgt);
    [branch] if (_BedCoarse > 0.0)
    {
        float hgt2;
        float3 pc = cwBedTexture(p.yx * float2(-1.0, 1.0) + 5.3, dpdx.yx * float2(-1.0, 1.0), dpdy.yx * float2(-1.0, 1.0), 1.7, hgt2);
        float coarse = smoothstep(0.45, 0.62, cwFbm2(p * 0.21 + 40.0)) * _BedCoarse;
        alb = lerp(alb, pc, coarse); hgt = lerp(hgt, hgt2, coarse);
    }
    float zone = cwFbm2(p * 0.16 + 3.0) + 0.10 * (cwNoise(p * 2.5) - 0.5);
    float sandM = smoothstep(hgt + 0.02, hgt + 0.16, (zone - 0.46) * 1.6) * _BedSandFill;
    float fp = max(length(dpdx), length(dpdy)); // metres per pixel
    // ripple marks: crests along the shore that meander, fork and change spacing (the phase is warped by noise),
    // in patches; smoothed out where the swash runs and faint on the dry beach (a single straight sine read as
    // regular stripes)
    float us = cwShoreU(p), du = us - _CoastProfileU.z;
    float ripPh = us * 16.0 + cwFbm2(p * 0.35 + 7.0) * 9.0 + cwNoise(p * 1.1 + 2.0) * 2.5;
    float ripZone = du > 0.0 ? smoothstep(2.0, 6.0, du) : 0.35 * smoothstep(3.0, 8.0, -du);
    float ripA = smoothstep(0.3, 0.7, cwNoise(p * 0.09 + 17.0)) * ripZone * saturate(2.0 - fp * 8.0) * _BedRipple;
    float marks = 0.5 + 0.5 * ripA * sin(ripPh);
    // a bed of sand: the ripples shade and shape all of it
    alb *= 1.0 + 0.24 * (marks - 0.5) * _BedSandBed; hgt += 0.12 * (marks - 0.5) * _BedSandBed;
    float grain = lerp(0.5, cwNoise(p * 40.0), saturate(2.0 - fp * 60.0));
    float3 sand = _BedSandColor.rgb * (0.82 + 0.22 * grain + 0.10 * marks);
    sand = sand * sand * 1.4; // to linear-ish, matching the texture
    alb = lerp(alb, sand, sandM); hgt = lerp(hgt, 0.42 + 0.05 * marks, sandM);
    // rock, with a band of gathered pebbles and shadow around its foot
    float rh = cwRock(p);
    rockM = smoothstep(0.03, 0.12, rh);
    alb *= lerp(1.0, 0.8, smoothstep(0.0, 0.03, rh) * (1.0 - rockM));
    hgt = lerp(hgt, 0.6, rockM);
    alb = lerp(dot(alb, float3(0.3, 0.55, 0.15)).xxx, alb, _BedSat) * _BedTint.rgb;
    // large-scale variation: sun-bleached patches, darker weedy hollows, a hint of olive film
    float big = cwNoise(p * 0.45) * 0.65 + cwNoise(p * 1.3 + 3.1) * 0.35;
    float weed = smoothstep(0.55, 0.85, cwNoise(p * 0.32 + 11.0));
    alb *= lerp(1.0 - 0.38 * _BedVar, 1.0 + 0.22 * _BedVar, big);
    alb = lerp(alb, alb * float3(0.55, 0.62, 0.40), weed * _BedWeed);
    alb = lerp(alb, lerp(float3(0.30, 0.29, 0.27), pow(max(alb, 0.0), float3(1.2, 1.2, 1.2)), 0.72) * 0.6, _BedGrade);
    alb *= _BedBright;
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

// Seen from under the water (set by the shader that draws it: the underwater fog, the water's view from below, the
// seabed seen from below), the light in the water never falls below a deep blue-green glow: after dusk, with no sun
// and no moon, it was black all round, nothing to tell the floor, the slope or an avatar by. With it the water glows
// a dark blue-green in the distance (some 10/255 on screen: only just out of black, for it to stay night) and what is
// near stands dark against it. It is under half the day sky's light in the water; the day's sun there is many times
// more, so by day nothing changes. The floor gets a third of it. From above the water the dark stays as it is.
// ClearwaterSky's Underwater Glow scales it (_Udon_CWNight.w: 1 as here, 0 none; without the sky of the time of day,
// whose fixed sky is always day, 0).
static bool cwUnderView = false;
#define CW_UNDER_GLOW float3(0.04, 0.20, 0.26)
#define CW_UNDER_GLOW_FLOOR float3(0.012, 0.06, 0.08)

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
    // the net of light needs depth to form: under a few centimetres of water the waves barely focus the sun, so it
    // fades to even light at the water edge (as on the wet sand beside it) instead of ending in a line
    caus = lerp(1.0, caus, smoothstep(0.01, 0.35, depthHere) * CW_WAVE); // (calmer water, fainter lines)
    float ao = lerp(0.55, 1.0, smoothstep(0.08, 0.42, hgt));
    float3 Esun = SUN * Ts * exp(-SIG_T * depthHere / (-sunT.y)) * caus * (-sunT.y) * lerp(0.75, 1.0, ao) * sunShade;
    float3 Esky = cwSkyIrr() * exp(-(SIG_A + 0.4 * SIG_S) * depthHere * 1.25) * ao;
    [branch] if (cwUnderView) Esky = max(Esky, CW_UNDER_GLOW_FLOOR * _Udon_CWNight.w * ao);
    // the light volumes' lamps, through the water as the sky's light comes (a lamp under the water too: near enough)
    float3 Elv = cwLightVolumesIrrAt(float3(FP.x, -depthHere, FP.y), float3(0, 1, 0)) * exp(-(SIG_A + 0.4 * SIG_S) * depthHere * 1.25) * ao;
    // sand under the last few centimetres of water is as dark as the wet sand just above the waterline (its pores are
    // full of water too), so the water edge does not show as a step in brightness; deeper, the floor as before
    float wet = lerp(0.65, 1.0, smoothstep(0.0, 0.25, depthHere));
    return alb / CW_PI * (Esun + Esky + Elv) * wet;
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
    [branch] if (cwUnderView) Lmid = max(Lmid, CW_UNDER_GLOW * _Udon_CWNight.w / (4.0 * CW_PI));
    return SIG_S / SIG_T * Lmid * (1.0 - Tv) * 3.2;
}

#endif
