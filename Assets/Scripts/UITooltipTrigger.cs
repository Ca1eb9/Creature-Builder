using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Shows the shared <see cref="UITooltip"/> bubble while the pointer rests on
/// this element. Must live in its own file so Unity can serialize it onto
/// scene objects.
/// </summary>
public class UITooltipTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [TextArea] public string text;

    private float hoverStart = -1f;
    private bool showing;
    private Vector2 lastPos;

    public void OnPointerEnter(PointerEventData e)
    {
        hoverStart = Time.unscaledTime;
        lastPos = e.position;
    }

    public void OnPointerExit(PointerEventData e)
    {
        hoverStart = -1f;
        if (showing) { UITooltip.Hide(); showing = false; }
    }

    void Update()
    {
        if (hoverStart < 0f || showing) return;
        if (Time.unscaledTime - hoverStart >= UITooltip.ShowDelay)
        {
            UITooltip.Show(text, lastPos);
            showing = true;
        }
    }

    void OnDisable()
    {
        if (showing) { UITooltip.Hide(); showing = false; }
        hoverStart = -1f;
    }
}
