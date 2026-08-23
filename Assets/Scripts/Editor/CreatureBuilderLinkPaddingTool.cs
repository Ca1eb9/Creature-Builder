using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Surgical padding pass for the top-bar nav links and the inline text links.
///
/// Unlike "Rebuild Studio UI", this does NOT regenerate the scene. It walks the
/// open scene, matches controls by their "Nav_" / "Link_" name prefix, and edits
/// only their HorizontalLayoutGroup padding — so hand-tuning done elsewhere in
/// the scene survives untouched. Matching by prefix also means renamed controls
/// (e.g. Nav_Lib) are still picked up. Re-runnable.
///
///   Tools > Creature Builder > Resize Nav and Text Link Padding
/// </summary>
public static class CreatureBuilderLinkPaddingTool
{
    // RectOffset is (left, right, top, bottom).
    private const int NavL = 8, NavR = 8, NavT = 6, NavB = 6;
    private const int LinkL = 12, LinkR = 12, LinkT = 6, LinkB = 6;

    private const string NavPrefix = "Nav_";
    private const string LinkPrefix = "Link_";

    [MenuItem("Tools/Creature Builder/Resize Nav and Text Link Padding")]
    public static void ResizeMenu()
    {
        int n = Apply();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorUtility.DisplayDialog("Nav / Link Padding",
            n > 0 ? $"Updated {n} control(s). Save the scene (Ctrl+S) to keep it."
                  : "No Nav_/Link_ controls found in the open scene.", "OK");
    }

    /// <summary>
    ///   Unity.exe -batchmode -quit -projectPath ...
    ///     -executeMethod CreatureBuilderLinkPaddingTool.ResizeBatch
    /// </summary>
    public static void ResizeBatch()
    {
        try
        {
            EditorSceneManager.OpenScene("Assets/Scenes/MainScene.unity");
            int n = Apply();
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            Debug.Log($"LINK PADDING: updated {n} control(s).");
            EditorApplication.Exit(0);
        }
        catch (System.Exception e)
        {
            Debug.LogError("ResizeBatch failed: " + e);
            EditorApplication.Exit(1);
        }
    }

    public static int Apply()
    {
        int changed = 0;
        foreach (var group in Object.FindObjectsByType<HorizontalLayoutGroup>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            string n = group.gameObject.name;
            bool isNav = n.StartsWith(NavPrefix, System.StringComparison.Ordinal);
            bool isLink = n.StartsWith(LinkPrefix, System.StringComparison.Ordinal);
            if (!isNav && !isLink) continue;

            Undo.RecordObject(group, "Resize link padding");
            // A fresh RectOffset per component: it's a reference type, so sharing
            // one instance would alias every control to the same object.
            group.padding = isNav
                ? new RectOffset(NavL, NavR, NavT, NavB)
                : new RectOffset(LinkL, LinkR, LinkT, LinkB);
            EditorUtility.SetDirty(group);
            changed++;
            Debug.Log($"  padding -> {(isNav ? "nav" : "link")}: {PathOf(group.transform)}");
        }
        return changed;
    }

    private static string PathOf(Transform t)
    {
        string p = t.name;
        while (t.parent != null) { t = t.parent; p = t.name + "/" + p; }
        return p;
    }
}
