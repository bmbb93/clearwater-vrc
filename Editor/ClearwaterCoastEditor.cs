using UnityEditor;
using UnityEngine;

/// <summary>Inspector for ClearwaterStamp: what the mode does, and a shortcut to bake the coast.</summary>
[CustomEditor(typeof(ClearwaterStamp)), CanEditMultipleObjects]
public class ClearwaterStampEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var s = (ClearwaterStamp)target;
        EditorGUILayout.HelpBox(s.mode == ClearwaterStamp.Mode.Obstacle
            ? "Obstacle: a prop in the water. Waves break on it and foam round it; it is drawn as itself."
            : "Brush: its top surface becomes the ground (seabed, walkable ground, waves). Hidden and not uploaded; " +
              "shown as a wireframe.", MessageType.None);
        var coast = Object.FindObjectOfType<ClearwaterCoast>();
        if (coast == null) { EditorGUILayout.HelpBox("No ClearwaterCoast in the scene.", MessageType.Warning); return; }
        bool stale = coast.bakedHash != ClearwaterCoastBake.Hash(coast);
        if (stale) EditorGUILayout.HelpBox("The coast needs baking again (stamps changed).", MessageType.Warning);
        if (GUILayout.Button("Bake the coast")) ClearwaterCoastBake.Bake(coast);
    }
}

/// <summary>
/// Inspector and Scene-view editing for ClearwaterCoast: drag the waterline's points, click "+" on a segment to add
/// one, select a point and press Delete to remove it; arrows show the sea side. The inspector shows the
/// cross-section and bakes the coast.
/// </summary>
[CustomEditor(typeof(ClearwaterCoast))]
public class ClearwaterCoastEditor : Editor
{
    int _selected = -1;
    int _preset;
    int _applyPreset = -1; // chosen this frame, applied after the serialized properties
    bool _resetLine;       // (likewise)

    // Cross-section presets: the floor's height (m, the water surface is 0) against the distance from the waterline
    // (m, + out to sea). null = the Gentle Beach numbers at their defaults. The gentle ones share its beach face (1:10:
    // 0.6 m up over 6 m, down to 0.35 m deep 3.5 m out), which also sets how far and how slowly the swash runs.
    static readonly (string name, Vector2[] keys)[] Presets =
    {
        ("Gentle beach (numbers)", null),
        ("Steep beach", new[] { new Vector2(-15, 1.4f), new Vector2(-5, 1.1f), new Vector2(-1.5f, 0.4f), new Vector2(0, 0),
            new Vector2(2, -0.8f), new Vector2(8, -2.4f), new Vector2(20, -4.2f), new Vector2(40, -5f), new Vector2(80, -5f) }),
        ("Beach with a sandbar", new[] { new Vector2(-14, 0.6f), new Vector2(-7, 0.6f), new Vector2(-3, 0.29f), new Vector2(0, 0), new Vector2(3.5f, -0.35f),
            new Vector2(10, -0.9f), new Vector2(20, -1.5f), new Vector2(28, -0.9f), new Vector2(33, -0.45f), new Vector2(38, -0.9f),
            new Vector2(50, -2.4f), new Vector2(70, -3.5f), new Vector2(100, -3.5f) }),
        ("Lagoon (wide shallow flat)", new[] { new Vector2(-10, 0.5f), new Vector2(-2, 0.5f), new Vector2(0, 0), new Vector2(3, -0.45f),
            new Vector2(20, -0.6f), new Vector2(55, -0.75f), new Vector2(62, -1.5f), new Vector2(72, -4f), new Vector2(100, -4.5f) }),
        ("Lake shore (use with Shore waves off)", new[] { new Vector2(-12, 0.5f), new Vector2(-2, 0.3f), new Vector2(0, 0),
            new Vector2(1.5f, -0.2f), new Vector2(8, -0.7f), new Vector2(25, -1.8f), new Vector2(45, -2.4f), new Vector2(80, -2.5f) }),
        ("Rocky drop-off", new[] { new Vector2(-12, 1.8f), new Vector2(-3, 1.3f), new Vector2(-1, 0.5f), new Vector2(0, 0),
            new Vector2(1.5f, -1f), new Vector2(4, -3f), new Vector2(10, -5.5f), new Vector2(30, -6.5f), new Vector2(80, -6.5f) }),
        ("Gentle beach under hills (land seen from afar)", new[] { new Vector2(-400, 40), new Vector2(-200, 28), new Vector2(-80, 10),
            new Vector2(-25, 2.5f), new Vector2(-10, 0.85f), new Vector2(-6, 0.6f), new Vector2(0, 0), new Vector2(3.5f, -0.35f),
            new Vector2(30, -1.4f), new Vector2(48, -3.45f), new Vector2(80, -3.45f) }),
    };

