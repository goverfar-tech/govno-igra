using UnityEngine;

// Подбираемый предмет в мире.
[RequireComponent(typeof(Collider))]
public class PickupItem : MonoBehaviour, IInteractable
{
    public ItemData item;
    [Min(1)] public int count = 1;

    public string GetPrompt()
        => item != null ? $"[E] Взять: {item.displayName} ×{count}" : "[E] Взять";

    public void Interact(Player player)
    {
        if (item == null || player == null) return;
        int leftover = player.Inventory.Add(item, count);
        if (leftover > 0)
        {
            GameEvents.RaiseNotify("Инвентарь полон");
            count = leftover;
        }
        else
        {
            Destroy(gameObject);
        }
    }
}
