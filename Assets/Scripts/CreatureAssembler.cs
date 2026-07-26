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

    // The attach-point local positions authored in the scene. Restored as the
    // fallback layout when no torso is equipped to measure sockets from.
    private Dictionary<BodyPartCategory, Vector3> defaultAttachLocalPos = new();
    private bool capturedDefaults;

    // Event: anything that wants to know when the creature changes
    public System.Action OnCreatureChanged;

    void Awake()
    {
        CaptureDefaultAttachPositions();
    }

    private void CaptureDefaultAttachPositions()
    {
        if (capturedDefaults) return;
        foreach (BodyPartCategory cat in System.Enum.GetValues(typeof(BodyPartCategory)))
        {
            Transform a = GetAttachPoint(cat);
            if (a != null) defaultAttachLocalPos[cat] = a.localPosition;
        }
        capturedDefaults = true;
    }

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

        if (notify)
        {
            // Recompute sockets from the (possibly new) torso, place every part,
            // then signal listeners.
            NotifyCreatureChanged();
        }
        else
        {
            // Batch equip (Randomize / Load): give the part a sane transform now;
            // the single final NotifyCreatureChanged does the real layout once.
            ApplyTransform(cat);
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

        if (notify) NotifyCreatureChanged();
    }

    public void ClearAll()
    {
        foreach (var cat in new List<BodyPartCategory>(equippedParts.Keys))
            RemovePart(cat, notify: false);
        overrides.Clear();
        NotifyCreatureChanged();
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
        // Renderer.bounds is a WORLD-space axis-aligned box: measured through
        // whatever rotation the creature is currently spun to, a part's box
        // inflates or shrinks with the angle (a cube at 45° measures ~41%
        // wider). That made auto-fit — and therefore load results — depend on
        // the creature's rotation at equip time. Measure in the unrotated
        // frame and restore the spin afterwards, all within the same frame.
        Quaternion savedRotation = transform.rotation;
        transform.rotation = Quaternion.identity;
        try
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
        }
        finally
        {
            transform.rotation = savedRotation;
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
        if (!notify)
        {
            // Batch (load): reflect the override now; the final
            // NotifyCreatureChanged runs the real layout once.
            ApplyTransform(cat);
            return;
        }

        // Changing a socket-reference part (torso/head) moves sockets and
        // therefore every dependent part — recompute the whole layout. Any
        // other part only needs to re-apply its own transform.
        if (CreatureSockets.IsSocketReference(cat))
        {
            NotifyCreatureChanged();
        }
        else
        {
            ApplyTransform(cat);
            cameraFramer?.FrameCreature();
            OnCreatureChanged?.Invoke();
        }
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
    /// Recomputes the layout, then fires the creature-changed signals. Use after
    /// a batch of EquipPart/SetOverride calls with notify:false (e.g. load), and
    /// whenever the set of parts changes.
    /// </summary>
    public void NotifyCreatureChanged()
    {
        RecomputeLayout();
        cameraFramer?.FrameCreature();
        OnCreatureChanged?.Invoke();
    }

    // ----------------------------------------------------------------
    // LAYOUT: measured torso sockets + part placement
    // ----------------------------------------------------------------

    /// <summary>
    /// Positions each attach point on the equipped torso (and horns on the head)
    /// from measured bounds, then applies every part's transform so each seam
    /// meets its socket. Measured with the root un-rotated so the result is the
    /// same regardless of how the creature is spun — which keeps loads
    /// deterministic (the same fix that cured the earlier reload-drift bug).
    /// </summary>
    public void RecomputeLayout()
    {
        CaptureDefaultAttachPositions(); // safe if Awake hasn't run (edit-time / tests)

        Quaternion savedRotation = transform.rotation;
        transform.rotation = Quaternion.identity;
        try
        {
            // 1. Torso: apply its own transform, measure it, place torso sockets.
            if (equippedParts.TryGetValue(BodyPartCategory.Torso, out var torso) && torso != null)
            {
                ApplyTransform(BodyPartCategory.Torso);
                Bounds torsoBounds = PartScaleNormalizer.GetCompositeBounds(torso);
                PositionSockets(SocketReference.Torso, torsoBounds);
            }
            else
            {
                // Nothing to measure against — fall back to the scene defaults.
                RestoreDefaultAttachPositions();
            }

            // 2. Place every torso-referenced part on its (updated) socket. This
            //    also positions the head, which the horns socket needs next.
            foreach (var cat in new List<BodyPartCategory>(equippedParts.Keys))
            {
                if (cat == BodyPartCategory.Torso) continue;
                if (ReferenceOf(cat) == SocketReference.Torso) ApplyTransform(cat);
            }

            // 3. Head: measure the now-placed head, position head sockets (horns).
            if (equippedParts.TryGetValue(BodyPartCategory.Head, out var head) && head != null)
            {
                Bounds headBounds = PartScaleNormalizer.GetCompositeBounds(head);
                PositionSockets(SocketReference.Head, headBounds);
            }

            // 4. Place head-referenced parts.
            foreach (var cat in new List<BodyPartCategory>(equippedParts.Keys))
            {
                if (ReferenceOf(cat) == SocketReference.Head) ApplyTransform(cat);
            }
        }
        finally
        {
            transform.rotation = savedRotation;
        }
    }

    private SocketReference ReferenceOf(BodyPartCategory cat)
        => CreatureSockets.TryGetRule(cat, out var rule) ? rule.reference : SocketReference.Torso;

    /// <summary>
    /// Moves the attach point of every category whose socket rule references the
    /// given part onto the correct spot of that part's measured bounds.
    /// </summary>
    private void PositionSockets(SocketReference reference, Bounds refBounds)
    {
        foreach (BodyPartCategory cat in System.Enum.GetValues(typeof(BodyPartCategory)))
        {
            if (!CreatureSockets.TryGetRule(cat, out var rule)) continue;
            if (rule.reference != reference) continue;

            Transform attach = GetAttachPoint(cat);
            if (attach == null || attach.parent == null) continue;

            Vector3 worldPoint = refBounds.center + Vector3.Scale(refBounds.extents, rule.boxFraction);
            attach.localPosition = attach.parent.InverseTransformPoint(worldPoint);
        }
    }

    private void RestoreDefaultAttachPositions()
    {
        foreach (var kvp in defaultAttachLocalPos)
        {
            Transform a = GetAttachPoint(kvp.Key);
            if (a != null) a.localPosition = kvp.Value;
        }
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