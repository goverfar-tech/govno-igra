using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Окно крафта по клавише C (Поток В). Список рецептов грузится из
// Resources/Recipes (пекутся через Survival → Bake Recipes): иконка
// результата (icon биндится ItemIconBinder'ом), название, стоимость
// «Древесина 2/3» по ингредиентам (красным, если не хватает),
// кнопка «Создать» активна ровно при CanCraft.
// Обновляется по InventoryChanged (подписка — в Hud, вызов Refresh).
// Само окно открывает/закрывает Hud (C / Escape); пока открыто —
// uiActive в Hud поднимает InventoryOpenChanged и ввод игрока блокируется.
public class CraftPanel : MonoBehaviour
{
    const float RowHeight = 62f;
    const float RowGap = 8f;
    const float Pad = 14f;

    class Row
    {
        public RecipeData recipe;
        public Text cost;
        public Button craft;
    }

    readonly List<Row> rows = new List<Row>();
    Inventory inventory;

    public static CraftPanel Create(Transform parent, Inventory inv)
    {
        var back = UiWidgets.Panel(parent, "CraftPanel", UiWidgets.PanelColor);
        var view = back.gameObject.AddComponent<CraftPanel>();
        view.inventory = inv;

        // стабильный порядок независимо от FS: по id результата
        var recipes = Resources.LoadAll<RecipeData>("Recipes");
        System.Array.Sort(recipes, (a, b) => string.CompareOrdinal(
            a.result != null ? a.result.id : "", b.result != null ? b.result.id : ""));

        var rt = (RectTransform)back.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        float width = 520f;
        float height = 42f + recipes.Length * (RowHeight + RowGap) + 34f + Pad;
        rt.sizeDelta = new Vector2(width, Mathf.Max(height, 150f));

        var title = UiWidgets.Text(back.transform, "Title", 18);
        var tr = (RectTransform)title.transform;
        tr.anchorMin = tr.anchorMax = new Vector2(0.5f, 1f);
        tr.pivot = new Vector2(0.5f, 1f);
        tr.anchoredPosition = new Vector2(0f, -10f);
        tr.sizeDelta = new Vector2(width, 24f);
        title.text = "КРАФТ";

        if (recipes.Length == 0)
        {
            var empty = UiWidgets.Text(back.transform, "Empty", 14);
            var er = (RectTransform)empty.transform;
            er.anchorMin = er.anchorMax = new Vector2(0.5f, 0.5f);
            er.sizeDelta = new Vector2(width, 30f);
            empty.text = "Нет рецептов (Survival → Bake Recipes)";
            empty.color = new Color(1f, 1f, 1f, 0.5f);
        }

        float y = -44f;
        foreach (var recipe in recipes)
        {
            view.BuildRow(back.transform, recipe, y, width);
            y -= RowHeight + RowGap;
        }

        var hint = UiWidgets.Text(back.transform, "Hint", 12);
        var hr = (RectTransform)hint.transform;
        hr.anchorMin = hr.anchorMax = new Vector2(0.5f, 0f);
        hr.pivot = new Vector2(0.5f, 0f);
        hr.anchoredPosition = new Vector2(0f, 8f);
        hr.sizeDelta = new Vector2(width, 22f);
        hint.text = "C / Esc — закрыть";
        hint.color = new Color(1f, 1f, 1f, 0.45f);

        back.gameObject.SetActive(false);
        return view;
    }

    void BuildRow(Transform parent, RecipeData recipe, float top, float panelWidth)
    {
        var back = UiWidgets.Panel(parent, "Recipe_" + recipe.name, UiWidgets.SlotColor);
        var rt = (RectTransform)back.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(Pad, top);
        rt.sizeDelta = new Vector2(panelWidth - 2 * Pad, RowHeight);

        // иконка результата (icon приходит из ItemIconBinder); нет иконки —
        // остаётся тёмная ячейка-подложка
        var cell = UiWidgets.Panel(rt, "IconCell", UiWidgets.BarBackColor);
        var crt0 = (RectTransform)cell.transform;
        crt0.anchorMin = crt0.anchorMax = new Vector2(0f, 0.5f);
        crt0.pivot = new Vector2(0f, 0.5f);
        crt0.anchoredPosition = new Vector2(9f, 0f);
        crt0.sizeDelta = new Vector2(44f, 44f);
        cell.raycastTarget = false;

        var icon = UiWidgets.Panel(cell.transform, "Icon", Color.white);
        UiWidgets.Stretch(icon.rectTransform, 3f);
        icon.preserveAspect = true;
        icon.raycastTarget = false;
        icon.enabled = false;
        var res = recipe.result;
        if (res != null && res.icon != null)
        {
            icon.sprite = res.icon;
            icon.enabled = true;
        }

        // название результата
        var nameText = UiWidgets.Text(rt, "Name", 15, TextAnchor.MiddleLeft);
        nameText.rectTransform.anchorMin = nameText.rectTransform.anchorMax = new Vector2(0f, 1f);
        nameText.rectTransform.pivot = new Vector2(0f, 1f);
        nameText.rectTransform.anchoredPosition = new Vector2(64f, -5f);
        nameText.rectTransform.sizeDelta = new Vector2(280f, 24f);
        if (res != null) nameText.text = res.displayName;

        // строка стоимости с подсветкой нехватки (rich text)
        var costText = UiWidgets.Text(rt, "Cost", 13, TextAnchor.MiddleLeft);
        costText.rectTransform.anchorMin = costText.rectTransform.anchorMax = new Vector2(0f, 0f);
        costText.rectTransform.pivot = new Vector2(0f, 0f);
        costText.rectTransform.anchoredPosition = new Vector2(64f, 5f);
        costText.rectTransform.sizeDelta = new Vector2(320f, 26f);

        // кнопка «Создать»
        var craftBtn = UiWidgets.Button(rt, "CraftButton", "Создать", 15);
        var crt = (RectTransform)craftBtn.transform;
        crt.anchorMin = crt.anchorMax = new Vector2(1f, 0.5f);
        crt.pivot = new Vector2(1f, 0.5f);
        crt.anchoredPosition = new Vector2(-10f, 0f);
        crt.sizeDelta = new Vector2(96f, 36f);
        craftBtn.onClick.AddListener(() => recipe.TryCraft(inventory));

        rows.Add(new Row { recipe = recipe, cost = costText, craft = craftBtn });
    }

    // Пересчёт доступности: вызывается Hud'ом на каждый InventoryChanged.
    public void Refresh(Inventory inv)
    {
        if (inv != null) inventory = inv;
        foreach (var row in rows)
        {
            row.cost.text = CostString(row.recipe, inventory);
            row.craft.interactable = row.recipe.CanCraft(inventory);
        }
    }

    // «Древесина 2/3 · Камень 1/0» — нехватка красным (WarningColor).
    static string CostString(RecipeData recipe, Inventory inv)
    {
        if (recipe.inputs == null) return "";
        var sb = new System.Text.StringBuilder();
        foreach (var ing in recipe.inputs)
        {
            if (ing.item == null) continue;
            int have = inv != null ? inv.CountOf(ing.item) : 0;
            string hex = have >= ing.count ? "#D8D8CC" : "#C04035";
            if (sb.Length > 0) sb.Append(" · ");
            sb.Append($"<color={hex}>{ing.item.displayName} {have}/{ing.count}</color>");
        }
        return sb.ToString();
    }
}
