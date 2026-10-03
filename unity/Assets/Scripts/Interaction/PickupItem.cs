using UnityEngine;

// Подбираемый предмет в мире.
[RequireComponent(typeof(Collider))]
public class PickupItem : MonoBehaviour, IInteractable
{
    public ItemData item;
    [Min(1)] public int count = 1;

    // Блик над предметом (R3): маленькая светящаяся точка, пульсирует,
    // чтобы пикапы находились глазами в траве/сумерках.
    static Material glintMat;
    Transform glint;

    void Start()
    {
        // Садимся на землю: дроп (ПКМ, переполнение, зомби/курица)
        // спавнится в воздухе/перед грудью — опускаем лучом вниз.
        // Триггеры (в т.ч. свой коллайдер и другие пикапы) луч игнорирует,
        // попадание по игроку тоже отсекаем.
        if (Physics.Raycast(transform.position + Vector3.up * 0.5f, Vector3.down,
                out var hit, 30f, ~0, QueryTriggerInteraction.Ignore)
            && hit.collider.GetComponentInParent<Player>() == null
            && hit.collider.GetComponentInParent<PickupItem>() == null)
        {
            transform.position = hit.point + Vector3.up * 0.05f;
        }

        if (glintMat == null)
        {
            glintMat = new Material(Shader.Find("Standard"))
                { color = new Color(1f, 0.95f, 0.6f) };
            glintMat.EnableKeyword("_EMISSION");
            glintMat.SetColor("_EmissionColor", new Color(1f, 0.9f, 0.5f) * 1.5f);
        }
        var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(g.GetComponent<SphereCollider>());
        g.transform.SetParent(transform, false);
        g.transform.localPosition = Vector3.up * 0.8f;
        g.transform.localScale = Vector3.one * 0.12f;
        g.name = "Glint";
        g.GetComponent<MeshRenderer>().sharedMaterial = glintMat;
        glint = g.transform;
    }

    void Update()
    {
        if (glint != null)
            glint.localScale = Vector3.one * (0.12f + Mathf.Sin(Time.time * 4f) * 0.03f);
    }

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
