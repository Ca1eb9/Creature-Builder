using UnityEngine;

[System.Serializable]
public class PartOverride
{
    public Vector3 positionDelta = Vector3.zero;
    public Vector3 rotationDelta = Vector3.zero;
    public float scaleMultiplier = 1.0f;

    public PartOverride Clone()
    {
        return new PartOverride
        {
            positionDelta = this.positionDelta,
            rotationDelta = this.rotationDelta,
            scaleMultiplier = this.scaleMultiplier
        };
    }

    public bool IsDefault()
    {
        return positionDelta == Vector3.zero
            && rotationDelta == Vector3.zero
            && Mathf.Approximately(scaleMultiplier, 1.0f);
    }
}