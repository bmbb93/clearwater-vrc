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
    static readonly Color LineColor = new Color(0.2f, 0.85f, 1f);
    static readonly Color SeaColor = new Color(0.25f, 0.55f, 1f);
    static readonly Color AreaColor = new Color(1f, 0.85f, 0.3f, 0.7f);
    static readonly Color GroundColor = new Color(0.5f, 1f, 0.5f, 0.6f);

    public override void OnInspectorGUI()
    {
        var coast = (ClearwaterCoast)target;
        serializedObject.Update();
        EditorGUILayout.HelpBox(
            "Draw the waterline in the Scene view: drag the points, click + on a segment to add a point, select one and " +
            "press Delete to remove it. The sea is on the side of the arrows (left of the line's direction). " +
            "Press Bake after changes.", MessageType.None);

        EditorGUILayout.PropertyField(serializedObject.FindProperty("points"), true);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("closed"));

        EditorGUILayout.Space();
        var section = serializedObject.FindProperty("section");
        EditorGUILayout.PropertyField(section, new GUIContent("Cross-section"));
        if (section.enumValueIndex == (int)ClearwaterCoast.Section.GentleBeach)
        {
            foreach (var p in new[] { "shallowDepth", "shallowSlope", "shelfSlope", "deepDepth", "deepStart", "beachSlope", "landHeight" })
                EditorGUILayout.PropertyField(serializedObject.FindProperty(p));
        }
        else EditorGUILayout.PropertyField(serializedObject.FindProperty("curve"), GUILayout.Height(60));

        EditorGUILayout.Space();
        foreach (var p in new[] { "areaSize", "resolution", "groundHalfSize", "groundStep" })
            EditorGUILayout.PropertyField(serializedObject.FindProperty(p));
        serializedObject.ApplyModifiedProperties();

        DrawSection(coast);

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Reverse direction (flip the sea side)"))
            {
                Undo.RecordObject(coast, "Reverse coast");
                System.Array.Reverse(coast.points);
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

        // the line, with arrows towards the sea
        for (int k = 0; k < segs; k++)
        {
            Vector3 a = w[k], b = w[(k + 1) % n];
            Handles.color = LineColor;
            Handles.DrawAAPolyLine(5f, a, b);
            Vector3 d = b - a; d.y = 0;
            if (d.sqrMagnitude < 1e-6f) continue;
            Vector3 sea = new Vector3(-d.z, 0, d.x).normalized; // left of the direction, seen from above
            Vector3 mid = (a + b) * 0.5f;
            float s = HandleUtility.GetHandleSize(mid);
            Handles.color = SeaColor;
            Handles.ArrowHandleCap(0, mid, Quaternion.LookRotation(sea), s * 0.6f, EventType.Repaint);
            // "+": insert a point here
            Handles.color = Color.white;
            if (Handles.Button(mid + d.normalized * s * 0.25f, Quaternion.identity, s * 0.06f, s * 0.09f, Handles.DotHandleCap))
            {
                Undo.RecordObject(coast, "Add coast point");
                var list = new System.Collections.Generic.List<Vector3>(pts);
                list.Insert(k + 1, tf.InverseTransformPoint(mid));
                coast.points = list.ToArray();
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

        // Delete removes the selected point instead of the object (a line keeps at least two points). The editor
        // sends Delete as a command: claim it in validation, act on execution.
        var e = Event.current;
        if ((e.type == EventType.ValidateCommand || e.type == EventType.ExecuteCommand) &&
            (e.commandName == "SoftDelete" || e.commandName == "Delete") && _selected >= 0 && _selected < n && n > 2)
        {
            if (e.type == EventType.ExecuteCommand)
            {
                Undo.RecordObject(coast, "Remove coast point");
                var list = new System.Collections.Generic.List<Vector3>(pts);
                list.RemoveAt(_selected);
                coast.points = list.ToArray();
                _selected = -1;
            }
            e.Use();
        }
    }
}
