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

    /// <summary>The coast's bed look (Pebbles when it has none).</summary>
    public static ClearwaterBedLook Of(ClearwaterCoast coast) => coast != null && coast.bedLook != null ? coast.bedLook : Pebbles;

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
