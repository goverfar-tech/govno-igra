using UnityEngine;

// FPS-контроллер (аналог player.gd): ходьба/бег/прыжок/присед,
// камера с захватом мыши, луч взаимодействия, ЛКМ = использовать предмет.
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(Inventory))]
[RequireComponent(typeof(Stats))]
public class Player : MonoBehaviour
{
    [Header("Движение")]
    public float walkSpeed = 4f;
    public float sprintMultiplier = 1.6f;
    public float crouchSpeed = 2f;
    public float jumpHeight = 1.1f;
    public float gravity = -18f;
    public float standHeight = 1.8f;
    public float crouchHeight = 1.0f;

    [Header("Камера")]
    public Transform head;          // дочерний объект с Camera
    public float mouseSensitivity = 2.2f;
    public float pitchLimit = 85f;

    [Header("Взаимодействие")]
    public float interactRange = 2.5f;
    public LayerMask interactMask = ~0;

    [Header("Ощущение тела (R1)")]
    public float bobAmplitude = 0.045f;   // вертикальный headbob при ходьбе
    public float bobFrequency = 8f;       // базовая частота, масштабируется скоростью
    public float landDipMax = 0.14f;      // проседание при жёстком приземлении
    public float sprintFovKick = 1.05f;   // множитель FOV на бегу

    float bobPhase, bobOffset, landOffset;
    // R4: урон ощущается телом (NotifyDamaged) — резкий импульс-кивок
    // вниз поверх bob/landOffset + короткий yaw-тычок головы.
    // Оба затухают в LateUpdate и существующие системы не ломают.
    float hitDip;
    float shakeTimer, shakeAmp;
    const float ShakeDuration = 0.22f;
    bool wasGrounded = true;
    float lastFallVel;
    bool isCrouchingSmooth;
    Camera cam;                           // камера в Head (для FOV-кика)
    float baseHeadY = -1f;                // высота головы из сетапа (ловим один раз)

    public Inventory Inventory { get; private set; }
    public Stats Stats { get; private set; }
    public float Pitch => pitch;

    CharacterController cc;
    float pitch;
    float verticalVel;
    float placeCooldown;
    bool inputBlocked;              // открыта панель инвентаря
    IInteractable focus;
    string lastPrompt;

    // Вид-модель выбранного предмета в руке (S): ребёнок головы,
    // размер нормируется независимо от масштаба GLB; коллайдеры у копии
    // отбираем — рука не должна ловить столкновения.
    Transform hand;
    GameObject handModel;
    ItemData handItem;
    const float handSize = 0.42f;

    // Точка возрождения (§9.5): последний костёр, у которого грелись
    // (ставит Campfire в Update, пока игрок в радиусе тепла).
    // Пока ни одного — просыпаемся на старте сцены.
    public static Vector3 lastCampfirePos;
    public static bool hasCampfireSpawn;
    Vector3 spawnPos;

    // Океан протухшего майонеза (§9): упасть можно, выплыть нельзя.
    // seaTimer — сколько секунд ведро тонет; lastDryPos — последняя сухая
    // точка (утонувший дроп инвентаря остаётся на берегу, а не на дне);
    // seaToastShown — тост борьбы показывается один раз за купание.
    float seaTimer;
    Vector3 lastDryPos;
    bool seaToastShown;

