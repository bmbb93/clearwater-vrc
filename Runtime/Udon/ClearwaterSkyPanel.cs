using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A panel in the world for the sky: the hour and the clouds on sliders, the day going by on a toggle, and the hour it
/// is now. Anyone can use it; what they set goes to everyone (ClearwaterSky shares it). Tools > Clearwater > Add Sky
/// Control Panel makes one; the UI calls OnHour, OnClouds and OnCycle.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class ClearwaterSkyPanel : UdonSharpBehaviour
{
    public ClearwaterSky sky;
    [Tooltip("The hour, 0 to 24")]
    public Slider hourSlider;
    [Tooltip("The cloud cover, 0 to 1")]
    public Slider cloudSlider;
    public Toggle cycleToggle;
    [Tooltip("Shows the hour it is now")]
    public Text hourLabel;
    public Text cloudLabel;

    bool _showing; // (while the panel shows the sky's state: the UI's events it sets off are not the viewer's)
    float _nextShow;

    void Start() { Show(); }

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
        int hh = Mathf.FloorToInt(h) % 24, mm = Mathf.FloorToInt((h - Mathf.Floor(h)) * 60f);
        if (hourLabel != null) hourLabel.text = "Time  " + hh.ToString("00") + ":" + mm.ToString("00");
        if (cloudLabel != null) cloudLabel.text = "Clouds  " + Mathf.RoundToInt(c * 100f) + "%";
        _showing = false;
    }
}
