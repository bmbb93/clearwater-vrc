using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
#if CW_PPV2
using UnityEngine.Rendering.PostProcessing;
#endif

/// <summary>
/// Where Clearwater's tone mapping happens. By default each of its shaders (water, seabed, sky, underwater fog) tone
/// maps its own output, so the world needs no post-processing and avatars keep their usual look. The alternative is
/// one post-processing pass (Unity's Post Processing Stack v2) over the whole view: its shaders then output linear HDR,
/// the whole scene shares one tone curve, and bloom can be added. Avatars are tone mapped too (they can look darker
/// and softer), and it costs a full-screen pass.
/// </summary>
public static class ClearwaterToneMapping
{
    const string MenuShaders = "Tools/Clearwater/Tone Mapping/In Shaders (default)";
    const string MenuPost = "Tools/Clearwater/Tone Mapping/In Post-processing (PPv2)";
    internal const string ProfilePath = ClearwaterSetup.Gen + "/ClearwaterPost.asset";
    internal const string LutPath = ClearwaterSetup.Gen + "/ClearwaterToneLut.asset";
    const string VolumeName = "Clearwater Post-processing";
    const string LayerName = "PostProcessing";

    internal const float BloomIntensity = 0.15f; // (a faint glow only round what is really bright: the sun, its glints)
    internal const float BloomThreshold = 2.0f;

    /// <summary>The Clearwater materials that tone map (their "Tone map in shader" toggle).</summary>
    static IEnumerable<Material> Materials()
    {
        var seen = new HashSet<Material>();
        foreach (var ctl in Object.FindObjectsOfType<ClearwaterController>(true))
            foreach (var m in new[] { ctl.waterMaterial, ctl.seabedMaterial, ctl.skyMaterial, ctl.underwaterMaterial, ctl.userBeachMaterial })
                if (m != null && m.HasProperty("_Tonemap") && seen.Add(m)) yield return m;
        // (the pools' copies of the water's)
        foreach (var pool in Object.FindObjectsOfType<ClearwaterPool>(true))
        {
            var water = pool.transform.Find(ClearwaterPoolBake.WaterName);
            if (water == null) continue;
            foreach (var r in water.GetComponentsInChildren<Renderer>(true))
                if (r.sharedMaterial != null && r.sharedMaterial.HasProperty("_Tonemap") && seen.Add(r.sharedMaterial)) yield return r.sharedMaterial;
        }
        if (RenderSettings.skybox != null && RenderSettings.skybox.HasProperty("_Tonemap") && seen.Add(RenderSettings.skybox))
            yield return RenderSettings.skybox;
    }

    static void SetShaderTonemap(bool on)
    {
        foreach (var m in Materials())
        {
            Undo.RecordObject(m, "Clearwater tone mapping");
            m.SetFloat("_Tonemap", on ? 1f : 0f);
            if (on) m.EnableKeyword("_CW_TONEMAP"); else m.DisableKeyword("_CW_TONEMAP");
            EditorUtility.SetDirty(m);
        }
    }

    /// <summary>True when the open scene's Clearwater materials leave tone mapping to post-processing.</summary>
    public static bool InPost()
    {
        foreach (var m in Materials()) return !m.IsKeywordEnabled("_CW_TONEMAP");
        return false;
    }

    [MenuItem(MenuShaders, true)]
    static bool ShadersCheck() { Menu.SetChecked(MenuShaders, !InPost()); Menu.SetChecked(MenuPost, InPost()); return true; }

    [MenuItem(MenuShaders, false, 40)]
    public static void UseShaders()
    {
        SetShaderTonemap(true);
#if CW_PPV2
        var vol = GameObject.Find(VolumeName);
        if (vol != null) Undo.DestroyObjectImmediate(vol);
        // the layer on the cameras is ours only if no other volume is left in the scene
        if (Object.FindObjectsOfType<PostProcessVolume>(true).Length == 0)
            foreach (var cam in Cameras())
            {
                var l = cam.GetComponent<PostProcessLayer>();
                if (l != null) Undo.DestroyObjectImmediate(l);
            }
#endif
        Debug.Log("[Clearwater] Tone mapping in the shaders.");
    }

#if CW_PPV2
    [MenuItem(MenuPost, true)]
    static bool PostCheck() { Menu.SetChecked(MenuShaders, !InPost()); Menu.SetChecked(MenuPost, InPost()); return true; }

