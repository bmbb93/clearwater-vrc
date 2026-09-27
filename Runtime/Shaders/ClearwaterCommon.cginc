// Clearwater for VRChat — shared helpers.
// Port of https://github.com/Aureliengmz/clearwater (MIT, (c) Aurélien / Lumaris).
//
// All water math runs in the original demo's coordinate frame ("JS space"): +y up, the default view looks
// toward -z. Unity is left-handed, so JS space = Unity space with z negated; a player facing Unity +z sees
// exactly the demo's framing (sun ahead, shore behind).
#ifndef CLEARWATER_COMMON_INCLUDED
#define CLEARWATER_COMMON_INCLUDED

#define CW_PI 3.14159265359

static const float CW_IOR = 1.3335;

inline float3 cwToJS(float3 v) { return float3(v.x, v.y, -v.z); }

// sun direction (towards the sun), Unity world space -> JS space
float4 _SunDir;

// A pool's own look (ClearwaterPool; the sea leaves them unset, 0): _Calm = how much calmer than the sea its surface
// is (1 - its wave strength); _Indoor = 1 indoors: no sunlight, and the room (the nearest reflection probe, times
// _EnvGain) where the sky would be.
float _Calm, _Indoor, _EnvGain;
#define CW_WAVE (1.0 - _Calm)

// ---- The sky of the time of day (ClearwaterSky, in the scene with Clearwater's own sky and sun), set for every shader at
// once. Without one (a world keeping its own sun and sky) they are all 0 and the shaders keep the fixed sky below.
// World directions (towards the body); colours already scaled by the eye's adaptation to the light of the moment.
sampler3D _Udon_CWSkyLUT;     // the sky's radiance (ClearwaterAtmosphere), per sun elevation divided by its mean
float4 _Udon_CWSun;           // xyz the sun, w the scale that slice of the table is shown at
float4 _Udon_CWMoon;          // xyz the moon, w as for the sun (0: no moon)
float4 _Udon_CWSunColor;      // rgb the sunlight on a surface facing it; w 1 when the sky of the time of day is on
float4 _Udon_CWMoonColor;     // rgb the moonlight; a 1 while the moon is up
float4 _Udon_CWKey;           // xyz the light that shades (the sun, the moon at night), w 1 when it is the moon
float4 _Udon_CWAmbient;       // rgb the whole sky's light on level ground; w the airglow's radiance (night's own faint light)
float4 _Udon_CWStars;         // xyz the celestial pole, w the angle the stars have turned; (their brightness in _Udon_CWNight.x)
float4 _Udon_CWNight;         // x how bright the stars show; y 0..1 night vision (colours fade and turn blue)
float4 _Udon_CWCloudLight;    // rgb the light on the clouds: the sun's as it is a couple of km up (it sets later there), and the moon's
float4 _Udon_CWHorizon;       // rgb the sky low over the horizon, all round it
float4 _Udon_CWBodies;        // the sun's and the moon's directions across the ground (JS space xz, normalised: sun xy, moon zw)
float4 _Udon_CWSlices;        // x the sun's slice of the table, y the moon's (texel-centred coordinates)
#define CW_TOD (_Udon_CWSunColor.w > 0.5)

inline float3 cwSun() { return CW_TOD ? normalize(cwToJS(_Udon_CWKey.xyz)) : normalize(cwToJS(_SunDir.xyz)); }
// the light that shades, as the fixed sky's sun (float3(1.0, 0.90, 0.74) * 6 at its reference, sun 31 degrees up)
inline float3 cwKeyColor() { return _Udon_CWKey.w > 0.5 ? _Udon_CWMoonColor.rgb : _Udon_CWSunColor.rgb; }

