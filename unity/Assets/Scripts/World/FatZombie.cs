using UnityEngine;

// Жирный босс-зомби (майонезный раб, «обмазан»): постоянный уникальный
// враг пещеры в горе. В отличие от ночного Zombie живёт круглосуточно —
// никакой подписки на TimeOfDayChanged: в его пещере всегда полумрак.
// Простая стейт-машина: SLEEP (дремлет у логова) → CHASE → ATTACK,
// leash от ТОЧКИ СПАВНА — убежал дальше leashRange, и жирный поплёётся
// домой (быстрее, чем гонится) отращивать складки (5 hp/с в SLEEP).
// «Жирный не догонит» — но в пещере от него не убежать.
// ГОЛОВОЙ УДАРЯТЬ: урон босс ловит публичным TakeDamage — тот же путь,
// что у Zombie (см. пометку в отчёте про Player.SwingTool).
[RequireComponent(typeof(CharacterController))]
public class FatZombie : MonoBehaviour
{
    public float maxHp = 300f;
    public float damage = 25f;
    public float aggroRange = 14f;
    public float leashRange = 40f;
    public float attackRange = 2.6f;
    public float attackCooldown = 1.6f;
    public float speed = 2.2f;
    // Дроп. Setup их не проставляет (не по спеке) — при смерти берём
    // лениво: пикап у Inventory, мясо из Resources/Items/meat.
    // Уникальный дроп босса — после решения автора (идеи: ключ от
    // чего-нибудь / «банка вечного майонеза»), пока временно 5 × мясо.
    public PickupItem pickupPrefab;
    public ItemData dropItem;

    // ---- Статика для сейва (SaveSystem §9.5: босс — часть мира) ----
    // Специально БЕЗ ResetStatics: убитый босс остаётся убитым и после
    // рестарта сцены («Начать заново»), Setup по FatZombie.Dead не спавнит.
    static bool dead;
    public static bool Dead => dead;
    // Из сейва (SaveSystem.Load). Статики мало: живой экземпляр мог
    // появиться ДО загрузки (Awake отработал на старте сцены со свежей
    // статикой), и ранний `if (dead) return` в Update/TakeDamage тогда
    // замораживал его навсегда — стоит в логове неподвижно и урона не
    // ловит (репро: убить босса → перезайти в Play → загрузить сейв).
    // Поэтому сверяемся с сейвом в ОБЕ стороны: «убит» — живой экземпляр
    // тихо удаляется (без дропа и тостов: лут уже выпадал в тот раз);
    // «жив», а экземпляра нет (самоуничтожился в Awake по устаревшей
    // статике) — респавн в логове по параметрам Setup.
    public static void Restore(bool d)
    {
        dead = d;
        var live = FindFirstObjectByType<FatZombie>();
        if (d)
        {
            if (live != null) Destroy(live.gameObject);
        }
        else if (live == null)
        {
            RespawnAtLair();
        }
    }
    public static bool CaptureDead() => dead;           // в сейв (Save)

    // Респавн 1:1 с Setup (SetupMainScene, блок «босс»): центр залы,
    // CharacterController 2.6/0.9, дроп — мясо из Resources.
    static void RespawnAtLair()
    {
        Vector2 mtC = TerrainGen.MountainCenter;
        var go = new GameObject("FatBoss");
        go.transform.position = new Vector3(mtC.x, TerrainGen.CaveFloor + 0.2f, mtC.y);
        var cc = go.AddComponent<CharacterController>();
        cc.height = 2.6f;
        cc.radius = 0.9f;
        cc.center = new Vector3(0f, 1.3f, 0f);
        var boss = go.AddComponent<FatZombie>();
        boss.dropItem = Resources.Load<ItemData>("Items/meat");
        var inv = FindFirstObjectByType<Inventory>();
        if (inv != null) boss.pickupPrefab = inv.pickupPrefab;
    }

    enum State { Sleep, Chase, Attack, Home }
    State state = State.Sleep;

    float hp;
    float cooldown;
    float windup = -1f;         // >0 — идёт замах (0.5 с): удар телеграфный
    Vector3 homePos;            // точка спавна — якорь leash и возврата
    CharacterController cc;
    Player player;
    float playerSearchRetry;    // повторный поиск игрока не чаще раза в секунду
    Transform body;             // контейнер кубов — для дрёмы-покачивания

