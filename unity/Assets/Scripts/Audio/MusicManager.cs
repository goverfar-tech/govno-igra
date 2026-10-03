using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using System.IO;
using UnityEngine.Networking;
#endif

// MusicManager — дневная/ночная музыка (Поток Б, M5).
// Источники треков по приоритету:
//   1) Resources/Music: клипы с именами day* (day1, day_forest, …) и night* —
//      единственный путь, работающий в билде. Папки может не существовать —
//      это нормально, LoadAll вернёт пустой массив.
//   2) ТОЛЬКО В РЕДАКТОРЕ (#if UNITY_EDITOR): стриминг day*.mp3/.wav и
//      night*.mp3/.wav из Assets/Audio. В билде этой папки нет, а путь
//      проекта может содержать кириллицу/пробелы — URI экранируется
//      посегментно, весь загруз в try/catch с одним LogWarning на сессию.
// Берём по одному случайному треку на сторону суток и кроссфейдим (3–5 с)
// по TimeOfDayChanged. Луп — AudioSource.loop; для идеального шва лучше WAV
// (у mp3 кодировочный зазор на стыке петли).
// Файлов нет вообще — один Debug.Log на старте и дальше тишина, без спама.
public class MusicManager : MonoBehaviour
{
    public static MusicManager Instance { get; private set; }

    [Header("Громкость и кроссфейд")]
    [Range(0f, 1f)] public float musicVolume = 0.3f;
    [Range(3f, 5f)] public float fadeSeconds = 4f;

    AudioClip dayClip, nightClip;
    AudioSource daySource, nightSource;
    bool isNight;
    float dayTarget, nightTarget;
    // Кэш целевых громкостей: TimeOfDayChanged может идти чуть ли не каждый
    // кадр — без изменений целей ApplyTargets выходит сразу.
    float cachedDayTarget = -1f, cachedNightTarget = -1f;

    // Дедупе синглтона: дубль (повторный вход в Play и т.п.) не строит
    // источники и НЕ подписывается на события.
    bool isMain;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        new GameObject(nameof(MusicManager)).AddComponent<MusicManager>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            // Дубль: просто умираем. Подписок и источников у него нет —
            // OnEnable/OnDisable их благодаря isMain тоже не тронут.
            Destroy(gameObject);
            return;
        }
        Instance = this;
        isMain = true;
        DontDestroyOnLoad(gameObject);

        daySource = gameObject.AddComponent<AudioSource>();
        nightSource = gameObject.AddComponent<AudioSource>();
        foreach (var src in new[] { daySource, nightSource })
        {
            src.spatialBlend = 0f; // музыка всегда «в голове», не в мире
            src.loop = true;
            src.playOnAwake = false;
            src.volume = 0f;
        }
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void OnEnable() { if (isMain) GameEvents.TimeOfDayChanged += OnTimeOfDayChanged; }
    void OnDisable() { if (isMain) GameEvents.TimeOfDayChanged -= OnTimeOfDayChanged; }

    void OnTimeOfDayChanged(float t, bool night)
    {
        isNight = night;
        ApplyTargets(false);
    }

    IEnumerator Start()
    {
        // 1) Билд-безопасный путь: клипы в Resources/Music, имена day*/night*
        //    (регистр не важен). Папки может не быть — это ок.
        var days = new List<AudioClip>();
        var nights = new List<AudioClip>();
        foreach (var c in Resources.LoadAll<AudioClip>("Music"))
        {
            if (c == null) continue;
            if (c.name.StartsWith("day", StringComparison.OrdinalIgnoreCase)) days.Add(c);
            else if (c.name.StartsWith("night", StringComparison.OrdinalIgnoreCase)) nights.Add(c);
        }
        dayClip = PickRandom(days);
        nightClip = PickRandom(nights);

#if UNITY_EDITOR
        // 2) Редакторный фолбэк: постримить недостающие стороны из
        //    Assets/Audio. В билде этого кода нет.
        if (dayClip == null || nightClip == null)
        {
            string dayPath = null, nightPath = null;
            try
            {
                string dir = Path.Combine(Application.dataPath, "Audio");
                if (dayClip == null) dayPath = PickRandomPath(dir, "day");
                if (nightClip == null) nightPath = PickRandomPath(dir, "night");
            }
            catch (Exception e)
            {
                WarnDiskOnce("не прочитался Assets/Audio: " + e.Message);
            }
            if (dayPath != null) yield return LoadClip(dayPath, c => dayClip = c);
            if (nightPath != null) yield return LoadClip(nightPath, c => nightClip = c);
        }
#endif

        if (dayClip == null && nightClip == null)
        {
            Debug.Log("MusicManager: музыка не найдена (ни в Resources/Music, " +
#if UNITY_EDITOR
                      "ни в Assets/Audio; " +
#endif
                      "нужны day*.mp3/.wav, night*.mp3/.wav) — играем молча");
            yield break;
        }

        if (dayClip != null) daySource.clip = dayClip;
        if (nightClip != null) nightSource.clip = nightClip;
        ApplyTargets(true); // встать на актуальную сторону суток без фейда
    }

    static AudioClip PickRandom(List<AudioClip> list)
        => list.Count == 0 ? null : list[UnityEngine.Random.Range(0, list.Count)];