// the table's coordinates (ClearwaterAtmosphere: ViewCoord, AzCoord; its slice, SunCoord, from ClearwaterSky)
#define CW_SKY_AZ 64.0
#define CW_SKY_EL 64.0
// the sky lit by a body across the ground at bh (JS space xz, normalised), seen along d, from the table's slice z
// (texel-centred) shown at scale
float3 cwSkyTable(float3 d, float2 bh, float z, float scale)
{
    float e = asin(saturate(d.y));
    float2 dh = normalize(d.xz + float2(1e-6, 0.0));
    float az = acos(clamp(dot(dh, bh), -1.0, 1.0));
    float2 c = float2(sqrt(az / CW_PI), sqrt(e / (0.5 * CW_PI)));
    const float2 n = float2(CW_SKY_AZ, CW_SKY_EL);
    return tex3Dlod(_Udon_CWSkyLUT, float4((c * (n - 1.0) + 0.5) / n, z, 0.0)).rgb * scale;
}
// the air's own light along d (JS space): the sunlit sky, the moonlit one, and the airglow
// (either left out while it is lost in the other's: ClearwaterSky gives it no scale then)
float3 cwAtmosphere(float3 d)
{
    float3 c = _Udon_CWAmbient.w * float3(0.35, 0.45, 0.75);
    [branch] if (_Udon_CWSun.w > 0.0) c += cwSkyTable(d, _Udon_CWBodies.xy, _Udon_CWSlices.x, _Udon_CWSun.w);
    [branch] if (_Udon_CWMoon.w > 0.0) c += cwSkyTable(d, _Udon_CWBodies.zw, _Udon_CWSlices.y, _Udon_CWMoon.w);
    return c;
}
// the colour of the far haze at the horizon along d (JS space): the far water and land fade into it
float3 cwHazeColor(float3 d, float3 sun)
{
    float3 c;
    [branch] if (CW_TOD) c = cwAtmosphere(normalize(float3(d.x, 0.0, d.z) + float3(0.0, 1e-3, 0.0)));
    else
    {
        float muh = max(dot(normalize(float3(d.x, 0.0, d.z) + 1e-5), sun), 0.0);
        c = float3(0.60, 0.71, 0.82) + float3(1.0, 0.86, 0.66) * (0.22 * pow(muh, 6.0) + 0.3 * pow(muh, 64.0));
    }
    return c;
}
// the haze a distance into the view along d: the far one the horizon's own colour that way (cwHazeColor), the near
// one the air lit by the sky all round (far: 0 near .. 1 far). At dusk the glow over the set sun is hundreds of times
// brighter than the dim scene: it is the light of a long way of high, still sunlit air, not of the air a few metres off
float3 cwHazeAt(float3 d, float3 sun, float far)
{
    float3 c = cwHazeColor(d, sun);
    return CW_TOD ? lerp(_Udon_CWHorizon.rgb, c, far) : c;
}
// the whole sky's light on level ground (the fixed sky's: float3(0.62, 0.70, 0.78) * pi * 0.22)
inline float3 cwAmbientIrr() { return CW_TOD ? _Udon_CWAmbient.rgb : float3(0.62, 0.70, 0.78) * CW_PI * 0.22; }

float cwHash12(float2 p) { float3 p3 = frac(p.xyx * .1031); p3 += dot(p3, p3.yzx + 33.33); return frac((p3.x + p3.y) * p3.z); }
float cwNoise(float2 p)
{
    float2 i = floor(p), f = frac(p); float2 u = f * f * (3. - 2. * f);
    return lerp(lerp(cwHash12(i), cwHash12(i + float2(1, 0)), u.x), lerp(cwHash12(i + float2(0, 1)), cwHash12(i + float2(1, 1)), u.x), u.y);
}
float cwFbm2(float2 p) { float v = 0., a = 0.5; for (int i = 0; i < 4; i++) { v += a * cwNoise(p); p = p * 2.03 + 17.1; a *= 0.5; } return v; }

float cwRidge(float a) // periodic headland silhouette, elevation in radians (~1.5-4 deg)
{
    return 0.040 + 0.016 * sin(a * 2.0 + 0.7) + 0.011 * sin(a * 5.0 + 2.1) + 0.006 * sin(a * 11.0 + 0.3) + 0.003 * sin(a * 23.0 + 1.7);
}

// Clouds (optional): a layer of fair-weather cumulus 1.5 km up, drifting with the wind and slowly changing shape.
// Set on the sky material; the water and the seabed get copies (they reflect and refract the same sky).
float _CloudCover; // 0..1, how much of the sky they cover (0: none)
float _CloudSize;  // m across a typical cloud
float _CloudSpeed; // m/s the layer drifts
float _CloudDir;   // degrees, the way it drifts: clockwise from world +z

