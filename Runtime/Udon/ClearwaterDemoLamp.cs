using UdonSharp;
using UnityEngine;
using VRC.Udon;

/// <summary>
/// For the Light Volumes demo (Tools > Clearwater > Demo Scenes, with VRC Light Volumes in the project): moves a
/// Point Light Volume round a circle across the waterline, bobbing over the ground (under the water where it is
/// deep), its colour going round the hues and its intensity pulsing, to show the light on the sand, the shallows and
/// the bottom following it. The volume must be Dynamic. Its colour and intensity are set on its UdonBehaviour, the way
/// Light Volumes watches for them, so this compiles without the Light Volumes package.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class ClearwaterDemoLamp : UdonSharpBehaviour
{
    [Tooltip("The Point Light Volume's UdonBehaviour (its Point Light Volume Instance)")]
    public UdonBehaviour lightVolume;
    [Tooltip("Shown in the light's colour, as bright as it (an emissive material of its own)")]
    public Renderer marker;

    [Header("Path")]
    [Tooltip("The circle's centre (world)")]
    public Vector3 center = new Vector3(0f, 0f, -33f);
    [Tooltip("The circle's radius (m)")]
    public float radius = 4f;
    [Tooltip("Once round (seconds)")]
    public float orbitSeconds = 24f;
    [Tooltip("Lowest and highest over the ground (m)")]
    public float heightMin = 0.25f, heightMax = 0.85f;
    [Tooltip("Down and up again (seconds)")]
    public float bobSeconds = 9f;
    [Tooltip("What it bobs over")]
    public LayerMask groundLayers = 1;

    [Header("Colour and intensity")]
    [Tooltip("Round the hues (seconds)")]
    public float hueSeconds = 30f;
    [Range(0f, 1f)] public float saturation = 0.65f;
    [Tooltip("Dimmest and brightest")]
    public float intensityMin = 1f, intensityMax = 8f;
    [Tooltip("Bright and dim again (seconds)")]
    public float pulseSeconds = 7f;

    Material _marker;

    void Start()
    {
        if (marker != null) _marker = marker.material; // (its own copy, to colour)
    }

    void Update()
    {
        float t = Time.time;
        float a = t / orbitSeconds * 2f * Mathf.PI;
        Vector3 p = center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
        float ground = center.y - 5f;
        RaycastHit hit;
        if (Physics.Raycast(new Vector3(p.x, center.y + 50f, p.z), Vector3.down, out hit, 100f, groundLayers, QueryTriggerInteraction.Ignore))
            ground = hit.point.y;
        p.y = ground + Mathf.Lerp(heightMin, heightMax, 0.5f + 0.5f * Mathf.Sin(t / bobSeconds * 2f * Mathf.PI));
        transform.position = p;

        Color c = Color.HSVToRGB(Mathf.Repeat(t / hueSeconds, 1f), saturation, 1f);
        float intensity = Mathf.Lerp(intensityMin, intensityMax, 0.5f + 0.5f * Mathf.Sin(t / pulseSeconds * 2f * Mathf.PI));
        if (lightVolume != null)
        {
            // (set on the UdonBehaviour, its change events have Light Volumes pick them up)
            lightVolume.SetProgramVariable("Color", c);
            lightVolume.SetProgramVariable("Intensity", intensity);
        }
        if (_marker != null) _marker.SetColor("_EmissionColor", c * (0.4f + 0.25f * intensity));
    }
}
