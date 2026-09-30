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
    public PickupItem pickupPrefab; // чем выбрасываем предметы в мир

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

    // Убрать из инвентаря count штук предмета; false — если столько нет.
    public bool RemoveItem(ItemData item, int count)
    {
        if (item == null || count <= 0) return false;
        foreach (var s in slots)
            if (!s.IsEmpty && s.item == item)
            {
                int take = Mathf.Min(count, s.count);
                s.count -= take;
                count -= take;
                if (s.count <= 0) { s.item = null; s.count = 0; }
                if (count == 0)
                {
                    GameEvents.RaiseInventoryChanged();
                    return true;
                }
            }
        return false;
    }

    // Выбросить пачку из активного слота перед игроком (ПКМ).
    public void DropSelected(Player player)
    {
        var s = SelectedSlot;
        if (s.IsEmpty || pickupPrefab == null || player == null) return;
        var drop = Instantiate(pickupPrefab,
            player.transform.position + player.transform.forward * 1.2f + Vector3.up * 0.3f,
            Quaternion.identity);
        drop.item = s.item;
        drop.count = s.count;
        s.item = null;
        s.count = 0;
        GameEvents.RaiseInventoryChanged();
    }

    // Перенос слота: одноимённые домерживаются до maxStack,
    // разные — свапаются. Пустая цель просто принимает всё.
    public void MoveOrMerge(int from, int to)
    {
        if (from == to) return;
        var a = slots[from];
        var b = slots[to];
        bool merge = !b.IsEmpty && a.item == b.item;

        if (merge)
        {
            int move = Mathf.Min(a.count, a.item.maxStack - b.count);
            b.count += move;
            a.count -= move;
            if (a.count <= 0) { a.item = null; a.count = 0; }
        }
        else
        {
            (a.item, b.item) = (b.item, a.item);
            (a.count, b.count) = (b.count, a.count);
        }

        // активность хотбара переезжает вместе с вещами
        // (при частичном мерже хвост остаётся на исходном слоте)
        if (selected == from && (!merge || slots[from].IsEmpty))
        {
            selected = to;
            GameEvents.RaiseSelectionChanged();
        }
        GameEvents.RaiseInventoryChanged();
    }

    public void Select(int index)
    {
        index = Mathf.Clamp(index, 0, HotbarSize - 1);
        if (index == selected) return;
        selected = index;
        GameEvents.RaiseSelectionChanged();
    }
}
