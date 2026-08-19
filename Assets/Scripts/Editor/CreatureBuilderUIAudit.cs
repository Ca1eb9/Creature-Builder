using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Text;
using TMPro;

/// <summary>
/// Diagnostic: opens MainScene and dumps the resolved rects of the UI shells so
/// layout problems can be seen without running the game.
///   Unity.exe -batchmode -quit -projectPath . -executeMethod CreatureBuilderUIAudit.Run
/// </summary>
public static class CreatureBuilderUIAudit
{
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/MainScene.unity");
        var canvas = Object.FindAnyObjectByType<Canvas>();
        var sb = new StringBuilder("UI AUDIT\n");

        foreach (string name in new[] { "TopBar", "StatusBar", "RailPanel", "RailGutter",
                                        "InspectorPanel", "InspectorGutter", "LibraryScreen" })
        {
            var t = Find(canvas.transform, name);
            if (t == null) { sb.AppendLine($"  {name}: MISSING"); continue; }
            var rt = (RectTransform)t;
            sb.AppendLine($"  {name}: active={t.gameObject.activeSelf} " +
                          $"aMin={rt.anchorMin} aMax={rt.anchorMax} " +
                          $"offMin={rt.offsetMin} offMax={rt.offsetMax} size={rt.rect.size}");
        }

        // Socket rows / slider rows resolved heights
        var socketList = Find(canvas.transform, "SocketList");
        if (socketList != null)
            sb.AppendLine($"  SocketList: children={socketList.childCount} size={((RectTransform)socketList).rect.size}");

        var xf = Find(canvas.transform, "Transform");
        if (xf != null)
            sb.AppendLine($"  Transform section: children={xf.childCount} size={((RectTransform)xf).rect.size}");

        var ui = Object.FindAnyObjectByType<UIManager>();
        if (ui != null)
        {
            sb.AppendLine("  --- UIManager refs ---");
            sb.AppendLine($"  railPanel={N(ui.railPanel)} railGutter={N(ui.railGutter)}");
            sb.AppendLine($"  inspectorPanel={N(ui.inspectorPanel)} inspectorGutter={N(ui.inspectorGutter)}");
            sb.AppendLine($"  railHide={N(ui.railHideButton)} railShow={N(ui.railShowButton)}");
            sb.AppendLine($"  inspHide={N(ui.inspectorHideButton)} inspShow={N(ui.inspectorShowButton)}");
            sb.AppendLine($"  library={N(ui.libraryScreen)} openLib={N(ui.openLibraryButton)} openBuild={N(ui.openBuildButton)}");
            sb.AppendLine($"  catContainer={N(ui.categoryButtonContainer)} partGrid={N(ui.partGridContainer)}");
            sb.AppendLine($"  socketsContainer={N(ui.socketsContainer)} socketRowPrefab={N(ui.socketRowPrefab)}");
            sb.AppendLine($"  statusLabel={N(ui.statusLabel)} railFooter={N(ui.railFooterLabel)}");
            sb.AppendLine($"  assembler={N(ui.assembler)} database={(ui.database ? ui.database.name : "NULL")} saveLoad={N(ui.saveLoad)}");
            sb.AppendLine($"  closeLib={N(ui.closeLibraryButton)} subtitle={N(ui.librarySubtitle)} empty={N(ui.libraryEmptyState)} search={N(ui.searchInput)}");
            sb.AppendLine($"  newCreature={N(ui.newCreatureButton)} save={N(ui.saveButton)} nameInput={N(ui.saveNameInput)}");
            sb.AppendLine($"  spin={N(ui.spinStateLabel)} railGutLbl={N(ui.railGutterLabel)} inspGutLbl={N(ui.inspectorGutterLabel)} libSearch={N(ui.librarySearchInput)}");
            sb.AppendLine($"  tooltips in scene={Object.FindObjectsByType<UITooltipTrigger>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length}");
        }
        var adj = Object.FindAnyObjectByType<PartAdjustmentPanel>();
        if (adj != null)
        {
            sb.AppendLine("  --- PartAdjustmentPanel refs ---");
            sb.AppendLine($"  posX={N(adj.posXSlider)} val={N(adj.posXValue)} scale={N(adj.scaleSlider)} scaleVal={N(adj.scaleValue)}");
            sb.AppendLine($"  reset={N(adj.resetButton)} title={N(adj.titleLabel)} contentRoot={N(adj.contentRoot)} assembler={N(adj.assembler)}");
        }

        Debug.Log(sb.ToString());
        EditorApplication.Exit(0);
    }

    private static string N(Object o) => o == null ? "NULL" : o.name;

    private static Transform Find(Transform root, string name)
    {
        foreach (Transform c in root)
        {
            if (c.name == name) return c;
            var f = Find(c, name);
            if (f != null) return f;
        }
        return null;
    }
}
