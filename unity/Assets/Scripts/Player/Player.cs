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

    public Inventory Inventory { get; private set; }
    public Stats Stats { get; private set; }

    CharacterController cc;
    float pitch;
    float verticalVel;
    float placeCooldown;
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

    void Update()
    {
        placeCooldown -= Time.deltaTime;
        if (Stats.IsDead) return;
        Look();
        Move();
        UpdateInteractFocus();

        if (Input.GetKeyDown(KeyCode.E) && focus != null)
            focus.Interact(this);

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

    // ЛКМ: постройка > расходник (Godot-эталон: player.gd use_selected_item)
    void UseSelected()
    {
        var slot = Inventory.SelectedSlot;
        if (slot.IsEmpty) return;
        if (slot.item.isPlaceable && TryPlace(slot.item)) return;
        Inventory.UseSelected(this);
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
        GameEvents.RaiseNotify("Построено: " + item.displayName);
        return true;
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

        cc.height = crouching ? crouchHeight : standHeight;

        if (cc.isGrounded)
        {
            verticalVel = -2f;
            if (Input.GetKeyDown(KeyCode.Space))
                verticalVel = Mathf.Sqrt(jumpHeight * -2f * gravity);
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
