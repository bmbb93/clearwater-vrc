using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A panel in the world for the sky: the hour, the clouds and the length of the day on sliders, the day going by on a
/// toggle, and the hour it is now. Anyone can use it; what they set goes to everyone (ClearwaterSky shares it).
/// Tools > Clearwater > Add Sky Control Panel makes one; the UI calls OnHour, OnClouds, OnDayLength and OnCycle.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class ClearwaterSkyPanel : UdonSharpBehaviour
{
    public ClearwaterSky sky;
    [Tooltip("The hour, 0 to 24")]
    public Slider hourSlider;
    [Tooltip("The cloud cover, 0 to 1")]
    public Slider cloudSlider;
    [Tooltip("The length of the day: whole steps along DayLengths")]
    public Slider dayLengthSlider;
    public Toggle cycleToggle;
    [Tooltip("Shows the hour it is now")]
    public Text hourLabel;
    public Text cloudLabel;
    public Text dayLengthLabel;

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
        if (_showing || sky == null || cloudSlider == null) return;
        sky.SetClouds(cloudSlider.value);
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

    // the sky's state on the panel: the hour moves on while the day goes by
    void Show()
    {
        if (sky == null) return;
        _showing = true;
        float h = sky.Hours();
        if (hourSlider != null && Mathf.Abs(hourSlider.value - h) > 0.01f) hourSlider.value = h;
        float c = sky.Clouds();
        if (cloudSlider != null && Mathf.Abs(cloudSlider.value - c) > 0.001f) cloudSlider.value = c;
        if (cycleToggle != null && cycleToggle.isOn != sky.cycle) cycleToggle.isOn = sky.cycle;
        int step = NearestDayLength(sky.dayMinutes);
        if (dayLengthSlider != null && Mathf.RoundToInt(dayLengthSlider.value) != step) dayLengthSlider.value = step;
        int hh = Mathf.FloorToInt(h) % 24, mm = Mathf.FloorToInt((h - Mathf.Floor(h)) * 60f);
        if (hourLabel != null) hourLabel.text = "Time  " + hh.ToString("00") + ":" + mm.ToString("00");
        if (cloudLabel != null) cloudLabel.text = "Clouds  " + Mathf.RoundToInt(c * 100f) + "%";
        if (dayLengthLabel != null) dayLengthLabel.text = "A day in  " + Duration(sky.dayMinutes);
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
