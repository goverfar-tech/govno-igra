using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Экран паузы (Esc): полупрозрачная вуаль, кнопки «Продолжить»,
// «Настройки» и «Начать заново». timeScale и событие PauseChanged
// ставит Hud — это чистая вьюшка, сюда проброшены колбэки.
public class PauseMenu : MonoBehaviour
{
    public System.Action ResumeRequested;
    public System.Action SettingsRequested;
    public System.Action RestartRequested;

    public static PauseMenu Create(Transform parent)
    {
        var veil = UiWidgets.Panel(parent, "PauseMenu", new Color(0.02f, 0.02f, 0.025f, 0.66f));
        var view = veil.gameObject.AddComponent<PauseMenu>();
        UiWidgets.Stretch(veil.rectTransform, 0f);

        var title = UiWidgets.Text(veil.transform, "Title", 38);
        var tr = (RectTransform)title.transform;
        tr.anchorMin = tr.anchorMax = new Vector2(0.5f, 0.5f);
        tr.pivot = new Vector2(0.5f, 0.5f);
        tr.anchoredPosition = new Vector2(0f, 110f);
        tr.sizeDelta = new Vector2(600f, 50f);
        title.text = "ПАУЗА";

        var resume = UiWidgets.Button(veil.transform, "ResumeButton", "Продолжить", 20);
        var rt = (RectTransform)resume.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(0f, 50f);
        rt.sizeDelta = new Vector2(240f, 44f);
        resume.onClick.AddListener(() => view.ResumeRequested?.Invoke());

        var settings = UiWidgets.Button(veil.transform, "SettingsButton", "Настройки", 20);
        var st = (RectTransform)settings.transform;
        st.anchorMin = st.anchorMax = new Vector2(0.5f, 0.5f);
        st.anchoredPosition = new Vector2(0f, -4f);
        st.sizeDelta = new Vector2(240f, 44f);
        settings.onClick.AddListener(() => view.SettingsRequested?.Invoke());

        var restart = UiWidgets.Button(veil.transform, "RestartButton", "Начать заново", 20);
        var rt2 = (RectTransform)restart.transform;
        rt2.anchorMin = rt2.anchorMax = new Vector2(0.5f, 0.5f);
        rt2.anchoredPosition = new Vector2(0f, -58f);
        rt2.sizeDelta = new Vector2(240f, 44f);
        restart.onClick.AddListener(() => view.RestartRequested?.Invoke());

        veil.gameObject.SetActive(false);
        return view;
    }

    public void Show() => gameObject.SetActive(true);
    public void Hide() => gameObject.SetActive(false);
}
