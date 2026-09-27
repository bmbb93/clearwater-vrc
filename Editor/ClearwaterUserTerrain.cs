using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The coast's user terrain (Terrain source = User): the user's own meshes and Unity terrains as the ground round the
/// walkable area.
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
    public static List<MeshRenderer> Renderers(ClearwaterCoast coast) => Renderers(coast != null ? coast.userTerrain : null);

    /// <summary>The enabled mesh renderers under a root (none past skip, what Clearwater made there).</summary>
    public static List<MeshRenderer> Renderers(GameObject root, Transform skip = null)
    {
        var list = new List<MeshRenderer>();
        if (root == null) return list;
        foreach (var r in root.GetComponentsInChildren<MeshRenderer>(false))
        {
            var mf = r.GetComponent<MeshFilter>();
            if (r.enabled && mf != null && mf.sharedMesh != null && (skip == null || !r.transform.IsChildOf(skip))) list.Add(r);
        }
        return list;
    }

    /// <summary>The Unity terrains that make the user terrain (enabled Terrain components under the User terrain).</summary>
    public static List<Terrain> Terrains(ClearwaterCoast coast) => Terrains(coast != null ? coast.userTerrain : null);

    /// <summary>The enabled Unity terrains under a root.</summary>
    public static List<Terrain> Terrains(GameObject root)
    {
        var list = new List<Terrain>();
        if (root == null) return list;
        foreach (var t in root.GetComponentsInChildren<Terrain>(false))
            if (t.enabled && t.terrainData != null) list.Add(t);
        return list;
    }

    /// <summary>How many meshes and terrains make the user terrain.</summary>
    public static int Count(ClearwaterCoast coast) => Renderers(coast).Count + Terrains(coast).Count;

    /// <summary>The layers the user terrain's meshes and terrains are on.</summary>
    public static int Layers(ClearwaterCoast coast)
    {
        int mask = 0;
        foreach (var r in Renderers(coast)) mask |= 1 << r.gameObject.layer;
        foreach (var t in Terrains(coast)) mask |= 1 << t.gameObject.layer;
        return mask;
    }

    /// <summary>What the user terrain is made of, to tell when it needs baking again.</summary>
    public static void HashInto(ClearwaterCoast coast, System.Text.StringBuilder sb)
    {
        foreach (var r in Renderers(coast))
        {
            var mesh = r.GetComponent<MeshFilter>().sharedMesh;
            sb.Append(mesh.name).Append(mesh.vertexCount).Append(r.transform.localToWorldMatrix);
        }
        foreach (var t in Terrains(coast))
        {
            var td = t.terrainData;
            sb.Append(td.name).Append(td.size).Append(td.heightmapResolution).Append(t.transform.position);
            for (int j = 0; j < 16; j++)
                for (int i = 0; i < 16; i++)
                    sb.Append(td.GetInterpolatedHeight(i / 15f, j / 15f).ToString("F3"));
        }
    }

    /// <summary>Bakes the user terrain round the walkable area (null when the coast has none).</summary>
    public static Data Bake(ClearwaterController ctl, ClearwaterCoast coast)
    {
        if (coast.terrainSource != ClearwaterCoast.TerrainSource.User) return null;
        var renderers = Renderers(coast);
        var terrains = Terrains(coast);
        if (renderers.Count + terrains.Count == 0)
        {
            Debug.LogWarning("[Clearwater] Terrain source is User, but the User terrain has no meshes or terrains: the generated terrain is used.");
            return null;
        }
        return Bake(ctl.water.position, coast.transform.position, Mathf.Max(coast.groundHalfSize, 1f), Mathf.Max(coast.seamWidth, 1f),
                    Mathf.Max(coast.groundStep, 0.05f), renderers, terrains);
    }

    /// <summary>Bakes meshes and terrains from above as a user terrain: heights relative to a water surface at origin,
    /// over the square of half size half round centre (world; snapped to step), with a seam round them.</summary>
    public static Data Bake(Vector3 origin, Vector3 centre, float half, float seam, float step, List<MeshRenderer> renderers, List<Terrain> terrains)
    {
        Vector3 c = centre - origin;
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
        // Unity terrains: their heights read straight from their data (the highest of all where they overlap)
        foreach (var t in terrains)
        {
            Vector3 tp = t.transform.position, ts = t.terrainData.size;
            for (int j = 0; j < res; j++)
                for (int i = 0; i < res; i++)
                {
                    Vector2 js = d.Pos(i, j);
                    float nx = (js.x + origin.x - tp.x) / ts.x, nz = (-js.y + origin.z - tp.z) / ts.z;
                    if (nx < 0f || nx > 1f || nz < 0f || nz > 1f) continue;
                    int k = j * res + i;
                    float h = tp.y + t.terrainData.GetInterpolatedHeight(nx, nz) - origin.y;
                    d.h[k] = d.on[k] ? Mathf.Max(d.h[k], h) : h;
                    d.on[k] = true;
                }
        }

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

        d.mean = MeanColour(renderers, terrains);
        return d;
    }

    // the average colour: each mesh material's colour times its main texture's mean, each terrain layer's texture mean
    // by how much of the terrain it paints, by the area each covers
    static Vector4 MeanColour(List<MeshRenderer> renderers, List<Terrain> terrains)
    {
        Vector3 acc = Vector3.zero; float wsum = 0;
        foreach (var t in terrains)
        {
            var td = t.terrainData;
            var layers = td.terrainLayers;
            if (layers == null || layers.Length == 0) continue;
            float area = td.size.x * td.size.z;
            var share = new float[layers.Length];
            var maps = td.GetAlphamaps(0, 0, td.alphamapWidth, td.alphamapHeight);
            for (int y = 0; y < td.alphamapHeight; y += 4)
                for (int x = 0; x < td.alphamapWidth; x += 4)
                    for (int l = 0; l < layers.Length && l < maps.GetLength(2); l++) share[l] += maps[y, x, l];
            float total = 0; foreach (var s in share) total += s;
            for (int l = 0; l < layers.Length; l++)
            {
                if (layers[l] == null || total <= 0) continue;
                Vector3 m = layers[l].diffuseTexture != null ? ClearwaterBedLook.MeanFromGpu(layers[l].diffuseTexture) : Vector3.one * 0.5f;
                Vector4 rm = layers[l].diffuseRemapMax;
                m = Vector3.Scale(m, new Vector3(rm.x, rm.y, rm.z));
                float w = area * share[l] / total;
                acc += m * w; wsum += w;
            }
        }
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

    // ---------------------------------------------------------------- the caustics on it

    const string ProjectorName = "User Terrain Caustics";
    const string BeachProjectorName = "User Terrain Beach";
    const float ProjectorAbove = 30f; // m above the surface; its box reaches 10 m below

    /// <summary>What is drawn on the user terrain's meshes from above: the caustics (a projector over its whole area,
    /// straight down, on its meshes' layers - anything else on them under the water there gets them too - with the
    /// avatars' caustics material, which places the pattern from world positions, so it lines up with the floor's
    /// everywhere), and the beach (the damp, the swash's film and its foam: UserBeach.shader, with the seabed's
    /// settings). Removed when there is no user terrain.</summary>
    public static void UpdateProjectors(ClearwaterController ctl, ClearwaterCoast coast, Data d)
    {
        int mask = Layers(coast);
        Material beach = null;
        if (d != null && ctl.seabedMaterial != null)
        {
            beach = new Material(Shader.Find("Clearwater/UserBeach")) { name = "UserBeach" };
            beach.CopyPropertiesFromMaterial(ctl.seabedMaterial);
            beach.renderQueue = -1; // (the shader's, not the seabed's)
            beach = ClearwaterSetup.Save(beach, ClearwaterSetup.SceneDir(ctl.gameObject.scene) + "UserBeach.mat");
        }
        if (ctl.userBeachMaterial != beach) { Undo.RecordObject(ctl, "User terrain beach"); ctl.userBeachMaterial = beach; EditorUtility.SetDirty(ctl); }
        Project(ctl, ProjectorName, d, mask, d != null ? ctl.avatarCausticsMaterial : null);
        Project(ctl, BeachProjectorName, d, mask, beach);
    }

    static void Project(ClearwaterController ctl, string name, Data d, int mask, Material mat)
    {
        var t = ctl.water.Find(name);
        if (d == null || mat == null)
        {
            if (t != null) Undo.DestroyObjectImmediate(t.gameObject);
            return;
        }
        if (t == null)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "User terrain caustics");
            t = go.transform;
            t.SetParent(ctl.water, false);
        }
        t.localPosition = new Vector3(d.area.x, ProjectorAbove, -d.area.y);
        t.localRotation = Quaternion.Euler(90, 0, 0);
        var proj = t.GetComponent<Projector>();
        if (proj == null) proj = Undo.AddComponent<Projector>(t.gameObject);
        proj.orthographic = true;
        proj.orthographicSize = d.area.z * 0.5f;
        proj.aspectRatio = 1;
        proj.nearClipPlane = 0.1f;
        proj.farClipPlane = ProjectorAbove + 10f;
        proj.material = mat;
        proj.ignoreLayers = ~mask;
        EditorUtility.SetDirty(proj);
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
    public static List<Vector2> Splice(List<Vector2> line, Data d, Vector2 centre, float half, int budget, out string note,
                                       out List<Vector2> noteAt)
    {
        note = null;
        noteAt = new List<Vector2>();
        bool Inside(Vector2 p) => Mathf.Abs(p.x - centre.x) <= half && Mathf.Abs(p.y - centre.y) <= half;
        // (a segment near the walkable area in metre steps, so a long straight one crossing it has points in it)
        var fine = new List<Vector2>();
        for (int i = 0; i < line.Count; i++)
        {
            fine.Add(line[i]);
            if (i + 1 >= line.Count) break;
            Vector2 a = line[i], b = line[i + 1];
            if (Mathf.Max(a.x, b.x) < centre.x - half || Mathf.Min(a.x, b.x) > centre.x + half ||
                Mathf.Max(a.y, b.y) < centre.y - half || Mathf.Min(a.y, b.y) > centre.y + half) continue;
            int steps = Mathf.Min(Mathf.CeilToInt(Vector2.Distance(a, b)), 20000);
            for (int s = 1; s < steps; s++)
            {
                var p = Vector2.Lerp(a, b, s / (float)steps);
                if (Inside(p)) fine.Add(p); // (only there: they are replaced, so the line outside keeps its points)
            }
        }
        line = fine;
        int i1 = -1, i2 = -1;
        for (int i = 0; i < line.Count; i++) if (Inside(line[i])) { if (i1 < 0) i1 = i; i2 = i; }
        var lines = Waterlines(d);
        if (lines.Count == 0) { note = "no waterline found on the user terrain (is it above and below the water?)"; return line; }
        if (i1 < 0) { note = "the coast line does not pass through the walkable area: the waterline found there is not joined to it"; return line; }
        // where the line comes into the walkable area and leaves it (its edge crossed between two points)
        Vector2 Crossing(Vector2 outside, Vector2 inside)
        {
            float lo = 0f, hi = 1f;
            for (int it = 0; it < 30; it++) { float mid = 0.5f * (lo + hi); if (Inside(Vector2.Lerp(outside, inside, mid))) hi = mid; else lo = mid; }
            return Vector2.Lerp(outside, inside, hi);
        }
        Vector2 e1 = i1 > 0 ? Crossing(line[i1 - 1], line[i1]) : line[i1];
        Vector2 e2 = i2 < line.Count - 1 ? Crossing(line[i2 + 1], line[i2]) : line[i2];
        // each waterline's part through the walkable area, from where it first comes in to where it last leaves: the
        // terrain reaches on into the seam, and only the part inside replaces the line (joined, and measured, there)
        var parts = new List<List<Vector2>>();
        foreach (var l in lines)
        {
            int a = -1, b = -1;
            for (int i = 0; i < l.Count; i++) if (Inside(l[i])) { if (a < 0) a = i; b = i; }
            if (a < 0 || b == a) continue;
            var part = new List<Vector2>();
            if (a > 0) part.Add(Crossing(l[a - 1], l[a]));
            part.AddRange(l.GetRange(a, b - a + 1));
            if (b < l.Count - 1) part.Add(Crossing(l[b + 1], l[b]));
            parts.Add(part);
        }
        if (parts.Count == 0) { note = "no waterline found on the user terrain in the walkable area (is it above and below the water there?)"; return line; }
        List<Vector2> best = null; float bestScore = float.MaxValue;
        foreach (var l in parts)
        {
            float score = Vector2.Distance(l[0], e1) + Vector2.Distance(l[l.Count - 1], e2);
            if (score < bestScore) { bestScore = score; best = l; }
        }
        float gap = Mathf.Max(Vector2.Distance(best[0], e1), Vector2.Distance(best[best.Count - 1], e2));
        if (gap > 5f)
        {
            note = $"the coast line meets the waterline on the user terrain {gap:0} m from where it should: draw it to meet " +
                   "the mesh's shore at the walkable area's (green) edge (marked: where the line crosses the edge, and " +
                   "where the waterline on the mesh reaches it)";
            noteAt.Add(e1); noteAt.Add(e2); noteAt.Add(best[0]); noteAt.Add(best[best.Count - 1]);
        }
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

    // ---------------------------------------------------------------- checks

    const float SeamSteep = 0.2f; // the seam's slope (its rise over its width) past which it is flagged (about 11°)
    const float MarkApart = 15f;  // m: flagged places nearer each other than this are marked once
    const int MaxMarks = 8;

    /// <summary>What the bake can tell is wrong with the user terrain: parts of the walkable area it leaves uncovered
    /// (nothing to stand on: with a user terrain the generated ground has no collider), a user terrain reaching past
    /// the baked square (past it the generated ground is drawn through it), and where the seam has to climb or drop
    /// steeply to meet the generated ground (a bank or a ditch round the mesh). floorBake: the shaders' floor as set
    /// up for this bake (its user terrain is switched off here, for the generated ground's own heights). Each with
    /// the places to mark (world).</summary>
    public static List<(string text, List<Vector3> at)> Check(Data d, ClearwaterCoast coast, Material floorBake, Vector3 origin)
    {
        var notes = new List<(string, List<Vector3>)>();
        int res = d.res;
        float texel = d.area.z / res, half = Mathf.Max(coast.groundHalfSize, 1f), seam = Mathf.Max(coast.seamWidth, 1f);
        Vector3 World(Vector2 p, float h) => new Vector3(p.x + origin.x, origin.y + h, -p.y + origin.z);

        // the walkable area, a metre in from its edge (the walls), covered?
        var holes = new List<(Vector2, float)>();
        var holeCells = new HashSet<long>();
        int bare = 0;
        for (int j = 0; j < res; j++)
            for (int i = 0; i < res; i++)
            {
                Vector2 p = d.Pos(i, j);
                if (Mathf.Abs(p.x - d.area.x) > half - 1f || Mathf.Abs(p.y - d.area.y) > half - 1f || d.on[j * res + i]) continue;
                bare++;
                if (holeCells.Add(Cell(p, 5f))) holes.Add((p, 0f));
            }
        float bareArea = bare * texel * texel;
        if (bareArea > 1f)
            notes.Add(($"{bareArea:0} m² of the walkable area is not covered by the user terrain (marked): there is nothing " +
                       "to stand on there, as the generated ground has no collider with a user terrain. Cover it, or make the " +
                       "walkable area (Ground Half Size) smaller", Marks(holes, p => World(p, 0f))));

        // reaching the baked square's edge?
        var over = new List<(Vector2, float)>();
        for (int k = 0; k < res; k++)
            foreach (int q in new[] { k, (res - 1) * res + k, k * res, k * res + res - 1 })
                if (d.on[q]) over.Add((d.Pos(q % res, q / res), 0f));
        if (over.Count > 0)
            notes.Add(("the user terrain reaches past the baked square, the walkable area and the seam round it (marked): " +
                       "past it the generated ground is drawn through it. Make it smaller, or the walkable area (Ground Half " +
                       "Size) or the seam bigger", Marks(over, p => World(p, d.At(p)))));

        // the seam: the mesh's edge against the generated ground a seam's width out
        int n = Mathf.Clamp(Mathf.CeilToInt(d.area.z), 64, 1024);
        float step = d.area.z / (n - 1);
        float x0 = d.area.x - d.area.z * 0.5f, z0 = -d.area.y - d.area.z * 0.5f; // (Unity x, z from the water origin)
        Vector4 userArea = floorBake.GetVector("_UserArea");
        floorBake.SetVector("_UserArea", Vector4.zero);
        floorBake.SetVector("_BakeGrid", new Vector4(x0, z0, step, n));
        var gen = ClearwaterCoastBake.BlitRead(floorBake, n, n);
        floorBake.SetVector("_UserArea", userArea);
        float Generated(Vector2 p)
        {
            float fi = Mathf.Clamp((p.x - x0) / step, 0f, n - 1.001f), fj = Mathf.Clamp((-p.y - z0) / step, 0f, n - 1.001f);
            int i = (int)fi, j = (int)fj; float ti = fi - i, tj = fj - j;
            float a = Mathf.Lerp(gen[j * n + i].r, gen[j * n + i + 1].r, ti), b = Mathf.Lerp(gen[(j + 1) * n + i].r, gen[(j + 1) * n + i + 1].r, ti);
            return -Mathf.Lerp(a, b, tj); // (depth -> height)
        }
        var steep = new List<(Vector2, float)>();
        var seen = new HashSet<long>();
        float worst = 0f;
        for (int j = 1; j < res - 1; j++)
            for (int i = 1; i < res - 1; i++)
            {
                int k = j * res + i;
                if (!d.on[k]) continue;
                Vector2 outward = Vector2.zero;
                for (int dj = -1; dj <= 1; dj++)
                    for (int di = -1; di <= 1; di++)
                        if (!d.on[k + dj * res + di]) outward += new Vector2(di, dj);
                if (outward == Vector2.zero) continue;
                Vector2 p = d.Pos(i, j);
                if (!seen.Add(Cell(p, 2f))) continue; // (every 2 m along the edge)
                float rise = Generated(p + outward.normalized * seam) - d.h[k];
                if (Mathf.Abs(rise) / seam <= SeamSteep) continue;
                steep.Add((p, Mathf.Abs(rise)));
                worst = Mathf.Max(worst, Mathf.Abs(rise));
            }
        if (steep.Count > 0)
            notes.Add(($"the seam round the user terrain climbs or drops up to {worst:0.0} m over its {seam:0} m (marked): a bank " +
                       "or a ditch round the mesh. Widen the seam (Seam Width), or carry the mesh on until its edge is near " +
                       "the generated ground's height there (the line and the cross-section shape it)",
                       Marks(steep, p => World(p, d.At(p)))));
        return notes;
    }

    static long Cell(Vector2 p, float size) => ((long)Mathf.FloorToInt(p.x / size) << 32) ^ (uint)Mathf.FloorToInt(p.y / size);

    // the worst places first, one per MarkApart, at most MaxMarks
    static List<Vector3> Marks(List<(Vector2 p, float how)> places, System.Func<Vector2, Vector3> world)
    {
        places.Sort((a, b) => b.how.CompareTo(a.how));
        var picked = new List<Vector2>();
        foreach (var (p, _) in places)
        {
            bool near = false;
            foreach (var q in picked) if ((q - p).sqrMagnitude < MarkApart * MarkApart) { near = true; break; }
            if (near) continue;
            picked.Add(p);
            if (picked.Count >= MaxMarks) break;
        }
        return picked.ConvertAll(p => world(p));
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
