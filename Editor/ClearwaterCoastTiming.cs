using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// When the swell reaches each stretch of shore. It crosses the deep ocean as a straight crest at the ocean's speed and
/// enters the grid round the coast through its up-wave edges, where they lie out at sea; there it slows to the coastal water's own speed
/// (sqrt(g h), the cross-section's depth at each point's distance from the coast), so it bends round into bays and,
/// meeting a shore at an angle, turns toward it as real swell does (Snell: the sine of its angle falls with its speed,
/// so a swell 60 degrees off the shore's normal out at sea breaks about 10 degrees off it). The first-arrival time over
/// the grid (fast marching on the eikonal equation |grad T| = 1 / c), read just off the shore; outside the grid, carried
/// on from the nearest shore inside it. Only the differences along the shore matter; they are given relative to the
/// shore nearest the coast object.
/// </summary>
public static class ClearwaterCoastTiming
{
    const float G = 9.81f;
    const int Cells = 384;      // per side of the grid
    const float OffShore = 3f;  // m off the waterline the times are read at
    // m/s: the swell crossing the deep ocean (an 8 s swell: g T / 2 pi). Starting from the coastal water's deepest
    // speed instead left its crests at twice the real angle to the shore where they break
    const float OceanSpeed = 12.5f;

    /// <summary>Seconds each shore sample (at, water space) is reached after the one nearest centre.</summary>
    public static float[] ShoreDelays(ClearwaterCoast coast, Vector4[] pts, Vector2 centre, Vector2 waveTo, Vector2[] at,
                                      bool closed, float smooth, float step)
    {
        int count = at.Length;
        float size = Mathf.Clamp(coast.areaSize * 1.5f, 200f, 1500f);
        int n = Cells;
        float cell = size / n;
        Vector2 origin = centre - Vector2.one * (size * 0.5f);

        // the shore coordinate u over the grid (the same bake the shaders use), and from it the depth and speed
        var field = ClearwaterCoastBake.BakeCoastField(pts, closed, centre, size, n, TextureFormat.RGFloat);
        var px = field.GetPixels();
        Object.DestroyImmediate(field);
        var slow = new float[n * n];   // seconds per metre (infinite on land)
        var T = new float[n * n];
        var state = new byte[n * n];   // 0 far, 1 in the band, 2 known
        var heap = new List<(float t, int i)>();
        // how far out the grid's edge reaches: it lets the swell in only out at sea (an edge crossing the coast would let
        // the ocean's crest in right by the shore, running along it)
        float maxU = 0f;
        for (int k = 0; k < n * n; k++)
        {
            int i = k % n, j = k / n;
            if (i == 0 || j == 0 || i == n - 1 || j == n - 1) maxU = Mathf.Max(maxU, px[k].r);
        }
        float seaU = Mathf.Max(40f, 0.95f * maxU);
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                int k = j * n + i;
                float u = px[k].r;
                float depth = ClearwaterCoastBake.SectionDepth(coast, u);
                slow[k] = depth > 0.02f ? 1f / Mathf.Sqrt(G * Mathf.Max(depth, 0.05f)) : float.PositiveInfinity;
                T[k] = float.PositiveInfinity;
                // the swell comes in through the grid's up-wave edges (their water), a straight crest from the ocean
                bool edge = i == 0 || j == 0 || i == n - 1 || j == n - 1;
                if (edge && depth > 0.02f && u >= seaU)
                {
                    Vector2 outward = new Vector2(i == 0 ? -1 : i == n - 1 ? 1 : 0, j == 0 ? -1 : j == n - 1 ? 1 : 0).normalized;
                    if (Vector2.Dot(outward, waveTo) < 0f)
                    {
                        Vector2 p = origin + new Vector2(i + 0.5f, j + 0.5f) * cell;
                        T[k] = Vector2.Dot(p - centre, waveTo) / OceanSpeed;
                        state[k] = 2;
                    }
                }
            }
        // the band: water next to where it comes in
        for (int k = 0; k < n * n; k++) if (state[k] == 2) Neighbours(k, n, T, slow, state, heap, cell);
        while (heap.Count > 0)
        {
            var (t, k) = Pop(heap);
            if (state[k] == 2 || t > T[k] + 1e-5f) continue;
            state[k] = 2;
            Neighbours(k, n, T, slow, state, heap, cell);
        }

