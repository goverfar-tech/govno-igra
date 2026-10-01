using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Слот панели инвентаря (M3). Драг-н-дроп: OnBeginDrag/OnDrag/OnEndDrag
// ведут «призрак» за курсором, OnDrop на целевом слоте вызывает
// Inventory.MoveOrMerge(from, to) — слияние/свап внутри логики инвентаря.
// Пока драг идёт, CanvasGroup исходного слота не ловит луч — иначе
// OnDrop приходит самому себе.
public class InventorySlotView : MonoBehaviour,
    IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
{
    public InventoryPanelView Panel { get; private set; }
    public int Index { get; private set; }

    Image icon;
    Text nameText;
    Text countText;
    Outline selection;
    CanvasGroup group;
    ItemData shownItem; // что сейчас нарисовано — для тултипа

    public static InventorySlotView Create(Transform parent, InventoryPanelView panel, int index)
    {
        var back = UiWidgets.Panel(parent, "Slot" + index, UiWidgets.SlotColor);
        var slot = back.gameObject.AddComponent<InventorySlotView>();
        slot.Panel = panel;
        slot.Index = index;
        slot.group = back.gameObject.AddComponent<CanvasGroup>();

        slot.selection = back.gameObject.AddComponent<Outline>();
        slot.selection.effectColor = new Color(0.85f, 0.85f, 0.8f, 0.9f);
        slot.selection.effectDistance = new Vector2(1.6f, -1.6f);
        slot.selection.enabled = false;

        slot.icon = UiWidgets.Panel(back.transform, "Icon", Color.white);
        UiWidgets.Stretch(slot.icon.rectTransform, 6f);
        slot.icon.preserveAspect = true;
        slot.icon.raycastTarget = false;
        slot.icon.enabled = false;

        slot.nameText = UiWidgets.Text(back.transform, "Name", 10);
        UiWidgets.Stretch(slot.nameText.rectTransform, 3f);
        slot.nameText.horizontalOverflow = HorizontalWrapMode.Wrap;

        slot.countText = UiWidgets.Text(back.transform, "Count", 14, TextAnchor.LowerRight);
        UiWidgets.Stretch(slot.countText.rectTransform, 4f);
        slot.countText.fontStyle = FontStyle.Bold;

        var trig = back.gameObject.AddComponent<ItemTooltipTrigger>();
        trig.GetText = () => slot.shownItem != null ? slot.shownItem.displayName : null;
        return slot;
    }

    public void Refresh(Inventory.Slot s, bool selected)
    {
        selection.enabled = selected;
        if (s == null || s.IsEmpty)
        {
            shownItem = null;
            icon.enabled = false;
            icon.sprite = null;
            nameText.text = "";
            countText.text = "";
            return;
        }
        shownItem = s.item;
        if (s.item.icon != null)
        {
            icon.sprite = s.item.icon;
            icon.enabled = true;
            nameText.text = "";
        }
        else
        {
            icon.enabled = false;
            icon.sprite = null;
            nameText.text = s.item.displayName;
        }
        countText.text = s.count > 1 ? s.count.ToString() : "";
    }

    public void OnBeginDrag(PointerEventData e) => Panel.SlotDragBegin(Index, this);
    public void OnDrag(PointerEventData e) => Panel.SlotDragMove(e);
    public void OnEndDrag(PointerEventData e) => Panel.SlotDragEnd();
    public void OnDrop(PointerEventData e) => Panel.SlotDropOn(Index);

    // Панель гасит лучи исходного слота на время драга
    internal void SetRaycasts(bool on) => group.blocksRaycasts = on;
}
