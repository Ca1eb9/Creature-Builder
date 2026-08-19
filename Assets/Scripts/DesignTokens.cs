using UnityEngine;

/// <summary>
/// The "Classical" design-system tokens, ported verbatim from the _ds
/// stylesheet. Single source of truth for the UI's colours, radii and fonts —
/// change a value here and it propagates everywhere that reads it (runtime UI,
/// prefab/scene builders, toasts, dialogs).
/// </summary>
public static class DesignTokens
{
    // ---- Core roles ----
    public static readonly Color Bg      = Hex("#f3f2f2");
    public static readonly Color Surface = Hex("#eae9e9");
    public static readonly Color Text    = Hex("#201f1d");
    public static readonly Color Accent  = Hex("#b68235");
    public static readonly Color Accent2 = Hex("#ac803e");
    public static readonly Color Danger  = Hex("#8c2f2f"); // delete / destructive

    // Divider = ink at 16% over the ground.
    public static readonly Color Divider = Alpha(Hex("#201f1d"), 0.16f);

    // ---- Neutral ramp ----
    public static readonly Color Neutral100 = Hex("#f8f4f4");
    public static readonly Color Neutral200 = Hex("#eae7e7");
    public static readonly Color Neutral300 = Hex("#d7d3d3");
    public static readonly Color Neutral400 = Hex("#bab6b6");
    public static readonly Color Neutral500 = Hex("#9b9797");
    public static readonly Color Neutral600 = Hex("#7d7979");
    public static readonly Color Neutral700 = Hex("#605d5d");
    public static readonly Color Neutral800 = Hex("#444141");
    public static readonly Color Neutral900 = Hex("#2d2b2b");

    // ---- Accent ramp ----
    public static readonly Color Accent100 = Hex("#fff3e4");
    public static readonly Color Accent200 = Hex("#ffe3bf");
    public static readonly Color Accent300 = Hex("#facb8d");
    public static readonly Color Accent400 = Hex("#e1ad66");
    public static readonly Color Accent500 = Hex("#c28d41");
    public static readonly Color Accent600 = Hex("#a06f24");
    public static readonly Color Accent700 = Hex("#7d5411");
    public static readonly Color Accent800 = Hex("#5a3b0a");
    public static readonly Color Accent900 = Hex("#3a270d");

    // ---- Radii (px) ----
    public const float RadiusSm = 2f;
    public const float RadiusMd = 4f;
    public const float RadiusLg = 7f;

    // ---- Fonts (TMP assets live under a Resources folder so runtime UI can load them) ----
    public const string HeadingFontResource = "Fonts/CormorantGaramond-SemiBold SDF";
    public const string DisplayFontResource = "Fonts/CormorantGaramond-Regular SDF";
    public const string BodyFontResource    = "Fonts/Lora-Regular SDF";

    private static TMPro.TMP_FontAsset _heading, _display, _body;
    /// <summary>Cormorant SemiBold (600) — interface headings.</summary>
    public static TMPro.TMP_FontAsset HeadingFont =>
        _heading != null ? _heading : (_heading = Resources.Load<TMPro.TMP_FontAsset>(HeadingFontResource));
    /// <summary>Cormorant Regular (400) — large display text goes lighter, per the design notes.</summary>
    public static TMPro.TMP_FontAsset DisplayFont =>
        _display != null ? _display : (_display = Resources.Load<TMPro.TMP_FontAsset>(DisplayFontResource));
    public static TMPro.TMP_FontAsset BodyFont =>
        _body != null ? _body : (_body = Resources.Load<TMPro.TMP_FontAsset>(BodyFontResource));

    // ---- Reusable 9-sliced sprites (baked by CreatureBuilderThemeTool into Resources/UI) ----
    private static Sprite _outline, _rounded;
    /// <summary>1px hairline frame, transparent center — tint for borders on cards/inputs/buttons/panels.</summary>
    public static Sprite OutlineSprite =>
        _outline != null ? _outline : (_outline = Resources.Load<Sprite>("UI/ui-outline"));
    /// <summary>Rounded-rect fill — tint for panel/card/dialog backgrounds.</summary>
    public static Sprite RoundedSprite =>
        _rounded != null ? _rounded : (_rounded = Resources.Load<Sprite>("UI/ui-rounded"));

    private static Sprite _dashed;
    /// <summary>Dashed 1px frame — the empty "No head" slot card. Use Image.Type.Tiled
    /// so the dash rhythm repeats instead of stretching.</summary>
    public static Sprite DashedSprite =>
        _dashed != null ? _dashed : (_dashed = Resources.Load<Sprite>("UI/ui-dashed"));

    // ---- helpers ----
    public static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString(hex, out Color c);
        return c;
    }

    public static Color Alpha(Color c, float a) { c.a = a; return c; }

    /// <summary>Mix an ink/accent colour over a solid ground at the given ratio,
    /// matching the CSS color-mix(... N%, transparent) used for hovers.</summary>
    public static Color Mix(Color over, Color ground, float ratio) => Color.Lerp(ground, over, ratio);
}
