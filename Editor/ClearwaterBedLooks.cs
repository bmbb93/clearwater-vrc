using UnityEditor;
using UnityEngine;

/// <summary>
/// The bed looks the package brings (Pebbles, Sand), and putting the coast's look on the materials that draw the bed:
/// at once when it (or the look itself) is changed, and again at every bake.
/// </summary>
public static class ClearwaterBedLooks
{
    public const string Folder = ClearwaterSetup.Pkg + "/BedLooks";

    public static ClearwaterBedLook Pebbles => AssetDatabase.LoadAssetAtPath<ClearwaterBedLook>(Folder + "/Pebbles.asset");
    public static ClearwaterBedLook Sand => AssetDatabase.LoadAssetAtPath<ClearwaterBedLook>(Folder + "/Sand.asset");

    /// <summary>The look a coast without one of its own gets (and a new scene's materials): the sand beach.</summary>
    public static ClearwaterBedLook Default => Sand;

    /// <summary>The coast's bed look (the default when it has none).</summary>
    public static ClearwaterBedLook Of(ClearwaterCoast coast) => coast != null && coast.bedLook != null ? coast.bedLook : Default;

    /// <summary>Puts the coast's bed look on the scene's water and seabed materials.</summary>
    public static void Apply(ClearwaterCoast coast)
    {
        var look = Of(coast);
        var ctl = Object.FindObjectOfType<ClearwaterController>(true);
        if (look == null || ctl == null) return;
        foreach (var m in new[] { ctl.waterMaterial, ctl.seabedMaterial })
        {
            if (m == null) continue;
            Undo.RecordObject(m, "Clearwater bed look");
            look.ApplyTo(m);
            EditorUtility.SetDirty(m);
        }
    }

