using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Bakes the pools (ClearwaterPool): each one's basin seen from above as its floor (the user terrain's bake, with the
/// pool's surface as the water level), and its water, underwater view and caustics as children of the pool, drawn
/// with copies of the sea's materials (the same shaders and wave simulation; the floor theirs, no shore waves). Also
/// cuts the pools out of the sea: its water, ground and caustics are not drawn in a basin (the pool mask).
/// Assets go to Generated/Pools/(name)_(id).
/// </summary>
public static class ClearwaterPoolBake
{
    public const string WaterName = "Pool Water";   // the pool's generated children: its water (with the rest under it)
    const string FogName = "Pool Underwater";
    const string CausticsName = "Pool Caustics";
    const float Margin = 1f;        // m the water and the bake reach past the pool's size
    const float Seam = 1f;
    const float Cell = 0.5f;        // m: the water plane's grid
    const float CausticsAbove = 3f; // m above the surface the caustics projector looks down from
    const float SurfaceBand = 0.6f; // (ClearwaterController.SurfaceBand: a pool's water reaches this far up for the checks)
    // the layers avatars are on (as the sea's avatar caustics): Player, PlayerLocal, MirrorReflection, ClearwaterProps
    static readonly int[] AvatarLayers = { 9, 10, 18, 22 };

    public static List<ClearwaterPool> All() => new List<ClearwaterPool>(Object.FindObjectsOfType<ClearwaterPool>());

    /// <summary>Bakes every pool in the scene, and the pool mask on the sea.</summary>
    public static void BakeAll()
    {
        var ctl = Object.FindObjectOfType<ClearwaterController>(true);
        if (ctl == null) return;
        var pools = All();
        foreach (var p in pools) BakeOne(ctl, p, pools);
        ApplyMask(ctl, pools);
        Register(ctl, pools);
    }

    /// <summary>Bakes one pool (and the pool mask; the sea's walkable ground is cut round it on the coast's bake).</summary>
    public static void Bake(ClearwaterPool pool)
    {
        var ctl = Object.FindObjectOfType<ClearwaterController>(true);
        if (ctl == null) { Debug.LogError("[Clearwater] Bake the pool: there is no Clearwater in the scene (Tools > Clearwater)."); return; }
        var pools = All();
        BakeOne(ctl, pool, pools);
        ApplyMask(ctl, pools);
        Register(ctl, pools);
        EditorSceneManager.MarkSceneDirty(pool.gameObject.scene);
        AssetDatabase.SaveAssets();
    }