    static void ApplyPreset(ClearwaterCoast coast, int index)
    {
        Undo.RecordObject(coast, "Cross-section preset");
        var keys = Presets[index].keys;
        if (keys == null)
        {
            // the component's own defaults
            var tmp = new GameObject("_defaults") { hideFlags = HideFlags.HideAndDontSave };
            var d = tmp.AddComponent<ClearwaterCoast>();
            coast.section = ClearwaterCoast.Section.GentleBeach;
            coast.shallowDepth = d.shallowDepth; coast.shallowSlope = d.shallowSlope; coast.shelfSlope = d.shelfSlope;
            coast.deepDepth = d.deepDepth; coast.deepStart = d.deepStart; coast.beachSlope = d.beachSlope; coast.landHeight = d.landHeight;
            Object.DestroyImmediate(tmp);
        }
        else
        {
            coast.section = ClearwaterCoast.Section.Curve;
            var k = new Keyframe[keys.Length];
            for (int i = 0; i < keys.Length; i++) k[i] = new Keyframe(keys[i].x, keys[i].y);
            coast.curve = new AnimationCurve(k);
            for (int i = 0; i < coast.curve.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(coast.curve, i, AnimationUtility.TangentMode.ClampedAuto);
                AnimationUtility.SetKeyRightTangentMode(coast.curve, i, AnimationUtility.TangentMode.ClampedAuto);
            }
        }
        EditorUtility.SetDirty(coast);
    }

    static void ResetLine(ClearwaterCoast coast)
    {
        Undo.RecordObject(coast, "Reset coast line");
        var tmp = new GameObject("_defaults") { hideFlags = HideFlags.HideAndDontSave };
        var d = tmp.AddComponent<ClearwaterCoast>();
        coast.points = (Vector3[])d.points.Clone();
        coast.closed = d.closed;
        coast.shape = d.shape;
        coast.handleIn = null; coast.handleOut = null; coast.corner = null;
        Object.DestroyImmediate(tmp);
        EditorUtility.SetDirty(coast);
    }
    static readonly Color LineColor = new Color(0.2f, 0.85f, 1f);
    static readonly Color SeaColor = new Color(0.25f, 0.55f, 1f);
    static readonly Color AreaColor = new Color(1f, 0.85f, 0.3f, 0.7f);
    static readonly Color GroundColor = new Color(0.5f, 1f, 0.5f, 0.6f);
    static readonly Color SeaAreaColor = new Color(0.4f, 0.7f, 1f, 0.5f);
    static readonly Color HandleColor = new Color(1f, 0.55f, 0.2f);

