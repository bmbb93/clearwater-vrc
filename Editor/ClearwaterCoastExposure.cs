using UnityEngine;

/// <summary>
/// How exposed each stretch of shore is to the swell. The swell comes in from the open sea from one direction, spread
/// over a range of angles; a shore gets the part of it that reaches it unobstructed (rays cast back up-wave from just
/// off the shore, stopped by the coast itself: a bay's head sees only what comes in through its mouth), each ray
/// counted by the cosine of its angle to the shore (the energy it brings per metre of shore: a wall the waves run
/// along gets next to nothing). Over what an open shore square to the swell gets, and as a height (the square root of
/// the energy), smoothed along the shore a little as waves bend round into shadow (diffraction).
/// </summary>
public static class ClearwaterCoastExposure
{
    /// <summary>The height factor 0..1 at shore samples every step metres along the line.</summary>
    /// <param name="pts">the line in water space (xy); the sea is on its right</param>
    /// <param name="waveTo">the way the swell travels (water space, unit)</param>
    /// <param name="spread">degrees: the spread of the swell's directions (a standard deviation)</param>
    /// <param name="reach">m: how far back toward the open sea a ray has to get clear</param>
    /// <param name="smooth">m along the shore the result is smoothed over</param>
    /// <param name="step">m between samples (the first at the line's start); at = where they are</param>
    public static float[] Compute(Vector2[] pts, bool closed, Vector2 waveTo, float spread, float reach, float smooth,
                                  float step, out Vector2[] at)
    {
        // the coast as segments (an open line runs on straight past its ends, as the shore coordinates do)
        int n = pts.Length;
        var segA = new System.Collections.Generic.List<Vector2>();
        var segB = new System.Collections.Generic.List<Vector2>();
        for (int k = 0; k < (closed ? n : n - 1); k++) { segA.Add(pts[k]); segB.Add(pts[(k + 1) % n]); }
        if (!closed)
        {
            Vector2 d0 = (pts[0] - pts[1]).normalized, d1 = (pts[n - 1] - pts[n - 2]).normalized;
            segA.Add(pts[0]); segB.Add(pts[0] + d0 * reach * 4f);
            segA.Add(pts[n - 1]); segB.Add(pts[n - 1] + d1 * reach * 4f);
        }

        // samples along the line: position, seaward normal
        var pos = new System.Collections.Generic.List<Vector2>();
        var nrm = new System.Collections.Generic.List<Vector2>();
        float total = 0;
        for (int k = 0; k < (closed ? n : n - 1); k++) total += Vector2.Distance(pts[k], pts[(k + 1) % n]);
        int count = Mathf.Max(2, Mathf.CeilToInt(total / step - 1e-3f) + 1);
        {
            int k = 0; float along = 0;
            for (int i = 0; i < count; i++)
            {
                float s = Mathf.Min(i * step, total);
                while (k < (closed ? n : n - 1) - 1 && along + Vector2.Distance(pts[k], pts[(k + 1) % n]) < s)
                { along += Vector2.Distance(pts[k], pts[(k + 1) % n]); k++; }
                Vector2 a = pts[k], b = pts[(k + 1) % n];
                float len = Mathf.Max(Vector2.Distance(a, b), 1e-4f);
                pos.Add(Vector2.Lerp(a, b, Mathf.Clamp01((s - along) / len)));
                nrm.Add(Right(b - a));
            }
        }
        // (normals averaged over a few metres, so a sharp corner does not flip the rays)
        var nrmS = new Vector2[count];
        int win = Mathf.Max(1, Mathf.RoundToInt(3f / step));
        for (int i = 0; i < count; i++)
        {
            Vector2 acc = Vector2.zero;
            for (int j = -win; j <= win; j++)
            {
                int q = i + j;
                if (closed) q = ((q % count) + count) % count; else q = Mathf.Clamp(q, 0, count - 1);
                acc += nrm[q];
            }
            nrmS[i] = acc.normalized;
        }

        // the swell's directions and their weights
        const int K = 25;
        float half = Mathf.Min(2.5f * spread, 85f);
        var dirs = new Vector2[K]; var w = new float[K];
        float reference = 0;
        for (int j = 0; j < K; j++)
        {
            float a = Mathf.Lerp(-half, half, j / (K - 1f));
            w[j] = Mathf.Exp(-0.5f * a * a / Mathf.Max(spread * spread, 1f));
            float r = a * Mathf.Deg2Rad;
            // back up-wave: the opposite of the way the swell travels, turned by a
            dirs[j] = -new Vector2(waveTo.x * Mathf.Cos(r) - waveTo.y * Mathf.Sin(r), waveTo.x * Mathf.Sin(r) + waveTo.y * Mathf.Cos(r));
            reference += w[j] * Mathf.Cos(r);
        }

        var e = new float[count];
        for (int i = 0; i < count; i++)
        {
            Vector2 start = pos[i] + nrmS[i] * 1.0f;
            float flux = 0;
            for (int j = 0; j < K; j++)
            {
                float c = Vector2.Dot(nrmS[i], dirs[j]);
                if (c <= 0) continue; // from behind the shore
                if (!Blocked(start, dirs[j] * reach, segA, segB)) flux += w[j] * c;
            }
            e[i] = flux / reference;
        }
        // smoothed along the shore (the waves bend round into the lee), then as a height
        var h = new float[count];
        float sig = Mathf.Max(smooth / step, 0.01f);
        int rad = Mathf.CeilToInt(3f * sig);
        for (int i = 0; i < count; i++)
        {
            float acc = 0, wsum = 0;
            for (int j = -rad; j <= rad; j++)
            {
                int q = i + j;
                if (closed) q = ((q % count) + count) % count; else if (q < 0 || q >= count) continue;
                float g = Mathf.Exp(-0.5f * j * j / (sig * sig));
                acc += g * e[q]; wsum += g;
            }
            h[i] = Mathf.Clamp01(Mathf.Sqrt(Mathf.Max(acc / wsum, 0f)));
        }
        at = pos.ToArray();
        return h;
    }

    static Vector2 Right(Vector2 d) { d.Normalize(); return new Vector2(d.y, -d.x); }

    // does the ray from p along d (its length the ray's) cross any of the segments?
    static bool Blocked(Vector2 p, Vector2 d, System.Collections.Generic.List<Vector2> A, System.Collections.Generic.List<Vector2> B)
    {
        float tMin = 0.3f / Mathf.Max(d.magnitude, 1e-3f); // (not the shore it starts from)
        for (int k = 0; k < A.Count; k++)
        {
            Vector2 e = B[k] - A[k];
            float den = d.x * e.y - d.y * e.x;
            if (Mathf.Abs(den) < 1e-9f) continue;
            Vector2 f = A[k] - p;
            float t = (f.x * e.y - f.y * e.x) / den;  // along the ray
            float s = (f.x * d.y - f.y * d.x) / den;  // along the segment
            if (t > tMin && t <= 1f && s >= 0f && s <= 1f) return true;
        }
        return false;
    }
}
