using UnityEngine;

/// <summary>
/// The sky of a clear day, from dawn through dusk to a moonlit night, worked out once from the physics of the air and
/// baked for the shaders: sunlight scattered by the air's molecules (Rayleigh: blue sky, red sunsets), by its fine
/// particles (Mie: the glare round the sun, the pale horizon) and absorbed by ozone (the deep blue of twilight), in a
/// spherical atmosphere 100 km deep, light scattered many times included (Hillaire 2020's multiple scattering
/// approximation). Seen from the ground: the sky's radiance for each view direction and sun elevation (a 3D table the
/// shaders read), and for each sun elevation the direct sunlight reaching the ground, the light of the whole sky on
/// level ground, the sky's mean brightness and its brightness round the horizon (tables the sky's controller,
/// ClearwaterSky, reads). Nothing of it is worked out per
/// frame. Units: the sunlight above the air is 1 in each channel (radiance per steradian of it).
/// </summary>
public static class ClearwaterAtmosphere
{
    // ---- the table's axes (the shaders read them the same way: cwSkyTable in ClearwaterCommon.cginc)
    public const int AzRes = 64, ElRes = 64, SunRes = 48;
    public const float SunMinDeg = -20f, SunMaxDeg = 90f;
    const float SunScale = 5f * Mathf.Deg2Rad; // (sun elevations crowd round the horizon: asinh(elevation / 5 deg))

    /// <summary>Table coordinate (0..1) of a sun elevation (radians).</summary>
    public static float SunCoord(float s) => (Asinh(s / SunScale) - A0) / (A1 - A0);
    public static float SunFromCoord(float w) => SunScale * Sinh(A0 + w * (A1 - A0));
    /// <summary>Of a view elevation (radians, 0 at the horizon): the rows crowd toward the horizon.</summary>
    public static float ViewCoord(float e) => Mathf.Sqrt(Mathf.Clamp01(e / (0.5f * Mathf.PI)));
    public static float ViewFromCoord(float v) => v * v * 0.5f * Mathf.PI;
    /// <summary>Of the azimuth from the sun's (radians, 0..pi): the columns crowd toward the sun's side.</summary>
    public static float AzCoord(float a) => Mathf.Sqrt(Mathf.Clamp01(a / Mathf.PI));
    public static float AzFromCoord(float u) => u * u * Mathf.PI;

    static readonly float A0 = Asinh(SunMinDeg * Mathf.Deg2Rad / SunScale), A1 = Asinh(SunMaxDeg * Mathf.Deg2Rad / SunScale);
    static float Asinh(float x) => Mathf.Log(x + Mathf.Sqrt(x * x + 1f));
    static float Sinh(float x) => 0.5f * (Mathf.Exp(x) - Mathf.Exp(-x));

    // ---- the air (km)
    const float Rg = 6360f, Rt = 6460f, Observer = 0.02f; // ground, top, the eye 20 m up
    static readonly Vector3 RayleighScat = new Vector3(5.802e-3f, 13.558e-3f, 33.1e-3f); // per km at sea level
    const float RayleighH = 8f;
    const float MieScat = 3.996e-3f, MieExt = 4.40e-3f, MieH = 1.2f, MieG = 0.8f;
    static readonly Vector3 OzoneAbs = new Vector3(0.650e-3f, 1.881e-3f, 0.085e-3f); // per km at its peak, 25 km up
    const float GroundAlbedo = 0.1f; // (mostly sea)

    /// <summary>Per sun elevation (the table's rows, SunMinDeg to SunMaxDeg in TableStep steps): the direct sunlight on a
    /// surface facing the sun (rgb), the whole sky's light on level ground (rgb; w = the sky's mean luminance) and the
    /// sky's radiance low over the horizon, all round it (rgb).</summary>
    public const float TableStep = 0.5f;
    public static int TableCount => Mathf.RoundToInt((SunMaxDeg - SunMinDeg) / TableStep) + 1;

