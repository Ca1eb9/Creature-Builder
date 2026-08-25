using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Removing a part properly means more than deleting its asset: the database
/// keeps a slot in allParts (which then shows up as an empty "None" entry and
/// inflates the part count), and its rendered icon is left orphaned on disk.
/// These two commands handle the whole job.
///
///   Tools > Creature Builder > Delete Selected Parts
///   Tools > Creature Builder > Clean Up Database
/// </summary>
public static class CreatureBuilderCleanupTool
{
    // ------------------------------------------------------------------
    //  DELETE SELECTED
    // ------------------------------------------------------------------

    [MenuItem("Tools/Creature Builder/Delete Selected Parts")]
    public static void DeleteSelectedMenu()
    {
        var parts = Selection.objects.OfType<BodyPartData>().ToList();
        if (parts.Count == 0)
        {
            EditorUtility.DisplayDialog("Delete Selected Parts",
                "Select one or more BodyPartData assets in the Project window first.\n\n" +
                "(The test parts live in Assets/ScriptableObjects/Parts/.)", "OK");
            return;
        }

        string names = string.Join("\n  • ", parts.Select(p => p.partName));
        if (!EditorUtility.DisplayDialog("Delete Selected Parts",
            $"Delete {parts.Count} part(s) — their BodyPartData, icon, and database entry?\n\n  • {names}\n\n" +
            "Their prefabs and models are left alone. Any saved creature using them will " +
            "load with that socket empty. This cannot be undone.",
            "Delete", "Cancel"))
            return;

        var databases = LoadDatabases();
        int deleted = 0, icons = 0;

        foreach (var part in parts)
        {
            // Unregister first, so nothing holds a reference while we delete.
            foreach (var db in databases)
                if (db.allParts.Remove(part)) EditorUtility.SetDirty(db);

            if (part.icon != null)
            {
                string iconPath = AssetDatabase.GetAssetPath(part.icon);
                if (!string.IsNullOrEmpty(iconPath) && AssetDatabase.DeleteAsset(iconPath)) icons++;
            }

            string assetPath = AssetDatabase.GetAssetPath(part);
            if (!string.IsNullOrEmpty(assetPath) && AssetDatabase.DeleteAsset(assetPath)) deleted++;
        }

        int compacted = CompactDatabases(databases);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("Delete Selected Parts",
            $"Deleted {deleted} part(s) and {icons} icon(s).\n" +
            $"Removed {compacted} leftover empty database entr{(compacted == 1 ? "y" : "ies")}.", "OK");
    }

    // ------------------------------------------------------------------
    //  CLEAN UP DATABASE
    // ------------------------------------------------------------------

