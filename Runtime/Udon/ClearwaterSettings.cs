using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// The sky and wave panel's settings: what the clouds and the waves start with (set here in the Inspector, which also
/// writes them into the materials so the Scene view shows them; the hour, the day going by and its length are the
/// sky's, shown here with them), and what anyone changes on the panel while the world runs, shared with everyone
/// (whoever changes them takes this over). Everything goes to the water, the seabed and the sky through the
/// controller. ResetAll puts the clouds and the waves back to the start values here and the hour back to the sky's
/// (ClearwaterSky.ResetTime). Tools > Clearwater > Add Sky Control Panel makes one with the panel.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class ClearwaterSettings : UdonSharpBehaviour
{
    [Tooltip("The sky of the time of day: the hour, the day going by and its length")]
    public ClearwaterSky sky;
    [Tooltip("The water's controller: the clouds and the waves go to the sky, the water and the seabed through it")]
    public ClearwaterController controller;

    [Header("What the world starts with, and the panel's Reset all goes back to")]
    [Tooltip("How much of the sky the clouds cover")]
    [Range(0f, 1f)] public float clouds = 0.3f;
    [Tooltip("How fast the clouds drift (m/s)")]
    [Range(0f, 60f)] public float cloudDrift = 8f;
    [Tooltip("How high the shore waves are (m, the breaker height; 0 = none)")]
    [Range(0f, 0.5f)] public float shoreWaves = 0.14f;
    [Tooltip("How strong the sea's small waves are: 0 a glassy calm, 1 as built")]
    [Range(0f, 1f)] public float ripples = 1f;
    [Tooltip("How fast the sea's small waves go, as against as built (1; 0 = still)")]
    [Range(0f, 2f)] public float rippleSpeed = 1f;

    // what was set on the panel while the world runs (below 0: the start values above)
    [UdonSynced] float _clouds = -1f;
    [UdonSynced] float _cloudDrift = -1f;
    [UdonSynced] float _shoreWaves = -1f;
    [UdonSynced] float _ripples = -1f;
    [UdonSynced] float _rippleSpeed = -1f;

    void Start() { Apply(); }

    public override void OnDeserialization() { Apply(); }

    /// <summary>Sets how much of the sky the clouds cover (0..1), for everyone.</summary>
    public void SetClouds(float cover)
    {
        TakeOver();
        _clouds = Mathf.Clamp01(cover);
        Share();
        if (sky != null) sky.RenderReflection();
    }

    /// <summary>Sets how fast the clouds drift (m/s), for everyone; they go on from where they are.</summary>
    public void SetCloudDrift(float speed)
    {
        TakeOver();
        _cloudDrift = Mathf.Max(speed, 0f);
        Share();
    }

    /// <summary>Sets how high the shore waves are (m, the breaker height; 0 = none), for everyone.</summary>
    public void SetShoreWaves(float height)
    {
        TakeOver();
        _shoreWaves = Mathf.Max(height, 0f);
        Share();
    }

    /// <summary>Sets how strong the sea's small waves are (0 = a glassy calm, 1 = as built), for everyone.</summary>
    public void SetRipples(float strength)
    {
        TakeOver();
        _ripples = Mathf.Clamp01(strength);
        Share();
    }

    /// <summary>Sets how fast the sea's small waves go, as against as built (1; 0 = still), for everyone.</summary>
    public void SetRippleSpeed(float speed)
    {
        TakeOver();
        _rippleSpeed = Mathf.Max(speed, 0f);
        Share();
    }

    /// <summary>Puts everything back for everyone: the clouds and the waves to the start values here, the hour, the day
    /// going by and its length to the sky's as set in the Inspector.</summary>
    public void ResetAll()
    {
        TakeOver();
        _clouds = -1f; _cloudDrift = -1f; _shoreWaves = -1f; _ripples = -1f; _rippleSpeed = -1f;
        Share();
        if (sky != null) { sky.ResetTime(); sky.RenderReflection(); }
    }

    /// <summary>The cloud cover now (0..1).</summary>
    public float Clouds() { return _clouds >= 0f ? _clouds : clouds; }
    /// <summary>How fast the clouds drift now (m/s).</summary>
    public float CloudDrift() { return _cloudDrift >= 0f ? _cloudDrift : cloudDrift; }
    /// <summary>The shore waves' height now (m).</summary>
    public float ShoreWaves() { return _shoreWaves >= 0f ? _shoreWaves : shoreWaves; }
    /// <summary>The sea's small waves' strength now (0..1).</summary>
    public float Ripples() { return _ripples >= 0f ? _ripples : ripples; }
    /// <summary>How fast the sea's small waves go now, as against as built.</summary>
    public float RippleSpeed() { return _rippleSpeed >= 0f ? _rippleSpeed : rippleSpeed; }

    void TakeOver()
    {
        if (!Networking.IsOwner(gameObject)) Networking.SetOwner(Networking.LocalPlayer, gameObject);
    }

    void Share()
    {
        RequestSerialization();
        Apply();
    }

    // the settings of the moment on the sky, the water and the seabed
    void Apply()
    {
        if (controller == null) return;
        controller.SetCloudCover(Clouds());
        controller.SetCloudSpeed(CloudDrift());
        controller.SetShoreWaveHeight(ShoreWaves());
        controller.SetSeaWaves(Ripples());
        controller.SetRippleSpeed(RippleSpeed());
    }
}
