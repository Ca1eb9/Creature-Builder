using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;

/// <summary>
/// Batch importer for body part prefabs. Turns the manual 6-step process
/// (create BodyPartData, name it, set category, assign prefab, make an icon,
/// add to database) into one menu click.
///
/// Two workflows are supported:
///
/// 1) "Import Parts Folder..." — one prefab per part, category from the
///    prefab's parent folder name:
///
///      Assets/Prefabs/BodyParts/
///        Heads/GiraffeHead.prefab      → category Head
///        Torsos/BearTorso.prefab       → category Torso
///        FrontLegs/...                 → category FrontLegs
///
/// 2) "Import Multi-Part FBX Folder..." — one FBX per ANIMAL, with each part
///    as a separate object inside it, named after its category (case
///    insensitive, trailing 's' optional). Each such child is extracted into
///    its own part prefab so any head fits any torso:
///
///      Assets/Models/Wolf.fbx  → objects named Head, Torso, FrontLegs, ...
///
///    On extraction the child's own origin (the seam you set in Blender) is
///    placed at the part prefab's (0,0,0) so it lands exactly on the attach
///    point, and its authored orientation is baked in so it stays upright.
///
/// Both workflows are idempotent — re-running after adding models is safe.
/// </summary>
public static class CreatureBuilderImporter
{
    private const string DefaultOutputFolder = "Assets/ScriptableObjects/Parts";
    private const string DefaultIconFolder = "Assets/Textures/Icons";
    private const string DefaultExtractFolder = "Assets/Prefabs/BodyParts/Generated";

    // ==================================================================
    //  MENU ENTRY POINTS
    // ==================================================================

    [MenuItem("Tools/Creature Builder/Import Multi-Part FBX Folder...")]
    public static void ImportMultiPartMenu()
    {
        if (!TryPickProjectFolder(out string relative)) return;
        if (!TryGetDatabase(out BodyPartDatabase database)) return;

        Report report = ImportMultiPartFolder(relative, database);
        Debug.Log(report.summary);
        EditorUtility.DisplayDialog("Import Multi-Part FBX", report.summary, "OK");
    }

    // ------------------------------------------------------------------

    private static bool TryPickProjectFolder(out string relative)
    {
        relative = null;
        string absolute = EditorUtility.OpenFolderPanel(
            "Choose the folder to import from", "Assets", "");
        if (string.IsNullOrEmpty(absolute)) return false;

        string projectRoot = Path.GetDirectoryName(Application.dataPath).Replace('\\', '/');
        string rel = absolute.Replace('\\', '/');
        if (!rel.StartsWith(projectRoot + "/Assets"))
        {
            EditorUtility.DisplayDialog("Import Parts",
                "The folder must be inside this project's Assets folder.", "OK");
            return false;
        }
        relative = rel.Substring(projectRoot.Length + 1); // "Assets/..."
        return true;
    }