    [MenuItem(MenuPost, false, 41)]
    public static void UsePost()
    {
        int layer = PostLayer();
        BakeLut(ExposureNow());
        var profile = Profile();
        // one global volume with the profile
        var vol = GameObject.Find(VolumeName);
        if (vol == null)
        {
            vol = new GameObject(VolumeName);
            Undo.RegisterCreatedObjectUndo(vol, "Clearwater post-processing");
        }
        vol.layer = layer;
        var v = vol.GetComponent<PostProcessVolume>();
        if (v == null) v = Undo.AddComponent<PostProcessVolume>(vol);
        v.isGlobal = true;
        v.sharedProfile = profile;
        // the world's reference camera (copied to every player's) and the editor cameras read it
        var res = AssetDatabase.LoadAssetAtPath<PostProcessResources>("Packages/com.unity.postprocessing/PostProcessing/PostProcessResources.asset");
        foreach (var cam in Cameras())
        {
            var l = cam.GetComponent<PostProcessLayer>();
            if (l == null) { l = Undo.AddComponent<PostProcessLayer>(cam.gameObject); l.Init(res); }
            l.volumeLayer |= 1 << layer;
            l.volumeTrigger = cam.transform;
            l.antialiasingMode = PostProcessLayer.Antialiasing.None;
            cam.allowHDR = true; // the shaders now hand over linear HDR
            EditorUtility.SetDirty(l);
        }
        SetShaderTonemap(false);
        Debug.Log("[Clearwater] Tone mapping in post-processing: " + ProfilePath + " (edit it for exposure, colour and bloom). " +
                  "Avatars are tone mapped too.");
    }

    /// <summary>The world's reference camera and the scene's editor preview camera.</summary>
    static IEnumerable<Camera> Cameras()
    {
        var desc = Object.FindObjectOfType<VRC.SDK3.Components.VRCSceneDescriptor>();
        if (desc != null && desc.ReferenceCamera != null)
        {
            var c = desc.ReferenceCamera.GetComponent<Camera>();
            if (c != null) yield return c;
        }
        foreach (var c in Object.FindObjectsOfType<Camera>(true))
            if (c.CompareTag("EditorOnly") && c.targetTexture == null) yield return c;
    }

    /// <summary>A layer named PostProcessing for the volume (made in the first free user layer if there is none).</summary>
    static int PostLayer()
    {
        int l = LayerMask.NameToLayer(LayerName);
        if (l >= 0) return l;
        for (l = 24; l < 32; l++)
            if (string.IsNullOrEmpty(LayerMask.LayerToName(l))) { ClearwaterSetup.NameLayer(l, LayerName); return l; }
        throw new System.Exception("No free layer for post-processing: name one \"" + LayerName + "\" in Tags and Layers.");
    }

    /// <summary>The Clearwater materials' Exposure (the tone curve's), 0.63 unless changed.</summary>
    static float ExposureNow()
    {
        foreach (var m in Materials()) if (m.HasProperty("_Exposure")) return m.GetFloat("_Exposure");
        return 0.63f;
    }

    // Alexa LogC (El 1000), the encoding Post Processing v2 looks its HDR LUTs up in (Colors.hlsl, the fast form)
    const float LogCA = 5.555556f, LogCB = 0.047996f, LogCC = 0.244161f, LogCD = 0.386036f;

