using NUnit.Framework;
using UnityEngine;

/// <summary>The coast line as the bake takes it: sampled into a polyline of at most 512 points.</summary>
public class CoastLineTests : TestScene
{
    [Test]
    public void A_long_winding_line_is_sampled_within_the_bakes_512_points_from_end_to_end()
    {
        var coast = Make<ClearwaterCoast>("Coast", Vector3.zero);
        coast.shape = ClearwaterCoast.LineShape.Smooth;
        var pts = new Vector3[300];
        for (int i = 0; i < pts.Length; i++) pts[i] = new Vector3(i * 10f - 1500f, 0, (i % 2) * 8f);
        coast.points = pts;

        var line = coast.Sampled();

        Assert.That(line.Count, Is.LessThanOrEqualTo(512));
        Assert.That(Vector3.Distance(line[0], pts[0]), Is.LessThan(1e-3f), "starts at the first point");
        Assert.That(Vector3.Distance(line[line.Count - 1], pts[pts.Length - 1]), Is.LessThan(1e-3f), "ends at the last");
    }
}