    [MenuItem("Tools/Creature Builder/Clean Up Database")]
    public static void CleanUpMenu()
    {
        var databases = LoadDatabases();
        int compacted = CompactDatabases(databases);

        // Parts whose prefab has gone missing can never be equipped — flag them
        // rather than deleting, since that's usually a broken import to fix.
        var broken = AssetDatabase.FindAssets("t:BodyPartData")
            .Select(g => AssetDatabase.LoadAssetAtPath<BodyPartData>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(p => p != null && p.prefab == null)
            .ToList();

        AssetDatabase.SaveAssets();

        string msg = $"Removed {compacted} empty entr{(compacted == 1 ? "y" : "ies")} from the database.";
        if (broken.Count > 0)
            msg += $"\n\n{broken.Count} part(s) have no prefab and will be invisible in game:\n  • "
                 + string.Join("\n  • ", broken.Select(b => b.partName));
        EditorUtility.DisplayDialog("Clean Up Database", msg, "OK");
    }

    // ------------------------------------------------------------------
    //  REIMPORT ONE MODEL
    // ------------------------------------------------------------------

    /// <summary>
    /// The importer skips a model whose extracted prefabs already exist, so a
    /// repaired FBX is ignored on a plain re-run. This clears just that model's
    /// generated artefacts and imports it again — every other animal is left
    /// alone (no need for the all-or-nothing Clear Generated Parts).
    ///
    /// Only needed when the repair moved an origin, rotation or scale, or
    /// renamed/added objects: a geometry-only fix flows through automatically,
    /// because the generated prefabs reference the FBX's meshes.
    /// </summary>
    [MenuItem("Tools/Creature Builder/Reimport Selected Model")]
    public static void ReimportSelectedModelMenu()
    {
        var models = Selection.objects
            .Select(o => AssetDatabase.GetAssetPath(o))
            .Where(path => !string.IsNullOrEmpty(path) &&
                           AssetImporter.GetAtPath(path) is ModelImporter)
            .Select(System.IO.Path.GetFileNameWithoutExtension)
            .Distinct()
            .ToList();

        if (models.Count == 0)
        {
            EditorUtility.DisplayDialog("Reimport Selected Model",
                "Select one or more model files (e.g. Assets/Models/Wasp.fbx) in the Project window first.", "OK");
            return;
        }

        if (!EditorUtility.DisplayDialog("Reimport Selected Model",
            $"Re-extract {string.Join(", ", models)}?\n\n" +
            "Their generated part prefabs, BodyPartData and icons are deleted and rebuilt " +
            "from the current FBX. Other animals are untouched.\n\n" +
            "Part IDs and your calibration (offset / rotation / scale) are preserved, so " +
            "existing saved creatures keep working — as long as the object names and " +
            "categories inside the FBX have not changed.",
            "Reimport", "Cancel"))
            return;

        var databases = LoadDatabases();
        if (databases.Count == 0)
        {
            EditorUtility.DisplayDialog("Reimport Selected Model", "No BodyPartDatabase found.", "OK");
            return;
        }

        // Identity + calibration are carried across the delete/re-create.
        var preserved = new Dictionary<string, PreservedPart>();

        int removed = 0;
        foreach (string model in models) removed += RemoveGeneratedFor(model, databases, preserved);
        CompactDatabases(databases);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        var report = CreatureBuilderImporter.ImportMultiPartFolder("Assets/Models", databases[0]);
        int restored = RestorePreserved(preserved);
        AssetDatabase.SaveAssets();

        EditorUtility.DisplayDialog("Reimport Selected Model",
            $"Removed {removed} old part(s), kept the ID and calibration of {restored}.\n\n{report.summary}", "OK");
    }

    /// <summary>
    /// The bits of a BodyPartData that must outlive a re-import. partID is the
    /// critical one: saved creatures reference parts by it, so a fresh GUID
    /// would orphan every creature already built with that part.
    /// </summary>
    private class PreservedPart
    {
        public string partID;
        public string partName;
        public string description;
        public Vector3 positionOffset;
        public Vector3 rotationOffset;
        public float scaleMultiplier;
        public bool useAutoScale;
    }

    /// <summary>Write the snapshotted identity/calibration back onto the rebuilt parts.</summary>
    private static int RestorePreserved(Dictionary<string, PreservedPart> preserved)
    {
        if (preserved == null || preserved.Count == 0) return 0;
        int restored = 0;

        foreach (string guid in AssetDatabase.FindAssets("t:BodyPartData"))
        {
            var data = AssetDatabase.LoadAssetAtPath<BodyPartData>(AssetDatabase.GUIDToAssetPath(guid));
            if (data == null || data.prefab == null) continue;
            if (!preserved.TryGetValue(AssetDatabase.GetAssetPath(data.prefab), out var old)) continue;

            data.partName = old.partName;
            data.description = old.description;
            data.positionOffset = old.positionOffset;
            data.rotationOffset = old.rotationOffset;
            data.scaleMultiplier = old.scaleMultiplier;
            data.useAutoScale = old.useAutoScale;

            // partID is private-serialized, so it needs SerializedObject.
            var so = new SerializedObject(data);
            so.FindProperty("partID").stringValue = old.partID;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(data);
            restored++;
        }
        return restored;
    }

    /// <summary>Delete every generated artefact belonging to one source model.</summary>
    private static int RemoveGeneratedFor(string modelName, List<BodyPartDatabase> databases,
                                          Dictionary<string, PreservedPart> preserved)
    {
        const string generatedRoot = "Assets/Prefabs/BodyParts/Generated";
        if (!AssetDatabase.IsValidFolder(generatedRoot)) return 0;

        // Extracted prefabs are named "<Model>_<Category>.prefab".
        string prefix = modelName + "_";
        var prefabPaths = AssetDatabase.FindAssets("t:Prefab", new[] { generatedRoot })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(path => System.IO.Path.GetFileNameWithoutExtension(path)
                                 .StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (prefabPaths.Count == 0) return 0;

        var targets = new HashSet<string>(prefabPaths);
        int removed = 0;

        // Drop the BodyPartData (and icon) pointing at each of those prefabs.
        foreach (string guid in AssetDatabase.FindAssets("t:BodyPartData"))
        {
            string dataPath = AssetDatabase.GUIDToAssetPath(guid);
            var data = AssetDatabase.LoadAssetAtPath<BodyPartData>(dataPath);
            if (data == null || data.prefab == null) continue;
            if (!targets.Contains(AssetDatabase.GetAssetPath(data.prefab))) continue;

            // Snapshot everything that must survive the round trip, keyed by the
            // prefab path (which is deterministic: <Model>_<Category>.prefab).
            if (preserved != null)
            {
                preserved[AssetDatabase.GetAssetPath(data.prefab)] = new PreservedPart
                {
                    partID = data.PartID,
                    partName = data.partName,
                    description = data.description,
                    positionOffset = data.positionOffset,
                    rotationOffset = data.rotationOffset,
                    scaleMultiplier = data.scaleMultiplier,
                    useAutoScale = data.useAutoScale
                };
            }

            foreach (var db in databases)
                if (db.allParts.Remove(data)) EditorUtility.SetDirty(db);

            if (data.icon != null)
            {
                string iconPath = AssetDatabase.GetAssetPath(data.icon);
                if (!string.IsNullOrEmpty(iconPath)) AssetDatabase.DeleteAsset(iconPath);
            }
            AssetDatabase.DeleteAsset(dataPath);
            removed++;
        }

        // Finally the prefabs themselves — this is what the importer checks, so
        // they must go or the model gets skipped again.
        foreach (string path in prefabPaths) AssetDatabase.DeleteAsset(path);
        return removed;
    }

    // ------------------------------------------------------------------

    private static List<BodyPartDatabase> LoadDatabases() =>
        AssetDatabase.FindAssets("t:BodyPartDatabase")
            .Select(g => AssetDatabase.LoadAssetAtPath<BodyPartDatabase>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(d => d != null)
            .ToList();

    /// <summary>Drop the empty slots a deleted asset leaves behind in allParts.</summary>
    private static int CompactDatabases(List<BodyPartDatabase> databases)
    {
        int removed = 0;
        foreach (var db in databases)
        {
            int before = db.allParts.Count;
            db.allParts.RemoveAll(p => p == null);
            if (db.allParts.Count != before)
            {
                removed += before - db.allParts.Count;
                EditorUtility.SetDirty(db);
            }
        }
        return removed;
    }
}
