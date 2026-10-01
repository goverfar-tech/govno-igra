using System;
using UnityEngine.EventSystems;
using UnityEngine;

// Наведение мышью на слот инвентаря/хотбара: показывает тултип
// с именем предмета (TooltipService). Текст лениво берётся из GetText,
// чтобы слот сам следил за своим содержимым.
public class ItemTooltipTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public Func<string> GetText;

    public void OnPointerEnter(PointerEventData e)
    {
        var text = GetText?.Invoke();
        if (!string.IsNullOrEmpty(text))
            TooltipService.Show(text, (RectTransform)transform);
    }

    public void OnPointerExit(PointerEventData e) => TooltipService.Hide();
}
