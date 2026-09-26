using NUnit.Framework;
using UnityEngine;

/// <summary>Where a pool's water is: its footprint, and the heights at which a head is in it.</summary>
public class PoolSpanTests : TestScene
{
    [Test]
    public void The_footprint_is_the_water_round_the_pool_object()
    {
        var pool = Make<ClearwaterPool>("Pool", new Vector3(5, 2, 3));
        pool.size = new Vector2(10, 6);

        var (r, _, _) = ClearwaterPoolBake.Span(pool);

        Assert.That(r.xMin, Is.EqualTo(0f).Within(1e-4f));
        Assert.That(r.xMax, Is.EqualTo(10f).Within(1e-4f));
        Assert.That(r.yMin, Is.EqualTo(0f).Within(1e-4f));
        Assert.That(r.yMax, Is.EqualTo(6f).Within(1e-4f));
    }

    [Test]
    public void A_head_is_in_the_pool_from_its_floor_to_just_over_its_surface()
    {
        var pool = Make<ClearwaterPool>("Pool", new Vector3(0, 2, 0));
        pool.depth = -1f; // (as baked: the floor 1 m under the water)

        var (_, top, bottom) = ClearwaterPoolBake.Span(pool);

        Assert.That(1.5f, Is.InRange(bottom, top), "under the water");
        Assert.That(1.05f, Is.InRange(bottom, top), "just over the floor");
        Assert.That(2.5f, Is.Not.InRange(bottom, top), "half a metre over the water");
        Assert.That(0.2f, Is.Not.InRange(bottom, top), "under the floor");
    }
}
