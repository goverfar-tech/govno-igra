using UnityEngine;

// Озеро/водоём: наполняет пустую флягу (аналог озера из Godot-версии, M4).
public class WaterSource : MonoBehaviour, IInteractable
{
    public ItemData emptyFlask;
    public ItemData fullFlask;

    public string GetPrompt() => "[E] Наполнить флягу";

    public void Interact(Player player)
    {
        if (player == null || emptyFlask == null || fullFlask == null) return;
        if (player.Inventory.CountOf(emptyFlask) <= 0)
        {
            GameEvents.RaiseNotify("Нужна пустая фляга");
            return;
        }
        if (!player.Inventory.RemoveItem(emptyFlask, 1)) return;

        int leftover = player.Inventory.Add(fullFlask, 1);
        if (leftover > 0)
        {
            player.Inventory.Add(emptyFlask, 1); // не влезло — вернуть пустую
            GameEvents.RaiseNotify("Инвентарь полон");
        }
        else
        {
            GameEvents.RaiseNotify("Фляга наполнена");
        }
    }
}
