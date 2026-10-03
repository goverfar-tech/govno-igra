using UnityEngine;
using UnityEngine.UI;

// Миникарта с туманом войны в духе HoMM3. Всё строится кодом (как весь
// HUD): панель в правом верхнем углу канваса, RawImage 230×230 с картой
// острова, поверх — маркеры (игрок/лужа/городок/костёр).
//
// Туман — сетка 5 м: 200×200 клеток на мир −500..+500. Три состояния
// клетки: никогда не видена → почти чёрный; открыта ранее, вне текущего
// обзора → base×0.5; в текущем радиусе обзора (25 м) → base целиком.
// Каждые 0.25 с: Reveal (открыть клетки вокруг игрока) + полная
// перерисовка (SetPixels32 на 256×256 ≈ 65 тыс. пикселей — дёшево).
//
// Карта-подложка строится ОДИН раз в Init из TerrainGen.HeightAt —
// рельеф детерминирован, карта всегда одна и та же.
//
// Состояние тумана отдаётся SaveSystem'у статикой (EncodeFog/DecodeFog).
// Сам Minimap — ребёнок канваса Hud и переживает перезагрузку сцены
// вместе с ним (DontDestroyOnLoad): мир не пересоздаётся, открытое
// сохраняется сейвом — принятое в проекте поведение.
public class Minimap : MonoBehaviour
{
    // --- туман: сетка 5 м на мир −500..+500 ---
    public const float WorldMin = -500f;
    public const float WorldMax = 500f;
    public const float CellSize = 5f;
    public const int CellsPerSide = 200;                 // 1000 м / 5 м
    public const int CellCount = CellsPerSide * CellsPerSide; // 40000

    // Радиус обзора вокруг игрока: одновременно «открывает» туман
    // и подсвечивает клетки целиком при отрисовке.
    public const float ViewRadius = 25f;

    const float TickInterval = 0.25f; // период открытия/перерисовки тумана
    const int TexSize = 256;          // карта-подложка 256×256
    const float MapSide = 230f;       // видимый размер карты в UI-единицах

    // Почти чёрный — клетки, никогда не виденные (0.05, 0.05, 0.07)
    static readonly Color32 HiddenColor = new Color32(13, 13, 18, 255);

    static Minimap instance;
    // Туман, декодированный до построения карты (порядок старта сцены не
    // гарантирован) — применяется в Init.
    static string pendingFog;

