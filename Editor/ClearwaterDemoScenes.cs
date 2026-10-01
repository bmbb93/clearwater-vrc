using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
#if CW_LIGHT_VOLUMES
using UdonSharpEditor;
using VRCLightVolumes;
#endif
using Object = UnityEngine.Object;

/// <summary>
/// Demo scenes, each showing a way to use Clearwater: the beach Clearwater makes itself (its own generated ground, as
/// Build Scene's), a cove modelled as a mesh (user terrain), a harbour (a quay, a stepped revetment, a ramp, a pier on
/// piles), a swimming pool (a user terrain with still water), a resort (the sea and two pools at other heights) and a
/// beach made with a Unity Terrain; with VRC Light Volumes in the project, also the beach at night lit by point light
/// volumes, one of them moving. Each has the sky's panel, called up anywhere (over the hand in VR, in front of
/// the view on a desktop); the Beach, the sample of a whole scene, also has the pebble by the spawn that shows it.
/// All tone map in post-processing, as a new scene does. Tools > Clearwater > Demo Scenes > Build Demo
/// Scenes makes them in Assets/Clearwater/Demo (the scenes, and their meshes, textures and materials, all made here);
/// each is a new Clearwater scene with its own ground, its bakes in a folder of its own in Generated like any scene's
/// (ADR 0003), and nothing else in the project is changed.
/// </summary>
public static class ClearwaterDemoScenes
{
    const string Dir = ClearwaterSetup.Root + "/Demo";
    const string Menu = "Tools/Clearwater/Demo Scenes/";

    [MenuItem(Menu + "Build Demo Scenes", false, 100)] static void BuildMenu() => Build(true);

