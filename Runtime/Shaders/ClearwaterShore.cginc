// Clearwater: the shoreline — swell rolling in over the shallows, breaking, running up the beach and draining,
// in time with the wave sound. Shared by the water surface (height, foam) and the seabed (wet sand, foam left
// behind).
//
// Timing comes from _SwashTrack, built from the breaks detected in the shore audio loop
// (Tools/detect_wave_breaks.js): per texel cos/sin of the wave phase (0 = a crest reaching the waterline, when
// the sound breaks) and the wave's strength. _SwashClock is the audio source's playback time.
// A crest seen at distance u from the shore is the one that will reach the waterline cwTravelTime(u) seconds
// later, travelling at the shallow-water speed sqrt(g d) over the floor profile — so crests slow down, bunch up,
// grow (Green's law) and sharpen as the water shallows, then break where they get too tall for the depth.
// u and v (along the shore) come from the baked coast (cwShoreUV), the travel time from the baked cross-section.
#ifndef CLEARWATER_SHORE_INCLUDED
#define CLEARWATER_SHORE_INCLUDED

#include "ClearwaterFloor.cginc"

sampler2D _SwashTrack;
float _SwashClock, _SwashLoop, _SwashHeight, _SwashRunup;
// the breaks one by one (r = time in the loop, g = strength), and per ~0.09 s of the loop the index of the last one
sampler2D _SwashBreaks, _SwashIdx;
float _SwashCount;
float _SwashSlope; // rise per metre of the beach face over the swash zone (baked from the cross-section)
float _ShoreWaves; // 1 = waves roll in, break and run up the beach; 0 = still water at the shore (a lake, a pond)
float _FoamRelief; // m: how high the densest foam stands (its density is its height, lit through the normal)
float _FoamLift;   // m: how far the whitewater stands up out of the water (the run-up's front lip, the breaking roller)

#define CW_G 9.81

// where the relief-free floor meets the still water
inline float cwWaterlineU() { return _CoastProfileU.z; }

// seconds until a crest now at u reaches the waterline (baked with the cross-section)
inline float cwTravelTime(float u) { return cwCoastProfile(u).y; }

// state at the waterline at time t: x = wave phase (-pi..pi, 0 = break), y = strength 0..1
float2 cwSwashState(float t)
{
    float4 s = tex2Dlod(_SwashTrack, float4(t / _SwashLoop, 0.5, 0, 0));
    return float2(atan2(s.y, s.x), s.z);
}

// along the beach (v = distance along the shore), crests arrive a little early or late and vary in strength
inline float cwSwashJitter(float v) { return 0.8 * (cwNoise(float2(v * 0.045, 3.7)) - 0.5) + 0.3 * (cwNoise(float2(v * 0.19, 9.1)) - 0.5) + 0.18 * (cwNoise(float2(v * 0.7, 15.3)) - 0.5); }
inline float cwSwashAmpVar(float v) { return lerp(0.75, 1.1, cwNoise(float2(v * 0.03, 21.0))); }

// ---- the swash: ballistic run-up (the shoreline motion of swash theory, and what timestacks of real beaches show)
// Each broken wave throws a sheet up the beach face at the bore's speed U = sqrt(2 g R) (R = the height it will reach
// above still water); gravity along the slope decelerates it steadily, so it rises fast, slows all the way and turns
// over at the top with no jolt, in t_up = sqrt(2R/g) / slope (~2.7 s for 36 cm on a 1:10 beach: a steeper beach, a
// quicker swash). The backwash starts from rest and drains with a weaker pull (friction, and water soaking into the
// sand), so it is slower than the uprush. Waves come faster than one swash lasts: the sheet is the highest of the last
// few waves' swashes, a new bore running up over the previous backwash, a small one lost in it.
#define CW_SWASH_BACK 0.6   // backwash acceleration, as a share of the uprush's deceleration
#define CW_SWASH_REST -0.04 // m: the sea's level at the foot of the swash between waves (the backwash's set-down)

#define CW_BORES 6 // waves followed: the next two (still coming) and the last four

struct CwSwash
{
    float level; // m above still water of the sheet (its front where this meets the beach)
    float surge; // 0..1, how hard the wave leading it is still running up (1 as the bore arrives, 0 at the top)
    float run;   // m it has come up the beach (level / slope): the foam rides with it
    float amp;   // strength of the wave leading it
};
#define CW_BORE_LOOK 3.0 // s before it arrives that a bore shows, coming in from the sea

