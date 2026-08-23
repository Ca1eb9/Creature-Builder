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
    public Button closeLibraryButton;
    public TextMeshProUGUI librarySubtitle;
    public GameObject libraryEmptyState;

    [Header("Search")]
    public TMP_InputField searchInput;
    public TMP_InputField librarySearchInput;

    [Header("Gutter context labels")]
    public TextMeshProUGUI railGutterLabel;
    public TextMeshProUGUI inspectorGutterLabel;

    [Header("Status / footers")]
    public TextMeshProUGUI statusLabel;         // "N parts"
    public TextMeshProUGUI railFooterLabel;     // "Showing X of Y"

    [Header("Save state (inspector, under the name field)")]
    public TextMeshProUGUI savedStateLabel;     // "Saved as ..." / "Not saved yet"
    public TextMeshProUGUI unsavedLabel;        // "Unsaved changes" — shown only when dirty
    public TextMeshProUGUI spinStateLabel;      // status bar, right side

    private BodyPartCategory? currentCategory = null;
    private RotateCreature cachedRotator;
    private CameraFramer cachedFramer;
    private Canvas rootCanvas;
    private string currentLoadedName = null;

    // category -> its SocketRow labels, built once
    private readonly Dictionary<BodyPartCategory, TextMeshProUGUI> socketValues = new();
    private readonly Dictionary<BodyPartCategory, TextMeshProUGUI> socketNames = new();

    // True when the creature has changed since the last save/load.
    private bool hasUnsavedChanges;

    void Start()
    {
        if (database == null) { Debug.LogError("UIManager: no BodyPartDatabase assigned.", this); return; }
        if (assembler == null) { Debug.LogError("UIManager: no CreatureAssembler assigned.", this); return; }

        cachedRotator = FindAnyObjectByType<RotateCreature>();
        cachedFramer = FindAnyObjectByType<CameraFramer>();
        rootCanvas = GetComponentInParent<Canvas>();
        if (saveNameInput != null) saveNameInput.characterLimit = 24;

        BuildCategoryTabs();
        BuildSocketRows();
        WireActionButtons();
        WireSaveLoad();
        WireCollapse();
        WireNav();
        RefreshLoadList();

        assembler.OnCreatureChanged += OnCreatureChanged;

        if (searchInput != null)
        {
            searchInput.onValueChanged.AddListener(_ => RefreshPartGrid());
            UpdateSearchPlaceholder();
        }
        if (librarySearchInput != null)
            librarySearchInput.onValueChanged.AddListener(_ => RefreshLoadList());

        UpdateSpinState();

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
        if (assembler != null) assembler.OnCreatureChanged -= OnCreatureChanged;
    }

    /// <summary>The creature changed — keep the sockets list and the part count live.</summary>
    void OnCreatureChanged()
    {
        UpdateSockets();
        UpdateStatus();
        SetUnsaved(true);

        // Re-assert the current selection. Resetting a transform (or any other
        // creature change) must never look like it cleared what you had picked.
        if (currentCategory.HasValue)
        {
            HighlightActiveCategory(currentCategory.Value);
            HighlightActiveSocket(currentCategory.Value);
        }
    }

    // -------- CATEGORY + PART GRID --------

    void BuildCategoryTabs()
    {
        if (categoryButtonContainer == null || categoryButtonPrefab == null) return;
        foreach (Transform child in categoryButtonContainer) Destroy(child.gameObject);

        int index = 0;
        foreach (BodyPartCategory cat in database.GetAvailableCategories())
        {
            GameObject cell = Instantiate(categoryButtonPrefab, categoryButtonContainer);
            SetChildText(cell, "Name", Prettify(cat.ToString()));
            SetChildText(cell, "Count", database.GetPartsInCategory(cat).Count.ToString());

            // Ruled grid: only the right-hand column carries a left hairline.
            SetActiveChild(cell, "LeftLine", index % 2 == 1);
            index++;

            BodyPartCategory capturedCat = cat;
            var btn = cell.GetComponent<Button>();
            if (btn != null) btn.onClick.AddListener(() => SelectCategory(capturedCat));
        }
    }

    void SelectCategory(BodyPartCategory category)
    {
        currentCategory = category;
        // Picking a category is an explicit "browse this drawer" action, so it
        // clears any global search that was narrowing the grid.
        if (searchInput != null && !string.IsNullOrEmpty(searchInput.text))
        {
            searchInput.SetTextWithoutNotify("");
            UpdateSearchPlaceholder();
        }
        RefreshPartGrid();
    }

    /// <summary>True while the search box is narrowing the grid across every category.</summary>
    private bool IsSearching =>
        searchInput != null && !string.IsNullOrWhiteSpace(searchInput.text);

    /// <summary>
    /// Rebuilds the part grid. Normally it shows the selected category (with its
    /// empty "No head" slot first); while searching it ignores the category and
    /// lists every matching part in the whole database, so "badger" turns up the
    /// badger head AND the badger legs.
    /// </summary>
    void RefreshPartGrid()
    {
        bool searching = IsSearching;

        HighlightActiveCategory(searching ? (BodyPartCategory?)null : currentCategory);
        if (!searching && currentCategory.HasValue) HighlightActiveSocket(currentCategory.Value);

        if (partGridContainer == null || partButtonPrefab == null) return;
        foreach (Transform child in partGridContainer) Destroy(child.gameObject);

        List<BodyPartData> parts;
        int poolSize;

        if (searching)
        {
            string q = searchInput.text.Trim();
            parts = database.allParts.FindAll(bp =>
                bp != null && !string.IsNullOrEmpty(bp.partName) &&
                bp.partName.IndexOf(q, System.StringComparison.OrdinalIgnoreCase) >= 0);
            parts.Sort((a, b) => a.category != b.category
                ? a.category.CompareTo(b.category)
                : string.Compare(a.partName, b.partName, System.StringComparison.OrdinalIgnoreCase));
            poolSize = database.allParts.Count;
        }
        else
        {
            if (!currentCategory.HasValue) return;
            BuildEmptySlotCard(currentCategory.Value);
            parts = database.GetPartsInCategory(currentCategory.Value);
            poolSize = parts.Count;
        }

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
                // Equipping from a search result focuses that part's own category.
                currentCategory = capturedPart.category;
                UpdateAdjustmentPanelTarget();
                RefreshPartGrid();
                HighlightActiveSocket(capturedPart.category);
            });
        }

        if (railFooterLabel != null)
        {
            if (searching)
                railFooterLabel.text = $"Showing {parts.Count} of {poolSize} parts";
            else
                railFooterLabel.text = $"{parts.Count} {Prettify(currentCategory.Value.ToString()).ToLower()}";
        }
        UpdateAdjustmentPanelTarget();
    }

    /// <summary>
    /// The dashed "No head" slot. It carries the selected tint whenever that
    /// category is genuinely empty, so "nothing equipped" reads as a real choice.
    /// </summary>
    void BuildEmptySlotCard(BodyPartCategory category)
    {
        GameObject noneCard = Instantiate(partButtonPrefab, partGridContainer);
        SetChildText(noneCard, "Name", "No " + Singular(category).ToLower());
        SetActiveChild(noneCard, "IconArea", false);
        SetActiveChild(noneCard, "Icon", false);
        SetActiveChild(noneCard, "IconRule", false);
        SetActiveChild(noneCard, "DashedBorder", true);
        SetActiveChild(noneCard, "Dash", true);
        SetActiveChild(noneCard, "Border", false);

        // Selected = this category genuinely has nothing equipped. The accent
        // tint carries that; the "EQUIPPED" kicker would read wrong on an empty slot.
        bool isSelected = !assembler.IsCategoryEquipped(category);
        SetActiveChild(noneCard, "Equipped", false);

        // Centre the caption under the dash instead of the card's usual top-left slot.
        var noneName = noneCard.transform.Find("Name")?.GetComponent<TextMeshProUGUI>();
        if (noneName != null)
        {
            noneName.alignment = TextAlignmentOptions.Center;
            noneName.color = isSelected ? DesignTokens.Accent700 : DesignTokens.Neutral600;
            noneName.fontSize = 13;
            var nrt = (RectTransform)noneName.transform;
            nrt.offsetMin = new Vector2(10, 18); nrt.offsetMax = new Vector2(-10, -110);
        }
        var dashLbl = noneCard.transform.Find("Dash")?.GetComponent<TextMeshProUGUI>();
        if (dashLbl != null) dashLbl.color = isSelected ? DesignTokens.Accent : DesignTokens.Neutral600;
        var dashed = noneCard.transform.Find("DashedBorder")?.GetComponent<Image>();
        if (dashed != null) dashed.color = isSelected ? DesignTokens.Accent : DesignTokens.Neutral400;

        SetButtonBase(noneCard.GetComponent<Button>(),
                      isSelected ? DesignTokens.Accent100 : new Color(1, 1, 1, 0));

        var noneBtn = noneCard.GetComponent<Button>();
        if (noneBtn != null) noneBtn.onClick.AddListener(() =>
        {
            assembler.RemovePart(category);
            UpdateInfoDisplay(null);
            UpdateAdjustmentPanelTarget();
            RefreshPartGrid();
        });
    }

    void UpdateSearchPlaceholder()
    {
        if (searchInput == null || database == null) return;
        int total = database.allParts != null ? database.allParts.Count : 0;
        if (searchInput.placeholder is TextMeshProUGUI ph) ph.text = $"Search all {total} parts...";
    }

    void HighlightActiveCategory(BodyPartCategory? category)
    {
        if (categoryButtonContainer == null) return;
        int i = 0;
        foreach (BodyPartCategory cat in database.GetAvailableCategories())
        {
            if (i >= categoryButtonContainer.childCount) break;
            Transform cell = categoryButtonContainer.GetChild(i);
            bool active = cat == category;

            // Set the ColorBlock, not Image.color — a Button's ColorTint
            // transition rewrites targetGraphic.color from colors.normalColor,
            // so painting the Image directly gets wiped immediately.
            SetButtonBase(cell.GetComponent<Button>(),
                          active ? DesignTokens.Accent100 : new Color(1, 1, 1, 0));

            var name = cell.Find("Name")?.GetComponent<TextMeshProUGUI>();
            if (name != null) name.color = active ? DesignTokens.Accent700 : DesignTokens.Text;
            var count = cell.Find("Count")?.GetComponent<TextMeshProUGUI>();
            if (count != null) count.color = active ? DesignTokens.Accent700 : DesignTokens.Neutral600;
            i++;
        }
    }

    void MarkCardEquipped(GameObject card)
    {
        SetButtonBase(card.GetComponent<Button>(), DesignTokens.Accent100);
        var border = card.transform.Find("Border")?.GetComponent<Image>();
        if (border != null) border.color = DesignTokens.Accent;
        // the rule under the icon well goes accent too, as in the mockup
        var iconRule = card.transform.Find("IconRule")?.GetComponent<Image>();
        if (iconRule != null) iconRule.color = DesignTokens.Accent;
    }

    /// <summary>Repaint a button's resting colour without fighting its ColorTint transition.</summary>
    static void SetButtonBase(Button btn, Color baseColor)
    {
        if (btn == null) return;
        var c = btn.colors;
        c.normalColor = baseColor;
        c.selectedColor = baseColor;
        btn.colors = c; // assigning re-applies the tint immediately
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
        socketNames.Clear();
        if (socketsContainer == null || socketRowPrefab == null) return;
        foreach (Transform child in socketsContainer) Destroy(child.gameObject);

        foreach (BodyPartCategory cat in database.GetAvailableCategories())
        {
            GameObject row = Instantiate(socketRowPrefab, socketsContainer);
            SetChildText(row, "Name", Prettify(cat.ToString()));
            var val = row.transform.Find("Value")?.GetComponent<TextMeshProUGUI>();
            if (val != null) socketValues[cat] = val;
            var nm = row.transform.Find("Name")?.GetComponent<TextMeshProUGUI>();
            if (nm != null) socketNames[cat] = nm;
        }
    }

    void UpdateSockets()
    {
        foreach (var kv in socketValues)
        {
            var data = assembler.GetEquippedData(kv.Key);
            if (kv.Value != null)
                kv.Value.text = data != null ? ShortPartName(data.partName, kv.Key) : "—";
        }
    }

    /// <summary>
    /// "Badger Head" in the Head row is redundant — the row is already labelled
    /// Head. Drop the trailing category words so it reads just "Badger".
    /// </summary>
    static string ShortPartName(string partName, BodyPartCategory cat)
    {
        if (string.IsNullOrWhiteSpace(partName)) return "—";
        string suffix = Prettify(cat.ToString());               // "Front Legs"
        string trimmed = partName.Trim();

        if (trimmed.Length > suffix.Length &&
            trimmed.EndsWith(suffix, System.StringComparison.OrdinalIgnoreCase))
        {
            string shortened = trimmed.Substring(0, trimmed.Length - suffix.Length).Trim();
            if (shortened.Length > 0) return shortened;
        }
        return trimmed;
    }

    /// <summary>Tint the socket row matching the category being edited.</summary>
    void HighlightActiveSocket(BodyPartCategory category)
    {
        foreach (var kv in socketNames)
        {
            bool active = kv.Key == category;
            if (kv.Value != null) kv.Value.color = active ? DesignTokens.Accent700 : DesignTokens.Neutral700;
            if (socketValues.TryGetValue(kv.Key, out var val) && val != null)
                val.color = active ? DesignTokens.Accent700 : DesignTokens.Text;
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
        railCollapsed = collapsed;
        if (railPanel != null) railPanel.SetActive(!collapsed);
        if (railGutter != null) railGutter.SetActive(collapsed);
        UpdateStageViewport();
    }

    public void SetInspectorCollapsed(bool collapsed)
    {
        inspectorCollapsed = collapsed;
        if (inspectorPanel != null) inspectorPanel.SetActive(!collapsed);
        if (inspectorGutter != null) inspectorGutter.SetActive(collapsed);
        UpdateStageViewport();
    }

    private bool railCollapsed, inspectorCollapsed;

    // Chrome sizes in canvas reference units, mirroring CreatureBuilderSceneTool.
    private const float TopBarH = 56f, StatusBarH = 40f, PanelW = 340f, GutterW = 40f;

    /// <summary>
    /// Insets the 3D stage to the free area between the chrome, so the creature
    /// is framed in what you can actually see instead of hiding behind the rail
    /// and inspector (the mockup insets the stage to left:340 / top:56 / …).
    /// </summary>
    private void UpdateStageViewport()
    {
        if (cachedFramer == null) return;
        float s = rootCanvas != null ? rootCanvas.scaleFactor : 1f;
        float left  = (railCollapsed ? GutterW : PanelW) * s;
        float right = (inspectorCollapsed ? GutterW : PanelW) * s;
        cachedFramer.SetViewportInsets(left, right, TopBarH * s, StatusBarH * s);
    }

    // -------- NAV / LIBRARY --------

    void WireNav()
    {
        if (openLibraryButton != null) openLibraryButton.onClick.AddListener(() => ShowLibrary(true));
        if (openBuildButton != null) openBuildButton.onClick.AddListener(() => ShowLibrary(false));
        if (closeLibraryButton != null) closeLibraryButton.onClick.AddListener(() => ShowLibrary(false));
        if (newCreatureButton != null) newCreatureButton.onClick.AddListener(() =>
        {
            OnClear();
            ShowLibrary(false);
        });
    }

    void ShowLibrary(bool show)
    {
        if (libraryScreen != null) libraryScreen.SetActive(show);
        SetNavActive(show);
        if (show) RefreshLoadList();
    }

    /// <summary>Underline/tint whichever nav link matches the current screen.</summary>
    void SetNavActive(bool libraryActive)
    {
        TintNav(openBuildButton, !libraryActive);
        TintNav(openLibraryButton, libraryActive);
    }

    static void TintNav(Button b, bool active)
    {
        if (b == null) return;
        var t = b.GetComponentInChildren<TextMeshProUGUI>(true);
        if (t != null) t.color = active ? DesignTokens.Accent : DesignTokens.Neutral700;
        var underline = b.transform.Find("Underline");
        if (underline != null) underline.gameObject.SetActive(active);
    }

    void Update()
    {
        // Esc closes the library sheet. (New Input System — the project is set to
        // "Input System Package (New)", so the legacy Input class would throw.)
        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame &&
            libraryScreen != null && libraryScreen.activeSelf && !UIFeedback.IsDialogOpen)
            ShowLibrary(false);
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
        RefreshPartGrid();
    }

    void OnClear()
    {
        currentLoadedName = null;
        assembler.ClearAll();
        RefreshPartGrid();
    }

    void OnScreenshot() => StartCoroutine(CaptureCleanScreenshot());

    private System.Collections.IEnumerator CaptureCleanScreenshot()
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas != null) canvas.enabled = false;
        UIFeedback.SetOverlayVisible(false);
        UITooltip.Hide();
        UITooltip.SetLayerVisible(false);
        yield return new WaitForEndOfFrame();

        Texture2D grabbed = ScreenCapture.CaptureScreenshotAsTexture();

        if (canvas != null) canvas.enabled = true;
        UIFeedback.SetOverlayVisible(true);
        UITooltip.SetLayerVisible(true);

        // Crop to the stage so the picture is just the creature, with none of
        // the empty margin the inset viewport leaves where the panels sit.
        Texture2D stage = CropToStage(grabbed);

        string downloadsPath = System.IO.Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), "Downloads");
        string filename = $"Creature_{System.DateTime.Now:yyyyMMdd_HHmmss}.png";
        string fullPath = System.IO.Path.Combine(downloadsPath, filename);
        try
        {
            System.IO.Directory.CreateDirectory(downloadsPath);
            System.IO.File.WriteAllBytes(fullPath, stage.EncodeToPNG());
            Debug.Log($"Screenshot saved: {fullPath}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Screenshot failed: {e}");
        }
        if (stage != grabbed) Destroy(stage);
        Destroy(grabbed);

        yield return null;
        UIFeedback.ShowToast("Screenshot saved to your Downloads folder");
    }

    void OnToggleAutoRotate()
    {
        if (cachedRotator == null) return;
        cachedRotator.autoRotate = !cachedRotator.autoRotate;
        var label = autoRotateButton.GetComponentInChildren<TextMeshProUGUI>();
        if (label != null) label.text = cachedRotator.autoRotate ? "Stop spin" : "Auto-spin";
        UpdateSpinState();
    }

    /// <summary>Right-hand status-bar indicator: "Auto-spin on" / "Auto-spin off".</summary>
    void UpdateSpinState()
    {
        if (spinStateLabel == null) return;
        bool on = cachedRotator != null && cachedRotator.autoRotate;
        spinStateLabel.text = on ? "Auto-spin on" : "Auto-spin off";
        spinStateLabel.color = on ? DesignTokens.Accent700 : DesignTokens.Neutral600;
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
            SetUnsaved(false);
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
        UITooltip.Hide();
        UITooltip.SetLayerVisible(false);
        yield return new WaitForEndOfFrame();

        Texture2D full = ScreenCapture.CaptureScreenshotAsTexture();

        if (canvas != null) canvas.enabled = true;
        UIFeedback.SetOverlayVisible(true);
        UITooltip.SetLayerVisible(true);

        Texture2D stage = CropToStage(full);
        Texture2D small = Downscale(stage, 480);
        saveLoad.SaveThumbnail(creatureName, small);
        if (stage != full) Destroy(stage);
        Destroy(full);
        Destroy(small);

        RefreshLoadList(); // so the new thumbnail shows on its card
    }

    /// <summary>
    /// Trims a full-window grab down to the stage camera's viewport. Without
    /// this every saved picture carries pale bars where the rail, inspector and
    /// bars sit, because the camera no longer renders the whole window.
    /// </summary>
    private Texture2D CropToStage(Texture2D full)
    {
        if (full == null) return null;
        Camera cam = cachedFramer != null ? cachedFramer.Cam : Camera.main;
        if (cam == null) return full;

        Rect r = cam.pixelRect;
        int x = Mathf.Clamp(Mathf.RoundToInt(r.x), 0, full.width - 1);
        int y = Mathf.Clamp(Mathf.RoundToInt(r.y), 0, full.height - 1);
        int w = Mathf.Clamp(Mathf.RoundToInt(r.width), 1, full.width - x);
        int h = Mathf.Clamp(Mathf.RoundToInt(r.height), 1, full.height - y);
        if (w == full.width && h == full.height) return full;

        var cropped = new Texture2D(w, h, TextureFormat.RGB24, false);
        cropped.SetPixels(full.GetPixels(x, y, w, h));
        cropped.Apply();
        return cropped;
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

        var saved = saveLoad.ListSavedCreatures();
        string libQuery = librarySearchInput != null ? librarySearchInput.text : "";
        if (!string.IsNullOrWhiteSpace(libQuery))
            saved = saved.FindAll(n => n.IndexOf(libQuery.Trim(), System.StringComparison.OrdinalIgnoreCase) >= 0);

        if (librarySubtitle != null)
            librarySubtitle.text = saved.Count == 1
                ? "1 creature · stored in your user folder"
                : $"{saved.Count} creatures · stored in your user folder";
        if (libraryEmptyState != null) libraryEmptyState.SetActive(saved.Count == 0);

        foreach (string entryName in saved)
        {
            GameObject card = Instantiate(loadListEntryPrefab, loadListContainer);
            SetChildText(card, "Name", entryName);
            SetChildText(card, "Parts", saveLoad.GetPartsSummary(entryName));
            SetChildText(card, "Date", saveLoad.GetSavedDate(entryName));
            // The creature currently on the stage is tagged "Open".
            SetActiveChild(card, "OpenTag", entryName == currentLoadedName);

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
            RefreshPartGrid();
            UpdateSockets();
            // Loading brings in an already-saved creature, so it starts clean.
            SetUnsaved(false);
            if (saveNameInput != null) saveNameInput.text = creatureName;
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
        statusLabel.text = equipped == 1 ? "1 part" : $"{equipped} parts";
    }

    /// <summary>Reflects the dirty flag in the inspector, next to the creature name.</summary>
    void SetUnsaved(bool unsaved)
    {
        hasUnsavedChanges = unsaved;
        if (unsavedLabel != null) unsavedLabel.gameObject.SetActive(unsaved);
        if (savedStateLabel != null)
            savedStateLabel.text = string.IsNullOrWhiteSpace(currentLoadedName)
                ? "Not saved yet"
                : $"Saved as “{currentLoadedName}”";

        // Collapsed inspector keeps its context, like "Inspector · Doomcow".
        if (inspectorGutterLabel != null)
            inspectorGutterLabel.text = string.IsNullOrWhiteSpace(currentLoadedName)
                ? "INSPECTOR"
                : $"INSPECTOR · {currentLoadedName.ToUpper()}";
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

    /// <summary>"BackLegs" → "Back legs" → caption "No back legs"; "Head" → "head".</summary>
    static string Singular(string categoryName) => Prettify(categoryName);
    static string Singular(BodyPartCategory cat) => Prettify(cat.ToString());

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