    public class Result
    {
        public Texture3D sky;         // radiance / the sky's mean luminance at that sun elevation (so half floats hold dusk)
        public Vector4[] sunDirect;   // per table row
        public Vector4[] skyLight;    // per table row; w = the mean luminance the sky's slice was divided by
        public Vector4[] horizon;     // per table row
    }

    /// <summary>The elevation (degrees) of the sky taken as the horizon's, for the light of the world's upright faces.</summary>
    public const float HorizonDeg = 5f;

    public static Result Bake()
    {
        BuildTransmittance();
        BuildMultipleScattering();
        var px = new Color[AzRes * ElRes * SunRes];
        var norm = new float[SunRes];
        var irr = new Vector3[SunRes];
        var hor = new Vector3[SunRes];
        for (int k = 0; k < SunRes; k++)
        {
            float s = SunFromCoord(k / (SunRes - 1f));
            var slice = new Vector3[AzRes * ElRes];
            double lum = 0, wsum = 0; Vector3 e = Vector3.zero;
            for (int j = 0; j < ElRes; j++)
            {
                float el = ViewFromCoord(j / (ElRes - 1f));
                for (int i = 0; i < AzRes; i++)
                {
                    float az = AzFromCoord(i / (AzRes - 1f));
                    var L = SkyRadiance(el, az, s);
                    slice[j * AzRes + i] = L;
                }
            }
            // the sky's mean luminance and its light on level ground, over the upper hemisphere (both halves of it: the
            // table holds one, azimuth 0..pi)
            const int NE = 48, NA = 48;
            for (int j = 0; j < NE; j++)
            {
                float el = (j + 0.5f) / NE * 0.5f * Mathf.PI;
                float dOmega = Mathf.Cos(el) * (0.5f * Mathf.PI / NE) * (Mathf.PI / NA) * 2f;
                for (int i = 0; i < NA; i++)
                {
                    float az = (i + 0.5f) / NA * Mathf.PI;
                    var L = SampleSlice(slice, el, az);
                    float y = Lum(L);
                    lum += y * dOmega; wsum += dOmega;
                    e += L * (Mathf.Sin(el) * dOmega);
                }
            }
            norm[k] = Mathf.Max((float)(lum / wsum), 1e-12f);
            irr[k] = e;
            for (int i = 0; i < NA; i++) hor[k] += SampleSlice(slice, HorizonDeg * Mathf.Deg2Rad, (i + 0.5f) / NA * Mathf.PI) / NA;
            for (int n = 0; n < slice.Length; n++)
            {
                var v = slice[n] / norm[k];
                px[k * AzRes * ElRes + n] = new Color(v.x, v.y, v.z, 1f);
            }
        }
        var tex = new Texture3D(AzRes, ElRes, SunRes, TextureFormat.RGBAHalf, false)
        {
            name = "SkyLUT", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
        };
        tex.SetPixels(px);
        tex.Apply(false, false);

        // the rows at even steps of sun elevation, from the slices (log-interpolated: dusk falls by orders of magnitude)
        int count = TableCount;
        var result = new Result { sky = tex, sunDirect = new Vector4[count], skyLight = new Vector4[count], horizon = new Vector4[count] };
        for (int r = 0; r < count; r++)
        {
            float s = (SunMinDeg + r * TableStep) * Mathf.Deg2Rad;
            float w = SunCoord(s) * (SunRes - 1);
            int k0 = Mathf.Clamp(Mathf.FloorToInt(w), 0, SunRes - 2);
            float t = Mathf.Clamp01(w - k0);
            float nl = Mathf.Exp(Mathf.Lerp(Mathf.Log(norm[k0]), Mathf.Log(norm[k0 + 1]), t));
            Vector3 e0 = irr[k0], e1 = irr[k0 + 1];
            var e = new Vector3(LogLerp(e0.x, e1.x, t), LogLerp(e0.y, e1.y, t), LogLerp(e0.z, e1.z, t));
            result.skyLight[r] = new Vector4(e.x, e.y, e.z, nl);
            Vector3 h0 = hor[k0], h1 = hor[k0 + 1];
            result.horizon[r] = new Vector4(LogLerp(h0.x, h1.x, t), LogLerp(h0.y, h1.y, t), LogLerp(h0.z, h1.z, t), 0f);
            var d = Transmittance(Rg + Observer, Mathf.Sin(s));
            result.sunDirect[r] = new Vector4(d.x, d.y, d.z, 0f);
        }
        return result;
    }

