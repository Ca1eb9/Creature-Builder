using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;

public class UIManager : MonoBehaviour
{
    [Header("Core References")]
    public CreatureAssembler assembler;
    public BodyPartDatabase database;
    public PartAdjustmentPanel adjustmentPanel;
    public CreatureSaveLoad saveLoad;

    [Header("Part Selection UI")]
    public Transform categoryButtonContainer;  // 2-col CategoryCell grid
    public Transform partGridContainer;         // 2-col PartCard grid
    public GameObject categoryButtonPrefab;     // CategoryCell
    public GameObject partButtonPrefab;         // PartCard

    [Header("Action Buttons")]
    public Button randomizeButton;
    public Button clearButton;
    public Button screenshotButton;
    public Button autoRotateButton;
    public Button exitButton;

    [Header("Save / Load UI")]
    public TMP_InputField saveNameInput;
    public Button saveButton;
    public Transform loadListContainer;         // Library CreatureCard grid
    public GameObject loadListEntryPrefab;      // CreatureCard

    [Header("Info Display")]
    public TextMeshProUGUI partNameLabel;
    public TextMeshProUGUI partDescLabel;

    [Header("Sockets (inspector)")]
    public Transform socketsContainer;
    public GameObject socketRowPrefab;          // SocketRow

    [Header("Collapse — rail")]
    public GameObject railPanel;
    public GameObject railGutter;
    public Button railHideButton;
    public Button railShowButton;

    [Header("Collapse — inspector")]
    public GameObject inspectorPanel;
    public GameObject inspectorGutter;
    public Button inspectorHideButton;
    public Button inspectorShowButton;

    [Header("Library sheet + nav")]
    public GameObject libraryScreen;
    public Button openLibraryButton;
    public Button openBuildButton;
    public Button newCreatureButton;

    [Header("Status / footers")]
    public TextMeshProUGUI statusLabel;         // "N parts · M in library"
    public TextMeshProUGUI railFooterLabel;     // "Showing X of Y"

    private BodyPartCategory? currentCategory = null;
    private RotateCreature cachedRotator;
    private string currentLoadedName = null;

    // category -> its SocketRow value label, built once
    private readonly Dictionary<BodyPartCategory, TextMeshProUGUI> socketValues = new();

    void Start()
    {
        if (database == null) { Debug.LogError("UIManager: no BodyPartDatabase assigned.", this); return; }
        if (assembler == null) { Debug.LogError("UIManager: no CreatureAssembler assigned.", this); return; }

        cachedRotator = FindAnyObjectByType<RotateCreature>();
        if (saveNameInput != null) saveNameInput.characterLimit = 24;

        BuildCategoryTabs();
        BuildSocketRows();
        WireActionButtons();
        WireSaveLoad();
        WireCollapse();
        WireNav();
        RefreshLoadList();

        assembler.OnCreatureChanged += UpdateSockets;

        SetRailCollapsed(false);
        SetInspectorCollapsed(false);
        ShowLibrary(false);

        assembler.Randomize(database);
        UpdateInfoDisplay(null);
        UpdateSockets();

        var categories = database.GetAvailableCategories();
        if (categories.Count > 0) SelectCategory(categories[0]);
    }

    void OnDestroy()
    {
        if (assembler != null) assembler.OnCreatureChanged -= UpdateSockets;
    }

    // -------- CATEGORY + PART GRID --------

    void BuildCategoryTabs()
    {
        if (categoryButtonContainer == null || categoryButtonPrefab == null) return;
        foreach (Transform child in categoryButtonContainer) Destroy(child.gameObject);

        foreach (BodyPartCategory cat in database.GetAvailableCategories())
        {
            GameObject cell = Instantiate(categoryButtonPrefab, categoryButtonContainer);
            SetChildText(cell, "Name", cat.ToString());
            SetChildText(cell, "Count", database.GetPartsInCategory(cat).Count.ToString());

            BodyPartCategory capturedCat = cat;
            var btn = cell.GetComponent<Button>();
            if (btn != null) btn.onClick.AddListener(() => SelectCategory(capturedCat));
        }
    }

