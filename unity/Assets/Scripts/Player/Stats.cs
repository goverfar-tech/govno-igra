using UnityEngine;

// HP / сытость / жажда (аналог stats.gd).
public class Stats : MonoBehaviour
{
    public float maxHp = 100f;
    public float maxFood = 100f;
    public float maxWater = 100f;

    [Header("Расход в секунду (бег ×1.6)")]
    public float foodDrain = 0.35f;
    public float waterDrain = 0.55f;
    public float starveDamage = 1f;   // HP/сек при нулевой сытости
    public float thirstDamage = 1.5f; // HP/сек при нулевой жажде
    public float sprintDrainMultiplier = 1.6f;

    public float Hp { get; private set; }
    public float Food { get; private set; }
    public float Water { get; private set; }
    public bool IsDead { get; private set; }

    void Awake()
    {
        Hp = maxHp; Food = maxFood; Water = maxWater;
    }

    // Вызывается из Player: знает, бежит ли игрок
    public void Tick(float dt, bool sprinting)
    {
        if (IsDead) return;
        float mult = sprinting ? sprintDrainMultiplier : 1f;
        Food = Mathf.Max(0f, Food - foodDrain * mult * dt);
        Water = Mathf.Max(0f, Water - waterDrain * mult * dt);

        if (Food <= 0f) Damage(starveDamage * dt);
        if (Water <= 0f) Damage(thirstDamage * dt);
        GameEvents.RaiseStatsChanged();
    }

    public void Eat(float food, float water, float heal)
    {
        if (IsDead) return;
        Food = Mathf.Min(maxFood, Food + food);
        Water = Mathf.Min(maxWater, Water + water);
        Hp = Mathf.Min(maxHp, Hp + heal);
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
