using UnityEngine;

// МАЙОНЕЗ — единый ресурс (§9.2): здоровье и сытость в одном ведре.
// Течёт сам (дыра в дне), бег/холодная ночь ускоряют, костёр греет,
// зомби выбивают ударами. Пустое ведро = смерть.
// Яд (§9.3) — отдельный статус: выветривается, но пока есть — жрёт майонез.
public class Stats : MonoBehaviour
{
    [Header("Майонез")]
    public float maxMayo = 100f;
    [Tooltip("утечка в секунду: ~8 минут до пустого ведра")]
    public float leakPerSec = 100f / 460f;
    public float sprintLeakMultiplier = 1.6f;
    public float nightLeakMultiplier = 1.5f;   // холодная ночь

    [Header("Яд")]
    public float maxPoison = 100f;
    public float poisonDecay = 2f;             // ед. яда/сек выветривается
    public float poisonMayoDps = 1.5f;         // майонеза/сек, пока яд > 0

    // Тепло костра: Campfire ставит Time.time, пока игрок рядом.
    [System.NonSerialized] public float lastWarmTime = -10f;
    const float WarmWindowSec = 1f;

    public float Mayo { get; private set; }
    public float Poison { get; private set; }
    public bool IsDead { get; private set; }

    bool isNight;

    void Awake() => Mayo = maxMayo;

    void OnEnable() => GameEvents.TimeOfDayChanged += OnTimeChanged;
    void OnDisable() => GameEvents.TimeOfDayChanged -= OnTimeChanged;
    void OnTimeChanged(float t, bool night) => isNight = night;

    // Вызывается из Player: знает, бежит ли игрок
    public void Tick(float dt, bool sprinting)
    {
        if (IsDead) return;
        bool warm = Time.time - lastWarmTime < WarmWindowSec;
        float mult = (sprinting ? sprintLeakMultiplier : 1f)
                   * (isNight && !warm ? nightLeakMultiplier : 1f);
        Mayo = Mathf.Max(0f, Mayo - leakPerSec * mult * dt);

        if (Poison > 0f)
        {
            Poison = Mathf.Max(0f, Poison - poisonDecay * dt);
            Mayo = Mathf.Max(0f, Mayo - poisonMayoDps * dt);
        }

        if (Mayo <= 0f)
        {
            IsDead = true;
            GameEvents.RaisePlayerDied();
        }
        GameEvents.RaiseStatsChanged();
    }

    // Еда/варево/питьё майонеза. heal слился в mayo (§9.2).
    public void Feed(float mayoRestore, float poison = 0f)
    {
        if (IsDead) return;
        Mayo = Mathf.Min(maxMayo, Mayo + mayoRestore);
        if (poison > 0f)
        {
            Poison = Mathf.Min(maxPoison, Poison + poison);
            GameEvents.RaiseNotify("Отравление!");
        }
        GameEvents.RaiseStatsChanged();
    }

    // Урон выбивает майонез из пробитого ведра.
    public void Damage(float amount)
    {
        if (IsDead) return;
        Mayo = Mathf.Max(0f, Mayo - amount);
        if (Mayo <= 0f)
        {
            IsDead = true;
            GameEvents.RaisePlayerDied();
        }
        GameEvents.RaiseStatsChanged();
    }

    // Проставить значения из сохранения (SaveSystem). Загрузка = оживление:
    // тело при смерти не уничтожается, поэтому IsDead сбрасываем — иначе
    // смерть необратима. StatsChanged летит всегда: UI по нему скроет
    // экран смерти.
    public void SetState(float mayo, float poison)
    {
        Mayo = Mathf.Clamp(mayo, 0f, maxMayo);
        Poison = Mathf.Clamp(poison, 0f, maxPoison);
        IsDead = false;
        GameEvents.RaiseStatsChanged();
    }
}
