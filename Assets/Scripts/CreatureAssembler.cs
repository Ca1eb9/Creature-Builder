using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class CreatureAssembler : MonoBehaviour
{
    [Header("Attachment Points")]
    public Transform headAttach;
    public Transform torsoAttach;
    public Transform frontLegsAttach;
    public Transform backLegsAttach;
    public Transform tailAttach;
    public Transform wingsAttach;
    public Transform hornsAttach;
    public Transform accessoriesAttach;

    [Header("Linked Systems")]
    public CameraFramer cameraFramer;

    [Header("Smart Scaling")]
    [Tooltip("When ON, non-torso parts inherit the torso's scale multiplier so proportions stay consistent.")]
    public bool linkPartsToTorsoScale = true;

    // Runtime state
    private Dictionary<BodyPartCategory, GameObject> equippedParts = new();
    private Dictionary<BodyPartCategory, BodyPartData> equippedData = new();
    private Dictionary<BodyPartCategory, PartOverride> overrides = new();
    // The scale a part has AFTER auto-fit + data.scaleMultiplier — before any
    // override or torso-link multiplier. This is the "natural baseline scale".
    private Dictionary<BodyPartCategory, Vector3> baselineScales = new();

    // Event: anything that wants to know when the creature changes
    public System.Action OnCreatureChanged;

    public Transform GetAttachPoint(BodyPartCategory category)
    {
        switch (category)
        {
            case BodyPartCategory.Head:        return headAttach;
            case BodyPartCategory.Torso:       return torsoAttach;
            case BodyPartCategory.FrontLegs:   return frontLegsAttach;
            case BodyPartCategory.BackLegs:    return backLegsAttach;
            case BodyPartCategory.Tail:        return tailAttach;
            case BodyPartCategory.Wings:       return wingsAttach;
            case BodyPartCategory.Horns:       return hornsAttach;
            case BodyPartCategory.Accessories: return accessoriesAttach;
            default: return null;
        }
    }

    // ----------------------------------------------------------------
    // EQUIP / REMOVE
    // ----------------------------------------------------------------

    public void EquipPart(BodyPartData partData, bool notify = true)
    {
        if (partData == null || partData.prefab == null) return;

        BodyPartCategory cat = partData.category;
        Transform attach = GetAttachPoint(cat);
        if (attach == null)
        {
            Debug.LogWarning($"No attach point for category {cat}");
            return;
        }

        // Preserve existing override for this category if there is one
        PartOverride existingOverride = overrides.ContainsKey(cat)
            ? overrides[cat].Clone()
            : new PartOverride();

        RemovePart(cat, notify: false);

        GameObject newPart = Instantiate(partData.prefab, attach);
        equippedParts[cat] = newPart;
        equippedData[cat] = partData;
        overrides[cat] = existingOverride;

        // Compute the baseline scale (auto-fit + per-part calibration)
        ComputeAndApplyBaseline(cat, newPart, partData);

        // Apply current override on top
        ApplyTransform(cat);

        // If we changed the TORSO, re-process every other equipped part
        // so they inherit the new torso's scale (if torso-link is on)
        if (cat == BodyPartCategory.Torso && linkPartsToTorsoScale)
        {
            foreach (var otherCat in new List<BodyPartCategory>(equippedParts.Keys))
            {
                if (otherCat == BodyPartCategory.Torso) continue;
                ApplyTransform(otherCat);
            }
        }

        if (notify)
        {
            cameraFramer?.FrameCreature();
            OnCreatureChanged?.Invoke();
        }
    }

    public void RemovePart(BodyPartCategory category, bool notify = true)
    {
        if (equippedParts.TryGetValue(category, out GameObject obj) && obj != null)
        {
            Destroy(obj);
        }
        equippedParts.Remove(category);
        equippedData.Remove(category);
        baselineScales.Remove(category);
        // Note: we deliberately keep `overrides` so the user's tweaks are
        // remembered if they re-equip the same category. Clear via ClearOverride.

        if (notify)
        {
            cameraFramer?.FrameCreature();
            OnCreatureChanged?.Invoke();
        }
    }

    public void ClearAll()
    {
        foreach (var cat in new List<BodyPartCategory>(equippedParts.Keys))
            RemovePart(cat, notify: false);
        overrides.Clear();
        cameraFramer?.FrameCreature();
        OnCreatureChanged?.Invoke();
    }

    public void Randomize(BodyPartDatabase database)
    {
        ClearAll();
        // IMPORTANT: equip torso FIRST so that torso-linked scaling
        // has something to base off of when other parts come in
        var categories = database.GetAvailableCategories()
            .OrderBy(c => c == BodyPartCategory.Torso ? 0 : 1)
            .ToList();

        foreach (var cat in categories)
        {
            var parts = database.GetPartsInCategory(cat);
            if (parts.Count > 0)
                EquipPart(parts[Random.Range(0, parts.Count)], notify: false);
        }

        NotifyCreatureChanged();
    }

    // ----------------------------------------------------------------
    // BASELINE SCALE CALCULATION
    // ----------------------------------------------------------------

    /// <summary>
    /// Runs auto-fit and applies the per-part scale calibration. Stores the result.
    /// </summary>
    private void ComputeAndApplyBaseline(BodyPartCategory cat, GameObject part, BodyPartData data)
    {
        if (data.useAutoScale)
        {
            // Reset scale before measuring so we get a consistent starting bounds
            part.transform.localScale = Vector3.one;

            // Auto-fit to category budget
            PartScaleNormalizer.NormalizeToSize(
                part,
                CategoryBudgets.GetBudget(cat),
                CategoryBudgets.GetFitAxis(cat)
            );
        }
        else
        {
            part.transform.localScale = Vector3.one;
        }

        // Apply the per-part calibration from the BodyPartData asset
        part.transform.localScale *= data.scaleMultiplier;

        // Store this scale as the baseline for future override calculations
        baselineScales[cat] = part.transform.localScale;
    }

    /// <summary>
    /// Returns the multiplier non-torso parts should apply to mirror the torso's scale changes.
    /// </summary>
    private float GetTorsoLinkMultiplier()
    {
        if (!linkPartsToTorsoScale) return 1f;
        if (!overrides.ContainsKey(BodyPartCategory.Torso)) return 1f;
        return overrides[BodyPartCategory.Torso].scaleMultiplier;
    }

    // ----------------------------------------------------------------
    // TRANSFORM APPLICATION
    // ----------------------------------------------------------------

    /// <summary>
    /// Reads the current baseline + override + torso link and applies the final transform.
    /// </summary>
    private void ApplyTransform(BodyPartCategory cat)
    {
        if (!equippedParts.ContainsKey(cat)) return;
        if (!equippedData.ContainsKey(cat)) return;

        GameObject part = equippedParts[cat];
        BodyPartData data = equippedData[cat];
        PartOverride ov = overrides.ContainsKey(cat) ? overrides[cat] : new PartOverride();
        Vector3 baseline = baselineScales.ContainsKey(cat) ? baselineScales[cat] : Vector3.one;

        // Position: baseline offset + user delta
        part.transform.localPosition = data.positionOffset + ov.positionDelta;

        // Rotation: baseline offset + user delta
        part.transform.localRotation = Quaternion.Euler(data.rotationOffset + ov.rotationDelta);

        // Scale: baseline × override multiplier × (optional torso link)
        float torsoMul = (cat == BodyPartCategory.Torso) ? 1f : GetTorsoLinkMultiplier();
        part.transform.localScale = baseline * ov.scaleMultiplier * torsoMul;
    }

    // ----------------------------------------------------------------
    // OVERRIDE API (used by sliders + save/load)
    // ----------------------------------------------------------------

    public PartOverride GetOverride(BodyPartCategory cat)
    {
        if (!overrides.ContainsKey(cat))
            overrides[cat] = new PartOverride();
        return overrides[cat];
    }

    public void SetOverride(BodyPartCategory cat, PartOverride newOverride, bool notify = true)
    {
        overrides[cat] = newOverride;
        RefreshOverride(cat, notify);
    }

    /// <summary>
    /// Re-applies the stored override for a category. Use when you've mutated
    /// the override in place (e.g. via the reference returned by GetOverride)
    /// and need the assembler to push the change to the part transform.
    /// </summary>
    public void RefreshOverride(BodyPartCategory cat, bool notify = true)
    {
        ApplyTransform(cat);

        // If we just changed the torso's scale and torso-link is on, propagate
        if (cat == BodyPartCategory.Torso && linkPartsToTorsoScale)
        {
            foreach (var other in new List<BodyPartCategory>(equippedParts.Keys))
            {
                if (other == BodyPartCategory.Torso) continue;
                ApplyTransform(other);
            }
        }

        if (notify) NotifyCreatureChanged();
    }

    public void ResetOverride(BodyPartCategory cat)
    {
        overrides[cat] = new PartOverride();
        RefreshOverride(cat);
    }

    // ----------------------------------------------------------------
    // NOTIFICATIONS
    // ----------------------------------------------------------------

    /// <summary>
    /// Fires the creature-changed signals manually. Use after a batch of
    /// EquipPart/SetOverride calls with notify:false (e.g. during load).
    /// </summary>
    public void NotifyCreatureChanged()
    {
        cameraFramer?.FrameCreature();
        OnCreatureChanged?.Invoke();
    }

    // ----------------------------------------------------------------
    // QUERIES
    // ----------------------------------------------------------------

    public bool IsCategoryEquipped(BodyPartCategory cat) => equippedParts.ContainsKey(cat);
    public BodyPartData GetEquippedData(BodyPartCategory cat)
        => equippedData.ContainsKey(cat) ? equippedData[cat] : null;
    public IEnumerable<BodyPartCategory> EquippedCategories => equippedParts.Keys;

    public bool IsEquipped(BodyPartData partData)
    {
        if (partData == null) return false;
        return equippedData.ContainsKey(partData.category)
            && equippedData[partData.category] == partData;
    }
}