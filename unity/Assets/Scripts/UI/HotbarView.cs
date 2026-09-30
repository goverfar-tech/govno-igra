using UnityEngine;
using UnityEngine.UI;

// Хотбар: 5 слотов внизу по центру. Активный слот — светлая рамка
// (Outline на подложке), количество — в правом нижнем углу,
// иконка item.icon, если назначена, иначе короткое имя предмета.
// Обновляется по событиям InventoryChanged/SelectionChanged из Hud.
public class HotbarView : MonoBehaviour
{
    public const int SlotSize = 64;
    public const int Padding = 8;

    class SlotRef
    {
        public Image icon;
        public Text name;
        public Text count;
        public Outline selection;
    }

    readonly SlotRef[] slots = new SlotRef[Inventory.HotbarSize];

    public static HotbarView Create(Transform parent)
    {
        var root = UiWidgets.Panel(parent, "Hotbar", UiWidgets.PanelColor);
        var view = root.gameObject.AddComponent<HotbarView>();

        var rt = (RectTransform)root.transform;
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 12f);
        int w = Inventory.HotbarSize * SlotSize + (Inventory.HotbarSize - 1) * Padding + Padding * 2;
        rt.sizeDelta = new Vector2(w, SlotSize + Padding * 2);

        var hlg = root.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(Padding, Padding, Padding, Padding);
        hlg.spacing = Padding;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = false;
        hlg.childControlHeight = false;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;

        for (int i = 0; i < Inventory.HotbarSize; i++)
            view.slots[i] = view.BuildSlot(root.transform, i);
        return view;
    }

    SlotRef BuildSlot(Transform parent, int index)
    {
        var back = UiWidgets.Panel(parent, "Slot" + index, UiWidgets.SlotColor);
        var rt = (RectTransform)back.transform;
        rt.sizeDelta = new Vector2(SlotSize, SlotSize);

        var r = new SlotRef();
        r.selection = back.gameObject.AddComponent<Outline>();
        r.selection.effectColor = new Color(0.85f, 0.85f, 0.8f, 0.9f);
        r.selection.effectDistance = new Vector2(1.6f, -1.6f);
        r.selection.enabled = false;

        r.icon = UiWidgets.Panel(back.transform, "Icon", Color.white);
        UiWidgets.Stretch(r.icon.rectTransform, 6f);
        r.icon.preserveAspect = true;
        r.icon.enabled = false;

        r.name = UiWidgets.Text(back.transform, "Name", 10);
        UiWidgets.Stretch(r.name.rectTransform, 3f);
        r.name.horizontalOverflow = HorizontalWrapMode.Wrap;

        r.count = UiWidgets.Text(back.transform, "Count", 14, TextAnchor.LowerRight);
        UiWidgets.Stretch(r.count.rectTransform, 4f);
        r.count.fontStyle = FontStyle.Bold;

        // клавиша слота (1..5) — в левом верхнем углу, слабо видна
        var key = UiWidgets.Text(back.transform, "Key", 10, TextAnchor.UpperLeft);
        UiWidgets.Stretch(key.rectTransform, 5f, 3f);
        key.text = (index + 1).ToString();
        key.color = new Color(1f, 1f, 1f, 0.35f);
        return r;
    }

    public void Refresh(Inventory inv)
    {
        if (inv == null) return;
        for (int i = 0; i < slots.Length; i++)
        {
            var s = inv.slots[i];
            var v = slots[i];
            bool empty = s.IsEmpty;
            v.count.text = !empty && s.count > 1 ? s.count.ToString() : "";
            if (!empty && s.item.icon != null)
            {
                v.icon.sprite = s.item.icon;
                v.icon.enabled = true;
                v.name.text = "";
            }
            else
            {
                v.icon.enabled = false;
                v.icon.sprite = null;
                v.name.text = empty ? "" : s.item.displayName;
            }
            v.selection.enabled = i == inv.selected;
        }
    }
}