    /// <summary>Builds the demo scenes (ask: offer to save the open scene first; otherwise its changes are dropped).</summary>
    public static void Build(bool ask)
    {
        if (ask && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        if (!AssetDatabase.IsValidFolder(ClearwaterSetup.Root)) AssetDatabase.CreateFolder("Assets", "Clearwater");
        if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder(ClearwaterSetup.Root, "Demo");
        foreach (var sub in new[] { "Meshes", "Materials", "Textures", "Terrain" })
            if (!AssetDatabase.IsValidFolder(Dir + "/" + sub)) AssetDatabase.CreateFolder(Dir, sub);
        _assets = ClearwaterSetup.BuildAssets();
        try
        {
            BuildCove();
            BuildHarbor();
            BuildPool();
            BuildResort();
            BuildTerrain();
#if CW_LIGHT_VOLUMES
            BuildLightVolumes();
#endif
            BuildBeach(); // (last: the one left open)
        }
        finally { _assets = null; }
        Debug.Log("[Clearwater] demo scenes built in " + Dir + " (Tools > Clearwater > Demo Scenes). The Beach demo (Clearwater's own ground) is open.");
    }

    [MenuItem(Menu + "Open Beach (Clearwater's own ground)", false, 199)] static void OpenBeach() => Open(Dir + "/Demo_Beach.unity");
    [MenuItem(Menu + "Open Cove (a mesh as the ground)", false, 200)] static void OpenCove() => Open(Dir + "/Demo_Cove.unity");
    [MenuItem(Menu + "Open Harbor (a quay and a pier)", false, 201)] static void OpenHarbor() => Open(Dir + "/Demo_Harbor.unity");
    [MenuItem(Menu + "Open Pool (still water only)", false, 202)] static void OpenPool() => Open(Dir + "/Demo_Pool.unity");
    [MenuItem(Menu + "Open Resort (the sea and two pools)", false, 203)] static void OpenResort() => Open(Dir + "/Demo_Resort.unity");
    [MenuItem(Menu + "Open Terrain (a Unity Terrain)", false, 204)] static void OpenTerrainScene() => Open(Dir + "/Demo_Terrain.unity");
#if CW_LIGHT_VOLUMES
    [MenuItem(Menu + "Open Light Volumes (VRC Light Volumes)", false, 205)] static void OpenLightVolumes() => Open(Dir + "/Demo_LightVolumes.unity");
#endif
    [MenuItem(Menu + "Back to the Clearwater Scene", false, 300)] static void OpenMain() => Open(ClearwaterSetup.ScenePath);

    /// <summary>Opens a scene (each keeps its own bakes: nothing to bake again).</summary>
    public static void Open(string path, bool ask = true)
    {
        if (!File.Exists(path))
        {
            Debug.LogWarning("[Clearwater] " + path + " has not been made yet: Tools > Clearwater > " +
                             (path == ClearwaterSetup.ScenePath ? "Build Scene." : "Demo Scenes > Build Demo Scenes."));
            return;
        }
        if (ask && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
    }

    // ------------------------------------------------------------------ scenes

    // the beach as Build Scene makes it: Clearwater's own generated ground (a straight coast, its cross-section and
    // relief), nothing added but the sky's panel and the pebble that shows it
    static void BuildBeach()
    {
        NewScene("Demo_Beach");
        SkyPanel(pebble: true);
        EditorSceneManager.SaveOpenScenes();
    }

    // the natural cove: a sand mesh whose shore winds and has a cove, and a rock in the water
    static void BuildCove()
    {
        var (coast, root) = Clone("Demo_Cove");
        Func<float, float> shore = x => 12f * Mathf.Sin(x / 40f) - 25f * Mathf.Exp(-Mathf.Pow((x - 30f) / 18f, 2));
        var mesh = Heightfield("Cove", 201, 100f, (x, z) =>
            Mathf.Clamp(-(z - shore(x)) * 0.083f, -2.5f, 1.2f) + 0.05f * (Mathf.PerlinNoise(x * 0.15f + 50, z * 0.15f + 50) - 0.5f), 0.5f);
        var sand = Mat("CoveSand", new Color(0.85f, 0.62f, 0.52f), Pkg("Sand.jpg"), 0.15f, Vector2.one);
        Child(root, "Ground", mesh, sand, Vector3.zero);
        var rock = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        rock.name = "Rock"; rock.transform.SetParent(root.transform, false);
        rock.transform.localPosition = new Vector3(-40, -1.2f, 14); rock.transform.localScale = new Vector3(9, 4, 7);
        rock.GetComponent<MeshRenderer>().sharedMaterial = Mat("Rock", new Color(0.45f, 0.44f, 0.42f), RockTex(), 0.1f, Vector2.one * 2);
        // (its collider the mesh itself: a sphere collider on a stretched sphere is a ball as wide as the widest axis)
        Object.DestroyImmediate(rock.GetComponent<SphereCollider>());
        rock.AddComponent<MeshCollider>();
        MeetShore(coast, shore);
        Finish(coast, root, true);
        // the generated ground past the mesh in the same sand (Match the user terrain)
        ClearwaterBedLooks.FromUserTerrain(coast);
        EditorSceneManager.SaveOpenScenes();
    }

    // a man-made shore: a vertical quay wall, a stepped revetment, a boat ramp; a pier on piles (the piles stamps the
    // waves break round)
    static void BuildHarbor()
    {
        var (coast, root) = Clone("Demo_Harbor");
        var mesh = Heightfield("Harbor", 401, 100f, (x, z) =>
        {
            const float top = 1.5f, floor = -3f;
            if (x >= -20f && x < 25f) // stepped revetment: 0.3 m steps every 0.6 m
                return z < -6f ? top : Mathf.Max(floor, top - (Mathf.Floor((z + 6f) / 0.6f) + 1f) * 0.3f);
            if (x >= 25f && x < 35f) // boat ramp, 1:8
                return Mathf.Clamp(top - (z + 12f) / 8f, floor, top);
            return z < 0f ? top : floor; // quay wall
        }, 0.25f);
        Child(root, "Quay", mesh, Mat("Concrete", new Color(0.78f, 0.77f, 0.74f), ConcreteTex(), 0.05f, Vector2.one), Vector3.zero);
        // the pier: not part of the ground (the water runs under it), its piles are obstacles to the waves
        var pier = new GameObject("Pier");
        pier.transform.SetParent(coast.transform.parent, false);
        pier.transform.position = coast.transform.position;
        var wood = Mat("Wood", new Color(0.45f, 0.33f, 0.24f), ConcreteTex(), 0.1f, new Vector2(0.3f, 3f));
        var deck = GameObject.CreatePrimitive(PrimitiveType.Cube);
        deck.name = "Deck"; deck.transform.SetParent(pier.transform, false);
        deck.transform.localPosition = new Vector3(63, 1.35f, 20); deck.transform.localScale = new Vector3(6, 0.3f, 40);
        deck.GetComponent<MeshRenderer>().sharedMaterial = wood;
        for (int k = 0; k < 9; k++)
            foreach (float px in new[] { 60.5f, 65.5f })
            {
                var pile = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                pile.name = "Pile"; pile.transform.SetParent(pier.transform, false);
                pile.transform.localPosition = new Vector3(px, -0.9f, 4 + k * 4.5f); pile.transform.localScale = new Vector3(0.45f, 2.2f, 0.45f);
                pile.GetComponent<MeshRenderer>().sharedMaterial = wood;
                var st = pile.AddComponent<ClearwaterStamp>();
                st.mode = ClearwaterStamp.Mode.Obstacle;
            }
        Finish(coast, root, false);
    }

    // a swimming pool: 25 x 12.5 m, 0.5 m deep (a jump climbs the 0.8 m from its floor to the deck), tiled, in a stone
    // deck 30 cm above the water; still water (no shore waves), the coast a loop round it with the water inside
    static void BuildPool()
    {
        var (coast, root) = Clone("Demo_Pool");
        const float hx = 12.5f, hz = 6.25f, deck = 0.3f, half = 30f; // (the deck covers the walkable area)
        var pool = new Rect(-hx, -hz, 2 * hx, 2 * hz);
        Child(root, "Deck", Deck("PoolDeck", Square(half), deck, pool), DeckStone(), Vector3.zero);
        Child(root, "Pool", Basin("PoolBasin", pool, 0.5f, 0.5f, deck), PoolTiles(), Vector3.zero);
        coast.closed = true;
        coast.shoreWaves = false;
        coast.points = Loop(pool);
        coast.groundHalfSize = half;
        Finish(coast, root, true);
    }

    // a resort by the sea (the default beach): a building on the land with an indoor pool on its ground
    // floor (its floor below the land's: the sea's ground and water are cut round it) in a hall open towards the sea,
    // and an outdoor pool on the terrace upstairs, over the hall, with a ramp up to it; lit indoors, with a reflection
    // probe
    static void BuildResort()
    {
        var (coast, root) = Clone("Demo_Resort");
        Object.DestroyImmediate(root); // (the sea's ground stays generated)
        Vector3 at = new Vector3(0, 0, -80); // on the land behind the beach (about 0.6 m up)
        var building = new GameObject("Building");
        building.transform.position = at;
        var stone = DeckStone(); var tiles = PoolTiles();
        var wall = Mat("ResortWall", new Color(0.92f, 0.9f, 0.86f), ConcreteTex(), 0.05f, Vector2.one * 0.5f);
        var area = new Rect(-15, -11, 30, 22);
        var pool1 = new Rect(-11, -5, 12, 6);  // ground floor, indoors
        var pool2 = new Rect(1, 0.5f, 10, 5);  // upstairs, outdoors
        const float floor1 = 1.0f, ceiling = 3.6f, floor2 = 5.0f, ground = 0.6f;
        Child(building, "Ground Floor", Deck("ResortFloor1", area, floor1, pool1), stone, Vector3.zero);
        Child(building, "Terrace", Deck("ResortFloor2", area, floor2, pool2), stone, Vector3.zero);
        Child(building, "Ceiling", Quads("ResortCeiling", new List<Vector3[]> { Rect(area.xMin, area.yMin, area.xMax, area.yMax, ceiling) }, 1f, true), wall, Vector3.zero);
        // the walls, both faces; the side towards the sea (+z) open between the floor and the ceiling
        var walls = new List<Vector3[]>();
        void Wall(Vector2 a, Vector2 b, float y0, float y1)
        {
            walls.Add(new[] { new Vector3(a.x, y0, a.y), new Vector3(b.x, y0, b.y), new Vector3(b.x, y1, b.y), new Vector3(a.x, y1, a.y) });
            walls.Add(new[] { new Vector3(b.x, y0, b.y), new Vector3(a.x, y0, a.y), new Vector3(a.x, y1, a.y), new Vector3(b.x, y1, b.y) });
        }
        Vector2 c00 = new Vector2(area.xMin, area.yMin), c10 = new Vector2(area.xMax, area.yMin), c11 = new Vector2(area.xMax, area.yMax), c01 = new Vector2(area.xMin, area.yMax);
        Wall(c00, c10, ground, floor2); Wall(c10, c11, ground, floor2); Wall(c01, c00, ground, floor2);
        Wall(c11, c01, ground, floor1); Wall(c11, c01, ceiling, floor2);
        Child(building, "Walls", Quads("ResortWalls", walls, 1f), wall, Vector3.zero);
        // a ramp up to the terrace outside the +x wall (on the right facing the sea): from the ground by the sea side
        // (about 14 degrees) to a landing level with the terrace, a parapet 1 m high along its outer edge and its end
        const float rx0 = 15f, rx1 = 17.5f, rzBottom = 11f, rzTop = -7f, rzEnd = -11f, foot = 0.45f, below = 0.4f, rail = 1f;
        var ramp = new List<Vector3[]>
        {
            new[] { new Vector3(rx0, floor2, rzTop), new Vector3(rx0, foot, rzBottom), new Vector3(rx1, foot, rzBottom), new Vector3(rx1, floor2, rzTop) },
            Rect(rx0, rzEnd, rx1, rzTop, floor2),
        };
        void BothFaces(Vector3[] q) { ramp.Add(q); ramp.Add(new[] { q[1], q[0], q[3], q[2] }); }
        BothFaces(new[] { new Vector3(rx1, below, rzBottom), new Vector3(rx1, below, rzTop), new Vector3(rx1, floor2 + rail, rzTop), new Vector3(rx1, foot + rail, rzBottom) });
        BothFaces(new[] { new Vector3(rx1, below, rzTop), new Vector3(rx1, below, rzEnd), new Vector3(rx1, floor2 + rail, rzEnd), new Vector3(rx1, floor2 + rail, rzTop) });
        BothFaces(new[] { new Vector3(rx1, below, rzEnd), new Vector3(rx0, below, rzEnd), new Vector3(rx0, floor2 + rail, rzEnd), new Vector3(rx1, floor2 + rail, rzEnd) });
        Child(building, "Ramp", Quads("ResortRamp", ramp, 1f), stone, Vector3.zero);
        // the pools: each at its water's height, its basin under it (0.7 m deep: a jump climbs the 0.8 m out)
        Pool("Pool (ground floor)", at + new Vector3(pool1.center.x, floor1 - 0.1f, pool1.center.y), pool1.size, 0.7f, true, tiles, "ResortBasin1");
        Pool("Pool (terrace)", at + new Vector3(pool2.center.x, floor2 - 0.1f, pool2.center.y), pool2.size, 0.7f, false, tiles, "ResortBasin2");
        // the hall's lights and its reflection probe
        foreach (float x in new[] { -8f, 6f })
        {
            var l = new GameObject("Hall Light").AddComponent<Light>();
            l.transform.SetParent(building.transform, false);
            l.transform.localPosition = new Vector3(x, ceiling - 0.3f, -3);
            l.type = LightType.Point; l.range = 14; l.intensity = 1.6f; l.color = new Color(1f, 0.93f, 0.82f);
        }
        var probe = new GameObject("Hall Reflection Probe").AddComponent<ReflectionProbe>();
        probe.transform.SetParent(building.transform, false);
        probe.transform.localPosition = new Vector3(0, (floor1 + ceiling) * 0.5f, 0);
        probe.size = new Vector3(area.width, ceiling - floor1 + 0.4f, area.height);
        probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Realtime;
        probe.refreshMode = UnityEngine.Rendering.ReflectionProbeRefreshMode.OnAwake;
        probe.boxProjection = true;
        ClearwaterCoastBake.Bake(coast); // (bakes the pools too, and cuts the sea round them)
        SpawnOnDryGround();
        SkyPanel();
        EditorSceneManager.SaveOpenScenes();
    }

    // a pool at its water's height (surface), its basin (depth deep, the walls up to 0.1 m over the water) under it
    static ClearwaterPool Pool(string name, Vector3 surface, Vector2 size, float deep, bool indoor, Material tiles, string mesh)
    {
        var go = new GameObject(name);
        go.transform.position = surface;
        var pool = go.AddComponent<ClearwaterPool>();
        pool.size = size; pool.indoor = indoor;
        pool.id = "demo"; // (a fixed id: building the demos again bakes into the same folders)
        var basin = new GameObject("Basin");
        basin.transform.SetParent(go.transform, false);
        Child(basin, "Basin", Basin(mesh, new Rect(-size.x * 0.5f, -size.y * 0.5f, size.x, size.y), deep, deep, 0.1f), tiles, Vector3.zero);
        pool.basin = basin;
        return pool;
    }

    static Material PoolTiles() => Mat("PoolTiles", Color.white, TileTex(), 0.6f, Vector2.one);
    static Material DeckStone() => Mat("DeckStone", new Color(0.86f, 0.83f, 0.77f), ConcreteTex(), 0.1f, Vector2.one * 0.5f);

    // a rectangle's corners as a coast loop, anticlockwise seen from above (the water inside)
    static Vector3[] Loop(Rect r) =>
        new[] { new Vector3(r.xMin, 0, r.yMin), new Vector3(r.xMax, 0, r.yMin), new Vector3(r.xMax, 0, r.yMax), new Vector3(r.xMin, 0, r.yMax) };

    static Rect Square(float half) => new Rect(-half, -half, 2 * half, 2 * half);

    // a flat deck over area (x, z) at height y with rectangular holes: cut into bands along z at the holes' edges, each
    // band's slabs between the holes crossing it
    static Mesh Deck(string name, Rect area, float y, params Rect[] holes) => Quads(name, DeckQuads(area, y, holes), 1f);

    static List<Vector3[]> DeckQuads(Rect area, float y, params Rect[] holes)
    {
        var zs = new List<float> { area.yMin, area.yMax };
        foreach (var h in holes) { zs.Add(h.yMin); zs.Add(h.yMax); }
        zs.Sort();
        var quads = new List<Vector3[]>();
        for (int b = 0; b + 1 < zs.Count; b++)
        {
            float z0 = zs[b], z1 = zs[b + 1];
            if (z1 - z0 < 1e-3f) continue;
            var cuts = new List<(float, float)>();
            foreach (var h in holes) if (h.yMin < z1 - 1e-3f && h.yMax > z0 + 1e-3f) cuts.Add((h.xMin, h.xMax));
            cuts.Sort();
            float x = area.xMin;
            foreach (var (a, e) in cuts) { if (a > x) quads.Add(Rect(x, z0, a, z1, y)); x = Mathf.Max(x, e); }
            if (x < area.xMax) quads.Add(Rect(x, z0, area.xMax, z1, y));
        }
        return quads;
    }

    // a pool's basin under a hole in the deck: four walls facing in, and the floor sloping from depth d0 (at xMin) to
    // d1 (at xMax)
    static Mesh Basin(string name, Rect r, float d0, float d1, float deck)
    {
        float x0 = r.xMin, x1 = r.xMax, z0 = r.yMin, z1 = r.yMax;
        Func<float, float> fy = x => -(d0 + (d1 - d0) * (x - x0) / (x1 - x0));
        var quads = new List<Vector3[]> {
            new[] { new Vector3(x0, fy(x0), z0), new Vector3(x1, fy(x1), z0), new Vector3(x1, deck, z0), new Vector3(x0, deck, z0) },
            new[] { new Vector3(x1, fy(x1), z1), new Vector3(x0, fy(x0), z1), new Vector3(x0, deck, z1), new Vector3(x1, deck, z1) },
            new[] { new Vector3(x0, fy(x0), z1), new Vector3(x0, fy(x0), z0), new Vector3(x0, deck, z0), new Vector3(x0, deck, z1) },
            new[] { new Vector3(x1, fy(x1), z0), new Vector3(x1, fy(x1), z1), new Vector3(x1, deck, z1), new Vector3(x1, deck, z0) },
            new[] { new Vector3(x0, fy(x0), z0), new Vector3(x0, fy(x0), z1), new Vector3(x1, fy(x1), z1), new Vector3(x1, fy(x1), z0) } };
        return Quads(name, quads, 1f);
    }

    // a beach made with a Unity Terrain: a cove, a headland and a sandbar, sand below and rock on the steep and the high
    static void BuildTerrain()
    {
        var (coast, root) = Clone("Demo_Terrain");
        const int res = 257; const float size = 200f, height = 8f, baseY = -4f;
        Func<float, float> shore = x => 10f * Mathf.Sin(x / 35f + 1f) - 22f * Mathf.Exp(-Mathf.Pow((x + 10f) / 20f, 2));
        Func<float, float, float> h = (x, z) =>
            Mathf.Clamp(-(z - shore(x)) * 0.07f, -3.3f, 1.8f)
            + 3.2f * Mathf.Exp(-Mathf.Pow((x - 55f) / 14f, 2) - Mathf.Pow((z - 5f) / 22f, 2))   // headland
            + 0.7f * Mathf.Exp(-Mathf.Pow((z - 38f) / 5f, 2)) * Mathf.SmoothStep(0, 1, (x + 90f) / 60f); // sandbar
        var td = new TerrainData { heightmapResolution = res, alphamapResolution = 256 };
        td.size = new Vector3(size, height, size);
        var hm = new float[res, res];
        for (int j = 0; j < res; j++)
            for (int i = 0; i < res; i++)
            {
                float x = -size / 2 + i * size / (res - 1), z = -size / 2 + j * size / (res - 1);
                hm[j, i] = Mathf.Clamp01((h(x, z) - baseY) / height);
            }
        td.SetHeights(0, 0, hm);
        var sandL = Layer("TerrainSand", Pkg("Sand.jpg"), 2f, new Color(1f, 0.95f, 0.88f));
        var rockL = Layer("TerrainRock", RockTex(), 4f, Color.white);
        td.terrainLayers = new[] { sandL, rockL };
        var am = new float[td.alphamapResolution, td.alphamapResolution, 2];
        for (int j = 0; j < td.alphamapResolution; j++)
            for (int i = 0; i < td.alphamapResolution; i++)
            {
                float nx = i / (td.alphamapResolution - 1f), nz = j / (td.alphamapResolution - 1f);
                float steep = td.GetSteepness(nx, nz), y = td.GetInterpolatedHeight(nx, nz) + baseY;
                float r = Mathf.Clamp01(Mathf.Max(Mathf.InverseLerp(14f, 24f, steep), Mathf.InverseLerp(2.2f, 3.2f, y)));
                am[j, i, 0] = 1f - r; am[j, i, 1] = r;
            }
        td.SetAlphamaps(0, 0, am);
        Save(td, "Terrain/BeachTerrain.asset");
        var tgo = Terrain.CreateTerrainGameObject(td);
        tgo.name = "Terrain";
        // matte: the built-in Standard terrain reads the sand texture's alpha as smoothness, a mirror-like sun glint
        tgo.GetComponent<Terrain>().materialTemplate =
            Save(new Material(Shader.Find("Nature/Terrain/Diffuse")) { name = "TerrainDiffuse" }, "Materials/TerrainDiffuse.mat");
        tgo.transform.SetParent(root.transform, false);
        tgo.transform.localPosition = new Vector3(-size / 2, baseY, -size / 2);
        MeetShore(coast, shore);
        Finish(coast, root, true);
    }

#if CW_LIGHT_VOLUMES
    // The beach at night (21:00, the moon up) lit by VRC Light Volumes' point light volumes, as Clearwater reads them:
    // lamps on the sand, over the shallows and under the water (in the shallows, and 1.6 m down), a spot over the beach,
    // and one that goes round across the waterline changing its colour and brightness (ClearwaterDemoLamp). Each has a
    // small glowing ball to show where it is (Standard: under the water too, where the water finds things by depth).
    static void BuildLightVolumes()
    {
        NewScene("Demo_LightVolumes");
        SkyPanel();
        var sky = Object.FindObjectOfType<ClearwaterSky>(true);
        if (sky != null)
        {
            sky.timeOfDay = 21f;
            UdonSharpEditorUtility.CopyProxyToUdon(sky);
            ClearwaterSkySetup.Show(sky);
        }
        var root = new GameObject("Light Volumes (Demo)");
        var warm = new Color(1f, 0.62f, 0.3f);
        var cool = new Color(0.4f, 0.8f, 1f);
        Lamp(root, "Lamp on the sand", new Vector3(-1.5f, 0.8f, -38f), warm, 6f);
        Lamp(root, "Lamp over the shallows", new Vector3(1.5f, 0.5f, -30f), warm, 6f);
        Lamp(root, "Lamp under the water", new Vector3(-3f, -0.35f, -31f), cool, 3f);
        Lamp(root, "Lamp in deeper water", new Vector3(-2f, -1.1f, -5f), cool, 3f);
        var spot = Lamp(root, "Spot on the beach", new Vector3(3.5f, 2.5f, -39f), new Color(1f, 0.9f, 0.75f), 8f);
        spot.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
        var moving = Lamp(root, "Moving lamp", new Vector3(4f, 0.6f, -33f), Color.white, 4f);
#if CW_LIGHT_VOLUMES_3
        spot.LightType = 1;                      // (a spot)
        spot.Angle = 70f * Mathf.Deg2Rad * 0.5f; // (3.x keeps the half angle, in radians)
        spot.Falloff = 0.6f;
        moving.IsDynamic = true;
#else
        spot.Type = PointLightVolume.LightType.SpotLight;
        spot.Angle = 70f;
        spot.Falloff = 0.6f;
        moving.Dynamic = true;
#endif

        ClearwaterSetup.EnsureProgramAsset(ClearwaterSetup.Pkg + "/Udon/ClearwaterDemoLamp.cs");
        var lamp = UdonSharpUndo.AddComponent<ClearwaterDemoLamp>(moving.gameObject);
        lamp.marker = moving.GetComponentInChildren<MeshRenderer>();
        var ground = GameObject.Find("Seabed Collider");
        if (ground != null) lamp.groundLayers = 1 << ground.layer;

#if CW_LIGHT_VOLUMES_3
        RegisterLights3(root);
        lamp.lightVolume = UdonSharpEditorUtility.GetBackingUdonBehaviour(moving);
#else
        var setup = Object.FindObjectOfType<LightVolumeSetup>();
        foreach (var v in root.GetComponentsInChildren<PointLightVolume>()) { v.SetupDependencies(); v.SyncUdonScript(); }
        lamp.lightVolume = UdonSharpEditorUtility.GetBackingUdonBehaviour(moving.PointLightVolumeInstance);
        if (setup == null) setup = Object.FindObjectOfType<LightVolumeSetup>();
        if (setup != null) { setup.RefreshVolumesList(); setup.SyncUdonScript(); }
#endif
        UdonSharpEditorUtility.CopyProxyToUdon(lamp);
        EditorSceneManager.SaveOpenScenes();
    }

#if CW_LIGHT_VOLUMES_3
    // VRC Light Volumes 3.x keeps a light's settings on its PointLightVolumeInstance, registered with the scene's one
    // Light Volume Manager. Its editor does that on its own, a moment after the change (and when a scene opens); a demo
    // built and saved at once does it here, through the same internal steps (by reflection: internal to the package,
    // and free to change in it. Missing, the lights wait for the scene to be opened)
    static void RegisterLights3(GameObject root)
    {
        const System.Reflection.BindingFlags Any = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance |
                                                  System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        var lights = root.GetComponentsInChildren<PointLightVolumeInstance>(true);
        var apply = typeof(PointLightVolumeInstance).GetMethod("EditorApplyAuthoringData", Any);
        var setupType = Type.GetType("VRCLightVolumes.LightVolumeSceneSetup, red.sim.LightVolumesEditor");
        var onboard = setupType?.GetMethod("OnboardHierarchy", Any);
        if (apply == null || onboard == null)
        {
            Debug.LogWarning("[Clearwater] Demo_LightVolumes: this VRC Light Volumes has no OnboardHierarchy or " +
                             "EditorApplyAuthoringData; its lamps will be set up when the scene is opened.");
            foreach (var l in lights) UdonSharpEditorUtility.CopyProxyToUdon(l);
            return;
        }
        foreach (var l in lights) apply.Invoke(l, new object[] { true, false, false });
        var args = new object[] { root, null, false };
        onboard.Invoke(null, args);
        foreach (var l in lights) UdonSharpEditorUtility.CopyProxyToUdon(l);
        if (args[1] is LightVolumeManager manager)
        {
            UdonSharpEditorUtility.CopyProxyToUdon(manager);
            manager.UpdateVolumes();
        }
    }
#endif

#if CW_LIGHT_VOLUMES_3
    // a point light volume (3.x: its instance alone) with a small glowing ball in its colour
    static PointLightVolumeInstance Lamp(GameObject root, string name, Vector3 pos, Color color, float intensity)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root.transform, false);
        go.transform.position = pos;
        var v = UdonSharpUndo.AddComponent<PointLightVolumeInstance>(go);
        v.LightType = 0;
        v.Color = color;
        v.Intensity = intensity;
        v.Range = 10f;
        v.LightSourceSize = 0.25f;
#else
    // a point light volume with a small glowing ball in its colour
    static PointLightVolume Lamp(GameObject root, string name, Vector3 pos, Color color, float intensity)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root.transform, false);
        go.transform.position = pos;
        // (its Udon side first, the U# way, so it has its UdonBehaviour at once: the volume would add it plainly, and
        // U# makes the UdonBehaviour for that only later, too late to be referred to or saved here)
        UdonSharpUndo.AddComponent<PointLightVolumeInstance>(go);
        var v = go.AddComponent<PointLightVolume>();
        v.Color = color;
        v.Intensity = intensity;
        v.Range = 10f;
        v.LightSourceSize = 0.25f;
#endif
        var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ball.name = "Marker";
        Object.DestroyImmediate(ball.GetComponent<Collider>());
        ball.transform.SetParent(go.transform, false);
        ball.transform.localScale = Vector3.one * 0.12f;
        var r = ball.GetComponent<MeshRenderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.sharedMaterial = Glow("Lamp " + name, color * 2f);
        return v;
    }

    static Material Glow(string name, Color emission)
    {
        var m = new Material(Shader.Find("Standard")) { name = name, color = Color.black };
        m.SetFloat("_Glossiness", 0f);
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", emission);
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        return Save(m, "Materials/" + name + ".mat");
    }
