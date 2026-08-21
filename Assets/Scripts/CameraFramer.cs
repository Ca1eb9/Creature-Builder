using UnityEngine;

/// <summary>
/// Automatically positions the camera so the creature fills a target portion
/// of the viewport. Runs on demand (called by CreatureAssembler when parts change).
/// </summary>
public class CameraFramer : MonoBehaviour
{
    [Header("References")]
    public Camera targetCamera;
    /// <summary>The stage camera, for callers that need its viewport (screenshot cropping).</summary>
    public Camera Cam => targetCamera;
    public Transform creatureRoot;

    [Header("Framing")]
    [Tooltip("Padding multiplier — 1.0 = creature edges touch viewport edges. 1.5 = ~33% empty space around it.")]
    public float framePadding = 1.5f;

    [Tooltip("User's zoom multiplier — 0.5 = closer, 2.0 = farther. Set via scroll wheel.")]
    [Range(0.3f, 3.0f)]
    public float userZoomMultiplier = 1.0f;

    [Header("Zoom Input")]
    [Tooltip("Fraction of the current distance moved per wheel notch. 0.2 = each notch zooms 20% closer/farther.")]
    [Range(0.05f, 0.4f)]
    public float zoomStepPerNotch = 0.2f;

    [Tooltip("How quickly the camera glides to the target zoom. Higher = snappier, lower = floatier.")]
    public float zoomSmoothing = 12f;

    private Vector3 cameraOffsetDirection;  // direction from creature to camera (unit vector)
    private float baseDistance;             // distance the framer computed last time
    private float targetZoomMultiplier = 1f; // where the glide is headed

    // Runs in Awake (not Start) so the viewing direction is already captured
    // if another script's Start — e.g. UIManager equipping a startup creature —
    // triggers FrameCreature before our own Start would have run. With an
    // uncaptured (zero) direction, ApplyCameraPosition would teleport the
    // camera into the creature and permanently lose the original view angle.
    void Awake()
    {
        targetZoomMultiplier = userZoomMultiplier;
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
        // Keep the stage inset correct when the window is resized.
        if (viewportInsets != Vector4.zero &&
            (!Mathf.Approximately(lastScreenSize.x, Screen.width) ||
             !Mathf.Approximately(lastScreenSize.y, Screen.height)))
            ApplyViewportInsets();

        var mouse = UnityEngine.InputSystem.Mouse.current;

        // Input is gated (no zooming over UI or under a dialog), but the
        // glide below still runs so an in-progress zoom finishes smoothly
        bool inputAllowed =
            mouse != null &&
            !UIFeedback.IsDialogOpen &&
            !(UnityEngine.EventSystems.EventSystem.current != null &&
              UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject());

        if (inputAllowed)
        {
            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                // Proportional zoom: each notch moves a fixed FRACTION of
                // the current distance, uniform whether close or far. The
                // Input System normalizes a wheel notch to ±1 (older setups
                // report raw ±120 — the clamp treats anything past one notch
                // the same); trackpads stream small fractional deltas and
                // get proportionally gentler steps.
                float steps = Mathf.Clamp(scroll, -1f, 1f);
                targetZoomMultiplier *= 1f - steps * zoomStepPerNotch;
                targetZoomMultiplier = Mathf.Clamp(targetZoomMultiplier, 0.3f, 3.0f);
            }
        }

        // Exponential glide toward the target — snappy at first, easing in
        if (!Mathf.Approximately(userZoomMultiplier, targetZoomMultiplier))
        {
            userZoomMultiplier = Mathf.Lerp(
                userZoomMultiplier, targetZoomMultiplier,
                1f - Mathf.Exp(-zoomSmoothing * Time.deltaTime));
            if (Mathf.Abs(userZoomMultiplier - targetZoomMultiplier) < 0.001f)
                userZoomMultiplier = targetZoomMultiplier;
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

            // Distance needed so the bounding sphere fits the camera's view. Check
            // BOTH axes: once the stage is inset between the panels the viewport
            // is much narrower than the window, and fitting only the vertical FOV
            // would crop a wide creature left and right.
            float fovRad = targetCamera.fieldOfView * Mathf.Deg2Rad;
            float distV = radius / Mathf.Sin(fovRad * 0.5f);

            float aspect = Mathf.Max(0.01f, targetCamera.aspect);
            float hFovRad = 2f * Mathf.Atan(Mathf.Tan(fovRad * 0.5f) * aspect);
            float distH = radius / Mathf.Sin(hFovRad * 0.5f);

            baseDistance = Mathf.Max(distV, distH) * framePadding;
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

    // ----------------------------------------------------------------
    // STAGE VIEWPORT
    // ----------------------------------------------------------------

    private Vector4 viewportInsets;   // left, right, top, bottom — in pixels
    private Vector2 lastScreenSize;

    /// <summary>
    /// Restricts the camera to the free area between the UI panels, the way the
    /// mockup insets the stage (left:340 top:56 …). Without this the creature is
    /// rendered full-screen and its edges hide behind the rail and inspector.
    /// Values are in screen pixels; re-applied automatically on resize.
    /// </summary>
    public void SetViewportInsets(float left, float right, float top, float bottom)
    {
        viewportInsets = new Vector4(left, right, top, bottom);
        ApplyViewportInsets();
    }

    private void ApplyViewportInsets()
    {
        if (targetCamera == null) return;

        float w = Mathf.Max(1f, Screen.width);
        float h = Mathf.Max(1f, Screen.height);
        lastScreenSize = new Vector2(w, h);

        float x = Mathf.Clamp01(viewportInsets.x / w);
        float y = Mathf.Clamp01(viewportInsets.w / h);
        float width = Mathf.Clamp01(1f - (viewportInsets.x + viewportInsets.y) / w);
        float height = Mathf.Clamp01(1f - (viewportInsets.z + viewportInsets.w) / h);

        // Never collapse the stage to nothing if the panels ever exceed the window.
        if (width < 0.05f || height < 0.05f) { targetCamera.rect = new Rect(0, 0, 1, 1); return; }

        targetCamera.rect = new Rect(x, y, width, height);
        FrameCreature(); // the visible aspect changed, so re-fit
    }

    public void ResetZoom()
    {
        userZoomMultiplier = 1.0f;
        targetZoomMultiplier = 1.0f;
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