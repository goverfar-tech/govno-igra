using System.Collections.Generic;
using UnityEngine;

// Майонезный след (§9.2 — первая мета-механика): ведро с пробитым дном
// течёт, точки следа капают за игроком. Бег = сильнее и жирнее след,
// крадучись (присед) = слабее. Зомби ночью нюхают след (Zombie.Wander).
// Визуала (пятна-декали) пока нет — отдельным арт-прогоном.
public class MayoTrail : MonoBehaviour
{
    public static MayoTrail Instance { get; private set; }

    class TrailPoint { public Vector3 pos; public float strength; public float time; }

    [Header("След")]
    public float lifeSec = 45f;        // сколько пахнет
    public float stepDistance = 0.6f;  // капать не чаще, чем раз в столько метров
    public float sprintStrength = 1.8f;
    public float crouchStrength = 0.5f;
    [Tooltip("защита от разрастания списка")]
    public int maxPoints = 500;

    readonly List<TrailPoint> points = new List<TrailPoint>();
    Player player;
    Vector3 lastDropPos;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Instance == null)
            new GameObject("MayoTrail").AddComponent<MayoTrail>();
    }

    void Awake() => Instance = this;
    void OnDestroy() { if (Instance == this) Instance = null; }

    void Update()
    {
        if (player == null)
        {
            player = FindFirstObjectByType<Player>();
            return;
        }

        // выметаем протухшее
        float now = Time.time;
        for (int i = points.Count - 1; i >= 0; i--)
            if (now - points[i].time > lifeSec) points.RemoveAt(i);

        var cc = player.GetComponent<CharacterController>();
        if (cc == null) return;

        Vector3 flatVel = cc.velocity; flatVel.y = 0f;
        if (flatVel.magnitude < 0.3f) return;                    // стоим — не течём (почти)
        if (Vector3.Distance(player.transform.position, lastDropPos) < stepDistance) return;

        bool sprinting = Input.GetKey(KeyCode.LeftShift);
        bool crouching = cc.height < 1.5f;
        float strength = sprinting ? sprintStrength : crouching ? crouchStrength : 1f;

        points.Add(new TrailPoint { pos = player.transform.position, strength = strength, time = now });
        lastDropPos = player.transform.position;
        if (points.Count > maxPoints) points.RemoveAt(0);
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
