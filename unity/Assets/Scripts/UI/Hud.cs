using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Корневой uGUI-HUD (этап M3, Поток В). Заменяет временный DebugHud
// на IMGUI; сам DebugHud не правим — при старте выключаем его GameObject.
//
// Весь интерфейс строится кодом в Awake: Canvas + CanvasScaler +
// GraphicRaycaster, полоски статов, прицел, подсказка, тосты, хотбар,
// панель инвентаря, экран смерти, пауза.
//
// Источники данных: Player/Inventory/Stats находятся сами по сцене,
// обновления — по событиям GameEvents (§4 архитектуры).
//
// Состояния: игра | инвентарь (Tab) | пауза (Esc) | смерть.
// Любое открытое UI-состояние агрегируется в uiActive и вещается
// наружу как InventoryOpenChanged — Player на него уже реагирует
// (блок ввода + освобождение курсора).
public class Hud : MonoBehaviour
{
    Stats stats;
    Inventory inventory;

    Canvas canvas;
    StatBar hpBar, foodBar, waterBar;
    HotbarView hotbar;
    Text promptText;
    ToastFeed toasts;
    InventoryPanelView inventoryPanel;
    DeathScreen deathScreen;
    PauseMenu pauseMenu;

    bool inventoryOpen;
    bool paused;
    bool dead;
    bool uiActive;

    void Awake()
    {
        // временный IMGUI-HUD больше не нужен (файл не трогаем — лишь гасим объект)
        var dbg = FindAnyObjectByType<DebugHud>(FindObjectsInactive.Include);
        if (dbg != null) dbg.gameObject.SetActive(false);

        var player = FindAnyObjectByType<Player>();
        if (player != null)
        {
            stats = player.GetComponent<Stats>();
            inventory = player.GetComponent<Inventory>();
        }

        EnsureEventSystem();
        Build();
        RefreshAll();
    }

    void OnEnable()
    {
        GameEvents.StatsChanged += RefreshStats;
        GameEvents.InventoryChanged += RefreshInventory;
        GameEvents.SelectionChanged += RefreshSelection;
        GameEvents.PromptChanged += OnPrompt;
        GameEvents.Notify += OnNotify;
        GameEvents.PlayerDied += OnPlayerDied;
    }

    void OnDisable()
    {
        GameEvents.StatsChanged -= RefreshStats;
        GameEvents.InventoryChanged -= RefreshInventory;
        GameEvents.SelectionChanged -= RefreshSelection;
        GameEvents.PromptChanged -= OnPrompt;
        GameEvents.Notify -= OnNotify;
        GameEvents.PlayerDied -= OnPlayerDied;
    }