    void SelectCategory(BodyPartCategory category)
    {
        currentCategory = category;
        HighlightActiveCategory(category);

        if (partGridContainer == null || partButtonPrefab == null) return;
        foreach (Transform child in partGridContainer) Destroy(child.gameObject);

        // "None" card
        GameObject noneCard = Instantiate(partButtonPrefab, partGridContainer);
        SetChildText(noneCard, "Name", "None");
        SetActiveChild(noneCard, "Equipped", !assembler.IsCategoryEquipped(category));
        var noneBtn = noneCard.GetComponent<Button>();
        if (noneBtn != null) noneBtn.onClick.AddListener(() =>
        {
            assembler.RemovePart(category);
            currentLoadedName = null;
            UpdateInfoDisplay(null);
            UpdateAdjustmentPanelTarget();
            SelectCategory(category);
        });

        var parts = database.GetPartsInCategory(category);
        foreach (BodyPartData part in parts)
        {
            GameObject card = Instantiate(partButtonPrefab, partGridContainer);

            bool hasIcon = part.icon != null;
            Image iconImage = card.transform.Find("Icon")?.GetComponent<Image>();
            if (iconImage != null)
            {
                iconImage.gameObject.SetActive(hasIcon);
                if (hasIcon) { iconImage.sprite = part.icon; iconImage.preserveAspect = true; }
            }

            // Name is ALWAYS shown on a PartCard now.
            SetChildText(card, "Name", part.partName);

            bool equipped = assembler.IsEquipped(part);
            SetActiveChild(card, "Equipped", equipped);
            if (equipped) MarkCardEquipped(card);

            BodyPartData capturedPart = part;
            var btn = card.GetComponent<Button>();
            if (btn != null) btn.onClick.AddListener(() =>
            {
                assembler.EquipPart(capturedPart);
                UpdateInfoDisplay(capturedPart);
                UpdateAdjustmentPanelTarget();
                SelectCategory(category);
            });
        }

        if (railFooterLabel != null)
            railFooterLabel.text = $"Showing {parts.Count} {category.ToString().ToLower()}";

        UpdateAdjustmentPanelTarget();
    }

    void HighlightActiveCategory(BodyPartCategory category)
    {
        if (categoryButtonContainer == null) return;
        int i = 0;
        foreach (BodyPartCategory cat in database.GetAvailableCategories())
        {
            if (i >= categoryButtonContainer.childCount) break;
            Transform cell = categoryButtonContainer.GetChild(i);
            bool active = cat == category;
            var bg = cell.GetComponent<Image>();
            if (bg != null) bg.color = active ? DesignTokens.Accent100 : new Color(0, 0, 0, 0);
            var name = cell.Find("Name")?.GetComponent<TextMeshProUGUI>();
            if (name != null) name.color = active ? DesignTokens.Accent700 : DesignTokens.Text;
            i++;
        }
    }

    void MarkCardEquipped(GameObject card)
    {
        var bg = card.GetComponent<Image>();
        if (bg != null) bg.color = DesignTokens.Accent100;
        var border = card.transform.Find("Border")?.GetComponent<Image>();
        if (border != null) border.color = DesignTokens.Accent;
    }

    void UpdateAdjustmentPanelTarget()
    {
        if (adjustmentPanel != null && currentCategory.HasValue)
            adjustmentPanel.SetActiveCategory(currentCategory.Value);
    }

    void UpdateInfoDisplay(BodyPartData part)
    {
        if (partNameLabel != null) partNameLabel.text = part != null ? part.partName : "";
        if (partDescLabel != null) partDescLabel.text = part != null ? part.description : "";
    }

    // -------- SOCKETS --------

    void BuildSocketRows()
    {
        socketValues.Clear();
        if (socketsContainer == null || socketRowPrefab == null) return;
        foreach (Transform child in socketsContainer) Destroy(child.gameObject);

        foreach (BodyPartCategory cat in database.GetAvailableCategories())
        {
            GameObject row = Instantiate(socketRowPrefab, socketsContainer);
            SetChildText(row, "Name", Prettify(cat.ToString()));
            var val = row.transform.Find("Value")?.GetComponent<TextMeshProUGUI>();
            if (val != null) socketValues[cat] = val;
        }
    }

    void UpdateSockets()
    {
        foreach (var kv in socketValues)
        {
            var data = assembler.GetEquippedData(kv.Key);
            if (kv.Value != null) kv.Value.text = data != null ? data.partName : "—";
        }
    }

    // -------- COLLAPSE --------

    void WireCollapse()
    {
        if (railHideButton != null) railHideButton.onClick.AddListener(() => SetRailCollapsed(true));
        if (railShowButton != null) railShowButton.onClick.AddListener(() => SetRailCollapsed(false));
        if (inspectorHideButton != null) inspectorHideButton.onClick.AddListener(() => SetInspectorCollapsed(true));
        if (inspectorShowButton != null) inspectorShowButton.onClick.AddListener(() => SetInspectorCollapsed(false));
    }