#if UNITY_EDITOR
    static bool diskWarned; // предупреждение о дисковой загрузке — раз за сессию

    static void WarnDiskOnce(string what)
    {
        if (diskWarned) return;
        diskWarned = true;
        Debug.LogWarning("MusicManager: " + what);
    }

    // Первый попавшийся случайный файл prefix*.mp3/.wav; null — если нет.
    static string PickRandomPath(string dir, string prefix)
    {
        if (!Directory.Exists(dir)) return null;
        var list = new List<string>();
        foreach (var ext in new[] { ".mp3", ".wav" })
            list.AddRange(Directory.GetFiles(dir, prefix + "*" + ext));
        return list.Count == 0 ? null : list[UnityEngine.Random.Range(0, list.Count)];
    }

    static IEnumerator LoadClip(string path, Action<AudioClip> done)
    {
        var type = path.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)
            ? AudioType.MPEG : AudioType.WAV;
        UnityWebRequest req;
        try
        {
            // Путь проекта бывает с пробелами/кириллицей — URI собираем
            // явно, с экранированием каждого сегмента.
            req = UnityWebRequestMultimedia.GetAudioClip(ToFileUri(path), type);
        }
        catch (Exception e)
        {
            WarnDiskOnce(Path.GetFileName(path) + ": " + e.Message);
            done(null);
            yield break;
        }
        using (req)
        {
            yield return req.SendWebRequest();
            if (req.result == UnityWebRequest.Result.Success)
                done(DownloadHandlerAudioClip.GetContent(req));
            else
            {
                WarnDiskOnce("не прочитался " + Path.GetFileName(path) + ": " + req.error);
                done(null);
            }
        }
    }

    // file:/// URI из локального пути: пробелы и кириллица экранируются
    // посегментно (EscapeDataString), двоеточие буквы диска не трогаем.
    static string ToFileUri(string path)
    {
        var parts = path.Replace('\\', '/').Split('/');
        for (int i = 0; i < parts.Length; i++)
        {
            if (i == 0 && parts[i].Length == 2 && parts[i][1] == ':') continue; // "E:"
            parts[i] = Uri.EscapeDataString(parts[i]);
        }
        return new Uri("file:///" + string.Join("/", parts)).AbsoluteUri;
    }
#endif

    // Целевые громкости: активна сторона текущего времени суток;
    // если её трека нет — продолжает играть другая сторона.
    void ApplyTargets(bool instant)
    {
        AudioSource want = isNight
            ? (nightSource.clip != null ? nightSource : daySource)
            : (daySource.clip != null ? daySource : nightSource);
        float d = want == daySource && daySource.clip != null ? musicVolume : 0f;
        float n = want == nightSource && nightSource.clip != null ? musicVolume : 0f;
        // Дешёвый кэш: цели не изменились — источники не трогаем.
        if (!instant && d == cachedDayTarget && n == cachedNightTarget) return;
        cachedDayTarget = d;
        cachedNightTarget = n;
        dayTarget = d;
        nightTarget = n;
        if (instant)
        {
            daySource.volume = dayTarget;
            nightSource.volume = nightTarget;
        }
    }

    void Update()
    {
        // unscaled: кроссфейд не замирает на паузе (PauseChanged ставит timeScale=0)
        float step = musicVolume / fadeSeconds * Time.unscaledDeltaTime;
        FadeSource(daySource, dayTarget, step);
        FadeSource(nightSource, nightTarget, step);
    }

    static void FadeSource(AudioSource src, float target, float step)
    {
        if (src.clip == null) return;
        if (target > 0f && !src.isPlaying) src.Play();
        src.volume = Mathf.MoveTowards(src.volume, target, step);
        if (target == 0f && src.volume == 0f && src.isPlaying) src.Stop();
    }
}
