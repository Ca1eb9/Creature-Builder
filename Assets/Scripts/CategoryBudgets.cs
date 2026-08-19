using UnityEngine;

/// <summary>
/// Defines the target size (in Unity units) for each body part category and the
/// measure used to reach it. Think of a budget as: "how big should a part of
/// this type be, overall?"
///
/// EVERY category fits on the bounding-box DIAGONAL, which tracks a part's
/// overall size regardless of shape — so a long-snouted head and a round head,
/// or a short ferret leg-pair and a tall sheep leg-pair, all end up the same
/// apparent size. (Single-axis fits were leaving the perceived size varying by
/// up to ~5x across a category — see CreaturePartSizeAudit.)
/// </summary>
public static class CategoryBudgets
{
    // In Unity units (meters by default). Diagonal budgets are larger than the
    // old single-axis ones because a box's diagonal is ~1.7x its side.
    // Tweak these to suit your art style.
    public static float GetBudget(BodyPartCategory category)
    {
        switch (category)
        {
            case BodyPartCategory.Head:       return 0.60f;  // overall size (diagonal)
            case BodyPartCategory.Torso:      return 1.30f;  // the big central mass
            case BodyPartCategory.FrontLegs:  return 0.85f;  // leg pair, overall size
            case BodyPartCategory.BackLegs:   return 0.85f;
            case BodyPartCategory.Tail:       return 0.70f;
            case BodyPartCategory.Wings:      return 1.10f;  // wide
            case BodyPartCategory.Horns:      return 0.35f;
            case BodyPartCategory.Ears:      return 0.35f;
            case BodyPartCategory.Accessories:return 0.30f;
            default:                          return 0.50f;
        }
    }

    // Every category is constrained on its overall size (bounding-box diagonal)
    // for consistent apparent sizing. Kept as a method so a specific category
    // could opt out later if needed.
    public static PartScaleNormalizer.FitAxis GetFitAxis(BodyPartCategory category)
        => PartScaleNormalizer.FitAxis.Diagonal;
}
