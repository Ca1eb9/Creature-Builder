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
    public Transform categoryButtonContainer;
    public Transform partGridContainer;
    public GameObject categoryButtonPrefab;
    public GameObject partButtonPrefab;

    [Header("Action Buttons")]
    public Button randomizeButton;
    public Button clearButton;
    public Button screenshotButton;
    public Button autoRotateButton;
    public Button exitButton;

    [Header("Save / Load UI")]
    public TMP_InputField saveNameInput;
    public Button saveButton;
    public Transform loadListContainer;        // a ScrollView Content area
    public GameObject loadListEntryPrefab;     // button with label and delete icon

    [Header("Info Display")]
    public TextMeshProUGUI partNameLabel;
    public TextMeshProUGUI partDescLabel;

    private BodyPartCategory? currentCategory = null;
    private RotateCreature cachedRotator;

    void Start()
    {
        // Fail loudly and early if core references are missing — otherwise the
        // first NullReferenceException here would silently abort the rest of
        // Start and leave every button dead with no obvious cause.
        if (database == null)
        {
            Debug.LogError("UIManager: no BodyPartDatabase assigned — UI cannot be built.", this);
            return;
        }
        if (assembler == null)
        {
            Debug.LogError("UIManager: no CreatureAssembler assigned — UI cannot be built.", this);
            return;
        }

        cachedRotator = FindAnyObjectByType<RotateCreature>();

        // 24 chars at the input's point size fits fully within the save row
        if (saveNameInput != null)
            saveNameInput.characterLimit = 24;

        BuildCategoryTabs();
        WireActionButtons();
        WireSaveLoad();
        RefreshLoadList();

        // Greet the user with a creature instead of an empty void
        assembler.Randomize(database);

        // Start with the info chip hidden until a part is clicked
        UpdateInfoDisplay(null);

        var categories = database.GetAvailableCategories();
        if (categories.Count > 0) SelectCategory(categories[0]);
    }

    // -------- CATEGORY + PART GRID --------

    void BuildCategoryTabs()
    {
        foreach (Transform child in categoryButtonContainer) Destroy(child.gameObject);

        foreach (BodyPartCategory cat in database.GetAvailableCategories())
        {
            GameObject btnObj = Instantiate(categoryButtonPrefab, categoryButtonContainer);
            TextMeshProUGUI label = btnObj.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null) label.text = cat.ToString();

            BodyPartCategory capturedCat = cat;
            btnObj.GetComponent<Button>().onClick.AddListener(() => SelectCategory(capturedCat));
        }
    }

    void SelectCategory(BodyPartCategory category)
    {
        currentCategory = category;

        foreach (Transform child in partGridContainer) Destroy(child.gameObject);

        // "None" button
        GameObject noneBtn = Instantiate(partButtonPrefab, partGridContainer);
        var noneLabel = noneBtn.GetComponentInChildren<TextMeshProUGUI>();
        if (noneLabel != null) noneLabel.text = "None";
        noneBtn.GetComponent<Button>().onClick.AddListener(() =>
        {
            assembler.RemovePart(category);
            UpdateInfoDisplay(null);
            UpdateAdjustmentPanelTarget();
        });

        foreach (BodyPartData part in database.GetPartsInCategory(category))
        {
            GameObject btnObj = Instantiate(partButtonPrefab, partGridContainer);

            Image iconImage = btnObj.transform.Find("Icon")?.GetComponent<Image>();
            if (iconImage != null)
            {
                iconImage.sprite = part.icon;
                iconImage.preserveAspect = true;
                // No sprite = keep the Image hidden, or it renders as a white box
                iconImage.enabled = part.icon != null;
            }

            var label = btnObj.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null) label.text = part.partName;

            if (assembler.IsEquipped(part))
            {
                var colors = btnObj.GetComponent<Button>().colors;
                colors.normalColor = new Color(0.4f, 0.8f, 0.4f);
                btnObj.GetComponent<Button>().colors = colors;
            }

            BodyPartData capturedPart = part;
            btnObj.GetComponent<Button>().onClick.AddListener(() =>
            {
                assembler.EquipPart(capturedPart);
                UpdateInfoDisplay(capturedPart);
                UpdateAdjustmentPanelTarget();
                SelectCategory(category);
            });
        }

        UpdateAdjustmentPanelTarget();
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

        // Hide the whole info chip when there's nothing to show
        if (partNameLabel != null && partNameLabel.transform.parent != null)
        {
            bool hasContent = part != null && !string.IsNullOrEmpty(part.partName);
            partNameLabel.transform.parent.gameObject.SetActive(hasContent);
        }
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
        assembler.Randomize(database);
        if (currentCategory.HasValue) SelectCategory(currentCategory.Value);
    }

    void OnClear()
    {
        assembler.ClearAll();
        if (currentCategory.HasValue) SelectCategory(currentCategory.Value);
    }

    void OnScreenshot() => StartCoroutine(CaptureCleanScreenshot());

    private System.Collections.IEnumerator CaptureCleanScreenshot()
    {
        // Hide BOTH canvases: the main UI and the feedback overlay —
        // a toast still fading from an earlier action would otherwise
        // end up in the photo.
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

        // CaptureScreenshot captures at the END of a frame; called after
        // WaitForEndOfFrame it may not resolve until the following frame's
        // end. Stay hidden through that frame so the UI can't re-enter
        // the shot, then restore.
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
        if (label != null) label.text = cachedRotator.autoRotate ? "Stop Spin" : "Auto Spin";
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
        string creatureName = saveNameInput != null ? saveNameInput.text : "MyCreature";
        if (string.IsNullOrWhiteSpace(creatureName)) creatureName = "MyCreature";

        if (saveLoad.CreatureExists(creatureName))
        {
            UIFeedback.ShowConfirm(
                $"Overwrite \"{creatureName}\"?",
                "A creature with this name already exists. Saving will replace it.",
                "Overwrite", "Cancel",
                onConfirm: () => DoSave(creatureName));
        }
        else
        {
            DoSave(creatureName);
        }
    }

    void DoSave(string creatureName)
    {
        if (saveLoad.SaveCreature(creatureName))
        {
            RefreshLoadList();
            UIFeedback.ShowToast($"Saved \"{creatureName}\"!");
        }
        else
        {
            UIFeedback.ShowToast("Save failed — see log for details");
        }
    }

    void RefreshLoadList()
    {
        if (saveLoad == null || loadListContainer == null || loadListEntryPrefab == null) return;

        foreach (Transform child in loadListContainer) Destroy(child.gameObject);

        foreach (string entryName in saveLoad.ListSavedCreatures())
        {
            GameObject entry = Instantiate(loadListEntryPrefab, loadListContainer);
            var label = entry.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null) label.text = entryName;

            // Main button = load
            string capturedName = entryName;
            var loadBtn = entry.GetComponent<Button>();
            if (loadBtn != null) loadBtn.onClick.AddListener(() => OnLoadCreature(capturedName));

            // Optional delete button (expect child named "DeleteButton")
            Transform deleteChild = entry.transform.Find("DeleteButton");
            if (deleteChild != null)
            {
                Button deleteBtn = deleteChild.GetComponent<Button>();
                if (deleteBtn != null)
                    deleteBtn.onClick.AddListener(() => OnDeleteCreature(capturedName));
            }
        }
    }

    void OnLoadCreature(string creatureName)
    {
        if (saveLoad.LoadCreature(creatureName))
        {
            if (currentCategory.HasValue) SelectCategory(currentCategory.Value);
            UIFeedback.ShowToast($"Loaded \"{creatureName}\"");
        }
        else
        {
            UIFeedback.ShowToast("Couldn't load that creature — see log for details");
        }
    }

    void OnDeleteCreature(string creatureName)
    {
        UIFeedback.ShowConfirm(
            $"Delete \"{creatureName}\"?",
            "This creature will be gone forever. This cannot be undone.",
            "Delete", "Keep it",
            onConfirm: () =>
            {
                if (saveLoad.DeleteCreature(creatureName))
                {
                    RefreshLoadList();
                    UIFeedback.ShowToast($"Deleted \"{creatureName}\"");
                }
            },
            dangerousConfirm: true);
    }
}