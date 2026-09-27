using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEditor.Events;

/// <summary>
/// The sky of the time of day in the editor: its tables baked (ClearwaterAtmosphere) and given to the scene's
/// ClearwaterSky, the sky set up on the Clearwater sun, and shown in the Scene and Game views while you edit (the
/// shaders' sky values are not saved with the scene: they are put back whenever a scene opens or the scripts reload).
/// </summary>
[InitializeOnLoad]
public static class ClearwaterSkySetup
{
    const string LutFile = "SkyLUT.asset";
    const string ProbeName = "Sky Reflection (Clearwater)";
    static ClearwaterAtmosphere.Result _tables;

    static ClearwaterSkySetup()
    {
        EditorApplication.delayCall += ShowAll;
        EditorSceneManager.sceneOpened += (s, m) => ShowAll();
        EditorApplication.playModeStateChanged += s => { if (s == PlayModeStateChange.EnteredEditMode) ShowAll(); };
    }

    /// <summary>The baked sky (once per editor session: it takes a few seconds), its table saved as Generated/SkyLUT.</summary>
    internal static ClearwaterAtmosphere.Result Tables()
    {
        if (_tables == null || _tables.sky == null)
        {
            var r = ClearwaterAtmosphere.Bake();
            r.sky = ClearwaterSetup.Save(r.sky, LutFile);
            _tables = r;
        }
        return _tables;
    }

    /// <summary>Gives the sun the sky of the time of day (made once, kept after: its settings stay as you left them),
    /// with a reflection probe of the sky, and Unity's ambient light for it to set.</summary>
    internal static ClearwaterSky Install(Light sun)
    {
        ClearwaterSetup.EnsureProgramAsset(ClearwaterSetup.Pkg + "/Udon/ClearwaterSky.cs");
        var sky = sun.GetComponent<ClearwaterSky>();
        bool made = sky == null;
        if (made) sky = UdonSharpUndo.AddComponent<ClearwaterSky>(sun.gameObject);
        else Undo.RecordObject(sky, "Clearwater sky");
        Fill(sky);
        if (made) sky.north = DefaultNorth(sky.timeOfDay, sky.latitude, sky.dayOfYear);
        sky.sunLight = sun;
        if (sky.controller == null) sky.controller = Object.FindObjectOfType<ClearwaterController>(true);

        var probe = sky.reflectionProbe;
        if (probe == null)
        {
            var t = sun.transform.Find(ProbeName);
            var go = t != null ? t.gameObject : new GameObject(ProbeName);
            if (t == null) Undo.RegisterCreatedObjectUndo(go, "Clearwater sky");
            go.transform.SetParent(sun.transform, false);
            probe = go.GetComponent<ReflectionProbe>();
            if (probe == null) probe = go.AddComponent<ReflectionProbe>();
        }
        // the sky alone, everywhere, beneath any probe of the world's own
        probe.mode = ReflectionProbeMode.Realtime;
        probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
        probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
        probe.cullingMask = 0;
        probe.clearFlags = ReflectionProbeClearFlags.Skybox;
        probe.size = Vector3.one * 100000f;
        probe.importance = 0;
        probe.resolution = 128;
        probe.hdr = true;
        probe.boxProjection = false;
        sky.reflectionProbe = probe;

        RenderSettings.ambientMode = AmbientMode.Trilight;
        EditorUtility.SetDirty(sky);
        Show(sky);
        return sky;
    }

    const string PanelName = "Sky Control Panel (Clearwater)";
    const int UILayer = 5;
    const int WalkthroughLayer = 17; // (players go through it and can still use it)
    const string StoneName = "Sky Stone (Clearwater)";