    static float LogLerp(float a, float b, float t) => Mathf.Exp(Mathf.Lerp(Mathf.Log(Mathf.Max(a, 1e-20f)), Mathf.Log(Mathf.Max(b, 1e-20f)), t));
    public static float Lum(Vector3 c) => 0.2126f * c.x + 0.7152f * c.y + 0.0722f * c.z;

    static Vector3 SampleSlice(Vector3[] slice, float el, float az)
    {
        float fx = AzCoord(az) * (AzRes - 1), fy = ViewCoord(el) * (ElRes - 1);
        int x = Mathf.Min((int)fx, AzRes - 2), y = Mathf.Min((int)fy, ElRes - 2);
        float tx = fx - x, ty = fy - y;
        var a = Vector3.Lerp(slice[y * AzRes + x], slice[y * AzRes + x + 1], tx);
        var b = Vector3.Lerp(slice[(y + 1) * AzRes + x], slice[(y + 1) * AzRes + x + 1], tx);
        return Vector3.Lerp(a, b, ty);
    }

    // ---- the air's make-up at height h above the ground (km)
    static void Media(float h, out Vector3 scatR, out float scatM, out Vector3 ext)
    {
        float dR = Mathf.Exp(-h / RayleighH), dM = Mathf.Exp(-h / MieH), dO = Mathf.Max(0f, 1f - Mathf.Abs(h - 25f) / 15f);
        scatR = RayleighScat * dR;
        scatM = MieScat * dM;
        ext = scatR + Vector3.one * (MieExt * dM) + OzoneAbs * dO;
    }

    static float PhaseRayleigh(float c) => 3f / (16f * Mathf.PI) * (1f + c * c);
    static float PhaseMie(float c)
    {
        float g = MieG, k = 3f / (8f * Mathf.PI) * (1f - g * g) / (2f + g * g);
        return k * (1f + c * c) / Mathf.Pow(Mathf.Max(1f + g * g - 2f * g * c, 1e-6f), 1.5f);
    }

    // distance along a ray from radius r, cosine mu to the vertical, to the sphere of radius R (-1: none)
    static float ToSphere(float r, float mu, float R)
    {
        float b = r * mu, c = r * r - R * R, disc = b * b - c;
        if (disc < 0f) return -1f;
        float sq = Mathf.Sqrt(disc);
        float t1 = -b - sq, t2 = -b + sq;
        if (t1 > 0f) return t1;
        return t2 > 0f ? t2 : -1f;
    }
    static bool HitsGround(float r, float mu) => mu < 0f && r * r * (mu * mu - 1f) + Rg * Rg >= 0f;

