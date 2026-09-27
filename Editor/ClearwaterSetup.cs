using System.Collections.Generic;
using System.IO;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using VRC.SDK3.Components;

/// <summary>
/// Builds everything the Clearwater port needs: the initial ocean spectrum, the CustomRenderTexture chain
/// (spectrum -> 4-pass FFT, ripple sim -> ripple normals), the caustics grid/camera, materials and a ready
/// VRChat world scene. Re-running regenerates Assets/Clearwater/Generated and the scene.
/// </summary>
public static class ClearwaterSetup
{
    // what this world makes (materials, render textures, bakes, the scene) lives in the project; what every world
    // shares (shaders, scripts, sounds, textures) comes from the package
    internal const string Root = "Assets/Clearwater";
    internal const string Gen = Root + "/Generated";
    internal const string ScenePath = Root + "/Scenes/Clearwater.unity";
    internal const string Pkg = "Packages/com.vbamboo.clearwater/Runtime";

    // Ocean spectrum (same constants as the demo)
    const int N = 256;
    const float L = 4.6f;
    const float DEPTH = 1.6f;
    const double TARGET_SLOPE = 0.078;
    // Ripples: the demo's texel density (7 m / 256) over a larger window, so everyone nearby is inside it
    const int RN = 512;
    const float RSIZE = 14f;
    // Caustics
    const int C = 1024;
    const int G = 282;          // grid cells across the source range
    const float Margin = 0.05f; // source range [-Margin, 1+Margin] covers patterns bent in from the neighbours
    const int CausticsLayer = 23;
    const float CausticsOrtho = 7.77f;
    static readonly Vector3 CausticsRigPos = new Vector3(0, -20000, 0);
    // Sun: 31 deg up, 6 deg off the view axis, straight ahead of the spawn
    const float SunEl = 31f, SunAz = 6f;
    internal const float DefaultSeaSize = 5000f;   // side of the water plane (m); set per scene on the ClearwaterCoast
    internal const float FarClipPerSeaSize = 0.8f; // cameras see the plane's corners and the ground's far edge
    internal const float SoundBreakY = -100000f;    // a shore sound point this low separates two pieces of the line
    // The coast itself is a ClearwaterCoast in the scene (drawn there, baked by ClearwaterCoastBake); a new scene
    // gets a straight one: the waterline along x at this z, the sea towards +z, the "gentle beach" section.
    const float DefaultWaterlineZ = -35.9f;
    const float SeabedInner = 20f, SeabedStep = 0.5f;        // rendered ground: 50 cm cells out to 20 m (its shading is
                                                             // per pixel: 25 cm cost ~0.2 ms/eye for no visible change),
    const float SeabedFarPerSeaSize = 0.6f, SeabedGrowth = 1.08f; // then 8% larger each step out to the horizon
    static readonly Vector3 SpawnPos = new Vector3(0, 0.8f, -41f); // on the beach, facing the water and the sun

    // Rebuilds everything in Generated/. The assets are updated in place (same files, same IDs), so an existing scene
    // keeps pointing at them and is otherwise left alone: things placed or tuned by hand, and the uploaded world's
    // ID, stay. Only a missing scene is created.
    [MenuItem("Tools/Clearwater/Build Scene")]
    public static void BuildScene()
    {
        var a = BuildAssets();
        if (File.Exists(ScenePath)) UpdateScene(a);
        else CreateScene(a);
    }