    static void BakeOne(ClearwaterController ctl, ClearwaterPool pool, List<ClearwaterPool> pools)
    {
        Undo.RecordObject(pool, "Bake pool");
        if (string.IsNullOrEmpty(pool.id) || pools.Exists(p => p != pool && p.id == pool.id && Safe(p.name) == Safe(pool.name)))
            // (new, or a copy of another with the same name: they would share a folder)
            pool.id = System.Guid.NewGuid().ToString("N").Substring(0, 8);
        string folder = "Pools/" + Safe(pool.name) + "_" + pool.id + "/";
        var notes = new List<ClearwaterCoast.BakeNote>();
        void Note(string text, params Vector3[] at) => notes.Add(new ClearwaterCoast.BakeNote { text = text, at = at });
        var tf = pool.transform;
        Vector3 origin = tf.position;
        if (Quaternion.Angle(tf.rotation, Quaternion.identity) > 0.01f)
            Note("the pool is turned: its water stays square to the world's axes (turn the Basin instead)", origin);

        // its generated children: the water, and under it the underwater view and the caustics
        var water = Child(tf, WaterName);
        water.localPosition = Vector3.zero; water.localRotation = Quaternion.identity; water.localScale = Vector3.one;

        var renderers = ClearwaterUserTerrain.Renderers(pool.basin, water);
        var terrains = ClearwaterUserTerrain.Terrains(pool.basin);
        if (renderers.Count + terrains.Count == 0)
        {
            Note("the pool has no Basin (its meshes): there is no water");
            water.gameObject.SetActive(false);
            Finish(pool, notes, 0f);
            return;
        }
        water.gameObject.SetActive(true);

        // the basin, from above, with the pool's surface as the water level
        float half = Mathf.Max(pool.size.x, pool.size.y) * 0.5f + Margin;
        var d = ClearwaterUserTerrain.Bake(origin, origin, half, Seam, 0.05f, renderers, terrains);
        d.tex = ClearwaterSetup.Save(d.tex, folder + "Floor.asset");
        float deepest = 0f;
        int wet = 0;
        var past = new List<Vector3>();
        Rect inner = new Rect(-pool.size.x * 0.5f - Margin, -pool.size.y * 0.5f - Margin, pool.size.x + 2 * Margin, pool.size.y + 2 * Margin);
        for (int k = 0; k < d.h.Length; k++)
        {
            if (!d.on[k] || d.h[k] >= -0.02f) continue;
            Vector2 p = d.Pos(k % d.res, k / d.res); // (water space: x, -z)
            if (inner.Contains(new Vector2(p.x, -p.y))) { wet++; deepest = Mathf.Min(deepest, d.h[k]); }
            else if (past.Count < 400) past.Add(origin + new Vector3(p.x, d.h[k], -p.y));
        }
        if (wet == 0)
            Note("none of the basin is below the pool's surface: there is no water. Is this object at the water's height, and " +
                 "is a ceiling or a cover in the Basin (the highest surface is taken as the floor)?", origin);
        if (past.Count > 0)
            Note("the basin carries on below the surface past the pool's Size (marked): the water stops short there. Make Size bigger",
                 past[0], past[past.Count / 2], past[past.Count - 1]);
        // pools one above the other must not share their water's height span
        Rect area = pool.Area;
        foreach (var o in pools)
        {
            if (o == pool || !o.Area.Overlaps(area)) continue;
            float oBottom = o.transform.position.y + Mathf.Min(o.depth, 0f), oTop = o.transform.position.y + SurfaceBand;
            if (origin.y + deepest < oTop && origin.y + SurfaceBand > oBottom)
                Note($"it overlaps the pool {o.name} (seen from above, at the same heights): only one of them is underwater at a time", origin, o.transform.position);
        }

        // its materials: the sea's, with its own floor
        var dry = DryProfile();
        var waterMat = Copy(ctl.waterMaterial, folder + "Water.mat", "Pool Water");
        var fogMat = Copy(ctl.underwaterMaterial, folder + "Underwater.mat", "Pool Underwater");
        var causMat = Copy(ctl.avatarCausticsMaterial, folder + "Caustics.mat", "Pool Caustics");
        foreach (var m in new[] { waterMat, fogMat, causMat })
        {
            if (m == null) continue;
            Floor(m, d, dry, origin);
            m.SetFloat("_Calm", 1f - pool.waveStrength);
            m.SetFloat("_Indoor", pool.indoor ? 1f : 0f);
            m.SetFloat("_EnvGain", 1f);
            // (seen from below and fogged only by a camera in it)
            Rect a = pool.Area;
            m.SetVector("_BodyArea", new Vector4(a.xMin, a.yMin, a.xMax, a.yMax));
            m.SetVector("_BodyFloor", new Vector4(origin.y + deepest - 0.3f, 0, 0, 1));
            EditorUtility.SetDirty(m);
        }
        if (fogMat != null && fogMat.HasProperty("_MaxDistance")) fogMat.SetFloat("_MaxDistance", pool.fogDistance);

        // the water: a plane over the pool's size and the margin
        var plane = ClearwaterSetup.Save(Plane(pool.size + Vector2.one * 2f * Margin), folder + "Plane.asset");
        water.GetOrAdd<MeshFilter>().sharedMesh = plane;
        var wr = water.GetOrAdd<MeshRenderer>();
        wr.sharedMaterial = waterMat;
        wr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        wr.receiveShadows = false;
        wr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        wr.reflectionProbeUsage = pool.indoor ? UnityEngine.Rendering.ReflectionProbeUsage.BlendProbes : UnityEngine.Rendering.ReflectionProbeUsage.Off;

        // underwater: the sea's fog box, round this pool (the controller turns it on in the pool)
        var fog = Child(water, FogName);
        fog.localPosition = Vector3.zero; fog.localRotation = Quaternion.identity;
        fog.localScale = new Vector3(pool.size.x + 2 * Margin, 40f, pool.size.y + 2 * Margin);
        fog.GetOrAdd<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        var fr = fog.GetOrAdd<MeshRenderer>();
        fr.sharedMaterial = fogMat;
        fr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        fr.receiveShadows = false;
        fr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        fr.reflectionProbeUsage = pool.indoor ? UnityEngine.Rendering.ReflectionProbeUsage.BlendProbes : UnityEngine.Rendering.ReflectionProbeUsage.Off;
        fr.enabled = false;

        // caustics on the basin and on the avatars in it, straight down over the pool
        var caus = Child(water, CausticsName);
        caus.localPosition = new Vector3(0, CausticsAbove, 0); caus.localRotation = Quaternion.Euler(90, 0, 0); caus.localScale = Vector3.one;
        var proj = caus.GetOrAdd<Projector>();
        proj.orthographic = true;
        proj.orthographicSize = pool.size.y * 0.5f + Margin;
        proj.aspectRatio = (pool.size.x + 2 * Margin) / (pool.size.y + 2 * Margin);
        proj.nearClipPlane = 0.1f;
        proj.farClipPlane = CausticsAbove - deepest + 0.5f;
        proj.material = causMat;
        int mask = 0;
        foreach (var r in renderers) mask |= 1 << r.gameObject.layer;
        foreach (var t in terrains) mask |= 1 << t.gameObject.layer;
        foreach (int l in AvatarLayers) mask |= 1 << l;
        proj.ignoreLayers = ~mask;
        EditorUtility.SetDirty(proj);

        Finish(pool, notes, deepest);
    }