        // read just off the shore
        var times = new float[count];
        var got = new bool[count];
        bool any = false;
        for (int s = 0; s < count; s++)
        {
            Vector2 a = at[Mathf.Max(s - 1, 0)], b = at[Mathf.Min(s + 1, count - 1)];
            if (closed) { a = at[(s - 1 + count) % count]; b = at[(s + 1) % count]; }
            Vector2 d = (b - a).normalized;
            Vector2 q = at[s] + new Vector2(d.y, -d.x) * OffShore; // (the sea is on the right in water space)
            times[s] = Sample(T, n, (q - origin) / cell, out got[s]);
            any |= got[s];
        }
        // outside the grid (or where nothing arrived): carried on from the nearest shore that has a time, as the
        // straight crest from the ocean sweeps along the coast (or the ocean crest alone, if none has)
        for (int s = 0; s < count; s++)
        {
            if (got[s]) continue;
            if (!any) { times[s] = Vector2.Dot(at[s] - centre, waveTo) / OceanSpeed; continue; }
            int from = -1;
            for (int r = 1; r < count && from < 0; r++)
            {
                int lo = s - r, hi = s + r;
                if (closed) { lo = ((lo % count) + count) % count; hi %= count; }
                if (lo >= 0 && got[lo]) from = lo; else if (hi < count && got[hi]) from = hi;
            }
            times[s] = times[from] + Vector2.Dot(at[s] - at[from], waveTo) / OceanSpeed;
        }
        // relative to the shore nearest the coast object, smoothed a little along the shore
        int nearest = 0; float best = float.MaxValue;
        for (int s = 0; s < count; s++) { float dd = (at[s] - centre).sqrMagnitude; if (dd < best) { best = dd; nearest = s; } }
        float t0 = times[nearest];
        var outp = new float[count];
        float sig = Mathf.Max(smooth / step, 0.01f);
        int rad = Mathf.CeilToInt(3f * sig);
        for (int s = 0; s < count; s++)
        {
            float acc = 0, w = 0;
            for (int j = -rad; j <= rad; j++)
            {
                int q = s + j;
                if (closed) q = ((q % count) + count) % count; else if (q < 0 || q >= count) continue;
                float g = Mathf.Exp(-0.5f * j * j / (sig * sig));
                acc += g * times[q]; w += g;
            }
            outp[s] = acc / w - t0;
        }
        return outp;
    }

    // the first-order eikonal update of k's four neighbours
    static void Neighbours(int k, int n, float[] T, float[] slow, byte[] state, List<(float, int)> heap, float h)
    {
        int i = k % n, j = k / n;
        Update(i - 1, j, n, T, slow, state, heap, h);
        Update(i + 1, j, n, T, slow, state, heap, h);
        Update(i, j - 1, n, T, slow, state, heap, h);
        Update(i, j + 1, n, T, slow, state, heap, h);
    }

    static void Update(int i, int j, int n, float[] T, float[] slow, byte[] state, List<(float, int)> heap, float h)
    {
        if (i < 0 || j < 0 || i >= n || j >= n) return;
        int k = j * n + i;
        if (state[k] == 2 || float.IsInfinity(slow[k])) return;
        float a = Mathf.Min(i > 0 ? Known(T, state, k - 1) : float.PositiveInfinity, i < n - 1 ? Known(T, state, k + 1) : float.PositiveInfinity);
        float b = Mathf.Min(j > 0 ? Known(T, state, k - n) : float.PositiveInfinity, j < n - 1 ? Known(T, state, k + n) : float.PositiveInfinity);
        float f = h * slow[k];
        float t;
        if (float.IsInfinity(a) && float.IsInfinity(b)) return;
        if (float.IsInfinity(a) || float.IsInfinity(b) || Mathf.Abs(a - b) >= f) t = Mathf.Min(a, b) + f;
        else t = 0.5f * (a + b + Mathf.Sqrt(2f * f * f - (a - b) * (a - b)));
        if (t < T[k]) { T[k] = t; state[k] = 1; Push(heap, (t, k)); }
    }

    static float Known(float[] T, byte[] state, int k) => state[k] == 2 ? T[k] : float.PositiveInfinity;

    static float Sample(float[] T, int n, Vector2 g, out bool ok)
    {
        ok = false;
        float x = g.x - 0.5f, y = g.y - 0.5f;
        int i = Mathf.FloorToInt(x), j = Mathf.FloorToInt(y);
        if (i < 0 || j < 0 || i >= n - 1 || j >= n - 1) return 0f;
        float fx = x - i, fy = y - j, acc = 0, w = 0;
        for (int dj = 0; dj <= 1; dj++)
            for (int di = 0; di <= 1; di++)
            {
                float t = T[(j + dj) * n + i + di];
                if (float.IsInfinity(t)) continue; // (land: the water corners only)
                float ww = (di == 1 ? fx : 1f - fx) * (dj == 1 ? fy : 1f - fy) + 1e-4f;
                acc += ww * t; w += ww;
            }
        if (w <= 0f) return 0f;
        ok = true;
        return acc / w;
    }

    // a binary min-heap on the time
    static void Push(List<(float t, int i)> h, (float, int) e)
    {
        h.Add(e);
        int c = h.Count - 1;
        while (c > 0) { int p = (c - 1) / 2; if (h[p].t <= h[c].t) break; (h[p], h[c]) = (h[c], h[p]); c = p; }
    }

    static (float t, int i) Pop(List<(float t, int i)> h)
    {
        var top = h[0];
        int last = h.Count - 1;
        h[0] = h[last]; h.RemoveAt(last);
        int c = 0;
        while (true)
        {
            int l = 2 * c + 1, r = l + 1, m = c;
            if (l < h.Count && h[l].t < h[m].t) m = l;
            if (r < h.Count && h[r].t < h[m].t) m = r;
            if (m == c) break;
            (h[m], h[c]) = (h[c], h[m]); c = m;
        }
        return top;
    }
}
