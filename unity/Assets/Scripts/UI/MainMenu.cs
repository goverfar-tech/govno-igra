using UnityEngine;

// Стартовое главное меню-оверлей (Поток B). Без отдельной сцены:
// строится в том же HudCanvas поверх мира, при запуске игра стоит
// (timeScale = 0, курсор свободен — это ставит Hud).
// Кнопки: Продолжить (SaveSystem.LoadGame), Новая игра, Настройки, Выйти.
// Логику кнопок выполняет Hud через колбэки.
public class MainMenu : MonoBehaviour
{
    public System.Action ContinueRequested;
    public System.Action NewGameRequested;
    public System.Action SettingsRequested;

    public static MainMenu Create(Transform parent)
    {
        var veil = UiWidgets.Panel(parent, "MainMenu", new Color(0.02f, 0.02f, 0.025f, 0.88f));
        var view = veil.gameObject.AddComponent<MainMenu>();
        UiWidgets.Stretch(veil.rectTransform, 0f);

        var title = UiWidgets.Text(veil.transform, "Title", 52);
        var tr = (RectTransform)title.transform;
        tr.anchorMin = tr.anchorMax = new Vector2(0.5f, 0.5f);
        tr.pivot = new Vector2(0.5f, 0.5f);
        tr.anchoredPosition = new Vector2(0f, 150f);
        tr.sizeDelta = new Vector2(1100f, 70f);
        title.text = "МАЙОНЕЗНОЕ ВЕДРО";
        title.fontStyle = FontStyle.Bold;
        title.color = new Color(0.93f, 0.88f, 0.68f); // приглушённый майонезный

        var sub = UiWidgets.Text(veil.transform, "Sub", 16);
        var sr = (RectTransform)sub.transform;
        sr.anchorMin = sr.anchorMax = new Vector2(0.5f, 0.5f);
        sr.anchoredPosition = new Vector2(0f, 105f);
        sr.sizeDelta = new Vector2(900f, 26f);
        sub.text = "ведро с пробитым дном";
        sub.color = new Color(1f, 1f, 1f, 0.55f);

        view.MakeButton("Продолжить", 24f, () => view.ContinueRequested?.Invoke());
        view.MakeButton("Новая игра", -32f, () => view.NewGameRequested?.Invoke());
        view.MakeButton("Настройки", -88f, () => view.SettingsRequested?.Invoke());
        view.MakeButton("Выйти", -144f, Quit);

        veil.gameObject.SetActive(false);
        return view;
    }

    void MakeButton(string label, float y, System.Action onClick)
    {
        var btn = UiWidgets.Button(transform, label.Replace(" ", "") + "Button", label, 20);
        var rt = (RectTransform)btn.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(0f, y);
        rt.sizeDelta = new Vector2(300f, 46f);
        btn.onClick.AddListener(() => onClick());
    }

    public void Open() => gameObject.SetActive(true);
    public void Close() => gameObject.SetActive(false);

    static void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
