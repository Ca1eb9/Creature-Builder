using UnityEngine;

/// <summary>
/// Normalizes the scale of a body part to fit within a defined bounding box.
/// Run this at equip time so every part fits the creature proportionally.
/// </summary>
public static class PartScaleNormalizer
{
    /// <summary>
    /// Rescales a part so a chosen measure of its bounds matches targetSize.
    /// Preserves aspect ratio — the part is never distorted.
    ///
    /// FitAxis.Diagonal fits the bounding-box diagonal, which tracks a part's
    /// *overall* size regardless of shape — a long snout and a round head then
    /// read as the same size. Prefer it for most categories; use a single axis
    /// only where one dimension is the meaningful one (e.g. wing span on X).
    /// </summary>
    /// <param name="part">The instantiated part GameObject</param>
    /// <param name="targetSize">The size in Unity units this part should occupy</param>
    /// <param name="axis">Which measure to constrain</param>
    public static void NormalizeToSize(GameObject part, float targetSize, FitAxis axis = FitAxis.Diagonal)
    {
        Bounds bounds = GetCompositeBounds(part);

        if (bounds.size == Vector3.zero) return;

        float currentSize;
        switch (axis)
        {
            case FitAxis.X:        currentSize = bounds.size.x; break;
            case FitAxis.Y:        currentSize = bounds.size.y; break;
            case FitAxis.Z:        currentSize = bounds.size.z; break;
            case FitAxis.Diagonal: currentSize = bounds.size.magnitude; break;
            default:               currentSize = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z); break;
        }

        if (currentSize < 0.0001f) return;

        float scaleFactor = targetSize / currentSize;
        part.transform.localScale *= scaleFactor;
    }

    /// <summary>
    /// Gets the combined bounding box of all renderers in a part,
    /// including all its children.
    /// </summary>
    public static Bounds GetCompositeBounds(GameObject obj)
    {
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();

        if (renderers.Length == 0) return new Bounds(obj.transform.position, Vector3.zero);

        Bounds combined = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            combined.Encapsulate(renderers[i].bounds);
        }
        return combined;
    }

    public enum FitAxis { Largest, X, Y, Z, Diagonal }
}
