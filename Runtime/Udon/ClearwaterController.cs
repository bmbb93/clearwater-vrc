using UdonSharp;
using UnityEngine;
using VRC.SDK3.Rendering;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon.Common;
using VRC.Udon.Common.Interfaces;

/// <summary>
/// Drives the Clearwater water: keeps the local ripple simulation window under the viewer, turns players'
/// feet and hands touching the water into ripples, lets anyone tap the water (Use / click) for everyone,
/// and copies the sun light's direction into the water, caustics and sky materials.
/// Positions handed to the shaders are in "water space": relative to the water object, with z negated
/// (the original demo's axes, see ClearwaterCommon.cginc).
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class ClearwaterController : UdonSharpBehaviour
{
    [Header("Scene")]
    public Transform water;
    public Light sun;

    [Header("Materials")]
    public Material waterMaterial;
    public Material rippleMaterial;
    public Material causticsMaterial;
    public Material skyMaterial;
    public Material seabedMaterial;
    public Material underwaterMaterial;
    public Material avatarCausticsMaterial;

    [Header("Avatar caustics")]
    [Tooltip("Projector (player layers only) that puts the caustics on avatars; kept over the local player")]
    public Transform avatarCausticsProjector;

    [Header("Underwater")]
    [Tooltip("Inside-out box with the underwater fog; shown only while the local head is below the surface")]
    public Renderer underwaterVolume;

    [Header("Ripple window (must match the ripple CRTs)")]
    public int rippleResolution = 512;
    public float rippleSize = 14f;

    [Header("Interaction")]
    public float tapRadius = 0.154f;
    public float tapStrength = 0.07f;
    public float stepStrength = 0.035f;
    public float stepSpacing = 0.22f;
    public float touchHeight = 0.18f;
    public float maxTapDistance = 40f;

    [Header("Wave audio")]
    [Tooltip("3D surf source; kept on the waterline, level with the listener, so the whole shore sounds like one line source")]
    public AudioSource shoreAudio;
    [Tooltip("Quiet 2D bed of distant sea")]
    public AudioSource bedAudio;
    [Tooltip("Muffled 2D loop heard while the head is under water")]
    public AudioSource underwaterAudio;
    [Tooltip("The waterline (world), set by the coast bake; the shore source stays on it, at the point nearest the listener")]
    public Vector3[] shorePoints = { new Vector3(-100, 0.2f, -35.9f), new Vector3(100, 0.2f, -35.9f) };
    [Tooltip("The points form a loop (set by the coast bake)")]
    public bool shoreClosed;
    [Tooltip("Waves break at the shore, with the surf sound (set by the coast bake); off: still water, no surf")]
    public bool shoreWaves = true;
    public float shoreLevel = 0.9f;
    public float bedLevel = 0.3f;
    public float underwaterLevel = 0.8f;
    [Tooltip("Length of the break timing track (= the shore loop)")]
    public float swashLoop = 90f;
    float _submerged;

    const int MaxQueue = 32;
    const int MaxPlayers = 96;
    const int BoneCount = 7;

    Vector2 _center;
    Vector4[] _queue = new Vector4[MaxQueue]; // water-space x, z, radius (m), strength
    int _queueCount;
    VRCPlayerApi[] _players = new VRCPlayerApi[MaxPlayers];
    int _playerCount;
    Vector3[] _lastTouch = new Vector3[MaxPlayers * BoneCount];
    bool[] _wasTouching = new bool[MaxPlayers * BoneCount];

    // Cameras closer than this (m) to the water surface may have the waterline across their view: the underwater
    // fog then draws the part below it (keep equal to CW_SURFACE_BAND in ClearwaterSurface.cginc)
    const float SurfaceBand = 0.6f;
    // a shore point below this separates pieces of the line (the coast bake keeps only the parts within earshot)
    const float BreakY = -50000f;

    void Start()
    {
        RefreshPlayers();
        if (!shoreWaves && shoreAudio != null) shoreAudio.Stop(); // still water: no surf
        if (underwaterMaterial != null && waterMaterial != null)
        {
            // the fog finds the waterline with the water's own breakers
            underwaterMaterial.SetFloat("_SwashHeight", waterMaterial.GetFloat("_SwashHeight"));
            underwaterMaterial.SetFloat("_SwashRunup", waterMaterial.GetFloat("_SwashRunup"));
        }
        CopyClouds(waterMaterial);
        CopyClouds(seabedMaterial);
    }

    // the water and the seabed reflect and refract the sky: give them its clouds
    void CopyClouds(Material m)
    {
        if (m == null || skyMaterial == null) return;
        m.SetFloat("_CloudCover", skyMaterial.GetFloat("_CloudCover"));
        m.SetFloat("_CloudSize", skyMaterial.GetFloat("_CloudSize"));
        m.SetFloat("_CloudSpeed", skyMaterial.GetFloat("_CloudSpeed"));
        m.SetFloat("_CloudDir", skyMaterial.GetFloat("_CloudDir"));
    }

    public override void OnPlayerJoined(VRCPlayerApi player) { RefreshPlayers(); }
    public override void OnPlayerLeft(VRCPlayerApi player) { RefreshPlayers(); }

    void RefreshPlayers()
    {
        _players = VRCPlayerApi.GetPlayers(_players);
        _playerCount = VRCPlayerApi.GetPlayerCount();
        if (_playerCount > MaxPlayers) _playerCount = MaxPlayers;
        for (int i = 0; i < _wasTouching.Length; i++) _wasTouching[i] = false;
    }

    Vector2 ToWater(Vector3 p)
    {
        Vector3 o = water.position;
        return new Vector2(p.x - o.x, -(p.z - o.z));
    }

    void Update()
    {
        if (sun != null)
        {
            Vector3 fw = sun.transform.forward;
            Vector4 sd = new Vector4(-fw.x, -fw.y, -fw.z, 0);
            if (waterMaterial != null) waterMaterial.SetVector("_SunDir", sd);
            if (causticsMaterial != null) causticsMaterial.SetVector("_SunDir", sd);
            if (skyMaterial != null) skyMaterial.SetVector("_SunDir", sd);
            if (seabedMaterial != null) seabedMaterial.SetVector("_SunDir", sd);
            if (underwaterMaterial != null) underwaterMaterial.SetVector("_SunDir", sd);
            if (avatarCausticsMaterial != null) avatarCausticsMaterial.SetVector("_SunDir", sd);
        }

        VRCPlayerApi local = Networking.LocalPlayer;
        if (!Utilities.IsValid(local)) return;

        // keep the ripple window centred a little ahead of the viewer's gaze, snapped to whole texels
        VRCPlayerApi.TrackingData head = local.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
        Vector3 f = head.rotation * Vector3.forward;
        float above = Mathf.Max(head.position.y - water.position.y, 0.2f);
        float look = above / Mathf.Max(-f.y, 0.2f) * 0.9f;
        Vector2 fxz = new Vector2(f.x, -f.z);
        if (fxz.sqrMagnitude > 1e-6f) fxz.Normalize();
        Vector2 want = ToWater(head.position) + fxz * Mathf.Min(look, rippleSize * 0.3f);
        float tx = rippleSize / rippleResolution;
        float dxT = Mathf.Round((want.x - _center.x) / tx), dzT = Mathf.Round((want.y - _center.y) / tx);
        _center += new Vector2(dxT * tx, dzT * tx);
        rippleMaterial.SetVector("_Shift", new Vector4(dxT / rippleResolution, dzT / rippleResolution, 0, 0));
        Vector4 rc = new Vector4(_center.x, _center.y, 0, 0);
        waterMaterial.SetVector("_RipCenter", rc);
        if (underwaterMaterial != null) underwaterMaterial.SetVector("_RipCenter", rc);
        if (seabedMaterial != null)
        {
            seabedMaterial.SetVector("_RipCenter", rc);
            // the ground grid follows the viewer, on its own 25 cm lattice so nothing swims
            Vector3 hp = head.position;
            seabedMaterial.SetVector("_GridCenter", new Vector4(Mathf.Round(hp.x * 4f) * 0.25f, 0, Mathf.Round(hp.z * 4f) * 0.25f, 0));
        }

        if (avatarCausticsProjector != null)
        {
            Vector3 pp = avatarCausticsProjector.position;
            avatarCausticsProjector.position = new Vector3(head.position.x, pp.y, head.position.z);
        }

        bool under = head.position.y < water.position.y;
        UpdateAudio(head.position, under);
        if (underwaterVolume != null)
        {
            // the fog box is needed while any camera of ours is under water or at the waterline: the head, the
            // screen view (it can differ from the head, e.g. third person) or the photo camera; its shader picks
            // its pixels per camera, so a dry head and a diving photo camera both look right
            bool fog = head.position.y < water.position.y + SurfaceBand ||
                       CameraUnder(VRCCameraSettings.ScreenCamera) || CameraUnder(VRCCameraSettings.PhotoCamera);
            if (underwaterVolume.enabled != fog) underwaterVolume.enabled = fog;
        }

        GatherBodyTouches();

        rippleMaterial.SetVector("_Drop0", PopDrop());
        rippleMaterial.SetVector("_Drop1", PopDrop());
        rippleMaterial.SetVector("_Drop2", PopDrop());
        rippleMaterial.SetVector("_Drop3", PopDrop());
    }

    bool CameraUnder(VRCCameraSettings cam)
    {
        return Utilities.IsValid(cam) && cam.Active && cam.Position.y < water.position.y + SurfaceBand;
    }

    // Nearest point of the waterline to p (in plan). Udon is slow, so each frame only the segments around the last
    // nearest one are checked, with a full search every 60 frames to catch jumps (teleports, respawns).
    int _shoreSeg;
    int _shoreFrame;
    Vector3 NearestOnShore(Vector3 p)
    {
        int n = shorePoints.Length;
        int segs = shoreClosed ? n : n - 1;
        int from = _shoreSeg - 2, to = _shoreSeg + 2;
        if (++_shoreFrame >= 60 || segs <= 5) { _shoreFrame = 0; from = 0; to = segs - 1; }
        float best = float.MaxValue;
        Vector3 bestP = shorePoints[0];
        for (int k = from; k <= to; k++)
        {
            int s = shoreClosed ? (k % segs + segs) % segs : Mathf.Clamp(k, 0, segs - 1);
            Vector3 a = shorePoints[s], b = shorePoints[(s + 1) % n];
            if (a.y < BreakY || b.y < BreakY) continue; // between two pieces of the line
            Vector3 ab = b - a;
            float t = Mathf.Clamp01(((p.x - a.x) * ab.x + (p.z - a.z) * ab.z) / Mathf.Max(ab.x * ab.x + ab.z * ab.z, 1e-6f));
            Vector3 q = a + ab * t;
            float d = (p.x - q.x) * (p.x - q.x) + (p.z - q.z) * (p.z - q.z);
            if (d < best) { best = d; bestP = q; _shoreSeg = s; }
        }
        return bestP;
    }

    void UpdateAudio(Vector3 headPos, bool under)
    {
        if (shoreWaves && shoreAudio != null && shorePoints != null && shorePoints.Length >= 2)
            shoreAudio.transform.position = NearestOnShore(headPos);
        // shoreline waves follow the shore loop's playback, so each surge lands with its sound
        float clock = (shoreAudio != null && shoreAudio.isPlaying) ? shoreAudio.time : Time.time % swashLoop;
        waterMaterial.SetFloat("_SwashClock", clock);
        if (seabedMaterial != null) seabedMaterial.SetFloat("_SwashClock", clock);
        if (underwaterMaterial != null) underwaterMaterial.SetFloat("_SwashClock", clock);

        // quick crossfade into / out of the muffled underwater loop
        _submerged = Mathf.MoveTowards(_submerged, under ? 1f : 0f, Time.deltaTime * 5f);
        if (shoreAudio != null) shoreAudio.volume = shoreWaves ? shoreLevel * (1f - _submerged) : 0f;
        if (bedAudio != null) bedAudio.volume = bedLevel * (1f - _submerged);
        if (underwaterAudio != null) underwaterAudio.volume = underwaterLevel * _submerged;
    }

    void GatherBodyTouches()
    {
        float y0 = water.position.y;
        for (int p = 0; p < _playerCount; p++)
        {
            VRCPlayerApi pl = _players[p];
            if (!Utilities.IsValid(pl)) continue;
            for (int b = 0; b < BoneCount; b++)
            {
                int slot = p * BoneCount + b;
                Vector3 pos = pl.GetBonePosition(Bone(b));
                if (pos == Vector3.zero) continue; // no humanoid avatar loaded
                // a body part is "at the surface" when it is within touchHeight of it, above or below
                bool touching = Mathf.Abs(pos.y - y0) < touchHeight;
                if (touching)
                {
                    Vector3 d = pos - _lastTouch[slot]; d.y = 0;
                    if (!_wasTouching[slot]) { Enqueue(pos, tapRadius * 0.8f, stepStrength); _lastTouch[slot] = pos; }
                    else if (d.magnitude > stepSpacing) { Enqueue(pos, tapRadius * 0.7f, stepStrength * 0.8f); _lastTouch[slot] = pos; }
                }
                _wasTouching[slot] = touching;
            }
        }
    }

    HumanBodyBones Bone(int b)
    {
        switch (b)
        {
            case 0: return HumanBodyBones.LeftFoot;
            case 1: return HumanBodyBones.RightFoot;
            case 2: return HumanBodyBones.LeftLowerLeg;
            case 3: return HumanBodyBones.RightLowerLeg;
            case 4: return HumanBodyBones.Hips;
            case 5: return HumanBodyBones.LeftHand;
            default: return HumanBodyBones.RightHand;
        }
    }

    public override void InputUse(bool value, UdonInputEventArgs args)
    {
        if (!value) return;
        VRCPlayerApi local = Networking.LocalPlayer;
        if (!Utilities.IsValid(local)) return;
        VRCPlayerApi.TrackingData td;
        if (local.IsUserInVR())
            td = local.GetTrackingData(args.handType == HandType.LEFT ? VRCPlayerApi.TrackingDataType.LeftHand : VRCPlayerApi.TrackingDataType.RightHand);
        else
            td = local.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
        Vector3 dir = td.rotation * Vector3.forward;
        if (dir.y > -0.01f) return;
        float t = (water.position.y - td.position.y) / dir.y;
        if (t < 0 || t > maxTapDistance) return;
        SendCustomNetworkEvent(NetworkEventTarget.All, nameof(Tap), td.position + dir * t);
    }

    [NetworkCallable(8)]
    public void Tap(Vector3 worldPos)
    {
        Enqueue(worldPos, tapRadius, tapStrength);
    }

    void Enqueue(Vector3 worldPos, float radius, float strength)
    {
        if (_queueCount >= MaxQueue) return;
        Vector2 w = ToWater(worldPos);
        _queue[_queueCount++] = new Vector4(w.x, w.y, radius, strength);
    }

    Vector4 PopDrop()
    {
        while (_queueCount > 0)
        {
            Vector4 q = _queue[0];
            for (int i = 1; i < _queueCount; i++) _queue[i - 1] = _queue[i];
            _queueCount--;
            float u = (q.x - _center.x) / rippleSize + 0.5f, v = (q.y - _center.y) / rippleSize + 0.5f;
            if (u < 0.05f || u > 0.95f || v < 0.05f || v > 0.95f) continue; // outside the local window
            return new Vector4(u, v, q.z / rippleSize, q.w);
        }
        return Vector4.zero;
    }
}
