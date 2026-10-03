using System.Collections.Generic;
using UnityEngine;

// Обыскываемый лут-объект городка (объекты ставит Setup, имена «Loot_*»).
// Обыскывается ОДИН раз за жизнь мира: обысканные помнятся по ИМЕНИ
// GameObject'а в статическом множестве — имена детерминированы Setup'ом,
// поэтому переживают перезагрузку сцены и восстанавливаются сейвом
// (SaveSystem: Restore/Capture). Лут-таблицы (loot/rolls) заполняет
// Setup — здесь только механика.
[RequireComponent(typeof(Collider))] // луч взаимодействия должен во что-то попадать
public class LootContainer : MonoBehaviour, IInteractable
{
    [Tooltip("Из чего рандомится выпадающее")]
    public ItemData[] loot;
    [Min(1)] public int rolls = 2;

    // Имена обысканных контейнеров. Статика переживает перезагрузку сцены
    // сознательно (мир при рестарте не пересоздаётся, файл-сейв остаётся —
    // принятое в проекте поведение); сбрасываем только на старте сессии
    // при выключенном domain reload, как у Player.lastCampfirePos.
    static readonly HashSet<string> Looted = new HashSet<string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Looted.Clear();

    // --- сейв (SaveSystem): имена детерминированы, не найденные мир молча игнорит ---

    public static void Restore(List<string> names)
    {
        Looted.Clear();
        if (names == null) return;
        foreach (var n in names)
            if (!string.IsNullOrEmpty(n)) Looted.Add(n);
    }

    public static List<string> Capture() => new List<string>(Looted);

    public string GetPrompt()
        => Looted.Contains(gameObject.name) ? "Пусто" : "[E] Обыскать";

    public void Interact(Player player)
    {
        if (player == null || Looted.Contains(gameObject.name)) return;
        // обыск засчитываем сразу: даже пустая таблица опустошает объект
        Looted.Add(gameObject.name);

        // rolls раз кидаем кости по таблице; выпавшее агрегируем по предметам,
        // чтобы тост был один («Нашёл: Древесина ×3»), а не на каждый ролл
        var found = new Dictionary<ItemData, int>();
        if (loot != null && loot.Length > 0) // Range(0,0) вернул бы индекс 0 мимо пустого массива
            for (int i = 0; i < Mathf.Max(0, rolls); i++)
            {
                var item = loot[Random.Range(0, loot.Length)];
                if (item == null) continue; // в таблице бывают дырки
                found.TryGetValue(item, out int have);
                found[item] = have + 1;
            }

        if (found.Count == 0)
        {
            GameEvents.RaiseNotify("Внутри пусто…");
            return;
        }

        // Не влезло — дроп под ноги через pickupPrefab (тот же паттерн,
        // что в Inventory.ConsumeReturns у крафта/фляг).
        var parts = new List<string>();
        foreach (var kv in found)
        {
            parts.Add($"{kv.Key.displayName} ×{kv.Value}");
            int leftover = player.Inventory.Add(kv.Key, kv.Value);
            if (leftover > 0) player.Inventory.DropAtFeet(kv.Key, leftover);
        }
        GameEvents.RaiseNotify("Нашёл: " + string.Join(", ", parts));
    }
}
