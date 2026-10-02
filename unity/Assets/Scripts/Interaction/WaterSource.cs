using UnityEngine;

// Озеро/водоём: наполняет пустую флягу (аналог озера из Godot-версии, M4).
public class WaterSource : MonoBehaviour, IInteractable
{
    public ItemData emptyFlask;
    public ItemData fullFlask;
    [Header("Баланс: лужа мелеет после наполнения, иначе бесконечные ресурсы")]
    public float refillCooldown = 120f;

    float nextFillTime;

    public string GetPrompt()
    {
        float left = nextFillTime - Time.time;
        return left > 0f ? $"Лужа мелеет… (ещё {Mathf.CeilToInt(left)} с)" : "[E] Начерпать сырой майонез";
    }

    public void Interact(Player player)
    {
        if (player == null || emptyFlask == null || fullFlask == null) return;
        if (Time.time < nextFillTime)
        {
            GameEvents.RaiseNotify("Лужа слишком мелкая — зайди позже");
            return;
        }
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
            return;
        }
        nextFillTime = Time.time + refillCooldown;
        GameEvents.RaiseNotify("Начерпал сырого майонеза");
    }
}
