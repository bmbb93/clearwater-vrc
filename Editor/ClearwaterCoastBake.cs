using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Turns a ClearwaterCoast into what the shaders and the world need:
///  - _CoastTex: shore coordinates (u = signed distance from the waterline, + seaward; v = distance along it) over
///    the coast's square, computed on the GPU from the line (CoastBake.shader)
///  - _CoastProfile: the cross-section over u (relief-free depth) and the time a wave takes from u to the waterline
///  - the walkable ground: the shaders' own cwFloorDepth evaluated on the GPU at its vertices (FloorBake.shader)
///  - the shore sound's path: the line, handed to the ClearwaterController
/// The materials and the ground are found through the scene's ClearwaterController.
/// </summary>
public static class ClearwaterCoastBake
{
    const int MaxShorePoints = 512;   // CoastBake.shader's array
    const int MaxSoundPoints = 64;    // the controller searches these every frame (Udon is slow)
    const float ProfileStep = 0.125f; // m per cross-section table entry
    const float ProfileMargin = 16f;  // m of level floor kept beyond the section's ends

    public static void Bake(ClearwaterCoast coast)
    {
        var ctl = Object.FindObjectOfType<ClearwaterController>();
        if (ctl == null || ctl.water == null || ctl.waterMaterial == null)
        {
            Debug.LogError("[Clearwater] Bake: no ClearwaterController with its water and materials in the scene.");
            return;
        }
        if (coast.points == null || coast.points.Length < 2)
        {
            Debug.LogError("[Clearwater] Bake: the coast needs at least two points.");
            return;
        }
        if (string.IsNullOrEmpty(ctl.gameObject.scene.path))
        {
            Debug.LogError("[Clearwater] Bake: save the scene first (what is baked for it goes in a folder named after it).");
            return;
        }
        ClearwaterSetup.OwnSceneAssets(ctl);
        string own = ClearwaterSetup.SceneDir(ctl.gameObject.scene); // (where its bakes go)
        var waterRenderer = ctl.water.GetComponent<MeshRenderer>(); // (a scene made before it was drawn first)
        if (waterRenderer != null && waterRenderer.sortingOrder != ClearwaterSetup.WaterSortingOrder)
        {
            Undo.RecordObject(waterRenderer, "Bake coast");
            waterRenderer.sortingOrder = ClearwaterSetup.WaterSortingOrder;
        }
        var seabedRenderer = ctl.water.Find("Seabed")?.GetComponent<MeshRenderer>(); // (and before it took the sun's shadows)
        if (seabedRenderer != null && !seabedRenderer.receiveShadows)
        {
            Undo.RecordObject(seabedRenderer, "Bake coast");
            seabedRenderer.receiveShadows = true;
        }
        Vector3 origin = ctl.water.position;
        Vector2 ToWater(Vector3 w) => new Vector2(w.x - origin.x, -(w.z - origin.z));
        // the line as drawn (straight segments, or the smooth curve sampled about every metre)
        var line = coast.Sampled();
        if (coast.closed) line.RemoveAt(line.Count - 1); // the bake closes the loop itself
        var world = new List<Vector3>();
        foreach (var p in line) world.Add(coast.transform.TransformPoint(p));

        // the user terrain: its heights, and the waterline found on it in place of the line through the walkable area
        var user = ClearwaterUserTerrain.Bake(ctl, coast);
        if (user != null) user.tex = ClearwaterSetup.Save(user.tex, own + "UserTerrain.asset");
        var notes = new List<(string text, List<Vector3> at)>(); // (what is wrong with the user terrain, and where)
        if (user != null && !coast.closed)
        {
            var js = new List<Vector2>();
            foreach (var w in world) js.Add(ToWater(w));
            js = ClearwaterUserTerrain.Splice(js, user, new Vector2(user.area.x, user.area.y), Mathf.Max(coast.groundHalfSize, 1f),
                                              MaxShorePoints, out string note, out var noteAt);
            if (note != null) notes.Add((note, noteAt.ConvertAll(p => new Vector3(p.x + origin.x, origin.y, -p.y + origin.z))));
            world.Clear();
            foreach (var p in js) world.Add(new Vector3(p.x + origin.x, origin.y, -p.y + origin.z));
        }
        else if (user != null) Debug.Log("[Clearwater] User terrain: the coast line is closed, so it is used as drawn (not joined to the waterline found on the terrain).");

        // shore coordinates
        var pts = new Vector4[world.Count];
        float along = 0;
        for (int i = 0; i < world.Count; i++)
        {
            var w = ToWater(world[i]);
            if (i > 0) along += Vector2.Distance(ToWater(world[i - 1]), w);
            pts[i] = new Vector4(w.x, w.y, along, 0);
        }
        Vector2 centre = ToWater(coast.transform.position);
        // v counts from the line's point nearest this object, so it stays small (precise in the half-float bake)
        // around the walkable area however far the line runs
        float v0 = AlongAt(pts, centre);
        for (int i = 0; i < pts.Length; i++) pts[i].z -= v0;
        float size = Mathf.Max(coast.areaSize, 1f);
        int res = Mathf.Clamp(coast.resolution, 64, 4096);
        var field = ClearwaterSetup.Save(BakeCoastField(pts, coast.closed, centre, size, res, TextureFormat.RGFloat), own + "CoastField.asset");
        // the same over the whole sea, coarse: the coast drawn outside the detailed square
        float seaSize = Mathf.Max(coast.seaSize, size);
        int outerRes = Mathf.Clamp(coast.outerResolution, 64, 4096);
        var farField = ClearwaterSetup.Save(BakeCoastField(pts, coast.closed, Vector2.zero, seaSize, outerRes, TextureFormat.RGHalf), own + "CoastFieldOuter.asset");
        farField.name = "CoastFieldOuter";

        // how exposed each stretch of shore is to the swell
        var (exposure, exposureV, expAt, expH, expD) = BakeExposure(coast, pts, centre);
        exposure = ClearwaterSetup.Save(exposure, own + "ShoreExposure.asset");

        // cross-section
        var (profile, range) = BuildProfile(coast);
        profile = ClearwaterSetup.Save(profile, own + "CoastProfile.asset");

        // the beach face's slope over the height the swash climbs (it sets how long the swash takes: gravity along it)
        float swashSlope = SwashSlope(coast, range.z, ctl.waterMaterial.GetFloat("_SwashHeight") * ctl.waterMaterial.GetFloat("_SwashRunup"));
        if (user != null && user.slope > 0f) swashSlope = user.slope; // (the user terrain's own beach face)
        if (ctl.underwaterMaterial != null) ctl.underwaterMaterial.SetFloat("_SwashSlope", swashSlope);

        // every material that draws or follows the floor
        var floorBake = new Material(Shader.Find("Hidden/Clearwater/FloorBake"));
        var mats = new List<Material> { ctl.waterMaterial, floorBake };
        if (ctl.seabedMaterial != null) mats.Add(ctl.seabedMaterial);
        if (ctl.avatarCausticsMaterial != null) mats.Add(ctl.avatarCausticsMaterial);
        if (ctl.underwaterMaterial != null) mats.Add(ctl.underwaterMaterial); // (the surface height at the waterline)
        foreach (var m in mats)
        {
            m.SetTexture("_CoastTex", field);
            m.SetVector("_CoastArea", new Vector4(centre.x, centre.y, size, 0));
            m.SetTexture("_CoastFarTex", farField);
            m.SetVector("_CoastFarArea", new Vector4(0, 0, seaSize, 0));
            m.SetFloat("_ShoreWaves", coast.shoreWaves ? 1 : 0);
            m.SetFloat("_SwashSlope", swashSlope);
            m.SetTexture("_CoastProfile", profile);
            m.SetVector("_CoastProfileU", range);
            m.SetTexture("_ShoreExposure", exposure);
            m.SetVector("_ShoreExposureV", exposureV);
            m.SetTexture("_UserTex", user != null ? user.tex : null);
            m.SetVector("_UserArea", user != null ? user.area : Vector4.zero);
            m.SetVector("_UserMean", user != null ? user.mean : new Vector4(0.2f, 0.2f, 0.2f, 1));
            EditorUtility.SetDirty(m);
        }
        // which way the sea is, for the distant land round the other side (the sky and what reflects it)
        Vector3 seaward = coast.SwellFrom();
        foreach (var m in new[] { ctl.skyMaterial, ctl.waterMaterial, ctl.seabedMaterial, ctl.userBeachMaterial })
        {
            if (m == null || !m.HasProperty("_SeaDir")) continue;
            m.SetVector("_SeaDir", new Vector4(seaward.x, seaward.z, 0, 1));
            EditorUtility.SetDirty(m);
        }
        floorBake.SetTexture("_RockTex", ctl.waterMaterial.GetTexture("_RockTex"));
        floorBake.SetVector("_RockArea", ctl.waterMaterial.GetVector("_RockArea"));

        // stamps: obstacles the waves feel, brushes that raise or carve the ground (before the ground is built)
        var (stampTex, stampArea) = BakeStamps(ctl, coast);
        foreach (var m in mats)
        {
            m.SetTexture("_StampTex", stampTex);
            m.SetVector("_StampArea", stampArea);
        }

        // walkable ground around the coast object, with invisible walls at its edge
        // the pools (their own water, and the sea cut round them), then the walkable ground, with holes for them
        ClearwaterPoolBake.BakeAll();
        BakeGround(ctl, coast, floorBake, user == null);
        if (user != null) notes.AddRange(ClearwaterUserTerrain.Check(user, coast, floorBake, origin));
        Object.DestroyImmediate(floorBake);

        // the sea's extent
        ClearwaterSetup.ApplySeaSize(ctl, coast.seaSize);
        // what is drawn on the user terrain from above (its caustics, its beach: with the seabed's settings)
        ClearwaterUserTerrain.UpdateProjectors(ctl, coast, user);
        // how the bed looks
        ClearwaterBedLooks.Apply(coast);

        // the shore sound follows the listener along the line
        bool soundClosed;
        ctl.shorePoints = SoundPath(world, coast.closed, origin.y, coast.transform.position,
                                    coast.groundHalfSize + SoundReach, out soundClosed);
        ctl.shoreClosed = soundClosed;
        // (the surf is as loud as the waves there are big)
        // (and in step with the waves there, which come a little early or late along the shore)
        var exp = new float[ctl.shorePoints.Length];
        var del = new float[ctl.shorePoints.Length];
        for (int i = 0; i < exp.Length; i++)
        {
            var q = ToWater(ctl.shorePoints[i]);
            float best = float.MaxValue;
            exp[i] = 1f;
            for (int j = 0; j < expAt.Length; j++)
            {
                float d = (expAt[j] - q).sqrMagnitude;
                if (d < best) { best = d; exp[i] = expH[j]; del[i] = expD[j]; }
            }
        }
        ctl.shoreExposure = exp;
        ctl.shoreDelay = del;
        ctl.shoreWaves = coast.shoreWaves;
        EditorUtility.SetDirty(ctl);

        coast.bakeNotes = notes.ConvertAll(n => new ClearwaterCoast.BakeNote { text = n.text, at = n.at.ToArray() }).ToArray();
        foreach (var n in notes) Debug.LogWarning("[Clearwater] User terrain: " + n.text + ".", coast);
        coast.bakedHash = Hash(coast);
        EditorUtility.SetDirty(coast);
        EditorSceneManager.MarkSceneDirty(coast.gameObject.scene);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Clearwater] Coast baked: {world.Count} points, {size} m at {res}², section u {range.x:F1}..{range.y:F1} m, waterline u {range.z:F2}.");
    }

    const float ExposureStep = 2f;    // m between shore exposure samples (more on a very long line)
    const float ExposureSmooth = 10f; // m along the shore it is smoothed over (the waves bend round into the lee)
    const float DelaySmooth = 5f;     // m along the shore the arrival times are smoothed over

    /// <summary>The swell's direction of travel in water space (ClearwaterCoast.SwellFrom, reversed).</summary>
    public static Vector2 SwellTravel(ClearwaterCoast coast)
    {
        Vector3 from = coast.SwellFrom();
        return -new Vector2(from.x, -from.z).normalized; // (water space: z flipped)
    }

    /// <summary>The shore's exposure to the swell along it (ClearwaterCoastExposure, R) and when the swell reaches it
    /// (ClearwaterCoastTiming, G: seconds after the shore nearest the coast object) as a texture over v, with the range
    /// the shaders map it by; also the samples' positions and values, for the surf's loudness and timing.</summary>
    static (Texture2D, Vector4, Vector2[], float[], float[]) BakeExposure(ClearwaterCoast coast, Vector4[] pts, Vector2 centre)
    {
        int n = pts.Length;
        var xy = new Vector2[n];
        for (int i = 0; i < n; i++) xy[i] = pts[i];
        float total = 0;
        for (int k = 0; k < (coast.closed ? n : n - 1); k++) total += Vector2.Distance(xy[k], xy[(k + 1) % n]);
        // whole samples over the line (so a loop wraps exactly), at most 8192
        int steps = Mathf.Clamp(Mathf.RoundToInt(total / ExposureStep), 1, 8191);
        float step = Mathf.Max(total, 1e-3f) / steps;
        float reach = Mathf.Clamp(coast.seaSize * 0.5f, 200f, 3000f);
        var waveTo = SwellTravel(coast);
        var h = ClearwaterCoastExposure.Compute(xy, coast.closed, waveTo, coast.waveSpread,
                                                reach, ExposureSmooth, step, out var at);
        var delay = ClearwaterCoastTiming.ShoreDelays(coast, pts, centre, waveTo, at, coast.closed, DelaySmooth, step);
        var tex = new Texture2D(h.Length, 1, TextureFormat.RGFloat, false, true)
        { name = "ShoreExposure", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color[h.Length];
        for (int i = 0; i < h.Length; i++) px[i] = new Color(h[i], delay[i], 0, 1);
        tex.SetPixels(px);
        tex.Apply(false);
        // (the samples run from the line's start, whose v is pts[0].z, over (count - 1) steps)
        return (tex, new Vector4(pts[0].z, step * (h.Length - 1), coast.closed ? 1 : 0, 1), at, h, delay);
    }

    /// <summary>What a coast's bake depends on (with its stamps), to tell when it needs baking again.</summary>
    public static string Hash(ClearwaterCoast coast)
    {
        string saved = coast.bakedHash;
        var look = coast.bedLook; // (the bed's look needs no bake: it goes straight onto the materials)
        var notes = coast.bakeNotes; // (what the bake found)
        coast.bakedHash = "";
        coast.bedLook = null;
        coast.bakeNotes = null;
        var sb = new System.Text.StringBuilder(JsonUtility.ToJson(coast));
        sb.Append(coast.transform.position).Append(coast.transform.rotation).Append(coast.transform.lossyScale);
        coast.bakedHash = saved;
        coast.bedLook = look;
        coast.bakeNotes = notes;
        foreach (var s in Object.FindObjectsOfType<ClearwaterStamp>())
        {
            sb.Append((int)s.mode).Append(s.receiveCaustics);
            foreach (var mf in s.GetComponentsInChildren<MeshFilter>())
                sb.Append(mf.sharedMesh != null ? mf.sharedMesh.name : "-").Append(mf.transform.localToWorldMatrix);
        }
        if (coast.terrainSource == ClearwaterCoast.TerrainSource.User) ClearwaterUserTerrain.HashInto(coast, sb);
        ClearwaterPoolBake.HashInto(sb); // (the walkable ground is cut round the pools)
        return Hash128.Compute(sb.ToString()).ToString();
    }

    // ---------------------------------------------------------------- stamps

    public const int PropsLayer = 22; // obstacles that receive the caustics (the projector draws on it)

    /// <summary>ClearwaterStamp meshes seen from above over the walkable ground: R = top of raise brushes, G = top of
    /// carve brushes, B = top of obstacles (water-space heights; -50 / +50 / -50 = none). Area w = 1 when any.</summary>
    static (Texture2D, Vector4) BakeStamps(ClearwaterController ctl, ClearwaterCoast coast)
    {
        Vector3 origin = ctl.water.position;
        float half = Mathf.Max(coast.groundHalfSize, 1f), step = Mathf.Max(coast.groundStep, 0.05f);
        Vector3 c = coast.transform.position - origin;
        c = new Vector3(Mathf.Round(c.x / step) * step, 0, Mathf.Round(c.z / step) * step);
        var area = new Vector4(c.x, -c.z, 2 * half, 0);
        var stamps = Object.FindObjectsOfType<ClearwaterStamp>();
        if (stamps.Length == 0)
        {
            AssetDatabase.DeleteAsset(ClearwaterSetup.Gen + "/" + ClearwaterSetup.SceneDir(ctl.gameObject.scene) + "StampHeights.asset");
            return (null, area);
        }
        ClearwaterSetup.NameLayer(PropsLayer, "ClearwaterProps");
        foreach (var proj in Object.FindObjectsOfType<Projector>())
            if (proj.material == ctl.avatarCausticsMaterial) proj.ignoreLayers &= ~(1 << PropsLayer);

        int res = Mathf.Clamp(coast.stampResolution, 64, 4096);
        var mat = new Material(Shader.Find("Hidden/Clearwater/StampBake"));
        mat.SetVector("_StampArea", area);
        mat.SetVector("_WaterOrigin", origin);
        var rt = new RenderTexture(res, res, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        var cmd = new UnityEngine.Rendering.CommandBuffer { name = "Clearwater stamps" };
        cmd.SetRenderTarget(rt);
        cmd.ClearRenderTarget(false, true, new Color(-50, -50, -50, 0));
        // no render-texture flip: row 0 must be the area's near edge, as in the blit-based bakes the shaders match
        cmd.SetViewProjectionMatrices(Matrix4x4.identity, GL.GetGPUProjectionMatrix(Matrix4x4.identity, false));
        var hidden = new List<MeshRenderer>();
        foreach (var s in stamps)
        {
            bool brush = s.mode != ClearwaterStamp.Mode.Obstacle;
            if (brush && !s.CompareTag("EditorOnly")) { Undo.RecordObject(s.gameObject, "Stamp brush"); s.gameObject.tag = "EditorOnly"; }
            foreach (var r in s.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                if (brush)
                {
                    // brushes shape the ground but are not drawn: hidden (drawn as a gizmo while selected)
                    if (!r.enabled) { r.enabled = true; } // (drawn below, hidden again after)
                    hidden.Add(r);
                }
                else if (s.receiveCaustics && r.gameObject.layer != PropsLayer)
                {
                    Undo.RecordObject(r.gameObject, "Stamp layer");
                    r.gameObject.layer = PropsLayer;
                }
                for (int sub = 0; sub < mf.sharedMesh.subMeshCount; sub++)
                    cmd.DrawRenderer(r, mat, sub, (int)s.mode);
            }
        }
        Graphics.ExecuteCommandBuffer(cmd);
        cmd.Release();
        foreach (var r in hidden) { Undo.RecordObject(r, "Hide stamp brush"); r.enabled = false; }

        var prev = RenderTexture.active;
        var read = new Texture2D(res, res, TextureFormat.RGBAFloat, false, true);
        RenderTexture.active = rt;
        read.ReadPixels(new Rect(0, 0, res, res), 0, 0); read.Apply(false);
        RenderTexture.active = prev; rt.Release();
        var px = read.GetPixels();
        Object.DestroyImmediate(read);
        Object.DestroyImmediate(mat);
        // carve back to a height; an obstacle counts only up to a little above the surface, so the broken water round
        // it reaches about as far (~1 m) for a tall pile as for a low rock
        for (int i = 0; i < px.Length; i++) px[i] = new Color(px[i].r, -px[i].g, Mathf.Min(px[i].b, ObstacleCap), 1);
        Slopes(px, res, area.z / res);
        var tex = new Texture2D(res, res, TextureFormat.RGBAHalf, false, true)
        {
            name = "StampHeights", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
        };
        tex.SetPixels(px); tex.Apply(false, false);
        tex = ClearwaterSetup.Save(tex, ClearwaterSetup.SceneDir(ctl.gameObject.scene) + "StampHeights.asset");
        area.w = 1;
        return (tex, area);
    }

    // ---------------------------------------------------------------- shore coordinates

    // format: the detailed field is full float - half floats step v by 3 cm 40 m along the shore, which the foam's
    // fine lace (laid out along v) showed as blocks; the coarse field over the whole sea is only seen from afar
    internal static Texture2D BakeCoastField(Vector4[] shore, bool closed, Vector2 centre, float size, int res, TextureFormat format)
    {
        var mat = new Material(Shader.Find("Hidden/Clearwater/CoastBake"));
        var arr = new Vector4[MaxShorePoints];
        int n = Mathf.Min(shore.Length, MaxShorePoints);
        for (int i = 0; i < n; i++) arr[i] = shore[i];
        mat.SetVectorArray("_ShorePts", arr);
        mat.SetInt("_ShoreCount", n);
        mat.SetFloat("_ShoreClosed", closed ? 1 : 0);
        mat.SetVector("_CoastArea", new Vector4(centre.x, centre.y, size, 0));
        var data = BlitRead(mat, res, res);
        Object.DestroyImmediate(mat);
        var tex = new Texture2D(res, res, format, false, true)
        {
            name = "CoastField", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
        };
        tex.SetPixels(data); tex.Apply(false, false);
        return tex;
    }

    // ---------------------------------------------------------------- cross-section

    /// <summary>The section's depth at u (m from the waterline, + seaward).</summary>
    public static float SectionDepth(ClearwaterCoast c, float u)
    {
        if (c.section == ClearwaterCoast.Section.Curve) return -c.curve.Evaluate(u);
        // gentle beach: dry land, beach face, long knee-deep shallows, a steeper slope, the flat deep bottom, joined
        // with smooth min/max so the slope never jumps (u is shifted so that 0 is the waterline)
        u -= c.shallowDepth / Mathf.Max(c.beachSlope, 1e-3f);
        float shallow = c.shallowDepth + c.shallowSlope * u;
        float steep = c.deepDepth - c.shelfSlope * (c.deepStart - c.shallowDepth / Mathf.Max(c.beachSlope, 1e-3f) - u);
        float beach = c.shallowDepth + c.beachSlope * u;
        float d = Smax(shallow, steep, 0.8f);
        d = Smin(d, c.deepDepth, 0.5f);
        d = Smin(d, beach, 0.3f);
        return Smax(d, -c.landHeight, 0.3f);
    }

    /// <summary>Mean rise per metre of the beach from the waterline (u = uw) up to height rise above still water.</summary>
    static float SwashSlope(ClearwaterCoast c, float uw, float rise)
    {
        rise = Mathf.Max(rise, 0.05f);
        for (float d = 0.05f; d < 60f; d += 0.05f)
            if (-SectionDepth(c, uw - d) >= rise) return Mathf.Clamp(rise / d, 0.02f, 0.6f);
        return 0.02f; // (the land never gets that high: a very flat beach)
    }

    /// <summary>The section's u range (m).</summary>
    public static Vector2 SectionRange(ClearwaterCoast c)
    {
        if (c.section == ClearwaterCoast.Section.Curve && c.curve != null && c.curve.length > 0)
            return new Vector2(c.curve.keys[0].time - ProfileMargin, c.curve.keys[c.curve.length - 1].time + ProfileMargin);
        return new Vector2(-32f, c.deepStart + 176f);
    }

    /// <summary>R = depth, G = seconds for a wave at u to reach the waterline (shallow-water speed sqrt(g d),
    /// integrated from the waterline out). Range = (u min, u max, waterline u).</summary>
    static (Texture2D, Vector4) BuildProfile(ClearwaterCoast c)
    {
        var r = SectionRange(c);
        int n = Mathf.Clamp(Mathf.CeilToInt((r.y - r.x) / ProfileStep) + 1, 256, 8192);
        var depth = new float[n];
        for (int i = 0; i < n; i++) depth[i] = SectionDepth(c, Mathf.Lerp(r.x, r.y, i / (n - 1f)));
        // the waterline: where the floor last comes out of the water, counted from the sea side (a section that is
        // under water all along has its "waterline" at its start)
        int iw = 0;
        for (int i = n - 1; i > 0; i--) if (depth[i - 1] <= 0f && depth[i] > 0f) { iw = i; break; }
        float du = (r.y - r.x) / (n - 1f);
        float uw = iw == 0 ? r.x : Mathf.Lerp(r.x, r.y, (iw - 1 + Mathf.InverseLerp(depth[iw - 1], depth[iw], 0f)) / (n - 1f));
        var travel = new float[n];
        const float g = 9.81f;
        for (int i = iw + 1; i < n; i++)
            travel[i] = travel[i - 1] + du / Mathf.Sqrt(g * Mathf.Max(0.5f * (depth[i] + depth[i - 1]), 0.01f));
        var tex = new Texture2D(n, 1, TextureFormat.RGFloat, false, true)
        {
            name = "CoastProfile", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
        };
        var px = new Color[n];
        for (int i = 0; i < n; i++) px[i] = new Color(depth[i], travel[i], 0, 1);
        tex.SetPixels(px); tex.Apply(false, false);
        return (tex, new Vector4(r.x, r.y, uw, 0));
    }

    static float Smin(float a, float b, float k) { float h = Mathf.Max(k - Mathf.Abs(a - b), 0) / k; return Mathf.Min(a, b) - h * h * k * 0.25f; }
    static float Smax(float a, float b, float k) { float h = Mathf.Max(k - Mathf.Abs(a - b), 0) / k; return Mathf.Max(a, b) + h * h * k * 0.25f; }

    const float StampSlope = 0.6f;  // m per m (~30 deg, loose sand): the skirt round every stamp
    const float ObstacleCap = 0.35f; // m above the surface an obstacle counts for the waves

    /// <summary>Gives every stamp a sloping skirt: raised ground and obstacles fall away at StampSlope, carved
    /// ground rises back at it (a two-pass chamfer distance sweep). Brushes get natural banks instead of cliffs,
    /// and the water shallows towards an obstacle, so waves break round it.</summary>
    static void Slopes(Color[] px, int n, float texel)
    {
        float d1 = StampSlope * texel, d2 = d1 * 1.41421356f;
        void Relax(int p, int q, float d)
        {
            Color a = px[p], b = px[q];
            a.r = Mathf.Max(a.r, b.r - d);
            a.g = Mathf.Min(a.g, b.g + d);
            a.b = Mathf.Max(a.b, b.b - d);
            px[p] = a;
        }
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                int p = y * n + x;
                if (x > 0) Relax(p, p - 1, d1);
                if (y > 0)
                {
                    Relax(p, p - n, d1);
                    if (x > 0) Relax(p, p - n - 1, d2);
                    if (x < n - 1) Relax(p, p - n + 1, d2);
                }
            }
        for (int y = n - 1; y >= 0; y--)
            for (int x = n - 1; x >= 0; x--)
            {
                int p = y * n + x;
                if (x < n - 1) Relax(p, p + 1, d1);
                if (y < n - 1)
                {
                    Relax(p, p + n, d1);
                    if (x < n - 1) Relax(p, p + n + 1, d2);
                    if (x > 0) Relax(p, p + n - 1, d2);
                }
            }
        // soften the stair-steps of the rasterised outlines (two [1 2 1] passes each way, ~2 texels)
        var tmp = new Color[px.Length];
        for (int pass = 0; pass < 2; pass++)
        {
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int p = y * n + x;
                    tmp[p] = (px[y * n + Mathf.Max(x - 1, 0)] + 2 * px[p] + px[y * n + Mathf.Min(x + 1, n - 1)]) * 0.25f;
                }
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int p = y * n + x;
                    px[p] = (tmp[Mathf.Max(y - 1, 0) * n + x] + 2 * tmp[p] + tmp[Mathf.Min(y + 1, n - 1) * n + x]) * 0.25f;
                }
        }
    }

    // ---------------------------------------------------------------- walkable ground

    static void BakeGround(ClearwaterController ctl, ClearwaterCoast coast, Material floorBake, bool ground)
    {
        Transform water = ctl.water;
        var groundTf = water.Find("Seabed Collider");
        if (groundTf == null)
        {
            groundTf = new GameObject("Seabed Collider").transform;
            groundTf.SetParent(water, false);
        }
        float half = Mathf.Max(coast.groundHalfSize, 1f), step = Mathf.Max(coast.groundStep, 0.05f);
        int n = Mathf.RoundToInt(2 * half / step) + 1;
        // centred on the coast object, in the water's frame
        Vector3 c = coast.transform.position - water.position;
        c = new Vector3(Mathf.Round(c.x / step) * step, 0, Mathf.Round(c.z / step) * step);
        groundTf.localPosition = c;

        floorBake.SetVector("_BakeGrid", new Vector4(c.x - half, c.z - half, step, n));
        var px = BlitRead(floorBake, n, n);
        var v = new Vector3[n * n];
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
                v[j * n + i] = new Vector3(-half + i * step, -px[j * n + i].r, -half + j * step);
        // (none in a pool's basin, where the ground would cut through its water: the basin's own colliders are there)
        var spans = new List<(Rect r, float top, float bottom)>();
        foreach (var p in ClearwaterPoolBake.All()) if (p.basin != null) spans.Add(ClearwaterPoolBake.Span(p));
        bool InPool(Vector3 local)
        {
            Vector3 w = water.TransformPoint(groundTf.localPosition + local);
            foreach (var (r, top, bottom) in spans)
                if (r.Contains(new Vector2(w.x, w.z)) && w.y < top && w.y > bottom) return true;
            return false;
        }
        var idx = new List<int>((n - 1) * (n - 1) * 6);
        for (int j = 0; j < n - 1; j++)
            for (int i = 0; i < n - 1; i++)
            {
                int a = j * n + i, b = a + 1, cc = a + n, d = cc + 1;
                if (spans.Count > 0 && InPool((v[a] + v[d]) * 0.5f)) continue;
                idx.Add(a); idx.Add(cc); idx.Add(b); idx.Add(b); idx.Add(cc); idx.Add(d); // facing up
            }
        var mesh = new Mesh { name = "SeabedCollider", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.vertices = v;
        mesh.SetTriangles(idx, 0);
        mesh.bounds = new Bounds(Vector3.zero, new Vector3(2 * half, 12, 2 * half));
        mesh = ClearwaterSetup.Save(mesh, ClearwaterSetup.SceneDir(ctl.gameObject.scene) + "SeabedCollider.asset");

        // (on a user terrain the ground to walk on is its own meshes' colliders: only the walls are made)
        var mc = groundTf.GetComponent<MeshCollider>();
        if (mc == null) mc = groundTf.gameObject.AddComponent<MeshCollider>();
        mc.sharedMesh = null; // re-cook
        mc.sharedMesh = mesh;
        mc.enabled = ground;

        // invisible walls at the edge (the existing ones are kept and moved, so a re-bake leaves the scene file quiet)
        var walls = new List<Transform>();
        foreach (Transform t in groundTf) if (t.name == "Edge Wall") walls.Add(t);
        var spec = new[] {
            (new Vector3(half, 5, 0), new Vector3(1, 20, 2 * half)), (new Vector3(-half, 5, 0), new Vector3(1, 20, 2 * half)),
            (new Vector3(0, 5, half), new Vector3(2 * half, 20, 1)), (new Vector3(0, 5, -half), new Vector3(2 * half, 20, 1)) };
        for (int k = 0; k < spec.Length; k++)
        {
            Transform wall;
            if (k < walls.Count) wall = walls[k];
            else
            {
                wall = new GameObject("Edge Wall").transform;
                wall.SetParent(groundTf, false);
            }
            if (wall.localPosition != spec[k].Item1) wall.localPosition = spec[k].Item1;
            var box = wall.GetComponent<BoxCollider>();
            if (box == null) box = wall.gameObject.AddComponent<BoxCollider>();
            if (box.size != spec[k].Item2) box.size = spec[k].Item2;
        }
        for (int k = spec.Length; k < walls.Count; k++) Object.DestroyImmediate(walls[k].gameObject);
    }

    // ---------------------------------------------------------------- shore sound

    const float SoundReach = 100f; // m past the walkable ground's edge where the shore can still be heard

    /// <summary>The distance along the line (pts: xy = point, z = along) of its point nearest p.</summary>
    static float AlongAt(Vector4[] pts, Vector2 p)
    {
        float best = float.MaxValue, along = 0;
        for (int i = 0; i + 1 < pts.Length; i++)
        {
            Vector2 a = pts[i], b = pts[i + 1], ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
            float d = (a + ab * t - p).sqrMagnitude;
            if (d < best) { best = d; along = Mathf.Lerp(pts[i].z, pts[i + 1].z, t); }
        }
        return along;
    }

    /// <summary>The line for the sound (world, at the water's height): only the part within reach of the walkable
    /// ground (the rest is never heard), split where the line leaves it and comes back (a break point between
    /// pieces, see ClearwaterController), resampled to at most MaxSoundPoints.</summary>
    static Vector3[] SoundPath(List<Vector3> world, bool closed, float y, Vector3 centre, float reach, out bool loop)
    {
        var line = new List<Vector3>(world);
        if (closed) line.Add(world[0]);
        for (int i = 0; i < line.Count; i++) line[i] = new Vector3(line[i].x, y + 0.2f, line[i].z);
        bool Inside(Vector3 p) => Mathf.Abs(p.x - centre.x) <= reach && Mathf.Abs(p.z - centre.z) <= reach;

        // the pieces inside (each keeps one point beyond the edge on either side, so it reaches the edge)
        var pieces = new List<List<Vector3>>();
        List<Vector3> cur = null;
        for (int i = 0; i < line.Count; i++)
        {
            bool inNow = Inside(line[i]) || (i + 1 < line.Count && Inside(line[i + 1])) || (i > 0 && Inside(line[i - 1]));
            if (inNow) { if (cur == null) { cur = new List<Vector3>(); pieces.Add(cur); } cur.Add(line[i]); }
            else cur = null;
        }
        loop = closed && pieces.Count == 1 && pieces[0].Count == line.Count;
        if (pieces.Count == 0) pieces.Add(new List<Vector3> { line[0], line[line.Count - 1] }); // (none near: anything)

        // points per piece in proportion to its length
        var lengths = new float[pieces.Count];
        float total = 0;
        for (int p = 0; p < pieces.Count; p++)
        {
            for (int i = 1; i < pieces[p].Count; i++) lengths[p] += Vector3.Distance(pieces[p][i - 1], pieces[p][i]);
            total += lengths[p];
        }
        int budget = MaxSoundPoints - (pieces.Count - 1); // (break points)
        var outp = new List<Vector3>();
        for (int p = 0; p < pieces.Count; p++)
        {
            if (p > 0) outp.Add(new Vector3(0, ClearwaterSetup.SoundBreakY, 0));
            int m = Mathf.Max(2, Mathf.FloorToInt(budget * lengths[p] / Mathf.Max(total, 1e-3f)));
            outp.AddRange(Resample(pieces[p], m));
        }
        return outp.ToArray();
    }

    static List<Vector3> Resample(List<Vector3> line, int count)
    {
        if (line.Count <= count) return new List<Vector3>(line);
        float total = 0;
        for (int i = 1; i < line.Count; i++) total += Vector3.Distance(line[i - 1], line[i]);
        var outp = new List<Vector3>();
        int seg = 0; float segStart = 0;
        for (int k = 0; k < count; k++)
        {
            float s = total * k / (count - 1);
            while (seg < line.Count - 2 && segStart + Vector3.Distance(line[seg], line[seg + 1]) < s)
            {
                segStart += Vector3.Distance(line[seg], line[seg + 1]);
                seg++;
            }
            float len = Mathf.Max(Vector3.Distance(line[seg], line[seg + 1]), 1e-4f);
            outp.Add(Vector3.Lerp(line[seg], line[seg + 1], Mathf.Clamp01((s - segStart) / len)));
        }
        return outp;
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Runs a bake material over a float render target and reads it back.</summary>
    public static Color[] BlitRead(Material mat, int w, int h)
    {
        var prev = RenderTexture.active; // (Blit leaves the target active)
        var rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        Graphics.Blit(null, rt, mat);
        var read = new Texture2D(w, h, TextureFormat.RGBAFloat, false, true);
        RenderTexture.active = rt;
        read.ReadPixels(new Rect(0, 0, w, h), 0, 0); read.Apply(false);
        RenderTexture.active = prev; rt.Release();
        var px = read.GetPixels();
        Object.DestroyImmediate(read);
        return px;
    }
}
