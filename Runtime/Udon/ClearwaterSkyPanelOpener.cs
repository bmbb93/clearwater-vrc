using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// Something to use (a pebble on the beach, say) that shows the sky panel above it, facing the viewer, a little below
/// their eyes, or puts it away; the panel goes away by itself when the viewer walks off. For the viewer alone: the others' panels stay as they are.
/// Tools > Clearwater > Add Sky Control Panel puts one on the beach by the panel.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class ClearwaterSkyPanelOpener : UdonSharpBehaviour
{
    [Tooltip("The panel it shows (the Sky Control Panel's root)")]
    public GameObject panel;
    [Tooltip("The panel goes away when the viewer is this far from it (metres); 0: it stays until used again")]
    public float closeDistance = 6f;
    [Tooltip("Show the panel above this, facing the viewer; off: where it was put")]
    public bool showAbove = true;

    public override void Interact()
    {
        if (panel == null) return;
        if (panel.activeSelf) { panel.SetActive(false); return; }
        VRCPlayerApi me = Networking.LocalPlayer;
        if (showAbove && me != null)
        {
            Vector3 eye = me.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
            Vector3 at = transform.position;
            at.y = Mathf.Max(eye.y - 0.3f, at.y + 0.4f);
            Vector3 away = at - eye;
            away.y = 0f;
            if (away.sqrMagnitude > 1e-4f) panel.transform.SetPositionAndRotation(at, Quaternion.LookRotation(away, Vector3.up));
            else panel.transform.position = at;
        }
        panel.SetActive(true);
    }

    void Update()
    {
        if (panel == null || closeDistance <= 0f || !panel.activeSelf) return;
        VRCPlayerApi me = Networking.LocalPlayer;
        if (me != null && Vector3.Distance(me.GetPosition(), panel.transform.position) > closeDistance) panel.SetActive(false);
    }
}