    // Статика не должна переживать запуск сессии при выключенном domain
    // reload — иначе EncodeFog ссылался бы на мёртвый объект прошлой игры.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        instance = null;
        pendingFog = null;
    }

    readonly bool[] explored = new bool[CellCount];
    Texture2D mapTexture;
    Color32[] baseColors;    // цвет местности без тумана, по пикселям
    Color32[] paintBuffer;   // переиспользуемый буфер SetPixels32
    float[] worldX, worldZ;  // мировая координата каждого столбца/строки
    RectTransform mapRect;
    Image playerMarker, lakeMarker, townMarker, fireMarker;

    Player player;           // кэш; игрок пересоздаётся на рестарте сцены
    float tickTimer;

    void OnDestroy()
    {
        if (instance == this) instance = null;
        // текстура карты создана кодом — без Destroy висела бы до GC
        if (mapTexture != null)
        {
            Destroy(mapTexture);
            mapTexture = null;
        }
    }

    // ---------- построение ----------

    // Строит панель в правом верхнем углу канваса: тёмная полупрозрачная
    // рамка в стиле панелей Hud, RawImage 230×230, маркеры детьми карты.
    // Игрока не ищет (лениво, в тике) — гонки Awake не создаёт.
    public void Init(RectTransform parent)
    {
        if (!(transform is RectTransform)) gameObject.AddComponent<RectTransform>();
        transform.SetParent(parent, false);
        instance = this;

        var rt = (RectTransform)transform;
        // правый верхний угол, отступ 12 — симметрично панели статов слева-снизу
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-12f, -12f);
        rt.sizeDelta = new Vector2(MapSide + 8f, MapSide + 8f); // рамка вокруг карты

        var frame = GetComponent<Image>();
        if (frame == null) frame = gameObject.AddComponent<Image>();
        frame.color = UiWidgets.PanelColor; // тот же тёмный полупрозрачный, что весь HUD
        frame.raycastTarget = false;

        var mapGo = new GameObject("Map", typeof(RectTransform), typeof(RawImage));
        mapGo.transform.SetParent(rt, false);
        mapRect = (RectTransform)mapGo.transform;
        mapRect.anchorMin = mapRect.anchorMax = new Vector2(0.5f, 0.5f);
        mapRect.sizeDelta = new Vector2(MapSide, MapSide);
        var mapImage = mapGo.GetComponent<RawImage>();
        mapImage.raycastTarget = false;

        BuildBaseMap(mapImage);

        // Маркеры: формы генерируются кодом (белые маски), цвет — Image.color.
        // Игрок последним — его стрелка должна лежать ПОВЕРХ точек интереса.
        lakeMarker = MakeMarker("LakeMarker", MakeDotTexture(9),
            new Color(0.93f, 0.88f, 0.62f), 9f);  // лужа — кремовая точка
        townMarker = MakeMarker("TownMarker", MakeDotTexture(9),
            new Color(0.62f, 0.48f, 0.30f), 11f); // городок — коричневая точка
        fireMarker = MakeMarker("FireMarker", MakeDotTexture(9),
            new Color(0.95f, 0.55f, 0.15f), 9f);  // костёр — оранжевая точка
        playerMarker = MakeMarker("PlayerMarker", MakeArrowTexture(16),
            new Color(0.93f, 0.90f, 0.78f), 15f); // игрок — кремовая стрелка
        lakeMarker.rectTransform.anchoredPosition =
            WorldToMap(TerrainGen.LakeCenter.x, TerrainGen.LakeCenter.y);
        townMarker.rectTransform.anchoredPosition =
            WorldToMap(TerrainGen.TownCenter.x, TerrainGen.TownCenter.y);

        // отложенный туман из сейва (DecodeFog мог прийти раньше Init)
        if (pendingFog != null) { ApplyFog(pendingFog); pendingFog = null; }

        Reveal();
        Repaint();
        SyncMarkerVisibility();
    }

    // Карта-подложка: один проход HeightAt на пиксель (256×256 вызовов —
    // один раз при старте). Ориентация: py=0 — НИЗ текстуры (и Texture2D,
    // и UV RawImage считают снизу) = z=−500, py=255 = z=+500 — север сверху.
    void BuildBaseMap(RawImage mapImage)
    {
        worldX = new float[TexSize];
        worldZ = new float[TexSize];
        for (int i = 0; i < TexSize; i++)
        {
            worldX[i] = WorldMin + (WorldMax - WorldMin) * i / (TexSize - 1);
            worldZ[i] = worldX[i];
        }
        baseColors = new Color32[TexSize * TexSize];
        for (int py = 0; py < TexSize; py++)
            for (int px = 0; px < TexSize; px++)
            {
                float wx = worldX[px], wz = worldZ[py];
                float h = TerrainGen.HeightAt(wx, wz);
                Color c;
                // Лужа проверяется ПЕРВОЙ и геометрически: дно её котловины
                // уходит ниже SeaLevel — по высоте она неотличима от океана.
                if (Vector2.Distance(new Vector2(wx, wz), TerrainGen.LakeCenter) < TerrainGen.LakeRadius)
                    c = new Color(0.93f, 0.88f, 0.62f);      // лужа
                else if (h < TerrainGen.SeaLevel)
                    c = new Color(0.36f, 0.34f, 0.19f);      // океан протухшего майонеза
                else if (h < 0.8f)
                    c = new Color(0.74f, 0.68f, 0.50f);      // пляж-отмель
                else if (Vector2.Distance(new Vector2(wx, wz), TerrainGen.TownCenter) < TerrainGen.TownRadius)
                    c = new Color(0.45f, 0.41f, 0.33f);      // терраса городка
                else
                {
                    float t = Mathf.Clamp01(h / 10f);
                    c = Color.Lerp(new Color(0.30f, 0.36f, 0.22f), // суша:
                                   new Color(0.52f, 0.53f, 0.37f), t); // темней в низинах
                }
                baseColors[py * TexSize + px] = c;
            }

        mapTexture = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, false);
        mapTexture.filterMode = FilterMode.Point; // пиксельная карта — HoMM3-стиль
        mapTexture.wrapMode = TextureWrapMode.Clamp;
        mapImage.texture = mapTexture;
        paintBuffer = new Color32[TexSize * TexSize];
    }

    // ---------- тик ----------

    void Update()
    {
        tickTimer -= Time.deltaTime;
        if (tickTimer <= 0f)
        {
            tickTimer = TickInterval;
            // игрок пересоздаётся на рестарте сцены — кэш с перепроверкой
            if (player == null) player = FindFirstObjectByType<Player>();
            Reveal();
            Repaint();
            SyncMarkerVisibility();
        }
        // стрелка — каждый кадр: поворот с периодом 0.25 с читается дёргано
        UpdatePlayerMarker();
    }

    // Открыть туман в радиусе обзора вокруг игрока.
    void Reveal()
    {
        if (player == null) return;
        var p = new Vector2(player.transform.position.x, player.transform.position.z);
        int cx = CellIndex(p.x), cz = CellIndex(p.y);
        int r = Mathf.CeilToInt(ViewRadius / CellSize); // 25 м / 5 м = 5 клеток
        float r2 = ViewRadius * ViewRadius;
        for (int z = cz - r; z <= cz + r; z++)
        {
            if (z < 0 || z >= CellsPerSide) continue;
            for (int x = cx - r; x <= cx + r; x++)
            {
                if (x < 0 || x >= CellsPerSide) continue;
                var center = new Vector2(WorldMin + (x + 0.5f) * CellSize,
                                         WorldMin + (z + 0.5f) * CellSize);
                if ((center - p).sqrMagnitude <= r2)
                    explored[z * CellsPerSide + x] = true;
            }
        }
    }

    // Полная перерисовка 256×256 раз в 0.25 с: три состояния клетки.
    void Repaint()
    {
        if (mapTexture == null) return;
        bool hasView = player != null;
        var view = hasView
            ? new Vector2(player.transform.position.x, player.transform.position.z)
            : Vector2.zero;

        for (int py = 0; py < TexSize; py++)
        {
            float wz = worldZ[py];
            int row = py * TexSize;
            for (int px = 0; px < TexSize; px++)
            {
                float dx = worldX[px] - view.x, dz = wz - view.y;
                Color32 c;
                if (hasView && dx * dx + dz * dz <= ViewRadius * ViewRadius)
                    c = baseColors[row + px];                 // в обзоре — целиком
                else if (ExploredAt(worldX[px], wz))
                    c = Dim(baseColors[row + px]);            // открыто ранее — вполсилы
                else
                    c = HiddenColor;                          // никогда не видено
                paintBuffer[row + px] = c;
            }
        }
        mapTexture.SetPixels32(paintBuffer);
        mapTexture.Apply(false);
    }

    // base×0.5 — byte-деление вместо float, точность тут не нужна
    static Color32 Dim(Color32 c)
        => new Color32((byte)(c.r >> 1), (byte)(c.g >> 1), (byte)(c.b >> 1), c.a);

    bool ExploredAt(float wx, float wz)
        => explored[CellIndex(wz) * CellsPerSide + CellIndex(wx)];

    static int CellIndex(float worldCoord)
        => Mathf.Clamp(Mathf.FloorToInt((worldCoord - WorldMin) / CellSize), 0, CellsPerSide - 1);

    // ---------- маркеры ----------

    // Мир → локальные координаты внутри RawImage (центр карты = 0,0,
    // север сверху). Ось согласована с текстурой: z=−500 — низ карты.
    Vector2 WorldToMap(float x, float z)
    {
        float u = (x - WorldMin) / (WorldMax - WorldMin); // 0..1 слева направо
        float v = (z - WorldMin) / (WorldMax - WorldMin); // 0..1 снизу вверх
        return new Vector2((u - 0.5f) * MapSide, (v - 0.5f) * MapSide);
    }

    Image MakeMarker(string name, Texture2D shape, Color color, float size)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(mapRect, false);
        var img = go.GetComponent<Image>();
        img.sprite = Sprite.Create(shape, new Rect(0f, 0f, shape.width, shape.height),
            new Vector2(0.5f, 0.5f)); // вращение вокруг центра формы
        img.color = color;
        img.raycastTarget = false;
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = new Vector2(size, size);
        return img;
    }

    // Позиция и поворот стрелки игрока — каждый кадр.
    void UpdatePlayerMarker()
    {
        if (playerMarker == null) return;
        if (player == null) { playerMarker.gameObject.SetActive(false); return; }
        if (!playerMarker.gameObject.activeSelf) playerMarker.gameObject.SetActive(true);
        playerMarker.rectTransform.anchoredPosition =
            WorldToMap(player.transform.position.x, player.transform.position.z);
        // ЗНАКИ ПОВОРОТА: рыскание игрока растёт ПРОТИВ часовой стрелки
        // (вид сверху, правая система координат: yaw=90° ⇒ вперёд = +X = восток).
        // На карте восток — вправо, север — вверх, значит «вперёд» игрока
        // смотрит от севера ПО часовой на угол yaw (направление (sin yaw, cos yaw)).
        // Положительный поворот RectTransform в uGUI крутит ПРОТИВ часовой,
        // поэтому экрану нужен минус: rotation = −yaw (yaw=90 → стрелка вправо).
        playerMarker.rectTransform.localEulerAngles =
            new Vector3(0f, 0f, -player.transform.eulerAngles.y);
    }

    // Точки интереса видны ТОЛЬКО если их клетка тумана открыта (HoMM3:
    // POI проявляются вместе с исследованной местностью).
    void SyncMarkerVisibility()
    {
        if (lakeMarker == null) return;
        lakeMarker.gameObject.SetActive(
            ExploredAt(TerrainGen.LakeCenter.x, TerrainGen.LakeCenter.y));
        townMarker.gameObject.SetActive(
            ExploredAt(TerrainGen.TownCenter.x, TerrainGen.TownCenter.y));
        bool fire = Player.hasCampfireSpawn
                    && ExploredAt(Player.lastCampfirePos.x, Player.lastCampfirePos.z);
        fireMarker.gameObject.SetActive(fire);
        if (fire) // костёр переставляется — точку обновляем вместе с видимостью
            fireMarker.rectTransform.anchoredPosition =
                WorldToMap(Player.lastCampfirePos.x, Player.lastCampfirePos.z);
    }

    // ---------- сейв (SaveSystem) ----------

    // Туман → base64-битмаска: бит i (клетка cz*200+cx, младший бит первого
    // байта) = 1, если клетка открыта. 40000 бит → 5000 байт → ~6.7 КБ строки.
    public static string EncodeFog()
    {
        if (instance == null) return "";
        var bytes = new byte[(CellCount + 7) / 8];
        var ex = instance.explored;
        for (int i = 0; i < CellCount; i++)
            if (ex[i]) bytes[i >> 3] |= (byte)(1 << (i & 7));
        return System.Convert.ToBase64String(bytes);
    }

    // Обратное преобразование. null/пусто (старый сейв v2 без этого поля —
    // JsonUtility молча подставит null) = ВСЁ ЗАКРЫТО: обратная совместимость
    // сознательная, формат/version не менялись. Битая маска = туман закрыт,
    // молча: карта не повод для тостов.
    public static void DecodeFog(string data)
    {
        // Карта могла ещё не построиться (порядок старта сцены не
        // гарантирован) — откладываем до Init.
        if (instance == null) { pendingFog = data; return; }
        instance.ApplyFog(data);
    }

    void ApplyFog(string data)
    {
        for (int i = 0; i < explored.Length; i++) explored[i] = false;
        if (string.IsNullOrEmpty(data)) return;
        byte[] bytes;
        try { bytes = System.Convert.FromBase64String(data); }
        catch (System.FormatException) { return; }
        for (int i = 0; i < CellCount && (i >> 3) < bytes.Length; i++)
            explored[i] = (bytes[i >> 3] & (1 << (i & 7))) != 0;
    }

    // ---------- генерация форм маркеров (белые маски, цвет — Image.color) ----------

    static Texture2D MakeDotTexture(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float half = size * 0.5f, c = half - 0.5f;
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - c, dy = y - c;
                // мягкий край: альфа гаснет к границе круга
                pixels[y * size + x] = Color.white * Mathf.Clamp01(half - Mathf.Sqrt(dx * dx + dy * dy));
            }
        tex.SetPixels(pixels);
        tex.Apply(false); // не makeNoLongerReadable: Sprite.Create читает текстуру
        return tex;
    }

    static Texture2D MakeArrowTexture(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var pixels = new Color[size * size];
        // Треугольник остриём ВВЕРХ (север): вершины в пикселях, y — снизу
        // вверх, как у Texture2D. Доворачивает его сам маркер (см. знаки
        // в UpdatePlayerMarker).
        var tip = new Vector2(size * 0.5f, size - 1.5f);
        var left = new Vector2(1.5f, 1f);
        var right = new Vector2(size - 1.5f, 1f);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                // обход вершин против часовой (в y-up) — внутри все знаки ≥ 0
                bool inside = Cross(tip, left, p) >= 0
                           && Cross(left, right, p) >= 0
                           && Cross(right, tip, p) >= 0;
                pixels[y * size + x] = inside ? Color.white : Color.clear;
            }
        tex.SetPixels(pixels);
        tex.Apply(false);
        return tex;
    }

    // Знак удвоенной площади (b−a)×(p−a): >0 — точка p слева от ребра a→b
    static float Cross(Vector2 a, Vector2 b, Vector2 p)
        => (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);
}
