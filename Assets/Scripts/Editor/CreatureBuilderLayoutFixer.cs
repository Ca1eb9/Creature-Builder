using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// One-shot, idempotent scene/prefab repairs for the panel layouts
/// (PLAN.md tasks 1.1, 1.2, 1.6). Everything is re-runnable: fixing an
/// already-fixed scene is a no-op.
///
///   Tools > Creature Builder > Fix Panel Layouts
///   Tools > Creature Builder > Add Missing Attach Points
///   Tools > Creature Builder > Add PartButton Icon Slot
///
/// Design notes: every layout group gets Control Child Size ON — without
/// it, "Child Force Expand" does nothing and children keep whatever size
/// they were left at (the root cause of the broken slider sizing).
/// Children then request sizes via LayoutElement.
/// </summary>
public static class CreatureBuilderLayoutFixer
{
    private const string PartButtonPrefabPath = "Assets/Prefabs/UI/PartButton.prefab";
    private const string LoadListEntryPrefabPath = "Assets/Prefabs/UI/LoadListEntry.prefab";

    // ------------------------------------------------------------------
    // MENU ITEMS
    // ------------------------------------------------------------------

    [MenuItem("Tools/Creature Builder/Fix Panel Layouts")]
    public static void FixPanelLayoutsMenu()
    {
        FixAdjustmentPanel();
        FixSaveLoadPanel();
        FixLoadListEntryPrefab();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("Fix Panel Layouts: done. Save the scene to keep the changes.");
    }

    [MenuItem("Tools/Creature Builder/Add Missing Attach Points")]
    public static void AddAttachPointsMenu()
    {
        AddMissingAttachPoints();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
    }

    [MenuItem("Tools/Creature Builder/Add PartButton Icon Slot")]
    public static void AddIconSlotMenu()
    {
        AddPartButtonIconSlot();
    }

    /// <summary>
    /// Batch entry point: opens MainScene, applies every fix, saves, then
    /// validates. Exit 0 on success.
    ///   Unity.exe -batchmode -quit -projectPath ...
    ///     -executeMethod CreatureBuilderLayoutFixer.FixAllBatch
    /// </summary>
    public static void FixAllBatch()
    {
        try
        {
            EditorSceneManager.OpenScene("Assets/Scenes/MainScene.unity");
            FixAdjustmentPanel();
            FixSaveLoadPanel();
            FixLoadListEntryPrefab();
            AddMissingAttachPoints();
            AddPartButtonIconSlot();
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();

            var report = CreatureBuilderValidator.Validate();
            Debug.Log(report.summary);
            EditorApplication.Exit(report.errors.Count == 0 ? 0 : 1);
        }
        catch (System.Exception e)
        {
            Debug.LogError("FixAllBatch failed: " + e);
            EditorApplication.Exit(1);
        }
    }

    // ------------------------------------------------------------------
    // ADJUSTMENT PANEL (task 1.1a)
    // ------------------------------------------------------------------

    private static void FixAdjustmentPanel()
    {
        var panel = Object.FindAnyObjectByType<PartAdjustmentPanel>(FindObjectsInactive.Include);
        if (panel == null) { Debug.LogWarning("No PartAdjustmentPanel in scene — skipped."); return; }

        // Root: dock to the top-right corner, width fixed, height hugging content
        var rootRect = (RectTransform)panel.transform;
        rootRect.anchorMin = new Vector2(1f, 1f);
        rootRect.anchorMax = new Vector2(1f, 1f);
        rootRect.pivot = new Vector2(1f, 1f);
        rootRect.anchoredPosition = new Vector2(-10f, -10f);
        rootRect.sizeDelta = new Vector2(300f, 600f); // height overridden by fitter

        ConfigureVertical(panel.gameObject, padding: 10, spacing: 8);
        EnsureFitter(panel.gameObject);

        if (panel.titleLabel != null)
        {
            EnsureLayoutElement(panel.titleLabel.gameObject, preferredHeight: 32);
            // Long category names ("Adjusting: Accessories") must shrink to
            // fit on one line, never wrap onto a second
            panel.titleLabel.textWrappingMode = TextWrappingModes.NoWrap;
            panel.titleLabel.overflowMode = TextOverflowModes.Ellipsis;
            panel.titleLabel.enableAutoSizing = true;
            panel.titleLabel.fontSizeMin = 12f;
            panel.titleLabel.fontSizeMax = 26f;
        }
        if (panel.toggleButton != null) EnsureLayoutElement(panel.toggleButton.gameObject, preferredHeight: 36);
        if (panel.resetButton != null) EnsureLayoutElement(panel.resetButton.gameObject, preferredHeight: 34);

        if (panel.contentRoot != null)
            ConfigureVertical(panel.contentRoot, padding: 0, spacing: 6);

        // Sliders, their label+slider rows, and the sections above them
        var sliders = new[] { panel.posXSlider, panel.posYSlider, panel.posZSlider,
                              panel.rotXSlider, panel.rotYSlider, panel.rotZSlider,
                              panel.scaleSlider };
        var sections = new HashSet<Transform>();
        foreach (var slider in sliders)
        {
            if (slider == null) continue;
            EnsureLayoutElement(slider.gameObject, preferredHeight: 22, flexibleWidth: 1);

            Transform parent = slider.transform.parent;
            if (parent == null) continue;

            if (parent.GetComponent<HorizontalLayoutGroup>() != null)
            {
                // label + slider row
                ConfigureRow(parent.gameObject, spacing: 6);
                EnsureLayoutElement(parent.gameObject, preferredHeight: 26);
                foreach (Transform child in parent)
                {
                    if (child == slider.transform) continue;
                    if (child.GetComponent<TextMeshProUGUI>() != null)
                        EnsureLayoutElement(child.gameObject, preferredWidth: 24, flexibleWidth: 0, minWidth: 24);
                }
                if (parent.parent != null) sections.Add(parent.parent);
            }
            else
            {
                sections.Add(parent); // slider sits directly in its section
            }
        }

        foreach (var section in sections)
        {
            if (section == null || panel.contentRoot == null ||
                section == panel.contentRoot.transform) continue;
            if (section.GetComponent<VerticalLayoutGroup>() != null)
                ConfigureVertical(section.gameObject, padding: 0, spacing: 4);
        }

        Debug.Log("AdjustmentPanel: docked top-right, layout groups now control child sizes.");
    }