    /// <summary>A panel in the world for the sky (ClearwaterSkyPanel): the hour, the clouds and the length of the day on
    /// sliders, the day going by on a toggle; anyone can use it and what they set goes to everyone. Placed in front of the
    /// spawn, facing it, what shows on the UI layer (out of VRChat's camera); one made before is replaced in place.</summary>
    [MenuItem("Tools/Clearwater/Add Sky Control Panel")]
    public static void AddPanel()
    {
        var sky = Object.FindObjectOfType<ClearwaterSky>(true);
        if (sky == null)
        {
            EditorUtility.DisplayDialog("Clearwater", "This scene has no sky of the day: use Tools > Clearwater > Use Clearwater Sky and Sun first.", "OK");
            return;
        }
        Undo.RecordObject(sky, "Clearwater sky panel");
        if (sky.controller == null) sky.controller = Object.FindObjectOfType<ClearwaterController>(true);
        EditorUtility.SetDirty(sky);
        ClearwaterSetup.EnsureProgramAsset(ClearwaterSetup.Pkg + "/Udon/ClearwaterSkyPanel.cs");
        ClearwaterSetup.EnsureProgramAsset(ClearwaterSetup.Pkg + "/Udon/ClearwaterSkyPanelOpener.cs");

        // by the spawn: the pebble lies in front of it, the panel above the pebble
        Vector3 at = Vector3.zero; Quaternion facing = Quaternion.identity;
        var desc = Object.FindObjectOfType<VRC.SDK3.Components.VRCSceneDescriptor>();
        if (desc != null && desc.spawns != null && desc.spawns.Length > 0 && desc.spawns[0] != null)
        {
            at = desc.spawns[0].position; facing = Quaternion.Euler(0f, desc.spawns[0].eulerAngles.y, 0f);
        }
        Vector3 spawn = at; Quaternion spawnFacing = facing;
        Transform parent = null;
        var old = Object.FindObjectOfType<ClearwaterSkyPanel>(true);
        if (old != null)
        {
            parent = old.transform.parent;
            Undo.DestroyObjectImmediate(old.gameObject);
        }
        var root = new GameObject(PanelName);
        Undo.RegisterCreatedObjectUndo(root, "Clearwater sky panel");
        root.layer = WalkthroughLayer; // (VRCUiShape's collider is on it: avatars walk through the panel)
        root.transform.SetParent(parent, false);
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        root.AddComponent<GraphicRaycaster>();
        var rt = (RectTransform)root.transform;
        rt.sizeDelta = new Vector2(420f, 360f);
        rt.localScale = Vector3.one * 0.0015f; // (63 x 54 cm)
        root.AddComponent<VRC.SDK3.Components.VRCUiShape>();
        // what shows sits in a canvas of its own on the UI layer, which VRChat's camera leaves out (unless its UI is
        // on); the shape stays on the root's layer, as VRChat's pointer passes by UI-layer shapes while its menu is shut
        var face = new GameObject("Face", typeof(RectTransform));
        face.transform.SetParent(root.transform, false);
        var fr = (RectTransform)face.transform;
        fr.anchorMin = Vector2.zero; fr.anchorMax = Vector2.one; fr.sizeDelta = Vector2.zero;
        face.AddComponent<Canvas>();
        face.AddComponent<GraphicRaycaster>();

        var res = new DefaultControls.Resources
        {
            standard = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"),
            background = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd"),
            knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd"),
            checkmark = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Checkmark.psd"),
        };
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var bg = face.AddComponent<Image>();
        bg.sprite = res.background; bg.type = Image.Type.Sliced; bg.color = new Color(0.08f, 0.10f, 0.14f, 0.82f);

        Text Label(string text, float y)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(face.transform, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 1f);
            r.sizeDelta = new Vector2(380f, 32f); r.anchoredPosition = new Vector2(0f, y);
            var t = go.AddComponent<Text>();
            t.font = font; t.fontSize = 24; t.color = Color.white; t.text = text; t.alignment = TextAnchor.MiddleLeft;
            return t;
        }
        Slider MakeSlider(string name, float y, float max)
        {
            var go = DefaultControls.CreateSlider(res);
            go.name = name;
            go.transform.SetParent(face.transform, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 1f);
            r.sizeDelta = new Vector2(380f, 24f); r.anchoredPosition = new Vector2(0f, y);
            var s = go.GetComponent<Slider>();
            s.minValue = 0f; s.maxValue = max;
            return s;
        }
        var hourLabel = Label("Time", -30f);
        var hour = MakeSlider("Hour", -62f, 24f);
        var cloudLabel = Label("Clouds", -100f);
        var clouds = MakeSlider("Clouds", -132f, 1f);
        var speedLabel = Label("Cloud drift", -170f);
        var speed = MakeSlider("Cloud drift", -202f, 60f);
        speed.wholeNumbers = true;
        var dayLabel = Label("A day in", -240f);
        var dayLength = MakeSlider("Day length", -272f, ClearwaterSkyPanel.DayLengths().Length - 1);
        dayLength.wholeNumbers = true;
        var toggleGo = DefaultControls.CreateToggle(res);
        toggleGo.name = "Day goes by";
        toggleGo.transform.SetParent(face.transform, false);
        var tr = (RectTransform)toggleGo.transform;
        tr.anchorMin = tr.anchorMax = new Vector2(0.5f, 1f);
        tr.sizeDelta = new Vector2(380f, 30f); tr.anchoredPosition = new Vector2(0f, -320f);
        var toggle = toggleGo.GetComponent<Toggle>();
        var toggleText = toggleGo.GetComponentInChildren<Text>();
        toggleText.font = font; toggleText.fontSize = 24; toggleText.color = Color.white; toggleText.text = "Day goes by";
        var tl = (RectTransform)toggleText.transform; tl.offsetMin = new Vector2(34f, 0f);
        var box = (RectTransform)toggleGo.transform.Find("Background");
        box.sizeDelta = new Vector2(26f, 26f);

