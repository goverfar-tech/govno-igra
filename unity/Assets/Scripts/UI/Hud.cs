using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Корневой uGUI-HUD (этап M3, Поток В). Заменяет временный DebugHud
// на IMGUI; сам DebugHud не правим — при старте выключаем его GameObject.
//
// Весь интерфейс строится кодом в Awake: Canvas + CanvasScaler +
// GraphicRaycaster, полоски статов, прицел, подсказка, тосты, хотбар,
// панель инвентаря, экран смерти, пауза, стартовое главное меню,
// настройки, тултипы.
//
// Источники данных: Player/Inventory/Stats находятся сами по сцене,
// обновления — по событиям GameEvents (§4 архитектуры).
//
// Состояния: игра | инвентарь (Tab) | крафт (C) | пауза (Esc) | смерть |
// стартовое меню | настройки (поверх меню/паузы).
// Escape-цепочка: настройки → крафт → инвентарь → пауза.
// Любое открытое UI-состояние агрегируется в uiActive и вещается
// наружу как InventoryOpenChanged — Player на него уже реагирует
// (блок ввода + освобождение курсора); курсор для верности дублируем тут.
// timeScale = 0 ровно когда открыто меню или пауза (SyncTimeScale).
public class Hud : MonoBehaviour
{
    // Рестарт (экран смерти/пауза) пережидает смену сцены статикой —
    // после «Начать заново» стартовое меню не показываем.
    static bool skipMenuOnce;
    public static void SkipMenuOnNextLoad() => skipMenuOnce = true;

    Stats stats;
    Inventory inventory;

    Canvas canvas;
    StatBar hpBar, foodBar, waterBar;
    HotbarView hotbar;
    Text promptText;
    ToastFeed toasts;
    InventoryPanelView inventoryPanel;
    CraftPanel craftPanel;
    DeathScreen deathScreen;
    PauseMenu pauseMenu;
    MainMenu mainMenu;
    SettingsPanel settingsPanel;

    bool inventoryOpen;
    bool craftOpen;     // окно крафта (C)
    bool paused;
    bool dead;
    bool menuOpen;      // стартовое меню
    bool settingsOpen;  // настройки поверх меню или паузы
    bool settingsFromMenu;
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

        GameSettings.ApplyAll(); // FOV/чувствительность/громкость из PlayerPrefs

        EnsureEventSystem();
        Build();

        // стартовое меню — только на «свежем» запуске, не после рестарта
        if (skipMenuOnce) { skipMenuOnce = false; menuOpen = false; }
        else menuOpen = true;
        mainMenu.gameObject.SetActive(menuOpen);
        SyncUiActive();
        SyncTimeScale();

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
        // Гонка старта: Player.Awake тоже лочит курсор и мог отработать
        // ПОСЛЕ нашего Awake. Пока открыт любой модальный UI — держим
        // курсор свободным каждый кадр, а не только на переходах.
        if (uiActive && Cursor.lockState != CursorLockMode.None)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        if (dead)
        {
            if (Input.GetKeyDown(KeyCode.R)) Restart();
            return;
        }
        if (menuOpen) return; // в меню выбор только кнопками
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (settingsOpen) CloseSettings();
            else if (craftOpen) SetCraftOpen(false);
            else if (inventoryOpen) SetInventoryOpen(false);
            else SetPaused(!paused);
        }
        if (Input.GetKeyDown(KeyCode.Tab) && !paused && !settingsOpen)
            SetInventoryOpen(!inventoryOpen);
        if (Input.GetKeyDown(KeyCode.C) && !paused && !settingsOpen)
            SetCraftOpen(!craftOpen);
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
        hotbar = HotbarView.Create(crt);
        inventoryPanel = InventoryPanelView.Create(crt, inventory);
        craftPanel = CraftPanel.Create(crt, inventory);
        deathScreen = DeathScreen.Create(crt);
        deathScreen.RestartRequested = Restart;
        pauseMenu = PauseMenu.Create(crt);
        pauseMenu.ResumeRequested = () => SetPaused(false);
        pauseMenu.SettingsRequested = () => OpenSettings(false);
        pauseMenu.RestartRequested = Restart;
        settingsPanel = SettingsPanel.Create(crt);
        settingsPanel.BackRequested = CloseSettings;
        mainMenu = MainMenu.Create(crt);
        mainMenu.ContinueRequested = ContinueGame;
        mainMenu.NewGameRequested = CloseMenu;
        mainMenu.SettingsRequested = () => OpenSettings(true);
        // тосты и тултип — последними, чтобы лежали поверх меню/паузы
        toasts = ToastFeed.Create(crt);
        TooltipService.Init(crt);
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
        craftPanel.Refresh(inventory);
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
        SetCraftOpen(false);
        deathScreen.Show();
        SyncUiActive();
    }

    // ---------- состояния ----------

    void SetInventoryOpen(bool open)
    {
        inventoryOpen = open;
        inventoryPanel.gameObject.SetActive(open);
        if (!open) TooltipService.Hide();
        SyncUiActive();
    }

    void SetCraftOpen(bool open)
    {
        craftOpen = open;
        craftPanel.gameObject.SetActive(open);
        if (!open) TooltipService.Hide();
        SyncUiActive();
    }

    void SetPaused(bool on)
    {
        if (dead || menuOpen) return;
        paused = on;
        pauseMenu.gameObject.SetActive(on);
        if (!on && settingsOpen) CloseSettings(); // не оставляем настройки без паузы
        GameEvents.RaisePauseChanged(on);
        SyncUiActive();
        SyncTimeScale();
    }

    void OpenSettings(bool fromMenu)
    {
        settingsFromMenu = fromMenu;
        settingsOpen = true;
        if (fromMenu) mainMenu.gameObject.SetActive(false);
        settingsPanel.Show();
        SyncUiActive();
    }

    void CloseSettings()
    {
        settingsOpen = false;
        settingsPanel.Hide();
        GameSettings.Flush();
        if (settingsFromMenu) mainMenu.gameObject.SetActive(true);
        SyncUiActive();
    }

    // «Продолжить» в стартовом меню: загрузить сейв (та же логика, что F9).
    // Сейва нет — остаёмся в меню, тост «Нет сохранения» покажет SaveSystem.
    void ContinueGame()
    {
        var ss = FindAnyObjectByType<SaveSystem>();
        if (ss != null && ss.LoadGame())
            CloseMenu();
    }

    // «Новая игра»: просто снять меню — мир сгенерирован при старте сцены.
    void CloseMenu()
    {
        if (!menuOpen) return;
        menuOpen = false;
        if (settingsOpen) CloseSettings();
        mainMenu.Close();
        SyncUiActive();
        SyncTimeScale();
    }

    void Restart()
    {
        SkipMenuOnNextLoad();
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    // Любое UI-состояние блокирует ввод Player и отпускает курсор
    // (Player сам следит за InventoryOpenChanged; курсор дублируем тут,
    // чтобы не зависеть от порядка подписки при старте сцены).
    void SyncUiActive()
    {
        bool active = inventoryOpen || craftOpen || paused || dead || menuOpen || settingsOpen;
        if (active == uiActive) return;
        uiActive = active;
        GameEvents.RaiseInventoryOpenChanged(active);
        if (active)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    // Стоп-кадр держится ровно на меню и паузе; смерть мир не замирает.
    void SyncTimeScale()
    {
        float target = (menuOpen || paused) ? 0f : 1f;
        if (!Mathf.Approximately(Time.timeScale, target))
            Time.timeScale = target;
    }
}
