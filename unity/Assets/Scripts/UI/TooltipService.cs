using UnityEngine;
using UnityEngine.UI;

// Тултип предмета над слотом (наведение мышью).
// R6: многострочный (имя + эффекты с цифрами), высота панели
// пересчитывается под число строк при каждом Show.
// Статический сервис: единственная вьюшка на HudCanvas, создаётся
// Hud'ом через Init; триггеры слотов дёргают Show/Hide.
public static class TooltipService
{
    const float PanelWidth = 240f;
    const float LineHeight = 16f; // fontSize 13 + межстрочный запас
    const float PaddingY = 10f;   // суммарные поля сверху+снизу

    static RectTransform panelRt;
    static Text label;

    public static void Init(Transform canvasRoot)
    {
        var back = UiWidgets.Panel(canvasRoot,
            "Tooltip", new Color(0.04f, 0.045f, 0.05f, 0.92f));
        panelRt = (RectTransform)back.transform;
        panelRt.pivot = new Vector2(0.5f, 0f);
        panelRt.anchorMin = panelRt.anchorMax = new Vector2(0f, 0f);
        // высота стартовая (одна строка); реальная — в Show под текст
        panelRt.sizeDelta = new Vector2(PanelWidth, LineHeight + PaddingY);
        back.raycastTarget = false;

        label = UiWidgets.Text(panelRt, "Text", 13);
        UiWidgets.Stretch(label.rectTransform, 8f, 0f);

        panelRt.gameObject.SetActive(false);
    }

    public static void Show(string text, RectTransform anchor)
    {
        if (panelRt == null || anchor == null) return;
        if (string.IsNullOrEmpty(text)) return;
        label.text = text;

        // высота под содержимое: считаем строки по '\n'
        // (переносов по ширине нет — у label horizontalOverflow = Overflow)
        int lines = 1;
        for (int i = 0; i < text.Length; i++)
            if (text[i] == '\n') lines++;
        panelRt.sizeDelta = new Vector2(PanelWidth, lines * LineHeight + PaddingY);

        panelRt.gameObject.SetActive(true);
        // верхняя грань слота в экранных пикселях (overlay: world == screen)
        var corners = new Vector3[4];
        anchor.GetWorldCorners(corners);
        var topMid = (corners[1] + corners[2]) * 0.5f;
        var pos = topMid + Vector3.up * 6f;
        // кламп в границы экрана: pivot тултипа (0.5, 0) — опорная точка
        // это середина нижней грани; мировые размеры — rect × масштаб канваса
        float sx = panelRt.lossyScale.x;
        float sy = panelRt.lossyScale.y;
        float halfW = panelRt.rect.width * 0.5f * sx;
        float h = panelRt.rect.height * sy;
        pos.x = Mathf.Clamp(pos.x, halfW, Mathf.Max(halfW, Screen.width - halfW));
        pos.y = Mathf.Clamp(pos.y, 0f, Mathf.Max(0f, Screen.height - h));
        panelRt.position = pos;
        panelRt.SetAsLastSibling();
    }

    public static void Hide()
    {
        if (panelRt != null) panelRt.gameObject.SetActive(false);
    }
}
