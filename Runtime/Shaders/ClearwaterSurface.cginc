// Clearwater: the height of the displaced water surface, and which side of it each pixel's view starts on.
// A camera within a hand's width of the water has its lens half in, half out: each pixel's ray then starts where
// it crosses the camera's near clip plane, above or below the waves there. The water, the seabed and the
// underwater fog each draw only their side of that line, which gives the split "over-under" view (and no gaps
// where the water plane itself is closer than the near clip).
#ifndef CLEARWATER_SURFACE_INCLUDED
#define CLEARWATER_SURFACE_INCLUDED
#include "ClearwaterShore.cginc"

sampler2D _Surf;
float4 _Surf_TexelSize;

// Cameras closer than this (m) to the mean surface decide above / below per pixel; it holds the waves, the
// run-up and the near clip plane. Keep equal to ClearwaterController.SurfaceBand.
#define CW_SURFACE_BAND 0.6

// GLSL mat2 products from the demo, written out
float2 mulM(float2 v)   { return float2(0.8 * v.x + 0.6 * v.y, -0.6 * v.x + 0.8 * v.y); }
float2 mulMt(float2 v)  { return float2(0.8 * v.x - 0.6 * v.y, 0.6 * v.x + 0.8 * v.y); }
float2 mulM2(float2 v)  { return float2(0.28 * v.x - 0.96 * v.y, 0.96 * v.x + 0.28 * v.y); }
float2 mulM2t(float2 v) { return float2(0.28 * v.x + 0.96 * v.y, -0.96 * v.x + 0.28 * v.y); }

// Surface height (water space, m) over xz: the sum the water shader's ray intersection settles on
float cwSurfaceHeight(float2 xz)
{
    const float SC = 0.41, WB = 0.10;
    float4 A = tex2Dlod(_Surf, float4(xz / _PatchSize, 0, 0));
    float4 B = tex2Dlod(_Surf, float4(mulM(xz) / (_PatchSize * SC) + 0.37, 0, 0));
    float4 R = tex2Dlod(_Rip, float4((xz - _RipCenter.xy) / _RipSize + 0.5, 0, 0));
    CwShore s = cwShore(xz, cwFloorDepth2(xz).y);
    return (A.x + WB * SC * B.x) * CW_WAVE * (1.0 - 0.75 * s.swash) + R.x + s.eta;
}

bool cwCameraNearSurface(float waterY) { return abs(_WorldSpaceCameraPos.y - waterY) < CW_SURFACE_BAND; }

// Is this camera in this water (seen from below, fogged)? A pool: only in its footprint and above its floor
// (cwInBody) - not a camera on the storey under it. The sea: anywhere but in a pool (the pool mask) - not a camera
// in a pool below the sea's level.
bool cwCamInBody()
{
    float3 c = _WorldSpaceCameraPos;
    [branch] if (_BodyFloor.w > 0.0) return cwInBody(c);
    return !cwInPool(c);
}

// where the ray of this pixel (world direction) leaves the camera: on its near clip plane
float3 cwNearPoint(float3 rdWorld)
{
    float3 fwd = -UNITY_MATRIX_V[2].xyz;
    return _WorldSpaceCameraPos + rdWorld * (_ProjectionParams.y / max(dot(rdWorld, fwd), 1e-3));
}

// Does this pixel's view start under the water? Per camera when it is well clear of the surface; per pixel near
// it (the start point under the waves there, and above the ground: no water inside the beach).
bool cwPixelUnder(float3 rdWorld, float3 waterOrigin)
{
    float camY = _WorldSpaceCameraPos.y - waterOrigin.y;
    if (abs(camY) >= CW_SURFACE_BAND) return camY < 0.0;
    float3 u = cwToJS(cwNearPoint(rdWorld) - waterOrigin);
    return u.y < cwSurfaceHeight(u.xz) && u.y > -cwFloorDepth(u.xz);
}

#endif