#define CW_CLOUD_H 1500.0

// where along d (JS space, d.y > 0) the ray meets the layer, in cloud sizes (drifted and warped: the shapes
// change as they go); fp = how many cloud sizes a pixel spans there (fwE = fwidth(d.y))
float2 cwCloudUV(float3 d, float fwE, out float fp)
{
    float y = d.y + 0.06;                          // (the layer flattened toward the horizon: never infinitely far)
    float s = 1.0 / max(_CloudSize, 50.0);
    float2 p = d.xz * (CW_CLOUD_H / y) * s;
    fp = fwE * CW_CLOUD_H * (length(d.xz) + y) / (y * y) * s;
    float a = radians(_CloudDir);
    float t = _Time.y;
    float2 q = p - float2(sin(a), -cos(a)) * (_CloudSpeed * s * t);
    // a slow warp of its own, so they grow, shrink and part rather than slide past like a picture
    float tw = t * 0.012;
    q += 0.45 * float2(cwNoise(q * 0.7 + float2(tw, 3.1)), cwNoise(q * 0.7 + float2(5.7, -tw))) - 0.225;
    return q;
}

// the clouds' two broadest octaves (as in cwFbm2): enough to tell which side of a cloud faces the sun
inline float cwCloudCoarse(float2 q) { return 0.5 * cwNoise(q) + 0.25 * cwNoise(q * 2.03 + 17.1); }

// density 0..1 of the layer at q, a pixel spanning fp there (far off, the detail averages out to haze);
// coarse = its two broadest octaves there, tau = its optical depth (~1 at the frayed edges, tens in the core)
float cwCloudDensity(float2 q, float fp, out float coarse, out float tau)
{
    float far = smoothstep(0.15, 0.6, fp);
    coarse = cwCloudCoarse(q);
    // the finer octaves turned off the lattice's axes, so its squares don't show along the edges
    float2 p = mul(float2x2(0.80, -0.60, 0.60, 0.80), q) * 4.12 + 3.3;
    float2 pd = mul(float2x2(0.28, 0.96, -0.96, 0.28), q) * 17.3 + 7.7;
    float n = coarse + 0.125 * cwNoise(p) + 0.0625 * cwNoise(mul(float2x2(0.80, -0.60, 0.60, 0.80), p) * 2.03 + 11.9)
            + 0.05 * (cwNoise(pd) - 0.5) * (1.0 - smoothstep(0.01, 0.04, fp)); // (crisp edges)
    n = lerp(n, 0.47, far);
    float thr = lerp(0.74, 0.30, saturate(_CloudCover));
    float dens = smoothstep(thr, thr + 0.12 + 0.3 * far, n);
    // what its opacity says across the edge (so the sky shows through only as much as the sunlight does: no darker
    // ring between a bright edge and the inside), then climbing into the tens once it is opaque
    float x = (n - thr) / 0.12;
    tau = (-log(1.0 - 0.99 * dens) + min(60.0 * max(x - 1.0, 0.0), 60.0)) * (1.0 - 0.8 * far);
    return dens;
}

// Henyey-Greenstein phase function: how much of the sunlight a droplet sends off at angle acos(mu) from it
inline float cwPhaseHG(float mu, float g) { return (1.0 - g * g) / (4.0 * CW_PI * pow(max(1.0 + g * g - 2.0 * g * mu, 1e-4), 1.5)); }

// how much of the sun's direct light gets through the clouds seen along d (for the sun's disc): exp(-optical
// depth), so a cumulus hides it entirely and only its frayed edges let a dimmed disc through
float cwCloudSunT(float3 d)
{
    if (_CloudCover <= 0.0 || d.y <= 0.0) return 1.0;
    float fp, coarse, tau; float2 q = cwCloudUV(d, 0.0, fp);
    cwCloudDensity(q, 0.0, coarse, tau);
    return exp(-tau);
}

