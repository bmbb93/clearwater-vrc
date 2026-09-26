using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The coast's user terrain (Terrain source = User): the user's own meshes as the ground round the walkable area.
/// Baked from above into heights the shaders shape the water, the waves and the seabed with (the mesh itself stays
/// what is seen: ADR 0001), with the seam round it where the generated terrain is brought to its edge; and the
/// waterline found on it (where its top crosses the still water) spliced into the coast line in place of the part
/// drawn through the walkable area.
/// </summary>
public static class ClearwaterUserTerrain
{
    public class Data
    {
        public Texture2D tex;      // RG: height (edge carried outward), weight (1 on the mesh, 0 past the seam)
        public Vector4 area;       // xy = centre (water space), z = size, w = 1
        public Vector4 mean;       // its average colour (linear)
        public int res;
        public float[] h;          // the heights (edge carried outward)
        public bool[] on;          // where the mesh is
        public float slope;        // the beach face's slope at the waterline (median)
        public Vector2 Pos(float i, float j) => new Vector2(area.x + ((i + 0.5f) / res - 0.5f) * area.z, area.y + ((j + 0.5f) / res - 0.5f) * area.z);
        public float At(Vector2 p)
        {
            float fi = ((p.x - area.x) / area.z + 0.5f) * res - 0.5f, fj = ((p.y - area.y) / area.z + 0.5f) * res - 0.5f;
            int i = Mathf.Clamp(Mathf.RoundToInt(fi), 0, res - 1), j = Mathf.Clamp(Mathf.RoundToInt(fj), 0, res - 1);
            return h[j * res + i];
        }
    }

    const float Texel = 0.2f; // m: the finest the heights are baked at

    /// <summary>The meshes that make the user terrain (enabled mesh renderers under the coast's User terrain).</summary>
    public static List<MeshRenderer> Renderers(ClearwaterCoast coast)
    {
        var list = new List<MeshRenderer>();
        if (coast == null || coast.userTerrain == null) return list;
        foreach (var r in coast.userTerrain.GetComponentsInChildren<MeshRenderer>(false))
        {
            var mf = r.GetComponent<MeshFilter>();
            if (r.enabled && mf != null && mf.sharedMesh != null) list.Add(r);
        }
        return list;
    }

