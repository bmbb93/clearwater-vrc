using UnityEditor;
using UnityEngine;

/// <summary>Inspector for the Clearwater sky material: its clouds are copied straight to the water and the seabed
/// (they reflect and refract the same sky), so what you set shows everywhere at once.</summary>
public class ClearwaterSkyGUI : ShaderGUI
{
    static readonly string[] Props = { "_CloudCover", "_CloudSize", "_CloudSpeed", "_CloudDir" };

    public override void OnGUI(MaterialEditor editor, MaterialProperty[] props)
    {
        EditorGUI.BeginChangeCheck();
        base.OnGUI(editor, props);
        bool changed = EditorGUI.EndChangeCheck();
        foreach (Object t in editor.targets)
        {
            var sky = t as Material;
            if (sky == null) continue;
            int n = CopyClouds(sky, changed);
            if (!changed && n < 0)
                EditorGUILayout.HelpBox("The water or the seabed has other clouds than this sky.", MessageType.Warning);
        }
        EditorGUILayout.HelpBox("Clouds drift while the world runs (in the Scene view, turn on Always Refresh to see it).",
            MessageType.None);
    }

    /// <summary>Copies the sky's clouds to the materials of every Clearwater in the open scenes that uses this sky.
    /// With apply off it only checks: -1 if any differ.</summary>
    public static int CopyClouds(Material sky, bool apply)
    {
        int n = 0;
        foreach (var ctl in Object.FindObjectsOfType<ClearwaterController>(true))
        {
            if (ctl.skyMaterial != sky) continue;
            foreach (var m in new[] { ctl.waterMaterial, ctl.seabedMaterial })
            {
                if (m == null) continue;
                foreach (var p in Props)
                {
                    if (!m.HasProperty(p) || Mathf.Approximately(m.GetFloat(p), sky.GetFloat(p))) continue;
                    if (!apply) return -1;
                    Undo.RecordObject(m, "Clearwater clouds");
                    m.SetFloat(p, sky.GetFloat(p));
                    EditorUtility.SetDirty(m);
                    n++;
                }
            }
        }
        return n;
    }
}