// the sky c seen along d with the clouds in front of it (e = d.y, mu = the cosine to the sun, hor = horizon colour)
float3 cwCloudsOver(float3 c, float3 d, float3 sun, float mu, float3 hor, float fwE)
{
    [branch] if (_CloudCover > 0.0 && d.y > 0.0)
    {
        float fp, coarse, tau; float2 q = cwCloudUV(d, fwE, fp);
        float dens = cwCloudDensity(q, fp, coarse, tau);
        [branch] if (dens > 0.0)
        {
            // lit from the sun's side, shaded where the cloud thickens toward the sun
            float2 toSun = normalize(sun.xz + 1e-5) * 0.12;
            float lit = saturate(0.55 + 3.5 * (coarse - cwCloudCoarse(q + toSun)));
            lit = lerp(lit, 0.6, smoothstep(0.15, 0.6, fp));
            float3 shade = float3(0.46, 0.52, 0.61), lamp = float3(1.30, 1.24, 1.12);
            [branch] if (CW_TOD) { shade = cwAmbientIrr() * 0.97; lamp = shade + _Udon_CWCloudLight.rgb * 0.14; } // (the fixed ones, at its reference)
            float3 col = lerp(shade, lamp, lit) * lerp(0.75, 1.0, saturate(sun.y * 3.0));
            // Close to the sun they glow with the sunlight they pass on, sent forward by the droplets (a phase function
            // peaked round the sun): as much as they cover the sky at their soft edges, fading slowly into the thick
            // core. (A band of light along the edge outlined them like a drawing.)
            float3 sunC = CW_TOD ? _Udon_CWCloudLight.rgb : float3(1.0, 0.86, 0.66) * 6.0;
            float3 fwd = sunC * 0.4 * cwPhaseHG(mu, 0.9) * dens * exp(-tau / 12.0);
            col = lerp(hor, col, smoothstep(0.0, 0.25, d.y)); // far ones fade into the haze
            float hz = smoothstep(0.0, 0.04, d.y);
            c = lerp(c, col, dens * hz) + fwd * hz;
        }
    }
    return c;
}

// sky radiance for a JS-space direction (HDR, linear). fwE = fwidth(d.y), for the anti-aliased ridge edge;
// pass it in when calling from inside a dynamic branch. (Indoors the room is there instead: cwSkyFw below.)
float3 cwSkyOutdoor(float3 d, float3 sun, float fwE)
{
    float e = d.y;
    float mu = dot(d, sun);
    float3 zen = float3(0.11, 0.27, 0.62), hor = float3(0.66, 0.78, 0.90), c;
    [branch] if (CW_TOD)
    {
        // the baked sky, and the glare right round the sun and the moon: only the core the table leaves out (within
        // 3 degrees; the wider glow is the table's own Mie scattering). The horizon's own colour only low down, where
        // the far clouds and the headland fade into it
        c = cwAtmosphere(d);
        hor = c;
        [branch] if (e < 0.3) hor = cwAtmosphere(normalize(float3(d.x, 0.02, d.z) + float3(0.0, 1e-3, 0.0)));
        float ms = max(dot(d, normalize(cwToJS(_Udon_CWSun.xyz))), 0.0);
        c += _Udon_CWSunColor.rgb * (0.04 * pow(ms, 400.0) + 0.27 * pow(ms, 2400.0));
        [branch] if (_Udon_CWMoonColor.a > 0.0)
        {
            float mm = max(dot(d, normalize(cwToJS(_Udon_CWMoon.xyz))), 0.0);
            c += _Udon_CWMoonColor.rgb * (0.04 * pow(mm, 400.0) + 0.27 * pow(mm, 2400.0));
        }
    }
    else
    {
        c = lerp(hor, zen, pow(saturate(e), 0.42));
        c += float3(1.0, 0.86, 0.66) * (0.22 * pow(max(mu, 0.), 6.) + 0.30 * pow(max(mu, 0.), 64.) + 1.6 * pow(max(mu, 0.), 2400.));
    }
    c = cwCloudsOver(c, d, sun, mu, hor, fwE);
    // distant headland: pine canopy over pale limestone, softened by ~2 km of air
    float a = atan2(d.z, d.x);
    float r = cwRidge(a) + 0.0045 * (cwNoise(float2(a * 260.0, 0.0)) - 0.5) + 0.002 * (cwNoise(float2(a * 900.0, 3.0)) - 0.5);
    float back = smoothstep(-0.3, 0.95, dot(normalize(float2(d.x, d.z) + 1e-5), normalize(float2(sun.x, sun.z))));
    float u = saturate(e / max(r, 1e-3));
    float2 q = float2(a * 420.0, e * 420.0);
    float tex = cwFbm2(q);
    float3 pine = float3(0.045, 0.070, 0.042) * (0.6 + 0.8 * tex);
    float3 rock = float3(0.30, 0.28, 0.23) * (0.55 + 0.7 * cwFbm2(q * 1.7 + 5.0));
    // pale rock along the whole foot, its top rising and falling with the direction (never switched on and off by
    // direction alone, which cut the band off with vertical edges), ragged where the pines come down
    float cliffTop = 0.08 + 0.30 * smoothstep(0.2, 0.8, cwFbm2(float2(a * 7.0, 1.0)));
    float cliff = smoothstep(cliffTop + 0.05, cliffTop - 0.05, u + 0.25 * (tex - 0.5) + 0.12 * (cwFbm2(float2(a * 60.0, e * 60.0 + 4.0)) - 0.5));
    float3 land = lerp(pine, rock, cliff);
    land *= lerp(1.0, 0.45, back);                         // backlit toward the sun
    [branch] if (CW_TOD) // (in the light of the moment, as against the fixed sky's)
        land *= (cwAmbientIrr() + 0.3 * cwKeyColor()) / (float3(0.62, 0.70, 0.78) * CW_PI * 0.22 + 0.3 * float3(6.0, 5.4, 4.44));
    land = lerp(land, hor * 0.92, 0.38 + 0.25 * back);      // aerial perspective
    float w = fwE * 1.2 + 2e-4;
    c = lerp(c, land, smoothstep(r + w, r - w, e) * step(-0.3, e));
    return c;
}

