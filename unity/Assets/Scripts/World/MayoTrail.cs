using System.Collections.Generic;
using UnityEngine;

// Майонезный след (§9.2 — первая мета-механика): ведро с пробитым дном
// течёт, точки следа капают за игроком. Бег = сильнее и жирнее след,
// крадучись (присед) = слабее. Зомби ночью нюхают след (Zombie.Wander).
// Визуала (пятна-декали) пока нет — отдельным арт-прогоном.
public class MayoTrail : MonoBehaviour
{
    public static MayoTrail Instance { get; private set; }

    class TrailPoint
    {
        public Vector3 pos;
        public float strength;
        public float time;
        public GameObject spot; // кремовая лужица на земле (временный визуал)
    }

    [Header("След")]
    public float lifeSec = 45f;        // сколько пахнет
    public float stepDistance = 0.6f;  // капать не чаще, чем раз в столько метров
    public float sprintStrength = 1.8f;
    public float crouchStrength = 0.5f;
    [Tooltip("защита от разрастания списка")]
    public int maxPoints = 500;

    [Header("Визуал пятен (временный, до арт-прогона)")]
    public float spotBaseSize = 0.35f;  // радиус пятна при силе 1
    public Color spotColor = new Color(0.93f, 0.88f, 0.62f, 1f);

    readonly List<TrailPoint> points = new List<TrailPoint>();
    Player player;
    Vector3 lastDropPos;
    Material spotMat;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Instance == null)
            new GameObject("MayoTrail").AddComponent<MayoTrail>();
    }

    void Awake() => Instance = this;
    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        foreach (var p in points)
            if (p.spot != null) Destroy(p.spot);
        points.Clear();
    }

    void Update()
    {
        if (player == null)
        {
            player = FindFirstObjectByType<Player>();
            return;
        }

        // выметаем протухшее (пятна уходят вместе с запахом)
        float now = Time.time;
        for (int i = points.Count - 1; i >= 0; i--)
            if (now - points[i].time > lifeSec) RemovePoint(i);

        var cc = player.GetComponent<CharacterController>();
        if (cc == null) return;

        Vector3 flatVel = cc.velocity; flatVel.y = 0f;
        if (flatVel.magnitude < 0.3f) return;                    // стоим — не течём (почти)
        if (Vector3.Distance(player.transform.position, lastDropPos) < stepDistance) return;

        bool sprinting = Input.GetKey(KeyCode.LeftShift);
        bool crouching = cc.height < 1.5f;
        float strength = sprinting ? sprintStrength : crouching ? crouchStrength : 1f;

        var tp = new TrailPoint { pos = player.transform.position, strength = strength, time = now };
        tp.spot = SpawnSpot(tp.pos, strength);
        points.Add(tp);
        lastDropPos = player.transform.position;
        if (points.Count > maxPoints) RemovePoint(0);
    }

    void RemovePoint(int i)
    {
        if (points[i].spot != null) Destroy(points[i].spot);
        points.RemoveAt(i);
    }

    // Кремовая лужица на рельефе; размер от силы, чуть выше земли,
    // чтобы не мерцать (z-fighting).
    GameObject SpawnSpot(Vector3 pos, float strength)
    {
        if (spotMat == null)
            spotMat = new Material(Shader.Find("Standard")) { color = spotColor };

        var spot = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(spot.GetComponent<CapsuleCollider>());
        float size = spotBaseSize * (0.7f + 0.3f * strength);
        spot.transform.position = new Vector3(pos.x, TerrainGen.HeightAt(pos.x, pos.z) + 0.02f, pos.z);
        spot.transform.localScale = new Vector3(size, 0.005f, size);
        spot.transform.rotation = Quaternion.Euler(0f, Random.value * 360f, 0f);
        spot.name = "MayoSpot";
        spot.GetComponent<MeshRenderer>().sharedMaterial = spotMat;
        return spot;
    }

    // Самая «вкусная» свежая точка следа в радиусе; false — следов нет.
    public bool FreshestNear(Vector3 pos, float radius, out Vector3 result)
    {
        result = default;
        float best = 0f;
        float now = Time.time;
        foreach (var p in points)
        {
            float age = now - p.time;
            float score = p.strength * (1f - age / lifeSec);
            if (score <= best) continue;
            if (Vector3.Distance(pos, p.pos) > radius) continue;
            best = score;
            result = p.pos;
        }
        return best > 0f;
    }
}
