using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common;

/// <summary>
/// Something to use (a pebble on the beach, say) that shows the sky panel above it, facing the viewer, a little below
/// their eyes, or puts it away; the panel goes away by itself when the viewer walks off. It can also be called up
/// anywhere: in VR a double tap of the left trigger shows it over and beyond the left hand, where it then stays (it went
/// with the hand, which made it hard to touch) until the viewer moves off from it; on a desktop a key (Tab) shows it in
/// front of the view. The same again puts it away. For the viewer alone: the others' panels stay as
/// they are. Tools > Clearwater > Add Sky Control Panel puts one on the beach by the panel.
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

    [Header("Called up anywhere")]
    [Tooltip("A double tap of the left trigger (VR) or the key below (desktop) shows the panel by the viewer, wherever they are")]
    public bool callAnywhere = true;
    [Tooltip("The desktop key that shows the panel in front of the view")]
    public KeyCode desktopKey = KeyCode.Tab;
    [Tooltip("The two taps of the left trigger come within this (seconds)")]
    public float doubleTapTime = 0.4f;
    [Tooltip("The panel's size over the hand, as a share of its size in the world")]
    [Range(0.2f, 1f)] public float handSize = 0.55f;
    [Tooltip("How far beyond the hand, away from the eyes, it shows (metres): at the hand, it is too close to work")]
    public float handReach = 0.25f;
    [Tooltip("The panel called up by the hand goes away when the viewer's eyes are this far from it (metres); 0: it stays until called again")]
    public float handCloseDistance = 1.5f;
    [Tooltip("How far in front of the eyes it shows on a desktop (metres)")]
    public float desktopDistance = 1.2f;

    const int InWorld = 0, OnHand = 1, InFront = 2;
    int _mode;
    Vector3 _worldScale;
    float _lastTap = -10f;

    void Start()
    {
        if (panel != null) _worldScale = panel.transform.localScale;
    }

    public override void Interact()
    {
        if (panel == null) return;
        if (panel.activeSelf && _mode == InWorld) { panel.SetActive(false); return; }
        VRCPlayerApi me = Networking.LocalPlayer;
        Place(InWorld);
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

    public override void InputUse(bool value, UdonInputEventArgs args)
    {
        if (!value || !callAnywhere || args.handType != HandType.LEFT) return;
        VRCPlayerApi me = Networking.LocalPlayer;
        if (me == null || !me.IsUserInVR()) return;
        if (Time.time - _lastTap <= doubleTapTime)
        {
            _lastTap = -10f;
            ToggleOnHand();
        }
        else _lastTap = Time.time;
    }

    /// <summary>Shows the panel over the viewer's left hand, following it (or puts it away if it is there).</summary>
    public void ToggleOnHand()
    {
        if (panel == null) return;
        if (panel.activeSelf && _mode == OnHand) { panel.SetActive(false); return; }
        Place(OnHand);
        PlaceByHand();
        panel.SetActive(true);
    }

    /// <summary>Shows the panel in front of the viewer's eyes, level, where it then stays (or puts it away if it is
    /// there).</summary>
    public void ToggleInFront()
    {
        if (panel == null) return;
        if (panel.activeSelf && _mode == InFront) { panel.SetActive(false); return; }
        VRCPlayerApi me = Networking.LocalPlayer;
        if (me == null) return;
        Place(InFront);
        VRCPlayerApi.TrackingData head = me.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
        Vector3 fwd = head.rotation * Vector3.forward;
        fwd.y = 0f;
        fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
        // a little below the eyes, where the view's centre (the desktop pointer) reaches all of it
        panel.transform.SetPositionAndRotation(head.position + fwd * desktopDistance - Vector3.up * 0.1f, Quaternion.LookRotation(fwd, Vector3.up));
        panel.SetActive(true);
    }

    // the panel's size for how it is shown: smaller over the hand
    void Place(int mode)
    {
        _mode = mode;
        if (_worldScale == Vector3.zero) _worldScale = panel.transform.localScale;
        panel.transform.localScale = mode == OnHand ? _worldScale * handSize : _worldScale;
    }

    // over the left hand, a hand's width above it and a little beyond it (away from the eyes), turned to the eyes; it stays
    // there (it used to go with the hand every frame: the panel moved away under the other hand's pointer)
    void PlaceByHand()
    {
        VRCPlayerApi me = Networking.LocalPlayer;
        if (me == null) return;
        Vector3 hand = me.GetTrackingData(VRCPlayerApi.TrackingDataType.LeftHand).position;
        Vector3 eye = me.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
        Vector3 out_ = hand - eye;
        out_.y = 0f;
        Vector3 at = hand + Vector3.up * 0.16f + (out_.sqrMagnitude > 1e-4f ? out_.normalized * handReach : Vector3.zero);
        Vector3 away = at - eye;
        if (away.sqrMagnitude > 1e-4f) panel.transform.SetPositionAndRotation(at, Quaternion.LookRotation(away, Vector3.up));
        else panel.transform.position = at;
    }

    void Update()
    {
        if (panel == null) return;
        VRCPlayerApi me = Networking.LocalPlayer;
        if (callAnywhere && me != null && !me.IsUserInVR() && Input.GetKeyDown(desktopKey)) ToggleInFront();
        if (!panel.activeSelf || me == null) return;
        if (_mode == OnHand)
        {
            // (from the eyes: the feet are already a metre and a half from a panel just called up)
            Vector3 eye = me.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
            if (handCloseDistance > 0f && Vector3.Distance(eye, panel.transform.position) > handCloseDistance) panel.SetActive(false);
            return;
        }
        if (closeDistance > 0f && Vector3.Distance(me.GetPosition(), panel.transform.position) > closeDistance) panel.SetActive(false);
    }
}
