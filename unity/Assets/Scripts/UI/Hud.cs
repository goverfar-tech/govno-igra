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
//
// Hud переживает смену сцены (синглтон + DontDestroyOnLoad): рестарт
// после смерти грузит Main заново, а HudBootstrap срабатывает только
// раз за сессию — без этого весь UI исчезал бы навсегда. Весь UI лежит
// дочерним к нашему GameObject (HudCanvas), поэтому переезжает вместе
// с ним. Сценные ссылки (Player/Stats/Inventory, DebugHud, EventSystem)
// перепривязываются в SceneManager.sceneLoaded — см. OnSceneLoaded.
public class Hud : MonoBehaviour
{
    // Рестарт (экран смерти/пауза) пережидает смену сцены статикой —
    // после «Начать заново» стартовое меню не показываем.
    static bool skipMenuOnce;
    public static void SkipMenuOnNextLoad() => skipMenuOnce = true;

    // Сброс статики на вход в Play/перезагрузку домена: без этого
    // skipMenuOnce мог пережить запуск и стартовое меню не показывалось
    // (вижу «игра сразу генерит мир без меню»). При включённом домен-
    // ресеете это дёшево и безвредно.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        instance = null;
        skipMenuOnce = false;
    }

    // Единственный живой Hud; копии из перезагруженной сцены самоубиваются.
    static Hud instance;

    Stats stats;
    Inventory inventory;

    Canvas canvas;
    // Полоски: майонез (HP+еда), яд, небесные часы (§9.2)
    StatBar mayoBar, poisonBar, timeBar;
    float timeOfDay;
    bool isNight;
    HotbarView hotbar;
    Minimap minimap;    // миникарта с туманом войны (HoMM3-стиль)
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

    // Кэш последних отрисованных значений: RefreshStats прилетает по два
    // события за кадр (StatsChanged + TimeOfDayChanged) — перерисовываем
    // тексты только когда хоть одно значение реально сменилось.
    float lastMayo = -1f, lastPoison = -1f, lastTime = -1f;
    bool lastNight;

    // Красная вспышка при уроне (R4, §11.5 «урон ощутим»): полноэкранная
    // вуаль поверх мира, но ниже экрана смерти/меню (порядок в Build).
    Image damageVeil;
    float flashTimer;   // оставшееся время гашения
    float flashPeak;    // стартовая альфа текущей вспышки
    const float FlashThreshold = 1.5f; // разовая потеря больше — это удар,
                                       // а не утечка/яд (те ≤ ~0.4 за кадр)
    const float FlashDuration = 0.35f;

    void Awake()
    {
        // синглтон-гвард: копия из перезагруженной сцены не нужна —
        // наш UI уже живёт поверх и перепривяжется в OnSceneLoaded
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;

        // временный IMGUI-HUD больше не нужен (файл не трогаем — лишь гасим объект)
        var dbg = FindAnyObjectByType<DebugHud>(FindObjectsInactive.Include);
        if (dbg != null) dbg.gameObject.SetActive(false);

        BindScene();

        GameSettings.ApplyAll(); // FOV/чувствительность/громкость из PlayerPrefs

        EnsureEventSystem();
        Build();

        // стартовое меню — только на «свежем» запуске, не после рестарта
        if (skipMenuOnce) { skipMenuOnce = false; menuOpen = false; }
        else menuOpen = true;
        // Open() добавочно зовёт RefreshButtons — «Продолжить» сразу если сейв
        if (menuOpen) mainMenu.Open(); // объект уже скрыт в MainMenu.Create
        SyncUiActive();
        SyncTimeScale();

        RefreshAll();
    }

    void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }
    }

    // Player/Stats/Inventory — сценные объекты, на рестарте пересоздаются.
    // Вызывается из Awake и на каждой загрузке новой сцены.
    void BindScene()
    {
        var player = FindAnyObjectByType<Player>();
        stats = player != null ? player.GetComponent<Stats>() : null;
        inventory = player != null ? player.GetComponent<Inventory>() : null;
    }

    // Загрузка ЛЮБОЙ сцены: перепривязать сценное и синхронизировать
    // состояние меню с флагом-одноразовиком skipMenuOnce.
    // Важно про порядок старта: с запечённым в сцене Hud (SetupHud)
    // Awake отрабатывает в процессе загрузки стартовой сцены, а это
    // событие приходит ПОСЛЕ — поэтому меню тут вычисляется заново,
    // а не гасится безусловно (был баг «меню не появляется при старте»).
    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (this != instance) return; // подстраховка: дубликат молчит

        EnsureEventSystem(); // EventSystem жил в старой сцене и разрушен

        // DebugHud свежей сцены снова активен — гасим повторно
        var dbg = FindAnyObjectByType<DebugHud>(FindObjectsInactive.Include);
        if (dbg != null) dbg.gameObject.SetActive(false);

        BindScene();
        GameSettings.ApplyAll(); // FOV/чувствительность — новой камере/игроку

        // меню: свежий вход → открыто; сюда же приходит рестарт после
        // SkipMenuOnNextLoad → флаг съедается, меню не показываем
        if (skipMenuOnce) { skipMenuOnce = false; menuOpen = false; }
        else menuOpen = true;
        settingsOpen = false;
        settingsPanel.Hide();
        // Open() заодно обновляет состояние «Продолжить» (есть ли сейв)
        if (menuOpen) mainMenu.Open(); else mainMenu.Close();
        inventoryOpen = false;
        inventoryPanel.gameObject.SetActive(false); // OnDisable панели довернёт драг
        craftOpen = false;
        craftPanel.gameObject.SetActive(false);
        paused = false;
        pauseMenu.Hide();
        dead = false;
        deathScreen.Hide();
        TooltipService.Hide();

        // на рестарте вуаль урона не должна догорать в свежей сцене
        flashTimer = 0f;
        if (damageVeil != null) SetVeilAlpha(0f);

        SyncUiActive();
        SyncTimeScale();
        RefreshAll();
    }

    void OnEnable()
    {
        GameEvents.StatsChanged += OnStatsChanged;
        GameEvents.InventoryChanged += RefreshInventory;
        GameEvents.SelectionChanged += RefreshSelection;
        GameEvents.PromptChanged += OnPrompt;
        GameEvents.Notify += OnNotify;
        GameEvents.PlayerDied += OnPlayerDied;
        GameEvents.TimeOfDayChanged += OnTime;
    }

    void OnTime(float t, bool night)
    {
        timeOfDay = t;
        isNight = night;
        RefreshStats();
    }

    void OnDisable()
    {
        GameEvents.StatsChanged -= OnStatsChanged;
        GameEvents.InventoryChanged -= RefreshInventory;
        GameEvents.SelectionChanged -= RefreshSelection;
        GameEvents.PromptChanged -= OnPrompt;
        GameEvents.Notify -= OnNotify;
        GameEvents.PlayerDied -= OnPlayerDied;
        GameEvents.TimeOfDayChanged -= OnTime;
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

        // Вуаль урона (R4) гаснет линейно от пика. У мёртвого — сразу
        // в ноль, чтобы красное не застряло под экраном смерти.
        // Блок стоит ДО return по dead: иначе гашение бы не добегало.
        if (damageVeil != null && damageVeil.color.a > 0f)
        {
            if (dead) flashTimer = 0f;
            else flashTimer = Mathf.Max(0f, flashTimer - Time.unscaledDeltaTime);
            SetVeilAlpha(flashPeak * (flashTimer / FlashDuration));
        }

        if (dead)
        {
            // смерть — corpse run (§9.5), не рестарт мира
            if (Input.GetKeyDown(KeyCode.R)) RespawnPlayer();
            return;
        }

        // Esc обрабатываем ДО return по menuOpen: иначе из стартового меню
        // нельзя закрыть настройки по Esc (окно поверх меню).
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (settingsOpen) CloseSettings();
            else if (!menuOpen)
            {
                if (craftOpen) SetCraftOpen(false);
                else if (inventoryOpen) SetInventoryOpen(false);
                else SetPaused(!paused);
            }
            // в самом главном меню Esc ничего не делает: выбор кнопками
        }

        if (menuOpen) return; // в меню остальной выбор только кнопками
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
        canvasGo.transform.SetParent(transform, false); // под нами — переживёт сцену
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
        // Миникарта: панель/туман/маркеры строит сама, игрока находит сама
        // (лениво, в тике) — сюда только отдаём канвас. Ставим до модальных
        // окон: карта лежит ниже экранов смерти/паузы по z.
        minimap = new GameObject("Minimap", typeof(RectTransform)).AddComponent<Minimap>();
        minimap.Init(crt);
        BuildDamageVeil(crt); // ДО deathScreen/pauseMenu/mainMenu: вуаль
                              // выше мира и HUD, но под экраном смерти/меню
        deathScreen = DeathScreen.Create(crt);
        deathScreen.RestartRequested = RespawnPlayer; // §9.5: воскрешение, мир сохраняется
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

        mayoBar = StatBar.Create(panel.transform, "Майонез", UiWidgets.FoodColor);
        poisonBar = StatBar.Create(panel.transform, "Яд", new Color(0.55f, 0.75f, 0.3f));
        timeBar = StatBar.Create(panel.transform, "Небо", new Color(0.35f, 0.4f, 0.6f));
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

    // Красная вуаль урона на весь экран. Изначально невидима (альфа 0),
    // проявляется FlashDamage и гаснет в Update.
    void BuildDamageVeil(RectTransform root)
    {
        damageVeil = UiWidgets.FullscreenVeil(root, "DamageVeil",
            new Color(0.62f, 0.05f, 0.03f, 0f));
    }

    // Вспышка: сила пропорциональна выбитому майонезу (clamp 0.25..0.55) —
    // лёгкий тычок и полноценный удар зомби читаются по-разному.
    void FlashDamage(float drop)
    {
        if (damageVeil == null) return;
        flashPeak = Mathf.Clamp(drop / 15f, 0.25f, 0.55f);
        flashTimer = FlashDuration;
        SetVeilAlpha(flashPeak);
    }

    void SetVeilAlpha(float a)
    {
        var c = damageVeil.color;
        c.a = a;
        damageVeil.color = c;
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

        // Дедупе: StatsChanged и TimeOfDayChanged прилетают парой за кадр —
        // нечего дёргать uGUI-тексты, пока ни одно значение не изменилось.
        if (Mathf.Approximately(stats.Mayo, lastMayo)
            && Mathf.Approximately(stats.Poison, lastPoison)
            && Mathf.Approximately(timeOfDay, lastTime)
            && isNight == lastNight) return;
        // R4: разовая потеря майонеза выше порога — это урон (удар зомби),
        // а не фоновая утечка или яд. lastMayo здесь ещё СТАРОЕ значение
        // (обновляется строками ниже), dead не вспыхивает.
        if (lastMayo >= 0f && !dead)
        {
            float drop = lastMayo - stats.Mayo;
            if (drop > FlashThreshold) FlashDamage(drop);
        }
        lastMayo = stats.Mayo;
        lastPoison = stats.Poison;
        lastTime = timeOfDay;
        lastNight = isNight;

        // §9.2: ведро = здоровье+сытость слились в «Майонез». Яд — статус.
        mayoBar.Set(stats.Mayo, stats.maxMayo);
        // пустеющее ведро мигает красным — смерть должна читаться заранее
        bool low = stats.Mayo / stats.maxMayo < 0.25f;
        mayoBar.SetFillColor(low
            ? Color.Lerp(UiWidgets.FoodColor, new Color(0.95f, 0.25f, 0.2f),
                         (Mathf.Sin(Time.unscaledTime * 6f) + 1f) * 0.5f)
            : UiWidgets.FoodColor);
        poisonBar.Set(stats.Poison, stats.maxPoison);
        // небесные часы — фазой суток и процентом, а не голым 0/1
        int pct = Mathf.RoundToInt(Mathf.Clamp01(timeOfDay) * 100f);
        string phase = isNight ? "ночь" : "день";
        timeBar.Set(timeOfDay, 1f, $"Небо {phase} {pct}%");
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

    // StatsChanged — ещё и канал «оживления»: F9/«Продолжить» гоняют
    // Stats.SetState, который снимает IsDead. Гасим экран смерти тут,
    // иначе после загрузки сейва он висел бы до рестарта.
    void OnStatsChanged()
    {
        if (dead && stats != null && !stats.IsDead)
        {
            dead = false;
            deathScreen.Hide();
            SyncUiActive();
            SyncTimeScale();
        }
        RefreshStats();
    }

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
        if (open && inventory == null) return; // нет игрока — показывать нечего
        inventoryOpen = open;
        inventoryPanel.gameObject.SetActive(open);
        if (open && craftOpen) // панели взаимоисключают друг друга
        {
            craftOpen = false;
            craftPanel.gameObject.SetActive(false);
        }
        if (!open) TooltipService.Hide();
        SyncUiActive();
    }

    void SetCraftOpen(bool open)
    {
        if (open && inventory == null) return;
        craftOpen = open;
        craftPanel.gameObject.SetActive(open);
        if (open && inventoryOpen)
        {
            inventoryOpen = false;
            inventoryPanel.gameObject.SetActive(false);
        }
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
        if (settingsFromMenu) mainMenu.Open(); // +RefreshButtons заодно
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

    // Смерть — не конец забега (§9.5): дроп инвентаря на месте смерти,
    // воскрешение у последнего костра. Полный сброс мира — только
    // осознанным «Начать заново» из паузы (Restart там и остаётся).
    void RespawnPlayer()
    {
        var player = FindAnyObjectByType<Player>();
        if (player == null) { Restart(); return; } // страховка на странную сцену
        // Stats.SetState внутри сбросит IsDead и кинет StatsChanged —
        // OnStatsChanged сам скроет экран смерти и разблокирует ввод
        player.Respawn();
    }

    void Restart()
    {
        SkipMenuOnNextLoad();
        // Снимаем паузу ДО загрузки: переживающие сцену слушатели
        // (AudioManager) иначе навсегда останутся в паузном состоянии.
        if (paused) { paused = false; GameEvents.RaisePauseChanged(false); }
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