    void Update()
    {
        if (dead)
        {
            if (Input.GetKeyDown(KeyCode.R)) Restart();
            return;
        }
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (inventoryOpen) SetInventoryOpen(false);
            else SetPaused(!paused);
        }
        if (Input.GetKeyDown(KeyCode.Tab) && !paused)
            SetInventoryOpen(!inventoryOpen);
    }

    // ---------- построение ----------

    void EnsureEventSystem()
    {
        if (FindAnyObjectByType<EventSystem>() != null) return;
        new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
    }

    void Build()
    {
        var canvasGo = new GameObject("HudCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        var crt = (RectTransform)canvas.transform;

        BuildStats(crt);
        BuildCrosshair(crt);
        BuildPrompt(crt);
        toasts = ToastFeed.Create(crt);
        hotbar = HotbarView.Create(crt);
        inventoryPanel = InventoryPanelView.Create(crt, inventory);
        deathScreen = DeathScreen.Create(crt); // рестарт делает сама вьюшка
        pauseMenu = PauseMenu.Create(crt);
        pauseMenu.ResumeRequested = () => SetPaused(false);
        pauseMenu.RestartRequested = Restart;
    }

    void BuildStats(RectTransform root)
    {
        var panel = UiWidgets.Panel(root, "Stats", UiWidgets.PanelColor);
        var rt = (RectTransform)panel.transform;
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.pivot = Vector2.zero;
        rt.anchoredPosition = new Vector2(12f, 12f);
        float height = 3 * StatBar.BarHeight + 2 * 6f + 2 * 8f;
        rt.sizeDelta = new Vector2(240f, height);

        var vlg = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(8, 8, 8, 8);
        vlg.spacing = 6f;
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        hpBar = StatBar.Create(panel.transform, "HP", UiWidgets.HpColor);
        foodBar = StatBar.Create(panel.transform, "Еда", UiWidgets.FoodColor);
        waterBar = StatBar.Create(panel.transform, "Вода", UiWidgets.WaterColor);
    }

    void BuildCrosshair(RectTransform root)
    {
        // минималистичный прицел: точка + 4 короткие линии с зазором
        var center = new GameObject("Crosshair", typeof(RectTransform));
        center.transform.SetParent(root, false);
        var crt = (RectTransform)center.transform;

        var dot = UiWidgets.Panel(crt, "Dot", new Color(0.92f, 0.92f, 0.88f, 0.85f));
        var drt = (RectTransform)dot.transform;
        drt.anchorMin = drt.anchorMax = new Vector2(0.5f, 0.5f);
        drt.sizeDelta = new Vector2(3f, 3f);
        drt.anchoredPosition = Vector2.zero;

        var line = new Color(0.92f, 0.92f, 0.88f, 0.55f);
        MakeLine(crt, new Vector2(10f, 2f), new Vector2(11f, 0f), line);   // право
        MakeLine(crt, new Vector2(10f, 2f), new Vector2(-11f, 0f), line);  // лево
        MakeLine(crt, new Vector2(2f, 10f), new Vector2(0f, 11f), line);   // верх
        MakeLine(crt, new Vector2(2f, 10f), new Vector2(0f, -11f), line);  // низ
    }

    void MakeLine(RectTransform parent, Vector2 size, Vector2 pos, Color color)
    {
        var img = UiWidgets.Panel(parent, "Line", color);
        img.raycastTarget = false;
        var rt = (RectTransform)img.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
    }

    void BuildPrompt(RectTransform root)
    {
        promptText = UiWidgets.Text(root, "Prompt", 20);
        var rt = (RectTransform)promptText.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(0f, -58f);
        rt.sizeDelta = new Vector2(900f, 28f);
        promptText.color = new Color(0.93f, 0.90f, 0.78f);
    }

    // ---------- обновление ----------

    void RefreshAll()
    {
        RefreshStats();
        RefreshInventory();
        RefreshSelection();
    }

    void RefreshStats()
    {
        if (stats == null) return;
        hpBar.Set(stats.Hp, stats.maxHp);
        foodBar.Set(stats.Food, stats.maxFood);
        waterBar.Set(stats.Water, stats.maxWater);
    }

    void RefreshInventory()
    {
        hotbar.Refresh(inventory);
        inventoryPanel.Refresh(inventory);
    }

    void RefreshSelection()
    {
        hotbar.Refresh(inventory);
        inventoryPanel.Refresh(inventory);
    }

    void OnPrompt(string prompt)
    {
        promptText.text = prompt ?? "";
        promptText.enabled = !string.IsNullOrEmpty(prompt);
    }

    void OnNotify(string text) => toasts.Push(text);

    void OnPlayerDied()
    {
        dead = true;
        SetInventoryOpen(false);
        deathScreen.Show();
        SyncUiActive();
    }

    // ---------- состояния ----------

    void SetInventoryOpen(bool open)
    {
        inventoryOpen = open;
        inventoryPanel.gameObject.SetActive(open);
        SyncUiActive();
    }

    void SetPaused(bool on)
    {
        paused = on;
        pauseMenu.gameObject.SetActive(on);
        Time.timeScale = on ? 0f : 1f;
        GameEvents.RaisePauseChanged(on);
        SyncUiActive();
    }

    void Restart()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    // Любое UI-состояние блокирует ввод Player и отпускает курсор
    // (Player сам следит за InventoryOpenChanged).
    void SyncUiActive()
    {
        bool active = inventoryOpen || paused || dead;
        if (active == uiActive) return;
        uiActive = active;
        GameEvents.RaiseInventoryOpenChanged(active);
    }
}
