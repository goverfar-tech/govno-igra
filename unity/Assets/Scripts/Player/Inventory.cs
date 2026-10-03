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

    // Список слотов фиксированного размера, ленивое дозаполнение.
    // Сцена сериализует список пустым (игрок собран сетапом, а наш
    // Awake ещё не жил), при этом Hud обновляется в СВОЁМ Awake и
    // читает slots раньше — без дозаполнения ловили IndexOutOfRange,
    // из-за которого умирал весь UI (2026-10-03).
    [SerializeField, UnityEngine.Serialization.FormerlySerializedAs("slots")]
    List<Slot> _slots = new List<Slot>();
    public List<Slot> slots
    {
        get
        {
            while (_slots.Count < Size) _slots.Add(new Slot());
            return _slots;
        }
        set => _slots = value;
    }
    public int selected;            // активный слот хотбара 0..4
    public PickupItem pickupPrefab; // чем выбрасываем предметы в мир

    void Awake()
    {
        var _ = slots.Count; // гарантируем заполнение и для внутренних обходов
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
        // §9.2: еда, лечение и «вода» слились в майонез; фляга сырого
        // майонеза питательна наполовину против еды.
        player.Stats.Feed(item.foodRestore + item.healAmount + item.waterRestore * 0.5f,
                          item.poisonAmount);
        s.count--;
        if (s.count <= 0) { s.item = null; s.count = 0; }
        ConsumeReturns(item.consumeReturns);
        GameEvents.RaiseItemConsumed(item); // звук еды/питья (шина → AudioManager)
        GameEvents.RaiseNotify($"Использовано: {item.displayName}");
        GameEvents.RaiseInventoryChanged();
    }

    // Вернуть предмет-остаток после использования (фляга полная → пустая).
    // Полный инвентарь не должен съедать возврат — бросаем под ноги.
    public void ConsumeReturns(ItemData returned)
    {
        if (returned == null) return;
        int leftover = Add(returned, 1);
        if (leftover > 0) DropAtFeet(returned, leftover);
    }

    // Сколько штук предмета есть суммарно по всем слотам.
    public int CountOf(ItemData item)
    {
        int n = 0;
        foreach (var s in slots)
            if (!s.IsEmpty && s.item == item) n += s.count;
        return n;
    }

    // Убрать из инвентаря count штук предмета; false — если столько нет.
    // Атомарно: при нехватке НИЧЕГО не списывается и события нет.
    public bool RemoveItem(ItemData item, int count)
    {
        if (item == null || count <= 0) return false;
        if (CountOf(item) < count) return false;
        foreach (var s in slots)
        {
            if (count == 0) break;
            if (!s.IsEmpty && s.item == item)
            {
                int take = Mathf.Min(count, s.count);
                s.count -= take;
                count -= take;
                if (s.count <= 0) { s.item = null; s.count = 0; }
            }
        }
        GameEvents.RaiseInventoryChanged();
        return true;
    }

    // Выбросить пачку пикапом у ног владельца (ПКМ-дроп, переполнение
    // крафта/фляг). Пикап сам найдёт землю лучом вниз (см. PickupItem).
    public void DropAtFeet(ItemData item, int count)
    {
        if (item == null || count <= 0 || pickupPrefab == null) return;
        var drop = Instantiate(pickupPrefab,
            transform.position + transform.forward * 1.2f + Vector3.up * 0.3f,
            Quaternion.identity);
        drop.item = item;
        drop.count = count;
    }

    // Выбросить пачку из активного слота перед игроком (ПКМ).
    public void DropSelected(Player player)
    {
        var s = SelectedSlot;
        if (s.IsEmpty || pickupPrefab == null || player == null) return;
        DropAtFeet(s.item, s.count);
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
        int oldSelected = selected;
        if (selected == from && (!merge || slots[from].IsEmpty))
            selected = to;
        // утащили активный слот в рюкзак (вне хотбара)? выбор остаётся
        // в пределах хотбара, иначе selected указывал бы мимо руки
        selected = Mathf.Clamp(selected, 0, HotbarSize - 1);
        if (selected != oldSelected) GameEvents.RaiseSelectionChanged();
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
