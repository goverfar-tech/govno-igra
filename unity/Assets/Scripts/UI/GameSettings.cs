using UnityEngine;

// Настройки игры (Поток B): FOV, чувствительность мыши, громкость.
// Хранение — PlayerPrefs, применяются при старте сцены (Hud.Awake)
// и живьём при движении слайдеров. Ничего не знает про UI-вёрстку —
// этим занимается SettingsPanel.
public static class GameSettings
{
    const string KeyFov = "set_fov";
    const string KeySens = "set_sens";
    const string KeyVolume = "set_vol";

    public static float Fov { get; private set; }
    public static float Sensitivity { get; private set; }
    public static float Volume { get; private set; }

    static bool loaded;
    static Player player; // scene-объект; после LoadScene протухает — перенаходим

    // Вызывается при старте сцены. Дефолты без сохранённых значений —
    // текущие из сцены (FOV камеры, mouseSensitivity игрока, громкость 1).
    public static void ApplyAll()
    {
        player = null;
        LoadOnce();
        ApplyFov(Fov);
        ApplySens(Sensitivity);
        ApplyVolume(Volume);
    }

    static void LoadOnce()
    {
        if (loaded) return;
        loaded = true;
        var cam = Camera.main;
        Fov = PlayerPrefs.GetFloat(KeyFov, cam != null ? cam.fieldOfView : 75f);
        var p = FindPlayer();
        Sensitivity = PlayerPrefs.GetFloat(KeySens, p != null ? p.mouseSensitivity : 2.2f);
        Volume = PlayerPrefs.GetFloat(KeyVolume, 1f);
    }

    public static void SetFov(float v)
    {
        Fov = Mathf.Clamp(v, 60f, 100f);
        PlayerPrefs.SetFloat(KeyFov, Fov);
        ApplyFov(Fov);
    }

    public static void SetSensitivity(float v)
    {
        Sensitivity = Mathf.Clamp(v, 0.5f, 6f);
        PlayerPrefs.SetFloat(KeySens, Sensitivity);
        ApplySens(Sensitivity);
    }

    public static void SetVolume(float v)
    {
        Volume = Mathf.Clamp01(v);
        PlayerPrefs.SetFloat(KeyVolume, Volume);
        ApplyVolume(Volume);
    }

    // Пишем на диск при закрытии панели, а не на каждый тик слайдера.
    public static void Flush() => PlayerPrefs.Save();

    static void ApplyFov(float v)
    {
        var cam = Camera.main;
        if (cam != null && Mathf.Abs(cam.fieldOfView - v) > 0.01f)
            cam.fieldOfView = v;
    }

    static void ApplySens(float v)
    {
        var p = FindPlayer();
        if (p != null) p.mouseSensitivity = v;
    }

    static void ApplyVolume(float v) => AudioListener.volume = v;

    static Player FindPlayer()
    {
        if (player == null) player = Object.FindAnyObjectByType<Player>();
        return player;
    }
}
