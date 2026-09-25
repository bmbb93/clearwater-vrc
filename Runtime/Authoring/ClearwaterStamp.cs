using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// Puts an object into the coast's bake (ClearwaterCoast → Bake), seen from above by the height of its top surface:
///  - Obstacle: a real prop standing in the water (a rock, a pier pile, driftwood). The water's waves see it: they
///    break on it and foam round it. It is drawn as itself; optionally the underwater caustics fall on it too.
///  - Raise ground / Carve ground: an invisible brush. Its top surface becomes the ground (a sandbar, a ledge; a
///    pool, a channel): the seabed, the floor seen through the water, the walkable ground and the waves all follow.
///    Brushes are tagged EditorOnly on bake, so they are not uploaded.
/// Only mesh renderers inside the coast's walkable-ground square count. Bake the coast again after moving stamps.
/// </summary>
[DisallowMultipleComponent]
public class ClearwaterStamp : MonoBehaviour, IEditorOnly
{
    public enum Mode { Obstacle = 0, RaiseGround = 1, CarveGround = 2 }

    public Mode mode = Mode.Obstacle;
    [Tooltip("Obstacle only: the underwater caustics fall on this object (its renderers move to the ClearwaterProps layer, " +
             "which the caustics projector draws on)")]
    public bool receiveCaustics = true;

    // brushes are hidden after a bake: show their shape as a wireframe
    void OnDrawGizmos() => DrawBrush(0.35f);
    void OnDrawGizmosSelected() => DrawBrush(1f);
    void DrawBrush(float alpha)
    {
        if (mode == Mode.Obstacle) return;
        Gizmos.color = mode == Mode.RaiseGround ? new Color(0.9f, 0.75f, 0.4f, alpha) : new Color(0.4f, 0.6f, 1f, alpha);
        foreach (var mf in GetComponentsInChildren<MeshFilter>())
            if (mf.sharedMesh != null)
            {
                Gizmos.matrix = mf.transform.localToWorldMatrix;
                Gizmos.DrawWireMesh(mf.sharedMesh);
            }
        Gizmos.matrix = Matrix4x4.identity;
    }
}
