using NUnit.Framework;
using UnityEngine;

/// <summary>The sky of the time of day (ClearwaterSky) puts the sun, the light and the stars where they belong: at its
/// defaults exactly as the fixed sky had them, and the stars turning with the sky through the night.</summary>
public class SkyClockTests : TestScene
{
    static ClearwaterAtmosphere.Result _baked;

    ClearwaterSky MakeSky()
    {
        _baked = _baked ?? ClearwaterAtmosphere.Bake();
        var sun = Make<Light>("Sun", Vector3.zero);
        sun.type = LightType.Directional;
        var sky = sun.gameObject.AddComponent<ClearwaterSky>();
        sky.sunDirect = _baked.sunDirect;
        sky.skyLight = _baked.skyLight;
        sky.horizon = _baked.horizon;
        sky.tableStart = ClearwaterAtmosphere.SunMinDeg;
        sky.tableStep = ClearwaterAtmosphere.TableStep;
        sky.referenceElevation = Mathf.Asin(ClearwaterSetup.SunVector().y) * Mathf.Rad2Deg;
        sky.sunLight = sun;
        sky.north = ClearwaterSkySetup.DefaultNorth(sky.timeOfDay, sky.latitude, sky.dayOfYear);
        return sky;
    }

    [Test]
    public void At_its_defaults_the_sun_stands_and_shines_as_the_fixed_skys_did()
    {
        var sky = MakeSky();

        sky.ShowHour(sky.timeOfDay);

        Assert.That(Vector3.Angle(-sky.sunLight.transform.forward, ClearwaterSetup.SunVector()), Is.LessThan(0.2f));
        Vector4 c = Shader.GetGlobalVector("_Udon_CWSunColor");
        Assert.That(new Vector3(c.x, c.y, c.z), Is.EqualTo(new Vector3(6.0f, 5.4f, 4.44f)).Using(new Vector3Close(0.1f)));
    }

    [Test]
    public void Through_the_night_the_stars_turn_with_the_sky()
    {
        var sky = MakeSky();

        // the sun, seen in the stars' own frame (as the sky shader turns directions into it), stays put as the hours go
        Vector3 early = SunAmongTheStars(sky, 20f), late = SunAmongTheStars(sky, 2f);

        Assert.That(Vector3.Angle(early, late), Is.LessThan(0.5f));
    }

    static Vector3 SunAmongTheStars(ClearwaterSky sky, float hours)
    {
        sky.ShowHour(hours);
        Vector4 s = Shader.GetGlobalVector("_Udon_CWSun"), st = Shader.GetGlobalVector("_Udon_CWStars");
        Vector3 d = new Vector3(s.x, s.y, -s.z).normalized, p = new Vector3(st.x, st.y, -st.z).normalized; // (the shaders' axes)
        float ca = Mathf.Cos(st.w), sa = Mathf.Sin(st.w);
        return d * ca + Vector3.Cross(p, d) * sa + p * Vector3.Dot(p, d) * (1f - ca);
    }

    class Vector3Close : System.Collections.Generic.IEqualityComparer<Vector3>
    {
        readonly float _tol;
        public Vector3Close(float tol) { _tol = tol; }
        public bool Equals(Vector3 a, Vector3 b) => (a - b).magnitude <= _tol;
        public int GetHashCode(Vector3 v) => 0;
    }
}
