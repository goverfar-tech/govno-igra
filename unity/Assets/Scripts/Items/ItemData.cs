using UnityEngine;

// Данные предмета (аналог ItemData .tres из Godot-версии).
// Чтобы добавить предмет — создаётся ассет (ПКМ → Create → Survival → Item),
// код не правится.
[CreateAssetMenu(fileName = "item", menuName = "Survival/Item")]
public class ItemData : ScriptableObject
{
    public string id;               // "wood", "stone", …
    public string displayName;      // "Дерево"
    [Min(1)] public int maxStack = 20;
    public Sprite icon;
    public GameObject worldModel;   // префаб/модель для пикапа и руки

    [Header("Расходник (ЛКМ = использовать)")]
    public float foodRestore;       // восстановление сытости
    public float waterRestore;      // восстановление жажды
    public float healAmount;        // восстановление HP
    // Яд §9.3: сырая плоть/мутный майонез — еда + poison; яд жрёт HP,
    // пока не выветрится. 0 = чистая еда.
    public float poisonAmount;
    // Что возвращается после использования (фляга: полная → пустая)
    public ItemData consumeReturns;

    [Header("Герметик (X2, §9.4): замазка дыры в дне ведра")]
    // Флаг отличает герметик от еды в Player.UseSelected: ЛКМ = замазать
    // дыру, а не съесть. sealantTier — какой это слой замазки:
    // 1=смола, 2=воск, 3=битум; чем выше тир, тем сильнее падает утечка
    // (таблица множителей — в Stats). Постоянно до конца сейва.
    public bool isSealant;
    public int sealantTier;

    [Header("Инструмент (для системы добычи/боя, M5)")]
    public bool isTool;
    public float toolDamage;

    [Header("Строительство")]
    public bool isPlaceable;
    public GameObject placeablePrefab;   // что ставится в мир (коллайдеры внутри)

    public bool IsConsumable => foodRestore > 0f || waterRestore > 0f || healAmount > 0f;
}
