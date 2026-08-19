using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

/// <summary>
/// Lightweight toasts and confirmation dialogs, built entirely from code at
/// runtime — no scene setup or prefabs required. Styled with the Classical
/// design system (see <see cref="DesignTokens"/>): a dark pill toast, and a
/// surface-coloured dialog with an accent kicker, Cormorant title and outlined
/// buttons.
///
///   UIFeedback.ShowToast("Saved!");
///   UIFeedback.ShowConfirm("Delete 'Fluffy'?", "This cannot be undone.",
///                          "Delete", "Cancel", onConfirm: () => { ... });
/// </summary>
public static class UIFeedback
{
    private static Canvas overlayCanvas;
    private static UIFeedbackHost host;
    private static GameObject activeDialog;
    private static GameObject activeToast;

    public static bool IsDialogOpen => activeDialog != null;

    public static void SetOverlayVisible(bool visible)
    {
        if (overlayCanvas != null) overlayCanvas.enabled = visible;
    }

    // ------------------------------------------------------------------
    // PUBLIC API
    // ------------------------------------------------------------------

    /// <summary>Non-blocking notification, bottom-center — a dark pill that fades out.</summary>
    public static void ShowToast(string message, float seconds = 2.5f)
    {
        EnsureCanvas();
        if (activeToast != null) Object.Destroy(activeToast);

        GameObject toast = CreatePanel("Toast", overlayCanvas.transform, DesignTokens.Neutral900,
            DesignTokens.RoundedSprite);
        activeToast = toast;
        AddShadow(toast, 0.35f);

        var rect = toast.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, 70f);

        var layout = toast.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(24, 24, 12, 12);
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;

        TextMeshProUGUI label = CreateLabel(toast.transform, message, 17f, DesignTokens.Neutral100);
        label.textWrappingMode = TextWrappingModes.NoWrap;

        var fitter = toast.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        toast.GetComponent<Image>().raycastTarget = false;
        label.raycastTarget = false;

