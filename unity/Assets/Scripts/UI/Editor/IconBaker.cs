using System.IO;
using UnityEditor;
using UnityEngine;

// Меню Survival → Bake Item Icons (Поток B, иконки M3/M5).
// Для каждого ItemData из Assets/Resources/Items берёт worldModel
// (если нет — placeablePrefab, чтобы костёр/стена тоже получили иконку),
// рендерит её временной камерой в RenderTexture 128×128 и сохраняет
// PNG-спрайт в Assets/Resources/Icons/<id>.png с прозрачным фоном.
// Сами ItemData-ассеты не меняются: спрайт к полю icon привязывает
// в рантайме ItemIconBinder (Resources.Load).
public static class IconBaker
{
    const string ItemsFolder = "Assets/Resources/Items";
    const string IconsFolder = "Assets/Resources/Icons";
    const int Size = 128;

    [MenuItem("Survival/Bake Item Icons")]
    public static void Run()
    {
        EnsureFolder(IconsFolder);

        int baked = 0;
        var skipped = new System.Text.StringBuilder();
        foreach (var guid in AssetDatabase.FindAssets("t:ItemData", new[] { ItemsFolder }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var item = AssetDatabase.LoadAssetAtPath<ItemData>(path);
            if (item == null || string.IsNullOrEmpty(item.id)) continue;

            var model = item.worldModel != null ? item.worldModel : item.placeablePrefab;
            if (model == null)
            {
                skipped.AppendLine($"— {item.id}: нет worldModel, остаётся текст в слоте");
                continue;
            }
            if (Bake(item.id, model)) baked++;
            else skipped.AppendLine($"— {item.id}: в модели нет рендереров");
        }

        AssetDatabase.Refresh();
        Debug.Log($"[Survival] Иконки запечены: {baked}. {(skipped.Length > 0 ? "Пропущено:\n" + skipped : "")}");
        EditorUtility.DisplayDialog("Survival",
            $"Запечено иконок: {baked}" +
            (skipped.Length > 0 ? $"\nПропущено (нет модели):\n{skipped}" : ""), "Ок");
    }

    static bool Bake(string id, GameObject model)
    {
        var rt = new RenderTexture(Size, Size, 24) { antiAliasing = 4 };
        var root = new GameObject("__IconBake");
        try
        {
            // сцену не трогаем: съёмочная площадка далеко за пределами мира
            root.transform.position = new Vector3(10000f, 10000f, 10000f);

            // InstantiatePrefab возвращает null, если модель не prefab-корень
            // (например, вложенный GameObject из GLB) — тогда обычная копия
            var inst = PrefabUtility.InstantiatePrefab(model) as GameObject;
            if (inst == null)
            {
                Debug.LogWarning($"[Survival] «{model.name}» не prefab-корень — копирую как обычный объект.");
                inst = Object.Instantiate(model);
            }
            if (inst == null) { Debug.LogWarning($"[Survival] Не удалось создать экземпляр «{model.name}»."); return false; }
            inst.transform.SetParent(root.transform, false);

            var renderers = inst.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return false;

            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            float extent = Mathf.Max(bounds.extents.magnitude, 0.01f);

            var camGo = new GameObject("Cam");
            camGo.transform.SetParent(root.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 30f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f); // прозрачный фон
            cam.targetTexture = rt;
            cam.enabled = false; // рендерим вручную

            float dist = extent / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.2f;
            // взгляд в три четверти сверху — так читается и форма, и объём
            var viewDir = new Vector3(1f, 0.85f, 1f).normalized;
            camGo.transform.position = bounds.center + viewDir * dist;
            camGo.transform.LookAt(bounds.center);
            cam.nearClipPlane = Mathf.Max(0.01f, dist - extent * 1.6f);
            cam.farClipPlane = dist + extent * 1.6f;

            // свой свет, чтобы не зависеть от сцены: ключевой + заполняющий
            MakeLight(camGo.transform, new Vector3(50f, -30f, 10f), 1.15f);
            MakeLight(camGo.transform, new Vector3(15f, 150f, 0f), 0.45f);

            cam.Render();

            RenderTexture.active = rt;
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false, false);
            tex.ReadPixels(new Rect(0f, 0f, Size, Size), 0, 0);
            tex.Apply();
            RenderTexture.active = null;

            // File.WriteAllBytes чувствителен к cwd процесса — собираем
            // абсолютный путь от Application.dataPath (…/Assets)
            string absPng = Application.dataPath + $"/Resources/Icons/{id}.png";
            File.WriteAllBytes(absPng, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
        finally
        {
            Object.DestroyImmediate(root);
            rt.Release();
        }

        string spritePath = $"{IconsFolder}/{id}.png";
        AssetDatabase.ImportAsset(spritePath, ImportAssetOptions.ForceSynchronousImport);
        var ti = AssetImporter.GetAtPath(spritePath) as TextureImporter;
        if (ti == null)
        {
            // сразу после записи файла импортёр может ещё не отдать ассет —
            // принудительный Refresh и вторая попытка
            AssetDatabase.Refresh();
            ti = AssetImporter.GetAtPath(spritePath) as TextureImporter;
        }
        if (ti == null)
        {
            Debug.LogWarning($"[Survival] Не получен TextureImporter для {spritePath} — PNG записан, спрайт настрой вручную.");
            return true; // картинка запечена, падаем только по настройке спрайта
        }
        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Single;
        ti.alphaIsTransparency = true;
        ti.mipmapEnabled = false;
        ti.maxTextureSize = Size;
        ti.SaveAndReimport();
        return true;
    }

    static void MakeLight(Transform parent, Vector3 euler, float intensity)
    {
        var go = new GameObject("Key");
        go.transform.SetParent(parent, false);
        go.transform.eulerAngles = euler;
        var l = go.AddComponent<Light>();
        l.type = LightType.Directional;
        l.intensity = intensity;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        string leaf = Path.GetFileName(path);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }
}
