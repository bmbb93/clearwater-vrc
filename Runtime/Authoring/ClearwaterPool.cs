using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// A pool: water with its own surface height, in a basin you model (a pool on a building's floor, indoors or out),
/// beside the one sea. This object sits at the pool's still-water surface; its Basin is the pool's own meshes (the
/// walls and floor, not a ceiling or a cover: the basin is baked from above, and the highest surface is the floor).
/// Bake (here, or the coast's Bake) makes its water, its underwater view and its caustics as children, drawn with
/// the sea's shaders and wave simulation. The water shows only where the basin is below the surface.
/// </summary>
[DisallowMultipleComponent]
public class ClearwaterPool : MonoBehaviour, IEditorOnly
{
    [Tooltip("The water's extent (m, x by z) round this object: a little larger than the basin's inside is fine " +
             "(the water shows only where the basin is below the surface)")]
    public Vector2 size = new Vector2(10f, 6f);
    [Tooltip("The pool's own meshes: its walls and floor (and the deck round it, if you like). Not a ceiling or a cover")]
    public GameObject basin;
    [Tooltip("How much the surface moves, against the sea's waves (1)")]
    [Range(0f, 1f)] public float waveStrength = 0.35f;
    [Tooltip("Indoors: no sunlight on it (no sun caustics or glints); it reflects the room (the nearest reflection " +
             "probe) instead of the sky")]
    public bool indoor;
    [Tooltip("Underwater: how far you see (m)")]
    [Min(1f)] public float fogDistance = 30f;

    [HideInInspector] public string id;        // (names its generated folder; kept when the object is renamed)
    [HideInInspector] public string bakedHash; // what the last bake was made from (the inspector flags changes)
    [HideInInspector] public float depth;      // its floor's lowest point below the surface (m, negative; baked)
    [HideInInspector] public ClearwaterCoast.BakeNote[] bakeNotes;

    /// <summary>The water's extent (world xz min, max).</summary>
    public Rect Area => new Rect(transform.position.x - size.x * 0.5f, transform.position.z - size.y * 0.5f, size.x, size.y);

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.8f);
        Gizmos.DrawWireCube(transform.position, new Vector3(size.x, 0f, size.y));
    }
}
