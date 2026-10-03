using System;
using UnityEngine;
using UnityEngine.UI;

// Панель настроек (Поток B): FOV камеры, чувствительность мыши,
// громкость. Модальная: открывается поверх главного меню или паузы,
// куда открыли — туда и возвращаемся по «Назад»/Esc (логика в Hud).
// Значения — GameSettings: применяются живьём и сразу пишутся на диск
// (PlayerPrefs.Save в сеттерах GameSettings).
public class SettingsPanel : MonoBehaviour
{
    public Action BackRequested;

    Text fovValue, sensValue, volumeValue;
    // слайдеры держим ссылками: при открытии подтягиваем бегунки
    // к текущим GameSettings (SetValueWithoutNotify — без побочных Set*)
    Slider fovSlider, sensSlider, volumeSlider;

    public static SettingsPanel Create(Transform parent)
    {
        var back = UiWidgets.Panel(parent, "SettingsPanel", new Color(0.04f, 0.05f, 0.06f, 0.96f));
        var view = back.gameObject.AddComponent<SettingsPanel>();
        var rt = (RectTransform)back.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(480f, 330f);

        var title = UiWidgets.Text(back.transform, "Title", 26);
        var tt = (RectTransform)title.transform;
        tt.anchorMin = tt.anchorMax = new Vector2(0.5f, 1f);
        tt.pivot = new Vector2(0.5f, 1f);
        tt.anchoredPosition = new Vector2(0f, -14f);
        tt.sizeDelta = new Vector2(400f, 34f);
        title.text = "НАСТРОЙКИ";

        BuildRow(back.transform, 70f, "Поле зрения",
            60f, 100f, GameSettings.Fov, v => $"{v:F0}", GameSettings.SetFov,
            out view.fovValue, out view.fovSlider);
        BuildRow(back.transform, 120f, "Чувствительность",
            0.5f, 6f, GameSettings.Sensitivity, v => $"{v:0.0}", GameSettings.SetSensitivity,
            out view.sensValue, out view.sensSlider);
        BuildRow(back.transform, 170f, "Громкость",
            0f, 1f, GameSettings.Volume, v => $"{v * 100f:F0}%", GameSettings.SetVolume,
            out view.volumeValue, out view.volumeSlider);

        var back_ = UiWidgets.Button(back.transform, "BackButton", "Назад", 18);
        var b = (RectTransform)back_.transform;
        b.anchorMin = b.anchorMax = new Vector2(0.5f, 0f);
        b.pivot = new Vector2(0.5f, 0f);
        b.anchoredPosition = new Vector2(0f, 18f);
        b.sizeDelta = new Vector2(160f, 44f);
        back_.onClick.AddListener(() => view.BackRequested?.Invoke());

        back.gameObject.SetActive(false);
        return view;
    }

    // Строка настройки: подпись слева, слайдер, текущее значение справа.
    static void BuildRow(Transform parent, float topOffset, string label,
        float min, float max, float initial, Func<float, string> format, Action<float> setter,
        out Text valueText, out Slider outSlider)
    {
        var row = new GameObject(label, typeof(RectTransform));
        var rr = (RectTransform)row.transform;
        rr.SetParent(parent, false);
        rr.anchorMin = rr.anchorMax = new Vector2(0.5f, 1f);
        rr.pivot = new Vector2(0.5f, 1f);
        rr.anchoredPosition = new Vector2(0f, -topOffset);
        rr.sizeDelta = new Vector2(440f, 34f);

        var nameText = UiWidgets.Text(rr, "Name", 15, TextAnchor.MiddleLeft);
        var nt = nameText.rectTransform;
        nt.anchorMin = nt.anchorMax = new Vector2(0f, 0.5f);
        nt.pivot = new Vector2(0f, 0.5f);
        nt.anchoredPosition = Vector2.zero;
        nt.sizeDelta = new Vector2(150f, 34f);
        nameText.text = label;

        // локальная переменная: out-параметр нельзя захватывать в лямбду
        var valText = UiWidgets.Text(rr, "Value", 15, TextAnchor.MiddleRight);
        var vt = valText.rectTransform;
        vt.anchorMin = vt.anchorMax = new Vector2(1f, 0.5f);
        vt.pivot = new Vector2(1f, 0.5f);
        vt.anchoredPosition = Vector2.zero;
        vt.sizeDelta = new Vector2(56f, 34f);
        valText.text = format(initial);

        var slider = UiWidgets.MakeSlider(rr, "Slider", min, max, initial);
        var st = (RectTransform)slider.transform;
        st.anchorMin = st.anchorMax = new Vector2(0f, 0.5f);
        st.pivot = new Vector2(0f, 0.5f);
        st.anchoredPosition = new Vector2(160f, 0f);
        st.sizeDelta = new Vector2(210f, 22f);

        slider.onValueChanged.AddListener(v =>
        {
            setter(v);
            valText.text = format(v);
        });
        valueText = valText;
        outSlider = slider;
    }

    // Подтянуть подписи и ПОЗИЦИИ бегунков к текущим GameSettings:
    // значения могли смениться с прошлого открытия (или сетапом сцены).
    public void SyncLabels()
    {
        if (fovSlider != null) fovSlider.SetValueWithoutNotify(GameSettings.Fov);
        if (sensSlider != null) sensSlider.SetValueWithoutNotify(GameSettings.Sensitivity);
        if (volumeSlider != null) volumeSlider.SetValueWithoutNotify(GameSettings.Volume);
        if (fovValue != null) fovValue.text = $"{GameSettings.Fov:F0}";
        if (sensValue != null) sensValue.text = $"{GameSettings.Sensitivity:0.0}";
        if (volumeValue != null) volumeValue.text = $"{GameSettings.Volume * 100f:F0}%";
    }

    public void Show() { SyncLabels(); gameObject.SetActive(true); }
    public void Hide() => gameObject.SetActive(false);
}
