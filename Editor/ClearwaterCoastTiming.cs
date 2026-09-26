using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// When the swell reaches each stretch of shore. It comes in from the open sea as a straight crest and slows over the
/// shallows (speed sqrt(g h), the cross-section's depth at each point's distance from the coast), so it bends round
/// into bays and sweeps along a shore it meets at an angle: the first-arrival time over a grid (fast marching on the
/// eikonal equation |grad T| = 1 / c), read just off the shore. Only the differences along the shore matter; they
/// are given relative to the shore nearest the coast object.
/// </summary>
public static class ClearwaterCoastTiming
{
    const float G = 9.81f;
    const int Cells = 384;      // per side of the grid
    const float OpenU = 40f;    // m from the coast: open sea, where the crest is still straight
    const float OffShore = 3f;  // m off the waterline the times are read at

    /// <summary>Seconds each shore sample (at, water space) is reached after the one nearest centre.</summary>
    public static float[] ShoreDelays(ClearwaterCoast coast, Vector4[] pts, Vector2 centre, Vector2 waveTo, Vector2[] at,
                                      bool closed, float smooth, float step)
    {
        int count = at.Length;
        float size = Mathf.Clamp(coast.areaSize * 1.5f, 200f, 1500f);
        int n = Cells;
        float cell = size / n;
        float c0 = Mathf.Sqrt(G * Mathf.Max(ClearwaterCoastBake.SectionDepth(coast, 1e4f), 0.1f));
        Vector2 origin = centre - Vector2.one * (size * 0.5f);

        // the shore coordinate u over the grid (the same bake the shaders use), and from it the depth and speed
        var field = ClearwaterCoastBake.BakeCoastField(pts, closed, centre, size, n, TextureFormat.RGFloat);
        var px = field.GetPixels();
        Object.DestroyImmediate(field);
        var slow = new float[n * n];   // seconds per metre (infinite on land)
        var T = new float[n * n];
        var state = new byte[n * n];   // 0 far, 1 in the band, 2 known
        var heap = new List<(float t, int i)>();
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                int k = j * n + i;
                float u = px[k].r;
                float depth = ClearwaterCoastBake.SectionDepth(coast, u);
                slow[k] = depth > 0.02f ? 1f / Mathf.Sqrt(G * Mathf.Max(depth, 0.05f)) : float.PositiveInfinity;
                T[k] = float.PositiveInfinity;
                if (u > OpenU && depth > 0.02f)
                {
                    // the open sea: the straight crest
                    Vector2 p = origin + new Vector2(i + 0.5f, j + 0.5f) * cell;
                    T[k] = Vector2.Dot(p - centre, waveTo) / c0;
                    state[k] = 2;
                }
            }
        // the band: water next to the open sea
        for (int k = 0; k < n * n; k++) if (state[k] == 2) Neighbours(k, n, T, slow, state, heap, cell);
        while (heap.Count > 0)
        {
            var (t, k) = Pop(heap);
            if (state[k] == 2 || t > T[k] + 1e-5f) continue;
            state[k] = 2;
            Neighbours(k, n, T, slow, state, heap, cell);
        }

        // read just off the shore. Outside the grid (or where nothing arrived): the straight crest out at OpenU, then
        // straight in across the shallows at their own speed (as the grid has it on an open straight shore)
        float shallows = 0;
        const int Steps = 200;
        for (int m = 0; m < Steps; m++)
        {
            float u = Mathf.Lerp(OffShore, OpenU, (m + 0.5f) / Steps);
            shallows += (OpenU - OffShore) / Steps / Mathf.Sqrt(G * Mathf.Max(ClearwaterCoastBake.SectionDepth(coast, u), 0.05f));
        }
        var times = new float[count];
        for (int s = 0; s < count; s++)
        {
            Vector2 a = at[Mathf.Max(s - 1, 0)], b = at[Mathf.Min(s + 1, count - 1)];
            if (closed) { a = at[(s - 1 + count) % count]; b = at[(s + 1) % count]; }
            Vector2 d = (b - a).normalized;
            Vector2 q = at[s] + new Vector2(d.y, -d.x) * OffShore; // (the sea is on the right in water space)
            times[s] = Sample(T, n, (q - origin) / cell, out bool ok);
            if (!ok) times[s] = Vector2.Dot(q + new Vector2(d.y, -d.x) * (OpenU - OffShore) - centre, waveTo) / c0 + shallows;
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