    /// <summary>The shaders' own tone curve (ClearwaterCommon.cginc cwTonemap; keep the two in step).</summary>
    static Vector3 Tonemap(Vector3 c, float exposure)
    {
        c *= exposure;
        const float a = 2.51f, b = 0.03f, cc = 2.43f, d = 0.59f, e = 0.14f, Knee = 0.8f, White = 0.9f;
        for (int i = 0; i < 3; i++)
        {
            float x = c[i];
            if (x <= Knee) c[i] = (x * (a * x + b)) / (x * (cc * x + d) + e);
            else { float u = 0.3125f / (White - 0.7523f) * (x - Knee); c[i] = 0.7523f + (White - 0.7523f) * u / (1f + u); } // (the long tail to a soft white)
        }
        float lum = Vector3.Dot(c, new Vector3(0.2126f, 0.7152f, 0.0722f));
        c = Vector3.Lerp(new Vector3(lum, lum, lum), c, 0.90f);
        float t = Mathf.Clamp01(lum / 0.35f); t = t * t * (3f - 2f * t);
        return Vector3.Lerp(Vector3.Scale(c, new Vector3(0.96f, 1.0f, 1.05f)), c, t);
    }

    /// <summary>The shaders' tone curve as a 33^3 LUT over LogC-encoded HDR, for post-processing's External mode:
    /// the post pass then gives exactly the look the shaders do (its own ACES shapes highlights differently: whiter
    /// and wider round the sun, most of all under the water).</summary>
    public static Texture3D BakeLut(float exposure)
    {
        const int N = 33;
        var px = new Color[N * N * N];
        for (int z = 0; z < N; z++)
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    var lin = new Vector3(x, y, z) / (N - 1);
                    for (int i = 0; i < 3; i++)
                        lin[i] = Mathf.Max((Mathf.Pow(10f, (lin[i] - LogCD) / LogCC) - LogCB) / LogCA, 0f);
                    var o = Tonemap(lin, exposure);
                    px[x + y * N + z * N * N] = new Color(o.x, o.y, o.z, 1f);
                }
        var lut = AssetDatabase.LoadAssetAtPath<Texture3D>(LutPath);
        if (lut == null)
        {
            lut = new Texture3D(N, N, N, TextureFormat.RGBAHalf, false) { name = "ClearwaterToneLut" };
            AssetDatabase.CreateAsset(lut, LutPath);
        }
        lut.wrapMode = TextureWrapMode.Clamp;
        lut.filterMode = FilterMode.Bilinear;
        lut.anisoLevel = 0;
        lut.SetPixels(px);
        lut.Apply(false);
        EditorUtility.SetDirty(lut);
        AssetDatabase.SaveAssets();
        return lut;
    }

    /// <summary>The profile (kept if it exists, so its edits survive): the shaders' tone curve as an external LUT,
    /// light bloom.</summary>
    internal static PostProcessProfile Profile()
    {
        var p = AssetDatabase.LoadAssetAtPath<PostProcessProfile>(ProfilePath);
        if (p != null) return p;
        p = ScriptableObject.CreateInstance<PostProcessProfile>();
        AssetDatabase.CreateAsset(p, ProfilePath);
        var cg = p.AddSettings<ColorGrading>();
        cg.gradingMode.Override(GradingMode.External);
        cg.externalLut.Override(AssetDatabase.LoadAssetAtPath<Texture3D>(LutPath));
        cg.postExposure.Override(0f);
        var bl = p.AddSettings<Bloom>();
        bl.intensity.Override(BloomIntensity);
        bl.threshold.Override(BloomThreshold);
        bl.softKnee.Override(0.5f);
        foreach (var s in p.settings) { s.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy; AssetDatabase.AddObjectToAsset(s, p); }
        EditorUtility.SetDirty(p);
        AssetDatabase.SaveAssets();
        return p;
    }
#else
    [MenuItem(MenuPost, false, 41)]
    static void UsePost()
    {
        EditorUtility.DisplayDialog("Clearwater", "Tone mapping in post-processing needs Unity's Post Processing package " +
            "(com.unity.postprocessing), which VRChat world projects normally have.", "OK");
    }
#endif
}