    // ------------------------------------------------------------------
    // SAVE / LOAD PANEL (task 1.1b)
    // ------------------------------------------------------------------

    private static void FixSaveLoadPanel()
    {
        var ui = Object.FindAnyObjectByType<UIManager>(FindObjectsInactive.Include);
        if (ui == null || ui.saveNameInput == null || ui.loadListContainer == null)
        {
            Debug.LogWarning("UIManager (or its save/load references) missing — SaveLoadPanel skipped.");
            return;
        }

        Transform saveSection = ui.saveNameInput.transform.parent;
        Transform panelRoot = saveSection != null ? saveSection.parent : null;
        if (panelRoot == null) { Debug.LogWarning("Couldn't derive SaveLoadPanel from scene wiring — skipped."); return; }

        // Root: dock to the bottom-right corner
        var rootRect = (RectTransform)panelRoot;
        rootRect.anchorMin = new Vector2(1f, 0f);
        rootRect.anchorMax = new Vector2(1f, 0f);
        rootRect.pivot = new Vector2(1f, 0f);
        rootRect.anchoredPosition = new Vector2(-10f, 10f);
        rootRect.sizeDelta = new Vector2(300f, 400f); // height overridden by fitter

        // Background so the panel reads as one unit (matches AdjustmentPanel's)
        var bg = panelRoot.GetComponent<Image>();
        if (bg == null)
        {
            bg = panelRoot.gameObject.AddComponent<Image>();
            bg.color = new Color(1f, 1f, 1f, 0.392f);
        }

        ConfigureVertical(panelRoot.gameObject, padding: 10, spacing: 8);
        EnsureFitter(panelRoot.gameObject);

        // --- Save row: [ name input, stretchy ][ Save button, fixed ] ---
        ConfigureRow(saveSection.gameObject, spacing: 6);
        EnsureLayoutElement(saveSection.gameObject, preferredHeight: 40);
        EnsureLayoutElement(ui.saveNameInput.gameObject, preferredHeight: 36, flexibleWidth: 1);
        if (ui.saveButton != null)
            EnsureLayoutElement(ui.saveButton.gameObject, preferredWidth: 90, flexibleWidth: 0);

        // Sized so a maximum-length creature name (24 chars, set in
        // UIManager) is fully visible in the field, not clipped
        ui.saveNameInput.pointSize = 14f;

        // --- Load section: label above a fixed-height scroll view ---
        Transform content = ui.loadListContainer;          // ScrollView Content
        Transform viewport = content.parent;
        Transform scrollView = viewport != null ? viewport.parent : null;
        Transform loadSection = scrollView != null ? scrollView.parent : null;

        if (loadSection != null && loadSection != panelRoot)
        {
            ConfigureVertical(loadSection.gameObject, padding: 0, spacing: 4);
            foreach (Transform child in loadSection)
            {
                if (child == scrollView) continue;
                if (child.GetComponent<TextMeshProUGUI>() != null)
                    EnsureLayoutElement(child.gameObject, preferredHeight: 24);
            }
        }

        if (scrollView != null)
        {
            EnsureLayoutElement(scrollView.gameObject, preferredHeight: 220, flexibleHeight: 0);
            var scrollRect = scrollView.GetComponent<ScrollRect>();
            if (scrollRect != null)
            {
                scrollRect.horizontal = false; // vertical list only
                // Fully retire the horizontal scrollbar — left merely
                // "disabled", its track still renders as a dead white bar
                if (scrollRect.horizontalScrollbar != null)
                {
                    scrollRect.horizontalScrollbar.gameObject.SetActive(false);
                    scrollRect.horizontalScrollbar = null;
                }
            }
        }

        if (viewport != null)
        {
            // Was a zero-size point anchor — the reason saved entries were
            // invisible. Stretch it to fill the scroll view, leaving room
            // for the vertical scrollbar.
            var vp = (RectTransform)viewport;
            vp.anchorMin = Vector2.zero;
            vp.anchorMax = Vector2.one;
            vp.pivot = new Vector2(0f, 1f);
            vp.offsetMin = Vector2.zero;
            vp.offsetMax = new Vector2(-18f, 0f);
        }

        // Content: top-anchored strip that grows downward with its entries
        var contentRect = (RectTransform)content;
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = new Vector2(0f, 0f);
        ConfigureVertical(content.gameObject, padding: 4, spacing: 4);
        EnsureFitter(content.gameObject);

        Debug.Log("SaveLoadPanel: docked bottom-right, viewport un-collapsed, list now lays out entries.");
    }