    /// <summary>Bakes the user terrain round the walkable area (null when the coast has none).</summary>
    public static Data Bake(ClearwaterController ctl, ClearwaterCoast coast)
    {
        if (coast.terrainSource != ClearwaterCoast.TerrainSource.User) return null;
        var renderers = Renderers(coast);
        if (renderers.Count == 0)
        {
            Debug.LogWarning("[Clearwater] Terrain source is User, but the User terrain has no meshes: the generated terrain is used.");
            return null;
        }
        Vector3 origin = ctl.water.position;
        float half = Mathf.Max(coast.groundHalfSize, 1f), seam = Mathf.Max(coast.seamWidth, 1f), step = Mathf.Max(coast.groundStep, 0.05f);
        Vector3 c = coast.transform.position - origin;
        c = new Vector3(Mathf.Round(c.x / step) * step, 0, Mathf.Round(c.z / step) * step);
        float size = 2f * (half + seam);
        int res = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.CeilToInt(size / Texel)), 256, 2048);
        var d = new Data { area = new Vector4(c.x, -c.z, size, 1), res = res };

        // the tops, from above (the stamps' bake, its user terrain pass)
        var mat = new Material(Shader.Find("Hidden/Clearwater/StampBake"));
        mat.SetVector("_StampArea", d.area);
        mat.SetVector("_WaterOrigin", origin);
        var rt = new RenderTexture(res, res, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        var cmd = new UnityEngine.Rendering.CommandBuffer { name = "Clearwater user terrain" };
        cmd.SetRenderTarget(rt);
        cmd.ClearRenderTarget(false, true, new Color(-50, 0, 0, 0));
        cmd.SetViewProjectionMatrices(Matrix4x4.identity, GL.GetGPUProjectionMatrix(Matrix4x4.identity, false));
        foreach (var r in renderers)
        {
            var mesh = r.GetComponent<MeshFilter>().sharedMesh;
            for (int sub = 0; sub < mesh.subMeshCount; sub++) cmd.DrawRenderer(r, mat, sub, 3);
        }
        Graphics.ExecuteCommandBuffer(cmd);
        cmd.Release();
        var prev = RenderTexture.active;
        var read = new Texture2D(res, res, TextureFormat.RGBAFloat, false, true);
        RenderTexture.active = rt;
        read.ReadPixels(new Rect(0, 0, res, res), 0, 0); read.Apply(false);
        RenderTexture.active = prev; rt.Release();
        var px = read.GetPixels();
        Object.DestroyImmediate(read);
        Object.DestroyImmediate(mat);

        int n = res * res;
        d.h = new float[n]; d.on = new bool[n];
        for (int k = 0; k < n; k++) { d.on[k] = px[k].g > 0.5f; d.h[k] = px[k].r; }

        // past its edge: the nearest edge's height, and a weight falling to 0 across the seam (chamfer distance)
        var dist = new float[n];
        var near = new int[n];
        for (int k = 0; k < n; k++) { dist[k] = d.on[k] ? 0f : float.MaxValue; near[k] = d.on[k] ? k : -1; }
        void Relax(int k, int q, float cost)
        {
            if (near[q] < 0) return;
            float nd = dist[q] + cost;
            if (nd < dist[k]) { dist[k] = nd; near[k] = near[q]; }
        }
        const float D1 = 1f, D2 = 1.41421356f;
        for (int j = 0; j < res; j++)
            for (int i = 0; i < res; i++)
            {
                int k = j * res + i;
                if (i > 0) Relax(k, k - 1, D1);
                if (j > 0) { Relax(k, k - res, D1); if (i > 0) Relax(k, k - res - 1, D2); if (i < res - 1) Relax(k, k - res + 1, D2); }
            }
        for (int j = res - 1; j >= 0; j--)
            for (int i = res - 1; i >= 0; i--)
            {
                int k = j * res + i;
                if (i < res - 1) Relax(k, k + 1, D1);
                if (j < res - 1) { Relax(k, k + res, D1); if (i < res - 1) Relax(k, k + res + 1, D2); if (i > 0) Relax(k, k + res - 1, D2); }
            }
        float texel = size / res;
        var outPx = new Color[n];
        for (int k = 0; k < n; k++)
        {
            float hk = near[k] >= 0 ? d.h[near[k]] : 0f;
            // (past the edge it stays below 0.97, so the shaders can tell the mesh itself - 1 - from its seam)
            float w = d.on[k] ? 1f : (near[k] >= 0 ? 0.96f * (1f - Mathf.SmoothStep(0f, 1f, dist[k] * texel / seam)) : 0f);
            d.h[k] = hk;
            outPx[k] = new Color(hk, w, 0, 1);
        }
        d.tex = new Texture2D(res, res, TextureFormat.RGHalf, false, true)
        { name = "UserTerrain", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        d.tex.SetPixels(outPx); d.tex.Apply(false, false);

        // the beach face's slope where it meets the water (for how far and how fast the swash runs up it)
        var slopes = new List<float>();
        for (int j = 1; j < res - 1; j++)
            for (int i = 1; i < res - 1; i++)
            {
                int k = j * res + i;
                if (!d.on[k] || Mathf.Abs(d.h[k]) > 0.25f) continue;
                float gx = (d.h[k + 1] - d.h[k - 1]) / (2f * texel), gy = (d.h[k + res] - d.h[k - res]) / (2f * texel);
                slopes.Add(Mathf.Sqrt(gx * gx + gy * gy));
            }
        slopes.Sort();
        d.slope = slopes.Count > 0 ? Mathf.Clamp(slopes[slopes.Count / 2], 0.02f, 1f) : 0f;

        d.mean = MeanColour(renderers);
        return d;
    }

    // the meshes' average colour: each material's colour times its main texture's mean, by the area each covers
    static Vector4 MeanColour(List<MeshRenderer> renderers)
    {
        Vector3 acc = Vector3.zero; float wsum = 0;
        foreach (var r in renderers)
        {
            float w = Mathf.Max(r.bounds.size.x * r.bounds.size.z, 1e-3f) / Mathf.Max(r.sharedMaterials.Length, 1);
            foreach (var m in r.sharedMaterials)
            {
                if (m == null) continue;
                Color col = m.HasProperty("_Color") ? m.color.linear : Color.white;
                Vector3 t = Vector3.one;
                if (m.mainTexture is Texture2D tex) t = ClearwaterBedLook.MeanFromGpu(tex);
                acc += Vector3.Scale(new Vector3(col.r, col.g, col.b), t) * w; wsum += w;
            }
        }
        Vector3 c = wsum > 0 ? acc / wsum : new Vector3(0.2f, 0.2f, 0.2f);
        return new Vector4(c.x, c.y, c.z, 1);
    }

    // ---------------------------------------------------------------- the waterline on it

    /// <summary>Where the mesh's top crosses the still water: polylines in water space, the sea on their right
    /// (marching squares over the cells the mesh covers).</summary>
    public static List<List<Vector2>> Waterlines(Data d)
    {
        int res = d.res;
        var segs = new List<(long a, long b, Vector2 pa, Vector2 pb)>();
        // edge keys: horizontal edge (i,j)-(i+1,j) = 2k, vertical (i,j)-(i,j+1) = 2k+1
        Vector2 Cross(int i0, int j0, int i1, int j1)
        {
            float a = d.h[j0 * res + i0], b = d.h[j1 * res + i1];
            float t = Mathf.Clamp01(a / (a - b));
            return d.Pos(Mathf.Lerp(i0, i1, t), Mathf.Lerp(j0, j1, t));
        }
        for (int j = 0; j < res - 1; j++)
            for (int i = 0; i < res - 1; i++)
            {
                int k = j * res + i;
                if (!d.on[k] || !d.on[k + 1] || !d.on[k + res] || !d.on[k + res + 1]) continue;
                bool b0 = d.h[k] > 0, b1 = d.h[k + 1] > 0, b2 = d.h[k + res + 1] > 0, b3 = d.h[k + res] > 0; // (corners counter-clockwise)
                int code = (b0 ? 1 : 0) | (b1 ? 2 : 0) | (b2 ? 4 : 0) | (b3 ? 8 : 0);
                if (code == 0 || code == 15) continue;
                long eB = 2L * k, eR = 2L * (k + 1) + 1, eT = 2L * (k + res), eL = 2L * k + 1; // bottom, right, top, left
                Vector2 pB = Cross(i, j, i + 1, j), pR = Cross(i + 1, j, i + 1, j + 1), pT = Cross(i, j + 1, i + 1, j + 1), pL = Cross(i, j, i, j + 1);
                void Add(long a, Vector2 pa, long b, Vector2 pb) => segs.Add((a, b, pa, pb));
                switch (code)
                {
                    case 1: case 14: Add(eL, pL, eB, pB); break;
                    case 2: case 13: Add(eB, pB, eR, pR); break;
                    case 3: case 12: Add(eL, pL, eR, pR); break;
                    case 4: case 11: Add(eR, pR, eT, pT); break;
                    case 6: case 9: Add(eB, pB, eT, pT); break;
                    case 7: case 8: Add(eL, pL, eT, pT); break;
                    case 5: case 10:
                    {
                        // a saddle: joined as the cell's centre lies
                        bool c = (d.h[k] + d.h[k + 1] + d.h[k + res] + d.h[k + res + 1]) > 0;
                        if ((code == 5) == c) { Add(eL, pL, eT, pT); Add(eB, pB, eR, pR); }
                        else { Add(eL, pL, eB, pB); Add(eR, pR, eT, pT); }
                        break;
                    }
                }
            }
        // join the pieces into lines
        var byEdge = new Dictionary<long, List<int>>();
        for (int s = 0; s < segs.Count; s++)
        {
            foreach (var e in new[] { segs[s].a, segs[s].b })
            {
                if (!byEdge.TryGetValue(e, out var l)) byEdge[e] = l = new List<int>();
                l.Add(s);
            }
        }
        var used = new bool[segs.Count];
        var lines = new List<List<Vector2>>();
        for (int s0 = 0; s0 < segs.Count; s0++)
        {
            if (used[s0]) continue;
            used[s0] = true;
            var fwd = new List<Vector2> { segs[s0].pa, segs[s0].pb };
            var back = new List<Vector2>();
            foreach (var (startEdge, list) in new[] { (segs[s0].b, fwd), (segs[s0].a, back) })
            {
                long e = startEdge;
                while (true)
                {
                    int next = -1;
                    foreach (var q in byEdge[e]) if (!used[q]) { next = q; break; }
                    if (next < 0) break;
                    used[next] = true;
                    bool fromA = segs[next].a == e;
                    list.Add(fromA ? segs[next].pb : segs[next].pa);
                    e = fromA ? segs[next].b : segs[next].a;
                }
            }
            back.Reverse();
            back.AddRange(fwd);
            if (back.Count >= 3) lines.Add(back);
        }
        // the sea on the right (water space): the land (higher) to the left
        foreach (var l in lines)
        {
            int m = l.Count / 2;
            Vector2 dir = (l[m + 1] - l[m - 1]).normalized, right = new Vector2(dir.y, -dir.x);
            if (d.At(l[m] + right * 1f) > d.At(l[m] - right * 1f)) l.Reverse();
        }
        return lines;
    }

    /// <summary>The coast line (water space) with the part through the walkable area replaced by the waterline found
    /// on the user terrain, joined to it at both ends; within the bake's point budget.</summary>
    public static List<Vector2> Splice(List<Vector2> line, Data d, Vector2 centre, float half, int budget, out string note)
    {
        note = null;
        bool Inside(Vector2 p) => Mathf.Abs(p.x - centre.x) <= half && Mathf.Abs(p.y - centre.y) <= half;
        int i1 = -1, i2 = -1;
        for (int i = 0; i < line.Count; i++) if (Inside(line[i])) { if (i1 < 0) i1 = i; i2 = i; }
        var lines = Waterlines(d);
        if (lines.Count == 0) { note = "no waterline found on the user terrain (is it above and below the water?)"; return line; }
        if (i1 < 0) { note = "the coast line does not pass through the walkable area: the waterline found there is not joined to it"; return line; }
        Vector2 e1 = line[i1], e2 = line[i2];
        List<Vector2> best = null; float bestScore = float.MaxValue;
        foreach (var l in lines)
        {
            float score = Vector2.Distance(l[0], e1) + Vector2.Distance(l[l.Count - 1], e2);
            if (score < bestScore) { bestScore = score; best = l; }
        }
        float gap = Mathf.Max(Vector2.Distance(best[0], e1), Vector2.Distance(best[best.Count - 1], e2));
        if (gap > 5f) note = $"the coast line meets the waterline on the user terrain {gap:0} m from where it should: draw it to meet the mesh's shore at the walkable area's edge";
        var outer = line.Count - (i2 - i1 + 1);
        List<Vector2> inner = best;
        for (float tol = 0.05f; ; tol *= 1.6f)
        {
            inner = Simplify(best, tol);
            if (inner.Count + outer <= budget || tol > 20f) break;
        }
        var result = new List<Vector2>();
        for (int i = 0; i < i1; i++) result.Add(line[i]);
        result.AddRange(inner);
        for (int i = i2 + 1; i < line.Count; i++) result.Add(line[i]);
        return result;
    }

    // Douglas-Peucker
    static List<Vector2> Simplify(List<Vector2> pts, float tol)
    {
        var keep = new bool[pts.Count];
        keep[0] = keep[pts.Count - 1] = true;
        var stack = new Stack<(int, int)>();
        stack.Push((0, pts.Count - 1));
        while (stack.Count > 0)
        {
            var (a, b) = stack.Pop();
            float dmax = 0; int idx = -1;
            Vector2 ab = pts[b] - pts[a];
            float len = Mathf.Max(ab.magnitude, 1e-6f);
            for (int i = a + 1; i < b; i++)
            {
                float dd = Mathf.Abs(ab.x * (pts[i].y - pts[a].y) - ab.y * (pts[i].x - pts[a].x)) / len;
                if (dd > dmax) { dmax = dd; idx = i; }
            }
            if (idx >= 0 && dmax > tol) { keep[idx] = true; stack.Push((a, idx)); stack.Push((idx, b)); }
        }
        var outp = new List<Vector2>();
        for (int i = 0; i < pts.Count; i++) if (keep[i]) outp.Add(pts[i]);
        return outp;
    }
}
