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

    // Герметики (X2, §9.4): дыра замазывается по мере прогресса
    // смола → воск → битум, каждый слой ПОСТОЯННО (до конца сейва)
    // снижает утечку. Таблица множителей по уровню замазки 0..3:
    // смола −15%, воск −30%, битум −45% — шаги равномерные, чтобы каждый
    // герметик ощущался, но даже битум не отменяет утечку: дыра остаётся
    // дырой (фоновое давление голода никуда не девается ~8 мин → ~14 мин).
    // Обратная кривая сложности §9.4: лучшие герметики дальше от старта.
    static readonly float[] SealantLeakMult = { 1f, 0.85f, 0.70f, 0.55f };
    public int SealantLevel { get; private set; }   // 0..3
    public float SealantLeakMultiplier =>
        SealantLeakMult[Mathf.Clamp(SealantLevel, 0, SealantLeakMult.Length - 1)];

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
        // герметик (X2) умножает ИТОГОВУЮ утечку — поверх бега/ночи,
        // не вместо: замазанное ведро медленнее течёт и в покое, и в спешке
        float mult = (sprinting ? sprintLeakMultiplier : 1f)
                   * (isNight && !warm ? nightLeakMultiplier : 1f)
                   * SealantLeakMultiplier;
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

    // Замазать дыру герметиком тира tier (X2, §9.4). false — дыра уже
    // замазана таким или лучшим слоем: предмет НЕ тратится (сравнение
    // тиров, слабый герметик остаётся запасом). Тост успеха здесь, а не
    // в Player: Stats сам знает новый множитель; Player тостит только отказ.
    public bool ApplySealant(int tier)
    {
        if (IsDead) return false;
        tier = Mathf.Clamp(tier, 1, SealantLeakMult.Length - 1);
        if (tier <= SealantLevel) return false;
        SealantLevel = tier;
        GameEvents.RaiseStatsChanged();
        GameEvents.RaiseNotify($"Дыра замазана: утечка теперь {Mathf.RoundToInt(SealantLeakMultiplier * 100f)}%");
        return true;
    }

    // Тихое восстановление уровня из сейва (SaveSystem.Load): без тостов
    // и событий — загрузка не должна «применять» герметик заново.
    // Старый сейв v2 без sealantLevel даст 0 — дыра не замазана.
    public void RestoreSealant(int level) =>
        SealantLevel = Mathf.Clamp(level, 0, SealantLeakMult.Length - 1);
}
