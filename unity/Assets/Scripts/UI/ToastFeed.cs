using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Стек всплывающих тостов (событие Notify): вверху по центру,
// до maxVisible штук, жизни lifeTime секунд, последний fadeSec
// плавно гаснет. Старое уходит вниз, новое появляется сверху.
public class ToastFeed : MonoBehaviour
{
    const int maxVisible = 5;
    const float lifeTime = 3.5f;
    const float fadeSec = 1f;
    const float rowHeight = 34f;

    class Entry
    {
        public RectTransform rt;
        public CanvasGroup group;
        public float bornAt;
    }

    readonly List<Entry> entries = new List<Entry>();
    Canvas canvas;

    public static ToastFeed Create(Transform canvasRt)
    {
        var go = new GameObject("Toasts", typeof(RectTransform));
        go.transform.SetParent(canvasRt, false);
        var view = go.AddComponent<ToastFeed>();
        view.canvas = canvasRt.GetComponent<Canvas>();
        return view;
    }

    public void Push(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        if (entries.Count >= maxVisible) RemoveOldest();

        var back = UiWidgets.Panel(transform, "Toast", UiWidgets.PanelColor);
        var rt = (RectTransform)back.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(520f, rowHeight - 6f);

        var t = UiWidgets.Text(back.transform, "Text", 15);
        UiWidgets.Stretch(t.rectTransform, 10f, 0f);
        t.text = text;

        var entry = new Entry { rt = rt, bornAt = Time.unscaledTime };
        entry.group = back.gameObject.AddComponent<CanvasGroup>();
        entries.Insert(0, entry);
        RefreshLayout();
    }

    void Update()
    {
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            float age = Time.unscaledTime - entries[i].bornAt;
            if (age >= lifeTime) { RemoveOldest(); continue; }
            entries[i].group.alpha = age > lifeTime - fadeSec
                ? 1f - (age - (lifeTime - fadeSec)) / fadeSec
                : 1f;
        }
    }

    void RefreshLayout()
    {
        for (int i = 0; i < entries.Count; i++)
            entries[i].rt.anchoredPosition = new Vector2(0f, -60f - i * rowHeight);
    }

    void RemoveOldest()
    {
        if (entries.Count == 0) return;
        var last = entries.Count - 1;
        if (entries[last].rt != null) Destroy(entries[last].rt.gameObject);
        entries.RemoveAt(last);
        RefreshLayout();
    }
}
