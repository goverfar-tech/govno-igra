using UnityEngine;

// AudioManager — порт scripts/autoload/audio_manager.gd (Поток Б «звук», M5).
// Все клипы процедурные (ProceduralSfx), внешних аудио-ассетов нет.
// Создаёт себя сам в рантайме при старте сцены — на объекты сцены ничего
// вешать не нужно; переживает перезагрузку сцены (смерть → R → LoadScene).
//
// Подписки GameEvents:
//   InventoryChanged  — тихий блип подбора (шина срабатывает на любое
//                       изменение инвентаря, звук читается как UI-фидбек);
//   ItemConsumed      — звук еды/питья (событие поднимает тот, кто тратит
//                       предмет — проводка за пределами Потока Б);
//   WorldHit          — позиционный «тук» по мировому объекту;
//   Notify            — едва слышный UI-тик на тосты;
//   PlayerDied        — стинг гибели;
//   TimeOfDayChanged  — ночью ветер гуще и тоном выше (§9.6: атмосфера
//                       важнее реализма).
//
// Шаги: находим Player (FindFirstObjectByType), копим пройденный путь по
// CharacterController.velocity — звук каждые ~1.9 м, как в эталоне.
// Бег — громкий мокрый плеск майонеза (§9.2.3: слышно и игроку, и зомби).
//
// Другим системам доступны статические вызовы в стиле Godot-автолоада:
// AudioManager.HitAt(pos), .Swing(), .GruntAt(pos), .SquealAt(pos).
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Header("Громкости (dB, значения из Godot-эталона)")]
    public float windDayDb = -34f;
    public float windNightDb = -22f;   // ночью гул заметно гуще
    public float stepWalkDb = -19f;
    public float stepCrouchDb = -22f;
    public float stepSprintDb = -12f;  // эталон -17; плеск — часть стелс-механики
    public float pickupDb = -14f;
    public float eatDb = -15f;
    public float swingDb = -16f;
    public float hitDb = -16f;
    public float toastDb = -28f;
    public float deathDb = -6f;
    public float gurgleDb = -18f;

    [Header("UI-подсказки слуху (клики/шорохи)")]
    public float selectDb = -24f;      // смена слота хотбара
    public float rustleDb = -22f;      // открытие/закрытие инвентаря
    public float pauseDb = -26f;       // пауза

    [Header("Шаги")]
    public float stepDistance = 1.9f;  // метров пути на один шаг (эталон)
    public float minStepSpeed = 0.4f;  // медленнее — считаем, что стоим

    [Header("Далёкое чавканье в тумане (только ночью)")]
    public float gurgleMinGap = 7f;    // секунд между звуками, минимум
    public float gurgleMaxGap = 18f;
    public float gurgleMinDist = 12f;  // радиус области вокруг игрока
    public float gurgleMaxDist = 20f;

    [Header("Треск костров (навешивается на объекты с FireLight)")]
    public float crackleDb = -14f;     // громкость у самого костра
    public float crackleMinDist = 2f;
    public float crackleMaxDist = 12f;
    public float fireScanInterval = 3f; // секунд между сканами сцены

    AudioClip stepClip, stepSplashClip, hitClip, pickupClip, eatClip;
    AudioClip whooshClip, windClip, gruntClip, squealClip;
    AudioClip gurgleClip, deathClip, toastClip;
    AudioClip selectClip, openClip, closeClip, pauseClip, crackleClip;

    AudioSource flatSource; // 2D одношоты (UI/еда/стинг); pitch ставится перед PlayOneShot
    AudioSource stepSource; // шаги отдельно — не перебивают UI-звуки pitch'ем
    AudioSource windSource; // зацикленный ветер

    Player player;
    CharacterController cc;
    float playerSearchRetry;

    bool isNight;
    float stepAccum;
    float lastPickupSfx = -99f;
    float lastToastSfx = -99f;
    float lastSelectSfx = -99f;
    float nextGurgleTime = 12f;
    float nextFireScan = 2f; // первый скан почти сразу — костры на спауне

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        new GameObject(nameof(AudioManager)).AddComponent<AudioManager>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        BuildClips();
        BuildSources();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void OnEnable()
    {
        GameEvents.InventoryChanged += OnInventoryChanged;
        GameEvents.ItemConsumed += OnItemConsumed;
        GameEvents.WorldHit += OnWorldHit;
        GameEvents.Notify += OnNotify;
        GameEvents.PlayerDied += OnPlayerDied;
        GameEvents.TimeOfDayChanged += OnTimeOfDayChanged;
        GameEvents.SelectionChanged += OnSelectionChanged;
        GameEvents.InventoryOpenChanged += OnInventoryOpenChanged;
        GameEvents.PauseChanged += OnPauseChanged;
    }

    void OnDisable()
    {
        GameEvents.InventoryChanged -= OnInventoryChanged;
        GameEvents.ItemConsumed -= OnItemConsumed;
        GameEvents.WorldHit -= OnWorldHit;
        GameEvents.Notify -= OnNotify;
        GameEvents.PlayerDied -= OnPlayerDied;
        GameEvents.TimeOfDayChanged -= OnTimeOfDayChanged;
        GameEvents.SelectionChanged -= OnSelectionChanged;
        GameEvents.InventoryOpenChanged -= OnInventoryOpenChanged;
        GameEvents.PauseChanged -= OnPauseChanged;
    }

    // ---------- построение ----------

    void BuildClips()
    {
        stepClip = ProceduralSfx.MakeStep();
        stepSplashClip = ProceduralSfx.MakeStepSplash();
        hitClip = ProceduralSfx.MakeHit();
        pickupClip = ProceduralSfx.MakeBlip("sfx_pickup", 660f, 880f, 0.12f);
        eatClip = ProceduralSfx.MakeBlip("sfx_eat", 440f, 260f, 0.25f);
        toastClip = ProceduralSfx.MakeBlip("sfx_toast", 980f, 1240f, 0.055f);
        squealClip = ProceduralSfx.MakeBlip("sfx_squeal", 600f, 1300f, 0.18f);
        whooshClip = ProceduralSfx.MakeWhoosh();
        windClip = ProceduralSfx.MakeWind(6f);
        gruntClip = ProceduralSfx.MakeGrunt();
        gurgleClip = ProceduralSfx.MakeGurgle();
        deathClip = ProceduralSfx.MakeDeathSting();
        selectClip = ProceduralSfx.MakeBlip("sfx_ui_select", 1400f, 2000f, 0.03f);
        openClip = ProceduralSfx.MakeRustle(true);
        closeClip = ProceduralSfx.MakeRustle(false);
        pauseClip = ProceduralSfx.MakeBlip("sfx_ui_pause", 420f, 300f, 0.07f);
        crackleClip = ProceduralSfx.MakeCrackle();
    }

    void BuildSources()
    {
        flatSource = gameObject.AddComponent<AudioSource>();
        flatSource.spatialBlend = 0f;
        flatSource.playOnAwake = false;

        stepSource = gameObject.AddComponent<AudioSource>();
        stepSource.spatialBlend = 0f;
        stepSource.playOnAwake = false;

        windSource = gameObject.AddComponent<AudioSource>();
        windSource.clip = windClip;
        windSource.loop = true;
        windSource.spatialBlend = 0f;
        windSource.playOnAwake = true;
        windSource.volume = Db(windDayDb);
        windSource.Play();
    }

    // ---------- Update-циклы ----------

    void Update()
    {
        FindPlayer();
        TickWind(Time.deltaTime);
        TickFootsteps(Time.deltaTime);
        TickGurgle();
        TickFireScan();
    }

    void FindPlayer()
    {
        if (player != null) return;
        if (Time.time < playerSearchRetry) return;
        playerSearchRetry = Time.time + 1f;
        player = FindFirstObjectByType<Player>();
        if (player != null) cc = player.GetComponent<CharacterController>();
    }

    // Ветер дышит к ночи: громкость и тон ползут к ночным значениям
    // (сам клип зациклен; смена плавная, без щелчков на закате/рассвете).
    void TickWind(float dt)
    {
        float targetVol = Db(isNight ? windNightDb : windDayDb);
        windSource.volume = Mathf.MoveTowards(windSource.volume, targetVol, dt * 0.012f);
        float targetPitch = isNight ? 1.06f : 1f;
        windSource.pitch = Mathf.MoveTowards(windSource.pitch, targetPitch, dt * 0.05f);
    }

    // Каденс из эталона (_update_footsteps): звук каждые ~1.9 м пути,
    // крадёмся — тише, бежим — громче и мокрее.
    void TickFootsteps(float dt)
    {
        if (player == null || cc == null || player.Stats.IsDead) return;

        Vector3 v = cc.velocity;
        v.y = 0f;
        float speed = v.magnitude;
        if (!cc.isGrounded || speed < minStepSpeed)
        {
            stepAccum = 0f;
            return;
        }

        stepAccum += speed * dt;
        if (stepAccum < stepDistance) return;
        stepAccum = 0f;

        bool crouching = cc.height < (player.standHeight + player.crouchHeight) * 0.5f;
        bool sprinting = !crouching && speed > player.walkSpeed * 1.15f;
        stepSource.pitch = Random.Range(0.94f, 1.06f);
        if (sprinting)
            stepSource.PlayOneShot(stepSplashClip, Db(stepSprintDb));
        else
            stepSource.PlayOneShot(stepClip, Db(crouching ? stepCrouchDb : stepWalkDb));
    }

    // По ночам в тумане что-то чавкает (§9.6). Позиция случайная вокруг
    // игрока — источника не видно, и не нужно: звук здесь и есть геймплей.
    void TickGurgle()
    {
        if (Time.time < nextGurgleTime) return;
        nextGurgleTime = Time.time + Random.Range(gurgleMinGap, gurgleMaxGap);
        if (!isNight || player == null) return;

        Vector3 dir = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;
        Vector3 pos = player.transform.position + dir * Random.Range(gurgleMinDist, gurgleMaxDist);
        PlayAt(gurgleClip, pos, gurgleDb, Random.Range(0.75f, 1.1f));
    }

    // Раз в fireScanInterval секунд находим костры (объекты с FireLight)
    // без AudioSource и навешиваем зацикленный процедурный треск.
    // Безобидно при перезагрузке сцены: погибшие костры отпадают сами,
    // новые (построенные игроком) подхватываются очередным сканом.
    void TickFireScan()
    {
        if (Time.time < nextFireScan) return;
        nextFireScan = Time.time + fireScanInterval;

        foreach (var fire in FindObjectsByType<FireLight>(FindObjectsSortMode.None))
        {
            if (fire == null || fire.GetComponent<AudioSource>() != null) continue;
            var src = fire.gameObject.AddComponent<AudioSource>();
            src.clip = crackleClip;
            src.loop = true;
            src.spatialBlend = 1f;
            src.dopplerLevel = 0f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = crackleMinDist;
            src.maxDistance = crackleMaxDist;
            src.pitch = Random.Range(0.9f, 1.1f);
            src.volume = Db(crackleDb);
            src.Play();
        }
    }

    // ---------- обработчики GameEvents ----------

    void OnInventoryChanged()
    {
        if (Time.time - lastPickupSfx < 0.08f) return; // не дробить пачку событий в кадре
        lastPickupSfx = Time.time;
        flatSource.pitch = Random.Range(0.97f, 1.04f);
        flatSource.PlayOneShot(pickupClip, Db(pickupDb));
    }

    void OnItemConsumed(ItemData item)
    {
        flatSource.pitch = Random.Range(0.94f, 1.06f);
        flatSource.PlayOneShot(eatClip, Db(eatDb));
    }

    void OnWorldHit(Vector3 pos) => PlayHitAt(pos);

    void OnNotify(string text)
    {
        if (Time.time - lastToastSfx < 0.12f) return;
        lastToastSfx = Time.time;
        flatSource.pitch = 1f;
        flatSource.PlayOneShot(toastClip, Db(toastDb));
    }

    void OnPlayerDied()
    {
        flatSource.pitch = 1f;
        flatSource.PlayOneShot(deathClip, Db(deathDb));
    }

    void OnTimeOfDayChanged(float t, bool night) => isNight = night;

    // Короткий «тик» при смене слота (колёсико может дать серию за кадр —
    // режем дросселем, иначе звук складывается в пулемётную очередь).
    void OnSelectionChanged()
    {
        if (Time.time - lastSelectSfx < 0.05f) return;
        lastSelectSfx = Time.time;
        flatSource.pitch = Random.Range(0.96f, 1.06f);
        flatSource.PlayOneShot(selectClip, Db(selectDb));
    }

    void OnInventoryOpenChanged(bool open)
    {
        flatSource.pitch = Random.Range(0.96f, 1.05f);
        flatSource.PlayOneShot(open ? openClip : closeClip, Db(rustleDb));
    }

    void OnPauseChanged(bool paused)
    {
        flatSource.pitch = 1f;
        flatSource.PlayOneShot(pauseClip, Db(pauseDb));
    }

    // ---------- проигрывание ----------

    // Позиционный одношот: временный AudioSource в мире, самоуничтожается.
    void PlayAt(AudioClip clip, Vector3 pos, float db, float pitch, float maxDist = 32f)
    {
        var go = new GameObject("sfx3d_" + clip.name);
        go.transform.position = pos;
        var src = go.AddComponent<AudioSource>();
        src.clip = clip;
        src.spatialBlend = 1f;
        src.dopplerLevel = 0f;
        src.rolloffMode = AudioRolloffMode.Linear;
        src.minDistance = 3f;
        src.maxDistance = maxDist;
        src.pitch = pitch;
        src.volume = Db(db);
        src.Play();
        Destroy(go, clip.length / Mathf.Max(0.01f, Mathf.Abs(pitch)) + 0.1f);
    }

    static float Db(float db) => Mathf.Pow(10f, db / 20f);

    // ---------- публичный API для других систем ----------

    // Удар инструментом по мировому объекту (бой/добыча — ведёт Поток А).
    public void PlayHitAt(Vector3 pos)
        => PlayAt(hitClip, pos, hitDb, Random.Range(0.94f, 1.08f));

    // Взмах инструментом (воздух).
    public void PlaySwing()
    {
        flatSource.pitch = Random.Range(0.94f, 1.06f);
        flatSource.PlayOneShot(whooshClip, Db(swingDb));
    }

    public static void HitAt(Vector3 pos) { if (Instance != null) Instance.PlayHitAt(pos); }
    public static void Swing() { if (Instance != null) Instance.PlaySwing(); }
    public static void GruntAt(Vector3 pos, float db = -6f)
    {
        if (Instance != null)
            Instance.PlayAt(Instance.gruntClip, pos, db, Random.Range(0.94f, 1.08f), 26f);
    }
    public static void SquealAt(Vector3 pos)
    {
        if (Instance != null)
            Instance.PlayAt(Instance.squealClip, pos, -6f, Random.Range(0.94f, 1.08f), 26f);
    }
}
