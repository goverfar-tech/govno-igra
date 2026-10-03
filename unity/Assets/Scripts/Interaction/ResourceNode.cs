using UnityEngine;

// Добываемый источник ресурса: дерево / камень-жилка (3 удара).
[RequireComponent(typeof(Collider))]
public class ResourceNode : MonoBehaviour, IInteractable
{
    public ItemData yield;          // что выпадает
    [Min(1)] public int hitsLeft = 3;
    [Min(1)] public int yieldPerHit = 1;
    public PickupItem pickupPrefab; // что спавним при добыче
    // Каким инструментом рубить быстрее ("" = любым). Дерево = axe,
    // камень = pickaxe (эталон: resource_node.gd tool_id).
    public string requiredToolId = "";

    int maxHits;      // исходный лимит из сцены — верхняя граница для сейвов
    bool greyedOut;   // посерение одноразовое: обратной раскраски нет

    void Awake()
    {
        maxHits = hitsLeft;
        if (hitsLeft <= 0) GreyOut(); // поднятый из сейва уже обобранным
    }

    public string GetPrompt()
    {
        if (yield == null) return "[E] Добыть";
        if (hitsLeft <= 0) return "Обобрано"; // дерево/камень остаются навсегда
        return $"[E] Добыть: {yield.displayName} (осталось: {hitsLeft})";
    }

    public void Interact(Player player) => Hit(player, 1);

    // Сбор руками: limit исчерпуем, но узел НЕ исчезает (§аудит:
    // инструменты вырезаны — дерево просто обдирается).
    public void Hit(Player player, int amount)
    {
        if (yield == null || player == null || hitsLeft <= 0) return;
        hitsLeft -= amount;

        // Добыча сразу в инвентарь; не влезло — падает пикапом перед игроком
        int leftover = player.Inventory.Add(yield, yieldPerHit * amount);
        if (leftover > 0 && pickupPrefab != null)
        {
            var drop = Instantiate(pickupPrefab,
                player.transform.position + player.transform.forward * 1.2f,
                Quaternion.identity);
            drop.item = yield;
            drop.count = leftover;
        }

        if (hitsLeft <= 0)
        {
            hitsLeft = 0;
            GameEvents.RaiseNotify("Обобрано: " + (yield != null ? yield.displayName : ""));
            GreyOut(); // визуально пустой — читается издалека (R3)
        }
    }

    // Восстановление из сейва (SaveSystem): остаток ударов, кламп
    // 0..исходный максимум; визуал исчерпания — тот же, что при добыче.
    public void RestoreHitsLeft(int hits)
    {
        hitsLeft = Mathf.Clamp(hits, 0, maxHits);
        if (hitsLeft <= 0) GreyOut();
    }

    // Перекрашиваем модель в тускло-серый. GLB-материалы одни на все
    // инстансы этого глба — поэтому ДЕЛАЕМ КОПИИ (r.material), иначе
    // посерели бы все деревья разом.
    void GreyOut()
    {
        if (greyedOut) return;
        greyedOut = true;
        foreach (var r in GetComponentsInChildren<MeshRenderer>())
        {
            var mats = r.materials; // инстанцирует копии
            for (int i = 0; i < mats.Length; i++)
            {
                var c = mats[i].color;
                mats[i].color = Color.Lerp(c, new Color(0.35f, 0.33f, 0.3f), 0.7f);
            }
            r.materials = mats;
        }
    }
}
