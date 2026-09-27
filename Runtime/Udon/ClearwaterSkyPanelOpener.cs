using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// Something to use (a pebble on the beach, say) that shows the sky panel, or puts it away; the panel goes away by
/// itself when the viewer walks off. For the viewer alone: the others' panels stay as they are.
/// Tools > Clearwater > Add Sky Control Panel puts one on the beach by the panel.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class ClearwaterSkyPanelOpener : UdonSharpBehaviour
{
    [Tooltip("The panel it shows (the Sky Control Panel's root)")]
    public GameObject panel;
    [Tooltip("The panel goes away when the viewer is this far from it (metres); 0: it stays until used again")]
    public float closeDistance = 6f;

    public override void Interact()
    {
        if (panel != null) panel.SetActive(!panel.activeSelf);
    }

    void Update()
    {
        if (panel == null || closeDistance <= 0f || !panel.activeSelf) return;
        VRCPlayerApi me = Networking.LocalPlayer;
        if (me != null && Vector3.Distance(me.GetPosition(), panel.transform.position) > closeDistance) panel.SetActive(false);
    }
}
