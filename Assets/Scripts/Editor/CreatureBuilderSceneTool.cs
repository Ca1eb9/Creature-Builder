using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;

/// <summary>
/// Reconstructs the MainScene UI as the Classical "plate rail" layout:
/// top bar, 340px parts rail (+ collapse gutter), inspector (+ gutter),
/// status bar, and a Library sheet. Keeps the logic components (UIManager,
/// PartAdjustmentPanel, CreatureSaveLoad) and their asset references, rebuilds
/// the visuals, and re-wires every serialized reference.
///
///   Tools > Creature Builder > Rebuild Studio UI   (or RebuildStudioBatch headless)
/// </summary>
public static class CreatureBuilderSceneTool
{
    private const float TopH = 56f, BottomH = 40f, RailW = 340f, GutterW = 40f;

    [MenuItem("Tools/Creature Builder/Rebuild Studio UI")]
    public static void RebuildMenu()
    {
        Build();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorUtility.DisplayDialog("Rebuild Studio UI", "Rebuilt. Save the scene to keep it.", "OK");
    }

    public static void RebuildStudioBatch()
    {
        try
        {
            EditorSceneManager.OpenScene("Assets/Scenes/MainScene.unity");
            Build();
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            Debug.Log("STUDIO UI: rebuilt and saved.");
            EditorApplication.Exit(0);
        }
        catch (System.Exception e) { Debug.LogError("RebuildStudioBatch failed: " + e); EditorApplication.Exit(1); }
    }

    // Prefab handles
    private static GameObject PartCard, CategoryCell, SliderRow, SocketRow, CreatureCard;

    private static void LoadPrefabs()
    {
        PartCard = L("PartCard"); CategoryCell = L("CategoryCell"); SliderRow = L("SliderRow");
        SocketRow = L("SocketRow"); CreatureCard = L("CreatureCard");
    }
    private static GameObject L(string n) => AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/UI/{n}.prefab");

    // ==================================================================
    public static void Build()
    {
        LoadPrefabs();

        var uiMgr = Object.FindAnyObjectByType<UIManager>();
        var adj = Object.FindAnyObjectByType<PartAdjustmentPanel>();
        var saveLoad = Object.FindAnyObjectByType<CreatureSaveLoad>();
        var assembler = Object.FindAnyObjectByType<CreatureAssembler>();
        var canvas = FindMainCanvas();
        if (uiMgr == null || canvas == null) { Debug.LogError("Missing UIManager or Canvas."); return; }

        // Keep logic hosts; strip their visual children and any other Canvas UI.
        Detach(uiMgr); Detach(adj); Detach(saveLoad);
        ClearChildren(uiMgr.gameObject);
        if (adj != null) ClearChildren(adj.gameObject);
        if (saveLoad != null) ClearChildren(saveLoad.gameObject);
        ClearChildren(canvas.gameObject);

        // uiMgr sits under the Canvas so its screenshot hide works.
        uiMgr.transform.SetParent(canvas.transform, false);

        var root = canvas.transform;

        // ---- TOP BAR ----
        var top = Panel("TopBar", root, DesignTokens.Bg);
        Dock(top, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -TopH), new Vector2(0, 0), new Vector2(0.5f, 1));
        HBar(top, 20, 20, 0, 0, 18, TextAnchor.MiddleLeft);
        BottomHairline(top);
        Head(top, "Creature Builder", 18);
        VDivider(top);
        var openBuild = NavLink(top, "Build", true);
        var openLib = NavLink(top, "Library", false);
        Spacer(top);
        var randomize = Btn(top.transform, "Randomize", false);
        var clear = Btn(top.transform, "Clear", false);
        var autospin = Btn(top.transform, "Auto-spin", false);
        var screenshot = Btn(top.transform, "Screenshot", false);
        var save = Btn(top.transform, "Save creature", true);
        VDivider(top);
        var exit = Btn(top.transform, "Exit", false);