    public override void OnInspectorGUI()
    {
        var coast = (ClearwaterCoast)target;
        serializedObject.Update();
        EditorGUILayout.HelpBox(
            "Draw the waterline in the Scene view: drag the points, click + on a segment to add a point, select one and " +
            "press Delete to remove it. The sea is on the side of the arrows (left of the line's direction). " +
            "Line = Handles: the selected point and its neighbours show handles (squares) that set the curve; " +
            "Alt-drag a handle to make a corner. Press Bake after changes.", MessageType.None);

        EditorGUILayout.PropertyField(serializedObject.FindProperty("points"), true);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("closed"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("shoreWaves"), new GUIContent("Shore waves"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("shape"), new GUIContent("Line"));
        if (GUILayout.Button("Reset line") && EditorUtility.DisplayDialog("Clearwater",
                "Put the waterline back to the default straight line (3 points, 200 m)? The cross-section and the " +
                "other settings stay. (Undo brings the line back.)", "Reset line", "Cancel"))
            _resetLine = true;

        EditorGUILayout.Space();
        var section = serializedObject.FindProperty("section");
        EditorGUILayout.PropertyField(section, new GUIContent("Cross-section"));
        using (new EditorGUILayout.HorizontalScope())
        {
            var names = new string[Presets.Length];
            for (int i = 0; i < names.Length; i++) names[i] = Presets[i].name;
            _preset = EditorGUILayout.Popup("Preset", _preset, names);
            if (GUILayout.Button("Apply", GUILayout.Width(60))) _applyPreset = _preset;
        }
        if (section.enumValueIndex == (int)ClearwaterCoast.Section.GentleBeach)
        {
            foreach (var p in new[] { "shallowDepth", "shallowSlope", "shelfSlope", "deepDepth", "deepStart", "beachSlope", "landHeight" })
                EditorGUILayout.PropertyField(serializedObject.FindProperty(p));
        }
        else EditorGUILayout.PropertyField(serializedObject.FindProperty("curve"), GUILayout.Height(60));

        EditorGUILayout.Space();
        EditorGUILayout.PropertyField(serializedObject.FindProperty("seaSize"), new GUIContent("Sea size (m)"));
        foreach (var p in new[] { "areaSize", "resolution", "outerResolution", "groundHalfSize", "groundStep" })
            EditorGUILayout.PropertyField(serializedObject.FindProperty(p));
        serializedObject.ApplyModifiedProperties();
        if (_applyPreset >= 0) { ApplyPreset(coast, _applyPreset); _applyPreset = -1; GUIUtility.ExitGUI(); }
        if (_resetLine) { _resetLine = false; ResetLine(coast); _selected = -1; GUIUtility.ExitGUI(); }
        FarClipCheck(coast);

        if (coast.shape == ClearwaterCoast.LineShape.Handles)
        {
            coast.EnsureHandles();
            bool has = _selected >= 0 && _selected < coast.points.Length;
            EditorGUILayout.LabelField(has ? $"Point {_selected}: " + (coast.corner[_selected] ? "corner" : "smooth") : "Select a point in the Scene view", EditorStyles.miniBoldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!has))
                {
                    if (GUILayout.Button("Smooth point"))
                    {
                        Undo.RecordObject(coast, "Smooth point");
                        coast.corner[_selected] = false;
                        // line the handles up along their average direction, keeping their lengths
                        Vector3 hi = coast.handleIn[_selected], ho = coast.handleOut[_selected];
                        Vector3 dir = (ho.normalized - hi.normalized).normalized;
                        if (dir.sqrMagnitude < 1e-6f) dir = coast.AutoOut(_selected).normalized;
                        coast.handleOut[_selected] = dir * ho.magnitude;
                        coast.handleIn[_selected] = -dir * hi.magnitude;
                    }
                    if (GUILayout.Button("Corner point"))
                    {
                        Undo.RecordObject(coast, "Corner point");
                        coast.corner[_selected] = true;
                    }
                    if (GUILayout.Button("Auto handles"))
                    {
                        Undo.RecordObject(coast, "Auto handles");
                        coast.ResetHandles(_selected);
                    }
                }
                if (GUILayout.Button("All auto"))
                {
                    Undo.RecordObject(coast, "Auto handles");
                    coast.ResetHandles();
                }
            }
        }

