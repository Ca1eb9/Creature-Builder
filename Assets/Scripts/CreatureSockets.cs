using UnityEngine;

/// <summary>
/// Which reference part a socket is measured against. The torso is the anchor
/// for most parts; horns (and head-mounted accessories) sit on the head.
/// </summary>
public enum SocketReference { Torso, Head }

/// <summary>
/// A rule for placing a category's attach point on a reference part's measured
/// bounding box. <see cref="boxFraction"/> is expressed as a fraction of the
/// box's extents (half-size) from its center, one component per axis in the
/// range [-1, 1]: 0 = center, +1 = the +face, -1 = the -face. Because it scales
/// with the measured box, the same rule works for a tiny torso or a huge one.
/// Forward is +Z.
/// </summary>
public struct SocketRule
{
    public SocketReference reference;
    public Vector3 boxFraction;

    public SocketRule(SocketReference reference, Vector3 boxFraction)
    {
        this.reference = reference;
        this.boxFraction = boxFraction;
    }
}

/// <summary>
/// Maps each body-part category to where its attach point sits on the measured
/// bounds of a reference part. Only non-torso parts have rules — the torso IS
/// the reference, so it stays at its authored attach point. Values are rough
/// anatomical defaults; fine-tune per part with the position sliders.
/// </summary>
public static class CreatureSockets
{
    public static bool TryGetRule(BodyPartCategory category, out SocketRule rule)
    {
        switch (category)
        {
            // On the torso box (forward = +Z):
            case BodyPartCategory.Head:        rule = new SocketRule(SocketReference.Torso, new Vector3(0f,  0.85f,  0.80f)); return true; // top, well forward
            case BodyPartCategory.FrontLegs:   rule = new SocketRule(SocketReference.Torso, new Vector3(0f, -0.90f,  0.55f)); return true; // bottom, forward
            case BodyPartCategory.BackLegs:    rule = new SocketRule(SocketReference.Torso, new Vector3(0f, -0.90f, -0.55f)); return true; // bottom, well back
            case BodyPartCategory.Tail:        rule = new SocketRule(SocketReference.Torso, new Vector3(0f,  0.30f, -0.95f)); return true; // back, mid-upper
            case BodyPartCategory.Wings:       rule = new SocketRule(SocketReference.Torso, new Vector3(0f,  0.60f, -0.10f)); return true; // upper back
            case BodyPartCategory.Accessories: rule = new SocketRule(SocketReference.Torso, new Vector3(0f,  0.40f,  0.50f)); return true; // upper front

            // On the head box:
            case BodyPartCategory.Horns:       rule = new SocketRule(SocketReference.Head,  new Vector3(0f,  0.90f,  0.00f)); return true; // top of head
            case BodyPartCategory.Ears:        rule = new SocketRule(SocketReference.Head,  new Vector3(0f,  0.65f,  -0.10f)); return true; // top of head

            default:                           rule = default; return false; // Torso: no socket
        }
    }

    /// <summary>
    /// True for parts whose measured bounds drive other parts' sockets (torso,
    /// head). Changing one of these must trigger a full layout recompute.
    /// </summary>
    public static bool IsSocketReference(BodyPartCategory category)
        => category == BodyPartCategory.Torso || category == BodyPartCategory.Head;
}
