using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Свободные сейвы (§9.5): F5 — сохранить, F9 — загрузить.
// JSON один слот в persistentDataPath. Сохраняет: позицию/взгляд игрока,
// статы, инвентарь, постройки (Placed), время суток.
// НЕ сохраняет пока: состояние источников (добытые деревья), дроп на земле.
// Появляется сам при старте любой сцены.
public class SaveSystem : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (FindFirstObjectByType<SaveSystem>() == null)
            new GameObject("SaveSystem").AddComponent<SaveSystem>();
    }

    [System.Serializable] class SlotData { public string item; public int count; }
    [System.Serializable] class PlacedData { public string item; public Vector3 pos; public float rotY; }

    [System.Serializable]
    class SaveData
    {
        public Vector3 playerPos;
        public float yaw, pitch;
        public float hp, food, water;
        public int selected;
        public float timeOfDay;
        public List<SlotData> inventory = new List<SlotData>();
        public List<PlacedData> placed = new List<PlacedData>();
    }

    static string SavePath => Path.Combine(Application.persistentDataPath, "save.json");

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
        var player = FindFirstObjectByType<Player>();
        if (player == null || player.Stats.IsDead) return;

        var data = new SaveData
        {
            playerPos = player.transform.position,
            yaw = player.transform.eulerAngles.y,
            pitch = player.Pitch,
            hp = player.Stats.Hp,
            food = player.Stats.Food,
            water = player.Stats.Water,
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

        File.WriteAllText(SavePath, JsonUtility.ToJson(data, true));
        GameEvents.RaiseNotify($"Сохранено (всего построек: {data.placed.Count})");
    }

    void Load()
    {
        if (!File.Exists(SavePath))
        {
            GameEvents.RaiseNotify("Нет сохранения");
            return;
        }
        var data = JsonUtility.FromJson<SaveData>(File.ReadAllText(SavePath));
        var player = FindFirstObjectByType<Player>();
        if (player == null) return;

        // постройки: старые убрать, сохранённые вернуть
        foreach (var p in FindObjectsByType<Placed>(FindObjectsSortMode.None))
            Destroy(p.gameObject);
        foreach (var pd in data.placed)
        {
            if (!Catalog.TryGetValue(pd.item, out var item) || item.placeablePrefab == null) continue;
            var go = Instantiate(item.placeablePrefab, pd.pos, Quaternion.Euler(0f, pd.rotY, 0f));
            go.name = item.displayName;
            var tag = go.GetComponent<Placed>();
            if (tag == null) tag = go.AddComponent<Placed>();
            tag.itemId = pd.item;
        }

        // игрок: позиция, взгляд, статы, инвентарь
        player.Teleport(data.playerPos);
        player.ApplyView(data.yaw, data.pitch);
        player.Stats.SetState(data.hp, data.food, data.water);

        for (int i = 0; i < player.Inventory.slots.Count && i < data.inventory.Count; i++)
        {
            var sd = data.inventory[i];
            var slot = player.Inventory.slots[i];
            if (!string.IsNullOrEmpty(sd.item) && Catalog.TryGetValue(sd.item, out var item))
            {
                slot.item = item;
                slot.count = sd.count;
            }
            else { slot.item = null; slot.count = 0; }
        }
        player.Inventory.Select(data.selected);
        GameEvents.RaiseInventoryChanged();

        var dayNight = FindFirstObjectByType<DayNight>();
        if (dayNight != null) dayNight.timeOfDay = data.timeOfDay;

        GameEvents.RaiseNotify("Загружено");
    }

    // Публичный фасад для UI (главное меню, Поток B): та же логика, что у F9.
    // Возвращает false, если сейва нет — меню остаётся открытым (тост уже показан).
    public bool LoadGame()
    {
        if (!File.Exists(SavePath))
        {
            GameEvents.RaiseNotify("Нет сохранения");
            return false;
        }
        Load();
        return true;
    }
}