        var panel = UdonSharpUndo.AddComponent<ClearwaterSkyPanel>(root);
        panel.sky = sky;
        panel.hourSlider = hour; panel.cloudSlider = clouds; panel.cycleToggle = toggle;
        panel.hourLabel = hourLabel; panel.cloudLabel = cloudLabel;
        panel.cloudSpeedSlider = speed; panel.cloudSpeedLabel = speedLabel;
        panel.dayLengthSlider = dayLength; panel.dayLengthLabel = dayLabel;
        EditorUtility.SetDirty(panel);
        // the UI tells the panel's Udon program (VRChat lets UI events call SendCustomEvent)
        var udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(panel);
        UnityEventTools.AddStringPersistentListener(hour.onValueChanged, udon.SendCustomEvent, "OnHour");
        UnityEventTools.AddStringPersistentListener(clouds.onValueChanged, udon.SendCustomEvent, "OnClouds");
        UnityEventTools.AddStringPersistentListener(speed.onValueChanged, udon.SendCustomEvent, "OnCloudSpeed");
        UnityEventTools.AddStringPersistentListener(dayLength.onValueChanged, udon.SendCustomEvent, "OnDayLength");
        UnityEventTools.AddStringPersistentListener(toggle.onValueChanged, udon.SendCustomEvent, "OnCycle");
        foreach (var t in face.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = UILayer;
        hour.value = sky.timeOfDay; toggle.isOn = sky.cycle;
        clouds.value = sky.controller != null ? sky.controller.CloudCover() : 0f;
        speed.value = sky.controller != null ? sky.controller.CloudSpeed() : 0f;
        dayLength.value = ClearwaterSkyPanel.NearestDayLength(sky.dayMinutes);
        var stone = AddStone(root, spawn, spawnFacing);
        // above the pebble, facing away from the spawn (the pebble puts it there again, facing whoever uses it)
        Vector3 away = stone.transform.position - spawn;
        away.y = 0f;
        root.transform.SetPositionAndRotation(stone.transform.position + Vector3.up * 1.3f,
            away.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(away, Vector3.up) : spawnFacing);
        root.SetActive(false); // (the stone shows it)
        Selection.activeGameObject = stone;
        EditorSceneManager.MarkSceneDirty(root.scene);
        Debug.Log("[Clearwater] Sky control panel added, shown above the pebble in front of the spawn when it is used: move the pebble where you like.");
    }

    /// <summary>A pebble on the ground by the spawn that shows the panel (ClearwaterSkyPanelOpener); one made before
    /// is replaced where it lies.</summary>
    static GameObject AddStone(GameObject panel, Vector3 spawn, Quaternion facing)
    {
        Vector3 at = spawn + facing * new Vector3(0.8f, 0f, 1.2f);
        Quaternion turn = facing * Quaternion.Euler(0f, 25f, 0f);
        Transform parent = null;
        var old = Object.FindObjectOfType<ClearwaterSkyPanelOpener>(true);
        if (old != null)
        {
            at = old.transform.position; turn = old.transform.rotation; parent = old.transform.parent;
            Undo.DestroyObjectImmediate(old.gameObject);
        }
        else
        {
            Physics.SyncTransforms();
            if (Physics.Raycast(at + Vector3.up * 3f, Vector3.down, out var hit, 10f, ~0, QueryTriggerInteraction.Ignore))
                at = hit.point;
            at.y -= 0.01f; // (settled in the sand)
        }
        var go = new GameObject(StoneName);
        Undo.RegisterCreatedObjectUndo(go, "Clearwater sky panel");
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(at, turn);
        go.layer = WalkthroughLayer;
        var mesh = ClearwaterSetup.Save(Pebble(), "Pebble.asset");
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mat = new Material(Shader.Find("Standard")) { color = new Color(0.40f, 0.36f, 0.31f) };
        mat.SetFloat("_Glossiness", 0.08f);
        mat = ClearwaterSetup.Save(mat, "Pebble.mat");
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        var col = go.AddComponent<MeshCollider>();
        col.sharedMesh = mesh; col.convex = true;
        var opener = UdonSharpUndo.AddComponent<ClearwaterSkyPanelOpener>(go);
        opener.panel = panel;
        EditorUtility.SetDirty(opener);
        UdonSharpEditorUtility.GetBackingUdonBehaviour(opener).interactText = "Sky";
        return go;
    }

