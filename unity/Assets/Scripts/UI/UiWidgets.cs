using UnityEngine;
using UnityEngine.UI;

// Фабрики базовых uGUI-элементов для HUD (M3).
// Весь интерфейс собирается кодом, без ручной сборки в редакторе.
// Стиль — мрачный минимализм §9.6: полупрозрачные тёмные панели,
// приглушённые акцентные цвета, тонкий чёрный контур текста.
public static class UiWidgets
{
    public static readonly Color TextColor = new Color(0.88f, 0.88f, 0.86f);
    public static readonly Color PanelColor = new Color(0.05f, 0.06f, 0.07f, 0.62f);
    public static readonly Color SlotColor = new Color(0.09f, 0.10f, 0.11f, 0.55f);
    public static readonly Color BarBackColor = new Color(0f, 0f, 0f, 0.5f);
    public static readonly Color HpColor = new Color(0.55f, 0.16f, 0.14f, 0.9f);
    public static readonly Color FoodColor = new Color(0.62f, 0.42f, 0.14f, 0.9f);
    public static readonly Color WaterColor = new Color(0.18f, 0.38f, 0.55f, 0.9f);
    public static readonly Color WarningColor = new Color(0.72f, 0.22f, 0.18f);

    static Font font;

    public static Font Font
    {
        get
        {
            if (font == null) font = LoadFont();
            return font;
        }
    }

    static Font LoadFont()
    {
        // LegacyRuntime.ttf — встроенный шрифт Unity 2022+ (кириллица есть);
        // Arial.ttf — запасной вариант для старых версий
        var f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (f == null) f = Resources.GetBuiltinResource<Font>("Arial.ttf");
        return f;
    }

    // Панель: GameObject + RectTransform + Image заданного цвета.
    public static Image Panel(Transform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = color;
        return img;
    }

    // Текст. Raycast выключен по умолчанию: иначе подписи перехватывают
    // драг инвентаря и клики кнопок у родителей.
    public static Text Text(Transform parent, string name, int fontSize,
        TextAnchor anchor = TextAnchor.MiddleCenter)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text), typeof(Outline));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<Text>();
        t.font = Font;
        t.fontSize = fontSize;
        t.alignment = anchor;
        t.color = TextColor;
        t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        var outline = go.GetComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
        outline.effectDistance = new Vector2(1.2f, -1.2f);
        return t;
    }

    // Кнопка: тёмная подложка + подпись. Возвращает сам Button.
    public static Button Button(Transform parent, string name, string label, int fontSize = 20)
    {
        var img = Panel(parent, name, SlotColor);
        var btn = img.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        var colors = btn.colors;
        colors.normalColor = SlotColor;
        colors.highlightedColor = new Color(0.18f, 0.19f, 0.20f, 0.9f);
        colors.pressedColor = new Color(0.28f, 0.29f, 0.30f, 0.95f);
        colors.fadeDuration = 0.06f;
        btn.colors = colors;
        var t = Text(img.transform, "Label", fontSize);
        Stretch(t.rectTransform, 0f);
        return btn;
    }

    // Растянуть RectTransform на весь родитель (с отступом margin).
    public static void Stretch(RectTransform rt, float margin)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(margin, margin);
        rt.offsetMax = new Vector2(-margin, -margin);
    }

    // Ширина/отступы с разными значениями по осям.
    public static void Stretch(RectTransform rt, float marginX, float marginY)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(marginX, marginY);
        rt.offsetMax = new Vector2(-marginX, -marginY);
    }
}
