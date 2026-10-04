using UnityEngine;

// Конечности майонезного ведра (S/тело 2026-10-04): короткие ножки и
// ручки из кубов — тот же spooky-cute приём, что у зомби. Процедурная
// анимация без рига: шаг/бег — махи ног в противофазе, руки в
// противоходе; прыжок/падение — поза «по воздуху»; удар — замах правой
// рукой (PlaySwing зовёт Player.SwingTool и E-рубка); полёт — «плывёт»;
// смерть — обмякает. Ведро стоит НА ногах: Setup поднимает его на
// LegHeight, камера выросла вместе с телом (мир видно дальше).
// Кубы БЕЗ коллайдеров: хитбокс игрока — только CharacterController,
// иначе конечности ловили бы лучи зомби и рейкасты мира.
public class BucketRig : MonoBehaviour
{
    const float LegHeight = 0.55f;   // подъём ведра — сверён с SetupMainScene
    const float LegHalf = 0.18f;     // половина расстановки ног по X
    const float LegSize = 0.18f;     // толщина ноги
    const float ShoulderY = LegHeight + 0.83f; // плечи — середина ведра
    const float ArmHalf = 0.56f;     // по бокам ведра (радиус ~0.45 + зазор)
    const float ArmSize = 0.15f;
    const float ArmLen = 0.5f;       // длина руки от плеча
    const float SwingTime = 0.45f;   // короче swingCooldown (0.6) — замах вписывается
    const float WalkRefSpeed = 6.4f; // walk×sprint игрока — нормировка маха

    Player player;
    CharacterController cc;
    Transform legL, legR, armL, armR;
    float phase;
    float swingT = -1f; // >=0 — идёт замах правой рукой

    static Material limbMat, mittenMat;

    void Start()
    {
        player = GetComponent<Player>();
        cc = GetComponent<CharacterController>();
        Build();
    }

    void Build()
    {
        if (limbMat == null)
        {
            limbMat = new Material(Shader.Find("Standard"))
                { color = new Color(0.92f, 0.90f, 0.86f) }; // белое ведро-пластик
            mittenMat = new Material(Shader.Find("Standard"))
                { color = new Color(0.93f, 0.88f, 0.62f) }; // «руки в майонезе»
        }
        legL = Limb("LegL", new Vector3(-LegHalf, LegHeight, 0f), LegSize, LegHeight, limbMat);
        legR = Limb("LegR", new Vector3(LegHalf, LegHeight, 0f), LegSize, LegHeight, limbMat);
        armL = Limb("ArmL", new Vector3(-ArmHalf, ShoulderY, 0f), ArmSize, ArmLen, mittenMat);
        armR = Limb("ArmR", new Vector3(ArmHalf, ShoulderY, 0f), ArmSize, ArmLen, mittenMat);
    }

    // Пивот конечности — в суставе (бедро/плечо), куб свисает вниз:
    // вращение пивота даёт честный мах (как руки зомби).
    Transform Limb(string name, Vector3 jointPos, float size, float len, Material mat)
    {
        var joint = new GameObject(name).transform;
        joint.SetParent(transform, false);
        joint.localPosition = jointPos;
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(go.GetComponent<BoxCollider>()); // хитбокс — только CharacterController
        go.transform.SetParent(joint, false);
        go.transform.localPosition = new Vector3(0f, -len * 0.5f, 0f);
        go.transform.localScale = new Vector3(size, len, size);
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return joint;
    }

    // Замах оружием/удар рукой: правая рука вскидывается и рубит вниз.
    public void PlaySwing() => swingT = SwingTime;

