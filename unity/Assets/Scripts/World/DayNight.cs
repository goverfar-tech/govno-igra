using UnityEngine;

// Цикл день/ночь (аналог day_night.gd): сутки 300 сек, вращение солнца,
// сумерки, тост «Наступила ночь», событие time_of_day_changed.
public class DayNight : MonoBehaviour
{
    public Light sun;
    // ~7 минут сутки (S-баланс): день ~3.5 мин свет на разведку —
    // «осматривайся, не сидись» из §9.5; ночь при ×1.5 утечке — давление
    [Min(10f)] public float dayLength = 420f; // секунд на полные сутки
    [Range(0f, 1f)] public float startTime = 0.3f; // старт утром
    public float nightStart = 0.75f;  // доля суток, с которой ночь
    public float dayStart = 0.25f;

    [Range(0f, 1f)]
    public float timeOfDay;           // 0..1
    bool wasNight;
    float lastSentT = -1f;            // троттлинг TimeOfDayChanged
    bool lastSentNight;

    void Awake()
    {
        timeOfDay = startTime;
        // Кривой интервал (nightStart <= dayStart) даёт деление почти на
        // ноль и вывернутый день — ругаемся один раз и разводим значения.
        if (nightStart - dayStart < 0.05f)
        {
            Debug.LogWarning("[DayNight] nightStart должен быть больше dayStart " +
                             "минимум на 0.05 — разведено автоматически.");
            nightStart = dayStart + 0.05f;
        }
    }

    void Update()
    {
        timeOfDay = (timeOfDay + Time.deltaTime / dayLength) % 1f;

        bool isNight = timeOfDay >= nightStart || timeOfDay < dayStart;
        // высота солнца 0..1 по доле светового дня (интервал > 0.05
        // гарантирован в Awake — деление безопасно)
        float intensity = Mathf.Clamp01(
            Mathf.Sin(Mathf.PI * (timeOfDay - dayStart) / (nightStart - dayStart)));
        if (sun != null)
        {
            // полдень (0.5) — солнце в зените
            float sunAngle = (timeOfDay - dayStart) / (nightStart - dayStart) * 180f;
            sun.transform.rotation = Quaternion.Euler(sunAngle, 170f, 0f);
            // приглушаем солнце ночью
            sun.intensity = isNight ? 0.02f : Mathf.Lerp(0.05f, 1.1f, intensity);
        }
        // обзор темнеет и без солнца — иначе удалённый источник света
        // оставлял бы вечный день
        RenderSettings.ambientIntensity = isNight ? 0.15f : Mathf.Lerp(0.35f, 1f, intensity);

        if (isNight && !wasNight) GameEvents.RaiseNotify("Наступила ночь");
        if (!isNight && wasNight) GameEvents.RaiseNotify("Наступил день");
        wasNight = isNight;

        // Событие — не каждый кадр, а при сдвиге t на >= 0.001 (≈0.3 с
        // реального времени) или при смене дня/ночи (фронт не теряется).
        if (isNight != lastSentNight || Mathf.Abs(timeOfDay - lastSentT) >= 0.001f)
        {
            lastSentT = timeOfDay;
            lastSentNight = isNight;
            GameEvents.RaiseTimeOfDayChanged(timeOfDay, isNight);
        }
    }
}
