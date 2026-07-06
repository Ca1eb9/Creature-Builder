using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;

/// <summary>
/// Batch importer for body part prefabs. Turns the manual 6-step process
/// (create BodyPartData, name it, set category, assign prefab, make an icon,
/// add to database) into one menu click per folder of prefabs.
///
/// Usage: organize prefabs into category-named subfolders, e.g.
///
///   Assets/Prefabs/BodyParts/
///     Heads/GiraffeHead.prefab      → category Head
///     Torsos/BearTorso.prefab       → category Torso
///     FrontLegs/...                 → category FrontLegs
///
/// then run Tools > Creature Builder > Import Parts Folder... and pick
/// the BodyParts folder. Prefabs already referenced by an existing
/// BodyPartData are skipped, so re-running after adding models is safe.
/// </summary>
public static class CreatureBuilderImporter
{
    private const string DefaultOutputFolder = "Assets/ScriptableObjects/Parts";
    private const string DefaultIconFolder = "Assets/Textures/Icons";

    [MenuItem("Tools/Creature Builder/Import Parts Folder...")]
    public static void ImportMenu()
    {
        string absolute = EditorUtility.OpenFolderPanel(
            "Choose the folder containing category subfolders of prefabs",
            "Assets/Prefabs", "");
        if (string.IsNullOrEmpty(absolute)) return;

        string projectRoot = Path.GetDirectoryName(Application.dataPath).Replace('\\', '/');
        string relative = absolute.Replace('\\', '/');
        if (!relative.StartsWith(projectRoot + "/Assets"))
        {
            EditorUtility.DisplayDialog("Import Parts",
                "The folder must be inside this project's Assets folder.", "OK");
            return;
        }
        relative = relative.Substring(projectRoot.Length + 1); // "Assets/..."

        var databases = AssetDatabase.FindAssets("t:BodyPartDatabase")
            .Select(g => AssetDatabase.LoadAssetAtPath<BodyPartDatabase>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(d => d != null).ToList();
        if (databases.Count == 0)
        {
            EditorUtility.DisplayDialog("Import Parts",
                "No BodyPartDatabase asset found in the project.", "OK");
            return;
        }
        if (databases.Count > 1)
            Debug.LogWarning($"Multiple databases found; importing into '{databases[0].name}'.");

        Report report = ImportFolder(relative, databases[0]);
        Debug.Log(report.summary);
        EditorUtility.DisplayDialog("Import Parts", report.summary, "OK");
    }

    // ------------------------------------------------------------------

    public class Report
    {
        public int created;
        public int skippedExisting;
        public List<string> warnings = new List<string>();
        public string summary;
    }

    /// <summary>
    /// Core import. Separated from the menu so it can be driven by tests.
    /// </summary>
    public static Report ImportFolder(string sourceFolder, BodyPartDatabase database,
                                      string outputFolder = DefaultOutputFolder,
                                      string iconFolder = DefaultIconFolder)
    {
        var report = new Report();

        // Prefabs already referenced by any BodyPartData → skip (idempotency)
        var alreadyImported = new HashSet<GameObject>(
            AssetDatabase.FindAssets("t:BodyPartData")
                .Select(g => AssetDatabase.LoadAssetAtPath<BodyPartData>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(p => p != null && p.prefab != null)
                .Select(p => p.prefab));

        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { sourceFolder });

        foreach (string guid in prefabGuids)
        {
            string prefabPath = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) continue;

            if (alreadyImported.Contains(prefab))
            {
                report.skippedExisting++;
                continue;
            }

            // Category comes from the prefab's immediate parent folder name
            string parentFolder = Path.GetFileName(Path.GetDirectoryName(prefabPath));
            if (!TryParseCategory(parentFolder, out BodyPartCategory category))
            {
                report.warnings.Add($"'{prefabPath}': folder '{parentFolder}' doesn't match any BodyPartCategory — skipped. Rename the folder to e.g. Heads, Torsos, FrontLegs, Tails, Wings, Horns, Accessories.");
                continue;
            }

            if (prefab.GetComponentsInChildren<Renderer>(true).Length == 0)
                report.warnings.Add($"'{prefab.name}': prefab has no Renderer — it will be invisible in the app.");

            // --- Create the BodyPartData asset ---
            string niceName = ObjectNames.NicifyVariableName(
                prefab.name.Replace('_', ' ').Replace('-', ' ')).Trim();

            var data = ScriptableObject.CreateInstance<BodyPartData>();
            data.partName = niceName;
            data.category = category;
            data.prefab = prefab;

            string categoryFolder = $"{outputFolder}/{category}";
            EnsureAssetFolder(categoryFolder);
            string assetPath = AssetDatabase.GenerateUniqueAssetPath(
                $"{categoryFolder}/{niceName}.asset");
            AssetDatabase.CreateAsset(data, assetPath);

            // Assign the stable ID explicitly (OnValidate would also do this,
            // but being explicit removes any dependence on editor callbacks)
            var so = new SerializedObject(data);
            var idProp = so.FindProperty("partID");
            if (string.IsNullOrEmpty(idProp.stringValue))
            {
                idProp.stringValue = System.Guid.NewGuid().ToString();
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            // --- Icon ---
            try
            {
                Sprite icon = GenerateIcon(prefab, iconFolder, niceName);
                if (icon != null) data.icon = icon;
                else report.warnings.Add($"'{niceName}': icon generation produced nothing — part button will be text-only.");
            }
            catch (System.Exception e)
            {
                report.warnings.Add($"'{niceName}': icon generation failed ({e.Message}) — part button will be text-only.");
            }

            // --- Register in the database ---
            database.allParts.Add(data);
            EditorUtility.SetDirty(data);
            report.created++;
        }

        if (report.created > 0)
        {
            EditorUtility.SetDirty(database);
            AssetDatabase.SaveAssets();
        }

        var lines = new List<string>
        {
            $"Import complete: {report.created} part(s) created, {report.skippedExisting} already imported (skipped)."
        };
        if (report.warnings.Count > 0)
        {
            lines.Add($"{report.warnings.Count} warning(s):");
            lines.AddRange(report.warnings.Select(w => "  • " + w));
        }
        report.summary = string.Join("\n", lines);
        return report;
    }

    // ------------------------------------------------------------------

    private static bool TryParseCategory(string folderName, out BodyPartCategory category)
    {
        foreach (BodyPartCategory c in System.Enum.GetValues(typeof(BodyPartCategory)))
        {
            string n = c.ToString();
            if (folderName.Equals(n, System.StringComparison.OrdinalIgnoreCase) ||
                folderName.Equals(n + "s", System.StringComparison.OrdinalIgnoreCase))
            {
                category = c;
                return true;
            }
        }
        category = default;
        return false;
    }

    private static void EnsureAssetFolder(string assetFolder)
    {
        if (AssetDatabase.IsValidFolder(assetFolder)) return;
        Directory.CreateDirectory(assetFolder); // relative to project root works for Assets/ paths
        AssetDatabase.Refresh();
    }

    /// <summary>
    /// Renders a 256x256 thumbnail of the prefab from a 3/4 angle using
    /// PreviewRenderUtility (synchronous — unlike AssetPreview, this works
    /// reliably in batch mode), saves it as a PNG, imports it as a Sprite.
    /// </summary>
    private static Sprite GenerateIcon(GameObject prefab, string iconFolder, string niceName)
    {
        var pru = new PreviewRenderUtility();
        Texture2D captured;
        try
        {
            pru.camera.clearFlags = CameraClearFlags.SolidColor;
            pru.camera.backgroundColor = new Color(0.16f, 0.16f, 0.20f, 1f);
            pru.camera.fieldOfView = 30f;

            GameObject instance = Object.Instantiate(prefab);
            instance.transform.position = Vector3.zero;
            instance.transform.rotation = Quaternion.identity;
            pru.AddSingleGO(instance);

            // Frame the object's bounds from a 3/4 view
            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = renderers.Length > 0
                ? renderers[0].bounds
                : new Bounds(Vector3.zero, Vector3.one);
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

            float radius = Mathf.Max(bounds.extents.magnitude, 0.001f);
            float distance = radius / Mathf.Sin(pru.camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.15f;
            Vector3 viewDir = new Vector3(1f, 0.6f, -1f).normalized;

            pru.camera.transform.position = bounds.center + viewDir * distance;
            pru.camera.transform.LookAt(bounds.center);
            pru.camera.nearClipPlane = distance * 0.01f;
            pru.camera.farClipPlane = distance * 10f;

            if (pru.lights.Length > 0 && pru.lights[0] != null)
            {
                pru.lights[0].transform.rotation = Quaternion.Euler(40f, -25f, 0f);
                pru.lights[0].intensity = 1.3f;
            }
            if (pru.lights.Length > 1 && pru.lights[1] != null)
                pru.lights[1].intensity = 0.6f;

            pru.BeginStaticPreview(new Rect(0f, 0f, 256f, 256f));
            pru.camera.Render();
            captured = pru.EndStaticPreview();
        }
        finally
        {
            pru.Cleanup();
        }

        if (captured == null) return null;

        EnsureAssetFolder(iconFolder);
        string pngPath = AssetDatabase.GenerateUniqueAssetPath($"{iconFolder}/{niceName}.png");
        File.WriteAllBytes(pngPath, captured.EncodeToPNG());
        AssetDatabase.ImportAsset(pngPath);

        var importer = (TextureImporter)AssetImporter.GetAtPath(pngPath);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.mipmapEnabled = false;
        importer.maxTextureSize = 256;
        importer.SaveAndReimport();

        return AssetDatabase.LoadAssetAtPath<Sprite>(pngPath);
    }
}
