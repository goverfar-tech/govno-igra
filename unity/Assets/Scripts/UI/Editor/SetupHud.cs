using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Меню Survival → Setup HUD (этап M3): добавляет объект Hud в активную
// сцену и выключает GameObject с временным DebugHud. Сами скрипты
// DebugHud.cs и SetupMainScene.cs не правим — только состояние сцены.
// Идемпотентно: повторный запуск ничего не дублирует.
public static class SetupHud
{
    [MenuItem("Survival/Setup HUD")]
    public static void Run()
    {
        if (EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("Survival",
                "Сначала выйди из Play-режима, потом запускай Setup.", "Ок");
            return;
        }

        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            EditorUtility.DisplayDialog("Survival",
                "Нет активной сцены. Открой Assets/Scenes/Main.unity и повтори.", "Ок");
            return;
        }

        // 1) выключаем временный IMGUI-HUD (объект остаётся в сцене)
        int disabled = 0;
        foreach (var root in scene.GetRootGameObjects())
            foreach (var dbg in root.GetComponentsInChildren<DebugHud>(true))
                if (dbg.gameObject.activeSelf)
                {
                    dbg.gameObject.SetActive(false);
                    disabled++;
                }

        // 2) ищем Hud (в т.ч. выключенный) или создаём
        Hud hud = FindInScene<Hud>(scene);
        if (hud == null)
        {
            var go = new GameObject("Hud");
            SceneManager.MoveGameObjectToScene(go, scene);
            hud = go.AddComponent<Hud>();
        }
        else if (!hud.gameObject.activeSelf)
        {
            hud.gameObject.SetActive(true);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();

        Debug.Log($"[Survival] HUD готов: объект «{hud.gameObject.name}» в сцене {scene.name}, " +
                  $"DebugHud выключено: {disabled}. Жми Play!");
        EditorUtility.DisplayDialog("Survival",
            "HUD добавлен в сцену, DebugHud выключен.\nЖми Play и тестируй.", "Ок");
    }

    static T FindInScene<T>(Scene scene) where T : Component
    {
        foreach (var root in scene.GetRootGameObjects())
            foreach (var c in root.GetComponentsInChildren<T>(true))
                return c;
        return null;
    }
}