// flat-surface refraction shift of the caustic pattern on the floor (keeps it registered under the sun)
float2 cwCausShift(float3 sun, float depth)
{
    float sinI = sqrt(saturate(1.0 - sun.y * sun.y)), sinT = sinI / CW_IOR, cosT = sqrt(1.0 - sinT * sinT);
    float hd = length(sun.xz); if (hd < 1e-6) return 0;
    return -sun.xz / hd * depth * (sinT / cosT);
}

float cwFresnel(float ci, float n)
{
    ci = saturate(ci);
    float st2 = (1.0 - ci * ci) / (n * n); if (st2 >= 1.0) return 1.0;
    float ct = sqrt(1.0 - st2);
    float rs = (ci - n * ct) / (ci + n * ct), rp = (n * ci - ct) / (n * ci + ct);
    return 0.5 * (rs * rs + rp * rp);
}

// share of the light let through from the water into the air, k = the squared cosine of its angle in the air
// (k <= 0: totally reflected)
float cwTransmitK(float k)
{
    if (k <= 0.0) return 0.0;
    return 1.0 - cwFresnel(sqrt(1.0 - (1.0 - k) / (CW_IOR * CW_IOR)), 1.0 / CW_IOR);
}

// The demo's tone curve (exposure, ACES fit, slight desaturation, cool shadows). Output is display-referred;
// Unity's linear->sRGB framebuffer encode stands in for the demo's pow(1/2.2).
// Highlights (a channel over 0.8 once exposed) go over to a curve of their own: taken on the brightest channel with
// the others kept in proportion, so their hue stays instead of each channel running into white, and with a longer
// tail than the ACES fit's (which puts everything from 2.5 up within a few percent of white): the glare round the sun
// and the silver edges of the clouds keep their gradation. Only the very brightest fade to white. (Editor/
// ClearwaterToneMapping.cs bakes the same curve for post-processing: keep the two in step.)
float _Exposure;
#define CW_TONE_KNEE 0.8
float cwToneShoulder(float m)
{
    // from the knee on, the ACES fit's value and slope there (0.7523, 0.3125), then slowly on to 1
    float u = 1.2617 * (m - CW_TONE_KNEE);
    return 0.7523 + 0.2477 * u / (1.0 + u);
}
float3 cwTonemap(float3 c)
{
#if defined(_CW_TONEMAP)
    c *= _Exposure;
    const float a = 2.51, b = 0.03, cc = 2.43, d = 0.59, e = 0.14;
    float m = max(c.r, max(c.g, c.b));
    float3 y = saturate((c * (a * c + b)) / (c * (cc * c + d) + e));
    [branch] if (m > CW_TONE_KNEE)
    {
        float s = cwToneShoulder(m);
        float3 h = c * (s / m);
        h = lerp(h, s.xxx, 0.7 * smoothstep(4.0, 40.0, m));
        y = lerp(y, h, smoothstep(CW_TONE_KNEE, 1.3, m));
    }
    c = y;
    float lum = dot(c, float3(0.2126, 0.7152, 0.0722));
    c = lerp(lum.xxx, c, 0.90);
    c = lerp(c, lum * float3(0.86, 1.0, 1.28), 0.55 * _Udon_CWNight.y); // (night vision: the dark scene's colours fade to blue)
    c = lerp(c, c * float3(0.96, 1.0, 1.05), 1.0 - smoothstep(0.0, 0.35, lum));
#endif
    return c;
}

