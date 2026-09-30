using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Экран паузы (Esc): полупрозрачная вуаль, кнопки «Продолжить»
// и «Начать заново». timeScale и событие PauseChanged ставит Hud —
// это чистая вьюшка, сюда проброшены колбэки.
public class PauseMenu : MonoBehaviour
{
    public System.Action ResumeRequested;
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
        tr.anchoredPosition = new Vector2(0f, 80f);
        tr.sizeDelta = new Vector2(600f, 50f);
        title.text = "ПАУЗА";

        var resume = UiWidgets.Button(veil.transform, "ResumeButton", "Продолжить", 20);
        var rt = (RectTransform)resume.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(0f, 4f);
        rt.sizeDelta = new Vector2(240f, 46f);
        resume.onClick.AddListener(() => view.ResumeRequested?.Invoke());

        var restart = UiWidgets.Button(veil.transform, "RestartButton", "Начать заново", 20);
        var rt2 = (RectTransform)restart.transform;
        rt2.anchorMin = rt2.anchorMax = new Vector2(0.5f, 0.5f);
        rt2.anchoredPosition = new Vector2(0f, -56f);
        rt2.sizeDelta = new Vector2(240f, 46f);
        restart.onClick.AddListener(() => view.RestartRequested?.Invoke());

        veil.gameObject.SetActive(false);
        return view;
    }

    public void Show() => gameObject.SetActive(true);
    public void Hide() => gameObject.SetActive(false);
}
