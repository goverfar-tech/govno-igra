using UnityEngine;
using UnityEngine.SceneManagement;

// Временный HUD на IMGUI (OnGUI) — не требует настройки Canvas.
// Служит до появления полноценного uGUI-HUD (этап M3): показывает статы,
// подсказку взаимодействия, тосты и экран смерти с рестартом по R.
public class DebugHud : MonoBehaviour
{
    public Stats stats;
    public Inventory inventory;

    string prompt;
    string toast;
    float toastUntil;
    bool dead;

    void OnEnable()
    {
        GameEvents.PromptChanged += OnPrompt;
        GameEvents.Notify += OnNotify;
        GameEvents.PlayerDied += OnDeath;
    }

    void OnDisable()
    {
        GameEvents.PromptChanged -= OnPrompt;
        GameEvents.Notify -= OnNotify;
        GameEvents.PlayerDied -= OnDeath;
    }

    void OnPrompt(string p) => prompt = p;
    void OnNotify(string text) { toast = text; toastUntil = Time.time + 3f; }
    void OnDeath()
    {
        dead = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void Update()
    {
        if (dead && Input.GetKeyDown(KeyCode.R))
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    void OnGUI()
    {
        var style = new GUIStyle(GUI.skin.label) { fontSize = 18 };
        float y = 10f;

        if (stats != null)
        {
            GUI.Label(new Rect(10, y, 400, 24), $"HP {stats.Hp:F0}/{stats.maxHp:F0}", style); y += 24;
            GUI.Label(new Rect(10, y, 400, 24), $"Еда {stats.Food:F0}/{stats.maxFood:F0}", style); y += 24;
            GUI.Label(new Rect(10, y, 400, 24), $"Вода {stats.Water:F0}/{stats.maxWater:F0}", style); y += 24;
        }

        // прицел
        GUI.Label(new Rect(Screen.width / 2f - 6, Screen.height / 2f - 12, 24, 24), "+", style);

        // хотбар: [содержимое слотов], активный выделен
        if (inventory != null)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < Inventory.HotbarSize; i++)
            {
                var s = inventory.slots[i];
                string cell = s.IsEmpty ? "--" : $"{s.item.displayName}×{s.count}";
                sb.Append(i == inventory.selected ? $"[{cell}] " : $" {cell}  ");
            }
            GUI.Label(new Rect(10, Screen.height - 34, 900, 26), sb.ToString(), style);
        }

        if (!string.IsNullOrEmpty(prompt))
            GUI.Label(new Rect(Screen.width / 2f - 200, Screen.height / 2f + 30, 400, 24), prompt, style);

        if (Time.time < toastUntil)
            GUI.Label(new Rect(Screen.width / 2f - 200, 60, 400, 24), toast, style);

        if (dead)
        {
            var big = new GUIStyle(style) { fontSize = 42, alignment = TextAnchor.MiddleCenter };
            GUI.Label(new Rect(0, Screen.height / 2f - 60, Screen.width, 60), "ВЫ ПОГИБЛИ", big);
            GUI.Label(new Rect(0, Screen.height / 2f + 10, Screen.width, 30), "R — начать заново", big);
        }
    }
}
