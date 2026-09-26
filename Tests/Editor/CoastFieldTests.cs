using NUnit.Framework;
using UnityEngine;

/// <summary>The shore coordinates the coast bake lays over the water (water space: x, and y = -z):
/// u = signed distance from the waterline, + on the sea side; v = distance along it.</summary>
public class CoastFieldTests : TestScene
{
    // a straight shore along x, drawn towards +x: the sea on its left seen from above (+z, water y < 0)
    static readonly Vector4[] Straight = { new Vector4(-100, 0, 0, 0), new Vector4(100, 0, 200, 0) };
    const float Size = 64f;
    const int Res = 128;

    static Vector2 At(Texture2D field, float x, float y) // water space, round the origin
    {
        var c = field.GetPixelBilinear(x / Size + 0.5f, y / Size + 0.5f);
        return new Vector2(c.r, c.g);
    }

    [Test]
    public void U_is_the_distance_from_the_waterline_positive_out_to_sea()
    {
        var field = Keep(ClearwaterCoastBake.BakeCoastField(Straight, false, Vector2.zero, Size, Res, TextureFormat.RGFloat));

        Assert.That(At(field, 5, -10).x, Is.EqualTo(10f).Within(0.3f), "10 m out to sea");
        Assert.That(At(field, 5, 10).x, Is.EqualTo(-10f).Within(0.3f), "10 m up the land");
    }

    [Test]
    public void V_is_the_distance_along_the_line_from_its_first_point()
    {
        var field = Keep(ClearwaterCoastBake.BakeCoastField(Straight, false, Vector2.zero, Size, Res, TextureFormat.RGFloat));

        Assert.That(At(field, 20, -5).y, Is.EqualTo(120f).Within(0.3f));
    }
}