    void Update()
    {
        if (legL == null || player == null || player.Stats == null) return;
        float dt = Time.deltaTime;

        // СУСТАВЫ ПРИВЯЗАНЫ К ПОЛУ КАПСУЛЫ, а не к пивоту (фикс 2026-10-04:
        // пивот — центр капсулы 2.2; суставы, поставленные «от земли»,
        // давали ноги внутри ведра и руки у головы — замах проходил сквозь
        // камеру). groundLocal = низ капсулы в локале пивота; присед сжимает
        // капсулу — конечности сжимаются вместе с ней, ноги остаются на полу.
        float groundLocal = cc != null ? -cc.height * 0.5f : -1.1f;
        legL.localPosition = new Vector3(-LegHalf, groundLocal + LegHeight, 0f);
        legR.localPosition = new Vector3(LegHalf, groundLocal + LegHeight, 0f);
        armL.localPosition = new Vector3(-ArmHalf, groundLocal + ShoulderY, 0f);
        armR.localPosition = new Vector3(ArmHalf, groundLocal + ShoulderY, 0f);

        // мёртвое ведро обмякает: конечности медленно сползают в ноль
        if (player.Stats.IsDead)
        {
            Limp(legL, dt); Limp(legR, dt); Limp(armL, dt); Limp(armR, dt);
            return;
        }

        Vector3 hv = cc != null ? cc.velocity : Vector3.zero;
        hv.y = 0f;
        float speedNorm = Mathf.Clamp01(hv.magnitude / WalkRefSpeed);
        bool grounded = cc != null && cc.isGrounded;

        if (player.IsFlying)
        {
            // полёт: ноги лениво болтаются, руки полураскрыты — «плывёт»
            float sway = Mathf.Sin(Time.time * 2.2f) * 10f;
            legL.localRotation = Quaternion.Euler(sway, 0f, 4f);
            legR.localRotation = Quaternion.Euler(-sway, 0f, -4f);
            armL.localRotation = Quaternion.Euler(-35f, 0f, 22f);
            armR.localRotation = Quaternion.Euler(-35f, 0f, -22f);
            swingT = -1f;
            return;
        }

        if (!grounded)
        {
            // в воздухе: передняя нога вытянута, задняя поджата, руки вскинуты;
            // замах в прыжке не показываем — доударит после приземления
            legL.localRotation = Quaternion.Lerp(legL.localRotation,
                Quaternion.Euler(30f, 0f, 0f), dt * 10f);
            legR.localRotation = Quaternion.Lerp(legR.localRotation,
                Quaternion.Euler(-16f, 0f, 0f), dt * 10f);
            armL.localRotation = Quaternion.Lerp(armL.localRotation,
                Quaternion.Euler(-45f, 0f, 12f), dt * 10f);
            armR.localRotation = Quaternion.Lerp(armR.localRotation,
                Quaternion.Euler(-45f, 0f, -12f), dt * 10f);
            return;
        }

        // шаг/бег: частота и амплитуда от скорости; присед — семенит мельче
        phase += dt * (5f + speedNorm * 7f);
        float amp = Mathf.Lerp(4f, player.IsCrouching ? 24f : 38f, speedNorm);
        float swing = Mathf.Sin(phase) * amp;
        legL.localRotation = Quaternion.Euler(swing, 0f, 0f);
        legR.localRotation = Quaternion.Euler(-swing, 0f, 0f);
        armL.localRotation = Quaternion.Euler(-swing * 0.6f, 0f, 3f);
        if (swingT < 0f)
            armR.localRotation = Quaternion.Euler(swing * 0.6f, 0f, -3f);

        // удар: взлёт руки назад-вверх и рубящий мах вниз (правая рука)
        if (swingT >= 0f)
        {
            swingT -= dt;
            float t = 1f - Mathf.Clamp01(swingT / SwingTime);
            float lift = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t * 2.2f));
            float chop = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.45f) / 0.55f));
            float angle = Mathf.Lerp(0f, -150f, lift) + Mathf.Lerp(0f, 175f, chop);
            armR.localRotation = Quaternion.Euler(angle, 0f, -8f);
            if (swingT < 0f) armR.localRotation = Quaternion.identity;
        }
    }

    void Limp(Transform limb, float dt)
        => limb.localRotation = Quaternion.Lerp(limb.localRotation,
            Quaternion.identity, dt * 3f);
}
