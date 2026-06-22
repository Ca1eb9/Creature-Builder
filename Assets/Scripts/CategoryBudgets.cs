using UnityEngine;

/// <summary>
/// Defines the target size (in Unity units) for each body part category.
/// Adjust these values to change the proportions of your creature.
/// Think of them as: "how tall/wide should a part of this type be?"
/// </summary>
public static class CategoryBudgets
{
    // These are in Unity units (meters by default).
    // A human is roughly 1.8 units tall for reference.
    // Tweak these to suit your art style.

    public static float GetBudget(BodyPartCategory category)
    {
        switch (category)
        {
            case BodyPartCategory.Head:       return 0.4f;  // head fits in a 0.4 unit cube
            case BodyPartCategory.Torso:      return 0.8f;  // torso is the biggest part
            case BodyPartCategory.FrontLegs:  return 0.6f;
            case BodyPartCategory.BackLegs:   return 0.6f;
            case BodyPartCategory.Tail:       return 0.5f;
            case BodyPartCategory.Wings:      return 0.9f;  // wings are wide
            case BodyPartCategory.Horns:      return 0.25f;
            case BodyPartCategory.Accessories:return 0.2f;
            default:                          return 0.4f;
        }
    }

    // Which axis to constrain each category on.
    // Legs should match HEIGHT (Y), Wings should match WIDTH (X), etc.
    public static PartScaleNormalizer.FitAxis GetFitAxis(BodyPartCategory category)
    {
        switch (category)
        {
            case BodyPartCategory.Wings:  return PartScaleNormalizer.FitAxis.X;
            case BodyPartCategory.Tail:   return PartScaleNormalizer.FitAxis.Z;
            default:                      return PartScaleNormalizer.FitAxis.Y;
        }
    }
}