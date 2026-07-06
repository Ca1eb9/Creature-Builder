using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Editor-only integrity checks for the Creature Builder part pipeline.
/// Run via Tools > Creature Builder before every ship build, and any time
/// parts are added. Catches the mistakes that silently break save/load:
/// missing or duplicated partIDs (Ctrl+D on an asset copies its ID!),
/// missing prefabs, and parts that never got added to the database.
/// </summary>
public static class CreatureBuilderValidator
{
    [MenuItem("Tools/Creature Builder/Validate Parts")]
    public static void ValidateMenu()
    {
        var report = Validate();
        Debug.Log(report.summary);

        if (!Application.isBatchMode)
        {
            EditorUtility.DisplayDialog(
                "Creature Builder Validation",
                report.summary,
                "OK");
        }
    }

    [MenuItem("Tools/Creature Builder/Fix Missing Part IDs")]
    public static void FixMissingIDs()
    {
        int fixedCount = 0;
        foreach (var part in LoadAllParts())
        {
            var so = new SerializedObject(part);
            var idProp = so.FindProperty("partID");
            if (string.IsNullOrEmpty(idProp.stringValue))
            {
                idProp.stringValue = System.Guid.NewGuid().ToString();
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(part);
                fixedCount++;
                Debug.Log($"Assigned new partID to '{part.name}'");
            }
        }

        if (fixedCount > 0) AssetDatabase.SaveAssets();
        Debug.Log($"Fix Missing Part IDs: {fixedCount} asset(s) updated.");

        if (!Application.isBatchMode)
        {
            EditorUtility.DisplayDialog(
                "Creature Builder",
                fixedCount > 0
                    ? $"Assigned new IDs to {fixedCount} part(s)."
                    : "All parts already have IDs — nothing to fix.",
                "OK");
        }
    }

    /// <summary>
    /// Batch-mode entry point:
    ///   Unity.exe -batchmode -quit -projectPath ... -executeMethod CreatureBuilderValidator.ValidateBatch
    /// Exits 0 when clean, 1 when any error is found (warnings don't fail the run).
    /// </summary>
    public static void ValidateBatch()
    {
        var report = Validate();
        Debug.Log(report.summary);
        EditorApplication.Exit(report.errors.Count == 0 ? 0 : 1);
    }

    // ------------------------------------------------------------------

    public class Report
    {
        public List<string> errors = new List<string>();
        public List<string> warnings = new List<string>();
        public string summary;
    }

    public static Report Validate()
    {
        var report = new Report();
        List<BodyPartData> parts = LoadAllParts();
        List<BodyPartDatabase> databases = LoadAllDatabases();

        // --- Part-level checks ---
        var seenIDs = new Dictionary<string, string>(); // partID -> first part name
        foreach (var part in parts)
        {
            if (string.IsNullOrEmpty(part.PartID))
            {
                report.errors.Add($"'{part.name}': partID is empty. Save/load cannot reference this part. Run Tools > Creature Builder > Fix Missing Part IDs.");
            }
            else if (seenIDs.TryGetValue(part.PartID, out string firstOwner))
            {
                report.errors.Add($"'{part.name}' has the SAME partID as '{firstOwner}' (likely a duplicated asset). Loading a save will equip the wrong part. Clear one ID and run Fix Missing Part IDs.");
            }
            else
            {
                seenIDs[part.PartID] = part.name;
            }

            if (part.prefab == null)
            {
                report.errors.Add($"'{part.name}': prefab is not assigned. Equipping it will silently do nothing.");
            }
            else if (part.prefab.GetComponentsInChildren<Renderer>(true).Length == 0)
            {
                report.warnings.Add($"'{part.name}': prefab '{part.prefab.name}' has no Renderer — it will be invisible and break auto-scaling bounds.");
            }

            if (part.icon == null)
            {
                report.warnings.Add($"'{part.name}': no icon assigned (part button will be text-only).");
            }
        }

        // --- Database-level checks ---
        if (databases.Count == 0)
        {
            report.errors.Add("No BodyPartDatabase asset found in the project.");
        }
        else
        {
            if (databases.Count > 1)
            {
                report.warnings.Add($"Multiple BodyPartDatabase assets found ({string.Join(", ", databases.Select(d => d.name))}). Make sure the scene references the right one.");
            }

            foreach (var db in databases)
            {
                int nullEntries = db.allParts.Count(p => p == null);
                if (nullEntries > 0)
                    report.warnings.Add($"Database '{db.name}': {nullEntries} empty (None) entr{(nullEntries == 1 ? "y" : "ies")} in allParts.");

                var duplicatesInDb = db.allParts
                    .Where(p => p != null)
                    .GroupBy(p => p)
                    .Where(g => g.Count() > 1);
                foreach (var group in duplicatesInDb)
                    report.warnings.Add($"Database '{db.name}': part '{group.Key.name}' is listed {group.Count()} times.");
            }

            // Parts that exist on disk but are in no database are invisible in the app
            var allDbParts = new HashSet<BodyPartData>(databases.SelectMany(d => d.allParts).Where(p => p != null));
            foreach (var part in parts)
            {
                if (!allDbParts.Contains(part))
                    report.errors.Add($"'{part.name}' is not in any BodyPartDatabase — it will never appear in the app.");
            }
        }

        // --- Summary ---
        var lines = new List<string>
        {
            $"Creature Builder validation: {parts.Count} part(s), {databases.Count} database(s) checked.",
            $"{report.errors.Count} error(s), {report.warnings.Count} warning(s)."
        };
        if (report.errors.Count > 0)
        {
            lines.Add("");
            lines.Add("ERRORS:");
            lines.AddRange(report.errors.Select(e => "  • " + e));
        }
        if (report.warnings.Count > 0)
        {
            lines.Add("");
            lines.Add("Warnings:");
            lines.AddRange(report.warnings.Select(w => "  • " + w));
        }
        if (report.errors.Count == 0 && report.warnings.Count == 0)
        {
            lines.Add("All clean — parts are ship-ready.");
        }
        report.summary = string.Join("\n", lines);
        return report;
    }

    private static List<BodyPartData> LoadAllParts()
    {
        return AssetDatabase.FindAssets("t:BodyPartData")
            .Select(guid => AssetDatabase.LoadAssetAtPath<BodyPartData>(AssetDatabase.GUIDToAssetPath(guid)))
            .Where(p => p != null)
            .ToList();
    }

    private static List<BodyPartDatabase> LoadAllDatabases()
    {
        return AssetDatabase.FindAssets("t:BodyPartDatabase")
            .Select(guid => AssetDatabase.LoadAssetAtPath<BodyPartDatabase>(AssetDatabase.GUIDToAssetPath(guid)))
            .Where(d => d != null)
            .ToList();
    }
}
