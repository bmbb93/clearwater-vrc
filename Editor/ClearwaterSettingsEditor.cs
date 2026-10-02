using UdonSharpEditor;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The sky and wave panel's settings in one place: what the world starts with and the panel's Reset all goes back
/// to. The hour, the day going by and its length are the sky's own (shown here, set on it); the clouds and the waves
/// are this object's, written into the materials that hold them as they change, so the Scene view shows them too.
/// </summary>
[CustomEditor(typeof(ClearwaterSettings))]
public class ClearwaterSettingsEditor : Editor
{
    public override void OnInspectorGUI()
    {
        if (UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader(target)) return;
        var settings = (ClearwaterSettings)target;
        EditorGUILayout.HelpBox("What the world starts with, and the sky and wave panel's Reset all goes back to.", MessageType.None);

        // the sky's: the hour, the day going by and its length
        if (settings.sky != null)
        {
            EditorGUILayout.LabelField("Sky (set on the Clearwater Sky)", EditorStyles.boldLabel);
            var so = new SerializedObject(settings.sky);
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(so.FindProperty("timeOfDay"), new GUIContent("Time"));
            EditorGUILayout.PropertyField(so.FindProperty("cycle"), new GUIContent("Day goes by"));
            EditorGUILayout.PropertyField(so.FindProperty("dayMinutes"), new GUIContent("A day in (minutes)"));
            if (EditorGUI.EndChangeCheck())
            {
                so.ApplyModifiedProperties();
                ClearwaterSkySetup.Show(settings.sky);
            }
        }
        else EditorGUILayout.HelpBox("No Clearwater Sky set: the panel's time has nothing to go to.", MessageType.Warning);

        EditorGUILayout.Space();
        EditorGUI.BeginChangeCheck();
        DrawPropertiesExcluding(serializedObject, "m_Script");
        if (EditorGUI.EndChangeCheck())
        {
            serializedObject.ApplyModifiedProperties();
            UdonSharpEditorUtility.CopyProxyToUdon(settings);
            ToMaterials(settings);
        }
        if (settings.controller == null)
            EditorGUILayout.HelpBox("No Clearwater Controller set: the clouds and the waves have nothing to go to.", MessageType.Warning);
    }

    /// <summary>The start values into the materials that hold them (as the world applies them when it starts), so the
    /// Scene view shows the world as it will start.</summary>
    internal static void ToMaterials(ClearwaterSettings settings)
    {
        var ctl = settings.controller;
        if (ctl == null) return;
        var cloudy = new System.Collections.Generic.List<Material> { ctl.userBeachMaterial, ctl.cloudDomeMaterial };
        if (ctl.poolWaterMaterials != null) cloudy.AddRange(ctl.poolWaterMaterials);
        SetAll(Mats(ctl.skyMaterial, ctl.waterMaterial, ctl.seabedMaterial, cloudy.ToArray()), "Clouds",
            m => { m.SetFloat("_CloudCover", settings.clouds); m.SetFloat("_CloudSpeed", settings.cloudDrift); });
        SetAll(Mats(ctl.waterMaterial, ctl.seabedMaterial, ctl.underwaterMaterial, new[] { ctl.userBeachMaterial }), "Shore waves",
            m => m.SetFloat("_SwashHeight", settings.shoreWaves));
        SetAll(Mats(ctl.waterMaterial, ctl.seabedMaterial, ctl.underwaterMaterial, new[] { ctl.avatarCausticsMaterial }), "Ripples",
            m => m.SetFloat("_Calm", 1f - settings.ripples));
    }

    static Material[] Mats(Material a, Material b, Material c, Material[] more)
    {
        var list = new System.Collections.Generic.List<Material> { a, b, c };
        if (more != null) list.AddRange(more);
        list.RemoveAll(m => m == null);
        return list.ToArray();
    }

    static void SetAll(Material[] mats, string what, System.Action<Material> set)
    {
        if (mats.Length == 0) return;
        Undo.RecordObjects(mats, what);
        foreach (var m in mats) { set(m); EditorUtility.SetDirty(m); }
    }
}
