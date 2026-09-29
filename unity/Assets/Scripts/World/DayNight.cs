using UnityEngine;

// Цикл день/ночь (аналог day_night.gd): сутки 300 сек, вращение солнца,
// сумерки, тост «Наступила ночь», событие time_of_day_changed.
public class DayNight : MonoBehaviour
{
    public Light sun;
    [Min(10f)] public float dayLength = 300f; // секунд на полные сутки
    [Range(0f, 1f)] public float startTime = 0.3f; // старт утром
    public float nightStart = 0.75f;  // доля суток, с которой ночь
    public float dayStart = 0.25f;

    [Range(0f, 1f)]
    public float timeOfDay;           // 0..1
    bool wasNight;

    void Awake() { timeOfDay = startTime; }

    void Update()
    {
        timeOfDay = (timeOfDay + Time.deltaTime / dayLength) % 1f;

        bool isNight = timeOfDay >= nightStart || timeOfDay < dayStart;
        if (sun != null)
        {
            // полдень (0.5) — солнце в зените
            float sunAngle = (timeOfDay - dayStart) / (nightStart - dayStart) * 180f;
            sun.transform.rotation = Quaternion.Euler(sunAngle, 170f, 0f);
            // приглушаем солнце ночью
            float intensity = Mathf.Clamp01(Mathf.Sin(Mathf.PI * (timeOfDay - dayStart) / (nightStart - dayStart)));
            sun.intensity = isNight ? 0.02f : Mathf.Lerp(0.05f, 1.1f, intensity);
            RenderSettings.ambientIntensity = isNight ? 0.15f : Mathf.Lerp(0.35f, 1f, intensity);
        }

        if (isNight && !wasNight) GameEvents.RaiseNotify("Наступила ночь");
        if (!isNight && wasNight) GameEvents.RaiseNotify("Наступил день");
        wasNight = isNight;

        GameEvents.RaiseTimeOfDayChanged(timeOfDay, isNight);
    }
}
