using UnityEditor;
using UnityEngine;

/// <summary>Inspector for ClearwaterPool: its settings, whether it needs baking, what the bake found wrong (marked in
/// the Scene view), and Bake.</summary>
[CustomEditor(typeof(ClearwaterPool)), CanEditMultipleObjects]
public class ClearwaterPoolEditor : Editor
{
    static readonly Color NoteColor = new Color(1f, 0.3f, 0.25f);
    static GUIStyle _noteLabel;
    static GUIStyle NoteLabel => _noteLabel ??= new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = NoteColor } };

    public override void OnInspectorGUI()
    {
        EditorGUILayout.HelpBox("Put this object at the pool's water height, the pool's own meshes (walls and floor) under " +
            "Basin, and Bake. The water shows where the basin is below the surface.", MessageType.None);
        DrawDefaultInspector();
        if (targets.Length > 1)
        {
            if (GUILayout.Button("Bake these pools", GUILayout.Height(28)))
                foreach (var t in targets) ClearwaterPoolBake.Bake((ClearwaterPool)t);
            return;
        }
        var pool = (ClearwaterPool)target;
        if (pool.bakedHash != ClearwaterPoolBake.Hash(pool))
            EditorGUILayout.HelpBox("The pool has changed since it was last baked.", MessageType.Warning);
        if (pool.bakeNotes != null)
            for (int k = 0; k < pool.bakeNotes.Length; k++)
            {
                var note = pool.bakeNotes[k];
                bool marked = note.at != null && note.at.Length > 0;
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.HelpBox((marked ? $"{k + 1}. " : "") + note.text + ".", MessageType.Warning);
                    if (marked && GUILayout.Button("Show", GUILayout.Width(48), GUILayout.Height(38)))
                    {
                        var b = new Bounds(note.at[0], Vector3.one * 6f);
                        foreach (var p in note.at) b.Encapsulate(p);
                        SceneView.lastActiveSceneView?.Frame(b, false);
                    }
                }
            }
        if (GUILayout.Button("Bake", GUILayout.Height(28))) ClearwaterPoolBake.Bake(pool);
        EditorGUILayout.HelpBox("The sea's walkable ground is cut round the pools when the coast is baked (Coast > Bake " +
            "bakes the pools too).", MessageType.None);
    }

    void OnSceneGUI()
    {
        var pool = (ClearwaterPool)target;
        if (pool.bakeNotes == null || Event.current.type != EventType.Repaint) return;
        for (int k = 0; k < pool.bakeNotes.Length; k++)
        {
            var at = pool.bakeNotes[k].at;
            if (at == null) continue;
            Handles.color = NoteColor;
            foreach (var p in at)
            {
                float r = HandleUtility.GetHandleSize(p) * 0.25f;
                Handles.DrawWireDisc(p, Vector3.up, r);
                Handles.DrawWireDisc(p, Vector3.up, r * 0.6f);
                Handles.Label(p + Vector3.up * r, (k + 1).ToString(), NoteLabel);
            }
        }
    }
}
