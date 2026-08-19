using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using TMPro;
using System.IO;

/// <summary>
/// Builds the five Classical-styled UI item prefabs used by the revamped
/// interface, entirely from code so they can be regenerated:
///   • PartCard    — 184px card: icon area + name + "Equipped" (child "Icon" kept
///                   so UIManager.SelectCategory works unchanged)
///   • CategoryCell — name + count, for the 2-column category picker
///   • SliderRow   — axis label + Slider + value box
///   • SocketRow   — category label + equipped-part value
///   • CreatureCard — library card: thumbnail + name + parts + Load/Delete
///
///   Tools > Creature Builder > Build UI Prefabs   (or BuildPrefabsBatch headless)
/// </summary>
public static class CreatureBuilderPrefabTool
{
    private const string Dir = "Assets/Prefabs/UI";

    [MenuItem("Tools/Creature Builder/Build UI Prefabs")]
    public static void BuildMenu()
    {
        string msg = BuildAll();
        EditorUtility.DisplayDialog("Build UI Prefabs", msg, "OK");
    }

    public static void BuildPrefabsBatch()
    {
        try { Debug.Log("PREFABS: " + BuildAll()); EditorApplication.Exit(0); }
        catch (System.Exception e) { Debug.LogError("BuildPrefabsBatch failed: " + e); EditorApplication.Exit(1); }
    }

    public static string BuildAll()
    {
        EnsureFolder(Dir);
        BuildPartCard();
        BuildCategoryCell();
        BuildSliderRow();
        BuildSocketRow();
        BuildCreatureCard();
        AssetDatabase.SaveAssets();
        return "Built PartCard, CategoryCell, SliderRow, SocketRow, CreatureCard.";
    }

    // ==================================================================
    //  PREFABS
    // ==================================================================

    private static void BuildPartCard()
    {
        var root = Root("PartCard", 160, 184);
        // Card fill + border; Button for click. Default (unequipped) look.
        var bg = root.AddComponent<Image>();
        bg.sprite = DesignTokens.RoundedSprite; bg.type = Image.Type.Sliced; bg.color = DesignTokens.Bg;
        var btn = root.AddComponent<Button>(); btn.targetGraphic = bg;
        Tint(btn);
        Border(root, DesignTokens.Divider);

        // Icon area (top 140px) with its own ground + bottom hairline.
        var iconArea = Child("IconArea", root.transform);
        TopBand(iconArea, 140);
        var iconBg = iconArea.AddComponent<Image>(); iconBg.color = DesignTokens.Neutral100;
        iconBg.sprite = DesignTokens.RoundedSprite; iconBg.type = Image.Type.Sliced; iconBg.raycastTarget = false;

        // The Icon Image — MUST be a direct child named "Icon" (UIManager finds it).
        var icon = Child("Icon", root.transform);
        var ir = (RectTransform)icon.transform;
        ir.anchorMin = new Vector2(0.5f, 1f); ir.anchorMax = new Vector2(0.5f, 1f); ir.pivot = new Vector2(0.5f, 1f);
        ir.sizeDelta = new Vector2(128, 128); ir.anchoredPosition = new Vector2(0, -6);
        var iconImg = icon.AddComponent<Image>(); iconImg.preserveAspect = true; iconImg.raycastTarget = false;
        icon.SetActive(false); // UIManager enables when a sprite is present

        // Name (first TMP → UIManager sets part.partName here).
        var name = Label("Name", root.transform, "Name", 15, DesignTokens.Text, DesignTokens.HeadingFont);
        var nr = (RectTransform)name.transform;
        nr.anchorMin = new Vector2(0, 0); nr.anchorMax = new Vector2(1, 1);
        nr.offsetMin = new Vector2(10, 6); nr.offsetMax = new Vector2(-10, -146);
        name.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.Left;

        // "Equipped" kicker (hidden by default; UIManager shows it on the equipped card).
        var eq = Label("Equipped", root.transform, "EQUIPPED", 9.5f, DesignTokens.Accent700, DesignTokens.BodyFont);
        var er = (RectTransform)eq.transform;
        er.anchorMin = new Vector2(0, 0); er.anchorMax = new Vector2(1, 0); er.pivot = new Vector2(0, 0);
        er.sizeDelta = new Vector2(-20, 14); er.anchoredPosition = new Vector2(10, 6);
        var eqt = eq.GetComponent<TextMeshProUGUI>(); eqt.characterSpacing = 8f; eqt.alignment = TextAlignmentOptions.Left;
        eq.SetActive(false);

        Save(root);
    }

