using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// The coast, as you draw it: the waterline (a line of points in the scene), the cross-section from dry land out to
/// the deep bottom, and the square around this object to bake. "Bake" (inspector) turns it into the textures every
/// Clearwater shader reads (the water, its waves and swash, the seabed, the caustics on avatars), rebuilds the
/// walkable ground and lays the shore sound along the line. Editor-only: stripped from uploads.
/// </summary>
[DisallowMultipleComponent]
public class ClearwaterCoast : MonoBehaviour, IEditorOnly
{
    public enum Section { GentleBeach, Curve }

    [Tooltip("The waterline, in this object's space (height ignored). The sea is on the LEFT of the line's direction, " +
             "seen from above. An open line carries on straight past its two ends.")]
    public Vector3[] points = { new Vector3(-100, 0, 0), new Vector3(0, 0, 0), new Vector3(100, 0, 0) };
    [Tooltip("A loop (an island, or a lake with the sea outside... or inside, by the direction) instead of an open line")]
    public bool closed;

    [Header("Cross-section")]
    public Section section = Section.GentleBeach;

    [Tooltip("Gentle beach: water depth at the foot of the beach face (m)")]
    public float shallowDepth = 0.35f;
    [Tooltip("Gentle beach: how much the knee-deep shallows deepen per metre out")]
    public float shallowSlope = 0.041f;
    [Tooltip("Gentle beach: slope of the steeper drop beyond the shallows")]
    public float shelfSlope = 0.155f;
    [Tooltip("Gentle beach: depth of the flat deep bottom (m)")]
    public float deepDepth = 3.45f;
    [Tooltip("Gentle beach: distance from the waterline to where the deep bottom starts (m)")]
    public float deepStart = 48.3f;
    [Tooltip("Gentle beach: slope of the beach face")]
    public float beachSlope = 0.25f;
    [Tooltip("Gentle beach: height of the dry land behind the beach (m)")]
    public float landHeight = 0.6f;

    [Tooltip("Curve: height of the floor (m; the water surface is 0) against distance from the waterline " +
             "(m; + out to sea). Beyond its ends the floor stays level.")]
    public AnimationCurve curve = new AnimationCurve(
        new Keyframe(-10f, 0.6f), new Keyframe(-2.4f, 0.6f), new Keyframe(0f, 0f), new Keyframe(1.4f, -0.35f),
        new Keyframe(30f, -1.4f), new Keyframe(48f, -3.45f), new Keyframe(80f, -3.45f));

    [Header("Bake")]
    [Tooltip("Size of the square baked around this object (m). Outside it the coast carries on the way it leaves it.")]
    public float areaSize = 512f;
    [Tooltip("Texels per side of the bake (512 m / 1024 = 0.5 m)")]
    public int resolution = 1024;
    [Tooltip("Walkable ground: half its size around this object (m); invisible walls stand at its edge")]
    public float groundHalfSize = 100f;
    [Tooltip("Walkable ground: grid spacing (m)")]
    public float groundStep = 0.5f;
    [Tooltip("Texels per side of the stamps' bake over the walkable ground (ClearwaterStamp objects); 1024 over 200 m = 0.2 m")]
    public int stampResolution = 1024;

    [HideInInspector] public string bakedHash; // what the last bake was made from (the inspector flags changes)

    void OnDrawGizmos()
    {
        // the line, also when the object is not selected
        if (points == null || points.Length < 2) return;
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.8f);
        int n = points.Length, segs = closed ? n : n - 1;
        for (int i = 0; i < segs; i++)
            Gizmos.DrawLine(transform.TransformPoint(Flat(points[i])), transform.TransformPoint(Flat(points[(i + 1) % n])));
    }

    public static Vector3 Flat(Vector3 p) => new Vector3(p.x, 0, p.z);
}