    public void SetRailCollapsed(bool collapsed)
    {
        if (railPanel != null) railPanel.SetActive(!collapsed);
        if (railGutter != null) railGutter.SetActive(collapsed);
    }

    public void SetInspectorCollapsed(bool collapsed)
    {
        if (inspectorPanel != null) inspectorPanel.SetActive(!collapsed);
        if (inspectorGutter != null) inspectorGutter.SetActive(collapsed);
    }

    // -------- NAV / LIBRARY --------

    void WireNav()
    {
        if (openLibraryButton != null) openLibraryButton.onClick.AddListener(() => ShowLibrary(true));
        if (openBuildButton != null) openBuildButton.onClick.AddListener(() => ShowLibrary(false));
        if (newCreatureButton != null) newCreatureButton.onClick.AddListener(() =>
        {
            OnClear();
            ShowLibrary(false);
        });
    }

    void ShowLibrary(bool show)
    {
        if (libraryScreen != null) libraryScreen.SetActive(show);
        if (show) RefreshLoadList();
    }

    // -------- ACTION BUTTONS --------

    void WireActionButtons()
    {
        if (randomizeButton != null) randomizeButton.onClick.AddListener(OnRandomize);
        if (clearButton != null) clearButton.onClick.AddListener(OnClear);
        if (screenshotButton != null) screenshotButton.onClick.AddListener(OnScreenshot);
        if (autoRotateButton != null) autoRotateButton.onClick.AddListener(OnToggleAutoRotate);
        if (exitButton != null) exitButton.onClick.AddListener(OnExit);
    }

    void OnRandomize()
    {
        currentLoadedName = null;
        assembler.Randomize(database);
        if (currentCategory.HasValue) SelectCategory(currentCategory.Value);
    }

    void OnClear()
    {
        currentLoadedName = null;
        assembler.ClearAll();
        if (currentCategory.HasValue) SelectCategory(currentCategory.Value);
    }

    void OnScreenshot() => StartCoroutine(CaptureCleanScreenshot());

