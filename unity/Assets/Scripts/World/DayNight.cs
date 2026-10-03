using UnityEngine;

// Цикл день/ночь (аналог day_night.gd): вращение солнца, сумерки,
// тосты «Наступила ночь/день», событие TimeOfDayChanged.
// Небо (2026-10-03): видимые диски солнца и луны (билборды вокруг
// камеры), лунный холодный свет ночью, дрейфующий слой облаков.
// Диски/облака создаёт Setup (шейдер Sky/SkyUnlit — без тумана),
// текстуры — сгенерированные PNG-ассеты.
public class DayNight : MonoBehaviour
{
    public Light sun;
    // ~7 минут сутки (S-баланс): день ~3.5 мин свет на разведку —
    // «осматривайся, не сидись» из §9.5; ночь при ×1.5 утечке — давление
    [Min(10f)] public float dayLength = 420f; // секунд на полные сутки
    [Range(0f, 1f)] public float startTime = 0.3f; // старт утром
    public float nightStart = 0.75f;  // доля суток, с которой ночь
    public float dayStart = 0.25f;

    [Header("Небо (заполняет Setup; можно оставить пустым)")]
    public Renderer sunDisc;
    public Renderer moonDisc;
    public Renderer cloudLayer;
    public float skyDist = 450f;      // дистанция дисков от камеры
    public float cloudHeight = 78f;   // высота слоя облаков, м
    public float cloudDrift = 0.006f; // дрейф облаков, UV/с

    [Range(0f, 1f)]
    public float timeOfDay;           // 0..1
    bool wasNight;
    float lastSentT = -1f;            // троттлинг TimeOfDayChanged
    bool lastSentNight;

    static readonly Color DaySunColor = new Color(1f, 0.96f, 0.88f);
    static readonly Color SunsetColor = new Color(1f, 0.72f, 0.45f);
    static readonly Color MoonlightColor = new Color(0.62f, 0.72f, 0.95f);
    static readonly Color CloudDayColor = new Color(1f, 1f, 1f, 1f);
    static readonly Color CloudNightColor = new Color(0.10f, 0.11f, 0.16f, 1f);

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
            if (isNight)
            {
                // луна: холодный слабый свет вместо тьмы
                sun.color = MoonlightColor;
                sun.intensity = 0.18f;
            }
            else
            {
                // у горизонта теплей — закат/рассвет
                sun.color = Color.Lerp(SunsetColor, DaySunColor, intensity);
                sun.intensity = Mathf.Lerp(0.05f, 1.1f, intensity);
            }
        }
        // обзор темнеет и без солнца — иначе удалённый источник света
        // оставлял бы вечный день
        RenderSettings.ambientIntensity = isNight ? 0.15f : Mathf.Lerp(0.35f, 1f, intensity);

        UpdateSky(intensity, isNight);

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

    // Солнце/луна — билборды вокруг камеры; облака ходят за камерой
    // и медленно дрейфуют. Альфа дисков гасится вне «их» времени суток.
    void UpdateSky(float dayIntensity, bool isNight)
    {
        var cam = Camera.main;
        Vector3 cp = cam != null ? cam.transform.position : Vector3.zero;

        if (sunDisc != null && sun != null)
        {
            float a = isNight ? 0f : Mathf.Clamp01(dayIntensity * 1.2f + 0.15f);
            PlaceDisc(sunDisc, cp - sun.transform.forward * skyDist, a);
        }
        if (moonDisc != null && sun != null)
        {
            // луна — в противосолнечной точке: выше горизонта ночью
            float np = ((timeOfDay - nightStart) + 1f) % 1f / ((1f - nightStart) + dayStart);
            float a = isNight ? Mathf.Clamp01(Mathf.Sin(Mathf.PI * np) * 1.3f) : 0f;
            PlaceDisc(moonDisc, cp + sun.transform.forward * skyDist, a);
        }
        if (cloudLayer != null)
        {
            var t = cloudLayer.transform;
            t.position = new Vector3(cp.x, cloudHeight, cp.z);
            var m = cloudLayer.sharedMaterial;
            if (m != null)
            {
                m.mainTextureOffset = new Vector2(
                    (Time.time * cloudDrift) % 1f, (Time.time * cloudDrift * 0.45f) % 1f);
                m.color = Color.Lerp(CloudNightColor, CloudDayColor,
                                     isNight ? 0.12f : dayIntensity);
            }
        }
    }

    void PlaceDisc(Renderer r, Vector3 pos, float alpha)
    {
        var cam = Camera.main;
        Vector3 cp = cam != null ? cam.transform.position : Vector3.zero;
        var t = r.transform;
        t.position = pos;
        // Quad «лицом» по -Z — разворачиваем плоскость от камеры
        t.rotation = Quaternion.LookRotation(pos - cp);
        var m = r.sharedMaterial;
        if (m != null)
        {
            var c = m.color;
            c.a = alpha;
            m.color = c;
        }
    }
}