// one wave's swash: its height tau seconds after it broke, reaching R at slope sb; up = its surge
float cwSwashZ(float tau, float R, float sb, out float up)
{
    up = 0.0;
    if (tau < 0.0 || R < 1e-3) return -1.0;
    float Tup = sqrt(2.0 * R / CW_G) / sb;
    if (tau < Tup)
    {
        float s = 1.0 - tau / Tup;
        up = s; // (1 at the break, as the bore coming in)
        return R * (1.0 - s * s);
    }
    float d = tau - Tup;
    return R - 0.5 * CW_SWASH_BACK * CW_G * sb * sb * d * d;
}

// one of the waves followed (i: 0, 1 = the next two, still coming; 2.. = the last four) at loop time tt: its front's
// height z above still water (negative: still out at sea), its surge, and how white its bore is (before the edge
// test); false if it is not in play
bool cwSwashWave(int i, float tt, float k, float L, float n, float Hr, float sb, out float z, out float up, out float amp, out float white)
{
    z = -10.0; up = 0.0; amp = 0.0; white = 0.0;
    float idx = k - (i - 2);
    idx -= floor(idx / n) * n;
    float2 b = tex2Dlod(_SwashBreaks, float4((idx + 0.5) / n, 0.5, 0, 0)).rg;
    float tau = tt - b.x;
    tau -= floor(tau / L) * L; // seconds since that break (wrapping round the loop)
    amp = b.y;
    if (tau < 0.5 * L)
    {
        z = cwSwashZ(tau, Hr * b.y, sb, up);
        white = smoothstep(0.0, 0.3, up) * saturate(0.4 + b.y); // (white while it still surges)
        return true;
    }
    if (L - tau < CW_BORE_LOOK)
    {
        // still coming in from the sea at the bore's speed (the same speed its swash starts with at the waterline, so
        // it carries straight on); if it overtakes the backwash's edge out there, the water's edge is its front
        float dt = L - tau;
        z = -sqrt(2.0 * CW_G * Hr * b.y) * dt * sb;
        up = 1.0;
        white = smoothstep(CW_BORE_LOOK, CW_BORE_LOOK - 0.8, dt) * saturate(0.4 + b.y);
        return true;
    }
    return false;
}

// the swash at time t (the audio clock) along the shore at v
CwSwash cwSwashAt(float t, float v)
{
    CwSwash o;
    o.level = CW_SWASH_REST; o.surge = 0.0; o.amp = 0.0;
    float sb = max(_SwashSlope, 0.02);
    float Hr = _SwashHeight * _SwashRunup * cwSwashAmpVar(v);
    float L = max(_SwashLoop, 1.0), n = max(_SwashCount, 1.0);
    float tt = t + cwSwashJitter(v);
    tt -= floor(tt / L) * L;
    float k = tex2Dlod(_SwashIdx, float4((floor(tt / L * 1024.0) + 0.5) / 1024.0, 0.5, 0, 0)).r;
    float z, up, amp, white;
    // the water: the highest of the waves (a new bore merges into the backwash it meets)
    [loop] for (int i = 0; i < CW_BORES; i++)
        if (cwSwashWave(i, tt, k, L, n, Hr, sb, z, up, amp, white))
        {
            if (z > o.level) { o.surge = up; o.amp = amp; }
            o.level = cwSmax(o.level, z, 0.01);
        }
    o.run = o.level / sb;
    return o;
}

// The swash now along the shore at v. It only depends on v, which barely changes between the few points a pixel looks
// at (the surface trace, its slope): callers work it out once per pixel and hand it to cwShoreSw.
inline CwSwash cwSwashNow(float2 suv) { return cwSwashAt(_SwashClock, suv.y); }

// no shore waves: a swash that never comes
CwSwash cwSwashNone()
{
    CwSwash o; o.level = CW_SWASH_REST; o.surge = 0.0; o.run = 0.0; o.amp = 0.0;
    return o;
}

