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

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        Inventory = GetComponent<Inventory>();
        Stats = GetComponent<Stats>();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void OnEnable() => GameEvents.InventoryOpenChanged += OnInventoryOpen;
    void OnDisable() => GameEvents.InventoryOpenChanged -= OnInventoryOpen;
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
        if (Stats.IsDead) return;

        // Модальный UI (меню/пауза/инвентарь от Hud) блокирует ввод.
        // Событие InventoryOpenChanged могло проскочить до нашей подписки
        // при старте сцены (гонка порядка Awake), поэтому проверяем
        // живое состояние: стоп-кадр или свободный курсор = UI владеет мышью.
        bool uiOwnsInput = inputBlocked || Time.timeScale <= 0f
                           || Cursor.lockState != CursorLockMode.Locked;
        if (!uiOwnsInput) Look();
        Move();
        if (uiOwnsInput) return;
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
    bool SwingTool(ItemData item)
    {
        if (swingCooldown > 0f) return true;
        swingCooldown = 0.6f;
        AudioManager.Swing(); // замах слышен всегда
        landOffset = Mathf.Max(landOffset - 0.05f, -0.1f);

        var origin = head != null ? head.position : transform.position + Vector3.up * 1.6f;
        if (Physics.Raycast(origin, (head != null ? head.forward : transform.forward),
                out var hit, 3f, interactMask))
        {
            var zombie = hit.collider.GetComponentInParent<Zombie>();
            if (zombie != null)
            {
                zombie.TakeDamage(item.toolDamage);
                GameEvents.RaiseWorldHit(hit.point);
                return true;
            }
        }
        return true;
    }

    // Установить постройку на поверхность перед игроком (до 6 м).
    // Порт _try_place из Godot-версии; превью-призрак — отдельной полировкой.
    bool TryPlace(ItemData item)
    {
        if (placeCooldown > 0f) return true;
        if (item.placeablePrefab == null) return true;

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
    }

    // Установить взгляд из сохранения.
    public void ApplyView(float yaw, float newPitch)
    {
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        pitch = newPitch;
        if (head != null) head.localEulerAngles = new Vector3(pitch, 0f, 0f);
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
        head.localPosition = new Vector3(0f, headY + bobOffset + landOffset, 0f);

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

    void Move()
    {
        bool sprinting = Input.GetKey(KeyCode.LeftShift);
        bool crouching = Input.GetKey(KeyCode.LeftControl);

        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        Vector3 dir = (transform.right * h + transform.forward * v).normalized;

        float speed = crouching ? crouchSpeed
                    : sprinting ? walkSpeed * sprintMultiplier
                    : walkSpeed;

        // плавный присед: высота капсулы ползёт, голова следом (R1)
        float targetHeight = crouching ? crouchHeight : standHeight;
        if (targetHeight > cc.height + 0.001f)
        {
            // не встаём сквозь потолок
            Vector3 from = transform.position + Vector3.up * (cc.height - 0.1f);
            if (Physics.SphereCast(from, cc.radius * 0.9f, Vector3.up, out _,
                    targetHeight - cc.height + 0.15f, ~0, QueryTriggerInteraction.Ignore))
                targetHeight = cc.height;
        }
        cc.height = Mathf.MoveTowards(cc.height, targetHeight, Time.deltaTime * 6f);
        isCrouchingSmooth = crouching || cc.height < standHeight - 0.01f;

        if (cc.isGrounded)
        {
            if (!wasGrounded) // приземление: проседание жёстче от скорости падения
                landOffset = -Mathf.Clamp(-lastFallVel * 0.018f, 0f, landDipMax);
            wasGrounded = true;
            verticalVel = -2f;
            if (Input.GetKeyDown(KeyCode.Space))
                verticalVel = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }
        else
        {
            lastFallVel = Mathf.Min(lastFallVel, verticalVel);
            wasGrounded = false;
        }
        verticalVel += gravity * Time.deltaTime;

        cc.Move((dir * speed + Vector3.up * verticalVel) * Time.deltaTime);

        bool moving = dir.sqrMagnitude > 0.01f && cc.isGrounded;
        Stats.Tick(Time.deltaTime, sprinting && moving);
    }

    void UpdateInteractFocus()
    {
        IInteractable hit = null;
        var cam = head != null ? head : transform;
        if (Physics.Raycast(cam.position, cam.forward, out var info, interactRange, interactMask))
            hit = info.collider.GetComponentInParent<IInteractable>();

        // подсказка пересылается и при смене цели, и при смене текста
        // (после удара по узлу «Добыть (2/3)» должно обновиться)
        string newPrompt = hit?.GetPrompt();
        if (!ReferenceEquals(hit, focus) || newPrompt != lastPrompt)
        {
            focus = hit;
            lastPrompt = newPrompt;
            GameEvents.RaisePromptChanged(newPrompt);
        }
    }
}
