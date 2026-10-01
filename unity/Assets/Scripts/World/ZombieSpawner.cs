using System.Collections.Generic;
using UnityEngine;

// Спавнит зомби ночью кольцом вокруг игрока (M5; приливы §9.4 — позже).
// Зомби сами дематериализуются на рассвете (см. Zombie.OnTime).
public class ZombieSpawner : MonoBehaviour
{
    public int perNight = 3;
    public float minDist = 22f;
    public float maxDist = 32f;
    public ItemData dropItem;         // сырая плоть
    public PickupItem pickupPrefab;

    static readonly List<Zombie> alive = new List<Zombie>();
    bool wasNight;

    void OnEnable() => GameEvents.TimeOfDayChanged += OnTime;
    void OnDisable() => GameEvents.TimeOfDayChanged -= OnTime;

    void OnTime(float t, bool night)
    {
        // Событие идёт КАЖДЫЙ кадр — спавним только на фронте ночи,
        // иначе за ночь заспавнятся тысячи.
        if (!night) { wasNight = false; return; }
        if (wasNight) return;
        wasNight = true;

        var player = FindFirstObjectByType<Player>();
        if (player == null) return;
        alive.RemoveAll(z => z == null);
        int cap = Mathf.Max(perNight * 2, 6); // предохранитель от нашествия
        int toSpawn = Mathf.Min(perNight, cap - alive.Count);
        if (toSpawn <= 0) return;

        for (int i = 0; i < toSpawn; i++)
        {
            float ang = Random.value * Mathf.PI * 2f;
            float r = Random.Range(minDist, maxDist);
            var pp = player.transform.position;
            float x = pp.x + Mathf.Cos(ang) * r, z = pp.z + Mathf.Sin(ang) * r;
            // не вылезти за карту
            x = Mathf.Clamp(x, -95f, 95f);
            z = Mathf.Clamp(z, -95f, 95f);
            SpawnZombie(new Vector3(x, TerrainGen.HeightAt(x, z) + 1f, z));
        }
        GameEvents.RaiseNotify("Из тумана доносится чавканье…");
    }

    void SpawnZombie(Vector3 pos)
    {
        var root = new GameObject("Zombie");
        root.transform.position = pos;
        var cc = root.AddComponent<CharacterController>();
        cc.height = 1.7f;
        cc.radius = 0.4f;
        cc.center = new Vector3(0f, 0.85f, 0f);

        var zombie = root.AddComponent<Zombie>();
        zombie.dropItem = dropItem;
        zombie.pickupPrefab = pickupPrefab;
        BuildBody(root.transform);
        alive.Add(zombie);
    }

    // Тело из кубов с вытянутыми вперёд руками — spooky-cute (§9.1).
    static Material bodyMat, eyeMat;
    static void BuildBody(Transform root)
    {
        if (bodyMat == null)
        {
            bodyMat = new Material(Shader.Find("Standard"))
                { color = new Color(0.45f, 0.58f, 0.38f) };
            eyeMat = new Material(Shader.Find("Standard")) { color = Color.red };
            eyeMat.EnableKeyword("_EMISSION");
            eyeMat.SetColor("_EmissionColor", Color.red * 1.8f);
        }

        Part(root, new Vector3(0f, 1.05f, 0f), new Vector3(0.55f, 0.7f, 0.3f), bodyMat);   // торс
        Part(root, new Vector3(0f, 1.6f, 0f), new Vector3(0.4f, 0.4f, 0.4f), bodyMat);    // голова
        Part(root, new Vector3(-0.18f, 0.35f, 0f), new Vector3(0.18f, 0.7f, 0.18f), bodyMat); // нога Л
        Part(root, new Vector3(0.18f, 0.35f, 0f), new Vector3(0.18f, 0.7f, 0.18f), bodyMat);  // нога П
        Part(root, new Vector3(-0.32f, 1.2f, 0.35f), new Vector3(0.15f, 0.15f, 0.6f), bodyMat); // рука Л (вперёд)
        Part(root, new Vector3(0.32f, 1.2f, 0.35f), new Vector3(0.15f, 0.15f, 0.6f), bodyMat);  // рука П
        Part(root, new Vector3(-0.1f, 1.65f, 0.2f), new Vector3(0.07f, 0.07f, 0.05f), eyeMat);  // глаз Л
        Part(root, new Vector3(0.1f, 1.65f, 0.2f), new Vector3(0.07f, 0.07f, 0.05f), eyeMat);   // глаз П
    }

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