    static void Finish(ClearwaterPool pool, List<ClearwaterCoast.BakeNote> notes, float deepest)
    {
        pool.depth = deepest;
        pool.bakeNotes = notes.ToArray();
        foreach (var n in notes) Debug.LogWarning($"[Clearwater] Pool {pool.name}: {n.text}.", pool);
        pool.bakedHash = Hash(pool);
        EditorUtility.SetDirty(pool);
    }

    // a pool's floor on a copy of the sea's materials: its basin (baked round its surface) and nothing of the sea's:
    // no coast (u = 0 everywhere), a dry cross-section (so the water is cut away wherever the basin is not below it,
    // and a camera outside the basin is never under it), no rocks, stamps or shore waves, no pool mask
    static void Floor(Material m, ClearwaterUserTerrain.Data d, Texture2D dry, Vector3 origin)
    {
        m.SetTexture("_CoastTex", null);
        m.SetVector("_CoastArea", new Vector4(0, 0, d.area.z, 0));
        m.SetTexture("_CoastFarTex", null);
        m.SetVector("_CoastFarArea", Vector4.zero);
        m.SetTexture("_CoastProfile", dry);
        m.SetVector("_CoastProfileU", new Vector4(-1, 1, -100, 0)); // (the waterline far away: no shore foam)
        m.SetFloat("_ShoreWaves", 0);
        m.SetTexture("_RockTex", null);
        m.SetTexture("_StampTex", null);
        m.SetVector("_StampArea", Vector4.zero);
        m.SetFloat("_BedRipple", 0);
        m.SetVector("_BedMean", d.mean * 0.6f);
        m.SetTexture("_UserTex", d.tex);
        m.SetVector("_UserArea", d.area);
        m.SetVector("_UserMean", d.mean);
        m.SetTexture("_PoolMask", null);
        m.SetVector("_PoolMaskArea", Vector4.zero);
        m.SetVector("_WaterOrigin", origin);
        m.SetVector("_RipCenter", new Vector4(1e5f, 1e5f, 0, 0)); // (the controller brings the ripples when someone is in it)
    }

    static Material Copy(Material src, string file, string name)
    {
        if (src == null) return null;
        return ClearwaterSetup.Save(new Material(src) { name = name }, file);
    }

    // the floor 1 m above the water everywhere (a table of one value)
    static Texture2D DryProfile()
    {
        var t = new Texture2D(4, 1, TextureFormat.RGFloat, false, true) { name = "DryProfile", wrapMode = TextureWrapMode.Clamp };
        t.SetPixels(new[] { new Color(-1, 0, 0, 1), new Color(-1, 0, 0, 1), new Color(-1, 0, 0, 1), new Color(-1, 0, 0, 1) });
        t.Apply(false, false);
        return ClearwaterSetup.Save(t, "Pools/DryProfile.asset");
    }

