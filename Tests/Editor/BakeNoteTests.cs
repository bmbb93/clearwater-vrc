using System.Linq;
using NUnit.Framework;
using UnityEngine;

/// <summary>What the bake tells about a user terrain, and where it marks it.</summary>
public class BakeNoteTests : UserTerrainTest
{
    const float Inner = Half + Seam - 1; // (a terrain over the walkable area and most of the seam: the usual)

    System.Collections.Generic.List<(string text, System.Collections.Generic.List<Vector3> at)> Notes(ClearwaterUserTerrain.Data d)
    {
        var coast = Make<ClearwaterCoast>("Coast", Vector3.zero);
        coast.groundHalfSize = Half;
        coast.seamWidth = Seam;
        var floorBake = Keep(new Material(Shader.Find("Hidden/Clearwater/FloorBake")));
        return ClearwaterUserTerrain.Check(d, coast, floorBake, Vector3.zero);
    }

    [Test]
    public void A_walkable_area_left_half_bare_is_noted_and_marked_in_the_bare_half()
    {
        var d = Bake(Slope(-Inner, 0f, -Inner, Inner, shoreZ: 3f));

        var bare = Notes(d).Where(n => n.text.Contains("not covered")).ToList();

        Assert.That(bare, Has.Count.EqualTo(1));
        Assert.That(bare[0].at, Is.Not.Empty);
        Assert.That(bare[0].at.All(p => p.x > -0.5f), "the marks are where nothing covers it");
    }

    [Test]
    public void A_covered_walkable_area_is_not_noted()
    {
        var d = Bake(Slope(-Inner, Inner, -Inner, Inner, shoreZ: 3f));

        Assert.That(Notes(d).Where(n => n.text.Contains("not covered")), Is.Empty);
    }

    [Test]
    public void A_terrain_reaching_past_the_baked_square_is_noted()
    {
        var d = Bake(Slope(-Half - Seam - 5, Half + Seam + 5, -Inner, Inner, shoreZ: 3f));

        Assert.That(Notes(d).Count(n => n.text.Contains("reaches past")), Is.EqualTo(1));
    }

    [Test]
    public void A_terrain_within_the_baked_square_is_not_noted_as_reaching_past()
    {
        var d = Bake(Slope(-Inner, Inner, -Inner, Inner, shoreZ: 3f));

        Assert.That(Notes(d).Where(n => n.text.Contains("reaches past")), Is.Empty);
    }
}
