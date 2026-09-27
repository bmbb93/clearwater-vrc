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
/// Besides the sea there may be pools (ClearwaterPool), each its own water at its own height: the viewer is at one
/// water at a time (a pool when over or in it, else the sea), which gets the ripples; each camera's fog is its water's.
/// Positions handed to the shaders are in "water space": relative to the water object (the sea's, or the pool's the
/// viewer is at), with z negated (the original demo's axes, see ClearwaterCommon.cginc).
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
    [Tooltip("The beach drawn on the coast's user terrain (set by the coast bake; none without one)")]
    public Material userBeachMaterial;

    [Header("Avatar caustics")]
    [Tooltip("Projector (player layers only) that puts the caustics on avatars; kept over the local player")]
    public Transform avatarCausticsProjector;

    [Header("Underwater")]
    [Tooltip("Inside-out box with the underwater fog; shown only while the local head is below the surface")]
    public Renderer underwaterVolume;

    [Header("Pools (set by their bake)")]
    [Tooltip("Each pool's water (at its surface)")]
    public Transform[] pools;
    [Tooltip("Each pool's footprint (world x, z min, x, z max)")]
    public Vector4[] poolAreas;
    [Tooltip("Each pool's floor at its deepest (world y)")]
    public float[] poolFloors;
    public Material[] poolWaterMaterials;
    public Material[] poolUnderwaterMaterials;
    public Material[] poolCausticsMaterials;
    public Renderer[] poolUnderwaterVolumes;

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
    [Tooltip("How big the waves are at each of the points, 0..1 (set by the coast bake from the swell's direction): the surf " +
             "sounds from the nearest shore that has it, as loud as it is there")]
    public float[] shoreExposure;
    [Tooltip("Seconds after the shore nearest the coast object that the swell reaches each of the points (set by the coast " +
             "bake): the waves are kept in step with the surf where it sounds from")]
    public float[] shoreDelay;
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
    // the shore waves as the world was built: the breaker height (m; the surf is as loud as built at it) and whether the
    // coast has shore waves at all (the materials' _ShoreWaves)
    float _builtHeight = -1f;
    float _builtShore = -1f;
    // how fast the sea's small waves go as against as built, and the shift that keeps them where they are when it
    // changes (the spectrum's clock is (time * speed + shift) * its time scale: _Udon_CWRipple)
    float _rippleSpeed = 1f, _rippleShift;

    int _body = -1;       // the water the viewer is at: -1 the sea, else a pool (the ripples are on it)
    int _poolCount;
    bool[] _poolFog;
    const float PoolAbove = 3f; // m over a pool's surface (and round it) within which the viewer is at the pool
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
        _poolCount = pools != null ? pools.Length : 0;
        _poolFog = new bool[_poolCount];
        RefreshPlayers();
        if (!shoreWaves && shoreAudio != null) shoreAudio.Stop(); // still water: no surf
        RememberBuilt();
        // where the swell starts to show, as set on the water (the others work out the surface's height too: the
        // waterline across a camera's view)
        if (waterMaterial != null)
        {
            float swell = waterMaterial.GetFloat("_SwellDepth");
            if (seabedMaterial != null) seabedMaterial.SetFloat("_SwellDepth", swell);
            if (userBeachMaterial != null) userBeachMaterial.SetFloat("_SwellDepth", swell);
        }
        // the waves' clock as built, unless the panel's settings came first (a value left over from the editor's
        // last play otherwise stays: shader globals outlive it)
        if (_rippleSpeed == 1f && _rippleShift == 0f)
            VRCShader.SetGlobalVector(VRCShader.PropertyToID("_Udon_CWRipple"), new Vector4(1f, 0f, 0f, 1f));
        if (underwaterMaterial != null && waterMaterial != null)
        {
            // the fog finds the waterline with the water's own breakers
            underwaterMaterial.SetFloat("_SwashHeight", waterMaterial.GetFloat("_SwashHeight"));
            underwaterMaterial.SetFloat("_SwashRunup", waterMaterial.GetFloat("_SwashRunup"));
            underwaterMaterial.SetFloat("_SwellDepth", waterMaterial.GetFloat("_SwellDepth"));
        }
        if (skyMaterial != null) skyMaterial.SetFloat("_CloudShift", 0f); // (none left over from the editor's play)
        CopyClouds(waterMaterial);
        CopyClouds(seabedMaterial);
        for (int i = 0; i < _poolCount; i++) CopyClouds(poolWaterMaterials[i]);
        SetRipCenters(new Vector4(1e5f, 1e5f, 0, 0)); // (the ripples are put on the viewer's water in Update)
    }

    // the water and the seabed reflect and refract the sky: give them its clouds
    void CopyClouds(Material m)
    {
        if (m == null || skyMaterial == null) return;
        m.SetFloat("_CloudCover", skyMaterial.GetFloat("_CloudCover"));
        m.SetFloat("_CloudSize", skyMaterial.GetFloat("_CloudSize"));
        m.SetFloat("_CloudSpeed", skyMaterial.GetFloat("_CloudSpeed"));
        m.SetFloat("_CloudDir", skyMaterial.GetFloat("_CloudDir"));
        m.SetFloat("_CloudShift", skyMaterial.GetFloat("_CloudShift"));
        m.SetFloat("_LandCover", skyMaterial.GetFloat("_LandCover"));
        m.SetVector("_SeaDir", skyMaterial.GetVector("_SeaDir"));
    }

    /// <summary>The clouds' cover (0..1) on the sky, the water, the seabed and the pools at once (ClearwaterSettings).</summary>
    public void SetCloudCover(float cover)
    {
        if (skyMaterial == null) return;
        skyMaterial.SetFloat("_CloudCover", cover);
        CopyClouds(waterMaterial);
        CopyClouds(seabedMaterial);
        for (int i = 0; i < _poolCount; i++) CopyClouds(poolWaterMaterials[i]);
    }

    public float CloudCover() { return skyMaterial != null ? skyMaterial.GetFloat("_CloudCover") : 0f; }

    /// <summary>How fast the clouds drift (m/s), on the sky, the water, the seabed and the pools at once
    /// (ClearwaterSettings). They go on from where they are.</summary>
    public void SetCloudSpeed(float speed)
    {
        if (skyMaterial == null) return;
        float was = skyMaterial.GetFloat("_CloudSpeed");
        if (was == speed) return;
        // the shaders put the layer at speed * time + shift (time since the scene loaded): keep that where it is now
        skyMaterial.SetFloat("_CloudShift", skyMaterial.GetFloat("_CloudShift") + (was - speed) * Time.timeSinceLevelLoad);
        skyMaterial.SetFloat("_CloudSpeed", speed);
        CopyClouds(waterMaterial);
        CopyClouds(seabedMaterial);
        for (int i = 0; i < _poolCount; i++) CopyClouds(poolWaterMaterials[i]);
    }

    public float CloudSpeed() { return skyMaterial != null ? skyMaterial.GetFloat("_CloudSpeed") : 0f; }

    /// <summary>How high the shore waves are (m, the breaker height; 0 = none): their breaking, run-up, foam and wet
    /// sand, on the water, the seabed, the underwater view and the user terrain's beach at once, the surf's sound louder
    /// or softer with them (ClearwaterSettings).</summary>
    public void SetShoreWaveHeight(float height)
    {
        if (waterMaterial == null) return;
        RememberBuilt();
        height = Mathf.Max(height, 0f);
        // none at all: still water at the shore, as a coast built without shore waves (no foam or wet sand left at the
        // water's edge, and none of their work for the shaders)
        float shore = height > 0.005f ? _builtShore : 0f;
        SetShore(waterMaterial, height, shore);
        SetShore(seabedMaterial, height, shore);
        SetShore(underwaterMaterial, height, shore);
        SetShore(userBeachMaterial, height, shore);
    }

    // the shore waves as built, before anything sets them (the panel's settings can come before Start)
    void RememberBuilt()
    {
        if (_builtHeight >= 0f || waterMaterial == null) return;
        _builtHeight = waterMaterial.GetFloat("_SwashHeight");
        _builtShore = waterMaterial.GetFloat("_ShoreWaves");
    }

    void SetShore(Material m, float height, float shore)
    {
        if (m == null) return;
        m.SetFloat("_SwashHeight", height);
        m.SetFloat("_ShoreWaves", shore);
    }

    public float ShoreWaveHeight() { return waterMaterial != null ? waterMaterial.GetFloat("_SwashHeight") : 0f; }

    /// <summary>How strong the sea's small waves are, 0 (a glassy calm: no light patterns on the floor) to 1 (as built),
    /// on the sea's water, seabed, underwater view and the light patterns on avatars at once; the pools keep their own
    /// (ClearwaterSettings).</summary>
    public void SetSeaWaves(float strength)
    {
        float calm = 1f - Mathf.Clamp01(strength);
        if (waterMaterial != null) waterMaterial.SetFloat("_Calm", calm);
        if (seabedMaterial != null) seabedMaterial.SetFloat("_Calm", calm);
        if (underwaterMaterial != null) underwaterMaterial.SetFloat("_Calm", calm);
        if (avatarCausticsMaterial != null) avatarCausticsMaterial.SetFloat("_Calm", calm);
    }

    public float SeaWaves() { return waterMaterial != null ? 1f - waterMaterial.GetFloat("_Calm") : 1f; }

    /// <summary>How fast the sea's small waves go, as against as built (1; 0 = still), for the pools too: they share the
    /// one surface. They go on from the shape they have (ClearwaterSettings).</summary>
    public void SetRippleSpeed(float speed)
    {
        speed = Mathf.Max(speed, 0f);
        if (speed == _rippleSpeed) return;
        _rippleShift += (_rippleSpeed - speed) * Time.timeSinceLevelLoad; // (the clock where it is now)
        _rippleSpeed = speed;
        VRCShader.SetGlobalVector(VRCShader.PropertyToID("_Udon_CWRipple"), new Vector4(_rippleSpeed, _rippleShift, 0f, 1f));
    }

    public float RippleSpeed() { return _rippleSpeed; }

    public override void OnPlayerJoined(VRCPlayerApi player) { RefreshPlayers(); }
    public override void OnPlayerLeft(VRCPlayerApi player) { RefreshPlayers(); }

    void RefreshPlayers()
    {
        _players = VRCPlayerApi.GetPlayers(_players);
        _playerCount = VRCPlayerApi.GetPlayerCount();
        if (_playerCount > MaxPlayers) _playerCount = MaxPlayers;
        for (int i = 0; i < _wasTouching.Length; i++) _wasTouching[i] = false;
    }

    // water space of the water the viewer is at
    Vector2 ToWater(Vector3 p)
    {
        Vector3 o = Origin(_body);
        return new Vector2(p.x - o.x, -(p.z - o.z));
    }

    Vector3 Origin(int body) { return body < 0 ? water.position : pools[body].position; }

    // the pool whose water p is in or over: in its footprint (widened by margin), from a little under its floor to
    // above over its surface; -1: none (the sea's)
    int PoolAt(Vector3 p, float above, float margin)
    {
        for (int i = 0; i < _poolCount; i++)
        {
            Vector4 a = poolAreas[i];
            if (p.x < a.x - margin || p.x > a.z + margin || p.z < a.y - margin || p.z > a.w + margin) continue;
            if (p.y < poolFloors[i] - 0.3f || p.y > pools[i].position.y + above) continue;
            return i;
        }
        return -1;
    }

    // the ripples on one water only: the others get a centre far away (none of the window on them)
    void SetRipCenters(Vector4 far)
    {
        waterMaterial.SetVector("_RipCenter", far);
        if (underwaterMaterial != null) underwaterMaterial.SetVector("_RipCenter", far);
        if (seabedMaterial != null) seabedMaterial.SetVector("_RipCenter", far);
        for (int i = 0; i < _poolCount; i++)
        {
            if (poolWaterMaterials[i] != null) poolWaterMaterials[i].SetVector("_RipCenter", far);
            if (poolUnderwaterMaterials[i] != null) poolUnderwaterMaterials[i].SetVector("_RipCenter", far);
        }
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
            if (userBeachMaterial != null) userBeachMaterial.SetVector("_SunDir", sd);
            for (int i = 0; i < _poolCount; i++)
            {
                if (poolWaterMaterials[i] != null) poolWaterMaterials[i].SetVector("_SunDir", sd);
                if (poolUnderwaterMaterials[i] != null) poolUnderwaterMaterials[i].SetVector("_SunDir", sd);
                if (poolCausticsMaterials[i] != null) poolCausticsMaterials[i].SetVector("_SunDir", sd);
            }
        }

        VRCPlayerApi local = Networking.LocalPlayer;
        if (!Utilities.IsValid(local)) return;

        // the water the viewer is at: a pool when over it or in it (or beside it), else the sea. Moving to another
        // water, the window jumps to it (the old ripples slide out of it) and the drops waiting for the old one go
        VRCPlayerApi.TrackingData head = local.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
        int body = PoolAt(head.position, PoolAbove, rippleSize * 0.3f);
        if (body != _body)
        {
            _body = body;
            _queueCount = 0;
            SetRipCenters(new Vector4(1e5f, 1e5f, 0, 0));
        }

        // keep the ripple window centred a little ahead of the viewer's gaze, snapped to whole texels
        Vector3 f = head.rotation * Vector3.forward;
        float above = Mathf.Max(head.position.y - Origin(_body).y, 0.2f);
        float look = above / Mathf.Max(-f.y, 0.2f) * 0.9f;
        Vector2 fxz = new Vector2(f.x, -f.z);
        if (fxz.sqrMagnitude > 1e-6f) fxz.Normalize();
        Vector2 want = ToWater(head.position) + fxz * Mathf.Min(look, rippleSize * 0.3f);
        float tx = rippleSize / rippleResolution;
        float dxT = Mathf.Round((want.x - _center.x) / tx), dzT = Mathf.Round((want.y - _center.y) / tx);
        _center += new Vector2(dxT * tx, dzT * tx);
        rippleMaterial.SetVector("_Shift", new Vector4(dxT / rippleResolution, dzT / rippleResolution, 0, 0));
        Vector4 rc = new Vector4(_center.x, _center.y, 0, 0);
        if (_body < 0)
        {
            waterMaterial.SetVector("_RipCenter", rc);
            if (underwaterMaterial != null) underwaterMaterial.SetVector("_RipCenter", rc);
            if (seabedMaterial != null) seabedMaterial.SetVector("_RipCenter", rc);
        }
        else
        {
            if (poolWaterMaterials[_body] != null) poolWaterMaterials[_body].SetVector("_RipCenter", rc);
            if (poolUnderwaterMaterials[_body] != null) poolUnderwaterMaterials[_body].SetVector("_RipCenter", rc);
        }
        if (seabedMaterial != null)
        {
            // the ground grid follows the viewer, on its own 50 cm lattice so nothing swims
            Vector3 hp = head.position;
            seabedMaterial.SetVector("_GridCenter", new Vector4(Mathf.Round(hp.x * 2f) * 0.5f, 0, Mathf.Round(hp.z * 2f) * 0.5f, 0));
        }

        if (avatarCausticsProjector != null)
        {
            Vector3 pp = avatarCausticsProjector.position;
            avatarCausticsProjector.position = new Vector3(head.position.x, pp.y, head.position.z);
        }

        int headPool = PoolAt(head.position, SurfaceBand, 0f);
        bool under = head.position.y < Origin(headPool).y;
        UpdateAudio(head.position, under);
        // A water's fog box is needed while any camera of ours is under it or at its waterline: the head, the screen
        // view (it can differ from the head, e.g. third person) or the photo camera. Each camera may be at another
        // water (a pool's, or the sea's); the fog shaders pick their pixels per camera (and only for one in their
        // water), so a dry head and a diving photo camera both look right.
        for (int i = 0; i < _poolCount; i++) _poolFog[i] = false;
        bool seaFog = false;
        if (headPool >= 0) _poolFog[headPool] = true; else seaFog = head.position.y < water.position.y + SurfaceBand;
        VRCCameraSettings cam = VRCCameraSettings.ScreenCamera;
        if (Utilities.IsValid(cam) && cam.Active && FogFor(cam.Position)) seaFog = true;
        cam = VRCCameraSettings.PhotoCamera;
        if (Utilities.IsValid(cam) && cam.Active && FogFor(cam.Position)) seaFog = true;
        if (underwaterVolume != null && underwaterVolume.enabled != seaFog) underwaterVolume.enabled = seaFog;
        for (int i = 0; i < _poolCount; i++)
        {
            Renderer v = poolUnderwaterVolumes[i];
            if (v != null && v.enabled != _poolFog[i]) v.enabled = _poolFog[i];
        }

        GatherBodyTouches();

        rippleMaterial.SetVector("_Drop0", PopDrop());
        rippleMaterial.SetVector("_Drop1", PopDrop());
        rippleMaterial.SetVector("_Drop2", PopDrop());
        rippleMaterial.SetVector("_Drop3", PopDrop());
    }

    // a camera at c needs its water's fog: marks a pool's; true when it is the sea's
    bool FogFor(Vector3 c)
    {
        int b = PoolAt(c, SurfaceBand, 0f);
        if (b >= 0) { _poolFog[b] = true; return false; }
        return c.y < water.position.y + SurfaceBand;
    }

    // Nearest point of the waterline to p (in plan), counting a quiet shore as further away (distance over its wave
    // height: in a sheltered cove the surf is heard from the open beach). Udon is slow, so each frame only the segments
    // around the last nearest one are checked, with a full search every 60 frames to catch jumps (teleports,
    // respawns). _shoreLoud = the wave height there.
    int _shoreSeg;
    int _shoreFrame;
    float _shoreLoud = 1f;
    float _shoreDelayNow, _clockShift; // the swell's delay where the surf sounds from, and the shift eased toward it
    bool _clockShiftSet;
    Vector3 NearestOnShore(Vector3 p)
    {
        int n = shorePoints.Length;
        int segs = shoreClosed ? n : n - 1;
        int from = _shoreSeg - 2, to = _shoreSeg + 2;
        if (++_shoreFrame >= 60 || segs <= 5) { _shoreFrame = 0; from = 0; to = segs - 1; }
        float best = float.MaxValue;
        Vector3 bestP = shorePoints[0];
        bool weighted = shoreExposure != null && shoreExposure.Length == n;
        bool timed = shoreDelay != null && shoreDelay.Length == n;
        for (int k = from; k <= to; k++)
        {
            int s = shoreClosed ? (k % segs + segs) % segs : Mathf.Clamp(k, 0, segs - 1);
            Vector3 a = shorePoints[s], b = shorePoints[(s + 1) % n];
            if (a.y < BreakY || b.y < BreakY) continue; // between two pieces of the line
            Vector3 ab = b - a;
            float t = Mathf.Clamp01(((p.x - a.x) * ab.x + (p.z - a.z) * ab.z) / Mathf.Max(ab.x * ab.x + ab.z * ab.z, 1e-6f));
            Vector3 q = a + ab * t;
            float d = (p.x - q.x) * (p.x - q.x) + (p.z - q.z) * (p.z - q.z);
            float e = weighted ? Mathf.Lerp(shoreExposure[s], shoreExposure[(s + 1) % n], t) : 1f;
            d /= Mathf.Max(e * e, 0.0025f);
            if (d < best)
            {
                best = d; bestP = q; _shoreSeg = s; _shoreLoud = e;
                _shoreDelayNow = timed ? Mathf.Lerp(shoreDelay[s], shoreDelay[(s + 1) % n], t) : 0f;
            }
        }
        return bestP;
    }

    void UpdateAudio(Vector3 headPos, bool under)
    {
        if (shoreWaves && shoreAudio != null && shorePoints != null && shorePoints.Length >= 2)
            shoreAudio.transform.position = NearestOnShore(headPos);
        // shoreline waves follow the shore loop's playback, so each surge lands with its sound
        float clock = (shoreAudio != null && shoreAudio.isPlaying) ? shoreAudio.time : Time.time % swashLoop;
        // The swell reaches each stretch of shore at its own time (it bends into bays and sweeps along an oblique
        // shore); the waves where the surf sounds from are kept in step with it. Eased, so the waves never jump when
        // the surf moves to another shore (at most a tenth of a second per second: unnoticeable).
        _clockShift = _clockShiftSet ? Mathf.MoveTowards(_clockShift, _shoreDelayNow, Time.deltaTime * 0.1f) : _shoreDelayNow;
        _clockShiftSet = true;
        clock += _clockShift;
        waterMaterial.SetFloat("_SwashClock", clock);
        if (seabedMaterial != null) seabedMaterial.SetFloat("_SwashClock", clock);
        if (underwaterMaterial != null) underwaterMaterial.SetFloat("_SwashClock", clock);
        if (userBeachMaterial != null) userBeachMaterial.SetFloat("_SwashClock", clock);

        // quick crossfade into / out of the muffled underwater loop
        _submerged = Mathf.MoveTowards(_submerged, under ? 1f : 0f, Time.deltaTime * 5f);
        // (the surf as loud as built at the built wave height, louder or softer as the waves are set higher or lower)
        float surf = _builtHeight > 0f ? Mathf.Sqrt(ShoreWaveHeight() / _builtHeight) : 1f;
        if (shoreAudio != null) shoreAudio.volume = shoreWaves ? shoreLevel * surf * _shoreLoud * (1f - _submerged) : 0f;
        if (bedAudio != null) bedAudio.volume = bedLevel * (1f - _submerged);
        if (underwaterAudio != null) underwaterAudio.volume = underwaterLevel * _submerged;
    }

    void GatherBodyTouches()
    {
        for (int p = 0; p < _playerCount; p++)
        {
            VRCPlayerApi pl = _players[p];
            if (!Utilities.IsValid(pl)) continue;
            for (int b = 0; b < BoneCount; b++)
            {
                int slot = p * BoneCount + b;
                Vector3 pos = pl.GetBonePosition(Bone(b));
                if (pos == Vector3.zero) continue; // no humanoid avatar loaded
                // a body part is "at the surface" when it is within touchHeight of it, above or below: of the pool
                // it is in, or the sea's (its ripples show when it is the water the viewer is at)
                int body = PoolAt(pos, touchHeight, 0f);
                bool touching = Mathf.Abs(pos.y - Origin(body).y) < touchHeight;
                if (touching)
                {
                    Vector3 d = pos - _lastTouch[slot]; d.y = 0;
                    if (!_wasTouching[slot]) { Enqueue(pos, tapRadius * 0.8f, stepStrength, body); _lastTouch[slot] = pos; }
                    else if (d.magnitude > stepSpacing) { Enqueue(pos, tapRadius * 0.7f, stepStrength * 0.8f, body); _lastTouch[slot] = pos; }
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
        // the nearest water the ray comes down on: a pool's surface in its footprint, or the sea's
        float t = (water.position.y - td.position.y) / dir.y;
        for (int i = 0; i < _poolCount; i++)
        {
            float tp = (pools[i].position.y - td.position.y) / dir.y;
            if (tp < 0 || (t >= 0 && tp >= t)) continue;
            Vector3 q = td.position + dir * tp;
            Vector4 a = poolAreas[i];
            if (q.x >= a.x && q.x <= a.z && q.z >= a.y && q.z <= a.w) t = tp;
        }
        if (t < 0 || t > maxTapDistance) return;
        SendCustomNetworkEvent(NetworkEventTarget.All, nameof(Tap), td.position + dir * t);
    }

    [NetworkCallable(8)]
    public void Tap(Vector3 worldPos)
    {
        Enqueue(worldPos, tapRadius, tapStrength, PoolAt(worldPos, 0.05f, 0f));
    }

    // a drop on a water (-1 the sea, else a pool): only the water the viewer is at has the ripples
    void Enqueue(Vector3 worldPos, float radius, float strength, int body)
    {
        if (body != _body || _queueCount >= MaxQueue) return;
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
