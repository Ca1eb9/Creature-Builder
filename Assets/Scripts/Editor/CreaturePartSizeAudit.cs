using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Diagnostic: reports how consistent each category's parts end up AFTER
/// auto-fit, under the current metric vs. a pure-diagonal metric. Prints the
/// "apparent size" (final bounding-box diagonal) per part plus each category's
/// spread (max/min ratio — 1.0 = perfectly uniform). Read-only; changes nothing.
///
///   Unity.exe -batchmode -quit -projectPath ...
///     -executeMethod CreaturePartSizeAudit.Run
/// </summary>
public static class CreaturePartSizeAudit
{
    public static void Run()
    {
        var db = AssetDatabase.FindAssets("t:BodyPartDatabase")
            .Select(g => AssetDatabase.LoadAssetAtPath<BodyPartDatabase>(AssetDatabase.GUIDToAssetPath(g)))
            .FirstOrDefault(d => d != null);
        if (db == null) { Debug.LogError("No BodyPartDatabase found."); EditorApplication.Exit(1); return; }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("PART SIZE AUDIT (apparent size = final bounds diagonal)");

        foreach (BodyPartCategory cat in System.Enum.GetValues(typeof(BodyPartCategory)))
        {
            var parts = db.allParts.Where(p => p != null && p.category == cat && p.prefab != null).ToList();
            if (parts.Count == 0) continue;

            float budget = CategoryBudgets.GetBudget(cat);
            var currentAxis = CategoryBudgets.GetFitAxis(cat);

            var current = new List<(string, float)>();
            var diagonal = new List<(string, float)>();
            foreach (var p in parts)
            {
                current.Add((p.partName, MeasureApparent(p.prefab, budget, currentAxis)));
                diagonal.Add((p.partName, MeasureApparent(p.prefab, budget, PartScaleNormalizer.FitAxis.Diagonal)));
            }

            sb.AppendLine($"\n=== {cat} (budget {budget}, current axis {currentAxis}) ===");
            sb.AppendLine($"  CURRENT metric  spread {Spread(current):F2}x   " + Format(current));
            sb.AppendLine($"  DIAGONAL metric spread {Spread(diagonal):F2}x   " + Format(diagonal));
        }

        Debug.Log(sb.ToString());
        EditorApplication.Exit(0);
    }

    private static float MeasureApparent(GameObject prefab, float budget, PartScaleNormalizer.FitAxis axis)
    {
        GameObject inst = Object.Instantiate(prefab);
        inst.transform.position = Vector3.zero;
        inst.transform.rotation = Quaternion.identity;
        inst.transform.localScale = Vector3.one;
        try
        {
            PartScaleNormalizer.NormalizeToSize(inst, budget, axis);
            return PartScaleNormalizer.GetCompositeBounds(inst).size.magnitude;
        }
        finally { Object.DestroyImmediate(inst); }
    }

    private static float Spread(List<(string name, float v)> xs)
    {
        float min = xs.Min(x => x.v), max = xs.Max(x => x.v);
        return min > 0.0001f ? max / min : 0f;
    }

    private static string Format(List<(string name, float v)> xs)
        => string.Join(", ", xs.OrderByDescending(x => x.v).Select(x => $"{x.name}={x.v:F2}"));
}
