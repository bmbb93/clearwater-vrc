using UdonSharp;
using UnityEngine;
using VRC.SDK3.Rendering;
using VRC.SDKBase;

/// <summary>
/// Something to use (a pebble on the beach, say) that shows the sky panel above it, facing the viewer, a little below
/// their eyes, or puts it away; the panel goes away by itself when the viewer walks off. It can also be called up
/// anywhere. In VR it rides on the left wrist (as VizVid's Overlay Control does: the same hand rotation and offset) and
/// shows while the viewer looks at it; the other hand's pointer works it. On a desktop it shows while a key (Tab) is
/// held, in the middle of the view, as far off as fills desktopFill of it: VRChat frees the cursor while Tab is held,
/// which is how it is clicked. In VRChat's third person view Tab turns the camera instead and frees no cursor, so there
/// the key shows a hint (use first person, F5) in the middle of the view while it is held. Either way it fades in and
/// out, growing a little as it comes. While the pebble's panel shows in the world, the wrist and the key leave it
/// there. For the viewer alone: the others' panels stay as they are. Tools > Clearwater > Add Sky Control Panel puts
/// one on the beach by the panel.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class ClearwaterSkyPanelOpener : UdonSharpBehaviour
{
    [Tooltip("The panel it shows (the Sky Control Panel's root)")]
    public GameObject panel;
    [Tooltip("The panel's CanvasGroup: it fades in and out with it (none: it comes and goes at once)")]
    public CanvasGroup fade;
    [Tooltip("The panel goes away when the viewer is this far from it (metres); 0: it stays until used again")]
    public float closeDistance = 6f;
    [Tooltip("Show the panel above this, facing the viewer; off: where it was put")]
    public bool showAbove = true;
    [Tooltip("How long it takes to come and to go (seconds); it grows from smallScale as it comes, and shrinks to it as it goes")]
    public float showTime = 0.18f, hideTime = 0.15f;
    public float smallScale = 0.92f;

    [Header("Called up anywhere")]
    [Tooltip("The panel on the left wrist (VR) and while the key below is held (desktop), wherever the viewer is")]
    public bool callAnywhere = true;
    [Tooltip("The panel's size on the wrist, as a share of its size in the world")]
    [Range(0.1f, 1f)] public float wristSize = 0.3f;
    [Tooltip("How far from the hand along the panel's offset direction (metres)")]
    public float wristOffset = 0.06f;
    [Tooltip("Where the panel sits in the hand's frame, after the wrist rotation (metres)")]
    public Vector3 wristShift = Vector3.zero;
    [Tooltip("On the wrist it shows while it is this near the gaze (degrees)")]
    public float lookAngle = 35f;
    [Tooltip("The desktop key: the panel shows while it is held (VRChat frees the cursor while Tab is held, to click it)")]
    public KeyCode desktopKey = KeyCode.Tab;
    [Tooltip("How much of the desktop view the panel fills, across or up (whichever it meets first)")]
    [Range(0.2f, 1f)] public float desktopFill = 0.64f;
    [Tooltip("The view counts as third person when the screen camera is this far from the head (metres)")]
    public float thirdPersonDistance = 0.35f;
    [Tooltip("Shown in third person instead of the panel, while the key is held: the panel works in first person")]
    public GameObject hint;
    public CanvasGroup hintFade;
    [Tooltip("How far in front of the view the hint shows (metres)")]
    public float hintDistance = 0.45f;

    const int InWorld = 0, OnWrist = 1, InView = 2;
    int _mode;
    bool _shown; // (where it is going; _t: how far it has come)
    float _t, _hintT;
    bool _waitForRelease; // (the key held in third person: the hint, not the panel, till it is let go)
    Vector3 _worldScale;
    VRCPlayerApi _me;
    bool _vr;

    // VizVid's Overlay Control: the canvas's rotation on the left hand, and the way it is pushed off the hand
    Quaternion leftHandRotation = Quaternion.Euler(-90f, -45f, 0f);
    Vector3 offsetDirection = new Vector3(0f, 1f, -1f);

    void Start()
    {
        _me = Networking.LocalPlayer;
        if (Utilities.IsValid(_me)) _vr = _me.IsUserInVR();
        if (panel == null) return;
        _worldScale = panel.transform.localScale;
        if (panel.activeSelf) { _shown = true; _t = 1f; } // (left showing in the scene: it stays, in the world)
        if (hint != null) hint.SetActive(false);
    }

    public override void Interact()
    {
        if (panel == null) return;
        if (_shown && _mode == InWorld) { _shown = false; return; }
        _mode = InWorld;
        if (showAbove && Utilities.IsValid(_me))
        {
            Vector3 eye = _me.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
            Vector3 at = transform.position;
            at.y = Mathf.Max(eye.y - 0.3f, at.y + 0.4f);
            Vector3 away = at - eye;
            away.y = 0f;
            if (away.sqrMagnitude > 1e-4f) panel.transform.SetPositionAndRotation(at, Quaternion.LookRotation(away, Vector3.up));
            else panel.transform.position = at;
        }
        _shown = true;
    }

    // the panel in the world, shown by the pebble (and not gone yet)
    bool InWorldNow() { return _mode == InWorld && (_shown || _t > 0f); }

    // VRChat's third person view: the screen camera off the head (Tab turns that camera there, and frees no cursor)
    bool InThirdPerson()
    {
        var head = _me.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
        return Vector3.Distance(VRCCameraSettings.ScreenCamera.Position, head.position) > thirdPersonDistance;
    }

    void Update()
    {
        if (panel == null || !Utilities.IsValid(_me)) return;
        if (_shown && _mode == InWorld && closeDistance > 0f
            && Vector3.Distance(_me.GetPosition(), panel.transform.position) > closeDistance) _shown = false;
        if (_vr || !callAnywhere) return;
        if (Input.GetKeyDown(desktopKey) && !InWorldNow() && InThirdPerson()) _waitForRelease = true;
        if (_waitForRelease) // (the key held in third person turns the camera: no panel till it is let go)
        {
            if (!Input.GetKey(desktopKey)) _waitForRelease = false;
            return;
        }
        if (InWorldNow()) return;
        bool held = Input.GetKey(desktopKey);
        if (held && !_shown) { _mode = InView; _shown = true; }
        else if (!held && _shown && _mode == InView) _shown = false;
    }

    public override void PostLateUpdate()
    {
        if (panel == null || !Utilities.IsValid(_me)) return;
        if (callAnywhere && !InWorldNow())
        {
            if (_vr)
            {
                _mode = OnWrist;
                PlaceOnWrist();
            }
            else if (_mode == InView && (_shown || _t > 0f)) PlaceInView(); // (after the view has moved this frame)
        }
        if (!_vr) ShowHint();
        Animate();
    }

    // on the left wrist, shown while looked at
    void PlaceOnWrist()
    {
        var head = _me.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
        var hand = _me.GetTrackingData(VRCPlayerApi.TrackingDataType.LeftHand);
        Quaternion rotation = hand.rotation * leftHandRotation;
        Vector3 position = hand.position + rotation * (offsetDirection.normalized * wristOffset + wristShift);
        panel.transform.SetPositionAndRotation(position, rotation);
        _shown = Vector3.Angle(head.rotation * Vector3.forward, position - head.position) < lookAngle;
    }

    // in the middle of the view, as far off as fills desktopFill of it (the panel at its size in the world)
    void PlaceInView()
    {
        var screen = VRCCameraSettings.ScreenCamera;
        Quaternion view = screen.Rotation;
        float tanUp = Mathf.Tan(screen.FieldOfView * 0.5f * Mathf.Deg2Rad);
        float tanAcross = tanUp * Mathf.Max(screen.Aspect, 0.1f);
        var rt = (RectTransform)panel.transform;
        Vector2 size = new Vector2(rt.rect.width * _worldScale.x, rt.rect.height * _worldScale.y);
        float distance = Mathf.Max(size.x * 0.5f / (tanAcross * desktopFill), size.y * 0.5f / (tanUp * desktopFill));
        panel.transform.SetPositionAndRotation(screen.Position + view * Vector3.forward * distance, view);
    }

    // the third-person hint: in the middle of the view while the key is held, fading in and out as the panel does
    void ShowHint()
    {
        if (hint == null) return;
        float dt = Mathf.Min(Time.deltaTime, 1f / 30f);
        _hintT = Mathf.MoveTowards(_hintT, _waitForRelease ? 1f : 0f, dt / Mathf.Max(_waitForRelease ? showTime : hideTime, 0.01f));
        bool on = _hintT > 0f;
        if (hint.activeSelf != on) hint.SetActive(on);
        if (!on) return;
        var screen = VRCCameraSettings.ScreenCamera;
        hint.transform.SetPositionAndRotation(screen.Position + screen.Rotation * Vector3.forward * hintDistance, screen.Rotation);
        if (hintFade != null) hintFade.alpha = 1f - (1f - _hintT) * (1f - _hintT);
    }

    // a step of the coming or going: faded and grown (eased out), the panel off once it has gone
    void Animate()
    {
        float goal = _shown ? 1f : 0f;
        // (a frame's step at most a 30th of a second: the frame that turns the panel on can be a long one, and would
        // skip the fade)
        float dt = Mathf.Min(Time.deltaTime, 1f / 30f);
        if (fade == null) _t = goal;
        else if (_t != goal) _t = Mathf.MoveTowards(_t, goal, dt / Mathf.Max(_shown ? showTime : hideTime, 0.01f));
        bool visible = _t > 0f;
        if (panel.activeSelf != visible) panel.SetActive(visible);
        if (!visible) return;
        float e = 1f - (1f - _t) * (1f - _t);
        if (fade != null)
        {
            fade.alpha = e;
            fade.interactable = _shown;
            fade.blocksRaycasts = _shown;
        }
        Vector3 scale = _mode == OnWrist ? _worldScale * wristSize : _worldScale;
        panel.transform.localScale = scale * (fade == null ? 1f : Mathf.Lerp(smallScale, 1f, e));
    }
}
