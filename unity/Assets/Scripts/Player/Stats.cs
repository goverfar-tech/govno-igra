using UnityEngine;

// HP / сытость / жажда (аналог stats.gd, числа — из Godot-эталона §10).
// Майонезная мета: «еда» позже переосмыслится как уровень майонеза
// в ведре с пробитым дном (§9.2) — расход уже сейчас и есть утечка.
public class Stats : MonoBehaviour
{
    public float maxHp = 100f;
    public float maxFood = 100f;
    public float maxWater = 100f;

    [Header("Расход в секунду (Godot-эталон: еда ~10 мин, вода ~6 мин)")]
    public float foodDrain = 100f / 600f;
    public float waterDrain = 100f / 360f;
    public float starveDamage = 1f;      // HP/сек при нулевом стате
    public float thirstDamage = 1f;
    public float sprintDrainMultiplier = 1.6f;
    public float nightDrainMultiplier = 1.5f;  // ночью прохладно — расход выше

    [Header("Регенерация (еда И вода выше порога)")]
    public float regenMinStat = 70f;
    public float regenRate = 0.5f;       // HP/сек

    // Тепло костра: Campfire ставит Time.time, пока игрок рядом.
    // Пока метка свежая — ночной множитель расхода не работает.
    [System.NonSerialized] public float lastWarmTime = -10f;
    const float WarmWindowSec = 1f;

    [Header("Яд (§9.3): еда с ядом жрёт HP, пока не выветрится")]
    public float maxPoison = 100f;
    public float poisonDecay = 2f;       // ед. яда/сек выветривается
    public float poisonDps = 1.5f;       // HP/сек, пока яд > 0

    public float Hp { get; private set; }
    public float Food { get; private set; }
    public float Water { get; private set; }
    public float Poison { get; private set; }
    public bool IsDead { get; private set; }

    bool isNight;

    void Awake()
    {
        Hp = maxHp; Food = maxFood; Water = maxWater;
    }

    void OnEnable() => GameEvents.TimeOfDayChanged += OnTimeChanged;
    void OnDisable() => GameEvents.TimeOfDayChanged -= OnTimeChanged;
    void OnTimeChanged(float t, bool night) => isNight = night;

    // Вызывается из Player: знает, бежит ли игрок
    public void Tick(float dt, bool sprinting)
    {
        if (IsDead) return;
        bool warm = Time.time - lastWarmTime < WarmWindowSec;
        float mult = (sprinting ? sprintDrainMultiplier : 1f)
                   * (isNight && !warm ? nightDrainMultiplier : 1f);
        Food = Mathf.Max(0f, Food - foodDrain * mult * dt);
        Water = Mathf.Max(0f, Water - waterDrain * mult * dt);

        if (Food <= 0f) Damage(starveDamage * dt);
        if (Water <= 0f) Damage(thirstDamage * dt);

        // яд: выветривается, пока есть — жрёт HP и блокирует реген
        if (Poison > 0f)
        {
            Poison = Mathf.Max(0f, Poison - poisonDecay * dt);
            Damage(poisonDps * dt);
        }

        if (!IsDead && Poison <= 0f && Food > regenMinStat && Water > regenMinStat)
            Hp = Mathf.Min(maxHp, Hp + regenRate * dt);

        GameEvents.RaiseStatsChanged();
    }

    // Проставить значения из сохранения (SaveSystem).
    public void SetState(float hp, float food, float water)
    {
        Hp = Mathf.Clamp(hp, 0f, maxHp);
        Food = Mathf.Clamp(food, 0f, maxFood);
        Water = Mathf.Clamp(water, 0f, maxWater);
        GameEvents.RaiseStatsChanged();
    }

    public void Eat(float food, float water, float heal, float poison = 0f)
    {
        if (IsDead) return;
        Food = Mathf.Min(maxFood, Food + food);
        Water = Mathf.Min(maxWater, Water + water);
        Hp = Mathf.Min(maxHp, Hp + heal);
        if (poison > 0f)
        {
            Poison = Mathf.Min(maxPoison, Poison + poison);
            GameEvents.RaiseNotify("Отравление!");
        }
        GameEvents.RaiseStatsChanged();
    }

    public void Damage(float amount)
    {
        if (IsDead) return;
        Hp = Mathf.Max(0f, Hp - amount);
        if (Hp <= 0f)
        {
            IsDead = true;
            GameEvents.RaisePlayerDied();
        }
        GameEvents.RaiseStatsChanged();
    }
}
