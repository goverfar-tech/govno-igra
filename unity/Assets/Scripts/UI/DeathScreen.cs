using UnityEngine;
using UnityEngine.UI;

// Экран смерти (событие PlayerDied): тёмная вуаль, заголовок
// и кнопка рестарта. Сам сцену не грузит — рестарт делает Hud
// (RestartRequested), чтобы выставить флаг «без стартового меню»
// и сбросить Time.timeScale перед LoadScene.
public class DeathScreen : MonoBehaviour
{
    public System.Action RestartRequested;

    public static DeathScreen Create(Transform parent)
    {
        var veil = UiWidgets.Panel(parent, "DeathScreen", new Color(0.02f, 0.02f, 0.025f, 0.78f));
        var view = veil.gameObject.AddComponent<DeathScreen>();
        UiWidgets.Stretch(veil.rectTransform, 0f);

        var title = UiWidgets.Text(veil.transform, "Title", 46);
        var tr = (RectTransform)title.transform;
        tr.anchorMin = tr.anchorMax = new Vector2(0.5f, 0.5f);
        tr.pivot = new Vector2(0.5f, 0.5f);
        tr.anchoredPosition = new Vector2(0f, 70f);
        tr.sizeDelta = new Vector2(800f, 60f);
        title.text = "ВЫ ПОГИБЛИ";
        title.color = UiWidgets.WarningColor;
        title.fontStyle = FontStyle.Bold;

        var sub = UiWidgets.Text(veil.transform, "Sub", 16);
        var sr = (RectTransform)sub.transform;
        sr.anchorMin = sr.anchorMax = new Vector2(0.5f, 0.5f);
        sr.anchoredPosition = new Vector2(0f, 20f);
        sr.sizeDelta = new Vector2(800f, 26f);
        sub.text = "Майонез вытек до конца.";
        sub.color = new Color(1f, 1f, 1f, 0.6f);

        var btn = UiWidgets.Button(veil.transform, "RestartButton", "Начать заново", 20);
        var brt = (RectTransform)btn.transform;
        brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.5f);
        brt.anchoredPosition = new Vector2(0f, -52f);
        brt.sizeDelta = new Vector2(240f, 48f);
        btn.onClick.AddListener(view.OnRestartClicked);

        var keyHint = UiWidgets.Text(veil.transform, "KeyHint", 13);
        var kr = (RectTransform)keyHint.transform;
        kr.anchorMin = kr.anchorMax = new Vector2(0.5f, 0.5f);
        kr.anchoredPosition = new Vector2(0f, -92f);
        kr.sizeDelta = new Vector2(800f, 20f);
        keyHint.text = "R — начать заново";
        keyHint.color = new Color(1f, 1f, 1f, 0.45f);

        veil.gameObject.SetActive(false);
        return view;
    }

    public void Show() => gameObject.SetActive(true);

    void OnRestartClicked() => RestartRequested?.Invoke();
}
