using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class RotateCreature : MonoBehaviour
{
    [Header("Rotation Settings")]
    public float rotationSpeed = 0.3f;
    public float verticalLimit = 80f;

    [Header("Auto Rotate")]
    public bool autoRotate = false;
    public float autoRotateSpeed = 20f;

    private bool rotating = false;
    private Vector2 lastMousePos;
    private float currentXRotation = 0f;
    private float currentYRotation = 0f;
    private Mouse mouse;

    void Start()
    {
        mouse = Mouse.current;
    }

    void Update()
    {
        if (mouse == null) return;

        // Stand down while a modal dialog is up — its blocker stops UI
        // raycasts but not this script, so dragging on the dialog would
        // spin the creature behind it. Also drop any in-progress drag so
        // the creature doesn't jump when the dialog closes.
        if (UIFeedback.IsDialogOpen)
        {
            rotating = false;
            return;
        }

        HandleRotation();
        HandleAutoRotate();
    }

    void HandleRotation()
    {
        if (IsPointerOverInteractableUI()) return;

        if (mouse.leftButton.wasPressedThisFrame)
        {
            rotating = true;
            lastMousePos = mouse.position.ReadValue();
        }
        else if (mouse.leftButton.wasReleasedThisFrame)
        {
            rotating = false;
        }

        if (rotating)
        {
            Vector2 currentPos = mouse.position.ReadValue();
            Vector2 delta = currentPos - lastMousePos;

            currentYRotation -= delta.x * rotationSpeed;
            currentXRotation += delta.y * rotationSpeed;
            currentXRotation = Mathf.Clamp(currentXRotation, -verticalLimit, verticalLimit);

            transform.localRotation = Quaternion.Euler(currentXRotation, currentYRotation, 0f);
            lastMousePos = currentPos;
        }
    }

    void HandleAutoRotate()
    {
        if (autoRotate && !rotating)
        {
            currentYRotation += autoRotateSpeed * Time.deltaTime;
            transform.localRotation = Quaternion.Euler(currentXRotation, currentYRotation, 0f);
        }
    }

    private bool IsPointerOverInteractableUI()
    {
        if (EventSystem.current == null) return false;
        var pointerData = new PointerEventData(EventSystem.current) { position = mouse.position.ReadValue() };
        var results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(pointerData, results);

        foreach (var hit in results)
        {
            if (hit.gameObject.GetComponentInParent<Button>() != null ||
                hit.gameObject.GetComponentInParent<ScrollRect>() != null ||
                hit.gameObject.GetComponentInParent<Toggle>() != null ||
                hit.gameObject.GetComponentInParent<Slider>() != null ||
                hit.gameObject.GetComponentInParent<TMP_InputField>() != null ||
                hit.gameObject.GetComponentInParent<TMP_Dropdown>() != null)
            {
                return true;
            }
        }
        return false;
    }
}