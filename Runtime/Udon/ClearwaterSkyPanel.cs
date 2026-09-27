using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A panel in the world for the sky and the sea: the hour, the clouds, how fast they drift and the length of the day on
/// sliders, the day going by on a toggle, and the hour it is now; the shore waves' height, the sea's small waves and how
/// fast they go on sliders; a button that puts it all back as the world starts. Anyone can use it; what they set goes
/// to everyone (the hour, the day going by and its length through ClearwaterSky, the clouds and the waves through
/// ClearwaterSettings, which holds their start values). Tools > Clearwater > Add Sky Control Panel makes one; the UI calls OnHour,
/// OnClouds, OnCloudSpeed, OnDayLength, OnCycle, OnShoreWaves, OnSeaWaves, OnRippleSpeed and OnReset.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class ClearwaterSkyPanel : UdonSharpBehaviour
{
    public ClearwaterSky sky;
    [Tooltip("The panel's settings: the clouds and the waves, their start values, and Reset all")]
    public ClearwaterSettings settings;
    [Tooltip("The hour, 0 to 24")]
    public Slider hourSlider;
    [Tooltip("The cloud cover, 0 to 1")]
    public Slider cloudSlider;
    [Tooltip("How fast the clouds drift, m/s")]
    public Slider cloudSpeedSlider;
    [Tooltip("The length of the day: whole steps along DayLengths")]
    public Slider dayLengthSlider;
    public Toggle cycleToggle;
    [Tooltip("The shore waves' height, m")]
    public Slider shoreWaveSlider;
    [Tooltip("The sea's small waves, 0 (calm) to 1 (as built)")]
    public Slider seaWaveSlider;
    [Tooltip("How fast the sea's small waves go, 0 (still) to 2 (twice as built)")]
    public Slider rippleSpeedSlider;
    [Tooltip("Shows the hour it is now")]
    public TextMeshProUGUI hourLabel;
    public TextMeshProUGUI cloudLabel;
    public TextMeshProUGUI cloudSpeedLabel;
    public TextMeshProUGUI dayLengthLabel;
    public TextMeshProUGUI shoreWaveLabel;
    public TextMeshProUGUI seaWaveLabel;
    public TextMeshProUGUI rippleSpeedLabel;

    bool _showing; // (while the panel shows the sky's state: the UI's events it sets off are not the viewer's)
    float _nextShow;

    void OnEnable() { Show(); } // (shown by an opener: the state it has now at once)

    void Update()
    {
        if (Time.time < _nextShow) return;
        _nextShow = Time.time + 0.25f;
        Show();
    }

    public void OnHour()
    {
        if (_showing || sky == null || hourSlider == null) return;
        sky.SetHour(hourSlider.value);
        Show();
    }

    public void OnClouds()
    {
        if (_showing || settings == null || cloudSlider == null) return;
        settings.SetClouds(cloudSlider.value);
        Show();
    }

    public void OnCloudSpeed()
    {
        if (_showing || settings == null || cloudSpeedSlider == null) return;
        settings.SetCloudDrift(cloudSpeedSlider.value);
        Show();
    }

    public void OnDayLength()
    {
        if (_showing || sky == null || dayLengthSlider == null) return;
        float[] steps = DayLengths();
        sky.SetDayMinutes(steps[Mathf.Clamp(Mathf.RoundToInt(dayLengthSlider.value), 0, steps.Length - 1)]);
        Show();
    }

    public void OnCycle()
    {
        if (_showing || sky == null || cycleToggle == null) return;
        sky.SetCycle(cycleToggle.isOn);
        Show();
    }

    public void OnShoreWaves()
    {
        if (_showing || settings == null || shoreWaveSlider == null) return;
        settings.SetShoreWaves(shoreWaveSlider.value);
        Show();
    }

    public void OnSeaWaves()
    {
        if (_showing || settings == null || seaWaveSlider == null) return;
        settings.SetRipples(seaWaveSlider.value);
        Show();
    }

    public void OnRippleSpeed()
    {
        if (_showing || settings == null || rippleSpeedSlider == null) return;
        settings.SetRippleSpeed(rippleSpeedSlider.value);
        Show();
    }

    public void OnReset()
    {
        if (settings == null) return;
        settings.ResetAll();
        Show();
    }

    // the state of the moment on the panel: the hour moves on while the day goes by
    void Show()
    {
        _showing = true;
        if (sky != null)
        {
            float h = sky.Hours();
            if (hourSlider != null && Mathf.Abs(hourSlider.value - h) > 0.01f) hourSlider.value = h;
            if (cycleToggle != null && cycleToggle.isOn != sky.cycle) cycleToggle.isOn = sky.cycle;
            int step = NearestDayLength(sky.dayMinutes);
            if (dayLengthSlider != null && Mathf.RoundToInt(dayLengthSlider.value) != step) dayLengthSlider.value = step;
            int hh = Mathf.FloorToInt(h) % 24, mm = Mathf.FloorToInt((h - Mathf.Floor(h)) * 60f);
            if (hourLabel != null) hourLabel.text = "Time  " + hh.ToString("00") + ":" + mm.ToString("00");
            if (dayLengthLabel != null) dayLengthLabel.text = "A day in  " + Duration(sky.dayMinutes);
        }
        if (settings != null)
        {
            float c = settings.Clouds();
            if (cloudSlider != null && Mathf.Abs(cloudSlider.value - c) > 0.001f) cloudSlider.value = c;
            float v = settings.CloudDrift();
            if (cloudSpeedSlider != null && Mathf.Abs(cloudSpeedSlider.value - v) > 0.01f) cloudSpeedSlider.value = v;
            if (cloudLabel != null) cloudLabel.text = "Clouds  " + Mathf.RoundToInt(c * 100f) + "%";
            if (cloudSpeedLabel != null) cloudSpeedLabel.text = "Cloud drift  " + Mathf.RoundToInt(v) + " m/s";
            float sh = settings.ShoreWaves();
            if (shoreWaveSlider != null && Mathf.Abs(shoreWaveSlider.value - sh) > 0.001f) shoreWaveSlider.value = sh;
            float sw = settings.Ripples();
            if (seaWaveSlider != null && Mathf.Abs(seaWaveSlider.value - sw) > 0.001f) seaWaveSlider.value = sw;
            if (shoreWaveLabel != null) shoreWaveLabel.text = "Shore waves  " + (sh < 0.005f ? "none" : Mathf.RoundToInt(sh * 100f) + " cm");
            if (seaWaveLabel != null) seaWaveLabel.text = "Ripples  " + Mathf.RoundToInt(sw * 100f) + "%";
            float rv = settings.RippleSpeed();
            if (rippleSpeedSlider != null && Mathf.Abs(rippleSpeedSlider.value - rv) > 0.001f) rippleSpeedSlider.value = rv;
            if (rippleSpeedLabel != null) rippleSpeedLabel.text = "Ripple speed  " + Mathf.RoundToInt(rv * 100f) + "%";
        }
        _showing = false;
    }

    /// <summary>The lengths of the day the slider steps through, in real minutes (its value is the index): up to a
    /// real day.</summary>
    public static float[] DayLengths()
    {
        return new float[] { 1f, 2f, 3f, 5f, 10f, 15f, 20f, 30f, 45f, 60f, 90f, 120f, 180f, 240f, 360f, 720f, 1440f };
    }

    /// <summary>The step of DayLengths nearest to these minutes.</summary>
    public static int NearestDayLength(float minutes)
    {
        float[] steps = DayLengths();
        minutes = Mathf.Max(minutes, 0.1f);
        int best = 0;
        for (int i = 1; i < steps.Length; i++)
            if (Mathf.Abs(Mathf.Log(steps[i] / minutes)) < Mathf.Abs(Mathf.Log(steps[best] / minutes))) best = i;
        return best;
    }

    static string Duration(float minutes)
    {
        if (minutes < 60f) return Mathf.RoundToInt(minutes) + " min";
        float h = minutes / 60f;
        return (Mathf.Abs(h - Mathf.Round(h)) < 0.01f ? Mathf.RoundToInt(h).ToString() : h.ToString("0.#")) + " h";
    }
}