    const float WindupTime = 0.5f;
    const float RegenPerSec = 5f;   // дома жирный отращивает утраченное
    const float HomeSlack = 1.5f;   // «дошёл до логова»
    const int MeatDrops = 5;        // временный дроп (см. поле dropItem)

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        // Убитый не живёт: Setup обычно не спавнит его (проверка
        // FatZombie.Dead), это страховка на рестарт сцены/ручную постановку.
        if (dead) { Destroy(gameObject); return; }
        hp = maxHp;
        homePos = transform.position;
        BuildBody();
    }

    void Update()
    {
        if (dead) return; // Destroy из Awake откладывается до конца кадра
        // гравитация — всегда, до любых ранних выходов (как у Zombie)
        cc.Move(Physics.gravity * Time.deltaTime);

        if (player == null)
        {
            // ссылка протухает (старт сцены, загрузка сохранения) —
            // ищем заново, но не чаще раза в секунду
            if (Time.time >= playerSearchRetry)
            {
                playerSearchRetry = Time.time + 1f;
                player = FindFirstObjectByType<Player>();
            }
            if (player == null) return;
        }

        // Труп не агрится: жирный расходится по логову, как при leash.
        cooldown -= Time.deltaTime;

        // Дистанции — строго по горизонтали (как у Zombie): 3D-дистанция
        // на неровном полу пещеры «докидывала» разницу высот.
        Vector3 toPlayer = player.transform.position - transform.position;
        float dist = new Vector2(toPlayer.x, toPlayer.z).magnitude;

        switch (state)
        {
            case State.Sleep:
                // дрёма: лёгкое покачивание складок + реген до полного
                hp = Mathf.Min(maxHp, hp + RegenPerSec * Time.deltaTime);
                Sway();
                if (player.Stats.IsDead) break;
                if (dist < aggroRange) EnterChase();
                break;

            case State.Chase:
                if (player.Stats.IsDead || PlayerBeyondLeash()) { GoHome(); break; }
                if (dist < attackRange) { state = State.Attack; windup = WindupTime; break; }
                MoveTowards(player.transform.position, speed);
                break;

            case State.Attack:
                if (player.Stats.IsDead || PlayerBeyondLeash()) { GoHome(); break; }
                if (dist > attackRange * 1.3f) { state = State.Chase; windup = -1f; break; }
                FaceTo(player.transform.position);
                if (windup > 0f)
                {
                    // замах длинный — жирный телеграфирует удар, уворот честный
                    windup -= Time.deltaTime;
                    if (windup <= 0f)
                    {
                        // промах тоже уходит в кулдаун — без пулемёта взмахов
                        cooldown = attackCooldown;
                        if (dist < attackRange * 1.2f)
                        {
                            player.Stats.Damage(damage);
                            player.NotifyDamaged(damage);   // R4: тряска камеры жертвы
                            AudioManager.GruntAt(transform.position, 0f);
                        }
                    }
                }
                else if (cooldown <= 0f)
                {
                    windup = WindupTime;
                }
                break;

            case State.Home:
                if (HorizontalDist(homePos) < HomeSlack)
                {
                    state = State.Sleep; // у кромки логова — снова дрёма
                    break;
                }
                MoveTowards(homePos, speed * 1.4f); // домой спешит
                break;
        }

        // выйдя из дрёмы, тело плавно выпрямляется
        if (state != State.Sleep && body != null)
            body.localRotation = Quaternion.Slerp(body.localRotation,
                Quaternion.identity, Time.deltaTime * 4f);
    }

    void EnterChase()
    {
        state = State.Chase;
        AudioManager.GruntAt(transform.position); // вскрик засечения
    }

    void GoHome()
    {
        state = State.Home;
        windup = -1f;
    }

    // Игрок дальше leashRange от ТОЧКИ СПАВНА (не от босса): жирный не
    // отрывается от своей пещеры — «жирный не догонит».
    bool PlayerBeyondLeash()
    {
        Vector3 d = player.transform.position - homePos;
        return new Vector2(d.x, d.z).magnitude > leashRange;
    }

    // Лёгкое покачивание спящего: тело-контейнер кренится чуть-чуть,
    // корень (и CharacterController) не трогаем.
    void Sway()
    {
        if (body != null)
            body.localRotation = Quaternion.Euler(0f, 0f,
                Mathf.Sin(Time.time * 1.2f) * 2.5f);
    }

    float HorizontalDist(Vector3 target)
    {
        Vector3 d = target - transform.position;
        return new Vector2(d.x, d.z).magnitude;
    }

    // Шаг по земле — как у Zombie.MoveTowards: y стабилизируется по
    // HeightAt (+1, тот же конверт, что у ночного зомби — и как ставит
    // спавн), островную кромку не проверяем: босс — пещерный житель,
    // leash не выпускает его к океану.
    void MoveTowards(Vector3 target, float spd)
    {
        Vector3 flat = target - transform.position;
        flat.y = 0f;
        if (flat.sqrMagnitude < 0.05f) return;
        FaceTo(target);
        Vector3 step = flat.normalized * spd;
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

    // Удар по боссу — тот же путь, что по обычному зомби (Player.SwingTool).
    // Боль будит даже из дрёмы и перехватывает возврат домой.
    public void TakeDamage(float dmg)
    {
        if (dead) return;
        hp -= dmg;
        GameEvents.RaiseNotify($"Жирный получает {dmg:F0} урона");
        if (state == State.Sleep || state == State.Home)
        {
            state = State.Chase;
            AudioManager.GruntAt(transform.position); // вскрик боли
        }
        if (hp <= 0f) Die();
    }

    void Die()
    {
        dead = true; // статик ставится ДО дестроя — Save успеет захватить
        GameEvents.RaiseNotify("Жирный обмяк… что-то тяжёлое выпало из его складок.");
        DropLoot();
        Destroy(gameObject);
    }

    // ВРЕМЕННЫЙ дроп: 5 × мясо через стандартный пикап (как дроп зомби
    // и трупа игрока; посадку на землю делает сам PickupItem рейкастом
    // вниз в Start). Уникальный дроп — после решения автора.
    void DropLoot()
    {
        if (dropItem == null)
            dropItem = Resources.Load<ItemData>("Items/meat"); // Resources/Items ✓
        if (pickupPrefab == null)
        {
            var inv = FindFirstObjectByType<Inventory>();
            if (inv != null) pickupPrefab = inv.pickupPrefab;
        }
        if (dropItem == null || pickupPrefab == null)
        {
            Debug.LogWarning("[FatZombie] нет pickupPrefab/dropItem — дроп пропущен");
            return;
        }
        for (int i = 0; i < MeatDrops; i++)
        {
            // раскладка по кругу (золотой угол), чтобы мясо не лепилось в одну точку
            float ang = i * 2.39996f;
            var drop = Instantiate(pickupPrefab,
                transform.position + new Vector3(Mathf.Cos(ang) * 0.9f, 0.5f, Mathf.Sin(ang) * 0.9f),
                Quaternion.identity);
            drop.item = dropItem;
            drop.count = 1;
        }
    }

    // ---- Тело из кубов, по образцу ZombieSpawner.BuildBody, но ЖИРНОЕ: ----
    // торс в два куба шириной, выпирающий майонезный живот, короткие
    // ноги, маленькая голова. ~2.2 м ростом, ~1.9 м в плечах.
    // Коллайдеры у кубов отбираем — хитбокс это CharacterController корня,
    // который ставит Setup (h 2.6, r 0.9).
    static Material fatBodyMat, bellyMat, eyeMat;

    void BuildBody()
    {
        if (fatBodyMat == null)
        {
            // цвета обычного зомби (0.45, 0.58, 0.38), но желтее/бледнее —
            // майонезный раб, «обмазан»
            fatBodyMat = new Material(Shader.Find("Standard"))
                { color = new Color(0.62f, 0.58f, 0.40f) };
            // живот намазан бледным майонезом
            bellyMat = new Material(Shader.Find("Standard"))
                { color = new Color(0.78f, 0.74f, 0.52f) };
            eyeMat = new Material(Shader.Find("Standard")) { color = Color.red };
            eyeMat.EnableKeyword("_EMISSION");
            eyeMat.SetColor("_EmissionColor", Color.red * 1.8f);
        }

        body = new GameObject("Body").transform;
        body.SetParent(transform, false);

        Part(body, new Vector3(-0.40f, 0.30f, 0f), new Vector3(0.30f, 0.60f, 0.34f), fatBodyMat); // нога Л (короткая)
        Part(body, new Vector3(0.40f, 0.30f, 0f), new Vector3(0.30f, 0.60f, 0.34f), fatBodyMat);  // нога П
        Part(body, new Vector3(-0.42f, 1.12f, 0f), new Vector3(0.80f, 1.05f, 0.75f), fatBodyMat); // торс Л
        Part(body, new Vector3(0.42f, 1.12f, 0f), new Vector3(0.80f, 1.05f, 0.75f), fatBodyMat);  // торс П (широкий)
        Part(body, new Vector3(0f, 0.95f, 0.62f), new Vector3(0.95f, 0.85f, 0.55f), bellyMat);    // живот (выпирает вперёд)
        Part(body, new Vector3(0f, 1.86f, 0.05f), new Vector3(0.42f, 0.42f, 0.42f), fatBodyMat);  // голова (маленькая)
        Part(body, new Vector3(-0.85f, 1.30f, 0.50f), new Vector3(0.24f, 0.24f, 0.85f), fatBodyMat); // рука Л (вперёд)
        Part(body, new Vector3(0.85f, 1.30f, 0.50f), new Vector3(0.24f, 0.24f, 0.85f), fatBodyMat);  // рука П
        Part(body, new Vector3(-0.10f, 1.90f, 0.28f), new Vector3(0.08f, 0.08f, 0.05f), eyeMat);  // глаз Л
        Part(body, new Vector3(0.10f, 1.90f, 0.28f), new Vector3(0.08f, 0.08f, 0.05f), eyeMat);   // глаз П
    }

    // Куб-деталь без коллайдера — 1:1 как у ZombieSpawner.Part.
    static void Part(Transform parent, Vector3 localPos, Vector3 size, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(go.GetComponent<BoxCollider>()); // хитбокс — CharacterController корня
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = size;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
    }
}