// Wet film: how recently the swash covered height y (above still water) along the shore at v, 1 = covered now,
// fading as the water soaks in (~2.5 s). Worked out exactly from each wave's ballistic path: the moment its sheet
// last came down past y.
float cwSwashFilm(float t, float v, float y)
{
    if (y <= CW_SWASH_REST) return 1.0;
    float sb = max(_SwashSlope, 0.02);
    float Hr = _SwashHeight * _SwashRunup * cwSwashAmpVar(v);
    float L = max(_SwashLoop, 1.0), n = max(_SwashCount, 1.0);
    float tt = t + cwSwashJitter(v);
    tt -= floor(tt / L) * L;
    float k = tex2Dlod(_SwashIdx, float4((floor(tt / L * 1024.0) + 0.5) / 1024.0, 0.5, 0, 0)).r;
    float film = 0.0;
    [loop] for (int j = -1; j <= 3; j++)
    {
        float idx = k - j;
        idx -= floor(idx / n) * n;
        float2 b = tex2Dlod(_SwashBreaks, float4((idx + 0.5) / n, 0.5, 0, 0)).rg;
        float tau = tt - b.x;
        tau -= floor(tau / L) * L;
        float R = Hr * b.y;
        if (tau < 0.5 * L && R > max(y, 1e-3))
        {
            float Tup = sqrt(2.0 * R / CW_G) / sb;
            float tIn = Tup * (1.0 - sqrt(saturate(1.0 - y / R)));                     // reaches y going up
            float tOut = Tup + sqrt(2.0 * (R - y) / (CW_SWASH_BACK * CW_G * sb * sb)); // leaves it draining
            if (tau >= tIn) film = max(film, tau <= tOut ? 1.0 : exp(-0.4 * (tau - tOut)));
        }
    }
    return film;
}

// the front is not a contour line: it pushes ahead in lobes and fingers a few tens of centimetres long that shift
// from wave to wave
inline float cwSwashLobes(float t, float2 xz)
{
    return 0.05 * (cwNoise(xz * float2(1.1, 0.7) + float2(t * 0.03, t * 0.07)) - 0.5)
         + 0.02 * (cwNoise(xz * float2(4.0, 2.5) - t * 0.35) - 0.5);
}

// height of the water sheet on the beach (above still water) at time t; v = distance along the shore
float cwBeachLevel(float t, float2 xz, float v)
{
    return cwSwashAt(t, v).level + cwSwashLobes(t, xz);
}

// peaked shallow-water wave of height H (crest to trough), zero mean; K = crest sharpness
float cwPeaked(float ph, float H, float K)
{
    float K2 = K * K;
    float mean = exp(-K) * (1.0 + K2 * (0.25 + K2 * (1.0 / 64.0 + K2 * (1.0 / 2304.0 + K2 / 147456.0)))); // e^-K I0(K)
    return H * (exp(K * (cos(ph) - 1.0)) - mean) / (1.0 - exp(-2.0 * K));
}

struct CwShore
{
    float eta;      // surface height added by the shore waves (m)
    float breaking; // 0..1, how hard the wave here is breaking
    float phase;    // wave phase here (0 = crest)
    float swash;    // 0..1, weight of the run-up sheet (near and on the beach)
    float lift;     // m of eta that is whitewater standing up (the front lip, the roller): foamy, not clear water
};

// shore waves at a water-space point; floorDepth = the floor's depth there (with relief); sw = the swash along the shore
// here (cwSwashNow, worked out once for the pixel)
CwShore cwShoreSw(float2 xz, float floorDepth, CwSwash sw)
{
    CwShore o;
    o.eta = 0; o.breaking = 0; o.phase = 3.14159; o.swash = 0; o.lift = 0;
    float2 sv = cwShoreUV(xz);
    float u = sv.x, v = sv.y;
    float2 prof = cwCoastProfile(u); // relief-free depth, travel time
    float db = prof.x;
    [branch] if (db <= 2.7 && _ShoreWaves > 0.5) // past the shallows (or no shore waves): open water, the demo's waves only
    {
        float uw = cwWaterlineU();
        float t = _SwashClock + prof.y + cwSwashJitter(v);
        float2 st = cwSwashState(t);
        o.phase = st.x;
        // Green's law shoaling (H ~ d^-1/4), faded out where the shallows end
        float Hs = _SwashHeight * st.y * cwSwashAmpVar(v) * pow(0.2 / max(db, 0.2), 0.25) * smoothstep(2.6, 1.8, db);
        // a wave cannot be taller than ~0.8 of the depth: the rest turns into whitewater
        float D = max(floorDepth, 0.0);
        float Hc = min(Hs, 0.8 * D + 0.02);
        o.breaking = saturate((Hs - 0.8 * D) / max(0.35 * Hs, 1e-3));
        float wave = cwPeaked(st.x, Hc, lerp(0.9, 3.5, saturate(1.0 - db / 1.4)));
        // the broken water rides the front of a breaking crest as a foamy roller, standing up above it
        float crest = smoothstep(-1.0, -0.2, st.x) * exp(-max(st.x, 0.0) * 0.6) * step(-1.0, st.x);
        float roller = _FoamLift * saturate(o.breaking * 1.6) * crest;

        // near and on the beach the level follows the run-up sheet instead
        o.swash = 1.0 - saturate((u - uw) / 1.5);
        o.eta = wave + roller;
        o.lift = roller * (1.0 - o.swash);
        [branch] if (o.swash > 0.0)
        {
            // the sheet's leading edge climbs the beach as a small bore: a white lip that stands up while the surge
            // runs up and melts away as it drains (it rises over the first 2 cm of water, so the edge stays smooth)
            float level = sw.level + cwSwashLobes(_SwashClock, xz);
            float thick0 = level + floorDepth; // the sheet's own thickness over the ground
            // a low rounded swell, highest where the sheet is ~6 cm thick (a hand or two behind its edge) and
            // tapering to nothing at the edge: a gentle slope, not a wall (a steep front refracted the floor into a
            // dark band along the edge)
            float xl = max(thick0, 0.0) / 0.06;
            float lip = _FoamLift * sw.surge * sw.amp * xl * exp(1.0 - xl);
            o.eta = lerp(wave + roller, level + lip, o.swash);
            o.lift = lerp(roller, lip, o.swash);
        }
    }
    return o;
}

