// Clearwater: the water surface's code, shared by the two passes of Water.shader. Each pass defines
// CW_WATER_BELOW (0: seen from above, 1: from under the water) before including this; a camera draws only the pass
// for its side, and the large under-water path (Snell's window with what is above, the mirror of what is below)
// is compiled into the under-water pass only.
#ifndef CLEARWATER_WATER_INCLUDED
#define CLEARWATER_WATER_INCLUDED
#include "UnityCG.cginc"
#define CW_ROCK_DETAIL_FP_SCALE 4.0
#include "ClearwaterSurface.cginc"

float _SeaHalfSize; // the water plane's half size (m)
UNITY_DECLARE_SCREENSPACE_TEXTURE(_CWGrabWater);
UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);

struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
struct v2f
{
    float4 pos : SV_POSITION;
    float3 wpos : TEXCOORD0;
    float3 origin : TEXCOORD1;
    float4 grabPos : TEXCOORD2;
    float4 screenPos : TEXCOORD3;
    UNITY_VERTEX_OUTPUT_STEREO
};

v2f vertSide(appdata v)
{
    v2f o;
    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_INITIALIZE_OUTPUT(v2f, o);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
    o.origin = mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz;
    if (cwCameraNearSurface(o.origin.y))
    {
        // a camera at the waterline draws both passes, each keeping its own pixels (fragSide); the plane only
        // marks where to shade, so it is moved clear of the near clip plane, to the camera's side of the surface
        // that the pass looks at (the fragment traces the true surface from the camera)
        float off = 0.1 + 4.0 * _ProjectionParams.y;
        o.wpos.y = (CW_WATER_BELOW != 0) ? max(o.wpos.y, _WorldSpaceCameraPos.y + off) : min(o.wpos.y, _WorldSpaceCameraPos.y - off);
    }
    else if (CW_WATER_BELOW == 0 && _ShoreWaves > 0.5)
    {
        // Seen from above, the plane is raised to the highest the run-up reaches: at the still-water height the beach
        // face above it hid it, so the sheet running up the beach was never drawn, only the wet sand it leaves (two
        // layers meeting at a fixed line). The fragment traces the true surface and drops the dry beach itself.
        float reach = _SwashHeight * _SwashRunup * 1.2 + _FoamLift + 0.05;
        o.wpos.y += min(reach, max(_WorldSpaceCameraPos.y - o.origin.y - 0.2, 0.0));
    }
    o.pos = UnityWorldToClipPos(o.wpos);
    if (!cwCameraNearSurface(o.origin.y) && (_WorldSpaceCameraPos.y < o.origin.y) != (CW_WATER_BELOW != 0))
        o.pos = float4(-2, -2, -2, 1); // the other side's pass
    o.grabPos = ComputeGrabScreenPos(o.pos);
    o.screenPos = ComputeScreenPos(o.pos);
    return o;
}


float4 texBS(sampler2D t, float2 uv) // cubic B-spline filtering in 4 bilinear taps: smooth slopes -> smooth highlights
{
    float2 ts = _Surf_TexelSize.zw; float2 p = uv * ts - 0.5; float2 f = frac(p); p = floor(p);
    float2 f2 = f * f, f3 = f2 * f;
    float2 w0 = (-f3 + 3.0 * f2 - 3.0 * f + 1.0) / 6.0, w1 = (3.0 * f3 - 6.0 * f2 + 4.0) / 6.0;
    float2 w2 = (-3.0 * f3 + 3.0 * f2 + 3.0 * f + 1.0) / 6.0, w3 = f3 / 6.0;
    float2 g0 = w0 + w1, g1 = w2 + w3; float2 h0 = (w1 / g0 - 0.5 + p) / ts, h1 = (w3 / g1 + 1.5 + p) / ts;
    return (tex2D(t, float2(h0.x, h0.y)) * g0.x + tex2D(t, float2(h1.x, h0.y)) * g1.x) * g0.y
         + (tex2D(t, float2(h0.x, h1.y)) * g0.x + tex2D(t, float2(h1.x, h1.y)) * g1.x) * g1.y;
}

