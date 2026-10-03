using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Окно крафта по клавише C (Поток В). Список рецептов грузится из
// Resources/Recipes (пекутся через Survival → Bake Recipes): иконка
// результата (icon биндится ItemIconBinder'ом), название, стоимость
// «Древесина 2/3» по ингредиентам (красным, если не хватает),
// кнопка «Создать» активна ровно при CanCraft.
// Список — прокручиваемый (ScrollRect + маскированный Viewport):
// при 10+ рецептах низ панели не уезжает за экран, высота панели
// ограничена ~70% референсной высоты канваса.
// Обновляется по InventoryChanged (подписка — в Hud, вызов Refresh).
// Само окно открывает/закрывает Hud (C / Escape); пока открыто —
// uiActive в Hud поднимает InventoryOpenChanged и ввод игрока блокируется.
public class CraftPanel : MonoBehaviour
{
    const float RowHeight = 62f;
    const float RowGap = 8f;
    const float Pad = 14f;
    const float PanelWidth = 520f;
    const float TopPad = 44f;    // место под заголовок
    const float BottomPad = 38f; // место под подсказку
    // потолок высоты — ~70% референсной высоты HudCanvas (1080)
    const float MaxHeight = 760f;

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

        // высота списка — по содержимому, но панель не выше MaxHeight
        float listFull = recipes.Length * (RowHeight + RowGap);
        float scrollH = Mathf.Clamp(listFull, 30f, MaxHeight - TopPad - BottomPad);
        float height = Mathf.Max(TopPad + scrollH + BottomPad, 150f);

        var rt = (RectTransform)back.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(PanelWidth, height);

        var title = UiWidgets.Text(back.transform, "Title", 18);
        var tr = (RectTransform)title.transform;
        tr.anchorMin = tr.anchorMax = new Vector2(0.5f, 1f);
        tr.pivot = new Vector2(0.5f, 1f);
        tr.anchoredPosition = new Vector2(0f, -10f);
        tr.sizeDelta = new Vector2(PanelWidth, 24f);
        title.text = "КРАФТ";

        // ---- прокрутка: ScrollRect → Viewport(RectMask2D) → Content(VLG+fitter) ----
        var scrollGo = new GameObject("Scroll", typeof(RectTransform), typeof(ScrollRect));
        scrollGo.transform.SetParent(back.transform, false);
        // невидимый ловец лучей: ScrollRect не Graphic, без этого Image
        // колесо/драг срабатывали бы только над строками, а не в зазорах
        var scrollCatch = scrollGo.AddComponent<Image>();
        scrollCatch.color = new Color(0f, 0f, 0f, 0f);
        var srt = (RectTransform)scrollGo.transform;
        srt.anchorMin = srt.anchorMax = new Vector2(0.5f, 1f);
        srt.pivot = new Vector2(0.5f, 1f);
        srt.anchoredPosition = new Vector2(0f, -TopPad + 6f);
        srt.sizeDelta = new Vector2(PanelWidth, scrollH);

        var viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
        viewportGo.transform.SetParent(srt, false);
        var vpt = (RectTransform)viewportGo.transform;
        UiWidgets.Stretch(vpt, 0f);

        var contentGo = new GameObject("Content", typeof(RectTransform));
        contentGo.transform.SetParent(vpt, false);
        var crt = (RectTransform)contentGo.transform;
        crt.anchorMin = new Vector2(0f, 1f);
        crt.anchorMax = new Vector2(1f, 1f);
        crt.pivot = new Vector2(0.5f, 1f);
        crt.anchoredPosition = Vector2.zero;
        crt.sizeDelta = Vector2.zero;

        var vlg = contentGo.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset((int)Pad, (int)Pad, 0, 0);
        vlg.spacing = RowGap;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        var fitter = contentGo.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = scrollGo.GetComponent<ScrollRect>();
        scroll.viewport = vpt;
        scroll.content = crt;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped; // без резиновых краёв
        scroll.scrollSensitivity = RowHeight * 0.5f;

        if (recipes.Length == 0)
        {
            var empty = UiWidgets.Text(back.transform, "Empty", 14);
            var er = (RectTransform)empty.transform;
            er.anchorMin = er.anchorMax = new Vector2(0.5f, 0.5f);
            er.sizeDelta = new Vector2(PanelWidth, 30f);
            empty.text = "Нет рецептов (Survival → Bake Recipes)";
            empty.color = new Color(1f, 1f, 1f, 0.5f);
        }

        foreach (var recipe in recipes)
            view.BuildRow(crt, recipe);

        var hint = UiWidgets.Text(back.transform, "Hint", 12);
        var hr = (RectTransform)hint.transform;
        hr.anchorMin = hr.anchorMax = new Vector2(0.5f, 0f);
        hr.pivot = new Vector2(0.5f, 0f);
        hr.anchoredPosition = new Vector2(0f, 8f);
        hr.sizeDelta = new Vector2(PanelWidth, 22f);
        hint.text = "C / Esc — закрыть  •  колесо — листать";
        hint.color = new Color(1f, 1f, 1f, 0.45f);

        back.gameObject.SetActive(false);
        return view;
    }

    // Строка рецепта внутри ScrollRect-контента: ширину и позицию задаёт
    // VerticalLayoutGroup, высоту фиксируем через LayoutElement.
    void BuildRow(Transform parent, RecipeData recipe)
    {
        var back = UiWidgets.Panel(parent, "Recipe_" + recipe.name, UiWidgets.SlotColor);
        var le = back.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = RowHeight;
        var rt = (RectTransform)back.transform;
        rt.sizeDelta = new Vector2(PanelWidth - 2 * Pad, RowHeight);

        // иконка результата (icon приходит из ItemIconBinder); нет иконки —
        // остаётся тёмная ячейка-подложка
        var cell = UiWidgets.Panel(rt, "IconCell", UiWidgets.BarBackColor);
        var crt0 = (RectTransform)cell.transform;
        crt0.anchorMin = crt0.anchorMax = new Vector2(0f, 0.5f);
        crt0.pivot = new Vector2(0f, 0.5f);
        crt0.anchoredPosition = new Vector2(9f, 0f);
        crt0.sizeDelta = new Vector2(44f, 44f);
        // R6: ячейка ловит луч — на ней висит тултип со статами результата
        cell.raycastTarget = true;
        var resTrig = cell.gameObject.AddComponent<ItemTooltipTrigger>();
        resTrig.GetText = () => ItemTooltipTrigger.BuildText(recipe.result);

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
            // без инвентаря (нет игрока в сцене) кнопка мертва — иначе
            // клик давал бы ложный тост «Не хватает материалов»
            row.craft.interactable = inventory != null && row.recipe.CanCraft(inventory);
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
