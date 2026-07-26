using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;

/// <summary>
/// Makes the imported animal part materials double-sided (backface culling off).
///
/// Why: the parts were cut apart in Blender, which leaves open holes at the
/// seams. With backface culling on, a hole exposes the mesh interior — whose
/// back-facing polys are culled — so you see straight through. Rendering both
/// sides draws that inner wall instead, and since it uses the SAME material as
/// the surrounding surface, the hole reads as filled in a sensible color for
/// free — no per-part Blender work.
///
/// The FBX materials are embedded (read-only), so we first extract them to
/// editable .mat files (one folder per model), reimport so the models use them,
/// then set Cull = Off on each. Re-running is safe: already-extracted models are
/// skipped and culling is simply re-asserted.
///
///   Tools > Creature Builder > Make Part Materials Double-Sided
/// </summary>
public static class CreatureBuilderMaterialTool
{
    private const string DefaultModelsFolder = "Assets/Models";
    private const string DefaultMatFolder = "Assets/Materials/Extracted";
    private const string DefaultGeneratedFolder = "Assets/Prefabs/BodyParts/Generated";

    [MenuItem("Tools/Creature Builder/Make Part Materials Double-Sided")]
    public static void MakeDoubleSidedMenu()
    {
        Report r = MakeDoubleSided();
        Debug.Log(r.summary);
        EditorUtility.DisplayDialog("Make Materials Double-Sided", r.summary, "OK");
    }

    public class Report
    {
        public int extracted, culled, skippedNoCull, prefabsRepaired;
        public List<string> warnings = new List<string>();
        public string summary;
    }

    public static Report MakeDoubleSided(string modelsFolder = DefaultModelsFolder,
                                         string matFolder = DefaultMatFolder)
    {
        var report = new Report();

        if (!AssetDatabase.IsValidFolder(modelsFolder))
        {
            report.summary = $"No models folder at '{modelsFolder}'.";
            return report;
        }

        // ---- 1. Extract still-embedded materials, per model ----
        string[] modelGuids = AssetDatabase.FindAssets("t:Model", new[] { modelsFolder });
        var toReimport = new List<string>();

        try
        {
            AssetDatabase.StartAssetEditing();
            foreach (string guid in modelGuids)
            {
                string modelPath = AssetDatabase.GUIDToAssetPath(guid);
                string modelName = Path.GetFileNameWithoutExtension(modelPath);

                foreach (Object rep in AssetDatabase.LoadAllAssetRepresentationsAtPath(modelPath))
                {
                    if (rep is not Material mat) continue;
                    // Only embedded materials live at the FBX path; external ones don't.
                    if (AssetDatabase.GetAssetPath(mat) != modelPath) continue;

                    string dir = $"{matFolder}/{modelName}";
                    EnsureFolder(dir);
                    string newPath = AssetDatabase.GenerateUniqueAssetPath($"{dir}/{SanitizeName(mat.name)}.mat");

                    string err = AssetDatabase.ExtractAsset(mat, newPath);
                    if (string.IsNullOrEmpty(err))
                    {
                        toReimport.Add(modelPath);
                        report.extracted++;
                    }
                    else
                    {
                        report.warnings.Add($"{modelName}/{mat.name}: extract failed ({err}).");
                    }
                }
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }

        // Reimport the models we pulled materials out of, so their meshes bind
        // to the new external .mat files.
        foreach (string p in toReimport.Distinct())
        {
            AssetDatabase.WriteImportSettingsIfDirty(p);
            AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);
        }
        AssetDatabase.Refresh();

        // ---- 2. Turn off backface culling on every extracted material ----
        foreach (string matGuid in AssetDatabase.FindAssets("t:Material", new[] { matFolder }))
        {
            string p = AssetDatabase.GUIDToAssetPath(matGuid);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (mat == null) continue;

            if (!mat.HasProperty("_Cull"))
            {
                report.skippedNoCull++;
                report.warnings.Add($"{mat.name}: shader '{mat.shader.name}' has no _Cull property — left unchanged.");
                continue;
            }

            mat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            mat.doubleSidedGI = true;
            EditorUtility.SetDirty(mat);
            report.culled++;
        }

        AssetDatabase.SaveAssets();

        // ---- 3. Repair generated part prefabs ----
        // Those prefabs were saved BEFORE extraction and hard-reference the old
        // embedded material sub-assets, which no longer exist (→ hot pink). Point
        // each renderer back at the model's now-external material(s).
        report.prefabsRepaired = RepairGeneratedPrefabMaterials(DefaultGeneratedFolder, report);
        AssetDatabase.SaveAssets();

        var lines = new List<string>
        {
            $"Double-sided pass complete: extracted {report.extracted} embedded material(s), " +
            $"set {report.culled} material(s) to render both sides, " +
            $"repaired {report.prefabsRepaired} part prefab(s)."
        };
        if (report.skippedNoCull > 0)
            lines.Add($"{report.skippedNoCull} material(s) had no _Cull property and were skipped.");
        if (report.warnings.Count > 0)
        {
            lines.Add($"{report.warnings.Count} warning(s):");
            lines.AddRange(report.warnings.Take(20).Select(w => "  • " + w));
        }
        report.summary = string.Join("\n", lines);
        return report;
    }