// distance along the (world) view ray to whatever opaque surface the depth texture holds
float sceneDistance(float2 uvD, float3 rdWorld)
{
    float raw = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uvD);
    float3 fwd = -UNITY_MATRIX_V[2].xyz;
    return LinearEyeDepth(raw) / max(dot(rdWorld, fwd), 1e-3);
}

#if defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED)
#define CW_GRAB_LOD(uv) UNITY_SAMPLE_TEX2DARRAY_LOD(_CWGrabWater, float3(uv, unity_StereoEyeIndex), 0)
#else
#define CW_GRAB_LOD(uv) tex2Dlod(_CWGrabWater, float4(uv, 0, 0))
#endif

// Screen-space trace from a point Pw on the surface (world) along dirW, starting D metres out: what this
// camera saw in that direction (depth texture), found by settling the distance along the ray (2 passes).
// S = the point found; hit = 1 when it is on screen, not sky, and lies on the ray (not something nearer
// the camera that hides it). Returns its colour from the grab (tone mapping undone). Explicit-LOD
// samples only, so it can sit in dynamic branches.
float3 cwScreenTrace(float3 Pw, float3 dirW, float D, out float3 S, out float hit)
{
    float3 fwdW = -UNITY_MATRIX_V[2].xyz;
    S = Pw; hit = 0;
    float2 uvG = 0;
    [unroll] for (int q = 0; q < 2; q++)
    {
        float3 Q = Pw + dirW * D;
        float4 cq = UnityWorldToClipPos(Q);
        float4 sq = ComputeScreenPos(cq), gq = ComputeGrabScreenPos(cq);
        float2 uvQ = sq.xy / max(sq.w, 1e-5);
        uvG = gq.xy / max(gq.w, 1e-5);
        float3 dirQ = normalize(Q - _WorldSpaceCameraPos);
        float raw = SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthTexture, float4(uvQ, 0, 0));
        S = _WorldSpaceCameraPos + dirQ * (LinearEyeDepth(raw) / max(dot(dirQ, fwdW), 1e-3));
        bool onScreen = cq.w > 0.0 && abs(cq.x) < cq.w && abs(cq.y) < cq.w;
        hit = (onScreen && Linear01Depth(raw) < 0.999) ? 1.0 : 0.0;
        D = clamp(dot(S - Pw, dirW), 0.05, 30.0);
    }
    float3 off = S - (Pw + dirW * D); // how far the point found is from the ray
    hit *= step(length(off), 0.1 + 0.05 * D);
    return cwInvTonemap(CW_GRAB_LOD(uvG).rgb);
}

// Whitewater density at xz (water space) for this pixel's shore state (cover, cc, run, sheetW, obst: see fragSide):
// the run-up foam laid out in shore coordinates, pushed by the surge and dragged into streaks by the backwash, and
// the breaking crest's foam (lace swirling round an obstacle). Also taken beside the pixel, for the foam's relief.
float cwWhitewater(float2 xz, float cover, float cc, float run, float sheetW, float obst, float footprint)
{
    float2 suv = cwShoreUV(xz);
    float foam = 0.0;
    [branch] if (cover > 0.014) // (below that cwFoam is 0)
        foam = cwRunupFoam(suv, cover, run, sheetW, footprint);
    [branch] if (cc > 0.014)
    {
        float2 cq = lerp(float2(suv.y, -suv.x + 0.4 * _SwashClock), xz * 1.3 + float2(0.0, 0.25 * _SwashClock), obst);
        foam = max(foam, cwFoam(cq, saturate(cc), _SwashClock, lerp(max(footprint, 0.15), footprint, obst)) * saturate(cc * 1.6));
    }
    return foam;
}

