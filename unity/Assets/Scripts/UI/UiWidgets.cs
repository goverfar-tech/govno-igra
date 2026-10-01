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

    // Слайдер настроек (фон/заливка/ручка). Fill и handle — как в
    // стандартном шаблоне uGUI, чтобы Slider мог двигать их якоря.
    public static Slider MakeSlider(Transform parent, string name, float min, float max, float value)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Slider));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.sizeDelta = new Vector2(200f, 20f);

        var bg = Panel(rt, "Background", BarBackColor);
        Stretch(bg.rectTransform, 0f, 8f);
        bg.raycastTarget = false;

        var fillArea = new GameObject("Fill Area", typeof(RectTransform));
        var faRt = (RectTransform)fillArea.transform;
        faRt.SetParent(rt, false);
        Stretch(faRt, 4f, 9f);

        var fill = Panel(faRt, "Fill", new Color(0.52f, 0.52f, 0.46f, 0.9f));
        fill.raycastTarget = false;
        var fillRt = fill.rectTransform;
        fillRt.anchorMin = Vector2.zero;
        fillRt.anchorMax = new Vector2(0f, 1f);
        fillRt.offsetMin = Vector2.zero;
        fillRt.offsetMax = Vector2.zero;

        var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
        var haRt = (RectTransform)handleArea.transform;
        haRt.SetParent(rt, false);
        Stretch(haRt, 8f, 0f);

        var handle = Panel(haRt, "Handle", new Color(0.85f, 0.85f, 0.8f, 1f));
        var hRt = handle.rectTransform;
        hRt.anchorMin = Vector2.zero;
        hRt.anchorMax = new Vector2(0f, 1f);
        hRt.sizeDelta = new Vector2(14f, 0f);

        var slider = go.GetComponent<Slider>();
        slider.fillRect = fillRt;
        slider.handleRect = hRt;
        slider.targetGraphic = handle;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = min;
        slider.maxValue = max;
        slider.SetValueWithoutNotify(value); // стартовое значение — без side-эффектов
        return slider;
    }
}
