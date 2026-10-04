using System.Collections.Generic;
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
    // быстрее шага игрока (4), медленнее спринта (6.4): оторваться можно,
    // но бег жжёт ведро ×1.6 и оставляет жирный след — выбор, не халява
    public float chaseSpeed = 4.6f;
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
    Vector3 lastKnown;          // где видели игрока в последний раз
    float searchTimer;          // сколько ещё «отслеживаем» после потери из виду
    const float SearchTime = 8f;
    const float MinAggroTime = 5f; // форсированный агр по урону — не короче
    CharacterController cc;
    Player player;
    float playerSearchRetry;    // повторный поиск игрока не чаще раза в секунду
    bool isNight;

    // антизастревание: меряем смещение при активном движении
    float stuckTimer = -1f;     // <0 — контрольная точка не взведена
    Vector3 stuckRef;
    float detourTimer;          // >0 — идём боковым обходом
    Vector3 detourTarget;
    int blockedHits;            // подряд удары, глухо ушедшие в постройку (R4)

    enum State { Idle, Wander, Chase, Attack }
    State state = State.Idle;

    // ---- Маршрут приливной волны (X1 §9.4) ----
    // MayoSurge ведёт рой ПО СЛЕДУ игрока: точки следа от старейшей к
    // свежей + позиция игрока на момент спавна. Маршрут живёт только в
    // IDLE/WANDER; любое агро (LOS/урон) его сбрасывает — бой важнее
    // «сценария» прилива. Своя копия списка: весь рой делит один List,
    // а индекс у каждого зомби свой.
    List<Vector3> waveRoute;
    int routeIndex;

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
        // гравитация — всегда, до любых ранних выходов (раньше ранний
        // return при отсутствии игрока оставлял зомби висеть в воздухе)
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
            if (player == null) { Wander(); return; }
        }

        // Труп не атакуем: экран смерти не должен тонуть в спаме
        // «Зомби выбивает майонез», а стая не должна тусоваться у тела.
        if (player.Stats.IsDead)
        {
            if (state > State.Wander) { state = State.Wander; ResetUnstick(); }
            Wander();
            return;
        }

        // Дистанции — строго по горизонтали: 3D-дистанция на склонах
        // «докидывала» разницу высот, и зомби не дотягивался до атаки.
        Vector3 toPlayer = player.transform.position - transform.position;
        float dist = new Vector2(toPlayer.x, toPlayer.z).magnitude;
        cooldown -= Time.deltaTime;

        switch (state)
        {
            case State.Idle:
            case State.Wander:
                // преследование начинается с ЗРЕНИЯ (LOS), не по следу
                if (dist < noticeRange && isNight && CanSeePlayer())
                {
                    EnterChase();
                    break;
                }
                // приливная волна: пока маршрут жив — идём по нему обычным
                // шагом вместо блуждания (нюх в Wander не работает: сценарий
                // прилива важнее случайных точек)
                if (FollowWaveRoute()) break;
                Wander();
                break;
            case State.Chase:
            {
                if (dist < attackRange) { state = State.Attack; break; }
                Vector3 moveTarget;
                if (CanSeePlayer())
                {
                    lastKnown = player.transform.position;
                    searchTimer = SearchTime;
                    moveTarget = player.transform.position;
                }
                else
                {
                    // потерял из виду: к последней точке, по пути «отслеживаем»
                    // свежий след (ограниченное время), потом сдаёмся
                    searchTimer -= Time.deltaTime;
                    if (searchTimer <= 0f) { state = State.Wander; ResetUnstick(); break; }
                    if (MayoTrail.Instance != null &&
                        MayoTrail.Instance.FreshestNear(transform.position, 20f, out var scent))
                        lastKnown = scent;
                    moveTarget = lastKnown;
                }
                // застрял о ствол/камень — боковой обход ±2 м примерно на секунду
                if (detourTimer > 0f)
                {
                    detourTimer -= Time.deltaTime;
                    moveTarget = detourTarget;
                }
                else if (TrackStuck())
                {
                    Vector3 flat = moveTarget - transform.position; flat.y = 0f;
                    if (flat.sqrMagnitude > 0.01f)
                    {
                        float side = Random.value < 0.5f ? 1f : -1f;
                        detourTarget = moveTarget
                            + Vector3.Cross(Vector3.up, flat.normalized) * (2f * side);
                        detourTimer = 1f;
                        moveTarget = detourTarget;
                    }
                }
                MoveTowards(moveTarget, chaseSpeed);
                break;
            }
            case State.Attack:
                if (dist > attackRange * 1.3f) { state = State.Chase; blockedHits = 0; break; }
                FaceTo(player.transform.position);
                // принудительный обход: удары дважды ушли в стену (R4) —
                // идём боком ~1 с; отойдём достаточно далеко — и обычный
                // unstick в Chase подхватит
                if (detourTimer > 0f)
                {
                    detourTimer -= Time.deltaTime;
                    MoveTowards(detourTarget, chaseSpeed);
                    break;
                }
                ResetUnstick(); // стоим у жертвы по делу — это не застревание
                if (cooldown <= 0f && dist < attackRange)
                {
                    // стены имеют смысл: постройка игрока (Placed) на линии
                    // удара глушит урон ПОЛНОСТЬЮ, даже вплотную. Зомби
                    // лупит по стене с тем же кулдауном; 2 промаха подряд —
                    // принудительный боковой обход (база держится
                    // планировкой, как в 7DtD-ритме §9.1)
                    if (BlockedByPlaced(out var wallHit))
                    {
                        cooldown = attackCooldown; // замах ушёл в стену
                        blockedHits++;
                        AudioManager.HitAt(wallHit.point); // глухой «тук» слышен жертве
                        if (blockedHits >= 2)
                        {
                            blockedHits = 0;
                            Vector3 flat = player.transform.position - transform.position;
                            flat.y = 0f;
                            if (flat.sqrMagnitude > 0.01f)
                            {
                                float side = Random.value < 0.5f ? 1f : -1f;
                                detourTarget = player.transform.position
                                    + Vector3.Cross(Vector3.up, flat.normalized) * (2f * side);
                                detourTimer = 1f;
                            }
                        }
                    }
                    // не бить сквозь ствол/камень; вплотную к тонкому
                    // препятствию (ближе половины радиуса) — бить и так
                    else if (dist < attackRange * 0.5f || CanSeePlayer())
                    {
                        cooldown = attackCooldown;
                        blockedHits = 0;
                        player.Stats.Damage(attackDamage);
                        player.NotifyDamaged(attackDamage); // R4: тряска камеры жертвы
                        AudioManager.GruntAt(transform.position, 0f); // удар — рык погромче
                        // тоста про урон нет: вспышка+тряска+рык говорят сами (2026-10-03)
                    }
                }
                break;
        }
    }

    // Переход в погоню с вскриком (единая точка — звук не задваивается).
    // Сюда же приходит и агр по урону — маршрут прилива сбрасывается
    // в обоих случаях (LOS/урон), боевое поведение как раньше.
    void EnterChase()
    {
        ClearWaveRoute();
        state = State.Chase;
        lastKnown = player.transform.position;
        searchTimer = SearchTime;
        AudioManager.GruntAt(transform.position); // вскрик засечения
    }

    // ---- Маршрут приливной волны (вызывает MayoSurge) ----
    // Назначить маршрут «по следу». В IDLE/WANDER зомби пойдёт по точкам
    // обычной скоростью (walkSpeed); конец маршрута — обычное поведение.
    // CHASE/ATTACK маршрутом не перебиваем: агр по LOS/урону сбрасывает
    // его через EnterChase/ClearWaveRoute.
    public void SetWaveRoute(List<Vector3> route)
    {
        if (route == null || route.Count == 0) return;
        waveRoute = new List<Vector3>(route); // копия: рой делит один список
        routeIndex = 0;
        if (state == State.Idle) state = State.Wander; // сразу в шаг, не стоять
    }

    void ClearWaveRoute()
    {
        waveRoute = null;
        routeIndex = 0;
    }

    // Шаг по маршруту; true — маршрут ещё жив. Застрял о ствол/камень —
    // пропускаем точку (рой не должен вязнуть на рельефе), движение
    // остаётся на MoveTowards с его клампом к острову.
    bool FollowWaveRoute()
    {
        if (waveRoute == null) return false;
        if (TrackStuck())
        {
            routeIndex++;
            ResetUnstick();
        }
        // досыгаем пройденные точки (порог шире wander'овского 1 м:
        // зомби идут роем и не обязаны стоять точно в лужице)
        while (routeIndex < waveRoute.Count
               && HorizontalDist(waveRoute[routeIndex]) < 1.2f)
            routeIndex++;
        if (routeIndex >= waveRoute.Count)
        {
            ClearWaveRoute(); // маршрут пройден — дальше обычное поведение
            return false;
        }
        MoveTowards(waveRoute[routeIndex], walkSpeed);
        return true;
    }

    // Зрение: луч от головы зомби к ГРУДИ игрока. Пивот игрока — середина
    // его капсулы (cc.center = 0, см. Setup), прежняя цель +1.4 летела НАД
    // макушкой капсулы — луч не цеплял контроллер, и зомби были слепы.
    // Триггеры луч игнорирует, собственная капсула не считается (старт
    // изнутри), а ствол/камень/холм между = не видит.
    bool CanSeePlayer()
    {
        if (player == null) return false;
        Vector3 from = transform.position + Vector3.up * 1.5f;        // голова
        Vector3 to = player.transform.position + Vector3.up * 0.35f;  // грудь
        Vector3 dir = to - from;
        if (Physics.Raycast(from, dir, out var hit, dir.magnitude + 0.5f,
                ~0, QueryTriggerInteraction.Ignore))
            return hit.collider.GetComponentInParent<Player>() != null;
        return false;
    }

    // Между нами и игроком постройка (Placed)? Стена/костёр глушат урон
    // полностью, даже вплотную (R4: «стены имеют смысл»). Луч тот же,
    // что у зрения: голова → грудь. Ствол/холм/куст урону не мешают —
    // только то, что построил игрок.
    bool BlockedByPlaced(out RaycastHit wallHit)
    {
        Vector3 from = transform.position + Vector3.up * 1.5f;        // голова
        Vector3 to = player.transform.position + Vector3.up * 0.35f;  // грудь
        if (Physics.Linecast(from, to, out wallHit, ~0,
                QueryTriggerInteraction.Ignore))
            return wallHit.collider.GetComponentInParent<Placed>() != null;
        return false;
    }

    // Антизастревание: за ~1.5 с «движения» смещение < 0.3 м — упёрлись.
    bool TrackStuck()
    {
        if (stuckTimer < 0f)
        {
            stuckTimer = 0f;
            stuckRef = transform.position;
            return false;
        }
        stuckTimer += Time.deltaTime;
        if (stuckTimer < 1.5f) return false;
        Vector3 moved = transform.position - stuckRef; moved.y = 0f;
        stuckTimer = -1f; // на следующем кадре движения перевзведётся само
        return moved.magnitude < 0.3f;
    }

    void ResetUnstick()
    {
        stuckTimer = -1f;
        detourTimer = 0f;
    }

    void Wander()
    {
        // застрял — немедленный репик цели
        if (TrackStuck())
        {
            repickTarget = 0f;
            wanderTarget = transform.position;
        }

        // нюх: ночью зомби тянет к свежему майонезному следу (§9.2)
        sniffTimer -= Time.deltaTime;
        if (isNight && sniffTimer <= 0f)
        {
            sniffTimer = 2.5f;
            if (MayoTrail.Instance != null &&
                MayoTrail.Instance.FreshestNear(transform.position, 10f, out var scent))
            {
                wanderTarget = scent;
                repickTarget = 8f; // не перебивать нюх сразу случайной точкой
                state = State.Wander;
                MoveTowards(wanderTarget, walkSpeed);
                return;
            }
        }

        repickTarget -= Time.deltaTime;
        if (repickTarget <= 0f || HorizontalDist(wanderTarget) < 1f)
        {
            repickTarget = 6f;
            // Зомби не плавает: цель в океане протухшего майонеза или за
            // кромкой острова бракуем; до 8 попыток, иначе бредём к прежней.
            var goal = wanderTarget;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                float ang = Random.value * Mathf.PI * 2f;
                float r = Random.Range(4f, 10f);
                var p = transform.position + new Vector3(Mathf.Cos(ang) * r, 0, Mathf.Sin(ang) * r);
                p.y = TerrainGen.HeightAt(p.x, p.z) + 1f;
                if (new Vector2(p.x, p.z).magnitude > TerrainGen.IslandRadius - 4f) continue;
                if (TerrainGen.IsInOcean(p)) continue;
                goal = p;
                break;
            }
            wanderTarget = goal;
            state = State.Wander;
        }
        MoveTowards(wanderTarget, walkSpeed);
    }

    float HorizontalDist(Vector3 target)
    {
        Vector3 d = target - transform.position;
        return new Vector2(d.x, d.z).magnitude;
    }

    void MoveTowards(Vector3 target, float speed)
    {
        Vector3 flat = target - transform.position;
        flat.y = 0f;
        if (flat.sqrMagnitude < 0.05f) return;
        FaceTo(target);
        Vector3 step = flat.normalized * speed;
        // Зомби не плавает — толпится у кромки острова: кандидат шага,
        // уходящий за окружность IslandRadius-2, проецируем обратно на
        // неё (y-часть шага — рельеф — не трогаем).
        var nextXZ = new Vector2(transform.position.x + step.x, transform.position.z + step.z);
        float edge = TerrainGen.IslandRadius - 2f;
        if (nextXZ.magnitude > edge)
        {
            nextXZ = nextXZ.normalized * edge;
            step.x = nextXZ.x - transform.position.x;
            step.z = nextXZ.y - transform.position.z;
        }
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
        if (player != null)
        {
            // Агр по урону — независимо от дня/ночи: удар «выдаёт» жертву,
            // даже если стена между. Форсируем Chase минимум на 5 с;
            // свежий агр получает полный SearchTime (8 с).
            if (state < State.Chase)
            {
                EnterChase(); // с вскриком
            }
            else
            {
                lastKnown = player.transform.position;
                searchTimer = Mathf.Max(searchTimer, MinAggroTime);
            }
        }
        if (hp <= 0f)
        {
            if (dropItem != null && pickupPrefab != null)
            {
                // +0.3 вверх от позиции зомби; посадку на землю делает
                // сам PickupItem (рейкаст вниз в Start)
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