    // Статика точки респауна не должна переживать перезапуск сессии —
    // иначе без domain reload «последний костёр» протекал бы в новый мир.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        lastCampfirePos = default;
        hasCampfireSpawn = false;
    }

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        Inventory = GetComponent<Inventory>();
        Stats = GetComponent<Stats>();
        spawnPos = transform.position;
        lastDryPos = spawnPos; // спаун на плато — сухая точка по умолчанию

        // Новый мир (рестарт сцены / «Начать заново») — костров в нём ещё
        // нет. Статику респауна чистим и здесь, а не только на старте
        // сессии: иначе смерть после рестарта уводила к «призрачному»
        // костру ПРЕДЫДУЩЕГО мира — в пустое место чужой карты.
        lastCampfirePos = default;
        hasCampfireSpawn = false;

        // якорь «в руке»: правее-ниже центра экрана, чуть повёрнут внутрь.
        // Вид-модель ребёнок головы — наследует headbob автоматически.
        if (head != null)
        {
            hand = new GameObject("Hand").transform;
            hand.SetParent(head, false);
            hand.localPosition = new Vector3(0.38f, -0.34f, 0.62f);
            hand.localRotation = Quaternion.Euler(-6f, -18f, 4f);
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void OnEnable()
    {
        GameEvents.InventoryOpenChanged += OnInventoryOpen;
        GameEvents.SelectionChanged += RefreshHand;
        GameEvents.InventoryChanged += RefreshHand;
    }
    void OnDisable()
    {
        GameEvents.InventoryOpenChanged -= OnInventoryOpen;
        GameEvents.SelectionChanged -= RefreshHand;
        GameEvents.InventoryChanged -= RefreshHand;
    }
    void OnInventoryOpen(bool open)
    {
        inputBlocked = open;
        Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = open;
    }

    void Update()
    {
        placeCooldown -= Time.deltaTime;
        swingCooldown -= Time.deltaTime;
        if (Stats.IsDead)
        {
            ClearFocusPrompt();
            return;
        }

        // Модальный UI (меню/пауза/инвентарь от Hud) блокирует ввод.
        // Событие InventoryOpenChanged могло проскочить до нашей подписки
        // при старте сцены (гонка порядка Awake), поэтому проверяем
        // живое состояние: стоп-кадр или свободный курсор = UI владеет мышью.
        bool uiOwnsInput = inputBlocked || Time.timeScale <= 0f
                           || Cursor.lockState != CursorLockMode.Locked;
        if (!uiOwnsInput) Look();
        Move(uiOwnsInput);
        if (uiOwnsInput)
        {
            ClearFocusPrompt(); // подсказка не должна висеть под панелью
            return;
        }
        UpdateInteractFocus();

        if (Input.GetKeyDown(KeyCode.E) && focus != null)
        {
            // отклик рубки рукой (R3): звук удара + микро-кивок камеры;
            // если узел уже обобран — не звучим (подсказка «Обобрано»)
            if (focus is ResourceNode rn && rn.hitsLeft > 0)
            {
                AudioManager.Swing();
                GameEvents.RaiseWorldHit(rn.transform.position);
                landOffset = Mathf.Max(landOffset - 0.045f, -0.09f);
            }
            focus.Interact(this);
        }

        if (Input.GetMouseButtonDown(0))
            UseSelected();

        if (Input.GetMouseButtonDown(1))
            Inventory.DropSelected(this);

        for (int i = 0; i < Inventory.HotbarSize; i++)
            if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                Inventory.Select(i);

        float wheel = Input.GetAxis("Mouse ScrollWheel");
        if (wheel > 0f) Inventory.Select((Inventory.selected + 1) % Inventory.HotbarSize);
        if (wheel < 0f) Inventory.Select((Inventory.selected + Inventory.HotbarSize - 1) % Inventory.HotbarSize);
    }

    // ЛКМ: постройка > инструмент (бой/добыча) > расходник
    void UseSelected()
    {
        var slot = Inventory.SelectedSlot;
        if (slot.IsEmpty) return;
        if (slot.item.isPlaceable && TryPlace(slot.item)) return;
        if (slot.item.isTool && SwingTool(slot.item)) return;
        Inventory.UseSelected(this);
    }

    float swingCooldown;

    // Замах оружием: бьёт зомби на toolDamage. Инструментов для добычи
    // больше нет (аудит 2026-10): деревья/камни обдираются руками по E.
    // false — предмет не оружие: UseSelected попробует его съесть.
    bool SwingTool(ItemData item)
    {
        if (item == null || !item.isTool) return false;
        if (swingCooldown > 0f) return true;
        swingCooldown = 0.6f;
        AudioManager.Swing(); // замах слышен всегда, даже в молоко
        landOffset = Mathf.Max(landOffset - 0.05f, -0.1f);

        var origin = head != null ? head.position : transform.position + Vector3.up * 1.6f;
        if (Physics.Raycast(origin, (head != null ? head.forward : transform.forward),
                out var hit, interactRange, interactMask))
        {
            var zombie = hit.collider.GetComponentInParent<Zombie>();
            if (zombie != null)
                zombie.TakeDamage(item.toolDamage);
            // босс-пещеры — тот же удар, отдельный тип (не ночная стая)
            var fat = hit.collider.GetComponentInParent<FatZombie>();
            if (fat != null)
                fat.TakeDamage(item.toolDamage);
            // звук попадания + событие на шине (хоть зомби, хоть мир)
            AudioManager.HitAt(hit.point);
            GameEvents.RaiseWorldHit(hit.point);
        }
        return true;
    }

    // Установить постройку на поверхность перед игроком (до 6 м).
    // Порт _try_place из Godot-версии; превью-призрак — отдельной полировкой.
    bool TryPlace(ItemData item)
    {
        if (placeCooldown > 0f) return true;
        if (item.placeablePrefab == null)
        {
            // isPlaceable есть, префаб потерян (сбой импорта GLB) —
            // глотать клик молча нельзя, предмет выглядит сломанным
            GameEvents.RaiseNotify("Постройка потеряна — поставить нельзя");
            return true;
        }

        var origin = head != null ? head.position : transform.position + Vector3.up * 1.6f;
        if (!Physics.Raycast(origin, (head != null ? head.forward : transform.forward),
                out var hit, 6f, interactMask))
        {
            GameEvents.RaiseNotify("Сюда не поставить");
            return true;
        }
        if (!Inventory.RemoveItem(item, 1)) return true;

        placeCooldown = 0.4f;
        var placed = Instantiate(item.placeablePrefab, hit.point,
            Quaternion.Euler(0f, transform.eulerAngles.y, 0f));
        placed.name = item.displayName;
        var tag = placed.GetComponent<Placed>();   // метка для SaveSystem
        if (tag == null) tag = placed.AddComponent<Placed>();
        tag.itemId = item.id;
        GameEvents.RaiseNotify("Построено: " + item.displayName);
        return true;
    }

    // Телепорт с отключением CharacterController (SaveSystem).
    public void Teleport(Vector3 pos)
    {
        cc.enabled = false;
        transform.position = pos;
        cc.enabled = true;
        verticalVel = 0f;
        lastFallVel = 0f; // падение «до» телепорта не считаем
    }

    // Установить взгляд из сохранения.
    public void ApplyView(float yaw, float newPitch)
    {
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        pitch = newPitch;
        if (head != null) head.localEulerAngles = new Vector3(pitch, 0f, 0f);
    }

    // Пересобрать вид-модель в руке под выбранный слот.
    void Start() => RefreshHand();

    void RefreshHand()
    {
        if (Inventory == null) return;
        var item = Inventory.slots[Inventory.selected].item;
        if (item == handItem) return;
        handItem = item;
        if (handModel != null) { Destroy(handModel); handModel = null; }
        if (item == null || item.worldModel == null || hand == null) return;

        handModel = Instantiate(item.worldModel, hand);
        foreach (var col in handModel.GetComponentsInChildren<Collider>())
            Destroy(col);

        var rends = handModel.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return;
        var b = rends[0].bounds;
        foreach (var r in rends) b.Encapsulate(r.bounds);
        float maxDim = Mathf.Max(b.size.x, b.size.y, b.size.z);
        if (maxDim > 0.0001f) handModel.transform.localScale *= handSize / maxDim;
        // центр модели — на якоре руки
        b = rends[0].bounds;
        foreach (var r in rends) b.Encapsulate(r.bounds);
        handModel.transform.position += hand.position - b.center;
    }

    // Возрождение по §9.5 (corpse run): весь инвентарь вываливается
    // дропом на месте смерти, игрок просыпается у последнего костра
    // (или на старте) с половиной ведра. Мир не пересоздаётся и стая
    // НЕ снимается: зомби дойдут до места гибели (там же лежит дроп)
    // и разбредутся — ночь продолжается как ни в чём не бывало.
    public void Respawn()
    {
        var inv = Inventory;
        // Утонувший в океане дроп остаётся на берегу (§9.5 corpse run),
        // а не на дне протухшего майонеза: центр раскладки — последняя
        // сухая точка, если ведро упокоило в море.
        Vector3 dropCenter = TerrainGen.IsInOcean(transform.position)
            ? lastDryPos : transform.position;
        for (int i = 0; i < inv.slots.Count; i++)
        {
            var s = inv.slots[i];
            if (s.IsEmpty || inv.pickupPrefab == null) continue;
            // раскладываем по кругу (золотой угол), чтобы не лепить в одну
            // точку; посадку на землю делает сам PickupItem
            float ang = i * 2.39996f;
            var drop = Instantiate(inv.pickupPrefab,
                dropCenter + new Vector3(Mathf.Cos(ang) * 0.8f, 0.5f, Mathf.Sin(ang) * 0.8f),
                Quaternion.identity);
            drop.item = s.item;
            drop.count = s.count;
            s.item = null; s.count = 0;
        }
        GameEvents.RaiseInventoryChanged();
        inv.Select(0);

        Teleport(hasCampfireSpawn ? lastCampfirePos + Vector3.up * 0.6f : spawnPos);
        seaTimer = 0f;         // вылезли из моря — таймеры протухшего
        seaToastShown = false; // майонеза сброшены
        Stats.SetState(Stats.maxMayo * 0.5f, 0f); // полведра; сбросит IsDead,
                                                  // экран смерти гаснет по StatsChanged
        GameEvents.RaiseNotify("Ты очнулся. Вещи остались там, где ты упал.");
    }

    // R4: урон ощущается телом. Зомби зовёт при каждом попадании.
    // Резкий dips-импульс вниз (как проседание при приземлении, но с
    // экспоненциальным затуханием и жёстче) + случайный yaw-тычок головы
    // на пару кадров. Масштаб от силы удара: ~20 урона = полный кивок.
    public void NotifyDamaged(float amount)
    {
        // повторные удары подряд складываются, но не уводят голову в пол
        hitDip = Mathf.Max(hitDip - 0.18f * Mathf.Clamp01(amount / 20f), -0.3f);
        shakeTimer = ShakeDuration;
        float side = Random.value < 0.5f ? -1f : 1f;
        shakeAmp = side * Mathf.Lerp(3f, 8f, Mathf.Clamp01(amount / 20f));
    }

    void LateUpdate()
    {
        if (head == null) return;
        if (cam == null) cam = head.GetComponentInChildren<Camera>();
        if (baseHeadY < 0f) baseHeadY = head.localPosition.y; // из сетапа (1.6)

        // --- headbob (R1): фаза идёт от скорости, затухает в прыжке/на месте ---
        Vector3 hv = cc.velocity; hv.y = 0f;
        float speedNorm = Mathf.Clamp01(hv.magnitude / (walkSpeed * sprintMultiplier));
        bool moving = speedNorm > 0.05f && cc.isGrounded;
        if (moving)
            bobPhase += Time.deltaTime * bobFrequency * Mathf.Lerp(0.6f, 1.4f, speedNorm);
        float amp = bobAmplitude * Mathf.Lerp(0.6f, 1f, speedNorm)
                    * (isCrouchingSmooth ? 0.45f : 1f);
        bobOffset = moving
            ? Mathf.Lerp(bobOffset, Mathf.Sin(bobPhase) * amp, Time.deltaTime * 8f)
            : Mathf.Lerp(bobOffset, 0f, Time.deltaTime * 6f);
        landOffset = Mathf.MoveTowards(landOffset, 0f, Time.deltaTime * 0.45f);

        // голова: высота следует за плавной капсулой + bob + проседание
        float heightT = Mathf.InverseLerp(crouchHeight, standHeight, cc.height);
        float headY = Mathf.Lerp(baseHeadY * 0.62f, baseHeadY, heightT);
        // импульс урона (R4): экспоненциальное затухание, складывается
        // с bob/landOffset — своих не трогает
        hitDip = Mathf.Lerp(hitDip, 0f, 1f - Mathf.Exp(-6f * Time.deltaTime));
        head.localPosition = new Vector3(0f, headY + bobOffset + landOffset + hitDip, 0f);

        // yaw-тычок от урона: Look() ставит yaw=0 каждый кадр в Update,
        // трясём поверх здесь (LateUpdate — после). По окончании явно
        // возвращаем 0, чтобы при открытом UI (Look не зовётся) голова
        // не осталась довёрнутой.
        if (shakeTimer > 0f)
        {
            shakeTimer -= Time.deltaTime;
            float t = Mathf.Clamp01(shakeTimer / ShakeDuration);
            head.localEulerAngles = new Vector3(pitch, shakeAmp * t, 0f);
            if (shakeTimer <= 0f) head.localEulerAngles = new Vector3(pitch, 0f, 0f);
        }

        // --- FOV-кик на бегу: базу читаем из GameSettings, не перезаписываем ---
        if (cam != null && GameSettings.Fov > 1f)
        {
            bool sprintNow = moving && Input.GetKey(KeyCode.LeftShift) && !isCrouchingSmooth;
            float targetFov = Mathf.Min(GameSettings.Fov * (sprintNow ? sprintFovKick : 1f), 100f);
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFov, Time.deltaTime * 6f);
        }
    }

    void Look()
    {
        float mx = Input.GetAxis("Mouse X") * mouseSensitivity;
        float my = Input.GetAxis("Mouse Y") * mouseSensitivity;
        transform.Rotate(0f, mx, 0f);
        pitch = Mathf.Clamp(pitch - my, -pitchLimit, pitchLimit);
        if (head != null) head.localEulerAngles = new Vector3(pitch, 0f, 0f);
    }

    void Move(bool uiOwnsInput)
    {
        // При открытом UI стоим и не бежим (иначе протекает ведро ×1.6
        // пока игрок копается в инвентаре); гравитацию считаем всегда,
        // чтобы не висеть в воздухе. Прыжок при UI запрещён.
        bool crouching = Input.GetKey(KeyCode.LeftControl);
        // спринт считаем только стоя: Shift+Ctrl = присед, утечка не растёт
        bool sprinting = Input.GetKey(KeyCode.LeftShift) && !crouching && !uiOwnsInput;

        float h = uiOwnsInput ? 0f : Input.GetAxisRaw("Horizontal");
        float v = uiOwnsInput ? 0f : Input.GetAxisRaw("Vertical");
        Vector3 dir = (transform.right * h + transform.forward * v).normalized;

        float speed = crouching ? crouchSpeed
                    : sprinting ? walkSpeed * sprintMultiplier
                    : walkSpeed;

        // плавный присед: высота капсулы ползёт, голова следом (R1)
        float targetHeight = crouching ? crouchHeight : standHeight;
        if (targetHeight > cc.height + 0.001f)
        {
            // не встаём сквозь потолок: пересекаем ЦЕЛЕВУЮ (стоячую)
            // капсулу OverlapCapsule. cc.center = 0 (см. Setup): пивот
            // посреди капсулы, низ текущей — на -cc.height/2 от него.
            float r = cc.radius * 0.95f;
            float bottomLocal = cc.center.y - cc.height * 0.5f;
            Vector3 low = transform.position + Vector3.up * (bottomLocal + r + 0.02f);
            Vector3 high = transform.position + Vector3.up * (bottomLocal + targetHeight - r - 0.02f);
            bool blocked = false;
            foreach (var col in Physics.OverlapCapsule(low, high, r, ~0,
                         QueryTriggerInteraction.Ignore))
                if (col != cc) { blocked = true; break; } // свой контроллер не в счёт
            if (blocked) targetHeight = cc.height;
        }
        cc.height = Mathf.MoveTowards(cc.height, targetHeight, Time.deltaTime * 6f);
        isCrouchingSmooth = crouching || cc.height < standHeight - 0.01f;

        if (cc.isGrounded)
        {
            if (!wasGrounded) // приземление: проседание жёстче от скорости падения
                landOffset = -Mathf.Clamp(-lastFallVel * 0.018f, 0f, landDipMax);
            lastFallVel = 0f; // падение отработано — не тащить в след. приземления
            wasGrounded = true;
            verticalVel = -2f;
            if (!uiOwnsInput && Input.GetKeyDown(KeyCode.Space))
                verticalVel = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }
        else
        {
            lastFallVel = Mathf.Min(lastFallVel, verticalVel);
            wasGrounded = false;
        }
        verticalVel += gravity * Time.deltaTime;

        // Океан протухшего майонеза (§9): упасть можно, выплыть нельзя.
        // Проверка ДО cc.Move — вязкость обязана глушить сам шаг.
        // Числа подобраны так, чтобы за отведённые 3.5 с нельзя было
        // выгрести обратно даже прыжками: 0.3 скорости × 3.5 с ≈ 4 м,
        // а до берега от кромки 10+ м.
        // Тонем не только за кромкой отмели (IsInOcean): голова под
        // поверхностью моря (−0.45) тоже включает таймер — раньше полоса
        // мелководья ~423–428 м была проходима по дну безнаказанно,
        // граница смерти не совпадала с ватерлинией (аудит 2026-10-04).
        // Вязкость и гашение прыжка остаются только глубокой воде:
        // по мелководью ко дну идём ходко, назад выбраться реально.
        bool deepSea = TerrainGen.IsInOcean(transform.position);
        bool inSea = deepSea
            || (head != null && head.position.y < TerrainGen.SeaLevel - 0.45f);
        if (deepSea)
        {
            dir *= 0.3f; // вязкий майонез: гребки почти впустую
            // майонез держит — тонем плавно, а не камнем;
            // прыжок гасится: выпрыгивать из ведра-в-ведре нечестно
            verticalVel = Mathf.Min(Mathf.Max(verticalVel, -2.5f), 1.2f);
        }
        if (inSea)
        {
            seaTimer += Time.deltaTime;
            if (!seaToastShown && seaTimer >= 0.5f)
            {
                seaToastShown = true;
                GameEvents.RaiseNotify("Протухший майонез засасывает ведро! Выбраться нельзя…");
            }
            // через 3.5 секунды ведро сдаётся: Damage клампит майонез в
            // ноль — IsDead и RaisePlayerDied поднимутся сами
            if (seaTimer >= 3.5f) Stats.Damage(Stats.Mayo);
            if (Stats.IsDead) return; // мёртвое ведро не гребёт и не тикает
        }
        else
        {
            seaTimer = 0f;
            seaToastShown = false;
            // запоминаем последнюю сухую точку для дропа при утоплении;
            // отмель во время отлива (дно ниже уровня моря) сухой не считаем
            if (TerrainGen.HeightAt(transform.position.x, transform.position.z)
                    > TerrainGen.SeaLevel + 0.3f)
                lastDryPos = transform.position;
        }

        cc.Move((dir * speed + Vector3.up * verticalVel) * Time.deltaTime);

        bool moving = dir.sqrMagnitude > 0.01f && cc.isGrounded;
        Stats.Tick(Time.deltaTime, sprinting && moving);
    }

    // Погасить прицельную подсказку (открыт UI / игрок мёртв):
    // UpdateInteractFocus в это время не зовётся, и последняя строка
    // иначе оставалась висеть под модальным окном.
    void ClearFocusPrompt()
    {
        if (focus == null && lastPrompt == null) return;
        focus = null;
        lastPrompt = null;
        GameEvents.RaisePromptChanged(null);
    }

    void UpdateInteractFocus()
    {
        IInteractable hit = null;
        var cam = head != null ? head : transform;
        if (Physics.Raycast(cam.position, cam.forward, out var info, interactRange, interactMask))
            hit = info.collider.GetComponentInParent<IInteractable>();

        // GetPrompt зовём КАЖДЫЙ кадр: подсказки живые (R5 — прогресс
        // готовки у костра «Жарим… N%», после удара по узлу «Добыть
        // (2/3)» и т.п.). Событие летит только при реальной смене
        // строки или цели — иначе Hud перерисовывался бы зря.
        string newPrompt = hit?.GetPrompt();
        if (!ReferenceEquals(hit, focus) || newPrompt != lastPrompt)
        {
            focus = hit;
            lastPrompt = newPrompt;
            GameEvents.RaisePromptChanged(newPrompt);
        }
    }
}
