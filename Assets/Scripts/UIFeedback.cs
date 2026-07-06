using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

/// <summary>
/// Lightweight toasts and confirmation dialogs, built entirely from code at
/// runtime — no scene setup or prefabs required. Creates its own overlay
/// canvas the first time it's used.
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

    // Palette
    private static readonly Color PanelColor = new Color(0.13f, 0.13f, 0.16f, 0.97f);
    private static readonly Color DimColor = new Color(0f, 0f, 0f, 0.55f);
    private static readonly Color ButtonColor = new Color(0.25f, 0.27f, 0.33f, 1f);
    private static readonly Color DangerColor = new Color(0.65f, 0.22f, 0.22f, 1f);
    private static readonly Color TextColor = new Color(0.94f, 0.94f, 0.94f, 1f);

    // ------------------------------------------------------------------
    // PUBLIC API
    // ------------------------------------------------------------------

    /// <summary>Non-blocking notification, bottom-center, fades out.</summary>
    public static void ShowToast(string message, float seconds = 2.5f)
    {
        EnsureCanvas();

        GameObject toast = CreatePanel("Toast", overlayCanvas.transform, PanelColor);
        var rect = toast.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, 60f);

        TextMeshProUGUI label = CreateLabel(toast.transform, message, 26f);
        label.margin = new Vector4(24f, 12f, 24f, 12f);

        // Size the pill to its text
        var fitter = toast.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Toasts must never eat clicks
        toast.GetComponent<Image>().raycastTarget = false;
        label.raycastTarget = false;

        host.StartCoroutine(FadeOutAndDestroy(toast, seconds));
    }

    /// <summary>
    /// Modal yes/no dialog. Blocks clicks behind it until dismissed.
    /// Only one dialog can be open at a time; a second request is ignored.
    /// </summary>
    public static void ShowConfirm(string title, string message,
                                   string confirmLabel, string cancelLabel,
                                   System.Action onConfirm,
                                   System.Action onCancel = null,
                                   bool dangerousConfirm = false)
    {
        EnsureCanvas();
        if (activeDialog != null) return;

        // Full-screen dim layer that swallows clicks behind the dialog
        GameObject blocker = CreatePanel("DialogBlocker", overlayCanvas.transform, DimColor);
        var blockerRect = blocker.GetComponent<RectTransform>();
        blockerRect.anchorMin = Vector2.zero;
        blockerRect.anchorMax = Vector2.one;
        blockerRect.offsetMin = Vector2.zero;
        blockerRect.offsetMax = Vector2.zero;
        activeDialog = blocker;

        // Centered panel
        GameObject panel = CreatePanel("DialogPanel", blocker.transform, PanelColor);
        var panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(520f, 240f);

        var layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(28, 28, 22, 22);
        layout.spacing = 14f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        TextMeshProUGUI titleText = CreateLabel(panel.transform, title, 32f);
        titleText.fontStyle = FontStyles.Bold;
        titleText.rectTransform.sizeDelta = new Vector2(0f, 44f);

        TextMeshProUGUI msgText = CreateLabel(panel.transform, message, 24f);
        msgText.rectTransform.sizeDelta = new Vector2(0f, 66f);

        // Button row
        GameObject row = new GameObject("Buttons", typeof(RectTransform));
        row.transform.SetParent(panel.transform, false);
        row.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 56f);
        var rowLayout = row.AddComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 16f;
        rowLayout.childAlignment = TextAnchor.MiddleCenter;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = true;
        rowLayout.childForceExpandHeight = true;

        CreateButton(row.transform, cancelLabel, ButtonColor, () =>
        {
            CloseDialog();
            onCancel?.Invoke();
        });
        CreateButton(row.transform, confirmLabel,
            dangerousConfirm ? DangerColor : ButtonColor, () =>
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
        overlayCanvas.sortingOrder = 500;   // above the main UI

        // Match the main canvas's scaling so sizes are consistent
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        host = go.AddComponent<UIFeedbackHost>();
    }

    private static GameObject CreatePanel(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = color;
        return go;
    }

    private static TextMeshProUGUI CreateLabel(Transform parent, string text, float size)
    {
        var go = new GameObject("Label", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.color = TextColor;
        tmp.alignment = TextAlignmentOptions.Center;
        return tmp;
    }

    private static Button CreateButton(Transform parent, string label, Color color, System.Action onClick)
    {
        var go = new GameObject("Button_" + label, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = color;

        var button = go.GetComponent<Button>();
        button.onClick.AddListener(() => onClick());

        TextMeshProUGUI text = CreateLabel(go.transform, label, 26f);
        var textRect = text.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        text.raycastTarget = false;

        return button;
    }

    private static IEnumerator FadeOutAndDestroy(GameObject toast, float visibleSeconds)
    {
        yield return new WaitForSeconds(visibleSeconds);

        const float fadeDuration = 0.35f;
        var graphics = toast.GetComponentsInChildren<Graphic>();
        float t = 0f;
        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            float alpha = 1f - (t / fadeDuration);
            foreach (var g in graphics)
            {
                Color c = g.color;
                c.a = alpha * (g is Image ? PanelColor.a : 1f);
                g.color = c;
            }
            yield return null;
        }
        Object.Destroy(toast);
    }

    /// <summary>Hidden coroutine host living on the overlay canvas.</summary>
    private class UIFeedbackHost : MonoBehaviour { }
}
