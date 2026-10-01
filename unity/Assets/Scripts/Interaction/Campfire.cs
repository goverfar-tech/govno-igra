using UnityEngine;

// Костёр (M5): готовка плоти + тепло в радиусе.
// Тепло: пока игрок в warmRadius, ночной множитель расхода (Stats) не
// действует — ночь у огня переживается. Механика «посидеть у костра».
// Рецепты берутся из Resources в Awake — префаб не зависит от Setup.
public class Campfire : MonoBehaviour, IInteractable
{
    public float warmRadius = 4f;
    public float cookCooldown = 1.2f;

    ItemData rawMeat, cookedMeat;
    Player player;
    float nextCook;
    bool toldWarm;

    void Awake()
    {
        rawMeat = Resources.Load<ItemData>("Items/meat");
        cookedMeat = Resources.Load<ItemData>("Items/cooked_meat");
    }

    public string GetPrompt()
        => player != null && rawMeat != null && player.Inventory.CountOf(rawMeat) > 0
            ? "[E] Пожарить плоть (у костра тепло)"
            : "Костёр греет (нужна сырая плоть для готовки)";

    public void Interact(Player p)
    {
        if (p == null || rawMeat == null || cookedMeat == null) return;
        if (Time.time < nextCook) return;

        if (p.Inventory.CountOf(rawMeat) <= 0)
        {
            GameEvents.RaiseNotify("Нет сырой плоти");
            return;
        }

        nextCook = Time.time + cookCooldown;
        p.Inventory.RemoveItem(rawMeat, 1);
        int leftover = p.Inventory.Add(cookedMeat, 1);
        if (leftover > 0)
        {
            p.Inventory.Add(rawMeat, 1); // не влезло — вернуть сырую
            GameEvents.RaiseNotify("Инвентарь полон");
        }
        else
        {
            GameEvents.RaiseNotify("Готово: " + cookedMeat.displayName);
        }
    }

    void Update()
    {
        if (player == null)
        {
            player = FindFirstObjectByType<Player>();
            return;
        }
        bool near = Vector3.Distance(transform.position, player.transform.position) < warmRadius;
        if (near)
        {
            player.Stats.lastWarmTime = Time.time;
            if (!toldWarm)
            {
                toldWarm = true;
                GameEvents.RaiseNotify("Тепло костра согревает");
            }
        }
        else toldWarm = false;
    }
}