    /// <summary>A look made from the user terrain, so the generated ground past it (outside the walkable area, and the
    /// sea floor beyond) looks like it: the colour texture, colour, tile size and smoothness of what covers most of it (a mesh's
    /// main material, or a Unity terrain's most used layer), with no effects over it. Made, or brought up to date, as
    /// "(scene) Bed Look" beside the scene, and chosen on the coast. Null when the coast has no user terrain.</summary>
    public static ClearwaterBedLook FromUserTerrain(ClearwaterCoast coast)
    {
        Texture2D tex = null; Color tint = Color.white; float tile = 1f, gloss = 0f, best = 0f;
        foreach (var r in ClearwaterUserTerrain.Renderers(coast))
        {
            var m = r.sharedMaterial;
            float area = r.bounds.size.x * r.bounds.size.z;
            if (m == null || area <= best || !m.HasProperty("_MainTex") || !(m.mainTexture is Texture2D t)) continue;
            float perUv = MetresPerUv(r.GetComponent<MeshFilter>().sharedMesh, r.transform);
            if (perUv <= 0f) continue;
            best = area; tex = t;
            tint = m.HasProperty("_Color") ? m.color.linear : Color.white;
            tile = perUv / Mathf.Max(m.mainTextureScale.x, 1e-4f);
            // its smoothness (the Standard shader's slider, or the scale on a smoothness map; other shaders' _Smoothness)
            bool glossMap = (m.HasProperty("_MetallicGlossMap") && m.GetTexture("_MetallicGlossMap") != null) ||
                            (m.HasProperty("_SmoothnessTextureChannel") && m.GetFloat("_SmoothnessTextureChannel") > 0.5f);
            gloss = glossMap && m.HasProperty("_GlossMapScale") ? m.GetFloat("_GlossMapScale")
                  : m.HasProperty("_Glossiness") ? m.GetFloat("_Glossiness")
                  : m.HasProperty("_Smoothness") ? m.GetFloat("_Smoothness") : 0f;
        }
        foreach (var t in ClearwaterUserTerrain.Terrains(coast))
        {
            var td = t.terrainData;
            float area = td.size.x * td.size.z;
            var layers = td.terrainLayers;
            if (area <= best || layers == null || layers.Length == 0) continue;
            // its most used layer
            var maps = td.GetAlphamaps(0, 0, td.alphamapWidth, td.alphamapHeight);
            var share = new float[layers.Length];
            for (int y = 0; y < td.alphamapHeight; y += 4)
                for (int x = 0; x < td.alphamapWidth; x += 4)
                    for (int l = 0; l < layers.Length && l < maps.GetLength(2); l++) share[l] += maps[y, x, l];
            int most = 0;
            for (int l = 1; l < layers.Length; l++) if (share[l] > share[most]) most = l;
            var layer = layers[most];
            if (layer == null || layer.diffuseTexture == null) continue;
            best = area; tex = layer.diffuseTexture;
            Vector4 rm = layer.diffuseRemapMax;
            tint = new Color(rm.x, rm.y, rm.z);
            tile = layer.tileSize.x;
            // (the layer's smoothness is only used by the Standard terrain shader, the default)
            var tm = t.materialTemplate;
            gloss = tm == null || tm.shader.name.Contains("Standard") ? layer.smoothness : 0f;
        }
        if (tex == null)
        {
            Debug.LogWarning("[Clearwater] The user terrain has no textured mesh or terrain layer to make a bed look from.");
            return null;
        }

        var scene = coast.gameObject.scene;
        string dir = string.IsNullOrEmpty(scene.path) ? "Assets" : System.IO.Path.GetDirectoryName(scene.path).Replace('\\', '/');
        string path = dir + "/" + (string.IsNullOrEmpty(scene.name) ? "Untitled" : scene.name) + " Bed Look.asset";
        var look = AssetDatabase.LoadAssetAtPath<ClearwaterBedLook>(path);
        if (look == null)
        {
            look = ScriptableObject.CreateInstance<ClearwaterBedLook>();
            AssetDatabase.CreateAsset(look, path);
        }
        Undo.RecordObject(look, "Bed look from user terrain");
        look.color = tex;
        look.height = null;
        look.tileSize = Mathf.Max(0.05f, tile);
        look.largerPatches = 0f;
        look.sandFill = false; look.sandBed = false; look.rippleMarks = false; look.weedTint = false;
        look.tint = new Color(tint.r, tint.g, tint.b, 1f);
        look.saturation = 1f; look.brightness = 1f; look.patchiness = 0f; look.mutedGrade = 0f;
        look.smoothness = gloss;
        EditorUtility.SetDirty(look);
        AssetDatabase.SaveAssets();

        Undo.RecordObject(coast, "Bed look from user terrain");
        coast.bedLook = look;
        EditorUtility.SetDirty(coast);
        Apply(coast);
        return look;
    }

    // how many metres one unit of the mesh's uv covers on it (from its triangles' areas in the world and in uv)
    static float MetresPerUv(Mesh mesh, Transform tf)
    {
        if (mesh == null) return 0f;
        var v = mesh.vertices; var uv = mesh.uv; var idx = mesh.triangles;
        if (uv == null || uv.Length != v.Length) return 0f;
        double world = 0, flat = 0;
        int stride = Mathf.Max(1, idx.Length / 3 / 20000) * 3; // (a sample of up to about 20000 triangles)
        for (int k = 0; k + 2 < idx.Length; k += stride)
        {
            Vector3 a = tf.TransformPoint(v[idx[k]]), b = tf.TransformPoint(v[idx[k + 1]]), c = tf.TransformPoint(v[idx[k + 2]]);
            world += Vector3.Cross(b - a, c - a).magnitude;
            Vector2 p = uv[idx[k]], q = uv[idx[k + 1]], s = uv[idx[k + 2]];
            flat += Mathf.Abs((q.x - p.x) * (s.y - p.y) - (q.y - p.y) * (s.x - p.x));
        }
        return flat > 0 ? (float)System.Math.Sqrt(world / flat) : 0f;
    }

