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
    [Tooltip("Waves roll in over the shallows, break and run up the beach, with the surf sound along the waterline. " +
             "Off: still water at the shore (a lake or a pond); the open water keeps its small waves.")]
    public bool shoreWaves = true;
    public enum LineShape { Straight, Smooth, Handles }
    [Tooltip("Straight: the points joined by straight lines. Smooth: a smooth curve through the points (handles set " +
             "automatically). Handles: like a path in Illustrator: each point has two handles that set the curve's " +
             "direction and curvature (Alt-drag a handle to make a corner).")]
    public LineShape shape = LineShape.Smooth;
    // Handles mode: each point's handles as offsets from it (in = towards the previous point, out = towards the next);
    // corner = the two handles move independently instead of staying in line
    [HideInInspector] public Vector3[] handleIn, handleOut;
    [HideInInspector] public bool[] corner;

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

    [Header("Sea")]
    [Tooltip("Side of the square of sea around the water object (m). Its edge fades into the haze; the ground is drawn " +
             "out to 0.6 x this from the viewer. Cameras need a far clip of about 0.8 x this (the inspector checks the " +
             "reference camera).")]
    public float seaSize = 5000f;

    [Header("Bake")]
    [Tooltip("Size of the square baked in detail around this object (m). The coast is drawn outside it too (see " +
             "Outer resolution); beyond the sea it carries on the way it leaves it.")]
    public float areaSize = 512f;
    [Tooltip("Texels per side of the bake (512 m / 1024 = 0.5 m)")]
    public int resolution = 1024;
    [Tooltip("Texels per side of the coarse bake over the whole sea (Sea size), which shapes the coast outside the " +
             "detailed square: points drawn far out make headlands, bays or a far shore (5000 m / 1024 = 4.9 m)")]
    public int outerResolution = 1024;
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

    // ---- the line as drawn: a cubic Bezier between each pair of points (straight, auto-smooth, or your handles)

    const float SampleStep = 1f;   // m between samples of a curved segment in the detailed area
    const int OuterSamples = 16;   // samples of a curved segment outside it (only seen from afar)
    const int MaxSamples = 511;    // the bake takes at most 512 points (a loop repeats its first)

    /// <summary>The waterline as a polyline in this object's space (height 0): curves sampled about every metre in
    /// the detailed area, more coarsely outside it. A loop ends with its first point again.</summary>
    public System.Collections.Generic.List<Vector3> Sampled()
    {
        var outp = new System.Collections.Generic.List<Vector3>();
        if (points == null || points.Length < 2) return outp;
        int n = points.Length, segs = closed ? n : n - 1;
        var per = new int[segs];
        var near = new bool[segs];
        int nearTotal = 0, farTotal = 0;
        float reach = areaSize * 0.75f; // (the detailed square's corner, in any rotation)
        for (int k = 0; k < segs; k++)
        {
            var (a, b, c, d) = Segment(k);
            float len = Vector3.Distance(a, b) + Vector3.Distance(b, c) + Vector3.Distance(c, d); // (>= the curve's length)
            near[k] = DistanceToOrigin(a, d) < reach;
            per[k] = shape == LineShape.Straight ? 1
                : near[k] ? Mathf.Clamp(Mathf.CeilToInt(len / SampleStep), 4, 64)
                : Mathf.Clamp(Mathf.CeilToInt(len / SampleStep), 4, OuterSamples);
            if (near[k]) nearTotal += per[k]; else farTotal += per[k];
        }
        // too many: thin the outer part first, then everything
        float farScale = 1f, allScale = 1f;
        if (nearTotal + farTotal > MaxSamples)
        {
            farScale = farTotal > 0 ? Mathf.Max(MaxSamples - nearTotal, farTotal / 4f) / farTotal : 1f;
            if (farScale > 1f) farScale = 1f;
            float t = nearTotal + farTotal * farScale;
            if (t > MaxSamples) allScale = MaxSamples / t;
        }
        for (int k = 0; k < segs; k++)
        {
            int m = Mathf.Max(1, Mathf.FloorToInt(per[k] * (near[k] ? 1f : farScale) * allScale));
            for (int j = 0; j < m; j++) outp.Add(PointOn(k, j / (float)m));
        }
        outp.Add(closed ? outp[0] : Flat(points[n - 1]));
        return outp;
    }

    static float DistanceToOrigin(Vector3 a, Vector3 b) // of the chord a-b, in plan
    {
        Vector2 p = new Vector2(a.x, a.z), q = new Vector2(b.x, b.z), ab = q - p;
        float t = Mathf.Clamp01(-Vector2.Dot(p, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
        return (p + ab * t).magnitude;
    }

    /// <summary>The point at t (0..1) along segment k (from point k to point k+1).</summary>
    public Vector3 PointOn(int k, float t)
    {
        var (a, b, c, d) = Segment(k);
        float s = 1f - t;
        return s * s * s * a + 3f * s * s * t * b + 3f * s * t * t * c + t * t * t * d;
    }

    /// <summary>Segment k as a cubic Bezier: point k, its out-handle, point k+1's in-handle, point k+1.</summary>
    public (Vector3, Vector3, Vector3, Vector3) Segment(int k)
    {
        int n = points.Length, k2 = (k + 1) % n;
        Vector3 p1 = Flat(points[k % n]), p2 = Flat(points[k2]);
        switch (shape)
        {
            case LineShape.Straight: return (p1, Vector3.Lerp(p1, p2, 1f / 3f), Vector3.Lerp(p1, p2, 2f / 3f), p2);
            case LineShape.Handles:
                EnsureHandles();
                return (p1, p1 + Flat(handleOut[k % n]), p2 + Flat(handleIn[k2]), p2);
            default: return (p1, p1 + AutoOut(k % n), p2 + AutoIn(k2), p2);
        }
    }

    // Automatic handles (Smooth, and the starting point for Handles): along the line from the previous point to the
    // next, a third of the way to each neighbour. An open line's end points aim straight at their neighbour, so the
    // line leaves along its end segments (and carries on straight beyond them).
    Vector3 Neighbour(int i, int step)
    {
        int n = points.Length, j = i + step;
        if (closed) return Flat(points[(j % n + n) % n]);
        if (j >= 0 && j < n) return Flat(points[j]);
        return 2f * Flat(points[i]) - Flat(points[i - step]); // mirrored beyond an end
    }
    Vector3 AutoDir(int i) => (Neighbour(i, 1) - Neighbour(i, -1)).normalized;
    public Vector3 AutoOut(int i) => AutoDir(i) * Vector3.Distance(Flat(points[i]), Neighbour(i, 1)) / 3f;
    public Vector3 AutoIn(int i) => -AutoDir(i) * Vector3.Distance(Flat(points[i]), Neighbour(i, -1)) / 3f;

    /// <summary>Keeps the handle arrays as long as the points; new points get automatic handles.</summary>
    public void EnsureHandles()
    {
        int n = points.Length;
        if (handleIn != null && handleOut != null && corner != null &&
            handleIn.Length == n && handleOut.Length == n && corner.Length == n) return;
        var hi = new Vector3[n]; var ho = new Vector3[n]; var co = new bool[n];
        for (int i = 0; i < n; i++)
        {
            bool had = handleIn != null && handleOut != null && i < handleIn.Length && i < handleOut.Length;
            hi[i] = had ? handleIn[i] : AutoIn(i);
            ho[i] = had ? handleOut[i] : AutoOut(i);
            co[i] = corner != null && i < corner.Length && corner[i];
        }
        handleIn = hi; handleOut = ho; corner = co;
    }

    /// <summary>Sets the handles of point i (or of every point, i &lt; 0) to the automatic smooth ones.</summary>
    public void ResetHandles(int i = -1)
    {
        EnsureHandles();
        for (int k = 0; k < points.Length; k++)
            if (i < 0 || k == i) { handleIn[k] = AutoIn(k); handleOut[k] = AutoOut(k); corner[k] = false; }
    }

    /// <summary>Adds a point on segment k at t without changing the curve's shape (de Casteljau split).</summary>
    public void InsertPoint(int k, float t)
    {
        int n = points.Length;
        var (a, b, c, d) = Segment(k);
        Vector3 ab = Vector3.Lerp(a, b, t), bc = Vector3.Lerp(b, c, t), cd = Vector3.Lerp(c, d, t);
        Vector3 abc = Vector3.Lerp(ab, bc, t), bcd = Vector3.Lerp(bc, cd, t), p = Vector3.Lerp(abc, bcd, t);
        bool keepHandles = shape == LineShape.Handles;
        if (keepHandles) EnsureHandles();
        var pts = new System.Collections.Generic.List<Vector3>(points);
        pts.Insert(k + 1, p);
        if (keepHandles)
        {
            var hi = new System.Collections.Generic.List<Vector3>(handleIn);
            var ho = new System.Collections.Generic.List<Vector3>(handleOut);
            var co = new System.Collections.Generic.List<bool>(corner);
            int k2 = (k + 1) % n;
            ho[k] = ab - a;          // the two halves keep exactly the old curve
            hi[k2] = cd - d;
            hi.Insert(k + 1, abc - p);
            ho.Insert(k + 1, bcd - p);
            co.Insert(k + 1, false);
            handleIn = hi.ToArray(); handleOut = ho.ToArray(); corner = co.ToArray();
        }
        points = pts.ToArray();
    }

    /// <summary>Removes point i (with its handles).</summary>
    public void RemovePoint(int i)
    {
        if (shape == LineShape.Handles) EnsureHandles();
        var pts = new System.Collections.Generic.List<Vector3>(points);
        pts.RemoveAt(i);
        if (handleIn != null && handleOut != null && corner != null && handleIn.Length == points.Length)
        {
            var hi = new System.Collections.Generic.List<Vector3>(handleIn); hi.RemoveAt(i); handleIn = hi.ToArray();
            var ho = new System.Collections.Generic.List<Vector3>(handleOut); ho.RemoveAt(i); handleOut = ho.ToArray();
            var co = new System.Collections.Generic.List<bool>(corner); co.RemoveAt(i); corner = co.ToArray();
        }
        points = pts.ToArray();
    }
}
