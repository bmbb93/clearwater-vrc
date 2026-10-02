using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The clouds' maps (CloudBake.shader): a seamless tile of cumulus as a height field (the tops and bases, the opacity,
/// the slope of the tops, the light at the bases), in textures that ship with the package (Runtime/Textures/
/// CloudMapA..D): fair weather (A, B) and a fuller sky (C, D). Baking them is a development tool (Tools > Clearwater > Development > Bake Cloud
/// Maps); Ensure gives a material them.
/// </summary>
public static class ClearwaterCloudBake
{
    internal static readonly string[] Props = { "_CloudMapA", "_CloudMapB", "_CloudMapC", "_CloudMapD" };
    static string PathOf(string prop) => ClearwaterSetup.Pkg + "/Textures/" + prop.Substring(1) + ".asset";

    const int Res = 1024;        // texels across the map
    const int VolumeRes = 512;   // the density field: voxels across the tile
    const int VolumeHeight = 64; // and up it
    // the strength from which there is cloud: fair weather (about a third of the sky), a fuller sky (about two thirds)
    internal const float Fair = 0.28f, Full = 0.05f;
    const float Sigma = 40f;     // extinction at density 1, per cloud size

    [MenuItem("Tools/Clearwater/Development/Bake Cloud Maps")]
    public static void BakeMenu()
    {
        Bake(Fair, out var a, out var b);
        Save(a, PathOf(Props[0])); Save(b, PathOf(Props[1]));
        Bake(Full, out var c, out var d);
        Save(c, PathOf(Props[2])); Save(d, PathOf(Props[3]));
        AssetDatabase.SaveAssets();
        Debug.Log("[Clearwater] Cloud maps baked into " + ClearwaterSetup.Pkg + "/Textures");
    }

    /// <summary>Gives m the package's cloud maps where it has others or none (a material made before they came).
    /// True if it changed.</summary>
    internal static bool Ensure(Material m)
    {
        if (m == null || !m.HasProperty(Props[0])) return false;
        bool changed = false;
        foreach (var p in Props)
        {
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(PathOf(p));
            if (t == null || m.GetTexture(p) == t) continue;
            m.SetTexture(p, t);
            changed = true;
        }
        if (changed) { EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m); } // (saved now: not lost with the scene's unsaved state)
        return changed;
    }

    /// <summary>The two maps (A = top, base, opacity, the cloud's number; B = the top's slope x, z, the base's light, the
    /// puffs), linear, with mipmaps, compressed.</summary>
    internal static void Bake(float threshold, out Texture2D a, out Texture2D b, string previewDir = null)
    {
        var mat = new Material(Shader.Find("Hidden/Clearwater/CloudBake"));
        mat.SetFloat("_Threshold", threshold);
        mat.SetFloat("_Sigma", Sigma);
        var vol = new RenderTexture(VolumeRes, VolumeRes, 0, RenderTextureFormat.RHalf, RenderTextureReadWrite.Linear)
        {
            dimension = TextureDimension.Tex3D, volumeDepth = VolumeHeight,
            wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Repeat, wrapModeW = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        vol.Create();
        for (int s = 0; s < VolumeHeight; s++)
        {
            mat.SetFloat("_Slice", (s + 0.5f) / VolumeHeight);
            Graphics.Blit(null, vol, mat, 0, s);
        }
        mat.SetTexture("_Density", vol);
        var rtA = new RenderTexture(Res, Res, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
        {
            useMipMap = true, autoGenerateMips = true, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear
        };
        rtA.Create();
        Graphics.Blit(null, rtA, mat, 1);
        a = Read(rtA, previewDir == null ? null : previewDir + "/CloudMapA.png", true);
        mat.SetTexture("_MapA", rtA); // (B's slopes are A's tops' differences)
        var rtB = RenderTexture.GetTemporary(Res, Res, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        Graphics.Blit(null, rtB, mat, 2);
        b = Read(rtB, previewDir == null ? null : previewDir + "/CloudMapB.png");
        RenderTexture.ReleaseTemporary(rtB);
        rtA.Release();
        Object.DestroyImmediate(rtA);
        vol.Release();
        Object.DestroyImmediate(vol);
        Object.DestroyImmediate(mat);
    }

    static Texture2D Read(RenderTexture rt, string preview, bool numberClouds = false)
    {
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var f = new Texture2D(Res, Res, TextureFormat.RGBAFloat, false, true);
        f.ReadPixels(new Rect(0, 0, Res, Res), 0, 0);
        f.Apply(false);
        RenderTexture.active = prev;
        var t = new Texture2D(Res, Res, TextureFormat.RGBA32, true, true)
        {
            wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8
        };
        var px = f.GetPixels();
        if (numberClouds) NumberClouds(px);
        t.SetPixels(px);
        t.Apply(true);
        if (preview != null) System.IO.File.WriteAllBytes(preview, t.EncodeToPNG());
        Object.DestroyImmediate(f);
        EditorUtility.CompressTexture(t, TextureFormat.BC7, TextureCompressionQuality.Best);
        return t;
    }

    /// <summary>Gives each cloud (the texels of opacity over a tenth that touch, across the map's edges too: it repeats) a
    /// number of its own, 0..1, in a: the shaders keep the clouds whose number is under the cover, whole.</summary>
    static void NumberClouds(Color[] px)
    {
        var label = new int[px.Length];
        var rng = new System.Random(1234);
        var stack = new System.Collections.Generic.Stack<int>();
        for (int i = 0; i < px.Length; i++)
        {
            if (label[i] != 0 || px[i].b < 0.1f) continue;
            float number = (float)rng.NextDouble();
            label[i] = 1;
            stack.Push(i);
            while (stack.Count > 0)
            {
                int k = stack.Pop(), x = k % Res, y = k / Res;
                px[k].a = number;
                int[] next = { y * Res + (x + 1) % Res, y * Res + (x + Res - 1) % Res, ((y + 1) % Res) * Res + x, ((y + Res - 1) % Res) * Res + x };
                foreach (int m in next)
                {
                    if (label[m] != 0 || px[m].b < 0.1f) continue;
                    label[m] = 1;
                    stack.Push(m);
                }
            }
        }
        // the clear sky round a cloud takes its nearest cloud's number (so the mips and the edges' blur keep it)
        for (int pass = 0; pass < 8; pass++)
        {
            for (int i = 0; i < px.Length; i++)
            {
                if (label[i] != 0) continue;
                int x = i % Res, y = i / Res;
                int[] next = { y * Res + (x + 1) % Res, y * Res + (x + Res - 1) % Res, ((y + 1) % Res) * Res + x, ((y + Res - 1) % Res) * Res + x };
                foreach (int m in next)
                    if (label[m] == 1) { px[i].a = px[m].a; label[i] = 2; break; }
            }
            for (int i = 0; i < px.Length; i++) if (label[i] == 2) label[i] = 1;
        }
    }

    static void Save(Texture2D t, string path)
    {
        t.name = System.IO.Path.GetFileNameWithoutExtension(path);
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (existing == null) { AssetDatabase.CreateAsset(t, path); return; }
        EditorUtility.CopySerialized(t, existing); // (the same asset, so what points at it still does)
        EditorUtility.SetDirty(existing);
        Object.DestroyImmediate(t);
    }
}