        // ---- STATUS BAR ----
        var status = Panel("StatusBar", root, DesignTokens.Bg);
        Dock(status, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 0), new Vector2(0, BottomH), new Vector2(0.5f, 0));
        HBar(status, 20, 20, 0, 0, 18, TextAnchor.MiddleLeft);
        TopHairline(status);
        var statusLabel = Label(status.transform, "0 parts · 0 in library", 12.5f, DesignTokens.Neutral700, DesignTokens.BodyFont);
        Fit(statusLabel);
        var hint = Label(status.transform, "Drag to rotate · Scroll to zoom · Adjust a part in the inspector", 12.5f, DesignTokens.Neutral700, DesignTokens.BodyFont);
        Fit(hint);

        // ---- PARTS RAIL ----
        var rail = Panel("RailPanel", root, DesignTokens.Bg);
        DockSide(rail, left: true, width: RailW);
        RightHairline(rail);
        var railV = VBar(rail.gameObject, 0, 0, 0, 0, 0, TextAnchor.UpperLeft);

        // header
        var railHead = Panel("Header", rail.transform, new Color(0, 0, 0, 0));
        RowH(railHead, 44); HBar(railHead, 18, 18, 0, 0, 8, TextAnchor.MiddleLeft); BottomHairline(railHead);
        var pKick = Label(railHead.transform, "PARTS", 10, DesignTokens.Accent, DesignTokens.BodyFont); Fit(pKick);
        ((TextMeshProUGUI)pKick.GetComponent<TextMeshProUGUI>()).characterSpacing = 10;
        Spacer(railHead);
        var railHide = TextLink(railHead, "Hide ‹", DesignTokens.Neutral600);

        // search (visual stub)
        var searchWrap = Panel("Search", rail.transform, new Color(0, 0, 0, 0)); RowH(searchWrap, 46);
        HBar(searchWrap, 18, 18, 8, 8, 0, TextAnchor.MiddleCenter);
        Input(searchWrap.transform, "Search parts…");

        // category grid (2-col CategoryCell)
        var catGrid = Panel("CategoryGrid", rail.transform, new Color(0, 0, 0, 0));
        var catGL = catGrid.AddComponent<GridLayoutGroup>();
        catGL.cellSize = new Vector2((RailW) / 2f, 40); catGL.spacing = Vector2.zero;
        catGL.constraint = GridLayoutGroup.Constraint.FixedColumnCount; catGL.constraintCount = 2;
        var catFit = catGrid.AddComponent<ContentSizeFitter>(); catFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        BottomHairline(catGrid);

        // part grid inside a scroll view
        var (partViewport, partContent) = ScrollArea(rail.transform);
        var pGL = partContent.AddComponent<GridLayoutGroup>();
        pGL.cellSize = new Vector2(146, 184); pGL.spacing = new Vector2(12, 14);
        pGL.padding = new RectOffset(18, 18, 16, 16);
        pGL.constraint = GridLayoutGroup.Constraint.FixedColumnCount; pGL.constraintCount = 2;
        var pFit = partContent.AddComponent<ContentSizeFitter>(); pFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var partScrollLE = partViewport.transform.parent.gameObject.AddComponent<LayoutElement>(); partScrollLE.flexibleHeight = 1;

        // footer
        var railFoot = Panel("Footer", rail.transform, new Color(0, 0, 0, 0)); RowH(railFoot, 34);
        HBar(railFoot, 18, 18, 0, 0, 0, TextAnchor.MiddleLeft); TopHairline(railFoot);
        var railFooter = Label(railFoot.transform, "", 11.5f, DesignTokens.Neutral600, DesignTokens.BodyFont); Fit(railFooter);

        // rail gutter (collapsed)
        var railGutter = Panel("RailGutter", root, DesignTokens.Bg);
        DockSide(railGutter, left: true, width: GutterW); RightHairline(railGutter);
        VBar(railGutter.gameObject, 0, 0, 14, 0, 16, TextAnchor.UpperCenter);
        var railShow = TextLink(railGutter, "|▶", DesignTokens.Accent);
        railGutter.SetActive(false);

        // ---- INSPECTOR ----
        var insp = Panel("InspectorPanel", root, DesignTokens.Bg);
        DockSide(insp, left: false, width: RailW); LeftHairline(insp);
        VBar(insp.gameObject, 0, 0, 0, 0, 0, TextAnchor.UpperLeft);

        var inspHead = Panel("Header", insp.transform, new Color(0, 0, 0, 0)); RowH(inspHead, 44);
        HBar(inspHead, 20, 20, 0, 0, 8, TextAnchor.MiddleLeft); BottomHairline(inspHead);
        var inspHide = TextLink(inspHead, "› Hide", DesignTokens.Neutral600);
        Spacer(inspHead);
        var iKick = Label(inspHead.transform, "INSPECTOR", 10, DesignTokens.Accent, DesignTokens.BodyFont); Fit(iKick);
        iKick.GetComponent<TextMeshProUGUI>().characterSpacing = 10;

        // name + status
        var nameWrap = Panel("NameWrap", insp.transform, new Color(0, 0, 0, 0)); RowH(nameWrap, 60);
        VBar(nameWrap, 20, 20, 12, 8, 4, TextAnchor.UpperLeft); BottomHairline(nameWrap);
        var nameInput = Input(nameWrap.transform, "Creature name");
        nameInput.textComponent.font = DesignTokens.HeadingFont; nameInput.pointSize = 22;

        // sockets
        var sockWrap = Panel("Sockets", insp.transform, new Color(0, 0, 0, 0));
        VBar(sockWrap, 20, 20, 14, 14, 6, TextAnchor.UpperLeft); BottomHairline(sockWrap);
        var sKick = Label(sockWrap.transform, "SOCKETS", 10, DesignTokens.Neutral600, DesignTokens.BodyFont); Fit(sKick);
        sKick.GetComponent<TextMeshProUGUI>().characterSpacing = 10;
        var socketsContainer = Panel("SocketList", sockWrap.transform, new Color(0, 0, 0, 0));
        VBar(socketsContainer, 0, 0, 0, 0, 5, TextAnchor.UpperLeft);
        socketsContainer.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // transform section
        var xfWrap = Panel("Transform", insp.transform, new Color(0, 0, 0, 0));
        var xfLE = xfWrap.AddComponent<LayoutElement>(); xfLE.flexibleHeight = 1;
        VBar(xfWrap, 20, 20, 16, 16, 10, TextAnchor.UpperLeft);
        var xfHead = Panel("Head", xfWrap.transform, new Color(0, 0, 0, 0)); RowH(xfHead, 24);
        HBar(xfHead, 0, 0, 0, 0, 0, TextAnchor.MiddleLeft);
        var xfTitle = Label(xfHead.transform, "Part transform", 18, DesignTokens.Text, DesignTokens.HeadingFont); Fit(xfTitle);
        Spacer(xfHead);
        var resetLink = TextLink(xfHead, "Reset", DesignTokens.Accent);

        Label(xfWrap.transform, "OFFSET (m)", 10, DesignTokens.Neutral600, DesignTokens.BodyFont).GetComponent<TextMeshProUGUI>().characterSpacing = 8;
        var (sPX, vPX) = MakeSliderRow(xfWrap.transform, "X");
        var (sPY, vPY) = MakeSliderRow(xfWrap.transform, "Y");
        var (sPZ, vPZ) = MakeSliderRow(xfWrap.transform, "Z");
        Label(xfWrap.transform, "ROTATION (°)", 10, DesignTokens.Neutral600, DesignTokens.BodyFont).GetComponent<TextMeshProUGUI>().characterSpacing = 8;
        var (sRX, vRX) = MakeSliderRow(xfWrap.transform, "X");
        var (sRY, vRY) = MakeSliderRow(xfWrap.transform, "Y");
        var (sRZ, vRZ) = MakeSliderRow(xfWrap.transform, "Z");
        Label(xfWrap.transform, "SCALE", 10, DesignTokens.Neutral600, DesignTokens.BodyFont).GetComponent<TextMeshProUGUI>().characterSpacing = 8;
        var (sSc, vSc) = MakeSliderRow(xfWrap.transform, "×");

        // inspector gutter
        var inspGutter = Panel("InspectorGutter", root, DesignTokens.Bg);
        DockSide(inspGutter, left: false, width: GutterW); LeftHairline(inspGutter);
        VBar(inspGutter.gameObject, 0, 0, 14, 0, 16, TextAnchor.UpperCenter);
        var inspShow = TextLink(inspGutter, "◀|", DesignTokens.Accent);
        inspGutter.SetActive(false);

        // ---- LIBRARY SHEET ----
        var library = Panel("LibraryScreen", root, DesignTokens.Alpha(DesignTokens.Neutral900, 0.45f));
        Fill(library);
        var sheet = Panel("Sheet", library.transform, DesignTokens.Bg, DesignTokens.RoundedSprite);
        Dock(sheet, new Vector2(0, 0), new Vector2(1, 1), new Vector2(220, 112), new Vector2(-220, -112), new Vector2(0.5f, 0.5f));
        Border(sheet, DesignTokens.Divider);
        VBar(sheet, 0, 0, 0, 0, 0, TextAnchor.UpperLeft);
        var libHead = Panel("Header", sheet.transform, new Color(0, 0, 0, 0)); RowH(libHead, 90);
        VBar(libHead, 32, 32, 24, 18, 4, TextAnchor.UpperLeft); BottomHairline(libHead);
        Label(libHead.transform, "LIBRARY", 10, DesignTokens.Accent, DesignTokens.BodyFont).GetComponent<TextMeshProUGUI>().characterSpacing = 10;
        var libTitle = Label(libHead.transform, "Saved creatures", 30, DesignTokens.Text, DesignTokens.HeadingFont); Fit(libTitle);
        var newBtn = Btn(libHead.transform, "New creature", true);

        var (libViewport, libContent) = ScrollArea(sheet.transform);
        var libGL = libContent.AddComponent<GridLayoutGroup>();
        libGL.cellSize = new Vector2(300, 330); libGL.spacing = new Vector2(24, 24);
        libGL.padding = new RectOffset(32, 32, 24, 24);
        libGL.constraint = GridLayoutGroup.Constraint.FixedColumnCount; libGL.constraintCount = 4;
        libContent.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var libScrollLE = libViewport.transform.parent.gameObject.AddComponent<LayoutElement>(); libScrollLE.flexibleHeight = 1;
        library.SetActive(false);

        // ================= WIRE UIManager =================
        uiMgr.categoryButtonContainer = catGrid.transform;
        uiMgr.partGridContainer = partContent.transform;
        uiMgr.categoryButtonPrefab = CategoryCell;
        uiMgr.partButtonPrefab = PartCard;
        uiMgr.randomizeButton = randomize; uiMgr.clearButton = clear; uiMgr.screenshotButton = screenshot;
        uiMgr.autoRotateButton = autospin; uiMgr.exitButton = exit; uiMgr.saveButton = save;
        uiMgr.saveNameInput = nameInput;
        uiMgr.loadListContainer = libContent.transform;
        uiMgr.loadListEntryPrefab = CreatureCard;
        uiMgr.socketsContainer = socketsContainer.transform;
        uiMgr.socketRowPrefab = SocketRow;
        uiMgr.railPanel = rail; uiMgr.railGutter = railGutter;
        uiMgr.railHideButton = railHide; uiMgr.railShowButton = railShow;
        uiMgr.inspectorPanel = insp; uiMgr.inspectorGutter = inspGutter;
        uiMgr.inspectorHideButton = inspHide; uiMgr.inspectorShowButton = inspShow;
        uiMgr.libraryScreen = library; uiMgr.openLibraryButton = openLib; uiMgr.openBuildButton = openBuild;
        uiMgr.newCreatureButton = newBtn;
        uiMgr.statusLabel = statusLabel.GetComponent<TextMeshProUGUI>();
        uiMgr.railFooterLabel = railFooter.GetComponent<TextMeshProUGUI>();
        uiMgr.partNameLabel = null; uiMgr.partDescLabel = null;
        if (saveLoad != null) uiMgr.saveLoad = saveLoad;
        if (assembler != null) uiMgr.assembler = assembler;
        if (adj != null) uiMgr.adjustmentPanel = adj;
        EditorUtility.SetDirty(uiMgr);

        // ================= WIRE PartAdjustmentPanel =================
        if (adj != null)
        {
            adj.posXSlider = sPX; adj.posYSlider = sPY; adj.posZSlider = sPZ;
            adj.rotXSlider = sRX; adj.rotYSlider = sRY; adj.rotZSlider = sRZ; adj.scaleSlider = sSc;
            adj.posXValue = vPX; adj.posYValue = vPY; adj.posZValue = vPZ;
            adj.rotXValue = vRX; adj.rotYValue = vRY; adj.rotZValue = vRZ; adj.scaleValue = vSc;
            adj.resetButton = resetLink.GetComponent<Button>();
            adj.titleLabel = xfTitle.GetComponent<TextMeshProUGUI>();
            adj.contentRoot = xfWrap; adj.toggleButton = null; adj.toggleLabel = null;
            if (assembler != null) adj.assembler = assembler;
            EditorUtility.SetDirty(adj);
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
    }

    // ==================================================================
    //  SLIDER ROW (instantiate prefab, return its Slider + Value label)
    // ==================================================================
    private static (Slider, TextMeshProUGUI) MakeSliderRow(Transform parent, string axis)
    {
        var row = (GameObject)PrefabUtility.InstantiatePrefab(SliderRow, parent);
        var axisT = row.transform.Find("Axis")?.GetComponent<TextMeshProUGUI>();
        if (axisT != null) axisT.text = axis;
        var slider = row.GetComponentInChildren<Slider>();
        var val = row.transform.Find("Value")?.GetComponent<TextMeshProUGUI>();
        return (slider, val);
    }

    // ==================================================================
    //  UI HELPERS
    // ==================================================================
    private static Canvas FindMainCanvas()
    {
        foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (c.renderMode == RenderMode.ScreenSpaceOverlay || c.renderMode == RenderMode.ScreenSpaceCamera) return c.rootCanvas;
        return Object.FindAnyObjectByType<Canvas>();
    }

    private static void Detach(Component c) { if (c != null) c.transform.SetParent(null, false); }

    private static void ClearChildren(GameObject go)
    {
        for (int i = go.transform.childCount - 1; i >= 0; i--)
            Object.DestroyImmediate(go.transform.GetChild(i).gameObject);
    }

    private static GameObject Panel(string name, Transform parent, Color color, Sprite sprite = null)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>(); img.color = color;
        if (sprite != null) { img.sprite = sprite; img.type = Image.Type.Sliced; }
        else img.raycastTarget = color.a > 0.001f;
        return go;
    }

    private static void Dock(GameObject go, Vector2 aMin, Vector2 aMax, Vector2 offMin, Vector2 offMax, Vector2 pivot)
    {
        var rt = (RectTransform)go.transform;
        rt.anchorMin = aMin; rt.anchorMax = aMax; rt.pivot = pivot;
        rt.offsetMin = offMin; rt.offsetMax = offMax;
    }

    private static void DockSide(GameObject go, bool left, float width)
    {
        var rt = (RectTransform)go.transform;
        if (left)
        {
            rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 0.5f);
            rt.offsetMin = new Vector2(0, BottomH); rt.offsetMax = new Vector2(width, -TopH);
        }
        else
        {
            rt.anchorMin = new Vector2(1, 0); rt.anchorMax = new Vector2(1, 1); rt.pivot = new Vector2(1, 0.5f);
            rt.offsetMin = new Vector2(-width, BottomH); rt.offsetMax = new Vector2(0, -TopH);
        }
    }

    private static void Fill(GameObject go)
    {
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    private static void RowH(GameObject go, float h) => go.AddComponent<LayoutElement>().minHeight = h;

    private static HorizontalLayoutGroup HBar(GameObject go, int l, int r, int t, int b, float spacing, TextAnchor a)
    {
        var h = go.AddComponent<HorizontalLayoutGroup>();
        h.padding = new RectOffset(l, r, t, b); h.spacing = spacing; h.childAlignment = a;
        h.childControlWidth = h.childControlHeight = true; h.childForceExpandWidth = h.childForceExpandHeight = false;
        return h;
    }

    private static VerticalLayoutGroup VBar(GameObject go, int l, int r, int t, int b, float spacing, TextAnchor a)
    {
        var v = go.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(l, r, t, b); v.spacing = spacing; v.childAlignment = a;
        v.childControlWidth = v.childControlHeight = true; v.childForceExpandWidth = true; v.childForceExpandHeight = false;
        return v;
    }

    private static GameObject Label(Transform parent, string text, float size, Color color, TMP_FontAsset font)
    {
        var go = new GameObject("Label", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.text = text; t.fontSize = size; t.color = color; t.alignment = TextAlignmentOptions.Left;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        return go;
    }

    private static void Head(GameObject parent, string text, float size)
    {
        var l = Label(parent.transform, text, size, DesignTokens.Text, DesignTokens.HeadingFont); Fit(l);
    }

    private static void Fit(GameObject label)
    {
        // Let TMP's own ILayoutElement drive width; just guarantee a sane height.
        label.AddComponent<LayoutElement>().minHeight = 18;
    }

    private static void Spacer(GameObject parent)
    {
        var go = new GameObject("Spacer", typeof(RectTransform));
        go.transform.SetParent(parent.transform, false);
        go.AddComponent<LayoutElement>().flexibleWidth = 1;
    }

    private static void VDivider(GameObject parent)
    {
        var go = Panel("Divider", parent.transform, DesignTokens.Divider);
        var le = go.AddComponent<LayoutElement>(); le.minWidth = 1; le.minHeight = 24;
    }

    private static Button Btn(Transform parent, string label, bool primary)
    {
        var go = new GameObject("Btn_" + label, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>(); img.sprite = DesignTokens.OutlineSprite; img.type = Image.Type.Sliced;
        img.color = primary ? DesignTokens.Accent : DesignTokens.Divider;
        var le = go.AddComponent<LayoutElement>(); le.minHeight = 34;
        var h = go.AddComponent<HorizontalLayoutGroup>();
        h.padding = new RectOffset(14, 14, 6, 6); h.childAlignment = TextAnchor.MiddleCenter;
        h.childControlWidth = h.childControlHeight = true; h.childForceExpandWidth = h.childForceExpandHeight = false;
        var btn = go.GetComponent<Button>(); btn.targetGraphic = img;
        var lbl = Label(go.transform, label, 13.5f, primary ? DesignTokens.Accent : DesignTokens.Text, DesignTokens.HeadingFont);
        lbl.GetComponent<TextMeshProUGUI>().raycastTarget = false;
        return btn;
    }

    private static Button NavLink(GameObject parent, string label, bool active)
    {
        var go = new GameObject("Nav_" + label, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent.transform, false);
        var img = go.GetComponent<Image>(); img.color = new Color(0, 0, 0, 0);
        var le = go.AddComponent<LayoutElement>(); le.minHeight = 24;
        var h = go.AddComponent<HorizontalLayoutGroup>();
        h.childControlWidth = h.childControlHeight = true; h.childForceExpandWidth = h.childForceExpandHeight = false;
        var lbl = Label(go.transform, label, 14, active ? DesignTokens.Accent : DesignTokens.Neutral700, DesignTokens.BodyFont);
        lbl.GetComponent<TextMeshProUGUI>().raycastTarget = false;
        return go.GetComponent<Button>();
    }

    private static Button TextLink(GameObject parent, string label, Color color)
    {
        var go = new GameObject("Link_" + label, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent.transform, false);
        var img = go.GetComponent<Image>(); img.color = new Color(0, 0, 0, 0);
        var le = go.AddComponent<LayoutElement>(); le.minHeight = 20; le.minWidth = 40;
        var h = go.AddComponent<HorizontalLayoutGroup>();
        h.childControlWidth = h.childControlHeight = true; h.childForceExpandWidth = h.childForceExpandHeight = false;
        var lbl = Label(go.transform, label, 12, color, DesignTokens.BodyFont);
        lbl.GetComponent<TextMeshProUGUI>().raycastTarget = false;
        return go.GetComponent<Button>();
    }

    private static TMP_InputField Input(Transform parent, string placeholder)
    {
        var go = new GameObject("Input", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>(); img.sprite = DesignTokens.OutlineSprite; img.type = Image.Type.Sliced; img.color = DesignTokens.Divider;
        var le = go.AddComponent<LayoutElement>(); le.minHeight = 36; le.flexibleWidth = 1;
        var input = go.AddComponent<TMP_InputField>();

        var textArea = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
        textArea.transform.SetParent(go.transform, false);
        var tar = (RectTransform)textArea.transform; tar.anchorMin = Vector2.zero; tar.anchorMax = Vector2.one;
        tar.offsetMin = new Vector2(10, 4); tar.offsetMax = new Vector2(-10, -4);

        var ph = Label(textArea.transform, placeholder, 14, DesignTokens.Neutral500, DesignTokens.BodyFont);
        Stretch((RectTransform)ph.transform);
        var text = Label(textArea.transform, "", 14, DesignTokens.Text, DesignTokens.BodyFont);
        Stretch((RectTransform)text.transform);

        input.textViewport = tar;
        input.textComponent = text.GetComponent<TextMeshProUGUI>();
        input.placeholder = ph.GetComponent<TextMeshProUGUI>();
        input.targetGraphic = img;
        return input;
    }

    private static (GameObject viewport, GameObject content) ScrollArea(Transform parent)
    {
        var scroll = new GameObject("Scroll", typeof(RectTransform), typeof(ScrollRect));
        scroll.transform.SetParent(parent, false);
        var sr = scroll.GetComponent<ScrollRect>();
        sr.horizontal = false; sr.vertical = true; sr.scrollSensitivity = 24; sr.movementType = ScrollRect.MovementType.Clamped;

        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D), typeof(Image));
        viewport.transform.SetParent(scroll.transform, false);
        var vp = (RectTransform)viewport.transform; vp.anchorMin = Vector2.zero; vp.anchorMax = Vector2.one; vp.offsetMin = Vector2.zero; vp.offsetMax = Vector2.zero; vp.pivot = new Vector2(0, 1);
        viewport.GetComponent<Image>().color = new Color(0, 0, 0, 0);

        var content = new GameObject("Content", typeof(RectTransform));
        content.transform.SetParent(viewport.transform, false);
        var cr = (RectTransform)content.transform; cr.anchorMin = new Vector2(0, 1); cr.anchorMax = new Vector2(1, 1); cr.pivot = new Vector2(0.5f, 1); cr.anchoredPosition = Vector2.zero; cr.sizeDelta = Vector2.zero;

        sr.viewport = vp; sr.content = cr;
        return (viewport, content);
    }

    private static void Stretch(RectTransform rt)
    { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero; }

    // hairline borders on one edge
    private static void EdgeLine(GameObject go, Vector2 aMin, Vector2 aMax, Vector2 size)
    {
        var line = Panel("Hairline", go.transform, DesignTokens.Divider);
        line.GetComponent<Image>().raycastTarget = false;
        var rt = (RectTransform)line.transform; rt.anchorMin = aMin; rt.anchorMax = aMax; rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size; rt.anchoredPosition = Vector2.zero;
    }
    private static void BottomHairline(GameObject go) => EdgeLine(go, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1));
    private static void TopHairline(GameObject go) => EdgeLine(go, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1));
    private static void LeftHairline(GameObject go) => EdgeLine(go, new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 0));
    private static void RightHairline(GameObject go) => EdgeLine(go, new Vector2(1, 0), new Vector2(1, 1), new Vector2(1, 0));

    private static void Border(GameObject go, Color color)
    {
        var b = Panel("Border", go.transform, color, DesignTokens.OutlineSprite);
        b.GetComponent<Image>().raycastTarget = false;
        Stretch((RectTransform)b.transform);
    }
}