    private static void BuildCategoryCell()
    {
        var root = Root("CategoryCell", 160, 40);
        var bg = root.AddComponent<Image>();
        bg.color = new Color(0, 0, 0, 0); bg.raycastTarget = true; // transparent hit area
        var btn = root.AddComponent<Button>(); btn.targetGraphic = bg; Tint(btn);
        Border(root, DesignTokens.Divider);

        var name = Label("Name", root.transform, "Category", 15.5f, DesignTokens.Text, DesignTokens.HeadingFont);
        var nr = (RectTransform)name.transform;
        nr.anchorMin = new Vector2(0, 0); nr.anchorMax = new Vector2(1, 1);
        nr.offsetMin = new Vector2(16, 0); nr.offsetMax = new Vector2(-34, 0);
        name.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.Left;

        var count = Label("Count", root.transform, "0", 11, DesignTokens.Neutral600, DesignTokens.BodyFont);
        var cr = (RectTransform)count.transform;
        cr.anchorMin = new Vector2(1, 0); cr.anchorMax = new Vector2(1, 1); cr.pivot = new Vector2(1, 0.5f);
        cr.sizeDelta = new Vector2(30, 0); cr.anchoredPosition = new Vector2(-12, 0);
        count.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.Right;

        Save(root);
    }

    private static void BuildSliderRow()
    {
        var root = Root("SliderRow", 300, 30);
        var h = root.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 10; h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = true; h.childControlHeight = true;
        h.childForceExpandWidth = false; h.childForceExpandHeight = false;

        var axis = Label("Axis", root.transform, "X", 13, DesignTokens.Neutral700, DesignTokens.BodyFont);
        LayoutSize(axis, 14, 24);
        axis.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.Left;

        var slider = BuildSlider(root.transform);
        var sle = slider.gameObject.AddComponent<LayoutElement>(); sle.flexibleWidth = 1; sle.minHeight = 20;

        var val = Label("Value", root.transform, "0.00", 12.5f, DesignTokens.Text, DesignTokens.BodyFont);
        var vt = val.GetComponent<TextMeshProUGUI>(); vt.alignment = TextAlignmentOptions.Right;
        Border(val, DesignTokens.Divider); // boxed value chip
        LayoutSize(val, 52, 24);
        vt.margin = new Vector4(6, 2, 6, 2);

        Save(root);
    }

    private static void BuildSocketRow()
    {
        var root = Root("SocketRow", 300, 24);
        var name = Label("Name", root.transform, "Head", 13.5f, DesignTokens.Neutral700, DesignTokens.BodyFont);
        var nr = (RectTransform)name.transform;
        nr.anchorMin = new Vector2(0, 0); nr.anchorMax = new Vector2(0.5f, 1); nr.offsetMin = Vector2.zero; nr.offsetMax = Vector2.zero;
        name.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.Left;

        var val = Label("Value", root.transform, "—", 15, DesignTokens.Text, DesignTokens.HeadingFont);
        var vr = (RectTransform)val.transform;
        vr.anchorMin = new Vector2(0.5f, 0); vr.anchorMax = new Vector2(1, 1); vr.offsetMin = Vector2.zero; vr.offsetMax = Vector2.zero;
        val.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.Right;

        Save(root);
    }