// The newest wave while the last one's water still covers the beach (waves come faster than a swash lasts): its
// broken front (a bore) runs in from the sea and on up the beach through that water, a band of white water, until it
// overtakes the water's edge and becomes the edge itself - seen all the way, instead of the sheet turning round all at
// once where it overtakes. 0..1 at shore coordinates suv (u, v); worked out once per pixel, for the foam only (in the
// surface's own height it was inlined at every sample and slowed the whole water shader).
float cwBoreBand(float2 suv)
{
    float sb = max(_SwashSlope, 0.02);
    float Hr = _SwashHeight * _SwashRunup * cwSwashAmpVar(suv.y);
    float L = max(_SwashLoop, 1.0), n = max(_SwashCount, 1.0);
    float tt = _SwashClock + cwSwashJitter(suv.y);
    tt -= floor(tt / L) * L;
    float k = tex2Dlod(_SwashIdx, float4((floor(tt / L * 1024.0) + 0.5) / 1024.0, 0.5, 0, 0)).r;
    float wobble = 0.3 * (cwNoise(float2(suv.y * 0.4, _SwashClock * 0.2)) - 0.5);
    float u = suv.x - wobble, uw = cwWaterlineU();
    // one pass: the water on top, and the two bores whose bands would cover u most (only one wave leads the water, so
    // the best hidden one is always among them)
    float level = CW_SWASH_REST, z, up, amp, white;
    float c1 = 0.0, z1 = -10.0, w1 = 0.0, c2 = 0.0, z2 = -10.0, w2 = 0.0;
    [loop] for (int i = 0; i < CW_BORES; i++)
        if (cwSwashWave(i, tt, k, L, n, Hr, sb, z, up, amp, white))
        {
            level = cwSmax(level, z, 0.01);
            float d = u - (uw - z / sb); // m behind (seaward of) its front
            // (cut off 2.5 m behind: an exponential tail never reaches 0, and any band switches on the foam work)
            float c = smoothstep(-0.05, 0.05, d) * exp(-max(d, 0.0) / 0.7) * smoothstep(2.5, 1.6, d) * white;
            if (c > c1) { c2 = c1; z2 = z1; w2 = w1; c1 = c; z1 = z; w1 = white; }
            else if (c > c2) { c2 = c; z2 = z; w2 = white; }
        }
    // a bore's band shows while it is under the water on top; once it leads, the water's edge is its front
    return max(c1 * smoothstep(0.0, 0.06, level - z1), c2 * smoothstep(0.0, 0.06, level - z2));
}

// the same for a single point, the swash worked out for it
CwShore cwShore(float2 xz, float floorDepth)
{
    CwSwash sw = cwSwashNone();
    [branch] if (_ShoreWaves > 0.5) sw = cwSwashNow(cwShoreUV(xz));
    return cwShoreSw(xz, floorDepth, sw);
}

// wet film left where the swash just drained, 0..1: fresh after a wave covers the point, soaking in over ~2.5 s
// (the lobes of the sheet's front make the high-water line uneven)
float cwWetFilm(float2 xz, float yAbove)
{
    float film = 0.0;
    [branch] if (_ShoreWaves > 0.5 && yAbove <= _SwashHeight * _SwashRunup * 1.1 + 0.05)
        film = cwSwashFilm(_SwashClock, cwShoreV(xz), yAbove - cwSwashLobes(_SwashClock, xz));
    return film;
}