    // ---- transmittance from radius r along cosine mu to the top of the air (0 where the ground is in the way)
    const int TR = 64, TM = 256;
    static Vector3[] _trans;
    static void BuildTransmittance()
    {
        _trans = new Vector3[TR * TM];
        for (int j = 0; j < TR; j++)
            for (int i = 0; i < TM; i++)
            {
                float r = Rg + (Rt - Rg) * j / (TR - 1f), mu = -1f + 2f * i / (TM - 1f);
                _trans[j * TM + i] = HitsGround(r, mu) ? Vector3.zero : Integrate(r, mu);
            }
    }
    static Vector3 Integrate(float r, float mu)
    {
        float d = ToSphere(r, mu, Rt);
        if (d <= 0f) return Vector3.one;
        const int N = 64;
        Vector3 od = Vector3.zero;
        for (int k = 0; k < N; k++)
        {
            float t = (k + 0.5f) / N * d;
            float h = Mathf.Sqrt(r * r + t * t + 2f * r * t * mu) - Rg;
            Media(h, out _, out _, out var ext);
            od += ext * (d / N);
        }
        return new Vector3(Mathf.Exp(-od.x), Mathf.Exp(-od.y), Mathf.Exp(-od.z));
    }
    public static Vector3 Transmittance(float r, float mu)
    {
        if (_trans == null) BuildTransmittance();
        if (HitsGround(r, mu)) return Vector3.zero;
        float fj = Mathf.Clamp((r - Rg) / (Rt - Rg), 0f, 1f) * (TR - 1), fi = Mathf.Clamp01((mu + 1f) * 0.5f) * (TM - 1);
        int j = Mathf.Min((int)fj, TR - 2), i = Mathf.Min((int)fi, TM - 2);
        float tj = fj - j, ti = fi - i;
        var a = Vector3.Lerp(_trans[j * TM + i], _trans[j * TM + i + 1], ti);
        var b = Vector3.Lerp(_trans[(j + 1) * TM + i], _trans[(j + 1) * TM + i + 1], ti);
        return Vector3.Lerp(a, b, tj);
    }

    // ---- light scattered more than once, as seen at radius r with the sun at cosine muS to the vertical (Hillaire's
    // Psi_ms: the second order's light, taken as isotropic, times 1 / (1 - f_ms) for all the orders after it)
    const int MR = 32, MM = 32;
    static Vector3[] _ms;
    static void BuildMultipleScattering()
    {
        if (_trans == null) BuildTransmittance();
        _ms = new Vector3[MR * MM];
        const int ND = 64, NS = 20;
        var dirs = new Vector3[ND];
        for (int n = 0; n < ND; n++) // (a Fibonacci sphere)
        {
            float y = 1f - 2f * (n + 0.5f) / ND, rr = Mathf.Sqrt(1f - y * y), ph = n * 2.39996323f;
            dirs[n] = new Vector3(rr * Mathf.Cos(ph), y, rr * Mathf.Sin(ph));
        }
        float pu = 1f / (4f * Mathf.PI);
        for (int j = 0; j < MR; j++)
            for (int i = 0; i < MM; i++)
            {
                float r = Rg + (Rt - Rg) * (j + 0.5f) / MR, muS = -1f + 2f * (i + 0.5f) / MM;
                var sun = new Vector3(Mathf.Sqrt(1f - muS * muS), muS, 0f);
                Vector3 L2 = Vector3.zero, fms = Vector3.zero;
                foreach (var v in dirs)
                {
                    float mu = v.y;
                    float dGround = HitsGround(r, mu) ? ToSphere(r, mu, Rg) : -1f;
                    float d = dGround > 0f ? dGround : Mathf.Max(ToSphere(r, mu, Rt), 0f);
                    Vector3 T = Vector3.one, Lp = Vector3.zero, Lf = Vector3.zero;
                    for (int k = 0; k < NS; k++)
                    {
                        float dt = d / NS, t = (k + 0.5f) * dt;
                        var p = new Vector3(0f, r, 0f) + v * t;
                        float pr = p.magnitude;
                        Media(pr - Rg, out var sR, out var sM, out var ext);
                        var scat = sR + Vector3.one * sM;
                        var ts = Transmittance(pr, Vector3.Dot(p / pr, sun));
                        var stepT = new Vector3(Mathf.Exp(-ext.x * dt), Mathf.Exp(-ext.y * dt), Mathf.Exp(-ext.z * dt));
                        var integ = Div(Vector3.one - stepT, ext); // (the segment's own extinction integrated)
                        Lp += Mul(T, Mul(Mul(scat, ts) * pu, integ));
                        Lf += Mul(T, Mul(scat, integ));
                        T = Mul(T, stepT);
                    }
                    if (dGround > 0f) // (light off the ground, once)
                    {
                        var g = new Vector3(0f, r, 0f) + v * dGround;
                        var up = g.normalized;
                        Lp += Mul(T, Transmittance(Rg, Vector3.Dot(up, sun))) * (GroundAlbedo / Mathf.PI * Mathf.Max(Vector3.Dot(up, sun), 0f));
                    }
                    L2 += Lp / ND;
                    fms += Lf / ND;
                }
                _ms[j * MM + i] = Div(L2, Vector3.one - fms);
            }
    }
    static Vector3 MultipleScattering(float r, float muS)
    {
        float fj = Mathf.Clamp((r - Rg) / (Rt - Rg) * MR - 0.5f, 0f, MR - 1.001f), fi = Mathf.Clamp((muS + 1f) * 0.5f * MM - 0.5f, 0f, MM - 1.001f);
        int j = (int)fj, i = (int)fi;
        float tj = fj - j, ti = fi - i;
        var a = Vector3.Lerp(_ms[j * MM + i], _ms[j * MM + i + 1], ti);
        var b = Vector3.Lerp(_ms[(j + 1) * MM + i], _ms[(j + 1) * MM + i + 1], ti);
        return Vector3.Lerp(a, b, tj);
    }

