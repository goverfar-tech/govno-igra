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
//   WorldHit          — НЕ подписаны: Player при попадании вызывает
//                       AudioManager.HitAt напрямую, второй звук через
//                       подписку давал бы дабл (событие остаётся на шине
//                       для будущих систем);
//   Notify            — едва слышный UI-тик на тосты;
//   PlayerDied        — стинг гибели;
//   TimeOfDayChanged  — ночью ветер гуще и тоном выше + редкие далёкие
//                       вскрики со stereo-разносом (§9.6: атмосфера
//                       важнее реализма);
//   StatsChanged      — HP ниже heartbeatHp: глухой стук сердца (петля
//                       ~1.2 с), тем громче, чем ближе к нулю.
//
// Гул-стон зомби (фон): скан сцены раз в ~2.5 с (FindObjectsByType),
// 2D-петля со слегка дрожащим тоном; громкость — по расстоянию до
// ближайшего (20 м — тишина, 3 м — полная), стая гул не усиливает.
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
    public float windDayDb = -42f;   // было -34: бил по ушам постоянным фоном
    public float windNightDb = -31f;   // ночью гуще, но не заглушает шаги/зомби
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

    [Header("Зомби-гул (2D, по ближайшему живому)")]
    public float zombieMoanDb = -12f;  // громкость вплотную
    public float zombieNearDist = 3f;  // ближе — полная
    public float zombieFarDist = 20f;  // дальше — тишина
    public float zombieScanInterval = 2.5f;

    [Header("Сердце при низком HP")]
    public float heartbeatHp = 30f;    // ниже этого порога — стук
    public float heartbeatMinDb = -24f; // у самого порога
    public float heartbeatMaxDb = -8f;  // у нуля HP

    [Header("Ночные далёкие вскрики (§9.6)")]
    public float nightCryDb = -30f;
    public float nightCryMinGap = 15f;
    public float nightCryMaxGap = 40f;

    [Header("Треск костров (навешивается на объекты с FireLight)")]
    public float crackleDb = -14f;     // громкость у самого костра
    public float crackleMinDist = 2f;
    public float crackleMaxDist = 12f;
    public float fireScanInterval = 3f; // секунд между сканами сцены

    AudioClip stepClip, stepSplashClip, hitClip, pickupClip, eatClip;
    AudioClip whooshClip, windClip, gruntClip, squealClip;
    AudioClip gurgleClip, deathClip, toastClip;
    AudioClip selectClip, openClip, closeClip, pauseClip, crackleClip;
    AudioClip moanClip, heartbeatClip, nightCryClip;

    AudioSource uiSource;        // короткие 2D-блипы (UI/еда/взмах); pitch ставится перед PlayOneShot
    AudioSource worldFlatSource; // длинные стинги (death): pitch СТРОГО 1 — не делит источник с питч-ездыкающими блипами
    AudioSource stepSource;      // шаги отдельно — не перебивают UI-звуки pitch'ем
    AudioSource windSource;      // зацикленный ветер

    [Header("Плеск утечки ведра (R2)")]
    public float leakWalkDb = -36f;    // шёл — почти неслышно
    public float leakSprintDb = -22f;  // бежал — плещет, но не заглушает
    AudioClip squelchClip;
    AudioSource squelchSource;
    AudioSource zombieSource;    // зацикленный гул-стон (громкость = близость)
    AudioSource heartbeatSource; // зацикленный стук сердца
    AudioSource crySource;       // ночные вскрики: одношоты с panStereo

    Player player;
    CharacterController cc;
    float playerSearchRetry;

    // Дедупе синглтона: дубль (повторный вход в Play и т.п.) не строит
    // источники и НЕ подписывается на события.
    bool isMain;

    bool isNight;
    float stepAccum;
    float lastPickupSfx = -99f;
    float lastToastSfx = -99f;
    float lastSelectSfx = -99f;
    float nextGurgleTime = 12f;
    float nextFireScan = 2f; // первый скан почти сразу — костры на спауне
    float nextZombieScan = 3f;
    float moanTarget;        // целевая громкость зомби-гула (линейная)
    float heartbeatTarget;   // целевая громкость сердца (линейная)
    float nextNightCryTime = 20f;

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
            // Дубль: просто умираем. Подписок и источников у него нет —
            // OnEnable/OnDisable их благодаря isMain тоже не тронут.
            Destroy(gameObject);
            return;
        }
        Instance = this;
        isMain = true;
        DontDestroyOnLoad(gameObject);
        // Сначала клипы и источники, и только потом подписки (OnEnable):
        // обработчик события не должен упереться в недостроенный источник.
        BuildClips();
        BuildSources();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void OnEnable()
    {
        if (!isMain) return; // дубль не подписывается — иначе даблы и NRE
        GameEvents.InventoryChanged += OnInventoryChanged;
        GameEvents.ItemConsumed += OnItemConsumed;
        // WorldHit поднимает Player, звук играет HitAt напрямую — подписка
        // убрана во избежание дабла (событие остаётся на шине для будущих систем).
        GameEvents.Notify += OnNotify;
        GameEvents.PlayerDied += OnPlayerDied;
        GameEvents.TimeOfDayChanged += OnTimeOfDayChanged;
        GameEvents.SelectionChanged += OnSelectionChanged;
        GameEvents.InventoryOpenChanged += OnInventoryOpenChanged;
        GameEvents.PauseChanged += OnPauseChanged;
        GameEvents.StatsChanged += OnStatsChanged;
    }

    void OnDisable()
    {
        if (!isMain) return;
        GameEvents.InventoryChanged -= OnInventoryChanged;
        GameEvents.ItemConsumed -= OnItemConsumed;
        GameEvents.Notify -= OnNotify;
        GameEvents.PlayerDied -= OnPlayerDied;
        GameEvents.TimeOfDayChanged -= OnTimeOfDayChanged;
        GameEvents.SelectionChanged -= OnSelectionChanged;
        GameEvents.InventoryOpenChanged -= OnInventoryOpenChanged;
        GameEvents.PauseChanged -= OnPauseChanged;
        GameEvents.StatsChanged -= OnStatsChanged;
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
        moanClip = ProceduralSfx.MakeZombieMoan();
        heartbeatClip = ProceduralSfx.MakeHeartbeat();
        nightCryClip = ProceduralSfx.MakeNightCry();
        squelchClip = ProceduralSfx.MakeSquelch();
    }

    void BuildSources()
    {
        uiSource = gameObject.AddComponent<AudioSource>();
        uiSource.spatialBlend = 0f;
        uiSource.playOnAwake = false;

        // Отдельный источник для длинных стингов: pitch жёстко 1 и не
        // меняется никогда, иначе UI-блипы «катают» тон играющего стинга.
        worldFlatSource = gameObject.AddComponent<AudioSource>();
        worldFlatSource.spatialBlend = 0f;
        worldFlatSource.playOnAwake = false;
        worldFlatSource.pitch = 1f;

        stepSource = gameObject.AddComponent<AudioSource>();
        stepSource.spatialBlend = 0f;
        stepSource.playOnAwake = false;

        windSource = gameObject.AddComponent<AudioSource>();
        windSource.clip = windClip;
        windSource.loop = true;
        windSource.spatialBlend = 0f;
        windSource.playOnAwake = false; // двойного запуска нет: стартуем явно
        windSource.volume = Db(windDayDb);
        windSource.Play();

        zombieSource = gameObject.AddComponent<AudioSource>();
        zombieSource.clip = moanClip;
        zombieSource.loop = true;
        zombieSource.spatialBlend = 0f;
        zombieSource.playOnAwake = false;
        zombieSource.volume = 0f;

        heartbeatSource = gameObject.AddComponent<AudioSource>();
        heartbeatSource.clip = heartbeatClip;
        heartbeatSource.loop = true;
        heartbeatSource.spatialBlend = 0f;
        heartbeatSource.playOnAwake = false;
        heartbeatSource.volume = 0f;

        crySource = gameObject.AddComponent<AudioSource>();
        crySource.spatialBlend = 0f;
        crySource.playOnAwake = false;

        squelchSource = gameObject.AddComponent<AudioSource>(); // плеск утечки (R2)
        squelchSource.clip = squelchClip;
        squelchSource.loop = true;
        squelchSource.spatialBlend = 0f;
        squelchSource.playOnAwake = false;
        squelchSource.volume = 0f;
        squelchSource.Play();
    }

    // ---------- Update-циклы ----------

    void Update()
    {
        float dt = Time.deltaTime;
        FindPlayer();
        TickWind(dt);
        TickFootsteps(dt);
        TickGurgle();
        TickFireScan();
        TickZombieScan();
        TickMoan(dt);
        TickHeartbeat(dt);
        TickNightCry();
        TickLeak(dt);
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
    // Ведро плещет при движении: ходьба — тихо, бег — отчётливо (R2/§9.2).
    void TickLeak(float dt)
    {
        if (player == null || cc == null || squelchSource == null) return;
        Vector3 v = cc.velocity; v.y = 0f;
        float speed = v.magnitude;
        float target = 0f;
        if (!player.Stats.IsDead && speed >= minStepSpeed && cc.isGrounded)
        {
            bool sprinting = speed > player.walkSpeed * 1.15f;
            target = Db(sprinting ? leakSprintDb : leakWalkDb)
                     * Mathf.Clamp01(speed / player.walkSpeed);
        }
        squelchSource.volume = Mathf.MoveTowards(squelchSource.volume, target, dt * 0.5f);
    }

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

    // Раз в zombieScanInterval секунд ищем живых зомби (мёртвые и дневные
    // уничтожаются — см. Zombie.cs, так что любой найденный = живой) и
    // задаём громкость гула по ближайшему. Стая не громче одиночки.
    void TickZombieScan()
    {
        if (Time.time < nextZombieScan) return;
        nextZombieScan = Time.time + zombieScanInterval * Random.Range(0.9f, 1.1f);

        moanTarget = 0f;
        if (player == null || player.Stats.IsDead) return;

        float nearest = float.MaxValue;
        foreach (var z in FindObjectsByType<Zombie>(FindObjectsSortMode.None))
        {
            if (z == null) continue;
            float d = Vector3.Distance(z.transform.position, player.transform.position);
            if (d < nearest) nearest = d;
        }
        if (nearest >= zombieFarDist) return;
        moanTarget = Db(zombieMoanDb) * Mathf.SmoothStep(0f, 1f,
            Mathf.InverseLerp(zombieFarDist, zombieNearDist, nearest));
    }

    // Петля гула плывёт к цели; пока звучит — тон слегка дрожит.
    void TickMoan(float dt)
    {
        zombieSource.volume = Mathf.MoveTowards(zombieSource.volume, moanTarget, dt * 0.25f);
        if (moanTarget > 0f && !zombieSource.isPlaying) zombieSource.Play();
        if (!zombieSource.isPlaying) return;
        if (moanTarget <= 0f && zombieSource.volume <= 0.001f)
        {
            zombieSource.Stop();
            zombieSource.pitch = 1f;
            return;
        }
        zombieSource.pitch = 1f
            + 0.035f * Mathf.Sin(Time.time * 2.3f)
            + 0.02f * Mathf.Sin(Time.time * 0.71f + 1.7f);
    }

    // Сердце: цель задаёт OnStatsChanged, здесь — плавный вход/выход.
    void TickHeartbeat(float dt)
    {
        heartbeatSource.volume = Mathf.MoveTowards(heartbeatSource.volume, heartbeatTarget, dt * 0.5f);
        if (heartbeatTarget > 0f && !heartbeatSource.isPlaying) heartbeatSource.Play();
        else if (heartbeatTarget <= 0f && heartbeatSource.volume <= 0.001f && heartbeatSource.isPlaying)
            heartbeatSource.Stop();
    }

    // Редкие далёкие вскрики/чавканье ночью (§9.6): тихо, со случайным
    // stereo-разносом — источник не локализуется, тем и жутко.
    void TickNightCry()
    {
        if (Time.time < nextNightCryTime) return;
        nextNightCryTime = Time.time + Random.Range(nightCryMinGap, nightCryMaxGap);
        if (!isNight || player == null || player.Stats.IsDead) return;

        crySource.panStereo = Random.Range(-0.85f, 0.85f);
        crySource.pitch = Random.Range(0.85f, 1.2f);
        crySource.PlayOneShot(nightCryClip, Db(nightCryDb));
    }

    // ---------- обработчики GameEvents ----------

    void OnInventoryChanged()
    {
        if (Time.time - lastPickupSfx < 0.08f) return; // не дробить пачку событий в кадре
        lastPickupSfx = Time.time;
        uiSource.pitch = Random.Range(0.97f, 1.04f);
        uiSource.PlayOneShot(pickupClip, Db(pickupDb));
    }

    // Звук еды/питья. Событие поднимает Inventory.UseSelected; item может
    // быть null — звук всё равно играем в базовом варианте.
    void OnItemConsumed(ItemData item)
    {
        if (uiSource == null || eatClip == null) return;
        float db = eatDb;
        float pitch = Random.Range(0.94f, 1.06f);
        if (item != null)
        {
            if (item.waterRestore > 0f && item.foodRestore <= 0f)
            {
                pitch += 0.15f; // питьё — глоток: выше и чуть тише жевания
                db -= 2f;
            }
            if (item.poisonAmount > 0f)
            {
                pitch -= 0.08f; // ядовитое (§9.3) звучит «мутнее»
                db += 1f;
            }
        }
        uiSource.pitch = pitch;
        uiSource.PlayOneShot(eatClip, Db(db));
    }

    // Подписки WorldHit здесь намеренно нет — см. OnEnable.

    void OnNotify(string text)
    {
        if (Time.time - lastToastSfx < 0.12f) return;
        lastToastSfx = Time.time;
        uiSource.pitch = 1f;
        uiSource.PlayOneShot(toastClip, Db(toastDb));
    }

    void OnPlayerDied()
    {
        heartbeatTarget = 0f; // сердцу всё
        moanTarget = 0f;
        // Длинный стинг — на отдельном источнике со строгим pitch=1:
        // не делить с короткими блипами, которые катают pitch.
        worldFlatSource.PlayOneShot(deathClip, Db(deathDb));
    }

    void OnTimeOfDayChanged(float t, bool night) => isNight = night;

    // StatsChanged стреляет каждый кадр (Stats.Tick) — обработчик только
    // пересчитывает целевую громкость сердца, ничего не проигрывая.
    void OnStatsChanged()
    {
        if (player == null || player.Stats == null) return;
        float hp = player.Stats.Mayo; // сердце = ведро пустеет
        if (player.Stats.IsDead || hp >= heartbeatHp)
        {
            heartbeatTarget = 0f;
            return;
        }
        float severity = 1f - hp / heartbeatHp; // 0 у порога, 1 у нуля
        heartbeatTarget = Db(Mathf.Lerp(heartbeatMinDb, heartbeatMaxDb, severity));
    }

    // Короткий «тик» при смене слота (колёсико может дать серию за кадр —
    // режем дросселем, иначе звук складывается в пулемётную очередь).
    void OnSelectionChanged()
    {
        if (Time.time - lastSelectSfx < 0.05f) return;
        lastSelectSfx = Time.time;
        uiSource.pitch = Random.Range(0.96f, 1.06f);
        uiSource.PlayOneShot(selectClip, Db(selectDb));
    }

    void OnInventoryOpenChanged(bool open)
    {
        uiSource.pitch = Random.Range(0.96f, 1.05f);
        uiSource.PlayOneShot(open ? openClip : closeClip, Db(rustleDb));
    }

    void OnPauseChanged(bool paused)
    {
        uiSource.pitch = 1f;
        uiSource.PlayOneShot(pauseClip, Db(pauseDb));
    }

    // ---------- проигрывание ----------

    // Позиционный одношот: временный AudioSource в мире, самоуничтожается.
    void PlayAt(AudioClip clip, Vector3 pos, float db, float pitch, float maxDist = 32f)
    {
        if (clip == null) return; // дешёвая страховка от NRE (clip.name ниже)
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
        uiSource.pitch = Random.Range(0.94f, 1.06f);
        uiSource.PlayOneShot(whooshClip, Db(swingDb));
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
