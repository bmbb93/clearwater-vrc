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
    [Tooltip("A smooth curve through the points (centripetal Catmull-Rom) instead of straight segments between them")]
    public bool smooth = true;

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
        var line = Sampled();
        if (line.Count < 2) return;
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.8f);
        for (int i = 0; i + 1 < line.Count; i++)
            Gizmos.DrawLine(transform.TransformPoint(line[i]), transform.TransformPoint(line[i + 1]));
    }

    public static Vector3 Flat(Vector3 p) => new Vector3(p.x, 0, p.z);

    // ---- the line as drawn: the points joined straight, or by a smooth curve through them

    const float SampleStep = 1f;   // m between samples of a smooth segment
    const int MaxSamples = 511;    // the bake takes at most 512 points (a loop repeats its first)

    /// <summary>The waterline as a polyline in this object's space (height 0): the points themselves, or a smooth
    /// curve through them sampled about every metre. A loop ends with its first point again.</summary>
    public System.Collections.Generic.List<Vector3> Sampled()
    {
        var outp = new System.Collections.Generic.List<Vector3>();
        if (points == null || points.Length < 2) return outp;
        int n = points.Length, segs = closed ? n : n - 1;
        int total = 0;
        var per = new int[segs];
        for (int k = 0; k < segs; k++)
        {
            float len = Vector3.Distance(Flat(points[k]), Flat(points[(k + 1) % n]));
            per[k] = smooth ? Mathf.Clamp(Mathf.CeilToInt(len / SampleStep), 4, 64) : 1;
            total += per[k];
        }
        float scale = total > MaxSamples ? (float)MaxSamples / total : 1f; // very long lines: fewer samples each
        for (int k = 0; k < segs; k++)
        {
            int m = Mathf.Max(1, Mathf.FloorToInt(per[k] * scale));
            for (int j = 0; j < m; j++) outp.Add(PointOn(k, j / (float)m));
        }
        outp.Add(closed ? outp[0] : Flat(points[n - 1]));
        return outp;
    }

    /// <summary>The point at t (0..1) along segment k (from point k to point k+1).</summary>
    public Vector3 PointOn(int k, float t)
    {
        int n = points.Length;
        Vector3 p1 = Flat(points[k % n]), p2 = Flat(points[(k + 1) % n]);
        if (!smooth || n < 2) return Vector3.Lerp(p1, p2, t);
        // neighbours; an open line's ends continue straight (mirrored), so the curve leaves along the end segments
        Vector3 p0 = closed || k > 0 ? Flat(points[(k - 1 + n) % n]) : 2 * p1 - p2;
        Vector3 p3 = closed || k + 2 < n ? Flat(points[(k + 2) % n]) : 2 * p2 - p1;
        return CatmullRom(p0, p1, p2, p3, t);
    }

    // centripetal Catmull-Rom (alpha 0.5): no cusps or loops between points, even when they are unevenly spaced
    static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t0 = 0f;
        float t1 = t0 + Mathf.Max(Mathf.Sqrt(Vector3.Distance(p0, p1)), 1e-4f);
        float t2 = t1 + Mathf.Max(Mathf.Sqrt(Vector3.Distance(p1, p2)), 1e-4f);
        float t3 = t2 + Mathf.Max(Mathf.Sqrt(Vector3.Distance(p2, p3)), 1e-4f);
        float u = Mathf.Lerp(t1, t2, t);
        Vector3 a1 = (t1 - u) / (t1 - t0) * p0 + (u - t0) / (t1 - t0) * p1;
        Vector3 a2 = (t2 - u) / (t2 - t1) * p1 + (u - t1) / (t2 - t1) * p2;
        Vector3 a3 = (t3 - u) / (t3 - t2) * p2 + (u - t2) / (t3 - t2) * p3;
        Vector3 b1 = (t2 - u) / (t2 - t0) * a1 + (u - t0) / (t2 - t0) * a2;
        Vector3 b2 = (t3 - u) / (t3 - t1) * a2 + (u - t1) / (t3 - t1) * a3;
        return (t2 - u) / (t2 - t1) * b1 + (u - t1) / (t2 - t1) * b2;
    }
}
