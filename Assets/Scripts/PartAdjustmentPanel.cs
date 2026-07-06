using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Compact panel with sliders for adjusting the currently focused body part.
/// Hidden by default, expands when user clicks the Adjust button.
/// </summary>
public class PartAdjustmentPanel : MonoBehaviour
{
    [Header("References")]
    public CreatureAssembler assembler;

    [Header("UI — collapsible content area")]
    public GameObject contentRoot;        // The whole sliders panel — toggle this
    public Button toggleButton;           // Expands/collapses the panel
    public TextMeshProUGUI toggleLabel;   // "Adjust ▾" or "Adjust ▴"

    [Header("UI — header")]
    public TextMeshProUGUI titleLabel;    // "Adjusting: Head"

    [Header("UI — sliders")]
    public Slider posXSlider, posYSlider, posZSlider;
    public Slider rotXSlider, rotYSlider, rotZSlider;
    public Slider scaleSlider;
    public Button resetButton;

    [Header("Ranges")]
    public float positionRange = 0.5f;     // ± from baseline, in Unity units
    public float rotationRange = 180f;     // ± degrees from baseline
    public Vector2 scaleRange = new Vector2(0.3f, 3.0f);

    private BodyPartCategory? activeCategory;
    private bool suppressEvents = false;
    private bool expanded = false;

    // Awake (not Start) so this one-time setup is guaranteed to run before
    // UIManager.Start calls SetActiveCategory on us. With Start, execution
    // order was undefined and this could reset the panel to "No part
    // selected" AFTER UIManager had already pointed it at a category.
    void Awake()
    {
        ConfigureSliderRanges();
        WireUp();
        SetExpanded(false);
        SetActiveCategory(null);

        if (assembler != null)
            assembler.OnCreatureChanged += OnCreatureChanged;
    }

    void OnDestroy()
    {
        if (assembler != null)
            assembler.OnCreatureChanged -= OnCreatureChanged;
    }

    private void ConfigureSliderRanges()
    {
        SetSliderRange(posXSlider, -positionRange, positionRange);
        SetSliderRange(posYSlider, -positionRange, positionRange);
        SetSliderRange(posZSlider, -positionRange, positionRange);
        SetSliderRange(rotXSlider, -rotationRange, rotationRange);
        SetSliderRange(rotYSlider, -rotationRange, rotationRange);
        SetSliderRange(rotZSlider, -rotationRange, rotationRange);
        SetSliderRange(scaleSlider, scaleRange.x, scaleRange.y);
    }

    private void SetSliderRange(Slider s, float min, float max)
    {
        if (s == null) return;
        s.minValue = min;
        s.maxValue = max;
    }

    private void WireUp()
    {
        if (toggleButton != null) toggleButton.onClick.AddListener(() => SetExpanded(!expanded));
        if (resetButton != null) resetButton.onClick.AddListener(OnReset);

        AddSliderListener(posXSlider); AddSliderListener(posYSlider); AddSliderListener(posZSlider);
        AddSliderListener(rotXSlider); AddSliderListener(rotYSlider); AddSliderListener(rotZSlider);
        AddSliderListener(scaleSlider);
    }

    private void AddSliderListener(Slider s)
    {
        if (s != null) s.onValueChanged.AddListener(_ => OnSliderChanged());
    }

    public void SetActiveCategory(BodyPartCategory? cat)
    {
        activeCategory = cat;
        bool valid = cat.HasValue && assembler != null && assembler.IsCategoryEquipped(cat.Value);

        if (toggleButton != null) toggleButton.interactable = valid;
        if (titleLabel != null) titleLabel.text = valid ? $"Adjusting: {cat.Value}" : "No part selected";

        if (!valid) SetExpanded(false);
        else if (expanded) LoadSlidersFromOverride();
    }

    private void SetExpanded(bool value)
    {
        expanded = value;
        if (contentRoot != null) contentRoot.SetActive(expanded);
        if (toggleLabel != null) toggleLabel.text = expanded ? "Hide Sliders" : "Show Sliders";
        if (expanded) LoadSlidersFromOverride();
    }

    private void LoadSlidersFromOverride()
    {
        if (!activeCategory.HasValue || assembler == null) return;
        if (!assembler.IsCategoryEquipped(activeCategory.Value)) return;

        var ov = assembler.GetOverride(activeCategory.Value);
        suppressEvents = true;
        if (posXSlider != null) posXSlider.value = ov.positionDelta.x;
        if (posYSlider != null) posYSlider.value = ov.positionDelta.y;
        if (posZSlider != null) posZSlider.value = ov.positionDelta.z;
        if (rotXSlider != null) rotXSlider.value = ov.rotationDelta.x;
        if (rotYSlider != null) rotYSlider.value = ov.rotationDelta.y;
        if (rotZSlider != null) rotZSlider.value = ov.rotationDelta.z;
        if (scaleSlider != null) scaleSlider.value = ov.scaleMultiplier;
        suppressEvents = false;
    }

    private void OnSliderChanged()
    {
        if (suppressEvents || !activeCategory.HasValue || assembler == null) return;
        if (!assembler.IsCategoryEquipped(activeCategory.Value)) return;

        // Mutate the assembler's existing override in place — no per-frame allocation
        // during slider drags. GetOverride returns the live reference.
        var ov = assembler.GetOverride(activeCategory.Value);
        ov.positionDelta = new Vector3(
            posXSlider != null ? posXSlider.value : 0f,
            posYSlider != null ? posYSlider.value : 0f,
            posZSlider != null ? posZSlider.value : 0f);
        ov.rotationDelta = new Vector3(
            rotXSlider != null ? rotXSlider.value : 0f,
            rotYSlider != null ? rotYSlider.value : 0f,
            rotZSlider != null ? rotZSlider.value : 0f);
        ov.scaleMultiplier = scaleSlider != null ? scaleSlider.value : 1f;

        assembler.RefreshOverride(activeCategory.Value);
    }

    private void OnReset()
    {
        if (!activeCategory.HasValue) return;
        assembler.ResetOverride(activeCategory.Value);
        LoadSlidersFromOverride();
    }

    private void OnCreatureChanged()
    {
        // If our active category is no longer equipped, hide
        if (activeCategory.HasValue && !assembler.IsCategoryEquipped(activeCategory.Value))
            SetActiveCategory(null);
    }
}