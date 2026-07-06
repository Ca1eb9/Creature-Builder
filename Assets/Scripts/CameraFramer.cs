using UnityEngine;

/// <summary>
/// Automatically positions the camera so the creature fills a target portion
/// of the viewport. Runs on demand (called by CreatureAssembler when parts change).
/// </summary>
public class CameraFramer : MonoBehaviour
{
    [Header("References")]
    public Camera targetCamera;
    public Transform creatureRoot;

    [Header("Framing")]
    [Tooltip("Padding multiplier — 1.0 = creature edges touch viewport edges. 1.5 = ~33% empty space around it.")]
    public float framePadding = 1.5f;

    [Tooltip("User's zoom multiplier — 0.5 = closer, 2.0 = farther. Set via scroll wheel.")]
    [Range(0.3f, 3.0f)]
    public float userZoomMultiplier = 1.0f;

    [Header("Zoom Input")]
    [Tooltip("How much userZoomMultiplier changes per unit of scroll. New Input System reports scroll in pixels (~120 per notch), so values around 0.001 feel natural.")]
    public float scrollZoomSpeed = 0.001f;

    private Vector3 cameraOffsetDirection;  // direction from creature to camera (unit vector)
    private float baseDistance;             // distance the framer computed last time

    // Runs in Awake (not Start) so the viewing direction is already captured
    // if another script's Start — e.g. UIManager equipping a startup creature —
    // triggers FrameCreature before our own Start would have run. With an
    // uncaptured (zero) direction, ApplyCameraPosition would teleport the
    // camera into the creature and permanently lose the original view angle.
    void Awake()
    {
        if (targetCamera == null) targetCamera = Camera.main;
        if (creatureRoot == null)
        {
            Debug.LogWarning("CameraFramer has no creatureRoot assigned.");
            return;
        }

        // Capture the camera's initial viewing direction so we can preserve
        // its viewing angle when re-framing (e.g., looking slightly downward)
        Vector3 offset = targetCamera.transform.position - creatureRoot.position;
        if (offset.sqrMagnitude < 0.001f) offset = -Vector3.forward;
        cameraOffsetDirection = offset.normalized;
        baseDistance = offset.magnitude;
    }

    void Start()
    {
        FrameCreature();
    }

    void Update()
    {
        if (UnityEngine.InputSystem.Mouse.current == null) return;

        // Only handle zoom if not over UI
        if (UnityEngine.EventSystems.EventSystem.current != null &&
            UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
            return;

        float scroll = UnityEngine.InputSystem.Mouse.current.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) > 0.01f)
        {
            // Scroll up = zoom in (smaller multiplier), scroll down = zoom out
            userZoomMultiplier -= scroll * scrollZoomSpeed;
            userZoomMultiplier = Mathf.Clamp(userZoomMultiplier, 0.3f, 3.0f);
            ApplyCameraPosition();
        }
    }

    /// <summary>
    /// Measures the creature's bounds and computes the base distance needed
    /// to fit it in view. Then applies the user's zoom multiplier on top.
    /// </summary>
    public void FrameCreature()
    {
        if (targetCamera == null || creatureRoot == null) return;

        Bounds b = GetCompositeBounds(creatureRoot.gameObject);

        if (b.size.sqrMagnitude < 0.0001f)
        {
            // Nothing in the creature yet — use a default distance
            baseDistance = 3.0f;
        }
        else
        {
            // Use the bounding sphere radius (diagonal of AABB) so framing
            // is roughly rotation-invariant
            float radius = b.size.magnitude * 0.5f;

            // Distance needed so the bounding sphere fits in the camera's vertical FOV
            float fovRad = targetCamera.fieldOfView * Mathf.Deg2Rad;
            baseDistance = radius / Mathf.Sin(fovRad * 0.5f);
            baseDistance *= framePadding;
        }

        ApplyCameraPosition();
    }

    private void ApplyCameraPosition()
    {
        if (targetCamera == null || creatureRoot == null) return;

        float finalDist = baseDistance * userZoomMultiplier;
        targetCamera.transform.position = creatureRoot.position + cameraOffsetDirection * finalDist;
        // Don't change rotation — preserve whatever viewing angle the camera had originally
    }

    public void ResetZoom()
    {
        userZoomMultiplier = 1.0f;
        ApplyCameraPosition();
    }

    /// <summary>
    /// Composite bounds of ALL renderers under the creature root.
    /// Uses world-space bounds (which reflect current scale).
    /// </summary>
    private Bounds GetCompositeBounds(GameObject obj)
    {
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(obj.transform.position, Vector3.zero);

        Bounds combined = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            combined.Encapsulate(renderers[i].bounds);
        return combined;
    }
}