    static readonly float GlareCos = Mathf.Cos(3f * Mathf.Deg2Rad);

    /// <summary>The sky's radiance seen from the ground at view elevation el, azimuth az from the sun's, with the sun at
    /// elevation s (radians; the sunlight above the air 1). The sun's disc itself is not in it, nor the core of its glare
    /// (within 3 degrees of it: the shaders draw that).</summary>
    public static Vector3 SkyRadiance(float el, float az, float s)
    {
        if (_ms == null) BuildMultipleScattering();
        var v = new Vector3(Mathf.Cos(el) * Mathf.Cos(az), Mathf.Sin(el), Mathf.Cos(el) * Mathf.Sin(az));
        var sun = new Vector3(Mathf.Cos(s), Mathf.Sin(s), 0f);
        // (the glare's sharp core, within a few degrees of the sun, is finer than the table: the shaders draw it)
        float c = Vector3.Dot(v, sun), pR = PhaseRayleigh(c), pM = PhaseMie(Mathf.Min(c, GlareCos));
        float r0 = Rg + Observer;
        float d = Mathf.Max(ToSphere(r0, v.y, Rt), 0f);
        const int N = 40;
        Vector3 T = Vector3.one, L = Vector3.zero;
        float tPrev = 0f;
        for (int k = 1; k <= N; k++)
        {
            float f = (float)k / N, t = d * f * f; // (denser near the eye, where the air is)
            float dt = t - tPrev, tm = 0.5f * (t + tPrev);
            tPrev = t;
            var p = new Vector3(0f, r0, 0f) + v * tm;
            float pr = p.magnitude;
            Media(pr - Rg, out var sR, out var sM, out var ext);
            float muS = Vector3.Dot(p / pr, sun);
            var ts = Transmittance(pr, muS);
            var ms = MultipleScattering(pr, muS);
            var S = Mul(sR, ts * pR + ms) + sM * (ts * pM + ms);
            var stepT = new Vector3(Mathf.Exp(-ext.x * dt), Mathf.Exp(-ext.y * dt), Mathf.Exp(-ext.z * dt));
            L += Mul(T, Mul(S, Div(Vector3.one - stepT, ext)));
            T = Mul(T, stepT);
        }
        return L;
    }

    static Vector3 Mul(Vector3 a, Vector3 b) => new Vector3(a.x * b.x, a.y * b.y, a.z * b.z);
    static Vector3 Div(Vector3 a, Vector3 b) => new Vector3(a.x / Mathf.Max(b.x, 1e-9f), a.y / Mathf.Max(b.y, 1e-9f), a.z / Mathf.Max(b.z, 1e-9f));
}
