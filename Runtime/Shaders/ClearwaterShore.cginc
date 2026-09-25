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
float _ShoreWaves; // 1 = waves roll in, break and run up the beach; 0 = still water at the shore (a lake, a pond)

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

// run-up over one wave, 0..1: the surge climbs quickly after the break, then drains slowly until the next one
float cwRunup(float ph)
{
    float p = ph < 0.0 ? ph + 6.2831853 : ph; // 0..2pi from the break
    return p < 0.9 ? smoothstep(0.0, 0.9, p) : pow(saturate(1.0 - (p - 0.9) / 3.8), 1.7);
}

// height of the water sheet on the beach (above still water) at time t; v = distance along the shore. The front
// is not a contour line: it pushes ahead in lobes and fingers a few tens of centimetres long that shift from wave
// to wave.
float cwBeachLevel(float t, float2 xz, float v)
{
    float2 s = cwSwashState(t + cwSwashJitter(v));
    float lobes = 0.05 * (cwNoise(xz * float2(1.1, 0.7) + float2(t * 0.03, t * 0.07)) - 0.5)
                + 0.02 * (cwNoise(xz * float2(4.0, 2.5) - t * 0.35) - 0.5);
    return _SwashHeight * _SwashRunup * s.y * cwSwashAmpVar(v) * (cwRunup(s.x) - 0.12) + lobes;
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
};

// shore waves at a water-space point; floorDepth = the floor's depth there (with relief)
CwShore cwShore(float2 xz, float floorDepth)
{
    CwShore o;
    o.eta = 0; o.breaking = 0; o.phase = 3.14159; o.swash = 0;
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

        // near and on the beach the level follows the run-up sheet instead
        o.swash = 1.0 - saturate((u - uw) / 1.5);
        o.eta = wave;
        [branch] if (o.swash > 0.0) o.eta = lerp(wave, cwBeachLevel(_SwashClock, xz, v), o.swash);
    }
    return o;
}

// wet film left where the swash just drained, 0..1: fresh after a wave covers the point, soaking in over ~2.5 s.
// Soft in both time and height, so the high-water line is a gradient, not a hard contour.
float cwWetFilm(float2 xz, float yAbove)
{
    float film = 0.0;
    [branch] if (_ShoreWaves > 0.5 && yAbove <= _SwashHeight * _SwashRunup * 1.1 + 0.05)
    {
        float v = cwShoreV(xz);
        [loop] for (int k = 0; k < 14; k++)
            film = max(film, exp(-k * 0.2) * smoothstep(-0.02, 0.015, cwBeachLevel(_SwashClock - k * 0.5, xz, v) - yAbove));
    }
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

// foam comes in broken patches along the beach (v = distance along the shore), not one continuous line
inline float cwFoamAlong(float v, float t) { return lerp(0.45, 1.15, cwNoise(float2(v * 0.35, t * 0.09 + 4.0))); }

// light off foam (scene-referred, like the water's other terms)
float3 cwFoamRadiance(float3 n, float3 sun)
{
    return 1.1 / CW_PI * (cwSunColor() * (0.25 + 0.75 * saturate(dot(n, sun))) * saturate(sun.y * 2.0) + cwSkyIrr() * 1.4);
}

#endif