    /// <summary>A pebble about 30 cm across, flat underneath: a lumpy squashed ball.</summary>
    static Mesh Pebble()
    {
        // an icosahedron cut twice into four
        float g = (1f + Mathf.Sqrt(5f)) / 2f;
        var v = new System.Collections.Generic.List<Vector3>
        {
            new Vector3(-1, g, 0), new Vector3(1, g, 0), new Vector3(-1, -g, 0), new Vector3(1, -g, 0),
            new Vector3(0, -1, g), new Vector3(0, 1, g), new Vector3(0, -1, -g), new Vector3(0, 1, -g),
            new Vector3(g, 0, -1), new Vector3(g, 0, 1), new Vector3(-g, 0, -1), new Vector3(-g, 0, 1),
        };
        var f = new System.Collections.Generic.List<int>
        {
            0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
            3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
        };
        for (int i = 0; i < v.Count; i++) v[i] = v[i].normalized;
        for (int pass = 0; pass < 2; pass++)
        {
            var mid = new System.Collections.Generic.Dictionary<long, int>();
            int Mid(int a, int b)
            {
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (mid.TryGetValue(key, out int m)) return m;
                v.Add(((v[a] + v[b]) * 0.5f).normalized);
                return mid[key] = v.Count - 1;
            }
            var next = new System.Collections.Generic.List<int>();
            for (int i = 0; i < f.Count; i += 3)
            {
                int a = f[i], b = f[i + 1], c = f[i + 2], ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                next.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
            f = next;
        }
        var verts = new Vector3[v.Count];
        for (int i = 0; i < v.Count; i++)
        {
            Vector3 d = v[i];
            float lump = 1f + 0.16f * Mathf.Sin(2.3f * d.x + 1.3f) * Mathf.Sin(1.9f * d.z + 0.4f)
                + 0.07f * Mathf.Sin(4.7f * d.y + 3.1f * d.x) + 0.04f * Mathf.Sin(7.3f * d.z - 2.9f * d.y + 0.8f);
            Vector3 p = new Vector3(d.x * 0.16f * (1f + 0.25f * d.z), d.y * 0.075f, d.z * 0.12f) * lump; // (wider at one end)
            if (p.y > 0f) p.y *= 1f - 0.35f * p.y / 0.09f; // (a flatter back)
            p.y = Mathf.Max(p.y, -0.02f) + 0.02f; // (flat underneath, resting on its base)
            verts[i] = p;
        }
        var mesh = new Mesh { vertices = verts, triangles = f.ToArray() };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>The baked tables, and the light the sky is calibrated to: the fixed sky's, with its sun 31 degrees up.</summary>
    internal static void Fill(ClearwaterSky sky)
    {
        var r = Tables();
        sky.skyTable = (Texture3D)r.sky;
        sky.sunDirect = r.sunDirect;
        sky.skyLight = r.skyLight;
        sky.horizon = r.horizon;
        sky.tableStart = ClearwaterAtmosphere.SunMinDeg;
        sky.tableStep = ClearwaterAtmosphere.TableStep;
        sky.referenceElevation = Mathf.Asin(ClearwaterSetup.SunVector().y) * Mathf.Rad2Deg;
        bool lin = GraphicsSettings.lightsUseLinearIntensity;
        Color c = ClearwaterSetup.SunColor * (lin ? 1f : ClearwaterSetup.SunIntensity);
        Color l = c.linear * (lin ? ClearwaterSetup.SunIntensity : 1f);
        sky.lightReference = new Vector3(l.r, l.g, l.b);
        sky.linearIntensity = lin;
    }

    /// <summary>Where north is (degrees clockwise from +Z) for the sky at that hour, latitude and day to put the sun
    /// where the fixed sky has it.</summary>
    internal static float DefaultNorth(float hours, float latitude, int dayOfYear)
    {
        Vector3 enu = SunEnu(hours, latitude, dayOfYear);
        Vector3 v = ClearwaterSetup.SunVector();
        float worldAz = Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg, localAz = Mathf.Atan2(enu.x, enu.y) * Mathf.Rad2Deg;
        return Mathf.Repeat(worldAz - localAz, 360f);
    }

    /// <summary>The sun (east, north, up) at the hour (local solar time), latitude and day of the year.</summary>
    internal static Vector3 SunEnu(float hours, float latitude, int dayOfYear)
    {
        float dec = -23.44f * Mathf.Deg2Rad * Mathf.Cos(2f * Mathf.PI / 365f * (dayOfYear + 10));
        float ha = (hours - 12f) * 15f * Mathf.Deg2Rad, lat = latitude * Mathf.Deg2Rad;
        return new Vector3(-Mathf.Cos(dec) * Mathf.Sin(ha),
                           Mathf.Sin(dec) * Mathf.Cos(lat) - Mathf.Cos(dec) * Mathf.Cos(ha) * Mathf.Sin(lat),
                           Mathf.Sin(dec) * Mathf.Sin(lat) + Mathf.Cos(dec) * Mathf.Cos(ha) * Mathf.Cos(lat));
    }

    /// <summary>Shows the sky at its hour in the editor (the sun turned, the light and the ambient set to match).</summary>
    internal static void Show(ClearwaterSky sky)
    {
        if (sky == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
        sky.ShowHour(sky.timeOfDay);
        DynamicGI.UpdateEnvironment();
        SceneView.RepaintAll();
    }

    // every open scene's sky; none: the shaders keep the fixed sky (the values of one shown before cleared)
    static void ShowAll()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var skies = Object.FindObjectsOfType<ClearwaterSky>();
        foreach (var s in skies) if (s.isActiveAndEnabled) { Show(s); return; }
        Shader.SetGlobalVector("_Udon_CWSunColor", Vector4.zero);
        Shader.SetGlobalVector("_Udon_CWKey", Vector4.zero);
    }
}

[CustomEditor(typeof(ClearwaterSky))]
public class ClearwaterSkyEditor : Editor
{
    public override void OnInspectorGUI()
    {
        if (UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader(target)) return;
        var sky = (ClearwaterSky)target;
        EditorGUI.BeginChangeCheck();
        DrawPropertiesExcluding(serializedObject, "m_Script", "skyTable", "sunDirect", "skyLight", "horizon", "tableStart",
                                "tableStep", "referenceElevation", "lightReference", "linearIntensity");
        bool changed = EditorGUI.EndChangeCheck();
        serializedObject.ApplyModifiedProperties();

        Vector3 enu = ClearwaterSkySetup.SunEnu(sky.timeOfDay, sky.latitude, sky.dayOfYear);
        float el = Mathf.Asin(enu.z) * Mathf.Rad2Deg;
        int hh = Mathf.FloorToInt(sky.timeOfDay), mm = Mathf.FloorToInt((sky.timeOfDay - hh) * 60f);
        EditorGUILayout.HelpBox($"{hh:00}:{mm:00}: the sun {el:0.0} degrees " + (el >= 0f ? "up" : "down") +
            (el < -18f ? " (night)" : el < -0.8f ? " (twilight)" : "") +
            (sky.cycle ? $". The day goes by: {sky.dayMinutes:0.#} minutes a day, from this hour." : "."), MessageType.None);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("North as the fixed sky's"))
            {
                Undo.RecordObject(sky, "Clearwater sky");
                sky.north = ClearwaterSkySetup.DefaultNorth(sky.timeOfDay, sky.latitude, sky.dayOfYear);
                changed = true;
            }
            if (GUILayout.Button("Bake the sky again"))
            {
                Undo.RecordObject(sky, "Clearwater sky");
                ClearwaterSkySetup.Fill(sky);
                changed = true;
            }
        }
        if (sky.sunDirect == null || sky.sunDirect.Length < 2)
            EditorGUILayout.HelpBox("Not baked: press Bake the sky again.", MessageType.Warning);
        if (changed)
        {
            EditorUtility.SetDirty(sky);
            ClearwaterSkySetup.Show(sky);
        }
    }
}
