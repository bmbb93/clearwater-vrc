using NUnit.Framework;
using UnityEngine;

/// <summary>The baked sky behaves as the real one does (ClearwaterAtmosphere): the colours of a clear day and its dusk.</summary>
public class SkyTests
{
    static float Deg(float d) => d * Mathf.Deg2Rad;
    static float BlueOverRed(Vector3 c) => c.z / Mathf.Max(c.x, 1e-9f);

    [Test]
    public void At_noon_the_zenith_is_bluer_than_the_horizon()
    {
        var zenith = ClearwaterAtmosphere.SkyRadiance(Deg(89f), Deg(90f), Deg(60f));
        var horizon = ClearwaterAtmosphere.SkyRadiance(Deg(1f), Deg(90f), Deg(60f));

        Assert.That(BlueOverRed(zenith), Is.GreaterThan(BlueOverRed(horizon)));
    }

    [Test]
    public void At_sunset_the_horizon_under_the_sun_is_redder_than_the_one_opposite()
    {
        var toSun = ClearwaterAtmosphere.SkyRadiance(Deg(2f), Deg(0f), Deg(0.5f));
        var away = ClearwaterAtmosphere.SkyRadiance(Deg(2f), Deg(180f), Deg(0.5f));

        Assert.That(BlueOverRed(toSun), Is.LessThan(BlueOverRed(away)));
    }

    [Test]
    public void A_low_sun_shines_redder_and_dimmer_than_a_high_one()
    {
        var low = ClearwaterAtmosphere.Transmittance(6360.02f, Mathf.Sin(Deg(5f)));
        var high = ClearwaterAtmosphere.Transmittance(6360.02f, Mathf.Sin(Deg(60f)));

        Assert.That(BlueOverRed(low), Is.LessThan(BlueOverRed(high)));
        Assert.That(ClearwaterAtmosphere.Lum(low), Is.LessThan(ClearwaterAtmosphere.Lum(high)));
    }

    [Test]
    public void With_the_sun_18_degrees_down_the_sky_is_all_but_dark()
    {
        var night = ClearwaterAtmosphere.SkyRadiance(Deg(45f), Deg(90f), Deg(-18f));
        var day = ClearwaterAtmosphere.SkyRadiance(Deg(45f), Deg(90f), Deg(45f));

        Assert.That(ClearwaterAtmosphere.Lum(night), Is.LessThan(1e-4f * ClearwaterAtmosphere.Lum(day)));
    }

    [Test]
    public void The_tables_axes_go_both_ways()
    {
        foreach (float d in new[] { -20f, -6f, 0f, 3f, 31f, 90f })
            Assert.That(ClearwaterAtmosphere.SunFromCoord(ClearwaterAtmosphere.SunCoord(Deg(d))) * Mathf.Rad2Deg, Is.EqualTo(d).Within(1e-3f));
        foreach (float d in new[] { 0f, 5f, 45f, 90f })
            Assert.That(ClearwaterAtmosphere.ViewFromCoord(ClearwaterAtmosphere.ViewCoord(Deg(d))) * Mathf.Rad2Deg, Is.EqualTo(d).Within(1e-3f));
        foreach (float d in new[] { 0f, 10f, 90f, 180f })
            Assert.That(ClearwaterAtmosphere.AzFromCoord(ClearwaterAtmosphere.AzCoord(Deg(d))) * Mathf.Rad2Deg, Is.EqualTo(d).Within(1e-3f));
    }
}
