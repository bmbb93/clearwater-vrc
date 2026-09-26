using UnityEngine;

/// <summary>
/// How the bed looks (the ground under the water and up the beach, one look for both): a tiling colour texture and
/// optionally its height, the effects laid over it, and its colour. Chosen on the ClearwaterCoast; the package brings
/// Pebbles and Sand, and Create > Clearwater > Bed Look makes your own. Changes show at once (no bake needed).
/// </summary>
[CreateAssetMenu(menuName = "Clearwater/Bed Look", fileName = "Bed Look", order = 400)]
public class ClearwaterBedLook : ScriptableObject
{
    [Header("Textures")]
    [Tooltip("The bed's colour (sRGB), tiling")]
    public Texture2D color;
    [Tooltip("Its height (optional; white = high): shades the relief and focuses the caustics. None: guessed from the " +
             "colour's brightness")]
    public Texture2D height;
    [Tooltip("Metres one tile of the textures covers")]
    [Min(0.05f)] public float tileSize = 1f;
    [Tooltip("The same texture at 1.7 times the size mixed in, in patches (cobbles among pebbles)")]
    [Range(0f, 1f)] public float largerPatches = 0f;

    [Header("Effects")]
    [Tooltip("Sand gathers in the low parts between the stones, in patches")]
    public bool sandFill;
    [Range(0f, 1f)] public float sandFillAmount = 1f;
    [Tooltip("That sand's colour")]
    public Color sandColor = new Color(0.60f, 0.55f, 0.44f);
    [Tooltip("The bed is sand itself: ripple marks form all over it, not only in the sand between stones")]
    public bool sandBed;
    [Tooltip("Ripple marks in the shallows: ridges along the shore, meandering and forking")]
    public bool rippleMarks;
    [Range(0f, 1f)] public float rippleStrength = 1f;
    [Tooltip("An olive film of weed in patches")]
    public bool weedTint;
    [Range(0f, 1f)] public float weedAmount = 0.7f;

    [Header("Colour")]
    public Color tint = Color.white;
    [Range(0f, 2f)] public float saturation = 1f;
    [Range(0f, 3f)] public float brightness = 1f;
    [Tooltip("Large patches of lighter and darker bed, metres across")]
    [Range(0f, 1f)] public float patchiness = 0.3f;
    [Tooltip("The Pebbles look's muted, darkened grade")]
    [Range(0f, 1f)] public float mutedGrade = 0f;

    static readonly float[] Lum = { 0.3f, 0.55f, 0.15f };

    /// <summary>Puts this look on a material that draws the bed (the water, the seabed): see ClearwaterFloor.cginc.</summary>
    public void ApplyTo(Material m)
    {
        if (m == null || !m.HasProperty("_BedTile")) return;
        m.SetTexture("_Peb", color);
        m.SetTexture("_BedHeight", height);
        m.SetFloat("_BedHasHeight", height != null ? 1f : 0f);
        m.SetFloat("_BedTile", tileSize);
        m.SetFloat("_BedCoarse", largerPatches);
        m.SetFloat("_BedSandFill", sandFill ? sandFillAmount : 0f);
        // (colours go in as the shader uses them: SetColor would convert them from sRGB in a linear project)
        m.SetVector("_BedSandColor", sandColor);
        m.SetFloat("_BedSandBed", sandBed ? 1f : 0f);
        m.SetFloat("_BedRipple", rippleMarks ? rippleStrength : 0f);
        m.SetFloat("_BedWeed", weedTint ? weedAmount : 0f);
        m.SetVector("_BedTint", tint);
        m.SetFloat("_BedSat", saturation);
        m.SetFloat("_BedBright", brightness);
        m.SetFloat("_BedVar", patchiness);
        m.SetFloat("_BedGrade", mutedGrade);
        // (the plain tone the underwater mirror shows where it cannot see the bed: far, dim and blurred, so darker than
        // the look's mean, as the original Pebbles tone was)
        m.SetVector("_BedMean", MeanColour() * 0.6f);
    }

    /// <summary>The look's average colour as the shader grades it (the texture's mean, linear), for what is too far
    /// or too blurred to texture.</summary>
    public Color MeanColour()
    {
        Vector3 c = new Vector3(0.3f, 0.3f, 0.3f);
        if (color != null && color.isReadable)
        {
            var px = color.GetPixels32(Mathf.Min(color.mipmapCount - 1, 6));
            Vector3 acc = Vector3.zero;
            foreach (var p in px) acc += new Vector3(Mathf.GammaToLinearSpace(p.r / 255f), Mathf.GammaToLinearSpace(p.g / 255f), Mathf.GammaToLinearSpace(p.b / 255f));
            c = acc / Mathf.Max(px.Length, 1);
        }
        else if (color != null) c = MeanFromGpu(color);
        if (sandFill)
        {
            Vector3 s = new Vector3(sandColor.r, sandColor.g, sandColor.b);
            c = Vector3.Lerp(c, Vector3.Scale(s, s) * 1.4f, 0.35f * sandFillAmount);
        }
        float lum = c.x * Lum[0] + c.y * Lum[1] + c.z * Lum[2];
        c = Vector3.Scale(Vector3.Lerp(new Vector3(lum, lum, lum), c, saturation), new Vector3(tint.r, tint.g, tint.b));
        if (weedTint) c = Vector3.Lerp(c, Vector3.Scale(c, new Vector3(0.55f, 0.62f, 0.40f)), 0.3f * weedAmount);
        var graded = (new Vector3(0.30f, 0.29f, 0.27f) * 0.28f + new Vector3(Mathf.Pow(c.x, 1.2f), Mathf.Pow(c.y, 1.2f), Mathf.Pow(c.z, 1.2f)) * 0.72f) * 0.6f;
        c = Vector3.Lerp(c, graded, mutedGrade) * brightness;
        return new Color(c.x, c.y, c.z, 1f);
    }

    // a texture that cannot be read on the CPU: its smallest mip, drawn through a small render texture
    public static Vector3 MeanFromGpu(Texture2D t)
    {
        var rt = RenderTexture.GetTemporary(8, 8, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        var prev = RenderTexture.active;
        Graphics.Blit(t, rt);
        RenderTexture.active = rt;
        var read = new Texture2D(8, 8, TextureFormat.RGBAFloat, false, true);
        read.ReadPixels(new Rect(0, 0, 8, 8), 0, 0);
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        Vector3 acc = Vector3.zero;
        foreach (var p in read.GetPixels()) acc += new Vector3(p.r, p.g, p.b);
        Object.DestroyImmediate(read);
        return acc / 64f;
    }
}