    // a flat grid, Cell apart, centred, facing up
    static Mesh Plane(Vector2 size)
    {
        int nx = Mathf.Max(2, Mathf.CeilToInt(size.x / Cell) + 1), nz = Mathf.Max(2, Mathf.CeilToInt(size.y / Cell) + 1);
        var v = new Vector3[nx * nz];
        for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
                v[j * nx + i] = new Vector3(-size.x * 0.5f + size.x * i / (nx - 1), 0, -size.y * 0.5f + size.y * j / (nz - 1));
        var idx = new int[(nx - 1) * (nz - 1) * 6];
        int o = 0;
        for (int j = 0; j < nz - 1; j++)
            for (int i = 0; i < nx - 1; i++)
            {
                int a = j * nx + i;
                idx[o++] = a; idx[o++] = a + nx; idx[o++] = a + 1; idx[o++] = a + 1; idx[o++] = a + nx; idx[o++] = a + nx + 1;
            }
        var mesh = new Mesh { name = "PoolPlane", vertices = v, triangles = idx };
        mesh.normals = System.Array.ConvertAll(v, _ => Vector3.up);
        mesh.RecalculateBounds();
        return mesh;
    }

    // ---------------------------------------------------------------- the sea, cut round the pools

    const float MaskTexel = 0.25f;

    /// <summary>The pools cut out of the sea: per texel over them all, the highest pool surface and the lowest pool
    /// floor; the sea's water, ground and caustics are not drawn between the two (the pool mask on its materials).</summary>
    static void ApplyMask(ClearwaterController ctl, List<ClearwaterPool> pools)
    {
        var areas = new List<(Rect r, float top, float bottom)>();
        foreach (var p in pools) if (p.basin != null) areas.Add(Span(p));
        Texture2D tex = null;
        Vector4 at = Vector4.zero;
        if (areas.Count > 0)
        {
            Rect all = areas[0].r;
            foreach (var a in areas) all = Rect.MinMaxRect(Mathf.Min(all.xMin, a.r.xMin), Mathf.Min(all.yMin, a.r.yMin), Mathf.Max(all.xMax, a.r.xMax), Mathf.Max(all.yMax, a.r.yMax));
            float size = Mathf.Max(all.width, all.height) + 2f;
            int res = Mathf.Clamp(Mathf.CeilToInt(size / MaskTexel), 8, 2048);
            Vector2 corner = all.center - Vector2.one * size * 0.5f;
            var px = new Color[res * res];
            for (int k = 0; k < px.Length; k++) px[k] = new Color(-1e4f, 1e4f, 0, 1);
            foreach (var (r, top, bottom) in areas)
            {
                int i0 = Mathf.FloorToInt((r.xMin - corner.x) / size * res), i1 = Mathf.CeilToInt((r.xMax - corner.x) / size * res);
                int j0 = Mathf.FloorToInt((r.yMin - corner.y) / size * res), j1 = Mathf.CeilToInt((r.yMax - corner.y) / size * res);
                for (int j = Mathf.Max(j0, 0); j < Mathf.Min(j1, res); j++)
                    for (int i = Mathf.Max(i0, 0); i < Mathf.Min(i1, res); i++)
                    {
                        var c = px[j * res + i];
                        px[j * res + i] = new Color(Mathf.Max(c.r, top), Mathf.Min(c.g, bottom), 0, 1);
                    }
            }
            tex = new Texture2D(res, res, TextureFormat.RGFloat, false, true) { name = "PoolMask", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point };
            tex.SetPixels(px);
            tex.Apply(false, false);
            tex = ClearwaterSetup.Save(tex, "Pools/PoolMask.asset");
            at = new Vector4(corner.x, corner.y, size, 1);
        }
        foreach (var m in new[] { ctl.waterMaterial, ctl.seabedMaterial, ctl.avatarCausticsMaterial, ctl.userBeachMaterial, ctl.underwaterMaterial })
        {
            if (m == null) continue;
            m.SetTexture("_PoolMask", tex);
            m.SetVector("_PoolMaskArea", at);
            EditorUtility.SetDirty(m);
        }
    }

