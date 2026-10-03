using System.Collections.Generic;
using UnityEngine;

// Курица (§9.3): дружелюбный источник яиц для домашнего майонеза.
// Бродит вокруг якоря, несёт яйцо раз в layInterval, НО вокруг неё
// одновременно лежит не больше maxEggsAround — бесконечной фермы нет.
// Сейчас якорь ставит SetupMainScene (~70 м от спауна); при карте
// 1000×1000 кольцо спауна будет 500–1000 м (правило баланса §9 добавки).
[RequireComponent(typeof(CharacterController))]
public class Chicken : MonoBehaviour
{
    public float wanderRadius = 15f;
    public float walkSpeed = 1.2f;
    public float layInterval = 180f;
    public int maxEggsAround = 3;
    public ItemData eggItem;
    public PickupItem pickupPrefab;

    static readonly List<GameObject> eggs = new List<GameObject>();

    CharacterController cc;
    Vector3 anchor;
    Vector3 target;
    float repick;
    float layTimer;

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        anchor = transform.position;
        target = anchor;
        layTimer = layInterval * 0.5f; // первое яйцо не сразу, но и не через 3 мин
        if (GetComponentInChildren<MeshRenderer>() == null) BuildBody(transform);
    }

    void Update()
    {
        repick -= Time.deltaTime;
        if (repick <= 0f || Vector3.Distance(transform.position, target) < 0.6f)
        {
            repick = Random.Range(3f, 7f);
            target = PickWanderTarget();
        }

        Vector3 flat = target - transform.position;
        flat.y = 0f;
        if (flat.sqrMagnitude > 0.05f)
        {
            transform.rotation = Quaternion.Slerp(transform.rotation,
                Quaternion.LookRotation(flat), Time.deltaTime * 5f);
            cc.Move(flat.normalized * walkSpeed * Time.deltaTime);
        }
        cc.Move(Physics.gravity * Time.deltaTime);

        layTimer -= Time.deltaTime;
        if (layTimer <= 0f)
        {
            layTimer = layInterval;
            LayEgg();
        }
    }

    // Цель блуждания: случайная вокруг якоря, но НЕ в озере
    // (до 5 попыток; не вышло — стоим на месте до следующего репика).
    Vector3 PickWanderTarget()
    {
        for (int i = 0; i < 5; i++)
        {
            float ang = Random.value * Mathf.PI * 2f;
            float r = Random.Range(0f, wanderRadius);
            var p = anchor + new Vector3(Mathf.Cos(ang) * r, 0, Mathf.Sin(ang) * r);
            if (Vector2.Distance(new Vector2(p.x, p.z), TerrainGen.LakeCenter)
                < TerrainGen.LakeRadius + 2f)
                continue;
            p.y = TerrainGen.HeightAt(p.x, p.z) + 0.55f;
            return p;
        }
        return transform.position; // постоим
    }

    void LayEgg()
    {
        if (eggItem == null || pickupPrefab == null) return;

        // Считаем ВСЕ яйца в мире: свой список (почистив null) плюс
        // любые PickupItem с eggItem — выброшенные игроком тоже в счёт.
        var seen = new HashSet<GameObject>();
        int total = 0;
        eggs.RemoveAll(e => e == null);
        foreach (var e in eggs)
            if (seen.Add(e)) total++;
        foreach (var p in FindObjectsByType<PickupItem>(FindObjectsSortMode.None))
            if (p.item == eggItem && seen.Add(p.gameObject)) total++;
        if (total >= maxEggsAround) return; // фермы не будет

        // +0.15 вверх за спиной; посадку на землю делает сам PickupItem
        var egg = Instantiate(pickupPrefab,
            transform.position + transform.forward * -0.4f + Vector3.up * 0.15f,
            Quaternion.identity);
        egg.item = eggItem;
        egg.count = 1;
        egg.name = "Egg";
        egg.transform.localScale = Vector3.one * 0.35f; // яйцо, а не бревно
        eggs.Add(egg.gameObject);
        AudioManager.SquealAt(transform.position); // кудахтанье
    }

    // Тело из кубов: spooky-cute курица (§9.1).
    static Material bodyMat, beakMat;
    static void BuildBody(Transform root)
    {
        if (bodyMat == null)
        {
            bodyMat = new Material(Shader.Find("Standard")) { color = new Color(0.92f, 0.9f, 0.84f) };
            beakMat = new Material(Shader.Find("Standard")) { color = new Color(0.95f, 0.6f, 0.1f) };
        }
        Part(root, new Vector3(0f, 0.42f, 0f), new Vector3(0.42f, 0.36f, 0.5f), bodyMat);  // корпус
        Part(root, new Vector3(0f, 0.72f, 0.24f), new Vector3(0.24f, 0.24f, 0.24f), bodyMat); // голова
        Part(root, new Vector3(0f, 0.7f, 0.42f), new Vector3(0.12f, 0.08f, 0.14f), beakMat);  // клюв
        Part(root, new Vector3(0f, 0.88f, 0.22f), new Vector3(0.08f, 0.12f, 0.1f), beakMat);  // гребешок
        Part(root, new Vector3(-0.12f, 0.12f, 0f), new Vector3(0.08f, 0.24f, 0.08f), beakMat); // нога Л
        Part(root, new Vector3(0.12f, 0.12f, 0f), new Vector3(0.08f, 0.24f, 0.08f), beakMat);  // нога П
    }

    static void Part(Transform parent, Vector3 localPos, Vector3 size, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(go.GetComponent<BoxCollider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = size;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
    }
}
