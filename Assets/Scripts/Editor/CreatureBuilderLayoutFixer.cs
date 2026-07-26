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

    // Note: the one-time panel-layout repair and attach-point setup are no
    // longer exposed as menu items (their job is done). The underlying
    // methods are retained below because FixAllBatch still applies them when
    // rebuilding a scene from scratch.

    [MenuItem("Tools/Creature Builder/Fix PartButton Layout")]
    public static void FixPartButtonLayoutMenu()
    {
        FixPartButtonLayout();
    }

    [MenuItem("Tools/Creature Builder/View Creature From Front")]
    public static void ViewFromFrontMenu()
    {
        ViewCreatureFromFront();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
    }

    [MenuItem("Tools/Creature Builder/Fix Part Grid Scrolling")]
    public static void FixPartGridScrollingMenu()
    {
        FixPartGridScrolling();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
    }

    /// <summary>
    /// Park the main camera at a 3/4 FRONT view of the creature — on the +Z side
    /// it faces, off to one side and slightly above — keeping its current
    /// distance. CameraFramer captures this direction in Awake and preserves it,
    /// so the app opens at this angle. Save the scene afterwards to keep it.
    /// </summary>
    private static void ViewCreatureFromFront()
    {
        var cam = Camera.main;
        if (cam == null) cam = Object.FindAnyObjectByType<Camera>();
        if (cam == null) { Debug.LogWarning("View From Front: no camera found."); return; }

        var asm = Object.FindAnyObjectByType<CreatureAssembler>();
        Vector3 target = asm != null ? asm.transform.position : Vector3.zero;

        float dist = (cam.transform.position - target).magnitude;
        if (dist < 0.01f) dist = 3f;

        // 3/4 front angle — must match the icon camera in
        // CreatureBuilderImporter.GenerateIcon so tiles and the live view agree.
        Vector3 dir = new Vector3(0.6f, 0.3f, 1f).normalized;
        cam.transform.position = target + dir * dist;
        cam.transform.LookAt(target);

        // Re-aim the key light to hit the same front the camera now sees,
        // otherwise the creature's front sits in shadow.
        AimKeyLightForFront();

        Debug.Log("View From Front: camera set to a 3/4 front angle and key light re-aimed. Save the scene to keep it.");
    }

    /// <summary>
    /// Point the scene's directional light at the creature's front from the
    /// upper-front (same +Z hemisphere as the camera) so the visible side is lit
    /// with a bit of top-down modeling.
    /// </summary>
    private static void AimKeyLightForFront()
    {
        Light sun = null;
        foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (l.type == LightType.Directional) { sun = l; break; }
        }
        if (sun == null) { Debug.LogWarning("View From Front: no directional light to re-aim."); return; }

        // Light travels toward the creature from up-front-right: rays go down (-Y),
        // toward the back (-Z, so they land on the +Z front faces), and across
        // from +X. Slightly off the camera axis for form rather than flat fill.
        Vector3 rayDirection = new Vector3(-0.5f, -0.7f, -0.5f).normalized;
        sun.transform.rotation = Quaternion.LookRotation(rayDirection);
    }

    /// <summary>
    /// Make the part picker scroll. The PartGrid object carries a ScrollRect
    /// with nothing wired and the GridLayoutGroup mashed onto it. This splits
    /// the roles properly: PartGrid becomes the clipping viewport (RectMask2D),
    /// a new "PartGridContent" child holds the grid + a ContentSizeFitter so it
    /// grows with the parts, and the ScrollRect is wired to both. UIManager's
    /// partGridContainer is repointed at Content so buttons spawn inside it.
    /// Idempotent.
    /// </summary>
    private static void FixPartGridScrolling()
    {
        var ui = Object.FindAnyObjectByType<UIManager>();
        if (ui == null) { Debug.LogWarning("Part grid scroll: no UIManager in the scene."); return; }

        var gridRect = ui.partGridContainer as RectTransform;
        if (gridRect == null) { Debug.LogWarning("Part grid scroll: UIManager.partGridContainer isn't set."); return; }

        // Already restructured — container points at the Content under a ScrollRect.
        if (gridRect.name == "PartGridContent" && gridRect.GetComponentInParent<ScrollRect>() != null)
        {
            Debug.Log("Part grid scroll: already set up.");
            return;
        }

        GameObject viewport = gridRect.gameObject;

        var scroll = viewport.GetComponent<ScrollRect>();
        if (scroll == null) scroll = viewport.AddComponent<ScrollRect>();
        if (viewport.GetComponent<RectMask2D>() == null) viewport.AddComponent<RectMask2D>();

        // New Content child, top-anchored and width-stretched so it grows downward.
        var content = new GameObject("PartGridContent", typeof(RectTransform));
        var contentRect = (RectTransform)content.transform;
        contentRect.SetParent(gridRect, false);
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = Vector2.zero;

        // Move the grid settings off the viewport onto Content.
        var grid = content.AddComponent<GridLayoutGroup>();
        var oldGrid = viewport.GetComponent<GridLayoutGroup>();
        if (oldGrid != null)
        {
            grid.cellSize = oldGrid.cellSize;
            grid.spacing = oldGrid.spacing;
            grid.startCorner = oldGrid.startCorner;
            grid.startAxis = oldGrid.startAxis;
            grid.childAlignment = oldGrid.childAlignment;
            grid.constraint = oldGrid.constraint;
            grid.constraintCount = oldGrid.constraintCount;
            grid.padding = oldGrid.padding;
            Object.DestroyImmediate(oldGrid);
        }
        else
        {
            grid.cellSize = new Vector2(100f, 100f);
            grid.childAlignment = TextAnchor.UpperCenter;
        }

        EnsureFitter(content); // vertical PreferredSize so Content height tracks the rows

        scroll.content = contentRect;
        scroll.viewport = gridRect;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 20f;

        ui.partGridContainer = contentRect; // buttons now spawn into Content
        EditorUtility.SetDirty(ui);

        Debug.Log("Part grid scroll: wrapped the grid in a scrollable Content. Save the scene to keep it.");
    }

    /// <summary>
    /// Batch entry: applies only the PartButton layout fix, verifies the
    /// resulting prefab structure, exits 0 on success / 1 on failure.
    ///   Unity.exe -batchmode -quit -projectPath ...
    ///     -executeMethod CreatureBuilderLayoutFixer.FixPartButtonLayoutBatch
    /// </summary>
    public static void FixPartButtonLayoutBatch()
    {
        var failures = new List<string>();
        try
        {
            FixPartButtonLayout();
            AssetDatabase.SaveAssets();

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PartButtonPrefabPath);
            if (prefab == null) failures.Add("PartButton prefab not found after fix.");
            else
            {
                if (prefab.GetComponent<HorizontalLayoutGroup>() != null)
                    failures.Add("Root still has a HorizontalLayoutGroup (overlay design expects none — the grid sizes the tile).");

                var iconTf = prefab.transform.Find("Icon") as RectTransform;
                if (iconTf == null) failures.Add("Icon child missing.");
                else
                {
                    if (iconTf.gameObject.activeSelf)
                        failures.Add("Icon should default to inactive so the label fallback shows when there's no sprite.");
                    if (iconTf.anchorMin != Vector2.zero || iconTf.anchorMax != Vector2.one)
                        failures.Add("Icon does not fill the tile (expected stretch anchors 0,0 → 1,1).");
                    if (!iconTf.GetComponent<Image>().preserveAspect)
                        failures.Add("Icon Image should preserve aspect.");
                }

                var label = prefab.GetComponentInChildren<TextMeshProUGUI>(true);
                if (label == null) failures.Add("Label (TextMeshProUGUI) missing.");
                else
                {
                    if (!label.gameObject.activeSelf)
                        failures.Add("Label should default to active (it's the fallback).");
                    if (!label.enableAutoSizing) failures.Add("Label auto-sizing not enabled.");
                    var lrt = (RectTransform)label.transform;
                    if (lrt.anchorMin != Vector2.zero || lrt.anchorMax != Vector2.one)
                        failures.Add("Label does not fill the tile (expected stretch anchors).");
                }
            }
        }
        catch (System.Exception e)
        {
            failures.Add("Unhandled exception: " + e);
        }

        if (failures.Count == 0)
        {
            Debug.Log("PARTBUTTON LAYOUT FIX PASSED — icon-fills-tile / label-fallback verified.");
            EditorApplication.Exit(0);
        }
        else
        {
            Debug.LogError("PARTBUTTON LAYOUT FIX FAILED:\n  • " + string.Join("\n  • ", failures));
            EditorApplication.Exit(1);
        }
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
            EnsureInfoPanel();
            AddMissingAttachPoints();
            FixPartButtonLayout();
            ViewCreatureFromFront();
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
    // DELETE BUTTON — replace the tiny word label with a trashcan icon
    // ------------------------------------------------------------------

    [MenuItem("Tools/Creature Builder/Fix Delete Button Icon")]
    public static void FixDeleteButtonIconMenu()
    {
        int n = FixDeleteButtonIcon();
        EditorUtility.DisplayDialog("Delete Button Icon",
            n > 0 ? "Delete button now shows a trashcan icon." : "DeleteButton not found.", "OK");
    }

    /// <summary>
    /// Swaps the DeleteButton's text label for a white trashcan icon built from
    /// plain UI shapes (lid + handle + body) — no sprite asset needed. Keeps the
    /// button's red background, size and position. Idempotent.
    /// </summary>
    public static int FixDeleteButtonIcon(string prefabPath = LoadListEntryPrefabPath)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            Transform delete = FindDescendant(root.transform, "DeleteButton");
            if (delete == null) return 0;

            // Remove the word label(s) and any previous icon so this re-runs cleanly.
            foreach (var tmp in delete.GetComponentsInChildren<TMPro.TMP_Text>(true))
                Object.DestroyImmediate(tmp.gameObject);
            var oldIcon = delete.Find("TrashIcon");
            if (oldIcon != null) Object.DestroyImmediate(oldIcon.gameObject);

            BuildTrashIcon(delete);

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            return 1;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void BuildTrashIcon(Transform button)
    {
        var icon = new GameObject("TrashIcon", typeof(RectTransform));
        var irt = (RectTransform)icon.transform;
        irt.SetParent(button, false);
        irt.anchorMin = Vector2.zero;
        irt.anchorMax = Vector2.one;
        irt.offsetMin = Vector2.zero;
        irt.offsetMax = Vector2.zero;

        // White bars, positioned about the button centre (button is ~24px).
        void Bar(string name, float w, float h, float x, float y)
        {
            var g = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)g.transform;
            rt.SetParent(icon.transform, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x, y);
            var img = g.GetComponent<Image>();
            img.color = Color.white;
            img.raycastTarget = false; // must not steal the button's clicks
        }

        Bar("Handle", 7f, 2f, 0f, 6f);   // little grip on the lid
        Bar("Lid", 15f, 2.5f, 0f, 4f);   // lid, wider than the body
        Bar("Body", 11f, 11f, 0f, -2.5f); // the can
    }

    // ------------------------------------------------------------------
    // SLIDER REPAIR — relink Fill/Handle refs that got cleared
    // ------------------------------------------------------------------

    [MenuItem("Tools/Creature Builder/Repair Adjustment Sliders")]
    public static void RepairSlidersMenu()
    {
        int fixedCount = RepairSliders();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorUtility.DisplayDialog("Repair Sliders",
            fixedCount > 0 ? $"Relinked {fixedCount} slider(s)." : "All sliders already OK.", "OK");
    }

    /// <summary>
    /// A Slider with no Handle Rect has no draggable handle — it sits pinned at
    /// the far left and cannot move (the RotX slider had all three internal refs
    /// cleared to fileID 0). Relink Fill / Handle / TargetGraphic from the child
    /// objects Unity's slider template creates ("Fill", "Handle"). Only touches
    /// sliders that are actually missing a ref, so it is safe and idempotent.
    /// </summary>
    public static int RepairSliders()
    {
        int fixedCount = 0;
        foreach (Slider s in Object.FindObjectsByType<Slider>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var so = new SerializedObject(s);
            var fillProp = so.FindProperty("m_FillRect");
            var handleProp = so.FindProperty("m_HandleRect");
            var targetProp = so.FindProperty("m_TargetGraphic");
            bool changed = false;

            if (handleProp.objectReferenceValue == null)
            {
                Transform handle = FindDescendant(s.transform, "Handle");
                if (handle != null) { handleProp.objectReferenceValue = handle.GetComponent<RectTransform>(); changed = true; }
            }
            if (fillProp.objectReferenceValue == null)
            {
                Transform fill = FindDescendant(s.transform, "Fill");
                if (fill != null) { fillProp.objectReferenceValue = fill.GetComponent<RectTransform>(); changed = true; }
            }
            if (targetProp.objectReferenceValue == null)
            {
                // The handle graphic is what highlights on hover/press.
                Transform handle = FindDescendant(s.transform, "Handle");
                Graphic g = handle != null ? handle.GetComponent<Graphic>() : null;
                if (g != null) { targetProp.objectReferenceValue = g; changed = true; }
            }

            if (changed)
            {
                so.ApplyModifiedPropertiesWithoutUndo();
                Debug.Log($"Repaired slider '{GetPath(s.transform)}'.");
                fixedCount++;
            }
        }
        return fixedCount;
    }

    /// <summary>Depth-first search for a descendant whose name matches (case-insensitive, exact).</summary>
    private static Transform FindDescendant(Transform root, string name)
    {
        foreach (Transform child in root)
        {
            if (string.Equals(child.name, name, System.StringComparison.OrdinalIgnoreCase)) return child;
            Transform found = FindDescendant(child, name);
            if (found != null) return found;
        }
        return null;
    }

    private static string GetPath(Transform t)
    {
        string path = t.name;
        while (t.parent != null) { t = t.parent; path = t.name + "/" + path; }
        return path;
    }

    /// <summary>
    /// Batch entry: opens MainScene, relinks any broken sliders, saves. Exit 0.
    ///   Unity.exe -batchmode -quit -projectPath ...
    ///     -executeMethod CreatureBuilderLayoutFixer.RepairSlidersBatch
    /// </summary>
    public static void RepairSlidersBatch()
    {
        try
        {
            EditorSceneManager.OpenScene("Assets/Scenes/MainScene.unity");
            int fixedCount = RepairSliders();
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            Debug.Log($"REPAIR SLIDERS: relinked {fixedCount} slider(s).");
            EditorApplication.Exit(0);
        }
        catch (System.Exception e)
        {
            Debug.LogError("RepairSlidersBatch failed: " + e);
            EditorApplication.Exit(1);
        }
    }

    /// <summary>
    /// Batch entry: swaps the delete button's word label for a trashcan icon.
    ///   Unity.exe -batchmode -quit -projectPath ...
    ///     -executeMethod CreatureBuilderLayoutFixer.FixDeleteButtonIconBatch
    /// </summary>
    public static void FixDeleteButtonIconBatch()
    {
        try
        {
            int n = FixDeleteButtonIcon();
            AssetDatabase.SaveAssets();
            Debug.Log($"DELETE BUTTON ICON: updated {n} prefab(s).");
            EditorApplication.Exit(0);
        }
        catch (System.Exception e)
        {
            Debug.LogError("FixDeleteButtonIconBatch failed: " + e);
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
    // INFO PANEL (task 1.3)
    // ------------------------------------------------------------------

    /// <summary>
    /// Creates the part name/description display and wires it into
    /// UIManager. Sits bottom-left, right of the part grid strip, clear of
    /// the bottom-center toasts and the bottom-right SaveLoadPanel.
    /// UIManager hides it whenever nothing is selected.
    /// </summary>
    private static void EnsureInfoPanel()
    {
        var ui = Object.FindAnyObjectByType<UIManager>(FindObjectsInactive.Include);
        if (ui == null) { Debug.LogWarning("No UIManager in scene — info panel skipped."); return; }
        if (ui.partNameLabel != null)
        {
            Debug.Log("Info panel: already wired, nothing to do.");
            return;
        }

        var panel = new GameObject("InfoPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        panel.transform.SetParent(ui.transform, false);

        var rect = (RectTransform)panel.transform;
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(0f, 0f);
        rect.pivot = new Vector2(0f, 0f);
        rect.anchoredPosition = new Vector2(210f, 10f);
        rect.sizeDelta = new Vector2(320f, 80f); // height overridden by fitter

        panel.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.392f); // matches the other panels
        ConfigureVertical(panel, padding: 10, spacing: 4);
        EnsureFitter(panel);

        TextMeshProUGUI nameLabel = CreateInfoLabel(panel.transform, "PartNameLabel", 22f, FontStyles.Bold);
        EnsureLayoutElement(nameLabel.gameObject, preferredHeight: 26);
        TextMeshProUGUI descLabel = CreateInfoLabel(panel.transform, "PartDescLabel", 16f, FontStyles.Normal);
        // desc height not fixed — wraps and grows, the fitter follows

        var so = new SerializedObject(ui);
        so.FindProperty("partNameLabel").objectReferenceValue = nameLabel;
        so.FindProperty("partDescLabel").objectReferenceValue = descLabel;
        so.ApplyModifiedPropertiesWithoutUndo();

        Debug.Log("Info panel: created bottom-left and wired into UIManager.");
    }

    private static TextMeshProUGUI CreateInfoLabel(Transform parent, string name, float size, FontStyles style)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = "";
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.color = new Color(0.13f, 0.13f, 0.15f, 1f);
        tmp.alignment = TextAlignmentOptions.TopLeft;
        tmp.raycastTarget = false;
        return tmp;
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

    /// <summary>
    /// Rework PartButton for the tile grid (PartGrid is a GridLayoutGroup whose
    /// 100x100 cells fix the button size). Icon and label OVERLAY the whole tile:
    ///   • the icon fills the tile and is shown only when the part has one
    ///     (GameObject inactive by default; UIManager activates it per part)
    ///   • the label fills the tile, centered, and is the FALLBACK shown when
    ///     there's no icon (so the tile is never blank)
    /// Any leftover row-layout components from a previous approach are removed.
    /// Idempotent: re-running just re-applies the same values.
    /// </summary>
    private static void FixPartButtonLayout()
    {
        var root = PrefabUtility.LoadPrefabContents(PartButtonPrefabPath);
        try
        {
            // The grid controls the button size, so strip any row layout / height
            // element left over from an earlier design.
            var hlg = root.GetComponent<HorizontalLayoutGroup>();
            if (hlg != null) Object.DestroyImmediate(hlg, true);
            var rootLE = root.GetComponent<LayoutElement>();
            if (rootLE != null) Object.DestroyImmediate(rootLE, true);

            // --- Icon: fills the tile, hidden until UIManager assigns a sprite ---
            var iconTf = root.transform.Find("Icon") as RectTransform;
            if (iconTf == null)
            {
                var go = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                go.transform.SetParent(root.transform, false);
                iconTf = (RectTransform)go.transform;
            }
            StretchFill(iconTf, 2f); // minimal inset so the thumbnail nearly fills the tile
            var image = iconTf.GetComponent<Image>();
            image.raycastTarget = false;
            image.preserveAspect = true;
            image.enabled = true; // visibility driven by the GameObject's active state
            var iconLE = iconTf.GetComponent<LayoutElement>();
            if (iconLE != null) Object.DestroyImmediate(iconLE, true);
            iconTf.gameObject.SetActive(false); // UIManager turns it on per part

            // --- Label: fills the tile, centered, the fallback when no icon ---
            var label = root.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null)
            {
                StretchFill((RectTransform)label.transform, 6f);
                var labelLE = label.GetComponent<LayoutElement>();
                if (labelLE != null) Object.DestroyImmediate(labelLE, true);
                label.alignment = TextAlignmentOptions.Center;
                label.textWrappingMode = TextWrappingModes.Normal;
                label.enableAutoSizing = true;
                label.fontSizeMin = 12;
                label.fontSizeMax = 22;
                label.raycastTarget = false;
                label.gameObject.SetActive(true); // default fallback is visible
            }

            PrefabUtility.SaveAsPrefabAsset(root, PartButtonPrefabPath);
            Debug.Log("PartButton prefab: icon now fills the tile (shown when present); label is a centered fallback.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>Anchor a child to fill its parent with a uniform inset.</summary>
    private static void StretchFill(RectTransform rt, float padding)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = new Vector2(padding, padding);
        rt.offsetMax = new Vector2(-padding, -padding);
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
