using UnityEngine;

// Зомби (M5, майонезная мета §9): ночная угроза.
// Состояния: IDLE (стоит) → WANDER (бродит) → CHASE (увидел игрока) →
// ATTACK (в упор). Днём дематериализуется — это не Resident Evil,
// это Don't Starve (§9.1 spooky-cute).
[RequireComponent(typeof(CharacterController))]
public class Zombie : MonoBehaviour
{
    public float maxHp = 30f;
    public float walkSpeed = 1.6f;
    public float chaseSpeed = 3.2f;
    public float noticeRange = 12f;
    public float attackRange = 1.8f;
    public float attackDamage = 8f;
    public float attackCooldown = 1.1f;
    public ItemData dropItem;          // сырая плоть
    public PickupItem pickupPrefab;

    float hp;
    float cooldown;
    Vector3 wanderTarget;
    float repickTarget;
    float sniffTimer;
    CharacterController cc;
    Player player;
    bool isNight;

    enum State { Idle, Wander, Chase, Attack }
    State state = State.Idle;

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        hp = maxHp;
    }

    void OnEnable() => GameEvents.TimeOfDayChanged += OnTime;
    void OnDisable() => GameEvents.TimeOfDayChanged -= OnTime;

    void OnTime(float t, bool night)
    {
        isNight = night;
        if (!night) Destroy(gameObject); // днём рассасываются
    }

    void Update()
    {
        if (player == null)
        {
            player = FindFirstObjectByType<Player>();
            if (player == null) { Wander(); return; }
        }

        float dist = Vector3.Distance(transform.position, player.transform.position);
        cooldown -= Time.deltaTime;

        switch (state)
        {
            case State.Idle:
            case State.Wander:
                if (dist < noticeRange && isNight) { state = State.Chase; break; }
                Wander();
                break;
            case State.Chase:
                if (dist > noticeRange * 1.6f) { state = State.Wander; break; }
                if (dist < attackRange) { state = State.Attack; break; }
                MoveTowards(player.transform.position, chaseSpeed);
                break;
            case State.Attack:
                if (dist > attackRange * 1.3f) { state = State.Chase; break; }
                FaceTo(player.transform.position);
                if (cooldown <= 0f)
                {
                    cooldown = attackCooldown;
                    player.Stats.Damage(attackDamage);
                    GameEvents.RaiseNotify("Зомби выбивает майонез! -" + attackDamage);
                }
                break;
        }

        // гравитация
        cc.Move(Physics.gravity * Time.deltaTime);
    }

    void Wander()
    {
        // нюх: ночью зомби тянет к свежему майонезному следу (§9.2)
        sniffTimer -= Time.deltaTime;
        if (isNight && sniffTimer <= 0f)
        {
            sniffTimer = 2.5f;
            if (MayoTrail.Instance != null &&
                MayoTrail.Instance.FreshestNear(transform.position, 14f, out var scent))
            {
                wanderTarget = scent;
                repickTarget = 8f; // не перебивать нюх сразу случайной точкой
                state = State.Wander;
                MoveTowards(wanderTarget, walkSpeed);
                return;
            }
        }

        repickTarget -= Time.deltaTime;
        if (repickTarget <= 0f || Vector3.Distance(transform.position, wanderTarget) < 1f)
        {
            repickTarget = 6f;
            float ang = Random.value * Mathf.PI * 2f;
            float r = Random.Range(4f, 10f);
            var p = transform.position + new Vector3(Mathf.Cos(ang) * r, 0, Mathf.Sin(ang) * r);
            p.y = TerrainGen.HeightAt(p.x, p.z) + 1f;
            wanderTarget = p;
            state = State.Wander;
        }
        MoveTowards(wanderTarget, walkSpeed);
    }

    void MoveTowards(Vector3 target, float speed)
    {
        Vector3 flat = target - transform.position;
        flat.y = 0f;
        if (flat.sqrMagnitude < 0.05f) return;
        FaceTo(target);
        Vector3 step = flat.normalized * speed;
        // держимся на рельефе
        step.y = (TerrainGen.HeightAt(transform.position.x, transform.position.z) + 1f
                  - transform.position.y) * 5f;
        cc.Move(step * Time.deltaTime);
    }

    void FaceTo(Vector3 target)
    {
        Vector3 flat = target - transform.position;
        flat.y = 0f;
        if (flat.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.Slerp(transform.rotation,
                Quaternion.LookRotation(flat), Time.deltaTime * 6f);
    }

    public void TakeDamage(float dmg)
    {
        hp -= dmg;
        GameEvents.RaiseNotify($"Зомби получает {dmg:F0} урона");
        if (player != null && state < State.Chase) state = State.Chase; // агримся даже днём
        if (hp <= 0f)
        {
            if (dropItem != null && pickupPrefab != null)
            {
                var drop = Instantiate(pickupPrefab,
                    transform.position + Vector3.up * 0.3f, Quaternion.identity);
                drop.item = dropItem;
                drop.count = 1;
            }
            GameEvents.RaiseNotify("Зомби повержен");
            Destroy(gameObject);
        }
    }
}
