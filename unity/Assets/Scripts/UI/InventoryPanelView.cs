using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Панель инвентаря по Tab: заголовок, сетка 5x4 (20 слотов), подсказка.
// Драг-н-дроп слотов через InventorySlotView; перестановка и слияние
// стаков делает Inventory.MoveOrMerge. Инвентарь — единственный
// источник истины, панель только перерисовывается по InventoryChanged.
public class InventoryPanelView : MonoBehaviour
{
    const int Cell = 64;
    const int Gap = 8;
    const int Cols = 5;

    InventorySlotView[] slots = new InventorySlotView[Inventory.Size];
    Inventory inventory;

    // состояние текущего драга
    int dragFrom = -1;
    RectTransform dragGhost;
    InventorySlotView dragSource;

    public static InventoryPanelView Create(Transform parent, Inventory inv)
    {
        var back = UiWidgets.Panel(parent, "InventoryPanel", UiWidgets.PanelColor);
        var view = back.gameObject.AddComponent<InventoryPanelView>();
        view.inventory = inv;

        var rt = (RectTransform)back.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        float width = Cols * Cell + (Cols - 1) * Gap + 2 * 14f;
        float height = 34f + 4 * Cell + 3 * Gap + 40f + 2 * 12f;
        rt.sizeDelta = new Vector2(width, height);

        var title = UiWidgets.Text(back.transform, "Title", 18);
        var trt = (RectTransform)title.transform;
        trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 1f);
        trt.pivot = new Vector2(0.5f, 1f);
        trt.anchoredPosition = new Vector2(0f, -10f);
        trt.sizeDelta = new Vector2(width, 22f);
        title.text = "ИНВЕНТАРЬ";

        var gridGo = new GameObject("Grid", typeof(RectTransform), typeof(GridLayoutGroup));
        gridGo.transform.SetParent(back.transform, false);
        var grt = (RectTransform)gridGo.transform;
        grt.anchorMin = grt.anchorMax = new Vector2(0.5f, 1f);
        grt.pivot = new Vector2(0.5f, 1f);
        grt.anchoredPosition = new Vector2(0f, -40f);
        grt.sizeDelta = new Vector2(Cols * Cell + (Cols - 1) * Gap, 4 * Cell + 3 * Gap);
        var grid = gridGo.GetComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(Cell, Cell);
        grid.spacing = new Vector2(Gap, Gap);
        grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
        grid.startAxis = GridLayoutGroup.Axis.Horizontal;
        grid.childAlignment = TextAnchor.UpperCenter;
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = Cols;

        for (int i = 0; i < Inventory.Size; i++)
            view.slots[i] = InventorySlotView.Create(grt, view, i);

        var hint = UiWidgets.Text(back.transform, "Hint", 12);
        var hrt = (RectTransform)hint.transform;
        hrt.anchorMin = hrt.anchorMax = new Vector2(0.5f, 0f);
        hrt.pivot = new Vector2(0.5f, 0f);
        hrt.anchoredPosition = new Vector2(0f, 10f);
        hrt.sizeDelta = new Vector2(width, 30f);
        hint.text = "ЛКМ — перетащить  •  Tab/Esc — закрыть  •  ПКМ (в мире) — выбросить";
        hint.color = new Color(1f, 1f, 1f, 0.45f);

        back.gameObject.SetActive(false);
        return view;
    }

    public void SetInventory(Inventory inv)
    {
        inventory = inv;
        Refresh(inv);
    }

    public void Refresh(Inventory inv)
    {
        if (inv != null) inventory = inv;
        if (inventory == null) return;
        for (int i = 0; i < slots.Length; i++)
            slots[i].Refresh(inventory.slots[i], i == inventory.selected);
    }

    // ---- драг-н-дроп ----

    internal void SlotDragBegin(int index, InventorySlotView source)
    {
        if (inventory == null || inventory.slots[index].IsEmpty) return;
        dragFrom = index;
        dragSource = source;
        source.SetRaycasts(false);

        // призрак за курсором: полупрозрачный квадрат с именем предмета
        var ghost = UiWidgets.Panel(transform.parent, "DragGhost",
            new Color(0.12f, 0.13f, 0.14f, 0.75f));
        dragGhost = (RectTransform)ghost.transform;
        dragGhost.sizeDelta = new Vector2(Cell - 8f, Cell - 8f);
        ghost.raycastTarget = false;
        var g = ghost.gameObject.AddComponent<CanvasGroup>();
        g.blocksRaycasts = false;
        g.interactable = false;

        var slot = inventory.slots[index];
        if (slot.item.icon != null)
        {
            var ic = UiWidgets.Panel(dragGhost, "Icon", Color.white);
            UiWidgets.Stretch(ic.rectTransform, 4f);
            ic.sprite = slot.item.icon;
            ic.preserveAspect = true;
            ic.raycastTarget = false;
        }
        else
        {
            var t = UiWidgets.Text(dragGhost, "Name", 10);
            UiWidgets.Stretch(t.rectTransform, 3f);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.text = slot.item.displayName;
        }
        dragGhost.SetAsLastSibling();
    }

    internal void SlotDragMove(PointerEventData e)
    {
        if (dragGhost == null) return;
        // overlay-canvas: экранные координаты = мировые
        dragGhost.position = e.position;
    }

    internal void SlotDragEnd()
    {
        if (dragSource != null) dragSource.SetRaycasts(true);
        dragSource = null;
        dragFrom = -1;
        if (dragGhost != null) Destroy(dragGhost.gameObject);
        dragGhost = null;
    }

    internal void SlotDropOn(int index)
    {
        if (inventory == null || dragFrom < 0 || dragFrom == index) return;
        inventory.MoveOrMerge(dragFrom, index);
    }
}
