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

    /// <summary>A panel in the world for the sky (ClearwaterSkyPanel): the hour, the clouds and the length of the day on
    /// sliders, the day going by on a toggle; anyone can use it and what they set goes to everyone. Placed in front of the
    /// spawn, facing it; one made before is replaced where it stands.</summary>
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

        // in front of the spawn, at chest height, facing it
        Vector3 at = Vector3.zero; Quaternion facing = Quaternion.identity;
        var desc = Object.FindObjectOfType<VRC.SDK3.Components.VRCSceneDescriptor>();
        if (desc != null && desc.spawns != null && desc.spawns.Length > 0 && desc.spawns[0] != null)
        {
            at = desc.spawns[0].position; facing = Quaternion.Euler(0f, desc.spawns[0].eulerAngles.y, 0f);
        }
        at += facing * new Vector3(0f, 1.3f, 1.5f);
        Transform parent = null;
        var old = Object.FindObjectOfType<ClearwaterSkyPanel>(true);
        if (old != null)
        {
            at = old.transform.position; facing = old.transform.rotation; parent = old.transform.parent;
            Undo.DestroyObjectImmediate(old.gameObject);
        }
        var root = new GameObject(PanelName);
        Undo.RegisterCreatedObjectUndo(root, "Clearwater sky panel");
        root.transform.SetParent(parent, false);
        root.transform.SetPositionAndRotation(at, facing);
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        root.AddComponent<GraphicRaycaster>();
        var rt = (RectTransform)root.transform;
        rt.sizeDelta = new Vector2(420f, 295f);
        rt.localScale = Vector3.one * 0.0015f; // (63 x 44 cm)
        root.AddComponent<VRC.SDK3.Components.VRCUiShape>();

        var res = new DefaultControls.Resources
        {
            standard = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"),
            background = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd"),
            knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd"),
            checkmark = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Checkmark.psd"),
        };
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var bg = root.AddComponent<Image>();
        bg.sprite = res.background; bg.type = Image.Type.Sliced; bg.color = new Color(0.08f, 0.10f, 0.14f, 0.82f);

        Text Label(string text, float y)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(root.transform, false);
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
            go.transform.SetParent(root.transform, false);
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
        var dayLabel = Label("A day in", -170f);
        var dayLength = MakeSlider("Day length", -202f, ClearwaterSkyPanel.DayLengths().Length - 1);
        dayLength.wholeNumbers = true;
        var toggleGo = DefaultControls.CreateToggle(res);
        toggleGo.name = "Day goes by";
        toggleGo.transform.SetParent(root.transform, false);
        var tr = (RectTransform)toggleGo.transform;
        tr.anchorMin = tr.anchorMax = new Vector2(0.5f, 1f);
        tr.sizeDelta = new Vector2(380f, 30f); tr.anchoredPosition = new Vector2(0f, -250f);
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
        panel.dayLengthSlider = dayLength; panel.dayLengthLabel = dayLabel;
        EditorUtility.SetDirty(panel);
        // the UI tells the panel's Udon program (VRChat lets UI events call SendCustomEvent)
        var udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(panel);
        UnityEventTools.AddStringPersistentListener(hour.onValueChanged, udon.SendCustomEvent, "OnHour");
        UnityEventTools.AddStringPersistentListener(clouds.onValueChanged, udon.SendCustomEvent, "OnClouds");
        UnityEventTools.AddStringPersistentListener(dayLength.onValueChanged, udon.SendCustomEvent, "OnDayLength");
        UnityEventTools.AddStringPersistentListener(toggle.onValueChanged, udon.SendCustomEvent, "OnCycle");
        hour.value = sky.timeOfDay; toggle.isOn = sky.cycle;
        clouds.value = sky.controller != null ? sky.controller.CloudCover() : 0f;
        dayLength.value = ClearwaterSkyPanel.NearestDayLength(sky.dayMinutes);
        Selection.activeGameObject = root;
        EditorSceneManager.MarkSceneDirty(root.scene);
        Debug.Log("[Clearwater] Sky control panel added in front of the spawn: move it where you like.");
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
