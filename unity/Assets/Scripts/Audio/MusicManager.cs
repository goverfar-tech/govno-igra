using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

// MusicManager — дневная/ночная музыка из Assets/Audio (Поток Б, M5).
// Кладём файлы day*.mp3|day*.wav и night*.mp3|night*.wav — менеджер берёт
// по одному случайному на сторону суток и кроссфейдит (3–5 сек) по
// TimeOfDayChanged. Луп — AudioSource.loop; для идеального шва лучше WAV
// (у mp3 есть кодировочный зазор на стыке петли).
// Файлов нет — один Debug.Log на старте и дальше тишина, без спама.
// Внимание: читаем файлы с диска, поэтому для сборок папку надо будет
// перенести в StreamingAssets — в билд Assets/Audio не упаковывается.
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
            Destroy(gameObject);
            return;
        }
        Instance = this;
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

    void OnEnable() => GameEvents.TimeOfDayChanged += OnTimeOfDayChanged;
    void OnDisable() => GameEvents.TimeOfDayChanged -= OnTimeOfDayChanged;

    void OnTimeOfDayChanged(float t, bool night)
    {
        isNight = night;
        ApplyTargets(false);
    }

    IEnumerator Start()
    {
        string dir = Path.Combine(Application.dataPath, "Audio");
        string dayPath = PickRandom(dir, "day");
        string nightPath = PickRandom(dir, "night");
        if (dayPath == null && nightPath == null)
        {
            Debug.Log("MusicManager: музыка не найдена в Assets/Audio " +
                      "(нужны day*.mp3/.wav, night*.mp3/.wav) — играем молча");
            yield break;
        }

        yield return LoadClip(dayPath, c => dayClip = c);
        yield return LoadClip(nightPath, c => nightClip = c);

        if (dayClip != null) daySource.clip = dayClip;
        if (nightClip != null) nightSource.clip = nightClip;
        ApplyTargets(true); // встать на актуальную сторону суток без фейда
    }

    // Первый попавшийся случайный файл prefix*.mp3/.wav; null — если нет.
    static string PickRandom(string dir, string prefix)
    {
        if (!Directory.Exists(dir)) return null;
        var list = new List<string>();
        foreach (var ext in new[] { ".mp3", ".wav" })
            list.AddRange(Directory.GetFiles(dir, prefix + "*" + ext));
        return list.Count == 0 ? null : list[Random.Range(0, list.Count)];
    }

    static IEnumerator LoadClip(string path, Action<AudioClip> done)
    {
        if (path == null) { done(null); yield break; }
        var type = path.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)
            ? AudioType.MPEG : AudioType.WAV;
        using var req = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, type);
        yield return req.SendWebRequest();
        if (req.result == UnityWebRequest.Result.Success)
            done(DownloadHandlerAudioClip.GetContent(req));
        else
        {
            Debug.LogWarning("MusicManager: не прочитался " +
                             Path.GetFileName(path) + ": " + req.error);
            done(null);
        }
    }

    // Целевые громкости: активна сторона текущего времени суток;
    // если её трека нет — продолжает играть другая сторона.
    void ApplyTargets(bool instant)
    {
        AudioSource want = isNight
            ? (nightSource.clip != null ? nightSource : daySource)
            : (daySource.clip != null ? daySource : nightSource);
        dayTarget = want == daySource && daySource.clip != null ? musicVolume : 0f;
        nightTarget = want == nightSource && nightSource.clip != null ? musicVolume : 0f;
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
