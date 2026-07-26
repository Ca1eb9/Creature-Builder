using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Automated self-test for CreatureBuilderImporter, runnable headless:
///
///   Unity.exe -batchmode -quit -projectPath ...
///     -executeMethod CreatureBuilderImporterSelfTest.RunMultiPartBatchTest
///
/// Builds a throwaway multi-part model + temp database, runs the importer twice
/// (checking idempotency and the extracted-part transforms), deletes all test
/// artifacts, then re-validates the real project to prove nothing leaked.
/// Exits 0 on success, 1 on any failure.
/// </summary>
public static class CreatureBuilderImporterSelfTest
{
    private const string TestRoot = "Assets/_ImporterSelfTest";

    // ==================================================================
    //  MULTI-PART (one FBX per animal) SELF-TEST
    //
    //    Unity.exe -batchmode -quit -projectPath ...
    //      -executeMethod CreatureBuilderImporterSelfTest.RunMultiPartBatchTest
    // ==================================================================

    public static void RunMultiPartBatchTest()
    {
        var failures = new List<string>();
        try
        {
            RunMultiPartChecks(failures);
        }
        catch (System.Exception e)
        {
            failures.Add("Unhandled exception: " + e);
        }
        finally
        {
            if (AssetDatabase.IsValidFolder(TestRoot))
                AssetDatabase.DeleteAsset(TestRoot);
            AssetDatabase.SaveAssets();
        }

        var postReport = CreatureBuilderValidator.Validate();
        if (postReport.errors.Count > 0)
            failures.Add("Project validation after test found errors:\n" + postReport.summary);

        int realParts = AssetDatabase.FindAssets("t:BodyPartData").Length;
        if (realParts != 4)
            failures.Add($"Expected 4 real BodyPartData assets after cleanup, found {realParts} — test artifacts may have leaked.");

        if (failures.Count == 0)
        {
            Debug.Log("MULTI-PART SELF-TEST PASSED — all checks green, no artifacts leaked.");
            EditorApplication.Exit(0);
        }
        else
        {
            Debug.LogError("MULTI-PART SELF-TEST FAILED:\n  • " + string.Join("\n  • ", failures));
            EditorApplication.Exit(1);
        }
    }

    private static void RunMultiPartChecks(List<string> failures)
    {
        // ---- Arrange ----
        AssetDatabase.CreateFolder("Assets", "_ImporterSelfTest");
        AssetDatabase.CreateFolder(TestRoot, "Models");
        AssetDatabase.CreateFolder(TestRoot, "Output");
        AssetDatabase.CreateFolder(TestRoot, "Icons");
        AssetDatabase.CreateFolder(TestRoot, "Extracted");

        // A "Wolf" model: Head tilted 30° on X, lowercase "torso" (case-insensitive),
        // and a "Junk" object that must be ignored.
        string modelPath = $"{TestRoot}/Models/Wolf.prefab";
        CreateMultiPartModelPrefab(modelPath);

        var testDb = ScriptableObject.CreateInstance<BodyPartDatabase>();
        AssetDatabase.CreateAsset(testDb, $"{TestRoot}/TestDatabase.asset");

        // ---- Act: first import ----
        var report = CreatureBuilderImporter.ImportMultiPartFolder(
            $"{TestRoot}/Models", testDb,
            $"{TestRoot}/Output", $"{TestRoot}/Icons", $"{TestRoot}/Extracted");
        Debug.Log("First multi-part import:\n" + report.summary);

        // ---- Assert: exactly Head + Torso created, Junk ignored ----
        if (report.created != 2)
            failures.Add($"Expected 2 parts created (Head, Torso), got {report.created}.");

        var byCat = testDb.allParts.Where(p => p != null).ToDictionary(p => p.category, p => p);
        if (!byCat.ContainsKey(BodyPartCategory.Head))
            failures.Add("Head part was not created from the multi-part FBX.");
        if (!byCat.ContainsKey(BodyPartCategory.Torso))
            failures.Add("Torso part (from lowercase 'torso' object) was not created — case-insensitive match failed.");
        if (byCat.ContainsKey(BodyPartCategory.Accessories))
            failures.Add("A part was created from the 'Junk' object — non-category children must be ignored.");

        if (byCat.TryGetValue(BodyPartCategory.Head, out var head))
        {
            if (string.IsNullOrEmpty(head.PartID))
                failures.Add("Extracted Head has an empty partID — save/load would break.");
            if (head.partName != "Wolf Head")
                failures.Add($"Expected Head name 'Wolf Head', got '{head.partName}'.");
            if (head.prefab == null)
                failures.Add("Extracted Head has no prefab reference.");
            else
                VerifyExtractedTransforms(head.prefab, failures);
        }

        // ---- Act: second import (idempotency) ----
        var second = CreatureBuilderImporter.ImportMultiPartFolder(
            $"{TestRoot}/Models", testDb,
            $"{TestRoot}/Output", $"{TestRoot}/Icons", $"{TestRoot}/Extracted");
        if (second.created != 0)
            failures.Add($"Second multi-part import should create 0 parts (idempotency), created {second.created}.");
        if (second.skippedExisting != 2)
            failures.Add($"Second multi-part import should skip 2 existing parts, skipped {second.skippedExisting}.");
    }

    /// <summary>
    /// The whole point of the extraction: the part prefab root sits at identity,
    /// and the mesh child's origin (the seam) is at (0,0,0) so it lands on the
    /// attach point, with the authored 30° tilt preserved (stays upright).
    /// </summary>
    private static void VerifyExtractedTransforms(GameObject partPrefab, List<string> failures)
    {
        Transform root = partPrefab.transform;
        if (Vector3.Distance(root.localPosition, Vector3.zero) > 0.001f ||
            Quaternion.Angle(root.localRotation, Quaternion.identity) > 0.5f ||
            Vector3.Distance(root.localScale, Vector3.one) > 0.001f)
            failures.Add($"Extracted part root is not at identity (pos {root.localPosition}, rot {root.localRotation.eulerAngles}, scale {root.localScale}) — offsets would be double-applied.");

        if (root.childCount != 1)
        {
            failures.Add($"Extracted part should have exactly one mesh child, has {root.childCount}.");
            return;
        }

        Transform mesh = root.GetChild(0);
        if (Vector3.Distance(mesh.localPosition, Vector3.zero) > 0.001f)
            failures.Add($"Mesh child origin (the seam) is not at (0,0,0) — it sits at {mesh.localPosition}; the part would float off its attach point.");

        // Head was authored with a 30° X tilt; it must be baked into the child.
        if (Quaternion.Angle(mesh.localRotation, Quaternion.Euler(30f, 0f, 0f)) > 1f)
            failures.Add($"Mesh child lost its authored orientation — expected ~30° X, got {mesh.localRotation.eulerAngles}.");
    }

    private static void CreateMultiPartModelPrefab(string path)
    {
        GameObject root = new GameObject("Wolf");
        try
        {
            MakeChildCube(root.transform, "Head", new Vector3(0f, 2f, 0f), Quaternion.Euler(30f, 0f, 0f));
            MakeChildCube(root.transform, "torso", Vector3.zero, Quaternion.identity);
            MakeChildCube(root.transform, "Junk", new Vector3(1f, 0f, 0f), Quaternion.identity);
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    private static void MakeChildCube(Transform parent, string name, Vector3 localPos, Quaternion localRot)
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = name;
        cube.transform.SetParent(parent, false);
        cube.transform.localPosition = localPos;
        cube.transform.localRotation = localRot;
    }
}
