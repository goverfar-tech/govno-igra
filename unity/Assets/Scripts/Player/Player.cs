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
    IInteractable focus;

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
        if (Stats.IsDead) return;
        Look();
        Move();
        UpdateInteractFocus();

        if (Input.GetKeyDown(KeyCode.E) && focus != null)
            focus.Interact(this);

        if (Input.GetMouseButtonDown(0))
            Inventory.UseSelected(this);

        for (int i = 0; i < Inventory.HotbarSize; i++)
            if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                Inventory.Select(i);

        float wheel = Input.GetAxis("Mouse ScrollWheel");
        if (wheel > 0f) Inventory.Select((Inventory.selected + 1) % Inventory.HotbarSize);
        if (wheel < 0f) Inventory.Select((Inventory.selected + Inventory.HotbarSize - 1) % Inventory.HotbarSize);
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

        if (!ReferenceEquals(hit, focus))
        {
            focus = hit;
            GameEvents.RaisePromptChanged(focus?.GetPrompt());
        }
    }
}