#endif

    // ------------------------------------------------------------------ building blocks

    static ClearwaterSetup.Assets _assets; // (the shared assets, made once per Build)

    // a new Clearwater scene (the default beach, baked), open; returns its coast
    static ClearwaterCoast NewScene(string name)
    {
        string path = Dir + "/" + name + ".unity";
        // made again from scratch in the same file (the same GUID, so the same folder of its own: emptied first)
        if (File.Exists(path))
        {
            string own = ClearwaterSetup.Gen + "/" + ClearwaterSetup.SceneDir(path, name).TrimEnd('/');
            if (AssetDatabase.IsValidFolder(own)) AssetDatabase.DeleteAsset(own);
        }
        ClearwaterSetup.CreateScene(_assets ?? ClearwaterSetup.BuildAssets(), path, false);
        return Object.FindObjectOfType<ClearwaterCoast>();
    }

    // a new Clearwater scene with an empty user terrain root at the coast object
    static (ClearwaterCoast, GameObject) Clone(string name)
    {
        var coast = NewScene(name);
        var root = new GameObject("User Terrain (" + name + ")");
        root.transform.position = coast.transform.position;
        return (coast, root);
    }

    static void Finish(ClearwaterCoast coast, GameObject root, bool keepLine)
    {
        coast.terrainSource = ClearwaterCoast.TerrainSource.User;
        coast.userTerrain = root;
        coast.seamWidth = 20f;
        if (!keepLine) { coast.closed = false; coast.points = new[] { new Vector3(-100, 0, 0), new Vector3(0, 0, 0), new Vector3(100, 0, 0) }; }
        coast.shape = ClearwaterCoast.LineShape.Straight;
        coast.handleIn = null; coast.handleOut = null; coast.corner = null;
        ClearwaterCoastBake.Bake(coast);
        SpawnOnDryGround();
        SkyPanel();
        EditorSceneManager.SaveOpenScenes();
    }

    // The spawn on dry ground, just over it. Build Scene's spot on its beach lay under the water in some demos (in
    // the pool, off the Terrain's shore) and in a step of the harbour's revetment: it goes back from the sea (-z)
    // to the first ground 25 cm over the water, and a metre past that edge.
    static void SpawnOnDryGround()
    {
        var desc = Object.FindObjectOfType<VRC.SDK3.Components.VRCSceneDescriptor>();
        var ctl = Object.FindObjectOfType<ClearwaterController>();
        if (desc == null || desc.spawns == null || desc.spawns.Length == 0 || desc.spawns[0] == null || ctl == null) return;
        var spawn = desc.spawns[0];
        float water = ctl.water.position.y;
        Physics.SyncTransforms();
        bool Dry(Vector3 p, out float y)
        {
            y = 0f;
            if (!Physics.Raycast(new Vector3(p.x, water + 20f, p.z), Vector3.down, out var hit, 60f, ~0, QueryTriggerInteraction.Ignore)) return false;
            y = hit.point.y;
            return y > water + 0.25f;
        }
        Vector3 at = spawn.position;
        for (int k = 0; k < 80; k++, at.z -= 0.5f)
        {
            if (!Dry(at, out _)) continue;
            Vector3 back = at - new Vector3(0f, 0f, k == 0 ? 0f : 1f); // (a metre in from the edge it found)
            if (!Dry(back, out float y)) { back = at; Dry(at, out y); }
            spawn.position = new Vector3(back.x, y + 0.1f, back.z);
            EditorUtility.SetDirty(spawn);
            return;
        }
    }

    // the sky's panel, called up anywhere; with the pebble by the spawn that shows it (the Beach's alone)
    static void SkyPanel(bool pebble = false)
    {
        var sky = Object.FindObjectOfType<ClearwaterSky>(true);
        if (sky != null) ClearwaterSkySetup.AddPanel(sky, pebble);
    }

    // the line drawn to meet the ground's shore (z = shore(x)) at the walkable area's edges, on out straight past them
    static void MeetShore(ClearwaterCoast coast, Func<float, float> shore)
    {
        coast.closed = false;
        coast.points = new[] { new Vector3(-100, 0, shore(-100)), new Vector3(100, 0, shore(100)) };
    }

    static GameObject Child(GameObject root, string name, Mesh mesh, Material mat, Vector3 pos)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root.transform, false);
        go.transform.localPosition = pos;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        go.AddComponent<MeshCollider>().sharedMesh = mesh;
        return go;
    }

    // a height field over [-half, half]^2 (n vertices a side), uv = position x uvScale seen from above; a steep cell (a
    // wall, a step's riser) has its own corners with the uv seen from the side it faces, so its texture is not
    // stretched down it (and its edges are sharp)
    static Mesh Heightfield(string name, int n, float half, Func<float, float, float> h, float uvScale)
    {
        var v = new List<Vector3>(n * n); var uv = new List<Vector2>(n * n);
        float step = 2 * half / (n - 1);
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                float x = -half + i * step, z = -half + j * step;
                v.Add(new Vector3(x, h(x, z), z)); uv.Add(new Vector2(x, z) * uvScale);
            }
        var idx = new List<int>((n - 1) * (n - 1) * 6);
        for (int j = 0; j < n - 1; j++)
            for (int i = 0; i < n - 1; i++)
            {
                int a = j * n + i, b = a + 1, c = a + n, d = c + 1;
                float rx = 0.5f * (v[b].y + v[d].y - v[a].y - v[c].y), rz = 0.5f * (v[c].y + v[d].y - v[a].y - v[b].y);
                if (Mathf.Max(Mathf.Abs(rx), Mathf.Abs(rz)) > step) // steeper than 45 degrees
                {
                    bool facesZ = Mathf.Abs(rz) >= Mathf.Abs(rx);
                    int s = v.Count;
                    foreach (int k in new[] { a, b, c, d })
                    {
                        v.Add(v[k]);
                        uv.Add(new Vector2(facesZ ? v[k].x : v[k].z, v[k].y) * uvScale);
                    }
                    a = s; b = s + 1; c = s + 2; d = s + 3;
                }
                idx.Add(a); idx.Add(c); idx.Add(b); idx.Add(b); idx.Add(c); idx.Add(d);
            }
        var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetTriangles(idx, 0);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return Save(mesh, "Meshes/" + name + ".asset");
    }

    static Vector3[] Rect(float x0, float z0, float x1, float z1, float y) =>
        new[] { new Vector3(x0, y, z0), new Vector3(x0, y, z1), new Vector3(x1, y, z1), new Vector3(x1, y, z0) };

    // flat quads (corners in order), uv = metres across the quad's own plane from its first corner x uvScale (u along its
    // first edge; right for any four-sided shape, the pool's walls over a sloping floor too); flip = wound the other way
    static Mesh Quads(string name, List<Vector3[]> quads, float uvScale, bool flip = false)
    {
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var idx = new List<int>();
        foreach (var q in quads)
        {
            int b = v.Count;
            Vector3 eu = (q[1] - q[0]).normalized, ev = Vector3.ProjectOnPlane(q[3] - q[0], eu).normalized;
            v.AddRange(q);
            foreach (var p in q) uv.Add(new Vector2(Vector3.Dot(p - q[0], eu), Vector3.Dot(p - q[0], ev)) * uvScale);
            if (!flip) idx.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
            else idx.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
        }
        var mesh = new Mesh { name = name, vertices = v.ToArray(), uv = uv.ToArray(), triangles = idx.ToArray() };
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return Save(mesh, "Meshes/" + name + ".asset");
    }

    static Material Mat(string name, Color color, Texture2D tex, float smooth, Vector2 tiling)
    {
        var m = new Material(Shader.Find("Standard")) { name = name, color = color, mainTexture = tex };
        m.mainTextureScale = tiling;
        m.SetFloat("_Glossiness", smooth);
        return Save(m, "Materials/" + name + ".mat");
    }

    static TerrainLayer Layer(string name, Texture2D tex, float tile, Color tint)
    {
        var l = new TerrainLayer { name = name, diffuseTexture = tex, tileSize = new Vector2(tile, tile), diffuseRemapMax = tint };
        return Save(l, "Terrain/" + name + ".terrainlayer");
    }

    static Texture2D Pkg(string file) => AssetDatabase.LoadAssetAtPath<Texture2D>(ClearwaterSetup.Pkg + "/Textures/" + file);

    static T Save<T>(T o, string rel) where T : Object
    {
        string path = Dir + "/" + rel;
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(o, path);
        return o;
    }

    // ------------------------------------------------------------------ textures (made here, no photographs)

    static Texture2D ConcreteTex() => MakeTex("Concrete", 512, (x, y) =>
    {
        float n = 0.55f + 0.18f * Fbm(x / 64f, y / 64f) + 0.08f * (Hash(x, y) - 0.5f);
        return new Color(n, n * 0.99f, n * 0.96f);
    });

    static Texture2D RockTex() => MakeTex("Rock", 512, (x, y) =>
    {
        float n = 0.45f + 0.3f * Fbm(x / 40f + 7, y / 40f + 3) + 0.06f * (Hash(x, y) - 0.5f);
        return new Color(n * 0.95f, n * 0.93f, n * 0.88f);
    });

    // 4 x 4 tiles (a 1 m square at a tiling of 1 per metre), pale blue with white grout
    static Texture2D TileTex() => MakeTex("Tiles", 512, (x, y) =>
    {
        int tx = x % 128, ty = y % 128;
        bool grout = tx < 4 || ty < 4;
        float n = 0.03f * (Hash(x / 128, y / 128) - 0.5f);
        return grout ? new Color(0.92f, 0.92f, 0.9f) : new Color(0.62f + n, 0.82f + n, 0.9f + n);
    });

    static Texture2D MakeTex(string name, int size, Func<int, int, Color> f)
    {
        string path = Dir + "/Textures/" + name + ".png";
        var t = new Texture2D(size, size, TextureFormat.RGB24, false);
        var px = new Color[size * size];
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) px[y * size + x] = f(x, y);
        t.SetPixels(px); t.Apply();
        File.WriteAllBytes(path, t.EncodeToPNG());
        Object.DestroyImmediate(t);
        AssetDatabase.ImportAsset(path);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.wrapMode = TextureWrapMode.Repeat; ti.anisoLevel = 8;
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static float Hash(int x, int y) { uint h = (uint)(x * 374761393 + y * 668265263); h = (h ^ (h >> 13)) * 1274126177; return (h & 0xffff) / 65535f; }

    // tiling noise (period 8 in the given units)
    static float Fbm(float x, float y)
    {
        float v = 0, a = 0.5f;
        for (int o = 0; o < 4; o++) { v += a * (Mathf.PerlinNoise(x, y) - 0.5f); x *= 2.03f; y *= 2.03f; a *= 0.5f; }
        return v;
    }
}
