using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>A user terrain: a flat slope down to the sea (+z), its waterline along x at z = shoreZ.</summary>
public abstract class UserTerrainTest : TestScene
{
    // the walkable area round the origin, and the seam round it (the defaults' shape, smaller)
    protected const float Half = 20f, Seam = 10f, Step = 0.5f;

    /// <summary>A plane over x0..x1, z0..z1 sloping 1 in 10 down towards +z, at the water's height at shoreZ.</summary>
    protected MeshRenderer Slope(float x0, float x1, float z0, float z1, float shoreZ)
    {
        float H(float z) => 0.1f * (shoreZ - z);
        var mesh = Keep(new Mesh { name = "Slope" });
        mesh.vertices = new[] { new Vector3(x0, H(z0), z0), new Vector3(x0, H(z1), z1), new Vector3(x1, H(z1), z1), new Vector3(x1, H(z0), z0) };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        var r = Make<MeshRenderer>("Slope", Vector3.zero);
        r.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        r.sharedMaterial = Keep(new Material(Shader.Find("Standard")));
        return r;
    }

    protected static ClearwaterUserTerrain.Data Bake(params MeshRenderer[] renderers) =>
        ClearwaterUserTerrain.Bake(Vector3.zero, Vector3.zero, Half, Seam, Step, new List<MeshRenderer>(renderers), new List<Terrain>());

    /// <summary>The coast line as drawn, in water space (x, -z): straight along x at z = lineZ, towards +x.</summary>
    protected static List<Vector2> Line(float lineZ) => new List<Vector2> { new Vector2(-100, -lineZ), new Vector2(100, -lineZ) };
}

/// <summary>Inside the walkable area the coast follows the waterline found on the user terrain.</summary>
public class UserTerrainSpliceTests : UserTerrainTest
{
    [Test]
    public void Inside_the_walkable_area_the_line_follows_the_terrains_waterline()
    {
        var d = Bake(Slope(-Half - Seam + 1, Half + Seam - 1, -Half - Seam + 1, Half + Seam - 1, shoreZ: 3f));

        var line = ClearwaterUserTerrain.Splice(Line(0f), d, Vector2.zero, Half, 512, out _, out _);

        foreach (var p in line)
            if (Mathf.Abs(p.x) < Half - 2f) Assert.That(-p.y, Is.EqualTo(3f).Within(0.3f), $"at x {p.x:0.0}");
    }

    [Test]
    public void Outside_the_walkable_area_the_line_stays_as_drawn_though_the_terrain_reaches_on()
    {
        var d = Bake(Slope(-Half - Seam + 1, Half + Seam - 1, -Half - Seam + 1, Half + Seam - 1, shoreZ: 3f));

        var line = ClearwaterUserTerrain.Splice(Line(0f), d, Vector2.zero, Half, 512, out _, out _);

        foreach (var p in line)
            if (Mathf.Abs(p.x) > Half + 0.01f) Assert.That(-p.y, Is.EqualTo(0f).Within(0.01f), $"at x {p.x:0.0}");
    }

    [Test]
    public void A_line_drawn_to_meet_the_waterline_at_the_walkable_edge_raises_no_note()
    {
        var d = Bake(Slope(-Half - Seam + 1, Half + Seam - 1, -Half - Seam + 1, Half + Seam - 1, shoreZ: 3f));

        ClearwaterUserTerrain.Splice(Line(3f), d, Vector2.zero, Half, 512, out string note, out _);

        Assert.That(note, Is.Null);
    }

    [Test]
    public void A_line_drawn_10_m_off_the_waterline_raises_a_note()
    {
        var d = Bake(Slope(-Half - Seam + 1, Half + Seam - 1, -Half - Seam + 1, Half + Seam - 1, shoreZ: 3f));

        ClearwaterUserTerrain.Splice(Line(13f), d, Vector2.zero, Half, 512, out string note, out _);

        Assert.That(note, Does.Contain("waterline"));
    }
}
