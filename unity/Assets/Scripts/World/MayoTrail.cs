using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Майонезный след (§9.2 — первая мета-механика): ведро с пробитым дном
// течёт, точки следа капают за игроком. Бег = сильнее и жирнее след,
// крадучись (присед) = слабее. Зомби ночью нюхают след (Zombie.Wander).
// Визуал — временные кремовые лужицы-цилиндры, до арт-прогона.
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
    public float lifeSec = 20f;        // сколько пахнет (автор: 45 — долго)
    public float stepDistance = 1.1f;  // капать не чаще; пятна идут РЕДКО, не сплошной полосой
    public float sprintStrength = 1.8f;
    public float crouchStrength = 0.5f;
    [Tooltip("защита от разрастания списка")]
    public int maxPoints = 500;

    [Header("Визуал пятен (временный, до арт-прогона)")]
    public float spotBaseSize = 0.28f;  // радиус пятна при силе 1
    public Color spotColor = new Color(0.93f, 0.88f, 0.62f, 1f);

    readonly List<TrailPoint> points = new List<TrailPoint>();
    Player player;
    CharacterController playerCc;   // кэш — не дёргать GetComponent каждый кадр
    float playerSearchRetry;        // перепоиск игрока не чаще раза в секунду
    Vector3 lastDropPos;
    Material spotMat;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        // Первичный спавн. При перезагрузке сцены Boot бежит снова, но
        // Instance уже живёт (DontDestroyOnLoad) — дубля не будет.
        if (Instance == null)
            new GameObject("MayoTrail").AddComponent<MayoTrail>();
    }

    void Awake()
    {
        // Синглтон-гвард + DontDestroyOnLoad: без этого объект умирал
        // вместе со стартовой сценой, Boot новый НЕ создавал (Instance
        // висел протухшей ссылкой) — и след был мёртв до конца сессии:
        // зомби не нюхали, пятна не капали.
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;
    void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        foreach (var p in points)
            if (p.spot != null) Destroy(p.spot);
        points.Clear();
    }

    // Новая сцена — новый мир: старые точки (и лужицы) не должны пахнуть
    // в нём, а ссылка на Player протухла вместе со старой сценой.
    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        foreach (var p in points)
            if (p.spot != null) Destroy(p.spot);
        points.Clear();
        player = null;
        playerCc = null;
    }

    void Update()
    {
        if (player == null)
        {
            // перепоиск протухшей ссылки — раз в секунду, не каждый кадр
            if (Time.time < playerSearchRetry) return;
            playerSearchRetry = Time.time + 1f;
            player = FindFirstObjectByType<Player>();
            if (player == null) return;
            playerCc = player.GetComponent<CharacterController>();
        }

        // выметаем протухшее (пятна уходят вместе с запахом)
        float now = Time.time;
        for (int i = points.Count - 1; i >= 0; i--)
            if (now - points[i].time > lifeSec) RemovePoint(i);

        var cc = playerCc;
        if (cc == null)
        {
            playerCc = cc = player.GetComponent<CharacterController>();
            if (cc == null) return;
        }

        Vector3 flatVel = cc.velocity; flatVel.y = 0f;
        if (flatVel.magnitude < 0.3f) return;                    // стоим — не течём (почти)
        if (Vector3.Distance(player.transform.position, lastDropPos) < stepDistance) return;
        if (TerrainGen.IsInOcean(player.transform.position)) return; // в океане всё и так в майонезе — след не капает

        // Публичного IsCrouching у Player нет — выводим присед по высоте
        // капсулы (порог — середина между стоя/сидя). Присед ВАЖНЕЕ
        // спринта: Shift+Ctrl — это крадущийся, а не бегущий.
        bool crouching = cc.height < (player.standHeight + player.crouchHeight) * 0.5f;
        bool sprinting = !crouching && Input.GetKey(KeyCode.LeftShift);
        float strength = crouching ? crouchStrength : sprinting ? sprintStrength : 1f;

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

    // Живые точки следа для приливов (X1 §9.4): MayoSurge строит из них
    // маршрут волны «от старейшей к свежей». Static — режиссёру приливов
    // не нужна ссылка на компонент (самодостаточность MayoSurge).
    // max — потолок длины маршрута; если живых точек больше, отрезаются
    // САМЫЕ СТАРЫЕ (берём свежий хвост), порядок «старые → свежие» сохранён.
    public static List<Vector3> RecentPoints(int max)
    {
        var result = new List<Vector3>();
        if (Instance == null || max <= 0) return result;
        float now = Time.time;
        // points хранятся в порядке добавления (старые → свежие); Update
        // выметает протухшее сам, но вызов может прийти между чистками —
        // фильтруем по возрасту, чтобы маршрут не вёл в испарившийся след.
        foreach (var p in Instance.points)
        {
            if (now - p.time > Instance.lifeSec) continue;
            result.Add(p.pos); // точки уже в мировых координатах
        }
        if (result.Count > max)
            result.RemoveRange(0, result.Count - max);
        return result;
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
