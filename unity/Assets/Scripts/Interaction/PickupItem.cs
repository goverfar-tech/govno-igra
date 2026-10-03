using UnityEngine;

// Подбираемый предмет в мире.
[RequireComponent(typeof(Collider))]
public class PickupItem : MonoBehaviour, IInteractable
{
    public ItemData item;
    [Min(1)] public int count = 1;

    // Блик над предметом (R3): маленькая светящаяся точка, пульсирует,
    // чтобы пикапы находились глазами в траве/сумерках. S-полировка:
    // дальше ~22 м не маячит (не превращать мир в гирлянду точек).
    static Material glintMat;
    Transform glint;
    const float glintHideDist = 22f;

    // Мягкий магнетизм (S): выроненное/добытное под ноги не липнет мгновенно
    // (insert grace после спавна), а уже лежащее притягивается, когда рядом.
    float spawnTime;
    const float noMagnetSec = 1.2f;
    const float magnetRange = 2.0f;   // с этого радиуса тянемся
    const float magnetSpeed = 5.5f;
    Player magnetPlayer;
    float magnetSearchRetry;

    void Start()
    {
        spawnTime = Time.time;
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
        // блик: пульс только если игрок близко — дальше мир не «переливается»
        if (glint != null)
        {
            float d2 = magnetPlayer != null
                ? (transform.position - magnetPlayer.transform.position).sqrMagnitude
                : float.MaxValue;
            bool vis = d2 < glintHideDist * glintHideDist;
            if (glint.gameObject.activeSelf != vis) glint.gameObject.SetActive(vis);
            if (vis)
                glint.localScale = Vector3.one * (0.11f + Mathf.Sin(Time.time * 4f) * 0.025f);
        }

        // магнетизм: только когда игрок уже есть и «возраст» дропа пережит
        if (item == null) return;
        if (Time.time - spawnTime < noMagnetSec) return;
        if (magnetPlayer == null)
        {
            if (Time.time < magnetSearchRetry) return;
            magnetSearchRetry = Time.time + 1f;
            magnetPlayer = FindFirstObjectByType<Player>();
            if (magnetPlayer == null) return;
        }
        Vector3 to = magnetPlayer.transform.position + Vector3.up * 0.4f - transform.position;
        float dist = to.magnitude;
        if (dist > magnetRange) return;
        if (dist < 0.55f)
        {
            // впритык — пробуем всосать в инвентарь (как E, но без клика)
            TryAbsorb(magnetPlayer);
            return;
        }
        // плавная тяга к ногам игрока; сквозь землю не проваливаемся —
        // рейкаст положил на землю, тянемся горизонтально-верхне.
        // Сквозь стены тоже не тянем (R4 «стены честные», аудит
        // 2026-10-04): преграда на линии (стена/камень/контейнер) гасит
        // тягу — дроп ждёт у преграды, пока игрок не подойдёт. Луч короче
        // цели на полметра: иначе капсула игрока (r=0.35) блокировала бы
        // саму себя в последней пяди.
        var dirTo = to / dist;
        if (Physics.Raycast(transform.position, dirTo, out _,
                dist - 0.5f, ~0, QueryTriggerInteraction.Ignore))
            return;
        transform.position += dirTo * (magnetSpeed * Time.deltaTime);
    }

    void TryAbsorb(Player player)
    {
        int leftover = player.Inventory.Add(item, count);
        if (leftover > 0)
        {
            if (leftover != count) GameEvents.RaiseNotify("Инвентарь полон");
            count = leftover;
        }
        else
        {
            Destroy(gameObject);
        }
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
