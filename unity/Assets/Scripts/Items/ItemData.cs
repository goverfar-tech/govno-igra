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
    // Что возвращается после использования (фляга: полная → пустая)
    public ItemData consumeReturns;

    [Header("Инструмент (для системы добычи/боя, M5)")]
    public bool isTool;
    public float toolDamage;

    [Header("Строительство (для M5)")]
    public bool isPlaceable;

    public bool IsConsumable => foodRestore > 0f || waterRestore > 0f || healAmount > 0f;
}