    private System.Collections.IEnumerator CaptureCleanScreenshot()
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas != null) canvas.enabled = false;
        UIFeedback.SetOverlayVisible(false);
        yield return new WaitForEndOfFrame();

        string downloadsPath = System.IO.Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), "Downloads");
        string filename = $"Creature_{System.DateTime.Now:yyyyMMdd_HHmmss}.png";
        string fullPath = System.IO.Path.Combine(downloadsPath, filename);
        ScreenCapture.CaptureScreenshot(fullPath);
        Debug.Log($"Screenshot saved: {fullPath}");

        yield return new WaitForEndOfFrame();
        if (canvas != null) canvas.enabled = true;
        UIFeedback.SetOverlayVisible(true);

        yield return null;
        UIFeedback.ShowToast("Screenshot saved to your Downloads folder");
    }

    void OnToggleAutoRotate()
    {
        if (cachedRotator == null) return;
        cachedRotator.autoRotate = !cachedRotator.autoRotate;
        var label = autoRotateButton.GetComponentInChildren<TextMeshProUGUI>();
        if (label != null) label.text = cachedRotator.autoRotate ? "Stop spin" : "Auto-spin";
    }

    void OnExit()
    {
        UIFeedback.ShowConfirm(
            "Exit Creature Builder?",
            "Anything you haven't saved will be lost.",
            "Exit", "Stay",
            onConfirm: () =>
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            });
    }

    // -------- SAVE / LOAD --------

    void WireSaveLoad()
    {
        if (saveButton != null) saveButton.onClick.AddListener(OnSave);
    }

    void OnSave()
    {
        if (saveLoad == null) return;

        string creatureName = saveNameInput != null ? saveNameInput.text : "";
        if (string.IsNullOrWhiteSpace(creatureName))
            creatureName = !string.IsNullOrWhiteSpace(currentLoadedName) ? currentLoadedName : "MyCreature";

        if (saveLoad.CreatureExists(creatureName))
        {
            UIFeedback.ShowConfirm(
                $"Overwrite \"{creatureName}\"?",
                "A creature with this name already exists. Saving will replace it.",
                "Overwrite", "Cancel",
                onConfirm: () => DoSave(creatureName));
        }
        else DoSave(creatureName);
    }

    void DoSave(string creatureName)
    {
        if (saveLoad.SaveCreature(creatureName))
        {
            currentLoadedName = creatureName;
            StartCoroutine(CaptureThumbnail(creatureName));
            RefreshLoadList();
            UIFeedback.ShowToast($"Saved \"{creatureName}\"!");
        }
        else UIFeedback.ShowToast("Save failed — see log for details");
    }

    private System.Collections.IEnumerator CaptureThumbnail(string creatureName)
    {
        // Grab a clean shot of just the creature (UI hidden), downscale, store.
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas != null) canvas.enabled = false;
        UIFeedback.SetOverlayVisible(false);
        yield return new WaitForEndOfFrame();

        Texture2D full = ScreenCapture.CaptureScreenshotAsTexture();

        if (canvas != null) canvas.enabled = true;
        UIFeedback.SetOverlayVisible(true);

        Texture2D small = Downscale(full, 480);
        saveLoad.SaveThumbnail(creatureName, small);
        Destroy(full);
        Destroy(small);

        RefreshLoadList(); // so the new thumbnail shows on its card
    }

    private static Texture2D Downscale(Texture2D src, int targetWidth)
    {
        if (src == null) return null;
        int w = targetWidth;
        int h = Mathf.Max(1, Mathf.RoundToInt(targetWidth * (float)src.height / src.width));
        var rt = RenderTexture.GetTemporary(w, h);
        Graphics.Blit(src, rt);
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var dst = new Texture2D(w, h, TextureFormat.RGB24, false);
        dst.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        dst.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        return dst;
    }

    void RefreshLoadList()
    {
        UpdateStatus();
        if (saveLoad == null || loadListContainer == null || loadListEntryPrefab == null) return;

        foreach (Transform child in loadListContainer) Destroy(child.gameObject);

        foreach (string entryName in saveLoad.ListSavedCreatures())
        {
            GameObject card = Instantiate(loadListEntryPrefab, loadListContainer);
            SetChildText(card, "Name", entryName);
            SetChildText(card, "Parts", saveLoad.GetPartsSummary(entryName));

            var thumb = card.transform.Find("Thumbnail")?.GetComponent<RawImage>();
            if (thumb != null)
            {
                var tex = saveLoad.LoadThumbnail(entryName);
                if (tex != null) thumb.texture = tex;
            }

            string capturedName = entryName;
            WireChildButton(card, "LoadButton", () => OnLoadCreature(capturedName));
            WireChildButton(card, "DeleteButton", () => OnDeleteCreature(capturedName));
        }
    }

    void OnLoadCreature(string creatureName)
    {
        if (saveLoad.LoadCreature(creatureName))
        {
            currentLoadedName = creatureName;
            ShowLibrary(false);
            if (currentCategory.HasValue) SelectCategory(currentCategory.Value);
            UpdateSockets();
            UIFeedback.ShowToast($"Loaded \"{creatureName}\"");
        }
        else UIFeedback.ShowToast("Couldn't load that creature — see log for details");
    }

    void OnDeleteCreature(string creatureName)
    {
        UIFeedback.ShowConfirm(
            $"Delete “{creatureName}”?",
            "This specimen will be gone for good — there's no undo once it's deleted.",
            "Delete", "Keep it",
            onConfirm: () =>
            {
                if (saveLoad.DeleteCreature(creatureName))
                {
                    if (creatureName == currentLoadedName) currentLoadedName = null;
                    RefreshLoadList();
                    UIFeedback.ShowToast($"Deleted \"{creatureName}\"");
                }
            },
            dangerousConfirm: true,
            kicker: "Library");
    }

    void UpdateStatus()
    {
        if (statusLabel == null || assembler == null) return;
        int equipped = 0;
        foreach (var _ in assembler.EquippedCategories) equipped++;
        int library = saveLoad != null ? saveLoad.ListSavedCreatures().Count : 0;
        statusLabel.text = $"{equipped} parts · {library} in library";
    }

    // -------- helpers --------

    static void SetChildText(GameObject go, string childName, string text)
    {
        var t = go.transform.Find(childName)?.GetComponent<TextMeshProUGUI>();
        if (t != null) t.text = text;
    }

    static void SetActiveChild(GameObject go, string childName, bool active)
    {
        var c = go.transform.Find(childName);
        if (c != null) c.gameObject.SetActive(active);
    }

    static void WireChildButton(GameObject go, string childName, UnityEngine.Events.UnityAction action)
    {
        var c = go.transform.Find(childName);
        var btn = c != null ? c.GetComponent<Button>() : null;
        if (btn != null) btn.onClick.AddListener(action);
    }

    static string Prettify(string enumName)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < enumName.Length; i++)
        {
            if (i > 0 && char.IsUpper(enumName[i])) sb.Append(' ');
            sb.Append(enumName[i]);
        }
        return sb.ToString();
    }
}