    /// <summary>The texture import settings the looks' textures need (colour sRGB, height linear; tiling, mipmapped,
    /// sharp at a glancing angle).</summary>
    public static void SetupImporters()
    {
        Import(ClearwaterSetup.Pkg + "/Textures/Pebbles.jpg", true);
        Import(ClearwaterSetup.Pkg + "/Textures/Sand.jpg", true);
        Import(ClearwaterSetup.Pkg + "/Textures/SandHeight.png", false);
    }

    static void Import(string path, bool srgb)
    {
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) return;
        if (ti.sRGBTexture == srgb && ti.mipmapEnabled && ti.wrapMode == TextureWrapMode.Repeat && ti.filterMode == FilterMode.Trilinear &&
            ti.anisoLevel == 16 && ti.maxTextureSize == 1024 && ti.textureCompression == TextureImporterCompression.CompressedHQ)
            return; // (the package ships with these settings; only fix them if they were changed)
        ti.sRGBTexture = srgb;
        ti.mipmapEnabled = true;
        ti.wrapMode = TextureWrapMode.Repeat;
        ti.filterMode = FilterMode.Trilinear;
        ti.anisoLevel = 16;
        ti.maxTextureSize = 1024;
        ti.textureCompression = TextureImporterCompression.CompressedHQ;
        ti.SaveAndReimport();
    }

    /// <summary>(Re)makes the package's two looks. Pebbles is the original look exactly (the shaders' defaults).</summary>
    public static void MakePresets()
    {
        SetupImporters();
        var peb = Make("Pebbles");
        peb.color = AssetDatabase.LoadAssetAtPath<Texture2D>(ClearwaterSetup.Pkg + "/Textures/Pebbles.jpg");
        peb.height = null;
        peb.tileSize = 0.78f;
        peb.largerPatches = 1f;
        peb.sandFill = true; peb.sandFillAmount = 1f; peb.sandColor = new Color(0.60f, 0.55f, 0.44f);
        peb.sandBed = false;
        peb.rippleMarks = true; peb.rippleStrength = 1f;
        peb.weedTint = true; peb.weedAmount = 0.7f;
        peb.tint = new Color(1.10f, 1.0f, 0.86f); peb.saturation = 0.8f; peb.brightness = 1f; peb.patchiness = 1f; peb.mutedGrade = 1f;
        EditorUtility.SetDirty(peb);

        var sand = Make("Sand");
        sand.color = AssetDatabase.LoadAssetAtPath<Texture2D>(ClearwaterSetup.Pkg + "/Textures/Sand.jpg");
        sand.height = AssetDatabase.LoadAssetAtPath<Texture2D>(ClearwaterSetup.Pkg + "/Textures/SandHeight.png");
        sand.tileSize = 1f;
        sand.largerPatches = 0f;
        sand.sandFill = false; sand.sandColor = new Color(0.62f, 0.55f, 0.43f);
        sand.sandBed = true;
        sand.rippleMarks = true; sand.rippleStrength = 1f;
        sand.weedTint = false; sand.weedAmount = 0.3f;
        sand.tint = Color.white; sand.saturation = 0.9f; sand.brightness = 0.8f; sand.patchiness = 0.35f; sand.mutedGrade = 0f;
        EditorUtility.SetDirty(sand);
        AssetDatabase.SaveAssets();
    }

    static ClearwaterBedLook Make(string name)
    {
        string path = Folder + "/" + name + ".asset";
        var look = AssetDatabase.LoadAssetAtPath<ClearwaterBedLook>(path);
        if (look != null) return look;
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder(ClearwaterSetup.Pkg, "BedLooks");
        look = ScriptableObject.CreateInstance<ClearwaterBedLook>();
        AssetDatabase.CreateAsset(look, path);
        return look;
    }

    /// <summary>The look's inspector: its changes show at once on every coast that uses it.</summary>
    [CustomEditor(typeof(ClearwaterBedLook))]
    class Inspector : Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck();
            DrawDefaultInspector();
            if (!EditorGUI.EndChangeCheck()) return;
            serializedObject.ApplyModifiedProperties();
            foreach (var c in Object.FindObjectsOfType<ClearwaterCoast>(true))
                if (Of(c) == target) Apply(c);
        }
    }
}
