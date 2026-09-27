using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// The sky of the time of day: the sun where it stands at that hour at the world's latitude and season, the full moon
/// opposite it and the stars turning round the pole at night, in the light the air gives them (ClearwaterAtmosphere
/// works that out once, in the editor: a table of the sky's radiance the shaders read, and the tables below). Either at
/// a fixed hour, or with the day going by, for everyone in the instance alike (from the server's clock).
/// Everything is handed to the shaders at once (global _Udon_CW* values, ClearwaterCommon.cginc), with the directional
/// light and the ambient light turned and tinted to match. A day that goes by starts at the set hour when the instance
/// opens: its owner shares the server time it started at, so everyone who joins later sees the same hour. The hour, the
/// day going by and the clouds can be changed while the world runs (SetHour, SetCycle, SetClouds; ClearwaterSkyPanel
/// is a panel for them): whoever changes them takes the sky over and shares them. So can the sea's waves (SetShoreWaves,
/// SetSeaWaves), shared along with the sky and set on the water through the controller. Brightness is as the eye sees it: it adapts to the dark, so a
/// moonlit night shows dim and blue (nightBrightness) instead of black, and the light of the fixed sky (the sun 31
/// degrees up) is kept exactly at that elevation.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class ClearwaterSky : UdonSharpBehaviour
{
    [Header("Time of day")]
    [Tooltip("The day goes by, from the hour below when the instance opens, the same for everyone in it; off: the sky stays at the hour below")]
    [UdonSynced] public bool cycle;
    [Tooltip("The hour (local solar time: the sun is highest at 12): the sky's, or the one the day starts from when it goes by")]
    [UdonSynced, Range(0f, 24f)] public float timeOfDay = 16.5f;
    [Tooltip("Real minutes a whole day takes when it goes by")]
    [UdonSynced] public float dayMinutes = 24f;

    [Header("Place and season")]
    [Tooltip("Latitude (degrees, north +): how high the sun climbs, how long the day and the dusk are")]
    [Range(-89f, 89f)] public float latitude = 35f;
    [Tooltip("Day of the year (1 = 1 January; 172 the June solstice, 355 the December one)")]
    [Range(1, 365)] public int dayOfYear = 172;
    [Tooltip("Which way north is in the world (degrees clockwise from +Z)")]
    public float north = 85f;

    [Header("Night")]
    [Tooltip("The full moon, opposite the sun: the night's light")]
    public bool moon = true;
    [Tooltip("How bright a moonlit night looks, as against the day (the eye adapting to the dark)")]
    [Range(0.01f, 0.3f)] public float nightBrightness = 0.05f;
    [Tooltip("How bright the stars show")]
    [Range(0f, 3f)] public float stars = 1f;

    [Header("Scene")]
    [Tooltip("The directional light: turned to the sun (the moon at night) and given its colour")]
    public Light sunLight;
    [Tooltip("A realtime reflection probe of the sky alone (culling mask Nothing), rendered again as the sky changes: the " +
             "reflections of everything else (avatars, the world) then show the sky of the moment")]
    public ReflectionProbe reflectionProbe;
    [Tooltip("Seconds between updates while the day goes by")]
    public float updateInterval = 0.1f;
    [Tooltip("Seconds between renders of the reflection probe while the day goes by")]
    public float probeInterval = 10f;
    [Tooltip("The water's controller: the clouds set from here (SetClouds) go to the sky, the water and the seabed through it")]
    public ClearwaterController controller;

    [Header("Baked (by Build Scene / Use Clearwater Sky and Sun)")]
    public Texture3D skyTable;
    [Tooltip("Per sun elevation: the sunlight on a surface facing the sun")]
    public Vector4[] sunDirect;
    [Tooltip("Per sun elevation: the whole sky's light on level ground; w the sky's mean luminance")]
    public Vector4[] skyLight;
    [Tooltip("Per sun elevation: the sky's radiance low over the horizon")]
    public Vector4[] horizon;
    public float tableStart = -20f, tableStep = 0.5f;
    [Tooltip("The sun elevation (degrees) the light is the fixed sky's at")]
    public float referenceElevation = 31f;
    [Tooltip("The directional light's colour times intensity (linear) there")]
    public Vector3 lightReference = new Vector3(1.3f, 1.104f, 0.831f);
    [Tooltip("The project's lights take linear intensities (Graphics settings)")]
    public bool linearIntensity = true;

    // the full moon's light as against the sun's, and the moonless night's (the airglow's radiance, luminance, and the
    // light of the stars and the airglow on level ground), in the tables' units
    const float MoonRatio = 2.5e-6f;
    const float AirglowLum = 1.2e-9f;
    const float NightIrr = 7.5e-9f;
    const float AirglowTintLum = 0.4504f; // (luminance of the shaders' airglow tint, 0.35, 0.45, 0.75)
    // the light the ground and the sea round about give back into the shade, as a part of the light on level ground
    const float Bounce = 0.15f;
    // how much lower the horizon is from the clouds (degrees: some 2 to 4 km up)
    const float CloudDip = 2.5f;
    // the eye's adaptation: how bright the scene looks goes with its light to this power for a little change from the
    // reference, less and less as the light falls away, so that a moonlit night (this much of the day's light) looks
    // nightBrightness as bright as the day
    const float DayAdaptation = 0.55f;
    const float MoonlitNight = 1e-6f;

    int _idLut, _idSun, _idMoon, _idSunColor, _idMoonColor, _idMoonDisc, _idKey, _idAmbient, _idStars, _idNight, _idCloud, _idHorizon, _idBodies, _idSlices;
    bool _ready;
    Vector3 _sunScale, _wb, _lightScale;
    float _ambScale, _skyScale, _yRef, _meanRef, _x0, _upGain, _sideGain, _downGain;
    float _nextApply, _nextProbe;
    // the server time the instance's day started at (the owner's, shared), and whether it has come; the cloud cover
    // set while the world runs (below 0: the sky material's own)
    [UdonSynced] double _start;
    [UdonSynced] float _clouds = -1f;
    [UdonSynced] float _cloudSpeed = -1f; // (below 0: the sky material's own)
    // the shore waves' height (m) and the sea's small waves' strength set while the world runs (below 0: the water's own)
    [UdonSynced] float _shoreWaves = -1f;
    [UdonSynced] float _seaWaves = -1f;
    bool _started;

    void Start()
    {
        if (Networking.IsOwner(gameObject))
        {
            _start = Networking.GetServerTimeInSeconds();
            _started = true;
            RequestSerialization();
        }
        ApplyNow();
    }

    public override void OnDeserialization()
    {
        _started = true;
        ApplyShared();
        ApplyNow();
    }

    /// <summary>Puts the sky at this hour for everyone (the day, if it goes by, goes on from it).</summary>
    public void SetHour(float hours)
    {
        TakeOver();
        timeOfDay = Mathf.Repeat(hours, 24f);
        _start = Networking.GetServerTimeInSeconds();
        Share();
        ApplyNow();
    }

    /// <summary>Lets the day go by from the hour it is now, or holds it there, for everyone.</summary>
    public void SetCycle(bool on)
    {
        if (on == cycle) return;
        TakeOver();
        timeOfDay = Hours();
        _start = Networking.GetServerTimeInSeconds();
        cycle = on;
        Share();
        ApplyNow();
    }

    /// <summary>Sets the real minutes a whole day takes, for everyone; the day goes on from the hour it is now.</summary>
    public void SetDayMinutes(float minutes)
    {
        minutes = Mathf.Max(minutes, 0.1f);
        if (minutes == dayMinutes) return;
        TakeOver();
        timeOfDay = Hours();
        _start = Networking.GetServerTimeInSeconds();
        dayMinutes = minutes;
        Share();
        ApplyNow();
    }

    /// <summary>Sets how much of the sky the clouds cover (0..1), for everyone.</summary>
    public void SetClouds(float cover)
    {
        TakeOver();
        _clouds = Mathf.Clamp01(cover);
        Share();
        ApplyShared();
        if (reflectionProbe != null) reflectionProbe.RenderProbe();
    }

    /// <summary>Sets how fast the clouds drift (m/s), for everyone; they go on from where they are.</summary>
    public void SetCloudSpeed(float speed)
    {
        TakeOver();
        _cloudSpeed = Mathf.Max(speed, 0f);
        Share();
        ApplyShared();
    }

    /// <summary>Sets how high the shore waves are (m, the breaker height; 0 = none), for everyone.</summary>
    public void SetShoreWaves(float height)
    {
        TakeOver();
        _shoreWaves = Mathf.Max(height, 0f);
        Share();
        ApplyShared();
    }

    /// <summary>Sets how strong the sea's small waves are (0 = a glassy calm, 1 = as built), for everyone.</summary>
    public void SetSeaWaves(float strength)
    {
        TakeOver();
        _seaWaves = Mathf.Clamp01(strength);
        Share();
        ApplyShared();
    }

    /// <summary>The shore waves' height now (m): as set while the world runs, or the water's.</summary>
    public float ShoreWaves() { return _shoreWaves >= 0f || controller == null ? Mathf.Max(_shoreWaves, 0f) : controller.ShoreWaveHeight(); }

    /// <summary>The sea's small waves' strength now (0..1): as set while the world runs, or the water's.</summary>
    public float SeaWaves() { return _seaWaves >= 0f || controller == null ? Mathf.Clamp01(_seaWaves) : controller.SeaWaves(); }

    /// <summary>How fast the clouds drift now (m/s): as set while the world runs, or the sky material's.</summary>
    public float CloudSpeed()
    {
        return _cloudSpeed >= 0f || controller == null ? Mathf.Max(_cloudSpeed, 0f) : controller.CloudSpeed();
    }

    /// <summary>The cloud cover now: as set while the world runs, or the sky material's.</summary>
    public float Clouds() { return _clouds >= 0f || controller == null ? Mathf.Max(_clouds, 0f) : controller.CloudCover(); }

    void TakeOver()
    {
        if (!Networking.IsOwner(gameObject)) Networking.SetOwner(Networking.LocalPlayer, gameObject);
    }

    void Share()
    {
        _started = true;
        RequestSerialization();
    }

    // what was set while the world runs, on the sky and the water (through the controller)
    void ApplyShared()
    {
        if (controller == null) return;
        if (_clouds >= 0f) controller.SetCloudCover(_clouds);
        if (_cloudSpeed >= 0f) controller.SetCloudSpeed(_cloudSpeed);
        if (_shoreWaves >= 0f) controller.SetShoreWaveHeight(_shoreWaves);
        if (_seaWaves >= 0f) controller.SetSeaWaves(_seaWaves);
    }

    void Update()
    {
        if (!cycle || Time.time < _nextApply) return;
        _nextApply = Time.time + updateInterval;
        Apply(Hours());
        if (reflectionProbe != null && Time.time >= _nextProbe)
        {
            _nextProbe = Time.time + probeInterval;
            reflectionProbe.RenderProbe();
        }
    }

    /// <summary>Shows the sky of the moment now (after changing the settings above from another behaviour, say).</summary>
    public void ApplyNow() { ShowHour(Hours()); }

    /// <summary>Shows the sky at the given hour now, the settings above taken afresh, the reflection probe rendered again.</summary>
    public void ShowHour(float hours)
    {
        _ready = false;
        Apply(hours);
        if (reflectionProbe != null) reflectionProbe.RenderProbe();
        _nextProbe = Time.time + probeInterval;
    }

    /// <summary>The hour shown: the fixed one, or where the instance's day has got to (the fixed one until the
    /// owner's start time has come).</summary>
    public float Hours()
    {
        if (!cycle || !_started) return timeOfDay;
        double h = timeOfDay + (Networking.GetServerTimeInSeconds() - _start) / (Mathf.Max(dayMinutes, 0.01f) * 60.0) * 24.0;
        h = h % 24.0;
        return (float)(h < 0.0 ? h + 24.0 : h);
    }

    // the light at the reference, and the scales that make it the fixed sky's there
    void Init()
    {
        _idLut = VRCShader.PropertyToID("_Udon_CWSkyLUT");
        _idSun = VRCShader.PropertyToID("_Udon_CWSun");
        _idMoon = VRCShader.PropertyToID("_Udon_CWMoon");
        _idSunColor = VRCShader.PropertyToID("_Udon_CWSunColor");
        _idMoonColor = VRCShader.PropertyToID("_Udon_CWMoonColor");
        _idMoonDisc = VRCShader.PropertyToID("_Udon_CWMoonDisc");
        _idKey = VRCShader.PropertyToID("_Udon_CWKey");
        _idAmbient = VRCShader.PropertyToID("_Udon_CWAmbient");
        _idStars = VRCShader.PropertyToID("_Udon_CWStars");
        _idNight = VRCShader.PropertyToID("_Udon_CWNight");
        _idCloud = VRCShader.PropertyToID("_Udon_CWCloudLight");
        _idHorizon = VRCShader.PropertyToID("_Udon_CWHorizon");
        _idBodies = VRCShader.PropertyToID("_Udon_CWBodies");
        _idSlices = VRCShader.PropertyToID("_Udon_CWSlices");

        float s = referenceElevation, sinR = Mathf.Sin(s * Mathf.Deg2Rad);
        Vector3 d = Row(sunDirect, s), e = Row(skyLight, s), h = Row(horizon, s);
        float mean = Row(skyLight, s).w;
        // the fixed sky's: its sun, its light from the whole sky, its mean brightness (ClearwaterCommon.cginc)
        Vector3 fixedSun = new Vector3(6.0f, 5.4f, 4.44f);
        Vector3 fixedAmb = new Vector3(0.62f, 0.70f, 0.78f) * (Mathf.PI * 0.22f);
        const float FixedSkyMean = 0.4397f, FixedSkyHorizon = 0.595f; // (its mean luminance; all round, 5 degrees up)
        // the white balance and the exposure, one for all the light: the sun exactly the fixed sky's. The light in the
        // shade (the sky's, and what the ground gives back) and the sky itself as bright as the fixed sky's, in their
        // own colours
        _sunScale = Div(fixedSun, d);
        _wb = _sunScale / Lum(_sunScale); // (the white balance alone)
        Vector3 ambRef = Vector3.Scale(e + Bounce * (d * sinR + e), _sunScale);
        _ambScale = Lum(fixedAmb) / Mathf.Max(Lum(ambRef), 1e-20f);
        // the sky itself: halfway (in log terms) between as bright as the fixed sky on the whole and as bright low
        // round the horizon. The real sky is brighter there (the haze) and darker overhead than the fixed one: matched
        // on the whole, its horizon ran into white; matched there, its zenith went dark
        float onWhole = FixedSkyMean / Mathf.Max(mean, 1e-20f);
        float onHorizon = FixedSkyHorizon / Mathf.Max(Lum(Vector3.Scale(h, _sunScale / Lum(_sunScale))), 1e-20f);
        _skyScale = Mathf.Sqrt(onWhole * onHorizon);
        _yRef = Seen(d, sinR) + Lum(e);
        _meanRef = mean;
        float xn = -Mathf.Log(MoonlitNight), nb = -Mathf.Log(Mathf.Clamp(nightBrightness, 0.005f, 0.9f));
        _x0 = xn / Mathf.Max(DayAdaptation * xn / nb - 1f, 1e-3f);
        _lightScale = Div(lightReference, fixedSun);
        // Unity's ambient light (avatars, the world: from above, round the horizon, from below) as bright as the fixed
        // sky's skybox gave it, from the light in the shade and the horizon's
        Vector3 horRef = Vector3.Scale(h, _wb) * _skyScale;
        _upGain = Lum(new Vector3(0.450f, 0.558f, 0.779f)) / Lum(fixedAmb);
        _sideGain = Lum(new Vector3(0.541f, 0.645f, 0.793f)) / Mathf.Max(Lum(horRef), 1e-20f);
        _downGain = Lum(new Vector3(0.574f, 0.678f, 0.781f)) / Mathf.Max(Lum(horRef), 1e-20f);
        _ready = true;
    }

    /// <summary>Puts the sky, the light and the shaders at the given hour.</summary>
    public void Apply(float hours)
    {
        if (sunDirect == null || sunDirect.Length < 2 || skyLight == null || horizon == null) return;
        if (!_ready) Init();

        // ---- where the sun is: its declination for the season, its hour angle, at the latitude (east, north, up)
        float dec = -23.44f * Mathf.Deg2Rad * Mathf.Cos(2f * Mathf.PI / 365f * (dayOfYear + 10));
        float ha = (hours - 12f) * 15f * Mathf.Deg2Rad, lat = latitude * Mathf.Deg2Rad;
        float cd = Mathf.Cos(dec), sd = Mathf.Sin(dec), cl = Mathf.Cos(lat), sl = Mathf.Sin(lat);
        float ch = Mathf.Cos(ha), sh = Mathf.Sin(ha);
        float yaw = north * Mathf.Deg2Rad;
        Vector3 northW = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw)), eastW = new Vector3(Mathf.Cos(yaw), 0f, -Mathf.Sin(yaw));
        Vector3 sunW = (eastW * (-cd * sh) + northW * (sd * cl - cd * ch * sl) + Vector3.up * (sd * sl + cd * ch * cl)).normalized;
        Vector3 moonW = -sunW; // (the full moon)
        Vector3 pole = northW * cl + Vector3.up * sl;
        // the stars turn with the hour, and through the year against the sun
        float starAngle = ha + 2f * Mathf.PI * (dayOfYear - 80) / 365.25f;
        float sEl = Mathf.Asin(Mathf.Clamp(sunW.y, -1f, 1f)) * Mathf.Rad2Deg, mEl = -sEl;
        // (below astronomical twilight the sun's light in the sky is under the night's own: gone by the table's end)
        float dusk = Mathf.Clamp01((sEl - tableStart) / 4f);

        // ---- the light (the tables' units): the sun's, the moon's, the sky's, the airglow and stars'
        float sinS = Mathf.Max(sunW.y, 0f), sinM = Mathf.Max(moonW.y, 0f);
        Vector3 sunD = Row(sunDirect, sEl);
        Vector4 skyS = Row(skyLight, sEl);
        Vector3 sky = skyS, hor = Row(horizon, sEl) * dusk;
        Vector3 ground = sunD * sinS + sky; // (on level ground)
        Vector3 moonD = Vector3.zero;
        float moonMean = 0f;
        if (moon)
        {
            // (the moon's soil gives back red light more than blue: moonlight is warmer than sunlight, some 4,100 K;
            // its luminance kept)
            moonD = Vector3.Scale(MoonRatio * (Vector3)Row(sunDirect, mEl), new Vector3(1.107f, 0.988f, 0.810f));
            Vector4 skyM = Row(skyLight, mEl);
            sky += MoonRatio * (Vector3)skyM;
            hor += MoonRatio * (Vector3)Row(horizon, mEl);
            ground += moonD * sinM + MoonRatio * (Vector3)skyM;
            moonMean = MoonRatio * skyM.w;
        }
        Vector3 amb = sky + Bounce * ground + Vector3.one * NightIrr;
        // the light of the scene against the reference's: half the view the lit world, half the sky itself
        float r = 0.5f * (Seen(sunD, sinS) + Seen(moonD, sinM) + Lum(sky) + NightIrr) / _yRef
                + 0.5f * (skyS.w * dusk + moonMean + AirglowLum) / _meanRef;
        // the eye's adaptation, and its night vision: colours fade and turn blue in the dark
        float adapt = Adaptation(r);
        float night = Mathf.Clamp01(Mathf.Log(r / 3e-3f) / Mathf.Log(1e-2f));

        Vector3 sunC = Night(Vector3.Scale(sunD, _sunScale) * adapt, night);
        Vector3 moonC = Night(Vector3.Scale(moonD, _sunScale) * adapt, night);
        Vector3 ambC = Night(Vector3.Scale(amb, _sunScale) * (_ambScale * adapt), night);
        float airglow = AirglowLum / AirglowTintLum * _skyScale * adapt;
        Vector3 horC = Night(Vector3.Scale(hor, _wb) * (_skyScale * adapt) + new Vector3(0.35f, 0.45f, 0.75f) * airglow, night);
        // the light that shades: the sun, or the moon once it gives more light
        bool byMoon = moon && (Lum(moonD) > Lum(sunD) || (Lum(sunD) <= 0f && mEl > sEl));
        Vector3 keyW = byMoon ? moonW : sunW;
        if (keyW.y < 0.0175f) // (never from under the horizon, where it gives no light: at least 1 degree up)
        {
            Vector3 flat = new Vector3(keyW.x, 0f, keyW.z);
            flat = flat.sqrMagnitude > 1e-8f ? flat.normalized : Vector3.forward;
            keyW = flat * 0.99985f + Vector3.up * 0.0175f;
        }
        Vector3 keyC = byMoon ? moonC : sunC;
        // the clouds, a couple of km up, see the sun over a lower horizon: lit red a while after it has set below
        Vector3 cloudSun = Night(Vector3.Scale(Row(sunDirect, sEl + CloudDip), _sunScale) * adapt, night);
        Vector3 cloudC = cloudSun + moonC;
        float cloudBySun = Lum(cloudSun) >= Lum(moonC) ? 1f : 0f; // (which way their light comes from)

        // ---- the shaders
        if (skyTable != null) VRCShader.SetGlobalTexture(_idLut, skyTable);
        // (the sunlit sky or the moonlit one left out while it is lost in the other: a table lookup less per pixel)
        float sunSky = skyS.w * dusk, moonSky = moonMean;
        if (moonSky < 1e-3f * sunSky) moonSky = 0f;
        if (sunSky < 1e-3f * moonSky) sunSky = 0f;
        VRCShader.SetGlobalVector(_idSun, new Vector4(sunW.x, sunW.y, sunW.z, sunSky * _skyScale * adapt));
        VRCShader.SetGlobalVector(_idMoon, new Vector4(moonW.x, moonW.y, moonW.z, moonSky * _skyScale * adapt));
        VRCShader.SetGlobalVector(_idSunColor, new Vector4(sunC.x, sunC.y, sunC.z, 1f));
        VRCShader.SetGlobalVector(_idMoonColor, new Vector4(moonC.x, moonC.y, moonC.z, moon && moonW.y > -0.01f ? 1f : 0f));
        // the disc: its radiance (its light over the sky it covers, 6.4e-5 sr: as bright as the day's sky), seen as
        // the sky is, softly held under 0.6 (well under the tone curve's shoulder: on screen it must read as far dimmer than the sun, its maria showing) once the eye has got used to the night; bright
        // enough for the eye to see its colour (no night vision on it)
        float discL = Lum(moonD) / 6.4e-5f * _skyScale * adapt;
        Vector3 discC = Vector3.Scale(moonD, _sunScale);
        discC = discC / Mathf.Max(Lum(discC), 1e-30f) * (discL / (1f + discL / 0.6f));
        VRCShader.SetGlobalVector(_idMoonDisc, new Vector4(discC.x, discC.y, discC.z, 0f));
        VRCShader.SetGlobalVector(_idKey, new Vector4(keyW.x, keyW.y, keyW.z, byMoon ? 1f : 0f));
        VRCShader.SetGlobalVector(_idAmbient, new Vector4(ambC.x, ambC.y, ambC.z, airglow));
        VRCShader.SetGlobalVector(_idStars, new Vector4(pole.x, pole.y, pole.z, starAngle));
        float starSeen = stars * Mathf.Min(adapt / Adaptation(MoonlitNight), 4f);
        if (starSeen < 1e-3f) starSeen = 0f; // (not a star shows: the sky shader skips them)
        VRCShader.SetGlobalVector(_idNight, new Vector4(starSeen, night, adapt, 0f));
        VRCShader.SetGlobalVector(_idCloud, new Vector4(cloudC.x, cloudC.y, cloudC.z, cloudBySun));
        VRCShader.SetGlobalVector(_idHorizon, new Vector4(horC.x, horC.y, horC.z, 0f));
        // (worked out here once, not for every pixel: the bodies across the ground and their slices of the table)
        Vector2 sunH = Across(sunW), moonH = Across(moonW);
        VRCShader.SetGlobalVector(_idBodies, new Vector4(sunH.x, sunH.y, moonH.x, moonH.y));
        VRCShader.SetGlobalVector(_idSlices, new Vector4(Slice(sEl), Slice(mEl), 0f, 0f));

        // ---- the directional light, and Unity's ambient light (avatars, the world)
        if (sunLight != null)
        {
            sunLight.transform.rotation = Quaternion.LookRotation(-keyW);
            Vector3 lin = Vector3.Scale(keyC, _lightScale);
            float mx = Mathf.Max(Mathf.Max(lin.x, lin.y), Mathf.Max(lin.z, 1e-3f));
            if (linearIntensity)
            {
                sunLight.color = new Color(Gamma(lin.x / mx), Gamma(lin.y / mx), Gamma(lin.z / mx));
                sunLight.intensity = mx;
            }
            else
            {
                Vector3 g = new Vector3(Gamma(lin.x), Gamma(lin.y), Gamma(lin.z));
                float gm = Mathf.Max(Mathf.Max(g.x, g.y), Mathf.Max(g.z, 1e-3f));
                sunLight.color = new Color(g.x / gm, g.y / gm, g.z / gm);
                sunLight.intensity = gm;
            }
        }
        Vector3 up = ambC * _upGain, side = horC * _sideGain, down = horC * _downGain;
        // (Unity's trilight colours that give these: its ambient light is a blend of the three)
        Vector3 skyT = up * 2.15189f - side * 1.46656f + down * 0.31467f;
        Vector3 eqT = up * -0.36672f + side * 1.73344f - down * 0.36672f;
        Vector3 gndT = up * 0.31467f - side * 1.46656f + down * 2.15189f;
        RenderSettings.ambientSkyColor = GammaColor(skyT);
        RenderSettings.ambientEquatorColor = GammaColor(eqT);
        RenderSettings.ambientGroundColor = GammaColor(gndT);
    }

    // how much a light (on a face toward it, at elevation sin) counts toward the scene's brightness the eye adapts to:
    // most of what is seen lies level, but not all of it
    float Seen(Vector3 direct, float sin) { return Lum(direct) * (0.3f + 0.7f * sin); }

    // the eye's gain for light r times the reference's: how bright it looks (as the log of it, f(log r)) over r
    float Adaptation(float r)
    {
        float x = Mathf.Log(Mathf.Max(r, 1e-30f));
        float f = x < 0f ? DayAdaptation * x / (1f - x / _x0) : DayAdaptation * x / (1f + x * 0.5f);
        return Mathf.Exp(f - x);
    }

    // a direction across the ground, in the shaders' axes (z the other way), normalised
    Vector2 Across(Vector3 w)
    {
        Vector2 h = new Vector2(w.x, -w.z);
        return h.sqrMagnitude > 1e-12f ? h.normalized : new Vector2(1f, 0f);
    }

    // the table's slice (texel-centred) for a body's elevation (degrees): ClearwaterAtmosphere.SunCoord, 48 slices
    // from -20 to 90 degrees, crowded round the horizon (asinh of the elevation over 5 degrees)
    float Slice(float deg)
    {
        float x = Mathf.Clamp(deg, -20f, 90f) / 5f;
        float a = Mathf.Log(x + Mathf.Sqrt(x * x + 1f));
        const float A0 = -2.0947125f, A1 = 3.5842897f; // (asinh(-4), asinh(18))
        return ((a - A0) / (A1 - A0) * 47f + 0.5f) / 48f;
    }

    Vector4 Row(Vector4[] t, float deg)
    {
        float f = (deg - tableStart) / tableStep;
        int n = t.Length;
        if (f <= 0f) return t[0];
        if (f >= n - 1) return t[n - 1];
        int i = (int)f;
        return Vector4.Lerp(t[i], t[i + 1], f - i);
    }

    // the colour c as seen in the dark: toward its luminance in blue
    Vector3 Night(Vector3 c, float n) { return Vector3.Lerp(c, new Vector3(0.80f, 1.02f, 1.44f) * Lum(c), n * 0.75f); }

    float Lum(Vector3 c) { return 0.2126f * c.x + 0.7152f * c.y + 0.0722f * c.z; }
    Vector3 Div(Vector3 a, Vector3 b) { return new Vector3(a.x / Mathf.Max(b.x, 1e-20f), a.y / Mathf.Max(b.y, 1e-20f), a.z / Mathf.Max(b.z, 1e-20f)); }
    float Gamma(float x) { return Mathf.LinearToGammaSpace(Mathf.Max(x, 0f)); }
    Color GammaColor(Vector3 c) { return new Color(Gamma(c.x), Gamma(c.y), Gamma(c.z)); }
}
