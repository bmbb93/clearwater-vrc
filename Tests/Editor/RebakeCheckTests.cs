using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>When a coast needs baking again: what its bake is made from has changed since.</summary>
public class RebakeCheckTests : TestScene
{
    [Test]
    public void Moving_a_stamp_needs_a_bake()
    {
        var coast = Make<ClearwaterCoast>("Coast", Vector3.zero);
        var stamp = Make<ClearwaterStamp>("Stamp", new Vector3(10, 0, 5));
        Keep(stamp.gameObject.AddComponent<MeshFilter>()).sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        string before = ClearwaterCoastBake.Hash(coast);

        stamp.transform.position += Vector3.right;

        Assert.That(ClearwaterCoastBake.Hash(coast), Is.Not.EqualTo(before));
    }

    [Test]
    public void Nothing_changed_needs_no_bake()
    {
        var coast = Make<ClearwaterCoast>("Coast", Vector3.zero);

        Assert.That(ClearwaterCoastBake.Hash(coast), Is.EqualTo(ClearwaterCoastBake.Hash(coast)));
    }

    [Test]
    public void Changing_the_bed_look_needs_no_bake()
    {
        var coast = Make<ClearwaterCoast>("Coast", Vector3.zero);
        coast.bedLook = ClearwaterBedLooks.Sand;
        string before = ClearwaterCoastBake.Hash(coast);

        coast.bedLook = ClearwaterBedLooks.Pebbles;

        Assert.That(ClearwaterCoastBake.Hash(coast), Is.EqualTo(before));
    }
}