    /// <summary>The scene's coast (a straight default one is added if there is none), baked and saved.</summary>
    static void BakeSceneCoast()
    {
        var coast = Object.FindObjectOfType<ClearwaterCoast>();
        if (coast == null) coast = CreateDefaultCoast();
        ClearwaterCoastBake.Bake(coast);
        var scene = coast.gameObject.scene;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    static ClearwaterCoast CreateDefaultCoast()
    {
        var go = new GameObject("Coast (editor only)") { tag = "EditorOnly" };
        go.transform.position = new Vector3(0, 0, DefaultWaterlineZ);
        return go.AddComponent<ClearwaterCoast>();
    }

    [MenuItem("Tools/Clearwater/Recreate Scene (discards changes to the scene)")]
    public static void RecreateScene()
    {
        if (File.Exists(ScenePath) && !EditorUtility.DisplayDialog("Recreate the Clearwater scene",
                "The scene is built again from scratch: anything placed or changed in it by hand is lost " +
                "(the uploaded world's ID is kept).", "Recreate", "Cancel"))
            return;
        CreateScene(BuildAssets());
    }

    // keep the scene; refresh only the values derived from the rebuilt assets
    static void UpdateScene(Assets a)
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            scene = EditorSceneManager.OpenScene(ScenePath);
        }
        var ctl = Object.FindObjectOfType<ClearwaterController>();
        if (ctl != null)
        {
            ctl.swashLoop = a.water.GetFloat("_SwashLoop");    // follows the wave audio
            EditorUtility.SetDirty(ctl);
        }
        BakeSceneCoast();
    }

    public class Assets
    {
        public Material water, sky, caustics, ripple, seabed, underwater, avatarCaustics;
        public RenderTexture causRT;
        public Mesh grid, plane, seabedGrid;
    }

    // ---------------------------------------------------------------- assets

    public static Assets BuildAssets()
    {
        // assets are updated in place (same GUIDs), so rebuilding with unchanged settings leaves git clean
        if (!AssetDatabase.IsValidFolder(Root)) AssetDatabase.CreateFolder("Assets", "Clearwater");
        if (!AssetDatabase.IsValidFolder(Gen)) AssetDatabase.CreateFolder(Root, "Generated");
        ClearwaterBedLooks.SetupImporters();
        var a = new Assets();

        // spectrum + FFT chain
        var h0 = BuildH0();
        h0 = Save(h0, "H0.asset");
        var specMat = Mat("Clearwater/CRT/Spectrum", "CRT_Spectrum");
        specMat.SetTexture("_H0", h0);
        specMat.SetFloat("_PatchSize", L);
        var spec = CRT("CRT_Spectrum", N, RenderTextureFormat.ARGBFloat, specMat, FilterMode.Point, TextureWrapMode.Repeat);

        Texture src = spec;
        string[] names = { "CRT_FFT_X0", "CRT_FFT_X1", "CRT_FFT_Y0", "CRT_FFT_Y1_Surface" };
        CustomRenderTexture surf = null;
        for (int p = 0; p < 4; p++)
        {
            var m = Mat("Clearwater/CRT/FFT", names[p]);
            m.SetTexture("_Src", src);
            m.SetFloat("_Axis", p / 2);
            m.SetFloat("_Stage", p % 2);
            bool last = p == 3;
            m.SetFloat("_Resolve", last ? 1 : 0);
            var crt = last
                ? CRT(names[p], N, RenderTextureFormat.ARGBHalf, m, FilterMode.Trilinear, TextureWrapMode.Repeat, mips: true, aniso: 8)
                : CRT(names[p], N, RenderTextureFormat.ARGBFloat, m, FilterMode.Point, TextureWrapMode.Repeat);
            src = crt;
            surf = crt;
        }

        // ripples
        a.ripple = Mat("Clearwater/CRT/Ripple", "CRT_Ripple");
        var rip = CRT("CRT_Ripple", RN, RenderTextureFormat.ARGBHalf, a.ripple, FilterMode.Bilinear, TextureWrapMode.Clamp, doubleBuffered: true);
        var ripNMat = Mat("Clearwater/CRT/RippleNormals", "CRT_RippleNormals");
        ripNMat.SetTexture("_Src", rip);
        ripNMat.SetFloat("_RipSize", RSIZE);
        var ripN = CRT("CRT_RippleNormals", RN, RenderTextureFormat.ARGBHalf, ripNMat, FilterMode.Bilinear, TextureWrapMode.Clamp);

        // caustics
        a.causRT = new RenderTexture(C, C, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
        {
            name = "RT_Caustics", useMipMap = true, autoGenerateMips = true,
            wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8
        };
        a.causRT = Save(a.causRT, "RT_Caustics.renderTexture");
        a.caustics = Mat("Clearwater/Caustics", "Caustics");
        a.caustics.SetTexture("_Surf", surf);
        a.caustics.SetFloat("_PatchSize", L);
        a.caustics.SetFloat("_Depth", DEPTH);
        a.caustics.SetFloat("_CausRes", C);
        a.caustics.SetFloat("_CamOrthoSize", CausticsOrtho);
        a.caustics.SetVector("_SunDir", SunVector());
        a.grid = BuildGrid();
        a.grid = Save(a.grid, "CausticsGrid.asset");

        // water + sky
        a.water = Mat("Clearwater/Water", "Water");
        a.water.SetTexture("_Surf", surf);
        a.water.SetTexture("_Caus", a.causRT);
        a.water.SetTexture("_Rip", ripN);
        a.water.SetTexture("_Peb", AssetDatabase.LoadAssetAtPath<Texture2D>(Pkg + "/Textures/Pebbles.jpg"));
        a.water.SetFloat("_PatchSize", L);
        a.water.SetFloat("_Depth", DEPTH);
        a.water.SetFloat("_RipSize", RSIZE);
        a.water.SetVector("_SunDir", SunVector());
        if (IsNew(a.water)) a.water.EnableKeyword("_CW_TONEMAP");
        a.sky = Mat("Clearwater/Skybox", "Sky");
        a.sky.SetVector("_SunDir", SunVector());
        if (IsNew(a.sky)) a.sky.EnableKeyword("_CW_TONEMAP");
        float seaSize = SceneSeaSize();
        a.plane = Save(BuildPlane(seaSize), "WaterPlane.asset");
        a.water.SetFloat("_SeaHalfSize", seaSize * 0.5f);

        // seabed / beach and the underwater view
        a.seabed = Mat("Clearwater/Seabed", "Seabed");
        a.seabed.SetFloat("_SeaHalfSize", seaSize * 0.5f);
        a.seabed.SetTexture("_Caus", a.causRT);
        a.seabed.SetTexture("_Rip", ripN);
        a.seabed.SetTexture("_Peb", a.water.GetTexture("_Peb"));
        // (the bed look: the default until a coast with its own is baked)
        if (ClearwaterBedLooks.Default != null)
        {
            if (IsNew(a.water)) ClearwaterBedLooks.Default.ApplyTo(a.water);
            if (IsNew(a.seabed)) ClearwaterBedLooks.Default.ApplyTo(a.seabed);
        }
        a.seabed.SetFloat("_PatchSize", L);
        a.seabed.SetFloat("_Depth", DEPTH);
        a.seabed.SetFloat("_RipSize", RSIZE);
        a.seabed.SetVector("_SunDir", SunVector());
        a.seabed.SetVector("_WaterOrigin", Vector4.zero);
        if (IsNew(a.seabed)) a.seabed.EnableKeyword("_CW_TONEMAP");
        a.underwater = Mat("Clearwater/Underwater", "Underwater");
        a.underwater.SetVector("_SunDir", SunVector());
        a.underwater.SetFloat("_Depth", DEPTH);
        if (IsNew(a.underwater)) a.underwater.EnableKeyword("_CW_TONEMAP");
        // the surface's height, for a camera at the waterline (ClearwaterSurface.cginc)
        foreach (var m in new[] { a.seabed, a.underwater }) m.SetTexture("_Surf", surf);
        a.underwater.SetTexture("_Rip", ripN);
        a.underwater.SetFloat("_PatchSize", L);
        a.underwater.SetFloat("_RipSize", RSIZE);
        // caustics projected on avatars in the water
        a.avatarCaustics = Mat("Clearwater/AvatarCaustics", "AvatarCaustics");
        a.avatarCaustics.SetTexture("_Caus", a.causRT);
        a.avatarCaustics.SetFloat("_PatchSize", L);
        a.avatarCaustics.SetFloat("_Depth", DEPTH);
        a.avatarCaustics.SetVector("_SunDir", SunVector());
        a.avatarCaustics.SetVector("_WaterOrigin", Vector4.zero);
        var (track, loop) = BuildSwashTrack();
        track = Save(track, "SwashTrack.asset");
        var (breaks, idx, count) = BuildSwashBreaks();
        breaks = Save(breaks, "SwashBreaks.asset");
        idx = Save(idx, "SwashIdx.asset");
        foreach (var m in new[] { a.water, a.seabed, a.underwater })
        {
            m.SetTexture("_SwashTrack", track);
            m.SetFloat("_SwashLoop", loop);
            m.SetTexture("_SwashBreaks", breaks);
            m.SetTexture("_SwashIdx", idx);
            m.SetFloat("_SwashCount", count);
        }
        // (the coast textures, the walkable ground and the shore sound's path come from the scene's ClearwaterCoast:
        // BakeSceneCoast, after the scene exists)
        Texture2D rocks = null;
        if (Rocks) rocks = Save(BakeRocks(), "RockHeight.asset");
        else AssetDatabase.DeleteAsset(Gen + "/RockHeight.asset");
        foreach (var m in new[] { a.water, a.seabed, a.avatarCaustics, a.underwater })
        {
            m.SetTexture("_RockTex", rocks); // none = the shaders' default black: no rock anywhere
            m.SetVector("_RockArea", new Vector4(0, 0, RockArea, 0));
        }
        a.seabedGrid = Save(BuildFarGrid("SeabedGrid", seaSize * SeabedFarPerSeaSize), "SeabedGrid.asset");

        _made.Clear();
        AssetDatabase.SaveAssets();
        return a;
    }

    internal static Vector3 SunVector()
    {
        float el = SunEl * Mathf.Deg2Rad, az = SunAz * Mathf.Deg2Rad;
        // the demo's (sin az cos el, sin el, -cos az cos el), with z flipped into Unity space
        return new Vector3(Mathf.Sin(az) * Mathf.Cos(el), Mathf.Sin(el), Mathf.Cos(az) * Mathf.Cos(el));
    }

    // Materials made (not found) by this build: only they get their starting settings (the tone mapping keyword, the
    // bed look); one that exists keeps what was tuned on it (the waves, exposure, clouds, the tone mapping mode...),
    // and the build only writes again the references and constants it owns.
    static readonly HashSet<Material> _made = new HashSet<Material>();
    static bool IsNew(Material m) => _made.Contains(m);

    static Material Mat(string shader, string name)
    {
        var s = Shader.Find(shader);
        if (s == null) throw new System.Exception("Shader not found: " + shader);
        var existing = AssetDatabase.LoadAssetAtPath<Material>(Gen + "/" + name + ".mat");
        if (existing != null)
        {
            if (existing.shader != s) existing.shader = s;
            EditorUtility.SetDirty(existing);
            return existing;
        }
        var m = Save(new Material(s) { name = name }, name + ".mat");
        _made.Add(m);
        return m;
    }

    static CustomRenderTexture CRT(string name, int size, RenderTextureFormat fmt, Material mat, FilterMode filter, TextureWrapMode wrap,
        bool mips = false, int aniso = 0, bool doubleBuffered = false)
    {
        var crt = new CustomRenderTexture(size, size, fmt, RenderTextureReadWrite.Linear)
        {
            name = name,
            material = mat,
            initializationMode = CustomRenderTextureUpdateMode.OnLoad,
            initializationSource = CustomRenderTextureInitializationSource.TextureAndColor,
            initializationColor = Color.clear,
            updateMode = CustomRenderTextureUpdateMode.Realtime,
            doubleBuffered = doubleBuffered,
            filterMode = filter,
            wrapMode = wrap,
            useMipMap = mips,
            autoGenerateMips = mips,
            anisoLevel = aniso,
        };
        return Save(crt, name + ".asset");
    }

    static string Dir(string path) => System.IO.Path.GetDirectoryName(path).Replace('\\', '/');

    /// <summary>Stores o at Generated/file. If that asset already exists its content is overwritten in place,
    /// keeping its GUID (and every reference to it); returns the object that now lives in the asset.</summary>
    internal static T Save<T>(T o, string file) where T : Object
    {
        string path = Gen + "/" + file;
        o.name = System.IO.Path.GetFileNameWithoutExtension(file); // (Unity warns when an asset's name is not its file's)
        var existing = AssetDatabase.LoadAssetAtPath<T>(path);
        if (existing == null || existing.GetType() != o.GetType())
        {
            // (a file in a sub-folder, a pool's: the folders made as needed)
            var missing = new System.Collections.Generic.Stack<string>();
            for (string d = Dir(path); !AssetDatabase.IsValidFolder(d); d = Dir(d)) missing.Push(d);
            while (missing.Count > 0) { string d = missing.Pop(); AssetDatabase.CreateFolder(Dir(d), System.IO.Path.GetFileName(d)); }
            if (AssetDatabase.LoadMainAssetAtPath(path) != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(o, path);
            return o;
        }
        if (existing is RenderTexture ert) ert.Release(); // size/format changes apply on next use
        if (existing is Mesh em && o is Mesh om) CopyMesh(om, em);
        else EditorUtility.CopySerialized(o, existing);
        EditorUtility.SetDirty(existing);
        Object.DestroyImmediate(o);
        return existing;
    }

    // A mesh is rewritten through the Mesh API: copied serialized over one with another layout, the grids drew
    // nothing (their data read back right, but not what the GPU was given).
    static void CopyMesh(Mesh from, Mesh to)
    {
        to.Clear();
        to.indexFormat = from.indexFormat;
        to.vertices = from.vertices;
        if (from.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Normal)) to.normals = from.normals;
        if (from.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Tangent)) to.tangents = from.tangents;
        if (from.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Color)) to.colors = from.colors;
        var uv = new List<Vector4>();
        for (int c = 0; c < 4; c++)
            if (from.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0 + c)) { from.GetUVs(c, uv); to.SetUVs(c, uv); }
        to.subMeshCount = from.subMeshCount;
        for (int s = 0; s < from.subMeshCount; s++) to.SetIndices(from.GetIndices(s), from.GetTopology(s), s, false);
        to.bounds = from.bounds;
    }

    // ---- initial spectrum: a port of the demo's buildH0(), same seed, so the waves are the demo's waves
    class Mulberry32
    {
        uint a;
        public Mulberry32(uint seed) { a = seed; }
        public double Next()
        {
            unchecked
            {
                a += 0x6D2B79F5;
                uint t = (a ^ (a >> 15)) * (1u | a);
                t = (t + (t ^ (t >> 7)) * (61u | t)) ^ t;
                return (t ^ (t >> 14)) / 4294967296.0;
            }
        }
    }

    static Texture2D BuildH0()
    {
        var rnd = new Mulberry32(7);
        double Gauss()
        {
            double u = 0, v;
            while (u == 0) u = rnd.Next();
            v = rnd.Next();
            return System.Math.Sqrt(-2 * System.Math.Log(u)) * System.Math.Cos(2 * System.Math.PI * v);
        }
        double kp = 2 * System.Math.PI / 0.62, kcut = 2 * System.Math.PI / 0.045;
        double[] wd = { 0.8, 0.6 };
        var re = new double[N * N];
        var im = new double[N * N];
        double s2 = 0;
        for (int m = 0; m < N; m++)
            for (int n = 0; n < N; n++)
            {
                int nx = n < N / 2 ? n : n - N, nz = m < N / 2 ? m : m - N;
                double kx = 2 * System.Math.PI * nx / L, kz = 2 * System.Math.PI * nz / L, k = System.Math.Sqrt(kx * kx + kz * kz);
                double P = 0;
                if (k > 1e-6)
                {
                    double lk = System.Math.Log(k / kp);
                    double bump = System.Math.Exp(-0.5 * (lk / 0.36) * (lk / 0.36));
                    double tail = 0.035 * System.Math.Exp(-(kp / k) * (kp / k)) * System.Math.Exp(-(k / kcut) * (k / kcut));
                    double ls = System.Math.Log(k / (2 * System.Math.PI / 1.6)) / 0.3;
                    double swell = 0.35 * System.Math.Exp(-0.5 * ls * ls);
                    double c = (kx * wd[0] + kz * wd[1]) / k;
                    double spread = (0.3 + 0.7 * c * c) * (c < 0 ? 0.35 : 1);
                    P = (bump + tail + swell) * spread / (k * k * k * k);
                }
                double amp = System.Math.Sqrt(P / 2);
                int i = m * N + n;
                re[i] = Gauss() * amp;
                im[i] = Gauss() * amp;
                s2 += 2 * k * k * (re[i] * re[i] + im[i] * im[i]);
            }
        double sc = TARGET_SLOPE / System.Math.Sqrt(s2);
        var data = new Color[N * N];
        for (int m = 0; m < N; m++)
            for (int n = 0; n < N; n++)
            {
                int i = m * N + n, j = ((N - m) % N) * N + ((N - n) % N);
                data[i] = new Color((float)(re[i] * sc), (float)(im[i] * sc), (float)(re[j] * sc), (float)(-im[j] * sc));
            }
        var tex = new Texture2D(N, N, TextureFormat.RGBAFloat, false, true)
        {
            name = "H0", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat
        };
        tex.SetPixels(data);
        tex.Apply(false, false);
        return tex;
    }

    static Mesh BuildGrid()
    {
        int n = G + 1;
        var v = new Vector3[n * n];
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
                v[j * n + i] = new Vector3(-Margin + (1 + 2 * Margin) * i / G, -Margin + (1 + 2 * Margin) * j / G, 0);
        var idx = new int[G * G * 6];
        int o = 0;
        for (int j = 0; j < G; j++)
            for (int i = 0; i < G; i++)
            {
                int a = j * n + i, b = a + 1, c = a + n, d = c + 1;
                idx[o++] = a; idx[o++] = b; idx[o++] = c; idx[o++] = b; idx[o++] = d; idx[o++] = c;
            }
        var mesh = new Mesh { name = "CausticsGrid", indexFormat = IndexFormat.UInt32 };
        mesh.vertices = v;
        mesh.triangles = idx;
        mesh.bounds = new Bounds(new Vector3(0.5f, 0.5f, 0), new Vector3(2, 2, 1));
        return mesh;
    }

    // ---- rocks: baked on the GPU from the shader's own cwRockAnalytic, then shared by every shader and the collider
    const float AvatarCausticsHalfSize = 16f; // avatars within this many metres of the local player get caustics
    const float AvatarCausticsAbove = 3f;     // projector height above the surface (its box reaches 8 m below)
    static readonly bool Rocks = false; // rocky areas off for now (the user preferred the open beach); true brings them back
    const int RockRes = 2048;
    const float RockArea = 204.8f; // metres, centred on the water origin: 10 cm per texel over the walkable area
    static Texture2D BakeRocks()
    {
        var mat = new Material(Shader.Find("Hidden/Clearwater/RockBake"));
        mat.SetVector("_RockArea", new Vector4(0, 0, RockArea, 0));
        var px = ClearwaterCoastBake.BlitRead(mat, RockRes, RockRes);
        Object.DestroyImmediate(mat);
        var tex = new Texture2D(RockRes, RockRes, TextureFormat.RHalf, true, true) // mips: the shaders read cavities from them
        {
            name = "RockHeight", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
        };
        tex.SetPixels(px); tex.Apply(true, false);
        return tex;
    }

    /// <summary>Grid for the rendered ground: uniform cells near the centre, growing geometrically outward. The
    /// seabed shader moves it with the viewer (snapped to the inner cell size) and computes every height.</summary>
    /// <summary>The sea size set on the open scene's coast (the default when it has none).</summary>
    static float SceneSeaSize()
    {
        var coast = Object.FindObjectOfType<ClearwaterCoast>();
        return coast != null ? Mathf.Max(coast.seaSize, 100f) : DefaultSeaSize;
    }

    /// <summary>Resizes the sea to size (m): the water plane, the rendered ground's reach, the water's edge fade and
    /// the underwater box. Meshes are rewritten in place only when they change.</summary>
    internal static void ApplySeaSize(ClearwaterController ctl, float size)
    {
        size = Mathf.Max(size, 100f);
        var plane = AssetDatabase.LoadAssetAtPath<Mesh>(Gen + "/WaterPlane.asset");
        if (plane == null || !Mathf.Approximately(plane.bounds.extents.x, size * 0.5f) || plane.vertexCount < 100) // (or still the old single quad)
            Save(BuildPlane(size), "WaterPlane.asset");
        float far = size * SeabedFarPerSeaSize;
        var grid = AssetDatabase.LoadAssetAtPath<Mesh>(Gen + "/SeabedGrid.asset");
        if (grid == null || !Mathf.Approximately(grid.bounds.extents.x, 2 * far))
            Save(BuildFarGrid("SeabedGrid", far), "SeabedGrid.asset");
        foreach (var m in new[] { ctl.waterMaterial, ctl.seabedMaterial })
        {
            if (m == null) continue;
            m.SetFloat("_SeaHalfSize", size * 0.5f);
            EditorUtility.SetDirty(m);
        }
        if (ctl.underwaterVolume != null)
        {
            var t = ctl.underwaterVolume.transform;
            var scale = UnderwaterScale(size);
            if (t.localScale != scale) { Undo.RecordObject(t, "Sea size"); t.localScale = scale; }
        }
    }

    // The underwater box is drawn around each camera (see Underwater.shader); the object only has to be big enough
    // for its bounds to hold any camera in the sea, so it is never culled.
    static Vector3 UnderwaterScale(float size) => new Vector3(size, 1000f, size);

    static Mesh BuildFarGrid(string name, float seabedFar)
    {
        var mesh = BuildRingGrid(name, SeabedStep, SeabedInner, SeabedGrowth, seabedFar, false, false);
        // never culled: it moves with the viewer and its heights are set on the GPU
        mesh.bounds = new Bounds(Vector3.zero, new Vector3(4 * seabedFar, 40, 4 * seabedFar));
        return mesh;
    }

    const float WaterInner = 120f, WaterStep = 2f, WaterGrowth = 1.08f; // plane: 2 m cells out to 120 m, then 8% larger each

    /// <summary>The water plane: a grid, 2 m cells over the walkable area growing outward to the sea's edge. (Its shading
    /// is per pixel, but each pixel's view ray comes from the position interpolated across its triangle: over one
    /// 5 km quad that interpolation lost precision far from the vertices, and on some GPUs, Radeon notably, the water
    /// shimmered.)</summary>
    static Mesh BuildPlane(float size)
    {
        var mesh = BuildRingGrid("WaterPlane", WaterStep, WaterInner, WaterGrowth, size * 0.5f, true, true);
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>A flat grid, finest round its centre: cells of step out to inner (a square on the step's lattice), then
    /// square rings, each growth times further from the last than that one from the one before, out to far (ending
    /// exactly there when clampToFar). The rings keep fewer points round them as they widen - half as many, where
    /// that leaves their cells no more than 1.25 times as long round the ring as they are deep - joined with no gaps,
    /// so the cells stay near square all the way out. (It was one axis of steps crossed with itself: its fine steps
    /// ran on to the horizon in a cross of long thin cells, near half of all the points.)</summary>
    static Mesh BuildRingGrid(string name, float step, float inner, float growth, float far, bool clampToFar, bool normals)
    {
        var v = new List<Vector3>();
        var idx = new List<int>();
        void Tri(int a, int b, int c) // (wound to face up, whatever the order given)
        {
            if (Vector3.Cross(v[b] - v[a], v[c] - v[a]).y > 0f) { idx.Add(a); idx.Add(b); idx.Add(c); }
            else { idx.Add(a); idx.Add(c); idx.Add(b); }
        }
        // the inner square
        int m = Mathf.Max(1, Mathf.FloorToInt(Mathf.Min(inner, far) / step + 1e-3f)), w = 2 * m + 1;
        for (int j = -m; j <= m; j++)
            for (int i = -m; i <= m; i++)
                v.Add(new Vector3(i * step, 0, j * step));
        int At(int i, int j) => (j + m) * w + (i + m);
        for (int j = -m; j < m; j++)
            for (int i = -m; i < m; i++)
            {
                Tri(At(i, j), At(i, j + 1), At(i + 1, j));
                Tri(At(i + 1, j), At(i, j + 1), At(i + 1, j + 1));
            }
        // its edge, round from the corner at (-, -): n segments a side
        int n = 2 * m;
        var ring = new List<int>();
        for (int k = 0; k < n; k++) ring.Add(At(-m + k, -m));
        for (int k = 0; k < n; k++) ring.Add(At(m, -m + k));
        for (int k = 0; k < n; k++) ring.Add(At(m - k, m));
        for (int k = 0; k < n; k++) ring.Add(At(-m, m - k));
        // the rings
        float r = m * step, s = step;
        while (r < far - 1e-3f)
        {
            s *= growth;
            float rNext = clampToFar ? Mathf.Min(r + s, far) : r + s;
            int nNext = n;
            if (n % 2 == 0 && n / 2 >= 8 && 2f * rNext / (n / 2) <= 1.25f * (rNext - r)) nNext = n / 2;
            var next = new List<int>();
            for (int side = 0; side < 4; side++)
                for (int k = 0; k < nNext; k++)
                {
                    float t = -rNext + 2f * rNext * k / nNext;
                    Vector3 p = side == 0 ? new Vector3(t, 0, -rNext) : side == 1 ? new Vector3(rNext, 0, t)
                              : side == 2 ? new Vector3(-t, 0, rNext) : new Vector3(-rNext, 0, -t);
                    next.Add(v.Count);
                    v.Add(p);
                }
            int cnt = ring.Count, cntN = next.Count;
            if (nNext == n)
                for (int k = 0; k < cnt; k++)
                {
                    int a = ring[k], b = ring[(k + 1) % cnt], c = next[k], d = next[(k + 1) % cntN];
                    Tri(a, c, b); Tri(b, c, d);
                }
            else // (two segments of this ring to each of the next: a fan, no T-junctions)
                for (int k = 0; k < cntN; k++)
                {
                    int p0 = ring[2 * k], p1 = ring[2 * k + 1], p2 = ring[(2 * k + 2) % cnt], c0 = next[k], c1 = next[(k + 1) % cntN];
                    Tri(p0, p1, c0); Tri(p1, p2, c1); Tri(p1, c1, c0);
                }
            ring = next; n = nNext; r = rNext;
        }
        var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
        mesh.SetVertices(v);
        if (normals) { var nr = new Vector3[v.Count]; for (int k = 0; k < nr.Length; k++) nr[k] = Vector3.up; mesh.normals = nr; }
        mesh.SetTriangles(idx, 0);
        return mesh;
    }

    // ---------------------------------------------------------------- scene

    public static void CreateScene(Assets a) => CreateScene(a, ScenePath, true);

    /// <summary>A new Clearwater scene at path (sun, sky, the water rig, a straight coast, a VRChat world and spawn),
    /// baked and saved. The Clearwater scene (main) also keeps its uploaded world ID and becomes the build's only
    /// scene; another one (a demo scene) leaves the build settings alone.</summary>
    internal static void CreateScene(Assets a, string path, bool main)
    {
        // keep the uploaded world's ID across rebuilds, or the next upload would create a second world
        string worldId = null;
        if (main && File.Exists(path))
        {
            // a plain field, or (the VRCWorld prefab's case) an override: "propertyPath: blueprintId" + "value: wrld_..."
            var m = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(path), @"blueprintId\s*(?:\r?\n\s*value)?:\s*(wrld_[0-9a-fA-F-]+)");
            if (m.Success) worldId = m.Groups[1].Value;
        }
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // sun + environment: the sky of the time of day on the sun
        var sun = CreateSun("Sun");
        RenderSettings.skybox = a.sky;
        RenderSettings.sun = sun;
        RenderSettings.ambientMode = AmbientMode.Skybox;
        RenderSettings.fog = false;

        CreateRig(a, sun);
        ClearwaterSkySetup.Install(sun);

        // VRChat world descriptor, spawn facing +z (toward the sun), reference camera
        var refGo = new GameObject("Reference Camera");
        refGo.transform.position = SpawnPos + Vector3.up * 1.5f;
        var refCam = refGo.AddComponent<Camera>();
        refCam.enabled = false;
        refCam.nearClipPlane = 0.03f;
        refCam.farClipPlane = DefaultSeaSize * FarClipPerSeaSize;
        refCam.cullingMask = ~(1 << CausticsLayer);
        refCam.allowHDR = true;
        refCam.clearFlags = CameraClearFlags.Skybox;

        var worldPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.vrchat.worlds/Samples/UdonExampleScene/Prefabs/VRCWorld.prefab");
        var world = (GameObject)PrefabUtility.InstantiatePrefab(worldPrefab);
        world.transform.position = SpawnPos;
        world.transform.rotation = Quaternion.identity;
        var desc = world.GetComponent<VRCSceneDescriptor>();
        desc.ReferenceCamera = refGo;
        desc.RespawnHeightY = -50;
        desc.spawns = new[] { world.transform };
        if (worldId != null) world.GetComponent<VRC.Core.PipelineManager>().blueprintId = worldId;

        // editor-only preview camera with the demo's framing (1.55 m up, 41 deg down, 64 deg vertical FOV)
        var prevGo = new GameObject("Preview Camera (editor only)") { tag = "EditorOnly" };
        prevGo.transform.position = new Vector3(0, 1.55f, 0);
        prevGo.transform.rotation = Quaternion.Euler(0.72f * Mathf.Rad2Deg, 0, 0);
        var prev = prevGo.AddComponent<Camera>();
        prev.fieldOfView = 64;
        prev.nearClipPlane = 0.03f;
        prev.farClipPlane = DefaultSeaSize * FarClipPerSeaSize;
        prev.cullingMask = ~(1 << CausticsLayer);
        prev.depth = -1;

        Directory.CreateDirectory(Path.GetDirectoryName(path));
        EditorSceneManager.SaveScene(scene, path);
        if (main) EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(path, true) };
        BakeSceneCoast(); // the coast, the walkable ground and the shore sound's path
        Debug.Log("[Clearwater] scene built: " + path);
    }

    /// <summary>The water surfaces' sorting order: drawn before every other see-through thing (it comes before the
    /// render queue), and with no depth written (the shader's _CWZWrite), so an avatar's see-through clothes are
    /// drawn over it where they are under the water, whatever their queue and wherever the camera is, instead of
    /// being hidden by it (or by it only from some places: the sea is one large plane sorted by its centre).</summary>
    internal const int WaterSortingOrder = -1;

    /// <summary>The water and everything that drives it: the surface, the seabed, the underwater fog, the caustics
    /// on avatars, the caustics rig, the wave sound and the controller. Returns the water object.</summary>
    static GameObject CreateRig(Assets a, Light sun)
    {
        NameLayer(CausticsLayer, "Caustics");
        // water surface + something to stand on (the demo's viewpoint is ~1.55 m above the water)
        var water = new GameObject("Clearwater");
        water.AddComponent<MeshFilter>().sharedMesh = a.plane;
        var mr = water.AddComponent<MeshRenderer>();
        mr.sharedMaterial = a.water;
        mr.sortingOrder = WaterSortingOrder;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = LightProbeUsage.Off;
        mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        GameObjectUtility.SetStaticEditorFlags(water, 0);

        var seabed = new GameObject("Seabed");
        seabed.transform.SetParent(water.transform, false);
        seabed.AddComponent<MeshFilter>().sharedMesh = a.seabedGrid;
        var smr = seabed.AddComponent<MeshRenderer>();
        smr.sharedMaterial = a.seabed;
        smr.shadowCastingMode = ShadowCastingMode.On; // puts it in the camera depth texture
        smr.receiveShadows = true; // (the sun's shadows on the dry beach)
        smr.lightProbeUsage = LightProbeUsage.Off;
        smr.reflectionProbeUsage = ReflectionProbeUsage.Off;

        var under = new GameObject("Underwater Volume");
        under.transform.SetParent(water.transform, false);
        under.transform.localScale = UnderwaterScale(DefaultSeaSize);
        under.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        var umr = under.AddComponent<MeshRenderer>();
        umr.sharedMaterial = a.underwater;
        umr.shadowCastingMode = ShadowCastingMode.Off;
        umr.receiveShadows = false;
        umr.lightProbeUsage = LightProbeUsage.Off;
        umr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        umr.enabled = false;

        // caustics on avatars: a Projector looking straight down that only sees the player layers; the controller
        // keeps it over the local player (the shader places the pattern from world positions, so only its box matters)
        var projGo = new GameObject("Avatar Caustics Projector");
        projGo.transform.SetParent(water.transform, false);
        projGo.transform.localPosition = new Vector3(0, AvatarCausticsAbove, 0);
        projGo.transform.localRotation = Quaternion.Euler(90, 0, 0);
        var proj = projGo.AddComponent<Projector>();
        proj.orthographic = true;
        proj.orthographicSize = AvatarCausticsHalfSize;
        proj.aspectRatio = 1;
        proj.nearClipPlane = 0.1f;
        proj.farClipPlane = AvatarCausticsAbove + 8f;
        proj.material = a.avatarCaustics;
        proj.ignoreLayers = ~((1 << 9) | (1 << 10) | (1 << 18) | (1 << ClearwaterCoastBake.PropsLayer)); // Player, PlayerLocal, MirrorReflection, ClearwaterProps

        // caustics rig, far below the world so no player camera ever sees it
        var rig = new GameObject("Caustics Rig");
        rig.transform.position = CausticsRigPos;
        var gridGo = new GameObject("Caustics Grid");
        gridGo.layer = CausticsLayer;
        gridGo.transform.SetParent(rig.transform, false);
        gridGo.AddComponent<MeshFilter>().sharedMesh = a.grid;
        var gmr = gridGo.AddComponent<MeshRenderer>();
        gmr.sharedMaterial = a.caustics;
        gmr.shadowCastingMode = ShadowCastingMode.Off;
        gmr.receiveShadows = false;
        gmr.lightProbeUsage = LightProbeUsage.Off;
        gmr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        var camGo = new GameObject("Caustics Camera");
        camGo.transform.SetParent(rig.transform, false);
        camGo.transform.localPosition = new Vector3(0.5f, 0.5f, -1f);
        var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = CausticsOrtho;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 5f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0, 0, 0, 0);
        cam.cullingMask = 1 << CausticsLayer;
        cam.targetTexture = a.causRT;
        cam.depth = -100;
        cam.allowHDR = false;
        cam.allowMSAA = false;
        cam.useOcclusionCulling = false;

        // wave ambience
        var audio = BuildAudio(water.transform);

        // controller
        var ctlGo = new GameObject("Clearwater Controller");
        EnsureProgramAsset(Pkg + "/Udon/ClearwaterController.cs");
        var ctl = UdonSharpUndo.AddComponent<ClearwaterController>(ctlGo);
        ctl.water = water.transform;
        ctl.sun = sun;
        ctl.waterMaterial = a.water;
        ctl.rippleMaterial = a.ripple;
        ctl.causticsMaterial = a.caustics;
        ctl.skyMaterial = a.sky;
        ctl.seabedMaterial = a.seabed;
        ctl.underwaterMaterial = a.underwater;
        ctl.underwaterVolume = umr;
        ctl.avatarCausticsMaterial = a.avatarCaustics;
        ctl.avatarCausticsProjector = projGo.transform;
        ctl.shoreAudio = audio.shore;
        ctl.bedAudio = audio.bed;
        ctl.underwaterAudio = audio.under;
        ctl.swashLoop = a.water.GetFloat("_SwashLoop");
        ctl.rippleResolution = RN;
        ctl.rippleSize = RSIZE;
        EditorUtility.SetDirty(ctl);

        return water;
    }

    /// <summary>The fixed sky's sunlight (the sky of the time of day keeps it with the sun 31 degrees up).</summary>
    internal static readonly Color SunColor = new Color(1.0f, 0.93f, 0.82f);
    internal const float SunIntensity = 1.3f;

    /// <summary>The Clearwater sun: warm white, 31 deg up, straight ahead of the default spawn, soft shadows (the water
    /// reads the camera depth texture, which VRChat only renders while the directional light casts shadows).</summary>
    static Light CreateSun(string name)
    {
        var sunGo = new GameObject(name);
        var sun = sunGo.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = SunColor;
        sun.intensity = SunIntensity;
        sun.shadows = LightShadows.Soft;
        sunGo.transform.rotation = Quaternion.LookRotation(-SunVector());
        return sun;
    }

    const string ClearwaterSunName = "Sun (Clearwater)";

    /// <summary>Gives the open scene the Clearwater sky and sun, as Build Scene does: a "Sun (Clearwater)" light
    /// (made once, reused after; in a scene Build Scene made, its own sun), the Clearwater skybox, the sky of the time
    /// of day on the sun (ClearwaterSky) with the ambient light it sets, and the controller pointed at the sun. The
    /// world's other directional lights are switched off (their Light components; the objects, their children and
    /// other components are left alone), not deleted: two suns would double the light.</summary>
    [MenuItem("Tools/Clearwater/Use Clearwater Sky and Sun")]
    public static void UseSkyAndSun()
    {
        var sky = AssetDatabase.LoadAssetAtPath<Material>(Gen + "/Sky.mat");
        if (sky == null) sky = BuildAssets().sky;
        Light sun = null;
        foreach (var l in Object.FindObjectsOfType<Light>(true))
            if (l.type == LightType.Directional && (l.name == ClearwaterSunName || l.GetComponent<ClearwaterSky>() != null)) { sun = l; break; }
        var built = Object.FindObjectOfType<ClearwaterController>();
        if (sun == null && built != null && built.sun != null && built.sun.type == LightType.Directional && built.sun.name == "Sun")
            sun = built.sun; // (Build Scene's)
        if (sun == null)
        {
            sun = CreateSun(ClearwaterSunName);
            Undo.RegisterCreatedObjectUndo(sun.gameObject, "Clearwater sun");
        }
        else
        {
            Undo.RecordObject(sun.gameObject, "Clearwater sun");
            sun.gameObject.SetActive(true);
            Undo.RecordObject(sun, "Clearwater sun");
            sun.enabled = true;
        }
        var off = new List<string>();
        foreach (var l in Object.FindObjectsOfType<Light>())
            if (l != sun && l.type == LightType.Directional && l.enabled)
            {
                Undo.RecordObject(l, "Switch off other sun");
                l.enabled = false;
                off.Add(l.name);
            }
        RenderSettings.skybox = sky;
        RenderSettings.sun = sun;
        var ctl = Object.FindObjectOfType<ClearwaterController>();
        if (ctl != null)
        {
            Undo.RecordObject(ctl, "Clearwater sun");
            ctl.sun = sun;
            EditorUtility.SetDirty(ctl);
        }
        ClearwaterSkySetup.Install(sun); // (the sky of the time of day, and the ambient light it sets)
        EditorSceneManager.MarkSceneDirty(sun.gameObject.scene);
        Debug.Log("[Clearwater] Clearwater sky and sun set." + (off.Count > 0 ? " Switched off the light of: " + string.Join(", ", off) + " (tick their Light components to undo)." : ""));
    }

    /// <summary>Adds Clearwater to the open scene (an existing world): the water rig and a straight coast to draw on.
    /// The world's spawn and descriptor are kept; its sky and sun too, unless you choose Clearwater's.</summary>
    [MenuItem("Tools/Clearwater/Add to Current Scene")]
    public static void AddToCurrentScene()
    {
        if (Object.FindObjectOfType<ClearwaterController>() != null)
        {
            EditorUtility.DisplayDialog("Clearwater", "This scene already has Clearwater (a ClearwaterController). " +
                "For the Clearwater sky and sun, use Tools > Clearwater > Use Clearwater Sky and Sun.", "OK");
            return;
        }
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (string.IsNullOrEmpty(scene.path))
        {
            EditorUtility.DisplayDialog("Clearwater", "Save the scene first.", "OK");
            return;
        }
        int choice = EditorUtility.DisplayDialogComplex("Clearwater",
            "Use Clearwater's sky and sun as well? (The water reflects its own sky and matches its sun best.) " +
            "The world's directional lights are switched off (their Light components), not deleted.",
            "Clearwater sky and sun", "Cancel", "Keep the world's");
        if (choice == 1) return;
        var a = BuildAssets();
        var sun = RenderSettings.sun;
        if (sun == null) foreach (var l in Object.FindObjectsOfType<Light>()) if (l.type == LightType.Directional) { sun = l; break; }
        if (sun == null)
        {
            var sunGo = new GameObject("Sun");
            sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = SunColor;
            sun.intensity = SunIntensity;
            sunGo.transform.rotation = Quaternion.LookRotation(-SunVector());
            RenderSettings.sun = sun;
        }
        // the water reads depth, which VRChat only renders while the directional light casts shadows
        if (sun.shadows == LightShadows.None) sun.shadows = LightShadows.Soft;
        // the water reflects its own sky; use it as the skybox too unless the world has one of its own
        if (RenderSettings.skybox == null || RenderSettings.skybox.name == "Default-Skybox") RenderSettings.skybox = a.sky;
        CreateRig(a, sun);
        if (choice == 0) UseSkyAndSun();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        BakeSceneCoast();
        Debug.Log("[Clearwater] added to " + scene.path + ". Move the 'Coast (editor only)' points to draw your waterline, then Bake. " +
                  "Give the world's reference camera a far clip of a few km.");
    }

    // ---------------------------------------------------------------- shoreline wave timing

    [System.Serializable]
    class Breaks { public float loopSeconds; public float[] times; public float[] amps; }

    /// <summary>Wave phase + strength over the shore audio loop, one texel per ~0.09 s (see ClearwaterShore.cginc):
    /// phase runs 0 -> 2pi from one detected break to the next, so a crest reaches the waterline on each break.</summary>
    /// <summary>The breaks for the ballistic swash (ClearwaterShore.cginc): one texel each (r = time in the loop,
    /// g = strength), and per ~0.09 s of the loop the index of the last break at or before it. Point-sampled.</summary>
    static (Texture2D, Texture2D, int) BuildSwashBreaks()
    {
        var b = JsonUtility.FromJson<Breaks>(AssetDatabase.LoadAssetAtPath<TextAsset>(AudioDir + "/WavesShore_breaks.json").text);
        int n = b.times.Length;
        var br = new Texture2D(n, 1, TextureFormat.RGFloat, false, true)
        {
            name = "SwashBreaks", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point
        };
        var px = new Color[n];
        for (int i = 0; i < n; i++) px[i] = new Color(b.times[i], b.amps[i], 0, 1);
        br.SetPixels(px); br.Apply(false, false);
        const int W = 1024;
        var ix = new Texture2D(W, 1, TextureFormat.RFloat, false, true)
        {
            name = "SwashIdx", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point
        };
        var ipx = new Color[W];
        for (int i = 0; i < W; i++)
        {
            float t = b.loopSeconds * i / W;
            int k = n - 1; // (before the first break: the last one of the loop)
            for (int j = 0; j < n; j++) if (b.times[j] <= t) k = j;
            ipx[i] = new Color(k, 0, 0, 1);
        }
        ix.SetPixels(ipx); ix.Apply(false, false);
        return (br, ix, n);
    }

    static (Texture2D, float) BuildSwashTrack()
    {
        var b = JsonUtility.FromJson<Breaks>(AssetDatabase.LoadAssetAtPath<TextAsset>(AudioDir + "/WavesShore_breaks.json").text);
        int n = b.times.Length;
        const int W = 1024;
        var px = new Color[W];
        for (int i = 0; i < W; i++)
        {
            float t = b.loopSeconds * i / W;
            int k = n - 1; // last break at or before t (wrapping)
            for (int j = 0; j < n; j++) if (b.times[j] <= t) k = j;
            int k2 = (k + 1) % n;
            float t0 = b.times[k], t1 = b.times[k2];
            if (t0 > t) t0 -= b.loopSeconds;
            if (t1 <= t0) t1 += b.loopSeconds;
            float ph = 2 * Mathf.PI * (t - t0) / (t1 - t0);
            // strength: the wave that just broke while it drains, the next one while it comes in
            float amp = Mathf.Lerp(b.amps[k], b.amps[k2], Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.6f * Mathf.PI, 1.4f * Mathf.PI, ph)));
            px[i] = new Color(Mathf.Cos(ph), Mathf.Sin(ph), amp, 1);
        }
        var tex = new Texture2D(W, 1, TextureFormat.RGBAHalf, false, true)
        {
            name = "SwashTrack", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear
        };
        tex.SetPixels(px);
        tex.Apply(false, false);
        return (tex, b.loopSeconds);
    }

    // ---------------------------------------------------------------- audio

    const string AudioDir = Pkg + "/Audio";

    struct AudioRig { public AudioSource shore, bed, under; public float shoreZ; }



    static AudioRig BuildAudio(Transform water)
    {
        foreach (var name in new[] { "WavesShore", "WavesBed", "WavesUnderwater" })
        {
            var imp = (AudioImporter)AssetImporter.GetAtPath($"{AudioDir}/{name}.ogg");
            var st = imp.defaultSampleSettings;
            bool mono = name == "WavesShore";
            if (st.loadType == AudioClipLoadType.CompressedInMemory && st.compressionFormat == AudioCompressionFormat.Vorbis &&
                Mathf.Approximately(st.quality, 0.6f) && st.preloadAudioData && imp.forceToMono == mono && imp.loadInBackground)
                continue; // (shipped this way)
            st.loadType = AudioClipLoadType.CompressedInMemory;
            st.compressionFormat = AudioCompressionFormat.Vorbis;
            st.quality = 0.6f;
            st.preloadAudioData = true;
            imp.defaultSampleSettings = st;
            imp.forceToMono = mono;
            imp.loadInBackground = true;
            imp.SaveAndReimport();
        }

        var root = new GameObject("Wave Audio");
        root.transform.SetParent(water, false);
        AudioSource Source(string name, string clip, float volume, bool spatial)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            var src = go.AddComponent<AudioSource>();
            src.clip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{AudioDir}/{clip}.ogg");
            src.loop = true;
            src.playOnAwake = true;
            src.volume = volume;
            src.dopplerLevel = 0;
            src.spatialBlend = spatial ? 1 : 0;
            src.priority = 64;
            var sp = go.AddComponent<VRCSpatialAudioSource>();
            sp.EnableSpatialization = spatial;
            if (spatial)
            {
                sp.Gain = 6;              // dB
                sp.Near = 1.5f;           // full level within 1.5 m of the waterline
                sp.Far = 60;              // fades out ~60 m from the shore
                sp.VolumetricRadius = 4;  // a broad source, not a point
            }
            return src;
        }
        float shoreZ = DefaultWaterlineZ; // until the coast bake lays the source along the waterline
        var rig = new AudioRig
        {
            shore = Source("Shore Waves (3D, follows the listener along the waterline)", "WavesShore", 0.9f, true),
            bed = Source("Sea Bed (2D)", "WavesBed", 0.3f, false),
            under = Source("Underwater (2D)", "WavesUnderwater", 0f, false),
            shoreZ = shoreZ,
        };
        rig.shore.transform.position = new Vector3(0, water.position.y + 0.2f, shoreZ);
        return rig;
    }

    internal static void EnsureProgramAsset(string scriptPath)
    {
        string assetPath = Path.ChangeExtension(scriptPath, ".asset");
        if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(assetPath) != null) return;
        var prog = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
        prog.sourceCsScript = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);
        AssetDatabase.CreateAsset(prog, assetPath);
        AssetDatabase.SaveAssets();
        UdonSharpProgramAsset.CompileAllCsPrograms(true);
    }

    internal static void NameLayer(int layer, string name)
    {
        var tm = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layers = tm.FindProperty("layers");
        var p = layers.GetArrayElementAtIndex(layer);
        if (string.IsNullOrEmpty(p.stringValue)) { p.stringValue = name; tm.ApplyModifiedProperties(); }
    }
}
