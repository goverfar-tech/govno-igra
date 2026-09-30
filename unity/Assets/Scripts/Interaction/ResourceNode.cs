using UnityEngine;

// Добываемый источник ресурса: дерево / камень-жилка (3 удара).
[RequireComponent(typeof(Collider))]
public class ResourceNode : MonoBehaviour, IInteractable
{
    public ItemData yield;          // что выпадает
    [Min(1)] public int hitsLeft = 3;
    [Min(1)] public int yieldPerHit = 1;
    public PickupItem pickupPrefab; // что спавним при добыче

    public string GetPrompt()
        => yield != null ? $"[E] Добыть: {yield.displayName} (осталось: {hitsLeft})" : "[E] Добыть";

    public void Interact(Player player) => Hit(player, 1);

    // Удар по узлу; инструменты бьют сильнее (Godot-эталон: топор = 2).
    public void Hit(Player player, int amount)
    {
        if (yield == null || player == null) return;
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

        if (hitsLeft <= 0) Destroy(gameObject);
    }
}
