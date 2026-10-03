using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The clouds. Their shapes come from three noise textures that ship with the package (Runtime/Textures/CloudShape,
/// CloudDetail, CloudWeather; baking them is a development tool: Tools > Clearwater > Development > Bake Cloud Noise).
/// Each scene draws them into a dome of its own (Generated/(scene)/CloudDome, a CustomRenderTexture with its material,
/// CRT_CloudDome.shader), which the sky, the water and the beach read (ClearwaterCommon.cginc); EnsureScene makes it and
/// hands it and the noise to the scene's materials.
/// </summary>
public static class ClearwaterCloudBake
{
    static string PathOf(string name) => ClearwaterSetup.Pkg + "/Textures/" + name + ".asset";
    const string ShapeName = "CloudShape", DetailName = "CloudDetail", WeatherName = "CloudWeather";
    internal const string DomeName = "CloudDome";
    internal const int DomeWidth = 2048, DomeHeight = 1024;

    const int ShapeN = 64, DetailN = 32, WeatherN = 256;

    [MenuItem("Tools/Clearwater/Development/Bake Cloud Noise")]
    public static void BakeMenu()
    {
        var t0 = System.DateTime.Now;
        Save3D(BakeShape(), PathOf(ShapeName));
        Save3D(BakeDetail(), PathOf(DetailName));
        Save2D(BakeWeather(), PathOf(WeatherName));
        AssetDatabase.SaveAssets();
        Debug.Log("[Clearwater] Cloud noise baked into " + ClearwaterSetup.Pkg + "/Textures in " +
                  (System.DateTime.Now - t0).TotalSeconds.ToString("0.0") + " s");
    }

    // ---------------------------------------------------------------- the scene's dome

    /// <summary>The scene's dome (made if missing, its settings brought up to date), the controller pointed at its
    /// material, and the dome and the noise handed to the controller's materials. The scene must have been saved.</summary>
    internal static void EnsureScene(ClearwaterController ctl)
    {
        if (ctl == null || string.IsNullOrEmpty(ctl.gameObject.scene.path) || ctl.skyMaterial == null) return;
        var weather = AssetDatabase.LoadAssetAtPath<Texture2D>(PathOf(WeatherName));
        var shape = AssetDatabase.LoadAssetAtPath<Texture3D>(PathOf(ShapeName));
        var detail = AssetDatabase.LoadAssetAtPath<Texture3D>(PathOf(DetailName));
        string dir = ClearwaterSetup.SceneDir(ctl.gameObject.scene);
        var mat = ClearwaterSetup.Mat("Clearwater/CRT/CloudDome", DomeName, dir);
        bool changed = false;
        changed |= SetTex(mat, "_CloudShape", shape);
        changed |= SetTex(mat, "_CloudDetail", detail);
        changed |= SetTex(mat, "_CloudWeather", weather);
        changed |= CopyFloats(ctl.skyMaterial, mat);
        if (mat.GetVector("_SunDir") != ctl.skyMaterial.GetVector("_SunDir")) { mat.SetVector("_SunDir", ctl.skyMaterial.GetVector("_SunDir")); changed = true; }
        if (changed) { EditorUtility.SetDirty(mat); AssetDatabase.SaveAssetIfDirty(mat); }
        var dome = ClearwaterSetup.LoadOrMakeCRT(dir + DomeName, DomeWidth, DomeHeight, RenderTextureFormat.ARGBHalf, mat,
            FilterMode.Bilinear, TextureWrapMode.Repeat, doubleBuffered: true);
        if (ctl.cloudDomeMaterial != mat)
        {
            Undo.RecordObject(ctl, "Clearwater clouds");
            ctl.cloudDomeMaterial = mat;
            EditorUtility.SetDirty(ctl);
        }
        foreach (var m in Materials(ctl)) Ensure(m, dome, weather);
    }

    // what reads the clouds: the sky, the water, the seabed, the beach on the user terrain, the pools' water
    static System.Collections.Generic.IEnumerable<Material> Materials(ClearwaterController ctl)
    {
        foreach (var m in new[] { ctl.skyMaterial, ctl.waterMaterial, ctl.seabedMaterial, ctl.userBeachMaterial }) if (m != null) yield return m;
        if (ctl.poolWaterMaterials != null) foreach (var m in ctl.poolWaterMaterials) if (m != null) yield return m;
    }

    /// <summary>Gives m the dome and the weather map where it has others or none. True if it changed.</summary>
    static bool Ensure(Material m, Texture dome, Texture weather)
    {
        if (m == null || !m.HasProperty("_CloudDome")) return false;
        bool changed = SetTex(m, "_CloudDome", dome) | SetTex(m, "_CloudWeather", weather) | DropOldMaps(m);
        if (changed) { EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m); } // (saved now: not lost with the scene's unsaved state)
        return changed;
    }

    // the clouds' maps of before (1.3's first try; the package has them no more): their slots left on the material
    static bool DropOldMaps(Material m)
    {
        var so = new SerializedObject(m);
        var envs = so.FindProperty("m_SavedProperties.m_TexEnvs");
        bool dropped = false;
        for (int i = envs.arraySize - 1; i >= 0; i--)
        {
            if (!envs.GetArrayElementAtIndex(i).FindPropertyRelative("first").stringValue.StartsWith("_CloudMap")) continue;
            envs.DeleteArrayElementAtIndex(i);
            dropped = true;
        }
        if (dropped) so.ApplyModifiedPropertiesWithoutUndo();
        return dropped;
    }

    static bool SetTex(Material m, string prop, Texture t)
    {
        if (t == null || !m.HasProperty(prop) || m.GetTexture(prop) == t) return false;
        m.SetTexture(prop, t);
        return true;
    }

    static readonly string[] Floats = { "_CloudCover", "_CloudSize", "_CloudSpeed", "_CloudDir", "_CloudShift", "_CloudClassic" };
    static bool CopyFloats(Material from, Material to)
    {
        bool changed = false;
        foreach (var p in Floats)
        {
            if (!from.HasProperty(p) || !to.HasProperty(p) || to.GetFloat(p) == from.GetFloat(p)) continue;
            to.SetFloat(p, from.GetFloat(p));
            changed = true;
        }
        return changed;
    }

    // ---------------------------------------------------------------- the noise (tileable)

    static float Hash3(int x, int y, int z, int seed)
    {
        unchecked
        {
            uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)z * 2147483647u + (uint)seed * 144269504u;
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return h / 4294967295f;
        }
    }

    static int Wrap(int i, int n) => ((i % n) + n) % n;

    // the distance to the nearest of one random point per cell, cells of 1 on a grid n across (periodic)
    static float Worley3(float x, float y, float z, int n, int seed)
    {
        int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y), zi = Mathf.FloorToInt(z);
        float d = 9f;
        for (int k = -1; k <= 1; k++)
        for (int j = -1; j <= 1; j++)
        for (int i = -1; i <= 1; i++)
        {
            int cx = xi + i, cy = yi + j, cz = zi + k;
            int wx = Wrap(cx, n), wy = Wrap(cy, n), wz = Wrap(cz, n);
            float dx = cx + Hash3(wx, wy, wz, seed) - x, dy = cy + Hash3(wx, wy, wz, seed + 1) - y, dz = cz + Hash3(wx, wy, wz, seed + 2) - z;
            float dd = dx * dx + dy * dy + dz * dz;
            if (dd < d) d = dd;
        }
        return Mathf.Sqrt(d);
    }

    static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

    static float Grad3(int ix, int iy, int iz, int n, int seed, float x, float y, float z)
    {
        int wx = Wrap(ix, n), wy = Wrap(iy, n), wz = Wrap(iz, n);
        float a = Hash3(wx, wy, wz, seed) * Mathf.PI * 2f, b = Mathf.Acos(Hash3(wx, wy, wz, seed + 7) * 2f - 1f);
        return Mathf.Sin(b) * Mathf.Cos(a) * x + Mathf.Sin(b) * Mathf.Sin(a) * y + Mathf.Cos(b) * z;
    }

    // gradient noise, -1..1 roughly, periodic over n
    static float Perlin3(float x, float y, float z, int n, int seed)
    {
        int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y), zi = Mathf.FloorToInt(z);
        float xf = x - xi, yf = y - yi, zf = z - zi;
        float u = Fade(xf), v = Fade(yf), w = Fade(zf);
        float C(int i, int j, int k) => Grad3(xi + i, yi + j, zi + k, n, seed, xf - i, yf - j, zf - k);
        return Mathf.Lerp(Mathf.Lerp(Mathf.Lerp(C(0, 0, 0), C(1, 0, 0), u), Mathf.Lerp(C(0, 1, 0), C(1, 1, 0), u), v),
                          Mathf.Lerp(Mathf.Lerp(C(0, 0, 1), C(1, 0, 1), u), Mathf.Lerp(C(0, 1, 1), C(1, 1, 1), u), v), w);
    }

    // 1 at a cell's point, 0 a cell away: billows
    static float WF(float x, float y, float z, int f, int seed) => 1f - Mathf.Min(1f, Worley3(x * f, y * f, z * f, f, seed));

    static Texture3D Make3D(int n, System.Func<float, float, float, Color> fn)
    {
        var px = new Color32[n * n * n];
        System.Threading.Tasks.Parallel.For(0, n, z =>
        {
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
                px[(z * n + y) * n + x] = fn((float)x / n, (float)y / n, (float)z / n);
        });
        var t = new Texture3D(n, n, n, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear
        };
        t.SetPixels32(px);
        t.Apply(false);
        return t;
    }

    // The big shape, 64 across a tile 1.8 cloud sizes wide (Schneider's): r Perlin-Worley (round lobes on a cloudy
    // field), g, b, a Worley at rising frequencies
    static Texture3D BakeShape() => Make3D(ShapeN, (x, y, z) =>
    {
        float p = 0.5f + 0.5f * (0.6f * Perlin3(x * 4, y * 4, z * 4, 4, 11) + 0.3f * Perlin3(x * 8, y * 8, z * 8, 8, 12) + 0.1f * Perlin3(x * 16, y * 16, z * 16, 16, 13));
        float w1 = WF(x, y, z, 4, 21) * 0.625f + WF(x, y, z, 8, 22) * 0.25f + WF(x, y, z, 16, 23) * 0.125f;
        float pw = Mathf.Clamp01((p - (1f - w1)) / (w1 + 1e-4f) * 0.5f + p * 0.5f);
        float g = WF(x, y, z, 4, 31) * 0.625f + WF(x, y, z, 8, 32) * 0.25f + WF(x, y, z, 16, 33) * 0.125f;
        float b = WF(x, y, z, 8, 41) * 0.625f + WF(x, y, z, 16, 42) * 0.25f + WF(x, y, z, 32, 43) * 0.125f;
        float a = WF(x, y, z, 16, 51) * 0.625f + WF(x, y, z, 32, 52) * 0.25f + WF(x, y, z, 64, 53) * 0.125f;
        return new Color(pw, g, b, a);
    });

    // the fine detail that frays the edges, 32 across a tile a fifth of a cloud size wide
    static Texture3D BakeDetail() => Make3D(DetailN, (x, y, z) => new Color(
        WF(x, y, z, 2, 61) * 0.625f + WF(x, y, z, 4, 62) * 0.25f + WF(x, y, z, 8, 63) * 0.125f,
        WF(x, y, z, 4, 71) * 0.625f + WF(x, y, z, 8, 72) * 0.25f + WF(x, y, z, 16, 73) * 0.125f,
        WF(x, y, z, 8, 81), 1f));

    // Where the clouds are, 12 cloud sizes across: r how strongly (spread evenly over 0..1, so a cover of c lets clouds
    // over about a share c of the sky), g how tall they grow, b a slow field (spread evenly too) that raises and lowers
    // the cover where it is read five times as large, a another that chooses between two readings of the map (so its
    // tiles never come round alike: ClearwaterCommon: cwCloudWeather)
    static Texture2D BakeWeather()
    {
        int n = WeatherN;
        var r = new float[n * n];
        var slow = new float[n * n];
        var slow2 = new float[n * n];
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            float u = (float)x / n, v = (float)y / n;
            r[y * n + x] = Mathf.Clamp01(0.55f * (1f - Mathf.Min(1f, Worley3(u * 6, v * 6, 0.5f, 6, 91)))
                                         + 0.3f * (0.5f + 0.5f * Perlin3(u * 8, v * 8, 0.3f, 8, 92))
                                         + 0.15f * (0.5f + 0.5f * Perlin3(u * 16, v * 16, 0.7f, 16, 93)));
            px[y * n + x].g = 0.5f + 0.5f * Perlin3(u * 3, v * 3, 0.2f, 3, 94);
            slow[y * n + x] = 0.6f * Perlin3(u * 2, v * 2, 0.4f, 2, 95) + 0.3f * Perlin3(u * 4, v * 4, 0.6f, 4, 96) + 0.1f * Perlin3(u * 8, v * 8, 0.8f, 8, 97);
            slow2[y * n + x] = 0.6f * Perlin3(u * 2, v * 2, 0.45f, 2, 98) + 0.3f * Perlin3(u * 4, v * 4, 0.65f, 4, 99) + 0.1f * Perlin3(u * 8, v * 8, 0.85f, 8, 100);
        }
        var order = new int[n * n];
        for (int i = 0; i < order.Length; i++) order[i] = i;
        System.Array.Sort(order, (a, b) => r[a].CompareTo(r[b]));
        for (int k = 0; k < order.Length; k++) px[order[k]].r = (float)k / (order.Length - 1);
        for (int i = 0; i < order.Length; i++) order[i] = i;
        System.Array.Sort(order, (a, b) => slow[a].CompareTo(slow[b]));
        for (int k = 0; k < order.Length; k++) px[order[k]].b = (float)k / (order.Length - 1);
        for (int i = 0; i < order.Length; i++) order[i] = i;
        System.Array.Sort(order, (a, b) => slow2[a].CompareTo(slow2[b]));
        for (int k = 0; k < order.Length; k++) px[order[k]].a = (float)k / (order.Length - 1);
        var t = new Texture2D(n, n, TextureFormat.RGBA32, true, true)
        {
            wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear
        };
        t.SetPixels(px);
        t.Apply(true);
        return t;
    }

    static void Save3D(Texture3D t, string path)
    {
        t.name = System.IO.Path.GetFileNameWithoutExtension(path);
        var existing = AssetDatabase.LoadAssetAtPath<Texture3D>(path);
        if (existing == null) { AssetDatabase.CreateAsset(t, path); return; }
        EditorUtility.CopySerialized(t, existing); // (the same asset, so what points at it still does)
        EditorUtility.SetDirty(existing);
        Object.DestroyImmediate(t);
    }

    static void Save2D(Texture2D t, string path)
    {
        t.name = System.IO.Path.GetFileNameWithoutExtension(path);
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (existing == null) { AssetDatabase.CreateAsset(t, path); return; }
        EditorUtility.CopySerialized(t, existing);
        EditorUtility.SetDirty(existing);
        Object.DestroyImmediate(t);
    }
}