    /// <summary>
    /// Reassigns each generated part prefab's renderer materials from its SOURCE
    /// model, matched by mesh identity. The source FBX model prefab already has
    /// the correct external material bound to each mesh (per its remap), and the
    /// generated part reuses those exact same Mesh assets — so copying the source
    /// renderer's materials per mesh restores the right colour on every part,
    /// including multi-material models. Idempotent.
    /// </summary>
    private static int RepairGeneratedPrefabMaterials(string generatedFolder, Report report)
    {
        if (!AssetDatabase.IsValidFolder(generatedFolder)) return 0;

        Dictionary<Mesh, Material[]> meshToMats = BuildMeshMaterialMap(DefaultModelsFolder);

        int repaired = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { generatedFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            bool changed = false;
            try
            {
                foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                {
                    Mesh mesh = MeshOf(r);
                    if (mesh == null) continue;
                    if (!meshToMats.TryGetValue(mesh, out Material[] correct)) continue;

                    r.sharedMaterials = (Material[])correct.Clone();
                    changed = true;
                }

                if (changed)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    repaired++;
                }
                else
                {
                    report.warnings.Add($"{Path.GetFileName(path)}: no source mesh matched — material not repaired.");
                }
            }
            catch (System.Exception e)
            {
                report.warnings.Add($"{path}: prefab material repair failed ({e.Message}).");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
        return repaired;
    }

    /// <summary>
    /// Maps every mesh in the source models to the material array bound to it on
    /// the model prefab (post-remap, i.e. the external double-sided materials).
    /// </summary>
    private static Dictionary<Mesh, Material[]> BuildMeshMaterialMap(string modelsFolder)
    {
        var map = new Dictionary<Mesh, Material[]>();
        if (!AssetDatabase.IsValidFolder(modelsFolder)) return map;

        foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { modelsFolder }))
        {
            string fbxPath = AssetDatabase.GUIDToAssetPath(guid);
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (model == null) continue;

            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
            {
                Mesh mesh = MeshOf(r);
                if (mesh != null && !map.ContainsKey(mesh))
                    map[mesh] = r.sharedMaterials;
            }
        }
        return map;
    }

    private static Mesh MeshOf(Renderer r)
    {
        if (r is SkinnedMeshRenderer smr) return smr.sharedMesh;
        var mf = r.GetComponent<MeshFilter>();
        return mf != null ? mf.sharedMesh : null;
    }

    private static void EnsureFolder(string assetFolder)
    {
        if (AssetDatabase.IsValidFolder(assetFolder)) return;
        Directory.CreateDirectory(assetFolder);
        AssetDatabase.Refresh();
    }

    private static string SanitizeName(string raw)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(raw.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return string.IsNullOrEmpty(cleaned) ? "Material" : cleaned;
    }

    /// <summary>
    /// Batch entry:
    ///   Unity.exe -batchmode -quit -projectPath ...
    ///     -executeMethod CreatureBuilderMaterialTool.MakeDoubleSidedBatch
    /// </summary>
    public static void MakeDoubleSidedBatch()
    {
        Report r = MakeDoubleSided();
        Debug.Log(r.summary);
        EditorApplication.Exit(0);
    }
}
