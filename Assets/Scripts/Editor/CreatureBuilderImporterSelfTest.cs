using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Automated self-test for CreatureBuilderImporter, runnable headless:
///
///   Unity.exe -batchmode -quit -projectPath ...
///     -executeMethod CreatureBuilderImporterSelfTest.RunBatchTest
///
/// Builds a throwaway folder with test prefabs and a temp database, runs the
/// importer twice (checking idempotency), verifies every field the app relies
/// on, deletes all test artifacts, then re-validates the real project to
/// prove nothing leaked. Exits 0 on success, 1 on any failure.
/// </summary>
public static class CreatureBuilderImporterSelfTest
{
    private const string TestRoot = "Assets/_ImporterSelfTest";

    public static void RunBatchTest()
    {
        var failures = new List<string>();
        try
        {
            RunAllChecks(failures);
        }
        catch (System.Exception e)
        {
            failures.Add("Unhandled exception: " + e);
        }
        finally
        {
            // Always clean up, even when asserts failed
            if (AssetDatabase.IsValidFolder(TestRoot))
                AssetDatabase.DeleteAsset(TestRoot);
            AssetDatabase.SaveAssets();
        }

        // The real project must be untouched by the test
        var postReport = CreatureBuilderValidator.Validate();
        if (postReport.errors.Count > 0)
            failures.Add("Project validation after test found errors:\n" + postReport.summary);

        int realParts = AssetDatabase.FindAssets("t:BodyPartData").Length;
        if (realParts != 4)
            failures.Add($"Expected 4 real BodyPartData assets after cleanup, found {realParts} — test artifacts may have leaked.");

        if (failures.Count == 0)
        {
            Debug.Log("IMPORTER SELF-TEST PASSED — all checks green, no artifacts leaked.");
            EditorApplication.Exit(0);
        }
        else
        {
            Debug.LogError("IMPORTER SELF-TEST FAILED:\n  • " + string.Join("\n  • ", failures));
            EditorApplication.Exit(1);
        }
    }

    private static void RunAllChecks(List<string> failures)
    {
        // ---- Arrange: build test content ----
        AssetDatabase.CreateFolder("Assets", "_ImporterSelfTest");
        AssetDatabase.CreateFolder(TestRoot, "Heads");
        AssetDatabase.CreateFolder(TestRoot, "Junk");     // invalid category name
        AssetDatabase.CreateFolder(TestRoot, "Output");
        AssetDatabase.CreateFolder(TestRoot, "Icons");

        CreateTestPrefab($"{TestRoot}/Heads/SelfTest_CubeHead.prefab");
        CreateTestPrefab($"{TestRoot}/Junk/SelfTest_Orphan.prefab");

        var testDb = ScriptableObject.CreateInstance<BodyPartDatabase>();
        AssetDatabase.CreateAsset(testDb, $"{TestRoot}/TestDatabase.asset");

        // ---- Act: first import ----
        var report = CreatureBuilderImporter.ImportFolder(
            TestRoot, testDb, $"{TestRoot}/Output", $"{TestRoot}/Icons");
        Debug.Log("First import:\n" + report.summary);

        // ---- Assert: created exactly the valid one ----
        if (report.created != 1)
            failures.Add($"Expected 1 part created, got {report.created}.");
        if (!report.warnings.Any(w => w.Contains("Junk")))
            failures.Add("Expected a warning about the 'Junk' folder prefab being skipped.");

        BodyPartData imported = testDb.allParts.FirstOrDefault();
        if (imported == null)
        {
            failures.Add("Imported part was not added to the database.");
        }
        else
        {
            if (string.IsNullOrEmpty(imported.PartID))
                failures.Add("Imported part has an empty partID — save/load would break.");
            if (imported.category != BodyPartCategory.Head)
                failures.Add($"Expected category Head, got {imported.category}.");
            if (imported.prefab == null)
                failures.Add("Imported part has no prefab reference.");
            if (imported.partName != "Self Test Cube Head")
                failures.Add($"Expected nicified name 'Self Test Cube Head', got '{imported.partName}'.");
            if (imported.icon == null)
                Debug.LogWarning("Icon was not generated in batch mode — non-fatal, but icons should be spot-checked in the editor.");
            else
                Debug.Log($"Icon generated: {AssetDatabase.GetAssetPath(imported.icon)} ({imported.icon.texture.width}x{imported.icon.texture.height})");
        }

        // ---- Act: second import (idempotency) ----
        var second = CreatureBuilderImporter.ImportFolder(
            TestRoot, testDb, $"{TestRoot}/Output", $"{TestRoot}/Icons");
        if (second.created != 0)
            failures.Add($"Second import should create 0 parts (idempotency), created {second.created}.");
        if (second.skippedExisting != 1)
            failures.Add($"Second import should skip 1 existing part, skipped {second.skippedExisting}.");
    }

    private static void CreateTestPrefab(string path)
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        PrefabUtility.SaveAsPrefabAsset(cube, path);
        Object.DestroyImmediate(cube);
    }
}
