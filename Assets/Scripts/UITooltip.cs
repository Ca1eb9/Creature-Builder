using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// A single shared tooltip bubble, created on demand. Attach
/// <see cref="UITooltipTrigger"/> to anything that should show one.
/// Styled to match the Classical system: dark bubble, light text.
/// </summary>
public static class UITooltip
{
    private static GameObject bubble;
    private static TextMeshProUGUI label;
    private static RectTransform rect;
    private static Canvas canvas;

    private const float Delay = 0.35f;

    public static void Show(string text, Vector2 screenPos)
    {
        if (string.IsNullOrEmpty(text)) return;
        EnsureBubble();
        label.text = text;
        bubble.SetActive(true);

        // Place just below-right of the cursor, flipped near screen edges.
        LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
        Vector2 size = rect.sizeDelta;
        float scale = canvas != null ? canvas.scaleFactor : 1f;
        Vector2 pos = screenPos + new Vector2(14f, -14f);
        if (pos.x + size.x * scale > Screen.width) pos.x = screenPos.x - size.x * scale - 14f;
        if (pos.y - size.y * scale < 0f) pos.y = screenPos.y + size.y * scale + 14f;
        bubble.transform.position = pos;
    }

    public static void Hide()
    {
        if (bubble != null) bubble.SetActive(false);
    }

    public static float ShowDelay => Delay;

    private static void EnsureBubble()
    {
        if (bubble != null) return;

        var go = new GameObject("UITooltipCanvas", typeof(Canvas), typeof(CanvasScaler));
        canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 900; // above dialogs
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        Object.DontDestroyOnLoad(go);

        bubble = new GameObject("Tooltip", typeof(RectTransform), typeof(Image),
                                typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
        bubble.transform.SetParent(go.transform, false);
        rect = (RectTransform)bubble.transform;
        rect.pivot = new Vector2(0f, 1f);

        var img = bubble.GetComponent<Image>();
        img.color = DesignTokens.Neutral900;
        if (DesignTokens.RoundedSprite != null) { img.sprite = DesignTokens.RoundedSprite; img.type = Image.Type.Sliced; }
        img.raycastTarget = false;

        var h = bubble.GetComponent<HorizontalLayoutGroup>();
        h.padding = new RectOffset(12, 12, 8, 8);
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = h.childForceExpandHeight = false;

        var fit = bubble.GetComponent<ContentSizeFitter>();
        fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var lgo = new GameObject("Label", typeof(RectTransform));
        lgo.transform.SetParent(bubble.transform, false);
        label = lgo.AddComponent<TextMeshProUGUI>();
        if (DesignTokens.BodyFont != null) label.font = DesignTokens.BodyFont;
        label.fontSize = 13f;
        label.color = DesignTokens.Neutral100;
        label.raycastTarget = false;
        label.textWrappingMode = TextWrappingModes.NoWrap;

        bubble.SetActive(false);
    }
}

// NOTE: UITooltipTrigger lives in its own file (UITooltipTrigger.cs) — Unity only
// serializes a MonoBehaviour into a scene when its class name matches its file.
