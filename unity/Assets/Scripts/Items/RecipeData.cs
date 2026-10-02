using UnityEngine;

// Данные рецепта крафта (ScriptableObject, аналог item_data).
// Ассеты пекутся кодом: меню Survival → Bake Recipes, лежат в
// Assets/Resources/Recipes/<id>.asset. UI грузит их через
// Resources.LoadAll<RecipeData>("Recipes"), код при этом не правится.
[CreateAssetMenu(fileName = "recipe", menuName = "Survival/Recipe")]
public class RecipeData : ScriptableObject
{
    [System.Serializable]
    public class Ingredient
    {
        public ItemData item;
        [Min(1)] public int count = 1;
    }

    public ItemData result;
    [Min(1)] public int resultCount = 1;
    public Ingredient[] inputs;

    // Хватает ли инвентарю материалов.
    public bool CanCraft(Inventory inv)
    {
        if (inv == null || result == null || inputs == null) return false;
        foreach (var ing in inputs)
        {
            if (ing.item == null) return false;
            if (inv.CountOf(ing.item) < ing.count) return false;
        }
        return true;
    }

    // Крафт: проверка → снять входы → добавить результат.
    // Логика слотов и события — внутри Inventory, тосты — через Notify.
    public bool TryCraft(Inventory inv)
    {
        if (!CanCraft(inv))
        {
            GameEvents.RaiseNotify("Не хватает материалов");
            return false;
        }
        foreach (var ing in inputs)
            inv.RemoveItem(ing.item, ing.count);

        int leftover = inv.Add(result, resultCount);
        if (leftover > 0)
            GameEvents.RaiseNotify("Инвентарь полон — часть не влезла");
        GameEvents.RaiseNotify($"Создано: {result.displayName}");
        return true;
    }
}
