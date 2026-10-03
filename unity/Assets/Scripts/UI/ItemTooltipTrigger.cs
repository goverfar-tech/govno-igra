using System;
using System.Text;
using UnityEngine.EventSystems;
using UnityEngine;

// Наведение мышью на слот инвентаря/хотбара или иконку рецепта:
// показывает тултип по предмету (TooltipService). Текст лениво берётся
// из GetText, чтобы слот сам следил за своим содержимым.
// R6: статический BuildText собирает многострочный тултип с цифрами
// (еда/яд/лечение/возврат/стак) — используется всеми слотами и CraftPanel.
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

    // Многострочный текст тултипа: имя + по одной строке на каждый
    // ненулевой эффект. null-предмет → null (пустой слот, тултипа нет).
    public static string BuildText(ItemData item)
    {
        if (item == null) return null;
        var sb = new StringBuilder(item.displayName);
        if (item.foodRestore > 0f)
            sb.Append("\nЕда: +").Append(Fmt(item.foodRestore));
        if (item.poisonAmount > 0f)
            // цвет внутри одного Text недоступен — яд помечаем словом
            sb.Append("\nЯд: +").Append(Fmt(item.poisonAmount)).Append(" (яд!)");
        if (item.healAmount > 0f)
            sb.Append("\nЛечение: +").Append(Fmt(item.healAmount));
        if (item.waterRestore > 0f)
            sb.Append("\nВода: +").Append(Fmt(item.waterRestore));
        if (item.consumeReturns != null)
            sb.Append("\nВозврат: ").Append(item.consumeReturns.displayName);
        if (item.maxStack > 1)
            sb.Append("\nСтак: ").Append(item.maxStack);
        return sb.ToString();
    }

    // 12 → "12", 12.5 → "12,5" (культура русская — запятая уместна)
    static string Fmt(float v) => v.ToString("0.#");
}