        host.StartCoroutine(FadeOutAndDestroy(toast, seconds));
    }

    /// <summary>Modal yes/no dialog. Blocks clicks behind it until dismissed.</summary>
    public static void ShowConfirm(string title, string message,
                                   string confirmLabel, string cancelLabel,
                                   System.Action onConfirm,
                                   System.Action onCancel = null,
                                   bool dangerousConfirm = false,
                                   string kicker = "Creature Builder")
    {
        EnsureCanvas();
        if (activeDialog != null) return;

        // Dim backdrop that swallows clicks.
        GameObject blocker = CreatePanel("DialogBlocker", overlayCanvas.transform,
            DesignTokens.Alpha(DesignTokens.Neutral900, 0.5f));
        var blockerRect = blocker.GetComponent<RectTransform>();
        blockerRect.anchorMin = Vector2.zero;
        blockerRect.anchorMax = Vector2.one;
        blockerRect.offsetMin = blockerRect.offsetMax = Vector2.zero;
        activeDialog = blocker;

        // Centered surface panel.
        GameObject panel = CreatePanel("DialogPanel", blocker.transform, DesignTokens.Surface,
            DesignTokens.RoundedSprite);
        AddBorder(panel, DesignTokens.Divider);
        AddShadow(panel, 0.4f);
        var panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(460f, 230f);

        var layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(26, 26, 22, 22);
        layout.spacing = 12f;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        panel.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Accent kicker (uppercase), Cormorant title, muted body.
        var kick = CreateLabel(panel.transform, kicker.ToUpperInvariant(), 12f, DesignTokens.Accent);
        kick.characterSpacing = 8f;

        var titleText = CreateLabel(panel.transform, title, 24f, DesignTokens.Text);
        titleText.font = DesignTokens.HeadingFont;

        var msgText = CreateLabel(panel.transform, message, 15f, DesignTokens.Alpha(DesignTokens.Text, 0.85f));
        msgText.textWrappingMode = TextWrappingModes.Normal;

        // Right-aligned button row.
        GameObject row = new GameObject("Buttons", typeof(RectTransform));
        row.transform.SetParent(panel.transform, false);
        var rowLayout = row.AddComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 10f;
        rowLayout.padding = new RectOffset(0, 0, 8, 0);
        rowLayout.childAlignment = TextAnchor.MiddleRight;
        rowLayout.childControlWidth = rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = false;
        rowLayout.childForceExpandHeight = false;
        row.AddComponent<LayoutElement>().minHeight = 40f;

        CreateButton(row.transform, cancelLabel, DesignTokens.Divider, DesignTokens.Text, () =>
        {
            CloseDialog();
            onCancel?.Invoke();
        });
        Color confirmTint = dangerousConfirm ? DesignTokens.Danger : DesignTokens.Accent;
        CreateButton(row.transform, confirmLabel, confirmTint, confirmTint, () =>
        {
            CloseDialog();
            onConfirm?.Invoke();
        });
    }

    public static void CloseDialog()
    {
        if (activeDialog != null)
        {
            Object.Destroy(activeDialog);
            activeDialog = null;
        }
    }

    // ------------------------------------------------------------------
    // INTERNALS
    // ------------------------------------------------------------------

    private static void EnsureCanvas()
    {
        if (overlayCanvas != null) return;

        var go = new GameObject("UIFeedbackCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        overlayCanvas = go.GetComponent<Canvas>();
        overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        overlayCanvas.sortingOrder = 500;

        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        host = go.AddComponent<UIFeedbackHost>();
    }

    private static GameObject CreatePanel(string name, Transform parent, Color color, Sprite sprite = null)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = color;
        if (sprite != null) { img.sprite = sprite; img.type = Image.Type.Sliced; }
        return go;
    }

    /// <summary>Adds a 1px hairline border as a stretched child using the outline sprite.</summary>
    private static void AddBorder(GameObject go, Color color)
    {
        var b = new GameObject("Border", typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)b.transform;
        rt.SetParent(go.transform, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        var img = b.GetComponent<Image>();
        img.sprite = DesignTokens.OutlineSprite;
        img.type = Image.Type.Sliced;
        img.color = color;
        img.raycastTarget = false;
    }

    private static void AddShadow(GameObject go, float alpha)
    {
        var sh = go.AddComponent<Shadow>();
        sh.effectColor = DesignTokens.Alpha(DesignTokens.Neutral900, alpha);
        sh.effectDistance = new Vector2(0f, -3f);
    }

    private static TextMeshProUGUI CreateLabel(Transform parent, string text, float size, Color color)
    {
        var go = new GameObject("Label", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        if (DesignTokens.BodyFont != null) tmp.font = DesignTokens.BodyFont;
        tmp.text = text;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Left;
        return tmp;
    }

    /// <summary>Outlined button: transparent fill, 1px tinted border, heading-font label.</summary>
    private static Button CreateButton(Transform parent, string label, Color borderColor, Color textColor,
                                       System.Action onClick)
    {
        var go = new GameObject("Button_" + label, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);

        var image = go.GetComponent<Image>();
        image.sprite = DesignTokens.OutlineSprite;
        image.type = Image.Type.Sliced;
        image.color = borderColor;

        var le = go.AddComponent<LayoutElement>();
        le.minHeight = 40f; le.minWidth = 96f;

        var padding = go.AddComponent<HorizontalLayoutGroup>();
        padding.padding = new RectOffset(18, 18, 8, 8);
        padding.childAlignment = TextAnchor.MiddleCenter;
        padding.childControlWidth = padding.childControlHeight = true;
        padding.childForceExpandWidth = padding.childForceExpandHeight = false;

        var button = go.GetComponent<Button>();
        button.targetGraphic = image;
        var colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1f, 1f, 1f, 0.7f);
        colors.pressedColor = new Color(1f, 1f, 1f, 0.5f);
        button.colors = colors;
        button.onClick.AddListener(() => onClick());

        var text = CreateLabel(go.transform, label, 15f, textColor);
        text.font = DesignTokens.HeadingFont;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;

        return button;
    }

    private static IEnumerator FadeOutAndDestroy(GameObject toast, float visibleSeconds)
    {
        yield return new WaitForSeconds(visibleSeconds);
        if (toast == null) yield break;

        const float fadeDuration = 0.35f;
        var graphics = toast.GetComponentsInChildren<Graphic>();
        var baseAlpha = new float[graphics.Length];
        for (int i = 0; i < graphics.Length; i++)
            baseAlpha[i] = graphics[i] != null ? graphics[i].color.a : 0f;

        var shadows = toast.GetComponentsInChildren<Shadow>();
        var shadowBaseAlpha = new float[shadows.Length];
        for (int i = 0; i < shadows.Length; i++)
            shadowBaseAlpha[i] = shadows[i] != null ? shadows[i].effectColor.a : 0f;

        float t = 0f;
        while (t < fadeDuration)
        {
            if (toast == null) yield break;
            t += Time.deltaTime;
            float k = 1f - (t / fadeDuration);
            for (int i = 0; i < graphics.Length; i++)
            {
                if (graphics[i] == null) continue;
                Color c = graphics[i].color; c.a = baseAlpha[i] * k; graphics[i].color = c;
            }
            for (int i = 0; i < shadows.Length; i++)
            {
                if (shadows[i] == null) continue;
                Color c = shadows[i].effectColor; c.a = shadowBaseAlpha[i] * k; shadows[i].effectColor = c;
            }
            yield return null;
        }
        if (toast != null) Object.Destroy(toast);
    }

    private class UIFeedbackHost : MonoBehaviour { }
}
