using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Adds the inspector's section dividers — the full-bleed rules that separate
/// the creature name / Sockets / transform blocks — to the CURRENT scene,
/// without needing a full UI rebuild.
///
/// Why this is needed: the generator added those edges with BottomHairline(),
/// but the same objects carry a VerticalLayoutGroup, which seizes any child and
/// re-lays it out as a stacked row — overriding the anchors that were supposed
/// to pin it to the bottom edge. The rules here are marked ignoreLayout so the
/// layout group leaves them alone.
///
///   Tools > Creature Builder > Fix Section Dividers
/// </summary>
public static class CreatureBuilderDividerTool
{
    // Deliberately stronger than DesignTokens.Divider (ink 16%): these separate
    // whole sections, so they should read heavier than the rules inside one.
    private static Color SectionRuleColor => DesignTokens.Alpha(DesignTokens.Text, 0.30f);

    private const string RuleName = "SectionRule";

    [MenuItem("Tools/Creature Builder/Fix Section Dividers")]
    public static void FixMenu()
    {
        int n = Fix();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorUtility.DisplayDialog("Section Dividers",
            n > 0 ? $"Added/updated {n} section divider(s).\n\nSave the scene to keep it."
                  : "Couldn't find the inspector sections (NameWrap / Sockets).", "OK");
    }

    public static void FixDividersBatch()
    {
        try
        {
            EditorSceneManager.OpenScene("Assets/Scenes/MainScene.unity");
            int n = Fix();
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            Debug.Log($"SECTION DIVIDERS: {n} added/updated.");
            EditorApplication.Exit(0);
        }
        catch (System.Exception e)
        {
            Debug.LogError("FixDividersBatch failed: " + e);
            EditorApplication.Exit(1);
        }
    }

    public static int Fix()
    {
        int count = 0;

        // The blocks that should be closed off by a rule along their bottom edge.
        foreach (string section in new[] { "NameWrap", "Sockets" })
        {
            Transform t = FindDeep(section);
            if (t == null) { Debug.LogWarning($"Section '{section}' not found — skipped."); continue; }
            EnsureBottomRule(t.gameObject);
            count++;
        }

        // The transform rules (Offset / Rotation / Scale separators) are 1px,
        // which at this project's canvas scale (screenWidth / 1920) renders at
        // well under one device pixel and intermittently vanishes on reflow.
        // Give them a second pixel so they always rasterise.
        foreach (var img in Object.FindObjectsByType<Image>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (img.name != "Rule") continue;
            var rle = img.GetComponent<LayoutElement>();
            if (rle == null) rle = img.gameObject.AddComponent<LayoutElement>();
            rle.minHeight = 2; rle.preferredHeight = 2; rle.flexibleHeight = 0;
            count++;
        }

        // Vertical rules in the bars (brand | nav, save | exit) are 1px wide and
        // vanish at sub-1 canvas scales just like the horizontal ones.
        foreach (var img in Object.FindObjectsByType<Image>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (img.name != "Divider") continue;
            var dle = img.GetComponent<LayoutElement>();
            if (dle == null) dle = img.gameObject.AddComponent<LayoutElement>();
            dle.minWidth = 2; dle.preferredWidth = 2; dle.flexibleWidth = 0;
            count++;
        }

        // Repair the edges the layout groups swallowed elsewhere (top bar,
        // status bar, rail/inspector headers): once ignored by layout they can
        // sit on their anchors again.
        foreach (var img in Object.FindObjectsByType<Image>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (img.name != "Hairline" && img.name != "Border") continue;
            if (img.transform.parent == null) continue;
            if (img.transform.parent.GetComponent<LayoutGroup>() == null) continue;
            IgnoreLayout(img.gameObject);
        }

        return count;
    }

    // ------------------------------------------------------------------

    private static void EnsureBottomRule(GameObject section)
    {
        Transform existing = section.transform.Find(RuleName);
        GameObject rule = existing != null ? existing.gameObject
                                           : new GameObject(RuleName, typeof(RectTransform), typeof(Image));
        if (existing == null) rule.transform.SetParent(section.transform, false);

        var img = rule.GetComponent<Image>();
        if (img == null) img = rule.AddComponent<Image>();
        img.color = SectionRuleColor;
        img.raycastTarget = false;

        // Full-bleed along the bottom edge — longer than the transform rules,
        // which sit inside the section's 20px padding.
        var rt = (RectTransform)rule.transform;
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.sizeDelta = new Vector2(0f, 1f);
        rt.anchoredPosition = Vector2.zero;

        IgnoreLayout(rule);
        rule.transform.SetAsLastSibling();
    }

    /// <summary>Take an element out of its parent's layout so its anchors hold.</summary>
    private static void IgnoreLayout(GameObject go)
    {
        var le = go.GetComponent<LayoutElement>();
        if (le == null) le = go.AddComponent<LayoutElement>();
        le.ignoreLayout = true;
    }

    private static Transform FindDeep(string name)
    {
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t.name == name) return t;
        return null;
    }
}
