using UnityEngine;
using UnityEditor;
using System.IO;
using TMPro;
using UnityEngine.TextCore.LowLevel;

/// <summary>
/// One-shot theme setup for the Classical UI revamp:
///   • generates dynamic TMP font assets from the imported TTFs (into a
///     Resources folder so runtime UI can load them by name),
///   • sets Lora as the TMP default so all text picks up the body face,
///   • bakes two reusable 9-sliced sprites — a 1px hairline outline and a
///     rounded fill — both white so they tint to any token colour.
///
///   Tools > Creature Builder > Build Theme Assets   (or BuildThemeBatch headless)
/// </summary>
public static class CreatureBuilderThemeTool
{
    private const string FontResourceDir = "Assets/Resources/Fonts";
    private const string SpriteDir = "Assets/Resources/UI";

    [MenuItem("Tools/Creature Builder/Build Theme Assets")]
    public static void BuildThemeMenu()
    {
        string msg = BuildTheme();
        EditorUtility.DisplayDialog("Build Theme Assets", msg, "OK");
    }

    public static void BuildThemeBatch()
    {
        try
        {
            string msg = BuildTheme();
            Debug.Log("THEME: " + msg);
            EditorApplication.Exit(0);
        }
        catch (System.Exception e)
        {
            Debug.LogError("BuildThemeBatch failed: " + e);
            EditorApplication.Exit(1);
        }
    }

    public static string BuildTheme()
    {
        EnsureFolder(FontResourceDir);
        EnsureFolder(SpriteDir);

        int fonts = 0;
        fonts += MakeFontAsset("Assets/Fonts/CormorantGaramond-SemiBold.ttf") ? 1 : 0;
        fonts += MakeFontAsset("Assets/Fonts/CormorantGaramond-Regular.ttf") ? 1 : 0;
        var lora = MakeFontAsset("Assets/Fonts/Lora-Regular.ttf");
        fonts += lora ? 1 : 0;
        fonts += MakeFontAsset("Assets/Fonts/Lora-Medium.ttf") ? 1 : 0;

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        bool defaultSet = SetDefaultFont($"{FontResourceDir}/Lora-Regular SDF.asset");

        MakeOutlineSprite($"{SpriteDir}/ui-outline.png");
        MakeRoundedSprite($"{SpriteDir}/ui-rounded.png", 8);
        MakeDashedSprite($"{SpriteDir}/ui-dashed.png");
        AssetDatabase.SaveAssets();

        return $"Generated {fonts} font asset(s); default font {(defaultSet ? "set to Lora" : "unchanged")}; baked outline + rounded sprites.";
    }

    // ------------------------------------------------------------------
    // FONTS
    // ------------------------------------------------------------------

    private static TMP_FontAsset MakeFontAsset(string ttfPath)
    {
        Font font = AssetDatabase.LoadAssetAtPath<Font>(ttfPath);
        if (font == null) { Debug.LogWarning($"TTF not found: {ttfPath}"); return null; }

        string niceName = Path.GetFileNameWithoutExtension(ttfPath) + " SDF";
        string outPath = $"{FontResourceDir}/{niceName}.asset";

        var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(outPath);
        if (existing != null) return existing; // idempotent

        // Dynamic SDF: atlas is empty on disk and fills from the TTF at runtime,
        // so we don't need to pre-bake every glyph.
        TMP_FontAsset fa = TMP_FontAsset.CreateFontAsset(
            font, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024,
            AtlasPopulationMode.Dynamic, enableMultiAtlasSupport: true);
        if (fa == null) { Debug.LogWarning($"CreateFontAsset failed for {ttfPath}"); return null; }

        fa.name = niceName;
        AssetDatabase.CreateAsset(fa, outPath);

        if (fa.atlasTextures != null && fa.atlasTextures.Length > 0)
        {
            fa.atlasTextures[0].name = niceName + " Atlas";
            AssetDatabase.AddObjectToAsset(fa.atlasTextures[0], fa);
        }
        if (fa.material != null)
        {
            fa.material.name = niceName + " Material";
            AssetDatabase.AddObjectToAsset(fa.material, fa);
        }
        EditorUtility.SetDirty(fa);
        return fa;
    }

    private static bool SetDefaultFont(string fontAssetPath)
    {
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fontAssetPath);
        if (font == null) return false;
        var settings = TMP_Settings.instance;
        if (settings == null) return false;

        var so = new SerializedObject(settings);
        var prop = so.FindProperty("m_defaultFontAsset");
        if (prop == null) return false;
        prop.objectReferenceValue = font;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(settings);
        return true;
    }

    // ------------------------------------------------------------------
    // SPRITES
    // ------------------------------------------------------------------

    /// <summary>1px white frame, transparent center, 9-sliced border (1,1,1,1).</summary>
    private static void MakeOutlineSprite(string path)
    {
        const int s = 8, b = 1;
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                bool edge = x < b || y < b || x >= s - b || y >= s - b;
                tex.SetPixel(x, y, edge ? Color.white : new Color(1, 1, 1, 0));
            }
        tex.Apply();
        WriteSprite(tex, path, new Vector4(1, 1, 1, 1));
    }

    /// <summary>
    /// 1px dashed white frame for the empty-slot card. Drawn on a 12px tile with
    /// a 6-on/6-off rhythm; used with Image.Type.Tiled so the dashes repeat
    /// along an edge instead of being stretched.
    /// </summary>
    private static void MakeDashedSprite(string path)
    {
        const int s = 16, dash = 8;
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                bool onEdge = x == 0 || y == 0 || x == s - 1 || y == s - 1;
                // Along the run of each edge, draw for the first half of the tile.
                bool inDash = (x == 0 || x == s - 1) ? (y % (dash * 2) < dash)
                                                     : (x % (dash * 2) < dash);
                tex.SetPixel(x, y, onEdge && inDash ? Color.white : new Color(1, 1, 1, 0));
            }
        tex.Apply();
        WriteSprite(tex, path, new Vector4(1, 1, 1, 1));
    }

    /// <summary>White rounded-rect fill with a 1px-AA corner, 9-sliced by the radius.</summary>
    private static void MakeRoundedSprite(string path, int radius)
    {
        int s = radius * 2 + 2;
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                // Distance into the nearest corner circle; interior is fully opaque.
                float cx = x < radius ? radius : (x >= s - radius ? s - radius - 1 : x);
                float cy = y < radius ? radius : (y >= s - radius ? s - radius - 1 : y);
                float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                float a = Mathf.Clamp01(radius - d + 0.5f);
                tex.SetPixel(x, y, new Color(1, 1, 1, a));
            }
        tex.Apply();
        WriteSprite(tex, path, new Vector4(radius, radius, radius, radius));
    }

    private static void WriteSprite(Texture2D tex, string path, Vector4 border)
    {
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);

        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Single;
        ti.spriteBorder = border;
        ti.filterMode = FilterMode.Bilinear;
        ti.mipmapEnabled = false;
        ti.alphaIsTransparency = true;
        ti.SaveAndReimport();
    }

    private static void EnsureFolder(string assetFolder)
    {
        if (AssetDatabase.IsValidFolder(assetFolder)) return;
        Directory.CreateDirectory(assetFolder);
        AssetDatabase.Refresh();
    }
}