    private static void BuildCreatureCard()
    {
        var root = Root("CreatureCard", 300, 330);
        var bg = root.AddComponent<Image>();
        bg.sprite = DesignTokens.RoundedSprite; bg.type = Image.Type.Sliced; bg.color = DesignTokens.Bg;
        Border(root, DesignTokens.Divider);

        // Thumbnail (RawImage so a runtime screenshot texture can be assigned).
        var thumb = Child("Thumbnail", root.transform);
        var tr = (RectTransform)thumb.transform;
        tr.anchorMin = new Vector2(0, 1); tr.anchorMax = new Vector2(1, 1); tr.pivot = new Vector2(0.5f, 1);
        tr.sizeDelta = new Vector2(0, 230); tr.anchoredPosition = Vector2.zero;
        var raw = thumb.AddComponent<RawImage>(); raw.color = Color.white;
        var thumbBg = Child("ThumbBg", root.transform); // ground behind empty thumbnails
        var tbr = (RectTransform)thumbBg.transform;
        tbr.anchorMin = new Vector2(0, 1); tbr.anchorMax = new Vector2(1, 1); tbr.pivot = new Vector2(0.5f, 1);
        tbr.sizeDelta = new Vector2(0, 230);
        var tbg = thumbBg.AddComponent<Image>(); tbg.color = DesignTokens.Neutral200; tbg.raycastTarget = false;
        thumbBg.transform.SetSiblingIndex(0); // behind the RawImage

        // Name.
        var name = Label("Name", root.transform, "Creature", 20, DesignTokens.Text, DesignTokens.HeadingFont);
        var nr = (RectTransform)name.transform;
        nr.anchorMin = new Vector2(0, 1); nr.anchorMax = new Vector2(1, 1); nr.pivot = new Vector2(0, 1);
        nr.sizeDelta = new Vector2(-32, 28); nr.anchoredPosition = new Vector2(16, -244);
        name.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.Left;

        // Parts line.
        var parts = Label("Parts", root.transform, "—", 12.5f, DesignTokens.Neutral600, DesignTokens.BodyFont);
        var pr = (RectTransform)parts.transform;
        pr.anchorMin = new Vector2(0, 1); pr.anchorMax = new Vector2(1, 1); pr.pivot = new Vector2(0, 1);
        pr.sizeDelta = new Vector2(-32, 20); pr.anchoredPosition = new Vector2(16, -276);
        parts.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.Left;

        // Footer: Load (accent) + Delete (danger).
        var load = TextButton("LoadButton", root.transform, "Load", DesignTokens.Accent);
        var lr = (RectTransform)load.transform;
        lr.anchorMin = new Vector2(0, 0); lr.anchorMax = new Vector2(0, 0); lr.pivot = new Vector2(0, 0);
        lr.sizeDelta = new Vector2(70, 28); lr.anchoredPosition = new Vector2(16, 14);

        var del = TextButton("DeleteButton", root.transform, "Delete", DesignTokens.Danger);
        var dr = (RectTransform)del.transform;
        dr.anchorMin = new Vector2(1, 0); dr.anchorMax = new Vector2(1, 0); dr.pivot = new Vector2(1, 0);
        dr.sizeDelta = new Vector2(80, 28); dr.anchoredPosition = new Vector2(-16, 14);

        Save(root);
    }

    // ==================================================================
    //  BUILDING BLOCKS
    // ==================================================================

