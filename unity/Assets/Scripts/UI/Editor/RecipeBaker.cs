using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Меню Survival → Bake Recipes (Поток В): печёт RecipeData-ассеты
// ремесленных рецептов в Assets/Resources/Recipes/<id>.asset.
// ItemData ищутся по id в Assets/Resources/Items (туда переехали при
// миграции сейвов). Повторный запуск перезаписывает поля — правки
// состава рецептов делаются здесь, как в SetupMainScene.SyncItem.
public static class RecipeBaker
{
    const string ItemsFolder = "Assets/Resources/Items";
    const string RecipesFolder = "Assets/Resources/Recipes";

    [MenuItem("Survival/Bake Recipes")]
    public static void Run()
    {
        // каталог предметов по id
        var items = new Dictionary<string, ItemData>();
        foreach (var guid in AssetDatabase.FindAssets("t:ItemData", new[] { ItemsFolder }))
        {
            var item = AssetDatabase.LoadAssetAtPath<ItemData>(AssetDatabase.GUIDToAssetPath(guid));
            if (item != null && !string.IsNullOrEmpty(item.id)) items[item.id] = item;
        }

        EnsureFolder(RecipesFolder);
        var missing = new System.Text.StringBuilder();
        int n = 0;
        // базовый набор M2: инструменты и постройки из дерева/камня
        n += Sync(items, missing, "axe", 1, ("wood", 2), ("stone", 1));
        n += Sync(items, missing, "pickaxe", 1, ("wood", 2), ("stone", 2));
        n += Sync(items, missing, "spear", 1, ("wood", 3));
        n += Sync(items, missing, "wall", 1, ("wood", 4));
        n += Sync(items, missing, "campfire", 1, ("wood", 5), ("stone", 3));
        AssetDatabase.SaveAssets();

        Debug.Log($"[Survival] Рецепты запечены: {n}/{5}" +
                  (missing.Length > 0 ? $"\nНе найдены предметы:\n{missing}" : ""));
        EditorUtility.DisplayDialog("Survival",
            $"Рецептов запечено: {n}." +
            (missing.Length > 0 ? $"\nНЕ найдены предметы:\n{missing}" : ""), "Ок");
    }

    // Создаёт рецепт-ассет ИЛИ перезаписывает поля существующего.
    // Возвращает 1 при успехе; отсутствующие предметы — в missing.
    static int Sync(Dictionary<string, ItemData> items, System.Text.StringBuilder missing,
        string resultId, int resultCount, params (string item, int n)[] inputs)
    {
        if (!items.TryGetValue(resultId, out var result))
        {
            missing.AppendLine($"— результат «{resultId}»");
            return 0;
        }
        var ings = new List<RecipeData.Ingredient>();
        bool ok = true;
        foreach (var (id, count) in inputs)
        {
            if (!items.TryGetValue(id, out var it))
            {
                missing.AppendLine($"— вход «{id}» для {resultId}");
                ok = false;
                continue;
            }
            ings.Add(new RecipeData.Ingredient { item = it, count = count });
        }
        if (!ok) return 0;

        string path = $"{RecipesFolder}/{resultId}.asset";
        var recipe = AssetDatabase.LoadAssetAtPath<RecipeData>(path);
        bool created = recipe == null;
        if (created) recipe = ScriptableObject.CreateInstance<RecipeData>();

        recipe.result = result;
        recipe.resultCount = resultCount;
        recipe.inputs = ings.ToArray();

        if (created) AssetDatabase.CreateAsset(recipe, path);
        else EditorUtility.SetDirty(recipe);
        return 1;
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
