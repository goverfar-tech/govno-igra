using UnityEngine;
using UnityEngine.UI;

// Одна полоска стата (HP/еда/вода): тёмная подложка, заполнение
// справа налево не уходит, подпись «Имя 78/100» поверх полоски.
// Высота фиксированная — LayoutElement, чтобы VerticalLayoutGroup
// ставил полоски с предсказуемым размером.
public class StatBar : MonoBehaviour
{
    public const float BarHeight = 18f;

    Image fill;
    Text label;
    string statName;

    public static StatBar Create(Transform parent, string statName, Color color)
    {
        var back = UiWidgets.Panel(parent, statName + "Bar", UiWidgets.BarBackColor);
        var bar = back.gameObject.AddComponent<StatBar>();
        bar.statName = statName;

        var le = back.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = BarHeight;

        var fillImg = UiWidgets.Panel(back.transform, "Fill", color);
        UiWidgets.Stretch(fillImg.rectTransform, 1f);
        bar.fill = fillImg;
        bar.fill.type = Image.Type.Filled;
        bar.fill.fillMethod = Image.FillMethod.Horizontal;

        bar.label = UiWidgets.Text(fillImg.transform, "Label", 13);
        UiWidgets.Stretch(bar.label.rectTransform, 6f, 0f);
        bar.label.alignment = TextAnchor.MiddleLeft;

        return bar;
    }

    public void Set(float value, float max)
    {
        if (fill == null) return;
        fill.fillAmount = max > 0f ? Mathf.Clamp01(value / max) : 0f;
        label.text = $"{statName} {value:F0}/{max:F0}";
    }
}