    /// <summary>Hands the pools to the controller (each one's water, footprint, floor, materials and fog box), which
    /// works out which water the viewer is in: its fog, its ripples, the touches and taps on it.</summary>
    static void Register(ClearwaterController ctl, List<ClearwaterPool> pools)
    {
        var baked = new List<ClearwaterPool>();
        foreach (var p in pools)
        {
            var w = p.transform.Find(WaterName);
            if (w != null && w.gameObject.activeSelf && w.GetComponent<MeshRenderer>() != null) baked.Add(p);
        }
        int n = baked.Count;
        Undo.RecordObject(ctl, "Pools");
        ctl.pools = new Transform[n];
        ctl.poolAreas = new Vector4[n];
        ctl.poolFloors = new float[n];
        ctl.poolWaterMaterials = new Material[n];
        ctl.poolUnderwaterMaterials = new Material[n];
        ctl.poolCausticsMaterials = new Material[n];
        ctl.poolUnderwaterVolumes = new Renderer[n];
        for (int i = 0; i < n; i++)
        {
            var p = baked[i];
            var w = p.transform.Find(WaterName);
            Rect a = p.Area;
            ctl.pools[i] = w;
            ctl.poolAreas[i] = new Vector4(a.xMin, a.yMin, a.xMax, a.yMax);
            ctl.poolFloors[i] = p.transform.position.y + p.depth;
            ctl.poolWaterMaterials[i] = w.GetComponent<MeshRenderer>().sharedMaterial;
            var fog = w.Find(FogName);
            ctl.poolUnderwaterVolumes[i] = fog != null ? fog.GetComponent<Renderer>() : null;
            ctl.poolUnderwaterMaterials[i] = fog != null ? fog.GetComponent<Renderer>().sharedMaterial : null;
            var caus = w.Find(CausticsName);
            ctl.poolCausticsMaterials[i] = caus != null ? caus.GetComponent<Projector>().material : null;
        }
        EditorUtility.SetDirty(ctl);
    }

    /// <summary>A pool's footprint (world x, z; its water's extent) and the heights between which it is the pool's:
    /// its floor (as baked) to a little over its surface.</summary>
    public static (Rect r, float top, float bottom) Span(ClearwaterPool p)
    {
        float y = p.transform.position.y;
        return (p.Area, y + 0.05f, y + Mathf.Min(p.depth, 0f) - 0.3f);
    }

    /// <summary>What the pools are (their place and size), for the coast's bake: its walkable ground is cut round them.</summary>
    public static void HashInto(System.Text.StringBuilder sb)
    {
        foreach (var p in All()) sb.Append(p.transform.position).Append(p.size).Append(p.depth).Append(p.basin != null);
    }

    /// <summary>What a pool's bake depends on, to tell when it needs baking again.</summary>
    public static string Hash(ClearwaterPool pool)
    {
        // (not what the bake itself writes: the hash, the notes, the depth)
        string saved = pool.bakedHash; var notes = pool.bakeNotes; float depth = pool.depth;
        pool.bakedHash = ""; pool.bakeNotes = null; pool.depth = 0f;
        var sb = new System.Text.StringBuilder(JsonUtility.ToJson(pool));
        pool.bakedHash = saved; pool.bakeNotes = notes; pool.depth = depth;
        sb.Append(pool.name).Append(pool.transform.position).Append(pool.transform.rotation);
        var skip = pool.transform.Find(WaterName);
        foreach (var r in ClearwaterUserTerrain.Renderers(pool.basin, skip))
        {
            var mesh = r.GetComponent<MeshFilter>().sharedMesh;
            sb.Append(mesh.name).Append(mesh.vertexCount).Append(r.transform.localToWorldMatrix);
        }
        var bytes = System.Security.Cryptography.MD5.Create().ComputeHash(System.Text.Encoding.UTF8.GetBytes(sb.ToString()));
        return System.BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
    }

    static Transform Child(Transform parent, string name)
    {
        var t = parent.Find(name);
        if (t != null) return t;
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Bake pool");
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    static T GetOrAdd<T>(this Transform t) where T : Component
    {
        var c = t.GetComponent<T>();
        return c != null ? c : t.gameObject.AddComponent<T>();
    }

    static string Safe(string s)
    {
        foreach (char c in System.IO.Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s.Replace('/', '_').Replace(' ', '_');
    }

    [MenuItem("Tools/Clearwater/Add Pool")]
    static void AddPool()
    {
        var go = new GameObject("Pool");
        Undo.RegisterCreatedObjectUndo(go, "Add pool");
        var sv = SceneView.lastActiveSceneView;
        go.transform.position = sv != null ? sv.pivot : Vector3.zero;
        go.AddComponent<ClearwaterPool>();
        var basin = new GameObject("Basin");
        basin.transform.SetParent(go.transform, false);
        go.GetComponent<ClearwaterPool>().basin = basin;
        Selection.activeGameObject = go;
        Debug.Log("[Clearwater] Pool added: put it at the water's height, its meshes (walls and floor) under Basin, then Bake.");
    }
}
