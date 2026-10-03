using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Свободные сейвы (§9.5): F5 — сохранить, F9 — загрузить.
// JSON один слот в persistentDataPath. Сохраняет: позицию/взгляд игрока,
// статы, инвентарь, постройки (Placed), время суток, состояние узлов
// добычи (остаток ударов) и кулдаун лужи.
// НЕ сохраняет пока: дроп на земле (пикапы/яйца).
// Появляется сам при старте любой сцены; синглтон с DontDestroyOnLoad —
// переживает перезагрузку сцены (смерть → «Начать заново»).
public class SaveSystem : MonoBehaviour
{
    public static SaveSystem Instance { get; private set; }

    // Сброс статики при входе в Play-режим / на старте билда:
    // иначе при выключенном domain reload Instance и кэш-каталог
    // переживают запуск и ссылаются на мёртвые объекты прошлой сессии.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Instance = null;
        catalog = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (FindFirstObjectByType<SaveSystem>() == null)
            new GameObject("SaveSystem").AddComponent<SaveSystem>();
    }

    void Awake()
    {
        // Дублёр после перезагрузки сцены — убить, иначе F5/F9 делает
        // чужой экземпляр или не делает никто.
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    [System.Serializable] class SlotData { public string item; public int count; }
    [System.Serializable] class PlacedData { public string item; public Vector3 pos; public float rotY; }
    [System.Serializable] class NodeData { public string name; public int hits; }
    [System.Serializable] class WaterData { public string name; public float cooldown; }

    [System.Serializable]
    class SaveData
    {
        // НЕ инициализировать: пустой {} должен давать 0 (не пройдёт проверку версии).
        // 2 = эпоха майонеза (hp/food/water → mayo/poison).
        public int version;
        public Vector3 playerPos;
        public float yaw, pitch;
        public float mayo, poison;
        public int selected;
        public float timeOfDay;
        public List<SlotData> inventory = new List<SlotData>();
        public List<PlacedData> placed = new List<PlacedData>();
        public List<NodeData> nodes = new List<NodeData>();
        public List<WaterData> waters = new List<WaterData>();
    }

    static string SavePath => Path.Combine(Application.persistentDataPath, "save.json");

    // Есть ли вообще файл сохранения (меню «Продолжить» сереет без него).
    public static bool HasSave => File.Exists(SavePath);

    static Dictionary<string, ItemData> catalog;
    static Dictionary<string, ItemData> Catalog
    {
        get
        {
            if (catalog == null)
            {
                catalog = new Dictionary<string, ItemData>();
                foreach (var it in Resources.LoadAll<ItemData>("Items"))
                    if (!string.IsNullOrEmpty(it.id)) catalog[it.id] = it;
            }
            return catalog;
        }
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F5)) Save();
        if (Input.GetKeyDown(KeyCode.F9)) Load();
    }

    void Save()
    {
        // Меню/пауза (timeScale == 0): сохранение «в стоячем мире» только путает.
        if (Time.timeScale <= 0f)
        {
            GameEvents.RaiseNotify("Сначала вернитесь в игру");
            return;
        }

        var player = FindFirstObjectByType<Player>();
        if (player == null) return;
        if (player.Stats.IsDead)
        {
            GameEvents.RaiseNotify("Нельзя сохранить мёртвым");
            return;
        }

        var data = new SaveData
        {
            version = 2,
            playerPos = player.transform.position,
            yaw = player.transform.eulerAngles.y,
            pitch = player.Pitch,
            mayo = player.Stats.Mayo,
            poison = player.Stats.Poison,
            selected = player.Inventory.selected
        };

        foreach (var s in player.Inventory.slots)
            data.inventory.Add(new SlotData { item = s.IsEmpty ? "" : s.item.id, count = s.count });

        var dayNight = FindFirstObjectByType<DayNight>();
        if (dayNight != null) data.timeOfDay = dayNight.timeOfDay;

        foreach (var p in FindObjectsByType<Placed>(FindObjectsSortMode.None))
            data.placed.Add(new PlacedData
            {
                item = p.itemId,
                pos = p.transform.position,
                rotY = p.transform.eulerAngles.y
            });

        // Узлы добычи: детерминированные имена из SetupMainScene (Tree0.., Rock0..).
        foreach (var n in FindObjectsByType<ResourceNode>(FindObjectsSortMode.None))
            data.nodes.Add(new NodeData { name = n.name, hits = n.hitsLeft });

        // Лужи: остаток «меления» в секундах.
        foreach (var w in FindObjectsByType<WaterSource>(FindObjectsSortMode.None))
            data.waters.Add(new WaterData { name = w.name, cooldown = w.RemainingCooldown });

        try
        {
            File.WriteAllText(SavePath, JsonUtility.ToJson(data, true));
        }
        catch (IOException e)
        {
            Debug.LogWarning("SaveSystem: не удалось записать сейв: " + e.Message);
            GameEvents.RaiseNotify("Не удалось сохранить сейв");
            return;
        }
        GameEvents.RaiseNotify($"Сохранено (всего построек: {data.placed.Count})");
    }

    // false — загрузка не состоялась (нет файла/битый/старая версия/
    // нет игрока): нужно, чтобы меню «Продолжить» не закрывалось впустую.
    bool Load()
    {
        if (!File.Exists(SavePath))
        {
            GameEvents.RaiseNotify("Нет сохранения");
            return false;
        }

        string json;
        try
        {
            json = File.ReadAllText(SavePath);
        }
        catch (IOException e)
        {
            Debug.LogWarning("SaveSystem: не удалось прочитать сейв: " + e.Message);
            GameEvents.RaiseNotify("Не удалось прочитать сейв");
            return false;
        }

        SaveData data = null;
        try
        {
            data = JsonUtility.FromJson<SaveData>(json);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("SaveSystem: битый JSON: " + e.Message);
        }
        if (data == null)
        {
            GameEvents.RaiseNotify("Сейв повреждён");
            return false;
        }
        if (data.version != 2)
        {
            GameEvents.RaiseNotify("Сейв старой версии — не встаёт, начни заново");
            return false;
        }
        var player = FindFirstObjectByType<Player>();
        if (player == null) return false;

        // id предметов, исчезнувших из проекта со времён сейва — один тост в конце.
        var lost = new List<string>();
        void MarkLost(string id)
        {
            if (!string.IsNullOrEmpty(id) && !lost.Contains(id)) lost.Add(id);
        }

        // Ночную стаю НЕ снимаем (первый вариант с Destroy давал
        // «сохранился-загрузился = исчезли зомби»): игрок телепортируется,
        // зомби теряют его из виду (LOS) и разбредаются сами — дневной
        // рассвет дочистит. ZombieSpawner.alive почистится на след. ночи.

        // постройки: старые убрать, сохранённые вернуть
        foreach (var p in FindObjectsByType<Placed>(FindObjectsSortMode.None))
            Destroy(p.gameObject);
        foreach (var pd in data.placed)
        {
            if (!Catalog.TryGetValue(pd.item, out var item) || item.placeablePrefab == null)
            {
                MarkLost(pd.item);
                continue;
            }
            var go = Instantiate(item.placeablePrefab, pd.pos, Quaternion.Euler(0f, pd.rotY, 0f));
            go.name = item.displayName;
            var tag = go.GetComponent<Placed>();
            if (tag == null) tag = go.AddComponent<Placed>();
            tag.itemId = pd.item;
        }

        // игрок: позиция, взгляд, статы, инвентарь.
        // SetState сам сбрасывает IsDead и шлёт StatsChanged — загрузка = оживление.
        // Майонез клампим минимум 1: мусорный ноль не должен убивать в момент загрузки.
        player.Teleport(data.playerPos);
        // Старые сейвы с квадратной карты 200×200 могли лечь в новый океан:
        // загрузка в протухший майонез — смерть до первого кадра. Выносим
        // ведро на плато спауна (формат сейва не меняется).
        if (TerrainGen.IsInOcean(player.transform.position))
        {
            player.Teleport(new Vector3(0f, TerrainGen.HeightAt(0f, 0f) + 1.1f, 0f));
            GameEvents.RaiseNotify("Сейв был в океане — ведро вынесло на берег.");
        }
        player.ApplyView(data.yaw, data.pitch);
        player.Stats.SetState(Mathf.Max(1f, data.mayo), data.poison);

        for (int i = 0; i < player.Inventory.slots.Count && i < data.inventory.Count; i++)
        {
            var sd = data.inventory[i];
            var slot = player.Inventory.slots[i];
            if (!string.IsNullOrEmpty(sd.item))
            {
                if (Catalog.TryGetValue(sd.item, out var item))
                {
                    slot.item = item;
                    slot.count = Mathf.Clamp(sd.count, 1, item.maxStack);
                }
                else
                {
                    MarkLost(sd.item);
                    slot.item = null; slot.count = 0;
                }
            }
            else { slot.item = null; slot.count = 0; }
        }
        player.Inventory.Select(data.selected);
        GameEvents.RaiseInventoryChanged();

        // узлы добычи: восстановить остаток ударов по имени
        // (имена детерминированы SetupMainScene; не найденные — мир
        // перегенерился, молча пропускаем)
        var nodeByName = new Dictionary<string, NodeData>();
        foreach (var nd in data.nodes)
            if (!string.IsNullOrEmpty(nd.name)) nodeByName[nd.name] = nd;
        foreach (var n in FindObjectsByType<ResourceNode>(FindObjectsSortMode.None))
            if (nodeByName.TryGetValue(n.name, out var nd))
                n.RestoreHitsLeft(nd.hits);

        // лужи: остаток кулдауна по имени
        var waterByName = new Dictionary<string, WaterData>();
        foreach (var wd in data.waters)
            if (!string.IsNullOrEmpty(wd.name)) waterByName[wd.name] = wd;
        foreach (var w in FindObjectsByType<WaterSource>(FindObjectsSortMode.None))
            if (waterByName.TryGetValue(w.name, out var wd))
                w.RemainingCooldown = wd.cooldown;

        var dayNight = FindFirstObjectByType<DayNight>();
        if (dayNight != null) dayNight.timeOfDay = data.timeOfDay;

        if (lost.Count > 0)
            GameEvents.RaiseNotify("Утеряно при загрузке: " + string.Join(", ", lost));
        GameEvents.RaiseNotify("Загружено");
        return true;
    }

    // Публичный фасад для UI (главное меню, Поток B): та же логика, что у F9.
    // Возвращает false при любой неудаче — меню остаётся открытым
    // (конкретный тост уже показан Load'ом).
    public bool LoadGame() => Load();
}
