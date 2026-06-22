using UnityEngine;
using System.Collections.Generic;
using System.Linq;

[CreateAssetMenu(fileName = "BodyPartDatabase", menuName = "Creature Viewer/Body Part Database")]
public class BodyPartDatabase : ScriptableObject
{
    public List<BodyPartData> allParts = new List<BodyPartData>();

    public List<BodyPartData> GetPartsInCategory(BodyPartCategory category)
    {
        return allParts.Where(p => p != null && p.category == category).ToList();
    }

    public List<BodyPartCategory> GetAvailableCategories()
    {
        return allParts
            .Where(p => p != null)
            .Select(p => p.category)
            .Distinct()
            .OrderBy(c => c)
            .ToList();
    }

    public BodyPartData GetPartByID(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        return allParts.FirstOrDefault(p => p != null && p.PartID == id);
    }
}