    private static void FixLoadListEntryPrefab()
    {
        var root = PrefabUtility.LoadPrefabContents(LoadListEntryPrefabPath);
        try
        {
            // Content's layout group controls entry heights; without a
            // LayoutElement an Image-only root reports preferred height 0
            // and every entry collapses flat.
            EnsureLayoutElement(root, preferredHeight: 32);

            // The guide's "Image (background)" child was never stretched — it
            // sat as a 100x100 white box floating over the entry. Remove it;
            // the root gets a proper background Image below instead.
            Transform strayImage = root.transform.Find("Image");
            if (strayImage != null) Object.DestroyImmediate(strayImage.gameObject);

            // The root was hand-built as a TMP text object, so it had a
            // TextMeshProUGUI on itself and NO Button/Image. Consequences:
            //  - UIManager's entry.GetComponent<Button>() found nothing, so
            //    clicking the row never loaded anything (silently skipped);
            //  - GetComponentInChildren<TMP>() (self-first) wrote the creature
            //    name into this root text instead of the Label child;
            //  - a Graphic on the root blocks adding the background Image.
            // Remove it, then build the root as the guide intended: a Button
            // with its own background covering the whole row.
            var rootText = root.GetComponent<TextMeshProUGUI>();
            if (rootText != null) Object.DestroyImmediate(rootText);

            var bg = root.GetComponent<Image>();
            if (bg == null) bg = root.AddComponent<Image>();
            // Light translucent chip to match the app's light theme
            bg.color = new Color(1f, 1f, 1f, 0.55f);
            bg.raycastTarget = true;

            var loadButton = root.GetComponent<Button>();
            if (loadButton == null) loadButton = root.AddComponent<Button>();
            loadButton.targetGraphic = bg;

            // Lay the entry out as [ label, stretchy ][ delete button, 24x24 ].
            // Before this, DeleteButton was 160x30 centered — covering nearly
            // the whole entry and eating the clicks meant for the load button.
            ConfigureRow(root, spacing: 4);
            var hlg = root.GetComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(8, 4, 4, 4);

            Transform label = root.transform.Find("Label");
            if (label != null)
            {
                EnsureLayoutElement(label.gameObject, flexibleWidth: 1);
                var tmp = label.GetComponent<TextMeshProUGUI>();
                if (tmp != null)
                {
                    tmp.alignment = TextAlignmentOptions.MidlineLeft;
                    tmp.textWrappingMode = TextWrappingModes.NoWrap;
                    tmp.overflowMode = TextOverflowModes.Ellipsis;
                    tmp.raycastTarget = false; // clicks fall through to the entry's load button
                    // Explicit dark text on the light chip — no theme guessing
                    tmp.color = new Color(0.13f, 0.13f, 0.15f, 1f);
                    tmp.fontSize = 18f;
                    tmp.enableAutoSizing = false;
                }
            }

            Transform delete = root.transform.Find("DeleteButton");
            if (delete != null)
            {
                EnsureLayoutElement(delete.gameObject, preferredHeight: 24, preferredWidth: 24, flexibleWidth: 0);
                var img = delete.GetComponent<Image>();
                if (img != null) img.color = new Color(0.87f, 0.42f, 0.42f, 0.95f); // soft red, fits light theme
                var text = delete.GetComponentInChildren<TextMeshProUGUI>();
                if (text != null)
                {
                    text.alignment = TextAlignmentOptions.Center;
                    text.enableAutoSizing = true;
                    text.fontSizeMin = 8f;
                    text.fontSizeMax = 18f;
                    text.raycastTarget = false;
                    text.color = Color.white;
                }
            }

            PrefabUtility.SaveAsPrefabAsset(root, LoadListEntryPrefabPath);
            Debug.Log("LoadListEntry prefab: row layout applied, stray white Image removed, DeleteButton sized 24x24.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // ------------------------------------------------------------------
    // ATTACH POINTS (task 1.2)
    // ------------------------------------------------------------------

    private static void AddMissingAttachPoints()
    {
        var assembler = Object.FindAnyObjectByType<CreatureAssembler>(FindObjectsInactive.Include);
        if (assembler == null) { Debug.LogWarning("No CreatureAssembler in scene — skipped."); return; }

        var so = new SerializedObject(assembler);
        int added = 0;
        added += EnsureAttachPoint(assembler.transform, so, "hornsAttach", "AttachPoint_Horns", new Vector3(0f, 1.7f, 0.3f));
        added += EnsureAttachPoint(assembler.transform, so, "accessoriesAttach", "AttachPoint_Accessories", new Vector3(0f, 1.1f, 0f));
        so.ApplyModifiedPropertiesWithoutUndo();

        Debug.Log(added > 0
            ? $"Attach points: created and wired {added} (positions are rough — tune with real models)."
            : "Attach points: all present, nothing to do.");
    }

    private static int EnsureAttachPoint(Transform root, SerializedObject so,
                                         string property, string name, Vector3 localPos)
    {
        var prop = so.FindProperty(property);
        if (prop.objectReferenceValue != null) return 0;

        Transform existing = root.Find(name);
        Transform point = existing != null ? existing : new GameObject(name).transform;
        if (existing == null)
        {
            point.SetParent(root, false);
            point.localPosition = localPos;
        }
        prop.objectReferenceValue = point;
        return 1;
    }

    // ------------------------------------------------------------------
    // PART BUTTON ICON SLOT (task 1.6)
    // ------------------------------------------------------------------

    private static void AddPartButtonIconSlot()
    {
        var root = PrefabUtility.LoadPrefabContents(PartButtonPrefabPath);
        try
        {
            if (root.transform.Find("Icon") != null)
            {
                Debug.Log("PartButton prefab: Icon slot already present.");
                return;
            }

            var icon = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            icon.transform.SetParent(root.transform, false);

            var rect = (RectTransform)icon.transform;
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(8f, 0f);
            rect.sizeDelta = new Vector2(44f, 44f);

            var image = icon.GetComponent<Image>();
            image.raycastTarget = false;
            image.preserveAspect = true;
            // Hidden until UIManager assigns a sprite — otherwise it would
            // render as a white square on icon-less parts
            image.enabled = false;

            PrefabUtility.SaveAsPrefabAsset(root, PartButtonPrefabPath);
            Debug.Log("PartButton prefab: added 'Icon' Image child (44x44, left side, hidden by default).");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // ------------------------------------------------------------------
    // SHARED HELPERS
    // ------------------------------------------------------------------

    private static void ConfigureVertical(GameObject go, int padding, float spacing)
    {
        var vlg = go.GetComponent<VerticalLayoutGroup>();
        if (vlg == null) vlg = go.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(padding, padding, padding, padding);
        vlg.spacing = spacing;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childScaleWidth = false;
        vlg.childScaleHeight = false;
    }

    private static void ConfigureRow(GameObject go, float spacing)
    {
        var hlg = go.GetComponent<HorizontalLayoutGroup>();
        if (hlg == null) hlg = go.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(0, 0, 0, 0);
        hlg.spacing = spacing;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = true;
        hlg.childScaleWidth = false;
        hlg.childScaleHeight = false;
    }

    private static void EnsureFitter(GameObject go)
    {
        var fitter = go.GetComponent<ContentSizeFitter>();
        if (fitter == null) fitter = go.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
    }

    private static void EnsureLayoutElement(GameObject go,
        float preferredHeight = -1, float preferredWidth = -1,
        float flexibleWidth = -1, float flexibleHeight = -1, float minWidth = -1)
    {
        var le = go.GetComponent<LayoutElement>();
        if (le == null) le = go.AddComponent<LayoutElement>();
        if (preferredHeight >= 0) le.preferredHeight = preferredHeight;
        if (preferredWidth >= 0) le.preferredWidth = preferredWidth;
        if (flexibleWidth >= 0) le.flexibleWidth = flexibleWidth;
        if (flexibleHeight >= 0) le.flexibleHeight = flexibleHeight;
        if (minWidth >= 0) le.minWidth = minWidth;
    }
}
