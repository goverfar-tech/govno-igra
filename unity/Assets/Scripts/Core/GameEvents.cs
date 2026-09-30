using System;

// Шина событий проекта (аналог SignalHub из Godot-версии).
// Системы общаются только через неё, в чужие компоненты напрямую не лезут.
public static class GameEvents
{
    // Подсказка взаимодействия под прицелом (null — скрыть)
    public static event Action<string> PromptChanged;
    // Всплывающий тост ("Наступила ночь", "Инвентарь полон"…)
    public static event Action<string> Notify;
    public static event Action InventoryChanged;
    // Изменился активный слот хотбара
    public static event Action SelectionChanged;
    public static event Action<bool> InventoryOpenChanged;
    public static event Action StatsChanged;
    public static event Action PlayerDied;
    // 0..1 — доля суток; bool — наступила ли ночь
    public static event Action<float, bool> TimeOfDayChanged;

    public static void RaisePromptChanged(string prompt) => PromptChanged?.Invoke(prompt);
    public static void RaiseNotify(string text) => Notify?.Invoke(text);
    public static void RaiseInventoryChanged() => InventoryChanged?.Invoke();
    public static void RaiseSelectionChanged() => SelectionChanged?.Invoke();
    public static void RaiseInventoryOpenChanged(bool open) => InventoryOpenChanged?.Invoke(open);
    public static void RaiseStatsChanged() => StatsChanged?.Invoke();
    public static void RaisePlayerDied() => PlayerDied?.Invoke();
    public static void RaiseTimeOfDayChanged(float t, bool isNight) => TimeOfDayChanged?.Invoke(t, isNight);

    // Добавлено на M3 (HUD):
    // Пауза по Esc: true — игра на паузе (Time.timeScale == 0)
    public static event Action<bool> PauseChanged;
    public static void RaisePauseChanged(bool paused) => PauseChanged?.Invoke(paused);

    // --- Аудио-хуки (добавлено Потоком Б «звук»; новые события — только сюда, в конец) ---
    // Израсходован предмет (еда/питьё) — звук потребления.
    // Поднимать там, где предмет тратится (Inventory.UseSelected).
    public static event Action<ItemData> ItemConsumed;
    // Удар инструментом по мировому объекту (добыча/бой) — 3D-«тук» в точке.
    public static event Action<UnityEngine.Vector3> WorldHit;

    public static void RaiseItemConsumed(ItemData item) => ItemConsumed?.Invoke(item);
    public static void RaiseWorldHit(UnityEngine.Vector3 pos) => WorldHit?.Invoke(pos);
}
