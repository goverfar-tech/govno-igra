using UnityEngine;

// Озеро/водоём: наполняет пустую флягу (аналог озера из Godot-версии, M4).
public class WaterSource : MonoBehaviour, IInteractable
{
    public ItemData emptyFlask;
    public ItemData fullFlask;
    [Header("Баланс: лужа мелеет после наполнения, иначе бесконечные ресурсы")]
    public float refillCooldown = 120f;

    [Header("Визуал: насколько ужимается лужа при отливе (доля исходного размера)")]
    [Range(0.1f, 1f)] public float minScaleFactor = 0.55f;

    float nextFillTime;
    Transform surface;        // соседний визуал «Surface» (брат InteractZone)
    Vector3 surfaceBaseScale; // исходный локальный масштаб визуала

    void Start()
    {
        // Ищем визуал рядом: этот скрипт на «InteractZone», визуал — «Surface» у общего родителя.
        surface = transform.parent != null ? transform.parent.Find("Surface") : null;
        if (surface != null) surfaceBaseScale = surface.localScale;
    }

    void Update()
    {
        if (surface == null) return;

        float remaining = nextFillTime - Time.time;
        // Целевой масштаб по XZ: во время отлива — от minScaleFactor до 1,
        // вне кулдауна — ровно исходный.
        float factor = 1f;
        if (remaining > 0f && refillCooldown > 0f)
        {
            float fraction = Mathf.Clamp01(1f - remaining / refillCooldown); // 0 на старте отлива, 1 когда снова полна
            factor = Mathf.Lerp(minScaleFactor, 1f, fraction);
        }

        float targetX = surfaceBaseScale.x * factor;
        float targetZ = surfaceBaseScale.z * factor;
        // Плавно подтягиваем текущий масштаб к целевому, чтобы не дёргалось.
        float speed = surfaceBaseScale.x * 2f; // скорость в единицах масштаба/с
        float newX = Mathf.MoveTowards(surface.localScale.x, targetX, speed * Time.deltaTime);
        float newZ = Mathf.MoveTowards(surface.localScale.z, targetZ, speed * Time.deltaTime);
        surface.localScale = new Vector3(newX, surfaceBaseScale.y, newZ); // Y не трогаем
    }

    // Остаток «меления» в секундах (SaveSystem сохраняет/восстанавливает).
    public float RemainingCooldown
    {
        get => Mathf.Max(0f, nextFillTime - Time.time);
        set => nextFillTime = Time.time + Mathf.Max(0f, value);
    }

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