float4 fragSide(v2f i)
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
    float3 SUN = cwSunColor();
    float3 uSun = cwSun();
    float uL = _PatchSize;
    float uTime = _Time.y;

    // water space: JS axes, origin at the water object, y = 0 on the mean surface
    float3 uCam = cwToJS(_WorldSpaceCameraPos - i.origin);
    float3 rdWorld = normalize(i.wpos - _WorldSpaceCameraPos);
    float3 wd = cwToJS(rdWorld);
    float3 rd = wd;
    float fwRd = fwidth(rd.y); // taken here, outside any branch, for the sky evaluations below
    float footprint = length(fwidth(i.wpos)); // metres per pixel, for the foam detail
    bool below = uCam.y < 0.0; // a run-time value on purpose: as a constant, the view from above compiles slower
    [branch] if (cwCameraNearSurface(i.origin.y))
    {
        // lens at the waterline: this pixel's side, from where its ray starts (see ClearwaterSurface.cginc)
        below = cwPixelUnder(rdWorld, i.origin);
        clip(below == (CW_WATER_BELOW != 0) ? 1.0 : -1.0);
        uCam = cwToJS(cwNearPoint(rdWorld) - i.origin);
    }
    if (below) { wd.y = max(wd.y, 0.0015); } else { wd.y = min(wd.y, -0.0015); }
    wd = normalize(wd);
    float2 ripC = _RipCenter.xy;

    // The plane is raised over the beach face (vertSide), so many pixels look at dry sand: where the ray gets down to
    // the highest the swash can be right now, is the sand already above it there? Then it met the sand first: done.
    // the swash along the shore where the ray meets still water, once for the pixel
    CwSwash swPix = cwSwashNone();
    [branch] if (_ShoreWaves > 0.5)
    {
        swPix = cwSwashNow(cwShoreUV((uCam + wd * (-uCam.y / wd.y)).xz));
    }
    [branch] if (!below && _ShoreWaves > 0.5 && uCam.y > 0.3)
    {
        float top = swPix.level + _FoamLift + 0.12; // (lobes, the lip, small waves)
        float3 Q = uCam + wd * ((top - uCam.y) / wd.y);
        clip(-cwFloorDepth(Q.xz) <= top ? 1.0 : -1.0);
    }

    // ---- surface intersection (height field, fixed-point) ----
    float t = -uCam.y / wd.y;
    float2 xz; float4 A, B, R;
    const float SC = 0.41, WB = 0.10;
    float hsum = 0.0;
    [unroll] for (int it = 0; it < 3; it++)
    {
        xz = uCam.xz + wd.xz * t;
        A = tex2D(_Surf, xz / uL);
        B = tex2D(_Surf, mulM(xz) / (uL * SC) + 0.37);
        float2 ruv = (xz - ripC) / _RipSize + 0.5;
        R = tex2D(_Rip, ruv);
        CwShore shi = cwShoreSw(xz, cwFloorDepth2(xz).y, swPix); // waves feel obstacles too
        // the thin run-up sheet does not carry the open-water swell
        hsum = (A.x + WB * SC * B.x) * (1.0 - 0.75 * shi.swash) + R.x + shi.eta;
        t = (hsum - uCam.y) / wd.y;
    }
    t = max(t, 0.0);
    float3 P = uCam + wd * t;
    // Anything standing out of the water between the raised plane and the surface (an avatar's waist, a post) is
    // in front of the water here: the depth buffer let the plane over it, so let it show through.
    float sceneFront = sceneDistance(i.screenPos.xy / i.screenPos.w, rdWorld);
    [branch] if (!below && _ShoreWaves > 0.5) clip(sceneFront - t + 0.01);
    // no water over the beach
    float2 fd = cwFloorDepth2(P.xz); // ground, and what the waves feel (obstacles included)
    float floorP = fd.x;
    // cut the water away over dry sand only; rock standing out of it hides the water through the depth
    // buffer (its mesh is drawn first), which always agrees with the rock you actually see
    clip(floorP + cwRock(P.xz) + P.y);
    // shore waves here, and their slope from two nearby samples (+u is seaward = -z in JS space)
    CwShore shore = cwShoreSw(P.xz, fd.y, swPix);
    const float SE = 0.06;
    float etaU = cwShoreSw(P.xz - float2(0, SE), cwFloorDepth2(P.xz - float2(0, SE)).y, swPix).eta;
    float etaX = cwShoreSw(P.xz + float2(SE, 0), cwFloorDepth2(P.xz + float2(SE, 0)).y, swPix).eta;

    A = texBS(_Surf, P.xz / uL);
    B = texBS(_Surf, mulM(P.xz) / (uL * SC) + 0.37);
    float calm = 1.0 - 0.75 * shore.swash;
    float2 slope = (A.yz + WB * mulMt(B.yz)) * calm + R.yz + float2(etaX - shore.eta, shore.eta - etaU) / SE;
    float4 Cm = tex2D(_Surf, mulM2(P.xz) / (uL * 0.13) + 0.71);
    slope += 0.13 * exp(-t * 0.18) * mulM2t(Cm.yz) * calm;
    // Ripples a hand's breadth long, seen from below: the window into the air bends every one of them into its
    // edge and the sky inside (on real water it is torn into small bright shapes); from above they are lost in
    // the reflection's blur, so it is left as it was.
    float4 Rp = tex2D(_Surf, mulM(P.xz) / (uL * 0.045) + 0.53);
    slope += (below ? 0.5 * exp(-t * 0.2) : 0.0) * mulMt(Rp.yz) * calm;
    // the sheet's own small ripples, stretched along the flow up and down the slope
    [branch] if (shore.swash > 0.0)
    {
        float2 fq = P.xz * float2(5.5, 2.2) + float2(0.0, _SwashClock * 1.3);
        float2 g = float2(cwNoise(fq + float2(0.15, 0)) - cwNoise(fq - float2(0.15, 0)),
                          cwNoise(fq + float2(0, 0.15)) - cwNoise(fq - float2(0, 0.15))) / 0.3;
        slope += shore.swash * 0.035 * g * float2(5.5, 2.2) / 5.5;
    }
    float var = max(A.w - dot(A.yz, A.yz), 0.0) + WB * WB * max(B.w - dot(B.yz, B.yz), 0.0);
    float3 n = normalize(float3(-slope.x, 1.0, -slope.y));
    float dist = t;
    float3 v = -wd;
    // seen from below: k = cos^2 of the angle into the air (negative: total internal reflection), and how much it
    // varies across the pixel (taken here, outside any branch)
    float cosU = max(dot(wd, n), 0.02);
    float kU = 1.0 - CW_IOR * CW_IOR * (1.0 - cosU * cosU);
    float kFw = fwidth(kU);

    if (below)
    {
        // ---- seen from under the water: Snell's window, total internal reflection outside it ----
        // The light let through falls steeply to nothing at the window's edge (the critical angle), following the
        // Fresnel curve rather than switched on and off, and averaged over the pixel's spread so the edge stays sharp
        // without flickering (a hard switch left ragged patches of sky scattered round the window). Blurring it by
        // the slopes smaller than a pixel as well made the edge a soft haze: on real water it is crisp.
        float3 nd = -n;
        float sigK = 0.5 * kFw + 0.002;
        float Tb = (4.0 * cwTransmitK(kU) + cwTransmitK(kU - 1.732 * sigK) + cwTransmitK(kU + 1.732 * sigK)) / 6.0;
        float Fb = 1.0 - Tb;
        // the way into the air (at the edge and past it, along the horizon)
        float3 ta = normalize(CW_IOR * wd - (CW_IOR * cosU - sqrt(max(kU, 0.0))) * n);
        ta.y = max(ta.y, 0.0); ta = normalize(ta);
        bool window = Tb > 0.001;
        float3 through = 0;
        [branch] if (window)
        {
            through = cwSkyFw(ta, uSun, fwRd) * 1.1;
            float disc = smoothstep(0.9990, 0.99995, dot(ta, uSun));
            [branch] if (disc > 0.0) through += SUN * 18.0 * disc * (1.0 - cwFresnel(uSun.y, CW_IOR)) * cwCloudSunT(ta);
        }

        // Things above the water (a head, a raised arm) seen through the window: the refracted ray into
        // the air, traced in screen space; anything off screen, under the water or sky leaves the sky.
        #if CW_WATER_BELOW
        float3 Pw = cwToJS(P) + i.origin;
        float sceneD = sceneDistance(i.screenPos.xy / i.screenPos.w, rdWorld); // outside the branches (implicit LOD)
        [branch] if (window)
        {
            float3 S; float hitA;
            float3 air = cwScreenTrace(Pw, cwToJS(ta), clamp(sceneD - t, 0.05, 30.0), S, hitA);
            through = lerp(through, air, hitA * step(i.origin.y + 0.02, S.y));
        }

        // Outside the window the surface is a mirror (total internal reflection) of the underwater scene:
        // the reflected ray, traced in screen space for what this camera sees under the water (avatars'
        // legs, the seabed), else the seabed worked out along it; dimmed by the water on the way.
        float3 inside = 0;
        [branch] if (Fb > 0.03)
        {
            float3 rr = reflect(wd, nd);
            rr.y = min(rr.y, -0.01); rr = normalize(rr);
            float fyR = -cwFloorDepth(P.xz), sR = (fyR - P.y) / rr.y; float3 RP = P + rr * sR;
            [unroll] for (int jr = 0; jr < 2; jr++) { fyR = -cwFloorDepth(RP.xz); sR = (fyR - P.y) / rr.y; RP = P + rr * sR; }
            sR = clamp(sR, 0.0, 60.0);
            float depthR = max(-RP.y, 0.0);
            float3 S2; float hitU;
            float3 mir = cwScreenTrace(Pw, cwToJS(rr), clamp(sR, 0.05, 30.0), S2, hitU);
            hitU *= step(S2.y, i.origin.y - 0.01);
            float Dm = sR;
            [branch] if (hitU > 0.0) Dm = length(S2 - Pw);
            else
            {
                // the seabed seen in the mirror is far, dim and blurred: the bed look's average colour under the caustics
                float3 albR = _BedMean.rgb * lerp(0.7, 1.25, cwNoise(RP.xz * 0.45));
                float3 causR = tex2Dlod(_Caus, float4(cwCausUV(RP.xz, 0.35, uSun), 0, 2)).rgb;
                mir = cwFloorRadianceUnder(RP.xz, depthR, 0.35, albR, uSun, causR, 1.0);
            }
            inside = mir * exp(-SIG_T * Dm) + cwInscatter(0.5 * depthR, Dm, rr, uSun);
        }
        #else
        float3 inside = cwInscatter(0.8, 40.0, reflect(wd, nd), uSun); // (the view-from-above pass never gets here)
        #endif
        float3 colb = Fb * inside + (1.0 - Fb) * through;
        return float4(cwTonemap(max(colb, 0.0)), 1.0);
    }

    float nv = dot(n, v);
    if (nv < 0.02) { n = normalize(n + v * (0.02 - nv)); nv = dot(n, v); }
    float F = cwFresnel(nv, CW_IOR);

    // ---- reflection ----
    // Seen at grazing angles, the facets that face the viewer tilt the reflection upward, which squashed
    // the headland's reflection into a thin band that read as an upright copy. On real rippled water the
    // hidden back faces balance this, so a distant shore reflects at its mirror position, only blurred.
    // Pull the reflected elevation toward the mirror's there, keeping part of the wave wobble; rays that
    // still point down (into the next wave) are clamped to the horizon rather than folded up (abs).
    float3 rr = reflect(wd, n);
    float mirrorW = smoothstep(0.16, 0.03, -wd.y) * 0.85;
    rr.y = max(lerp(rr.y, -wd.y, mirrorW), 0.0);
    rr = normalize(rr);
    float3 refl = cwSky(rr, uSun) * 1.25;

    // sun glints: Beckmann with slope-variance widening (LEAN-style)
    float a2 = 0.00012 + 1.2 * var;
    float3 h = normalize(v + uSun);
    float nh = max(dot(n, h), 0.0), nl = max(dot(n, uSun), 0.0);
    float c2 = max(nh * nh, 1e-4); float tan2 = (1.0 - c2) / c2;
    float D = exp(-tan2 / a2) / (CW_PI * a2 * c2 * c2);
    float Vis = 0.5 / (nl * sqrt(nv * nv * (1.0 - a2) + a2) + nv * sqrt(nl * nl * (1.0 - a2) + a2) + 1e-5);
    float Fh = cwFresnel(max(dot(h, v), 0.0), CW_IOR);
    float3 spec = SUN * min(D * Vis * Fh * nl, 12000.0);

    // ---- refraction / underwater ----
    float3 tr = refract(wd, n, 1.0 / CW_IOR);
    float fy = -cwFloorDepth(P.xz);
    float s = (fy - P.y) / tr.y; float3 FP = P + tr * s;
    [unroll] for (int j = 0; j < 2; j++) { fy = -cwFloorDepth(FP.xz); s = (fy - P.y) / tr.y; FP = P + tr * s; }
    s = max(s, 0.0);
    float depthHere = max(P.y - FP.y, 0.0);

    float hgt, rockM;
    float2 dFPdx = ddx(FP.xz), dFPdy = ddy(FP.xz);
    float3 alb = cwAlgae(cwFloorAlbedo(FP.xz, dFPdx, dFPdy, hgt, rockM), rockM);
    float sunShade = 1.0;
    [branch] if (rockM > 0.0) sunShade = lerp(1.0, cwUnderSunShade(cwFloorNormal(FP.xz, rockM), uSun), rockM);
    float3 caus = tex2Dbias(_Caus, float4(cwCausUV(FP.xz, hgt, uSun), 0, 1.0)).rgb;
    float3 Lfloor = cwFloorRadianceUnder(FP.xz, depthHere, hgt, alb, uSun, caus, sunShade);
    float3 under = Lfloor * exp(-SIG_T * s) + cwInscatter(depthHere, s, tr, uSun);

    // something standing in the water (an avatar) in front of the floor? take it from the grab texture
    float2 uvD = i.screenPos.xy / i.screenPos.w;
    float2 uvG = i.grabPos.xy / i.grabPos.w;
    float sceneDist = sceneDistance(uvD, rdWorld);
    // floor distance along the unrefracted ray, to tell the seabed mesh apart from objects above it
    float sv = (-cwFloorDepth(P.xz) - P.y) / wd.y;
    [unroll] for (int q = 0; q < 2; q++) { float2 fxz = P.xz + wd.xz * sv; sv = (-cwFloorDepth(fxz) - P.y) / wd.y; }
    float dObj = sceneDist - t;
    s = min(s, 30.0);
    if (dObj > 0.0 && dObj < sv - 0.06)
    {
        // refraction offset in screen space, undone where it would pick up something that is not the
        // submerged object (above the water, or the seabed, which is not shaded under the water)
        float2 off = -slope * 0.06 * saturate(dObj);
        float gsign = _ProjectionParams.x;
        #if UNITY_UV_STARTS_AT_TOP
        gsign = -gsign;
        #endif
        float dOff = sceneDistance(uvD + off, rdWorld) - t;
        if (dOff < 0.0 || dOff > sv - 0.06) off = 0;
        float4 objG = UNITY_SAMPLE_SCREENSPACE_TEXTURE(_CWGrabWater, uvG + float2(off.x, off.y * gsign));
        if (objG.a < 0.5) objG = UNITY_SAMPLE_SCREENSPACE_TEXTURE(_CWGrabWater, uvG);
        // alpha 0 = skipped seabed (its coarse mesh can sit in front of the traced floor at rock edges)
        if (objG.a >= 0.5)
        {
            under = cwInvTonemap(objG.rgb) * exp(-SIG_T * dObj) + cwInscatter(depthHere, dObj, tr, uSun);
            s = dObj;
        }
    }

    // suspended specks at three depths: tiny sunlit particles that give the water column volume
    float Ts = 1.0 - cwFresnel(uSun.y, CW_IOR);
    [unroll] for (int k = 0; k < 3; k++)
    {
        float dz = 0.22 + 0.38 * float(k);
        float tt = dz / max(-tr.y, 0.05);
        float2 qq = (P.xz + tr.xz * tt) * 48.0 + float2(uTime * (0.05 + 0.03 * float(k)), uTime * 0.02) + float(k) * 17.0;
        float2 id = floor(qq), f = frac(qq) - 0.5;
        float r = cwHash12(id + float(k) * 13.1);
        float2 of = float2(cwHash12(id + 3.1), cwHash12(id + 7.7)) - 0.5;
        float fw = fwidth(qq.x) + fwidth(qq.y);
        float dot_ = smoothstep(0.10 + fw, 0.0, length(f - of * 0.6)) * step(0.988, r) * step(tt, s);
        float fade = exp(-SIG_T.g * tt * 2.0) * smoothstep(1.2, 0.3, fw);
        under += dot_ * fade * SUN * Ts * 0.022 * lerp(float3(0.9, 1.0, 0.95), float3(0.4, 0.35, 0.3), step(0.992, r));
    }

    float3 col = F * refl + (1.0 - F) * under + spec;

    // ---- shoreline whitewater ----
    // (the grab read uses implicit derivatives, so it has to happen outside the branch or the compiler
    // flattens the branch and every pixel pays for the foam)
    float3 behindGrab = UNITY_SAMPLE_SCREENSPACE_TEXTURE(_CWGrabWater, i.grabPos.xy / i.grabPos.w).rgb;
    // (worked out here, late: carried through the whole shader its values cost ~2 ms/eye in register pressure)
    float boreBand = 0.0;
    [branch] if (_ShoreWaves > 0.5) boreBand = cwBoreBand(cwShoreUV(P.xz));
    [branch] if (shore.breaking + shore.swash + boreBand > 0.0 || P.y + fd.y < 0.16) // (and any thin water: its floor is the seabed's)
    {
        // on a breaking crest and in the churned water just behind it; handed over to the run-up
        // foam where the sheet takes over, so the two never show as separate bands
        float ph = shore.phase;
        float crest = smoothstep(-1.0, -0.2, ph) * exp(-max(ph, 0.0) * 0.6) * step(-1.0, ph);
        float crestCover = saturate(shore.breaking * 1.6) * crest * 0.8 * (1.0 - 0.6 * shore.swash);
        // run-up: densest right at the leading edge, thinning out behind it
        float thick = max(P.y + fd.y - shore.lift, 0.0); // water over the ground (or an obstacle's skirt), not counting the whitewater standing on it
        float2 suv = cwShoreUV(P.xz); // shore coordinates: u out to sea, v along the shore
        float surge = swPix.surge, run = swPix.run;
        // (the thin edge keeps its foam a little further out than the run-up sheet reaches, so when the backwash
        // leaves the water edge below the sheet zone the foam on the sand still carries on into the water)
        float edgeGate = max(shore.swash, 1.0 - saturate((suv.x - cwWaterlineU() - 1.5) / 3.0));
        // (as dense at the very edge as the foam the sheet leaves on the sand, and thinning out over the first 8 cm of
        // depth, so the lace runs on from the sand into the water)
        float cover = edgeGate * 0.45 * exp(-thick / 0.08) * smoothstep(0.075, 0.04, thick) + shore.swash * 0.35 * surge * exp(-thick / 0.07);
        // (gone by 7.5 cm of water: past 8 cm this branch only runs in the sheet zone, and the foam must not end in a line)
        cover *= cwFoamAlong(suv.y, _SwashClock) * smoothstep(0.05, 0.6, cwShoreExposure(suv.y)); // (no lace where no waves come)
        cover += 0.5 * saturate(shore.lift / max(_FoamLift, 1e-3)) * shore.swash; // the lip is whitewater
        // A real water edge is no clean curve: it frays into fingers and a rim of bubbles. The sheet ends where its
        // thickness, less a lace pattern riding with the water, runs out (up to 3 cm of water, ~12 cm up the slope),
        // and a thin rim of foam follows that ragged line.
        float thickSand = P.y + floorP + cwRock(P.xz); // (the ground's water, not an obstacle's skirt: this fades into the beach)
        // Under thin water the floor is the seabed's own render (the grab), the same sand, film and foam as on the
        // beach beside it; the water only adds its surface (reflection, glints) and a little absorption. Deeper, over
        // 5-15 cm, the water's own floor trace with its caustics takes over.
        {
            float3 floorThin = cwInvTonemap(behindGrab) * exp(-SIG_T * max(thickSand, 0.0) * 2.0);
            float3 colThin = F * refl + (1.0 - F) * floorThin + spec;
            col = lerp(colThin, col, smoothstep(0.05, 0.15, thickSand));
        }
        float2 eq = float2(suv.y, -(suv.x + run * shore.swash)) * 3.0;
        float rag = saturate(cwFbm2(eq) * 1.4 - 0.35) * 0.03 + cwNoise(eq * 6.0) * 0.008;
        float thickEdge = thickSand - rag * saturate(1.5 - footprint * 15.0);
        cover += 0.55 * exp(-max(thickEdge, 0.0) / 0.006) * step(0.0, thickEdge) * edgeGate;
        cover = min(cover, 0.65); // (never a solid slab: even the densest foam keeps its holes and lace)
        // pushed shoreward by the surge; the backwash drags it into streaks down the slope (foam laid out in shore
        // coordinates: along the shore, and up the beach)
        // the breaking crest is a narrow band: soft whitewater only, no lace (it would shred into lines). Round an
        // obstacle (where it, not the ground, makes the water shallow) the broken water is lace swirling round it.
        crestCover *= cwFoamAlong(suv.y, _SwashClock);
        float obst = saturate((fd.x - fd.y) * 3.0);
        float cc = crestCover * lerp(1.0, 0.7, obst);
        cc = max(cc, boreBand); // the next bore running in: soft whitewater, like a breaking crest
        cover = max(cover, 0.6 * boreBand); // ...threaded with lace
        float foam = cwWhitewater(P.xz, cover, cc, run, shore.swash, obst, footprint);
        // relief: the density's slope from two samples beside the pixel (a pixel apart at least, so it never
        // aliases), fading out before the bubbles get smaller than a pixel
        float2 g = 0;
        float reliefW = saturate(1.5 - footprint * 12.0);
        [branch] if (foam > 0.01 && _FoamRelief > 0.0 && reliefW > 0.0)
        {
            float e = max(0.04, footprint);
            g = float2(cwWhitewater(P.xz + float2(e, 0), cover, cc, run, shore.swash, obst, footprint) - foam,
                       cwWhitewater(P.xz + float2(0, e), cover, cc, run, shore.swash, obst, footprint) - foam) / e * reliefW;
        }
        // thin foam lets the water through; dense foam is brighter, its thin edges darker
        col = lerp(col, cwFoamLit(foam, g, n, uSun, v), saturate(foam * 1.15) * 0.9);
        // the last few millimetres of the sheet fade into the wet beach behind it, no hard clip line
        float3 behind = cwInvTonemap(behindGrab);
        // (only at the edge over sand: rock standing out of the water is cut by the depth buffer instead)
        col = lerp(behind, col, smoothstep(0.0, 0.05, thickEdge) * 0.9 + 0.1 * saturate(thickEdge * 100.0));
    }

    // distant haze over the water
    float haze = 1.0 - exp(-dist * 0.004);
    float muh = max(dot(normalize(float3(wd.x, 0.0, wd.z)), uSun), 0.0);
    float3 hazeC = float3(0.60, 0.71, 0.82) + float3(1.0, 0.86, 0.66) * (0.22 * pow(muh, 6.0) + 0.3 * pow(muh, 64.0));
    // ...and all the way into it over the outer half of the plane, so a small sea shows no edge (the sky
    // below the horizon is the same haze)
    float edge = smoothstep(0.5, 1.0, max(abs(P.x), abs(P.z)) / _SeaHalfSize);
    col = lerp(col, hazeC * 0.95, max(haze * 0.8, edge));

    // sky above the horizon (only reached at grazing angles, so only evaluated there)
    float hz = smoothstep(-0.0005, 0.0015, rd.y);
    [branch] if (hz > 0.0)
    {
        float3 skyc = cwSkyFw(rd, uSun, fwRd);
        float mu = dot(rd, uSun);
        float disc = smoothstep(0.99996, 0.999985, mu);
        [branch] if (disc > 0.0) skyc += SUN * 18.0 * disc * cwCloudSunT(rd);
        col = lerp(col, skyc, hz);
    }

    return float4(cwTonemap(max(col, 0.0)), 1.0);
}
#endif
