using UnityEngine;

public enum BodyPartCategory
{
    Head, Torso, FrontLegs, BackLegs, Tail, Wings, Horns, Ears, Accessories
}

[CreateAssetMenu(fileName = "NewBodyPart", menuName = "Creature Viewer/Body Part Data")]
public class BodyPartData : ScriptableObject
{
    [Header("Identification")]
    [SerializeField] private string partID;
    public string PartID => partID;

    [Header("Display")]
    public string partName = "New Part";
    public BodyPartCategory category;
    public GameObject prefab;
    public Sprite icon;
    [TextArea] public string description;

    [Header("Baseline Calibration (per-part, persistent)")]
    public Vector3 positionOffset = Vector3.zero;
    public Vector3 rotationOffset = Vector3.zero;
    public float scaleMultiplier = 1.0f;

    [Header("Auto Scale")]
    public bool useAutoScale = true;

    void OnValidate()
    {
        // Auto-generate a stable ID the first time the asset is created
        if (string.IsNullOrEmpty(partID))
        {
            partID = System.Guid.NewGuid().ToString();
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }
    }
}