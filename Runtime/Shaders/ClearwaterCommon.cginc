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
inline float3 cwSun() { return normalize(cwToJS(_SunDir.xyz)); }

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
// coarse = its two broadest octaves there
float cwCloudDensity(float2 q, float fp, out float coarse)
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
    return smoothstep(thr, thr + 0.12 + 0.3 * far, n);
}

// how much of the sun gets through the clouds seen along d (for the sun's disc)
float cwCloudSunT(float3 d)
{
    if (_CloudCover <= 0.0 || d.y <= 0.0) return 1.0;
    float fp, coarse; float2 q = cwCloudUV(d, 0.0, fp);
    return 1.0 - 0.97 * cwCloudDensity(q, 0.0, coarse);
}

// the sky c seen along d with the clouds in front of it (e = d.y, mu = the cosine to the sun, hor = horizon colour)
float3 cwCloudsOver(float3 c, float3 d, float3 sun, float mu, float3 hor, float fwE)
{
    [branch] if (_CloudCover > 0.0 && d.y > 0.0)
    {
        float fp, coarse; float2 q = cwCloudUV(d, fwE, fp);
        float dens = cwCloudDensity(q, fp, coarse);
        [branch] if (dens > 0.0)
        {
            // lit from the sun's side, shaded where the cloud thickens toward the sun
            float2 toSun = normalize(sun.xz + 1e-5) * 0.12;
            float lit = saturate(0.55 + 3.5 * (coarse - cwCloudCoarse(q + toSun)));
            lit = lerp(lit, 0.6, smoothstep(0.15, 0.6, fp));
            float3 col = lerp(float3(0.46, 0.52, 0.61), float3(1.30, 1.24, 1.12), lit) * lerp(0.75, 1.0, saturate(sun.y * 3.0));
            // thin edges round the sun glow (light scattered forward through them)
            col += float3(1.0, 0.86, 0.66) * 2.5 * pow(max(mu, 0.0), 12.0) * (1.0 - dens);
            col = lerp(hor, col, smoothstep(0.0, 0.25, d.y)); // far ones fade into the haze
            c = lerp(c, col, dens * smoothstep(0.0, 0.04, d.y));
        }
    }
    return c;
}

// sky radiance for a JS-space direction (HDR, linear). fwE = fwidth(d.y), for the anti-aliased ridge edge;
// pass it in when calling from inside a dynamic branch.
float3 cwSkyFw(float3 d, float3 sun, float fwE)
{
    float e = d.y;
    float mu = dot(d, sun);
    float3 zen = float3(0.11, 0.27, 0.62), hor = float3(0.66, 0.78, 0.90);
    float3 c = lerp(hor, zen, pow(saturate(e), 0.42));
    c += float3(1.0, 0.86, 0.66) * (0.22 * pow(max(mu, 0.), 6.) + 0.30 * pow(max(mu, 0.), 64.) + 1.6 * pow(max(mu, 0.), 2400.));
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
    land = lerp(land, hor * 0.92, 0.38 + 0.25 * back);      // aerial perspective
    float w = fwE * 1.2 + 2e-4;
    c = lerp(c, land, smoothstep(r + w, r - w, e) * step(-0.3, e));
    return c;
}
float3 cwSky(float3 d, float3 sun) { return cwSkyFw(d, sun, fwidth(d.y)); }

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
float _Exposure;
float3 cwTonemap(float3 c)
{
#if defined(_CW_TONEMAP)
    c *= _Exposure;
    const float a = 2.51, b = 0.03, cc = 2.43, d = 0.59, e = 0.14;
    c = saturate((c * (a * c + b)) / (c * (cc * c + d) + e));
    float lum = dot(c, float3(0.2126, 0.7152, 0.0722));
    c = lerp(lum.xxx, c, 0.90);
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
    return y;
#endif
}

#endif