// Approximate inverse of cwTonemap (exact for the ACES fit, ignores the small grade), used to bring already
// rendered scene colours (avatars, the seabed mesh) back to scene radiance before water attenuates them.
float3 cwInvTonemap(float3 y)
{
#if defined(_CW_TONEMAP)
    y = clamp(y, 0.0, 0.985);
    const float a = 2.51, b = 0.03, c = 2.43, d = 0.59, e = 0.14;
    float3 A = y * c - a, B = y * d - b, Cc = y * e;
    float3 x = (-B - sqrt(max(B * B - 4.0 * A * Cc, 0.0))) / (2.0 * A);
    return x / max(_Exposure, 1e-4);
#else
    // Already linear HDR (tone mapping in post-processing). Capped where the in-shader curve tops out (its inverse
    // at 0.985), so the water in front of it dims the sun and the glow round it as it does in the default mode;
    // uncapped, seen from under the water they spread into a wide white blur.
    return min(max(y, 0.0), 4.97 / max(_Exposure, 1e-4));
#endif
}

// Indoors, what the water reflects and lets through from above is the room: the nearest reflection probe, box
// projected from where it is seen (cwEnvPos, world; the water sets it to the point it shades), blurred by rough
// (0 sharp .. 1 its broadest mip). The probe holds the room as its own shaders show it, not tone mapped: brought back
// to the radiance these shaders tone map, so the room in the water looks as it does beside it.
static float3 cwEnvPos;
float3 cwRoom(float3 d, float rough)
{
#if defined(UNITY_CG_INCLUDED)
    float3 dir = cwToJS(d); // (world)
    [branch] if (unity_SpecCube0_ProbePosition.w > 0.0 && all(cwEnvPos >= unity_SpecCube0_BoxMin.xyz) && all(cwEnvPos <= unity_SpecCube0_BoxMax.xyz))
    {
        float3 nd = normalize(dir);
        float3 rmax = (unity_SpecCube0_BoxMax.xyz - cwEnvPos) / nd, rmin = (unity_SpecCube0_BoxMin.xyz - cwEnvPos) / nd;
        float3 r = nd > 0.0 ? rmax : rmin;
        dir = cwEnvPos - unity_SpecCube0_ProbePosition.xyz + nd * min(min(r.x, r.y), r.z);
    }
    float4 s = UNITY_SAMPLE_TEXCUBE_LOD(unity_SpecCube0, dir, rough * 6.0);
    return cwInvTonemap(DecodeHDR(s, unity_SpecCube0_HDR)) * _EnvGain;
#else
    return 0.0;
#endif
}

float3 cwSkyFw(float3 d, float3 sun, float fwE)
{
    [branch] if (_Indoor > 0.5) return cwRoom(d, 0.0);
    return cwSkyOutdoor(d, sun, fwE);
}
float3 cwSky(float3 d, float3 sun) { return cwSkyFw(d, sun, fwidth(d.y)); }

#endif