// distance to the nearest jittered cell point, and that point's random value
float cwCellF1(float2 p, out float rnd)
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

// Sea foam, 0..1: organic patches with holes of every size (domain-warped noise), threaded with lace, grained
// with tiny bubbles. As coverage drops the holes grow and merge until only threads are left, the way foam breaks
// up. footprint = metres per pixel: detail fades out before it would alias.
float cwFoam(float2 p, float coverage, float t, float footprint)
{
    float2 w = float2(cwFbm2(p * 1.9 + float2(0.0, t * 0.08)), cwFbm2(p * 1.9 + float2(5.2, 1.3) - t * 0.05));
    float2 q = p + (w - 0.5) * 0.9;
    float c = saturate(coverage * (0.6 + 0.8 * w.x));
    float laceW = saturate(1.5 - footprint * 12.0), fineW = saturate(1.5 - footprint * 18.0);
    float S = cwFbm2(q * 4.5);                                        // patches and holes
    float lace = 1.0 - abs(2.0 * cwFbm2(q * 11.0 + 3.1) - 1.0);       // thin threads
    float field = S + 0.22 * lerp(0.5, lace, laceW);
    float th = lerp(0.9, 0.25, c);
    // a density, not a cut-out: thick in the middle of a patch, thinning to nothing at its edge
    float foam = smoothstep(th - 0.14, th + 0.1, field);
    foam *= lerp(1.0, 0.45 + 0.55 * lace, laceW * (1.0 - 0.5 * foam));   // threads show where it is thin
    float r; float f1 = cwCellF1(p * 38.0, r);                        // tiny bubbles
    foam *= lerp(1.0, lerp(0.4, 1.0, smoothstep(0.1, 0.45, f1 + 0.25 * r)), fineW);
    return foam * smoothstep(0.02, 0.15, c);
}

// The run-up's foam: laid out in shore coordinates (suv = u, v), a little streaked down the slope, riding the sheet
// up the beach and back down with it: run = how far the sheet has come up (m, CwSwash.run), w = how much of that
// applies (the sheet zone). It only ever moves up and down the beach, with the water, and continuously (the swash
// is). The water draws it on the sheet, the seabed where the sheet has just drained off, so it carries on across the
// water's edge.
float cwRunupFoam(float2 suv, float cover, float run, float w, float footprint)
{
    // (u grows seaward: a feature at fixed -(u + run) moves up the beach as the sheet comes up, down as it drains)
    float2 fp = float2(suv.y * 1.4, -(suv.x + run * w) * 0.6);
    return cwFoam(fp, saturate(cover), _SwashClock, footprint) * saturate(cover * 1.6);
}

// foam comes in broken patches along the beach (v = distance along the shore), not one continuous line
inline float cwFoamAlong(float v, float t) { return lerp(0.45, 1.15, cwNoise(float2(v * 0.35, t * 0.09 + 4.0))); }

// light off foam (scene-referred, like the water's other terms)
float3 cwFoamRadiance(float3 n, float3 sun)
{
    return 1.1 / CW_PI * (cwSunColor() * (0.25 + 0.75 * saturate(dot(n, sun))) * saturate(sun.y * 2.0) + cwSkyIrr() * 1.4);
}

// Foam with relief: its density is its height (up to _FoamRelief), so clumps stand up, their sunward sides light
// and their far sides shade, thin foam and the holes sink in, and wet bubble tops catch the sun.
// n = the surface under the foam; g = the density's gradient (per metre); v = towards the viewer.
float3 cwFoamLit(float foam, float2 g, float3 n, float3 sun, float3 v)
{
    float3 nf = normalize(n + float3(-g.x, 0.0, -g.y) * _FoamRelief);
    // foam is white but not a light: kept below clipping, with more of the light coming straight from the sun so
    // the faces turned away from it (the back of a lip or a roller) fall into the sky's bluish shade
    float3 c = 1.1 / CW_PI * (cwSunColor() * (0.1 + 0.9 * saturate(dot(nf, sun))) * saturate(sun.y * 2.0) + cwSkyIrr() * 1.4)
             * 0.8 * (0.75 + 0.3 * foam);
    float3 r = reflect(-sun, nf);
    c += cwSunColor() * pow(saturate(dot(r, v)), 60.0) * 0.35 * foam * saturate(sun.y * 2.0);
    return c;
}

#endif
