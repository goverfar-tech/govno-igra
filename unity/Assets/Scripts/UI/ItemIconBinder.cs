using UnityEngine;

// Рантайм-привязка иконок: ItemData-ассеты в git не трогаем,
// icon ставится на загруженный SO в памяти при старте каждой сцены.
// Спрайты запекает IconBaker (Survival → Bake Item Icons) в
// Assets/Resources/Icons/<id>.png. Если иконки нет — слот показывает
// имя предмета текстом, как раньше.
public static class ItemIconBinder
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bind()
    {
        foreach (var item in Resources.LoadAll<ItemData>("Items"))
        {
            if (item.icon != null) continue; // уважаем вручную назначенное
            var sprite = Resources.Load<Sprite>("Icons/" + item.id);
            if (sprite != null) item.icon = sprite;
        }
    }
}