        DrawSection(coast);

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Reverse direction (flip the sea side)"))
            {
                Undo.RecordObject(coast, "Reverse coast");
                System.Array.Reverse(coast.points);
                if (coast.handleIn != null && coast.handleOut != null && coast.corner != null &&
                    coast.handleIn.Length == coast.points.Length && coast.handleOut.Length == coast.points.Length)
                {
                    // walking the other way, each point's in and out handles swap
                    var hi = (Vector3[])coast.handleOut.Clone(); var ho = (Vector3[])coast.handleIn.Clone();
                    System.Array.Reverse(hi); System.Array.Reverse(ho); System.Array.Reverse(coast.corner);
                    coast.handleIn = hi; coast.handleOut = ho;
                }
            }
            if (coast.section == ClearwaterCoast.Section.Curve && GUILayout.Button("Curve from gentle beach"))
            {
                Undo.RecordObject(coast, "Curve from gentle beach");
                var probe = Object.Instantiate(coast); // evaluate the preset with the current numbers
                probe.section = ClearwaterCoast.Section.GentleBeach;
                var keys = new System.Collections.Generic.List<Keyframe>();
                foreach (float u in new[] { -10f, -3f, -2f, -1f, 0f, 0.7f, 1.4f, 5f, 15f, 25f, 32f, 38f, 44f, 50f, 80f })
                    keys.Add(new Keyframe(u, -ClearwaterCoastBake.SectionDepth(probe, u)));
                Object.DestroyImmediate(probe);
                coast.curve = new AnimationCurve(keys.ToArray());
                for (int i = 0; i < coast.curve.length; i++) AnimationUtility.SetKeyLeftTangentMode(coast.curve, i, AnimationUtility.TangentMode.ClampedAuto);
                for (int i = 0; i < coast.curve.length; i++) AnimationUtility.SetKeyRightTangentMode(coast.curve, i, AnimationUtility.TangentMode.ClampedAuto);
            }
        }

        bool stale = coast.bakedHash != ClearwaterCoastBake.Hash(coast);
        if (stale) EditorGUILayout.HelpBox("The coast has changed since it was last baked.", MessageType.Warning);
        if (GUILayout.Button(stale ? "Bake" : "Bake again", GUILayout.Height(28)))
            ClearwaterCoastBake.Bake(coast);
    }

    /// <summary>The world's reference camera (its far clip is every player's) has to reach across the sea.</summary>
    static void FarClipCheck(ClearwaterCoast coast)
    {
        var desc = Object.FindObjectOfType<VRC.SDK3.Components.VRCSceneDescriptor>();
        if (desc == null) return;
        var cam = desc.ReferenceCamera != null ? desc.ReferenceCamera.GetComponent<Camera>() : null;
        float need = Mathf.Round(Mathf.Max(coast.seaSize, 100f) * ClearwaterSetup.FarClipPerSeaSize);
        if (cam == null)
        {
            EditorGUILayout.HelpBox($"The world has no Reference Camera, so players get VRChat's default far clip (1000 m). " +
                $"For this sea, give it one with a far clip of {need} m.", MessageType.Warning);
            return;
        }
        if (cam.farClipPlane + 0.5f >= need) return;
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.HelpBox($"The Reference Camera's far clip ({cam.farClipPlane:0} m) is short of this sea: the far " +
                $"water and ground would be cut off. {need} m is needed.", MessageType.Warning);
            if (GUILayout.Button($"Set {need} m", GUILayout.Width(90), GUILayout.Height(38)))
            {
                Undo.RecordObject(cam, "Far clip");
                cam.farClipPlane = need;
                EditorUtility.SetDirty(cam);
            }
        }
    }

    /// <summary>A small side view of the section: the floor (sand) against the water surface (blue).</summary>
    static void DrawSection(ClearwaterCoast coast)
    {
        var rect = GUILayoutUtility.GetRect(10, 90, GUILayout.ExpandWidth(true));
        if (Event.current.type != EventType.Repaint) return;
        EditorGUI.DrawRect(rect, new Color(0.12f, 0.12f, 0.12f));
        var r = ClearwaterCoastBake.SectionRange(coast);
        const int N = 160;
        var h = new float[N];
        float hMin = 0, hMax = 0;
        for (int i = 0; i < N; i++)
        {
            h[i] = -ClearwaterCoastBake.SectionDepth(coast, Mathf.Lerp(r.x, r.y, i / (N - 1f)));
            hMin = Mathf.Min(hMin, h[i]); hMax = Mathf.Max(hMax, h[i]);
        }
        hMin -= 0.2f; hMax += 0.2f;
        float Y(float v) => Mathf.Lerp(rect.yMax - 4, rect.yMin + 4, Mathf.InverseLerp(hMin, hMax, v));
        float X(float u) => Mathf.Lerp(rect.xMin + 4, rect.xMax - 4, Mathf.InverseLerp(r.x, r.y, u));
        Handles.color = SeaColor;
        Handles.DrawLine(new Vector3(rect.xMin, Y(0)), new Vector3(rect.xMax, Y(0)));
        Handles.color = new Color(0.5f, 0.5f, 0.5f);
        Handles.DrawLine(new Vector3(X(0), rect.yMin), new Vector3(X(0), rect.yMax));
        var pts = new Vector3[N];
        for (int i = 0; i < N; i++) pts[i] = new Vector3(Mathf.Lerp(rect.xMin + 4, rect.xMax - 4, i / (N - 1f)), Y(h[i]));
        Handles.color = new Color(0.9f, 0.78f, 0.5f);
        Handles.DrawAAPolyLine(2f, pts);
        GUI.Label(new Rect(rect.xMin + 4, rect.yMin + 2, 300, 16),
            $"u {r.x:F0} .. {r.y:F0} m (0 = waterline, + out to sea), height {hMin + 0.2f:F2} .. {hMax - 0.2f:F2} m", EditorStyles.miniLabel);
    }

    void OnSceneGUI()
    {
        var coast = (ClearwaterCoast)target;
        var tf = coast.transform;
        var pts = coast.points;
        if (pts == null || pts.Length == 0) return;
        int n = pts.Length, segs = coast.closed ? n : n - 1;
        var w = new Vector3[n];
        for (int i = 0; i < n; i++) w[i] = tf.TransformPoint(ClearwaterCoast.Flat(pts[i]));

        // the bake area and the walkable ground (axis-aligned around the object)
        Handles.color = AreaColor;
        Handles.DrawWireCube(tf.position, new Vector3(coast.areaSize, 0, coast.areaSize));
        Handles.color = GroundColor;
        Handles.DrawWireCube(tf.position, new Vector3(2 * coast.groundHalfSize, 0, 2 * coast.groundHalfSize));
        // the sea (the coarse outer bake): around the water object
        var ctl = Object.FindObjectOfType<ClearwaterController>();
        if (ctl != null && ctl.water != null)
        {
            Handles.color = SeaAreaColor;
            float sea = Mathf.Max(coast.seaSize, coast.areaSize);
            Handles.DrawWireCube(new Vector3(ctl.water.position.x, tf.position.y, ctl.water.position.z), new Vector3(sea, 0, sea));
        }

        // the line as drawn (a smooth curve through the points, or straight segments), with arrows towards the sea;
        // a smooth line also shows its points joined straight, faintly
        if (coast.shape != ClearwaterCoast.LineShape.Straight && n > 1)
        {
            Handles.color = new Color(LineColor.r, LineColor.g, LineColor.b, 0.25f);
            for (int k = 0; k < segs; k++) Handles.DrawDottedLine(w[k], w[(k + 1) % n], 4f);
        }
        for (int k = 0; k < segs; k++)
        {
            const int N = 24;
            var seg = new Vector3[N + 1];
            for (int j = 0; j <= N; j++) seg[j] = tf.TransformPoint(coast.PointOn(k, j / (float)N));
            Handles.color = LineColor;
            Handles.DrawAAPolyLine(5f, seg);
            Vector3 mid = tf.TransformPoint(coast.PointOn(k, 0.5f));
            Vector3 d = tf.TransformPoint(coast.PointOn(k, 0.55f)) - tf.TransformPoint(coast.PointOn(k, 0.45f)); d.y = 0;
            if (d.sqrMagnitude < 1e-8f) continue;
            Vector3 sea = new Vector3(-d.z, 0, d.x).normalized; // left of the direction, seen from above
            float s = HandleUtility.GetHandleSize(mid);
            Handles.color = SeaColor;
            Handles.ArrowHandleCap(0, mid, Quaternion.LookRotation(sea), s * 0.6f, EventType.Repaint);
            // "+": insert a point here
            Handles.color = Color.white;
            if (Handles.Button(mid + d.normalized * s * 0.25f, Quaternion.identity, s * 0.06f, s * 0.09f, Handles.DotHandleCap))
            {
                Undo.RecordObject(coast, "Add coast point");
                coast.InsertPoint(k, 0.5f); // on the curve, without changing its shape
                _selected = k + 1;
                return;
            }
        }

        // the points: drag in the horizontal plane
        for (int i = 0; i < n; i++)
        {
            float s = HandleUtility.GetHandleSize(w[i]) * 0.1f;
            Handles.color = i == _selected ? Color.yellow : (i == 0 ? new Color(0.4f, 1f, 0.4f) : LineColor);
            EditorGUI.BeginChangeCheck();
            Vector3 moved = Handles.FreeMoveHandle(w[i], s, Vector3.zero, Handles.SphereHandleCap);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(coast, "Move coast point");
                moved.y = w[i].y;
                pts[i] = ClearwaterCoast.Flat(tf.InverseTransformPoint(moved));
                _selected = i;
            }
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 &&
                HandleUtility.DistanceToCircle(w[i], s) < 1f) _selected = i;
        }

        // Handles mode: the handles of the selected point and its neighbours (an open line's outer ends have none)
        if (coast.shape == ClearwaterCoast.LineShape.Handles && _selected >= 0 && _selected < n)
        {
            coast.EnsureHandles();
            for (int di = -1; di <= 1; di++)
            {
                int i = _selected + di;
                if (coast.closed) i = (i % n + n) % n; else if (i < 0 || i >= n) continue;
                for (int side = 0; side < 2; side++) // 0 = in, 1 = out
                {
                    if (!coast.closed && ((side == 0 && i == 0) || (side == 1 && i == n - 1))) continue;
                    Vector3 off = side == 0 ? coast.handleIn[i] : coast.handleOut[i];
                    Vector3 hw = tf.TransformPoint(ClearwaterCoast.Flat(pts[i] + off));
                    Handles.color = HandleColor;
                    Handles.DrawLine(w[i], hw);
                    float hs = HandleUtility.GetHandleSize(hw) * 0.07f;
                    EditorGUI.BeginChangeCheck();
                    Vector3 moved = Handles.FreeMoveHandle(hw, hs, Vector3.zero, Handles.RectangleHandleCap);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(coast, "Move coast handle");
                        moved.y = w[i].y;
                        Vector3 nOff = ClearwaterCoast.Flat(tf.InverseTransformPoint(moved)) - ClearwaterCoast.Flat(pts[i]);
                        if (Event.current.alt) coast.corner[i] = true; // Alt: break the pair, as in Illustrator
                        if (side == 0) coast.handleIn[i] = nOff; else coast.handleOut[i] = nOff;
                        if (!coast.corner[i] && nOff.sqrMagnitude > 1e-8f)
                        {
                            // a smooth point keeps its two handles in line (each keeps its own length)
                            if (side == 0) coast.handleOut[i] = -nOff.normalized * coast.handleOut[i].magnitude;
                            else coast.handleIn[i] = -nOff.normalized * coast.handleIn[i].magnitude;
                        }
                    }
                }
            }
        }

        // Delete removes the selected point instead of the object (a line keeps at least two points). The editor
        // sends Delete as a command: claim it in validation, act on execution.
        var e = Event.current;
        if ((e.type == EventType.ValidateCommand || e.type == EventType.ExecuteCommand) &&
            (e.commandName == "SoftDelete" || e.commandName == "Delete") && _selected >= 0 && _selected < n && n > 2)
        {
            if (e.type == EventType.ExecuteCommand)
            {
                Undo.RecordObject(coast, "Remove coast point");
                coast.RemovePoint(_selected);
                _selected = -1;
            }
            e.Use();
        }
    }
}
