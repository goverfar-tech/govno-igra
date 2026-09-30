using UnityEngine;

// Входная точка HUD (M3): если сцена запущена без объекта Hud
// (не прогнали Survival → Setup HUD), создаём его на лету.
// Hud.Awake сам строит весь uGUI-интерфейс и выключает DebugHud.
// Если Hud уже расставлен в сцене сетапом — ничего не делаем.
public static class HudBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void EnsureHud()
    {
        if (Object.FindAnyObjectByType<Hud>(FindObjectsInactive.Include) != null) return;
        new GameObject("Hud").AddComponent<Hud>();
    }
}
