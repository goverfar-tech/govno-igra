using System.Collections.Generic;
using UnityEngine;

// Инвентарь: 20 слотов логики + хотбар 1–5 (аналог inventory.gd).
public class Inventory : MonoBehaviour
{
    [System.Serializable]
    public class Slot
    {
        public ItemData item;
        public int count;
        public bool IsEmpty => item == null || count <= 0;
    }

    public const int Size = 20;
    public const int HotbarSize = 5;

    public List<Slot> slots = new List<Slot>();
    public int selected;            // активный слот хотбара 0..4

    void Awake()
    {
        if (slots.Count == 0)
            for (int i = 0; i < Size; i++) slots.Add(new Slot());
    }

    // Добавить пачку; возвращает, сколько НЕ влезло.
    public int Add(ItemData item, int count)
    {
        if (item == null || count <= 0) return count;

        // 1) дозаполнить существующие стаки
        foreach (var s in slots)
        {
            if (count == 0) break;
            if (s.item == item && s.count < item.maxStack)
            {
                int move = Mathf.Min(count, item.maxStack - s.count);
                s.count += move;
                count -= move;
            }
        }
        // 2) занять пустые слоты
        foreach (var s in slots)
        {
            if (count == 0) break;
            if (s.IsEmpty)
            {
                int move = Mathf.Min(count, item.maxStack);
                s.item = item;
                s.count = move;
                count -= move;
            }
        }
        GameEvents.RaiseInventoryChanged();
        return count;
    }

    public Slot SelectedSlot => slots[selected];

    // Использовать активный предмет (съесть/выпить) — ЛКМ.
    public void UseSelected(Player player)
    {
        var s = SelectedSlot;
        if (s.IsEmpty || !s.item.IsConsumable) return;
        var item = s.item;
        player.Stats.Eat(item.foodRestore, item.waterRestore, item.healAmount);
        s.count--;
        if (s.count <= 0) { s.item = null; s.count = 0; }
        ConsumeReturns(item.consumeReturns);
        GameEvents.RaiseNotify($"Использовано: {item.displayName}");
        GameEvents.RaiseInventoryChanged();
    }

    // Вернуть предмет-остаток после использования (фляга полная → пустая).
    public void ConsumeReturns(ItemData returned)
    {
        if (returned != null) Add(returned, 1);
    }

    public void Select(int index)
    {
        index = Mathf.Clamp(index, 0, HotbarSize - 1);
        if (index == selected) return;
        selected = index;
        GameEvents.RaiseSelectionChanged();
    }
}