    private static bool TryGetDatabase(out BodyPartDatabase database)
    {
        var databases = AssetDatabase.FindAssets("t:BodyPartDatabase")
            .Select(g => AssetDatabase.LoadAssetAtPath<BodyPartDatabase>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(d => d != null).ToList();
        if (databases.Count == 0)
        {
            EditorUtility.DisplayDialog("Import Parts",
                "No BodyPartDatabase asset found in the project.", "OK");
            database = null;
            return false;
        }
        if (databases.Count > 1)
            Debug.LogWarning($"Multiple databases found; importing into '{databases[0].name}'.");
        database = databases[0];
        return true;
    }

    // ==================================================================
    //  REPORT
    // ==================================================================

    public class Report
    {
        public int created;
        public int skippedExisting;
        public List<string> warnings = new List<string>();
        public string summary;

        public void Finish(string header)
        {
            var lines = new List<string> { header };
            if (warnings.Count > 0)
            {
                lines.Add($"{warnings.Count} warning(s):");
                lines.AddRange(warnings.Select(w => "  • " + w));
            }
            summary = string.Join("\n", lines);
        }
    }

    // ==================================================================
    //  IMPORT — one FBX per animal, parts split inside by object name
    // ==================================================================

    /// <summary>
    /// Import a folder of multi-part model prefabs/FBX. Every child object
    /// whose name matches a BodyPartCategory is extracted into its own part
    /// prefab and registered. Separated from the menu so it can be tested.
    /// </summary>
    public static Report ImportMultiPartFolder(string sourceFolder, BodyPartDatabase database,
                                               string outputFolder = DefaultOutputFolder,
                                               string iconFolder = DefaultIconFolder,
                                               string extractFolder = DefaultExtractFolder)
    {
        var report = new Report();

        // FBX/model assets are GameObjects, not ".prefab" files — "t:GameObject"
        // matches both, whereas "t:Prefab" silently misses every raw .fbx.
        string[] prefabGuids = AssetDatabase.FindAssets("t:GameObject", new[] { sourceFolder });
        if (prefabGuids.Length == 0)
            report.warnings.Add($"No models or prefabs found in '{sourceFolder}'. Put your .fbx file(s) directly in this folder (or a subfolder) and choose that folder.");

        foreach (string guid in prefabGuids)
        {
            string modelPath = AssetDatabase.GUIDToAssetPath(guid);
            GameObject modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (modelPrefab == null) continue;

            string sourceName = Path.GetFileNameWithoutExtension(modelPath);

            // Work on a live instance so we can read WORLD orientation/scale.
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(modelPrefab);
            if (instance == null)
            {
                report.warnings.Add($"'{sourceName}': could not instantiate — skipped.");
                continue;
            }

            try
            {
                // Snapshot every category-named child in the assembled pose
                // BEFORE we touch anything (reparenting would change world values).
                var jobs = new List<(BodyPartCategory cat, GameObject go, Quaternion worldRot, Vector3 worldScale)>();
                var childNames = new List<string>();
                foreach (Transform t in instance.GetComponentsInChildren<Transform>(true))
                {
                    if (t == instance.transform) continue;
                    childNames.Add(t.name);
                    if (TryParseCategory(t.name, out BodyPartCategory cat))
                        jobs.Add((cat, t.gameObject, t.rotation, t.lossyScale));
                }

                if (jobs.Count == 0)
                {
                    // Single-object model: the mesh is on the root with no
                    // category-named child. Derive the category from the root
                    // object name, then the mesh name(s), then the file name,
                    // and import the whole model as one part.
                    var meshNames = CollectMeshNames(instance);
                    BodyPartCategory rootCat = default;
                    bool found = TryParseCategory(modelPrefab.name, out rootCat);
                    if (!found)
                        foreach (var mn in meshNames)
                            if (TryParseCategory(mn, out rootCat)) { found = true; break; }
                    if (!found)
                        found = TryParseCategory(sourceName, out rootCat);

                    if (found)
                    {
                        jobs.Add((rootCat, instance,
                                  instance.transform.rotation, instance.transform.lossyScale));
                    }
                    else
                    {
                        string kids = childNames.Count > 0 ? "[" + string.Join(", ", childNames) + "]" : "(no child objects)";
                        string meshes = meshNames.Count > 0 ? "[" + string.Join(", ", meshNames) + "]" : "(none)";
                        report.warnings.Add($"'{sourceName}': couldn't determine a category. Child objects: {kids}; mesh names: {meshes}; file: '{sourceName}'. Name the object, its mesh, or the file after a category (Head, Torso, FrontLegs, BackLegs, Tail, Wings, Horns, Accessories).");
                        continue;
                    }
                }

                foreach (var job in jobs)
                {
                    string catFolder = $"{extractFolder}/{job.cat}";
                    EnsureAssetFolder(catFolder);

                    string safeSource = SanitizeFileName(sourceName);
                    string extractedPath = $"{catFolder}/{safeSource}_{job.cat}.prefab";

                    // Idempotency: an extracted prefab already sitting here means
                    // this (animal, category) pair was imported on a prior run.
                    if (AssetDatabase.LoadAssetAtPath<GameObject>(extractedPath) != null)
                    {
                        report.skippedExisting++;
                        continue;
                    }

                    GameObject partPrefab = ExtractChildAsPrefab(
                        job.go, job.worldRot, job.worldScale, extractedPath);
                    if (partPrefab == null)
                    {
                        report.warnings.Add($"'{sourceName}' / '{job.cat}': extraction failed — skipped.");
                        continue;
                    }

                    string niceName = $"{Nicify(sourceName)} {Nicify(job.cat.ToString())}".Trim();
                    CreatePartData(partPrefab, job.cat, database, report, outputFolder, iconFolder, niceName);
                }
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        if (report.created > 0)
        {
            EditorUtility.SetDirty(database);
            AssetDatabase.SaveAssets();
        }

        report.Finish(
            $"Multi-part import complete: {report.created} part(s) created, {report.skippedExisting} already imported (skipped).");
        return report;
    }

    /// <summary>
    /// Clone one child object out of an assembled model into a standalone part
    /// prefab. The clone's own origin (the seam set in Blender) is moved to the
    /// prefab root's (0,0,0) so equipping at zero offset lands the seam on the
    /// attach point; the child's authored world orientation/scale is baked into
    /// the clone so it keeps standing upright.
    /// </summary>
    private static GameObject ExtractChildAsPrefab(GameObject child, Quaternion worldRot,
                                                   Vector3 worldScale, string prefabPath)
    {
        // A detached copy — the source instance is never mutated.
        GameObject copy = Object.Instantiate(child);
        GameObject partRoot = new GameObject(child.name);

        try
        {
            partRoot.transform.position = Vector3.zero;
            partRoot.transform.rotation = Quaternion.identity;
            partRoot.transform.localScale = Vector3.one;

            copy.transform.SetParent(partRoot.transform, false);
            copy.transform.localPosition = Vector3.zero;   // seam → attach anchor
            copy.transform.localRotation = worldRot;        // stay upright
            copy.transform.localScale = worldScale;

            return PrefabUtility.SaveAsPrefabAsset(partRoot, prefabPath);
        }
        finally
        {
            Object.DestroyImmediate(partRoot); // takes the copy with it
        }
    }

    // ==================================================================
    //  SHARED: turn a prefab into a BodyPartData (+ id, icon, registration)
    // ==================================================================

    private static void CreatePartData(GameObject prefab, BodyPartCategory category,
                                       BodyPartDatabase database, Report report,
                                       string outputFolder, string iconFolder,
                                       string overrideName = null)
    {
        if (prefab.GetComponentsInChildren<Renderer>(true).Length == 0)
            report.warnings.Add($"'{prefab.name}': prefab has no Renderer — it will be invisible in the app.");

        string niceName = overrideName ?? Nicify(prefab.name);

        var data = ScriptableObject.CreateInstance<BodyPartData>();
        data.partName = niceName;
        data.category = category;
        data.prefab = prefab;

        string categoryFolder = $"{outputFolder}/{category}";
        EnsureAssetFolder(categoryFolder);
        string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{categoryFolder}/{niceName}.asset");
        AssetDatabase.CreateAsset(data, assetPath);

        // Assign the stable ID explicitly (OnValidate would also do this, but
        // being explicit removes any dependence on editor callbacks)
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
            Sprite icon = GenerateIcon(prefab, iconFolder, niceName, Quaternion.Euler(data.rotationOffset));
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

    // ==================================================================
    //  HELPERS
    // ==================================================================

    private static string Nicify(string raw) =>
        ObjectNames.NicifyVariableName(raw.Replace('_', ' ').Replace('-', ' ')).Trim();

    private static string SanitizeFileName(string raw)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = raw.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        string cleaned = new string(chars).Trim();
        return string.IsNullOrEmpty(cleaned) ? "Model" : cleaned;
    }

    private static bool TryParseCategory(string rawName, out BodyPartCategory category)
    {
        category = default;
        if (string.IsNullOrEmpty(rawName)) return false;

        // Tolerate real-world naming noise: surrounding whitespace, Blender/FBX
        // duplicate suffixes like ".001" or " (1)", and separators inside a
        // compound name (so "Front_Legs" still reads as FrontLegs).
        string name = rawName.Trim();
        name = System.Text.RegularExpressions.Regex.Replace(name, @"[\s._\-]*(\(\d+\)|\d+)$", "");
        string collapsed = name.Replace("_", "").Replace(" ", "").Replace("-", "").Replace(".", "");

        foreach (BodyPartCategory c in System.Enum.GetValues(typeof(BodyPartCategory)))
        {
            string n = c.ToString();
            if (collapsed.Equals(n, System.StringComparison.OrdinalIgnoreCase) ||
                collapsed.Equals(n + "s", System.StringComparison.OrdinalIgnoreCase))
            {
                category = c;
                return true;
            }
        }
        return false;
    }

    /// <summary>Names of every mesh under an instance — used to categorize single-object models.</summary>
    private static List<string> CollectMeshNames(GameObject instance)
    {
        var names = new List<string>();
        foreach (var mf in instance.GetComponentsInChildren<MeshFilter>(true))
            if (mf.sharedMesh != null) names.Add(mf.sharedMesh.name);
        foreach (var smr in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (smr.sharedMesh != null) names.Add(smr.sharedMesh.name);
        return names;
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
    /// reliably in batch mode), with a TRANSPARENT background so only the part
    /// shows. Saves it as a PNG and imports it as a Sprite.
    ///
    /// Transparency is extracted by rendering the part twice — once on black,
    /// once on white — and solving for coverage per pixel. This is robust under
    /// URP, where the preview render target's own alpha channel is unreliable.
    /// </summary>
    private static Sprite GenerateIcon(GameObject prefab, string iconFolder, string niceName,
                                       Quaternion partRotation)
    {
        const int size = 256;
        var rect = new Rect(0f, 0f, size, size);
        var pru = new PreviewRenderUtility();
        Texture2D captured = null;

        try
        {
            pru.camera.clearFlags = CameraClearFlags.SolidColor;
            pru.camera.fieldOfView = 30f;

            GameObject instance = Object.Instantiate(prefab);
            instance.transform.position = Vector3.zero;
            // Orient the part exactly as it sits on the creature (its rotationOffset)
            // so the icon matches what the user sees in the app.
            instance.transform.rotation = partRotation;
            pru.AddSingleGO(instance);

            // Frame the object's bounds from a 3/4 view
            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = renderers.Length > 0
                ? renderers[0].bounds
                : new Bounds(Vector3.zero, Vector3.one);
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

            float radius = Mathf.Max(bounds.extents.magnitude, 0.001f);
            // radius is the bounding-SPHERE radius, so framing it edge-to-edge
            // (multiplier 1.0) can never clip from any angle while filling the
            // frame tightly. A hair under 1.0 fills a bit more since the actual
            // silhouette is always inscribed in that sphere.
            float distance = radius / Mathf.Sin(pru.camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * 0.95f;
            // A 3/4 FRONT view: on the +Z side the animal faces, off to one side
            // (~29°) and slightly above (~14°) for depth. Front-dominant so
            // single-sided parts (open legs, hollow torsos) don't show culled
            // back faces the way a steep or rear angle did. Keep this in sync
            // with the app camera in CreatureBuilderLayoutFixer.ViewCreatureFromFront.
            Vector3 viewDir = new Vector3(0.6f, 0.3f, 1f).normalized;

            pru.camera.transform.position = bounds.center + viewDir * distance;
            pru.camera.transform.LookAt(bounds.center);
            pru.camera.nearClipPlane = distance * 0.01f;
            pru.camera.farClipPlane = distance * 10f;

            // Light the +Z front that the icon camera views (see viewDir),
            // matching the app's key light (CreatureBuilderLayoutFixer
            // .AimKeyLightForFront) so tiles look like the live creature. Rays
            // travel with negative Z so they land on the front faces.
            if (pru.lights.Length > 0 && pru.lights[0] != null)
            {
                pru.lights[0].transform.rotation =
                    Quaternion.LookRotation(new Vector3(-0.5f, -0.7f, -0.5f).normalized);
                pru.lights[0].intensity = 1.3f;
            }
            if (pru.lights.Length > 1 && pru.lights[1] != null)
            {
                pru.lights[1].transform.rotation =
                    Quaternion.LookRotation(new Vector3(0.4f, -0.2f, -0.7f).normalized);
                pru.lights[1].intensity = 0.6f;
            }

            Color[] onBlack = RenderPixels(pru, rect, size, Color.black);
            Color[] onWhite = RenderPixels(pru, rect, size, Color.white);

            captured = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var outPixels = new Color[onBlack.Length];
            for (int i = 0; i < onBlack.Length; i++)
            {
                // observed = fg*a + bg*(1-a). On black: obs_b = fg*a. On white:
                // obs_w = fg*a + (1-a). So a = 1 - (obs_w - obs_b); premultiplied
                // colour is obs_b, un-premultiply to straight alpha for the PNG.
                float a = 1f - ((onWhite[i].r - onBlack[i].r) +
                                (onWhite[i].g - onBlack[i].g) +
                                (onWhite[i].b - onBlack[i].b)) / 3f;
                a = Mathf.Clamp01(a);
                outPixels[i] = a > 0.001f
                    ? new Color(onBlack[i].r / a, onBlack[i].g / a, onBlack[i].b / a, a)
                    : new Color(0f, 0f, 0f, 0f);
            }
            captured.SetPixels(outPixels);
            captured.Apply();
        }
        finally
        {
            pru.Cleanup();
        }

        if (captured == null) return null;

        EnsureAssetFolder(iconFolder);
        string pngPath = AssetDatabase.GenerateUniqueAssetPath($"{iconFolder}/{niceName}.png");
        File.WriteAllBytes(pngPath, captured.EncodeToPNG());
        Object.DestroyImmediate(captured);
        AssetDatabase.ImportAsset(pngPath);

        var importer = (TextureImporter)AssetImporter.GetAtPath(pngPath);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.maxTextureSize = 256;
        importer.SaveAndReimport();

        return AssetDatabase.LoadAssetAtPath<Sprite>(pngPath);
    }

    /// <summary>
    /// Render the already-configured preview on a solid background and read back
    /// RGBA pixels. Uses BeginStaticPreview, which is colour-managed (returns
    /// display-ready sRGB) — a raw RenderTexture readback would hand back linear
    /// values in a Linear-colour-space project and the PNG would come out dark.
    /// </summary>
    private static Color[] RenderPixels(PreviewRenderUtility pru, Rect rect, int size, Color background)
    {
        pru.camera.backgroundColor = background;
        pru.BeginStaticPreview(rect);
        pru.camera.Render();
        Texture2D tex = pru.EndStaticPreview();
        Color[] pixels = tex.GetPixels();
        Object.DestroyImmediate(tex);
        return pixels;
    }

    // ==================================================================
    //  REGENERATE ICONS (after switching to transparent, or re-tuning models)
    // ==================================================================

    [MenuItem("Tools/Creature Builder/Regenerate All Icons")]
    public static void RegenerateIconsMenu()
    {
        var parts = AssetDatabase.FindAssets("t:BodyPartData")
            .Select(g => AssetDatabase.LoadAssetAtPath<BodyPartData>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(p => p != null && p.prefab != null)
            .ToList();

        int done = 0, failed = 0;
        foreach (var p in parts)
        {
            // Remove the old icon PNG so we don't leave orphans behind
            if (p.icon != null)
            {
                string oldPath = AssetDatabase.GetAssetPath(p.icon);
                if (!string.IsNullOrEmpty(oldPath)) AssetDatabase.DeleteAsset(oldPath);
                p.icon = null;
            }

            try
            {
                Sprite icon = GenerateIcon(p.prefab, DefaultIconFolder, p.partName, Quaternion.Euler(p.rotationOffset));
                if (icon != null) { p.icon = icon; EditorUtility.SetDirty(p); done++; }
                else failed++;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Icon regen failed for '{p.partName}': {e.Message}");
                failed++;
            }
        }

        AssetDatabase.SaveAssets();
        string msg = $"Regenerated {done} icon(s)" + (failed > 0 ? $", {failed} failed (see console)." : ".");
        Debug.Log(msg);
        EditorUtility.DisplayDialog("Regenerate Icons", msg, "OK");
    }

    // ==================================================================
    //  CLEAR GENERATED PARTS (so a fixed model can be re-imported)
    // ==================================================================

    [MenuItem("Tools/Creature Builder/Clear Generated Parts")]
    public static void ClearGeneratedPartsMenu()
    {
        if (!EditorUtility.DisplayDialog("Clear Generated Parts",
            "Remove every part imported from a model — its BodyPartData, extracted prefab, and icon — and unregister it from the database?\n\n" +
            "Hand-made / test parts (anything whose prefab lives outside the Generated folder) are kept. This cannot be undone.",
            "Clear", "Cancel"))
            return;

        var databases = AssetDatabase.FindAssets("t:BodyPartDatabase")
            .Select(g => AssetDatabase.LoadAssetAtPath<BodyPartDatabase>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(d => d != null).ToList();

        var allData = AssetDatabase.FindAssets("t:BodyPartData")
            .Select(g => AssetDatabase.LoadAssetAtPath<BodyPartData>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(p => p != null).ToList();

        string generatedRoot = DefaultExtractFolder + "/";
        int removed = 0;

        foreach (var data in allData)
        {
            string prefabPath = data.prefab != null ? AssetDatabase.GetAssetPath(data.prefab) : null;
            bool isGenerated = !string.IsNullOrEmpty(prefabPath) &&
                               prefabPath.Replace('\\', '/').StartsWith(generatedRoot);
            if (!isGenerated) continue;

            foreach (var db in databases)
                if (db.allParts.Remove(data)) EditorUtility.SetDirty(db);

            if (data.icon != null)
            {
                string iconPath = AssetDatabase.GetAssetPath(data.icon);
                if (!string.IsNullOrEmpty(iconPath)) AssetDatabase.DeleteAsset(iconPath);
            }

            AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(data));
            removed++;
        }

        // Remove the extracted part prefabs wholesale (source FBX/models are untouched)
        if (AssetDatabase.IsValidFolder(DefaultExtractFolder))
            AssetDatabase.DeleteAsset(DefaultExtractFolder);

        // Tidy any now-empty slots the removals left behind
        foreach (var db in databases)
        {
            int before = db.allParts.Count;
            db.allParts.RemoveAll(p => p == null);
            if (db.allParts.Count != before) EditorUtility.SetDirty(db);
        }

        AssetDatabase.SaveAssets();
        string msg = $"Cleared {removed} generated part(s). Re-import your models to regenerate them.";
        Debug.Log(msg);
        EditorUtility.DisplayDialog("Clear Generated Parts", msg, "OK");
    }
}