    private static GameObject Root(string name, float w, float h)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = new Vector2(w, h);
        return go;
    }

    private static GameObject Child(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    private static void TopBand(GameObject go, float height)
    {
        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1); rt.pivot = new Vector2(0.5f, 1);
        rt.sizeDelta = new Vector2(0, height); rt.anchoredPosition = Vector2.zero;
    }

    private static GameObject Label(string name, Transform parent, string text, float size, Color color, TMP_FontAsset font)
    {
        var go = Child(name, parent);
        var t = go.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.text = text; t.fontSize = size; t.color = color;
        t.alignment = TextAlignmentOptions.Left; t.textWrappingMode = TextWrappingModes.NoWrap;
        Stretch((RectTransform)go.transform);
        return go;
    }

    /// <summary>An outlined text-only button (transparent fill, accent/danger label).</summary>
    private static GameObject TextButton(string name, Transform parent, string text, Color color)
    {
        var go = Child(name, parent);
        var img = go.AddComponent<Image>(); img.color = new Color(0, 0, 0, 0); // invisible hit area
        var btn = go.AddComponent<Button>(); btn.targetGraphic = img; Tint(btn);
        var lbl = Label("Label", go.transform, text, 13, color, DesignTokens.BodyFont);
        Stretch((RectTransform)lbl.transform);
        lbl.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.Center;
        lbl.GetComponent<TextMeshProUGUI>().raycastTarget = false;
        return go;
    }

    private static Slider BuildSlider(Transform parent)
    {
        var go = Child("Slider", parent);
        var slider = go.AddComponent<Slider>();

        var bg = Child("Background", go.transform);
        var bgr = (RectTransform)bg.transform;
        bgr.anchorMin = new Vector2(0, 0.5f); bgr.anchorMax = new Vector2(1, 0.5f); bgr.pivot = new Vector2(0.5f, 0.5f);
        bgr.sizeDelta = new Vector2(0, 3);
        var bgi = bg.AddComponent<Image>(); bgi.sprite = DesignTokens.RoundedSprite; bgi.type = Image.Type.Sliced; bgi.color = DesignTokens.Neutral300;

        var fillArea = Child("Fill Area", go.transform);
        var far = (RectTransform)fillArea.transform;
        far.anchorMin = new Vector2(0, 0.5f); far.anchorMax = new Vector2(1, 0.5f); far.pivot = new Vector2(0.5f, 0.5f);
        far.offsetMin = new Vector2(0, -1.5f); far.offsetMax = new Vector2(-7, 1.5f); far.anchoredPosition = Vector2.zero;
        var fill = Child("Fill", fillArea.transform);
        var fr = (RectTransform)fill.transform; fr.sizeDelta = new Vector2(7, 0);
        var fi = fill.AddComponent<Image>(); fi.sprite = DesignTokens.RoundedSprite; fi.type = Image.Type.Sliced; fi.color = DesignTokens.Accent;

        var handleArea = Child("Handle Slide Area", go.transform);
        var har = (RectTransform)handleArea.transform;
        har.anchorMin = new Vector2(0, 0); har.anchorMax = new Vector2(1, 1);
        har.offsetMin = new Vector2(7, 0); har.offsetMax = new Vector2(-7, 0);
        var handle = Child("Handle", handleArea.transform);
        var hr = (RectTransform)handle.transform; hr.sizeDelta = new Vector2(14, 14);
        var hi = handle.AddComponent<Image>(); hi.sprite = DesignTokens.RoundedSprite; hi.type = Image.Type.Sliced; hi.color = DesignTokens.Bg;
        Border(handle, DesignTokens.Accent);

        slider.fillRect = (RectTransform)fill.transform;
        slider.handleRect = (RectTransform)handle.transform;
        slider.targetGraphic = hi;
        slider.direction = Slider.Direction.LeftToRight;
        return slider;
    }

    private static void Border(GameObject go, Color color)
    {
        var b = Child("Border", go.transform);
        Stretch((RectTransform)b.transform);
        var img = b.AddComponent<Image>();
        img.sprite = DesignTokens.OutlineSprite; img.type = Image.Type.Sliced; img.color = color; img.raycastTarget = false;
        b.transform.SetAsLastSibling();
    }

    private static void Tint(Button btn)
    {
        var c = btn.colors;
        c.normalColor = Color.white;
        c.highlightedColor = new Color(1, 1, 1, 0.92f);
        c.pressedColor = new Color(1, 1, 1, 0.8f);
        c.fadeDuration = 0.08f;
        btn.colors = c;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    private static void LayoutSize(GameObject go, float w, float h)
    {
        var le = go.AddComponent<LayoutElement>();
        le.minWidth = le.preferredWidth = w;
        le.minHeight = le.preferredHeight = h;
    }

    private static void Save(GameObject root)
    {
        string path = $"{Dir}/{root.name}.prefab";
        PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        Directory.CreateDirectory(folder);
        AssetDatabase.Refresh();
    }
}
