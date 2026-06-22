using UnityEngine;

/// <summary>
/// Normalizes the scale of a body part to fit within a defined bounding box.
/// Run this at equip time so every part fits the creature proportionally.
/// </summary>
public static class PartScaleNormalizer
{
    /// <summary>
    /// Rescales a part so its largest dimension fits within targetSize.
    /// Preserves aspect ratio — the part will never be distorted.
    /// </summary>
    /// <param name="part">The instantiated part GameObject</param>
    /// <param name="targetSize">The max size in Unity units this part should occupy</param>
    /// <param name="axis">Which axis to constrain: 0=largest, 1=X, 2=Y, 3=Z</param>
    public static void NormalizeToSize(GameObject part, float targetSize, FitAxis axis = FitAxis.Largest)
    {
        Bounds bounds = GetCompositeBounds(part);

        if (bounds.size == Vector3.zero) return;

        float currentSize;
        switch (axis)
        {
            case FitAxis.X:        currentSize = bounds.size.x; break;
            case FitAxis.Y:        currentSize = bounds.size.y; break;
            case FitAxis.Z:        currentSize = bounds.size.z; break;
            default:               currentSize = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z); break;
        }

        if (currentSize < 0.0001f) return;

        float scaleFactor = targetSize / currentSize;
        part.transform.localScale *= scaleFactor;
    }

    /// <summary>
    /// Rescales a part so it matches a reference part's size on a given axis.
    /// Useful for: "make this head the same height as the current torso's neck."
    /// </summary>
    public static void NormalizeToMatch(GameObject partToScale, GameObject reference,
                                        FitAxis axis = FitAxis.Y, float multiplier = 1f)
    {
        Bounds boundsA = GetCompositeBounds(partToScale);
        Bounds boundsB = GetCompositeBounds(reference);

        if (boundsA.size == Vector3.zero || boundsB.size == Vector3.zero) return;

        float sizeA, sizeB;
        switch (axis)
        {
            case FitAxis.X:
                sizeA = boundsA.size.x;
                sizeB = boundsB.size.x;
                break;
            case FitAxis.Z:
                sizeA = boundsA.size.z;
                sizeB = boundsB.size.z;
                break;
            default: // Y
                sizeA = boundsA.size.y;
                sizeB = boundsB.size.y;
                break;
        }

        if (sizeA < 0.0001f) return;

        float scaleFactor = (sizeB / sizeA) * multiplier;
        partToScale.transform.localScale *= scaleFactor;
    }

    /// <summary>
    /// After scaling, repositions the part so its bottom sits at the attachment point.
    /// Prevents parts from floating above or sinking below where they should be.
    /// </summary>
    public static void AlignBottom(GameObject part, Transform attachPoint)
    {
        Bounds bounds = GetCompositeBounds(part);

        // How far is the bottom of the mesh from the object's pivot?
        float pivotToBottom = part.transform.position.y - bounds.min.y;

        // Offset the part so its bottom is exactly at the attach point
        part.transform.position = new Vector3(
            attachPoint.position.x,
            attachPoint.position.y + pivotToBottom,
            attachPoint.position.z
        );
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

    public enum FitAxis { Largest, X, Y, Z }
}