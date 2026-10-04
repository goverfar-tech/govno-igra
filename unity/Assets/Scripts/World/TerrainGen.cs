using UnityEngine;

// Процедурный рельеф по шуму Перлина (аналог terrain.gd из Godot-версии, M4).
// Одна функция HeightAt используется и для меша, и для расстановки объектов.
// Мир — круглый остров в океане протухшего майонеза: холмы в центре,
// к краю пляж-отмель почти вровень с водой, за ним ровное дно, на
// северо-западе — гора с вырезанной пещерой босса (S/мир 2026-10).
public static class TerrainGen
{
    public const float Size = 1000f;     // карта 1000×1000 м
    public const float Amp = 5f;         // высота холмов
    public const float Freq = 0.02f;     // частота (пятна ~50 м)

    // Остров и океан. Константы и IsInOcean используют чужие скрипты
    // (Player/ZombieSpawner/Setup) — сигнатуры и имена не менять.
    public const float IslandRadius = 420f; // радиус суши до начала отмели
    public const float SeaLevel = 0f;       // уровень океана
    public const float TownRadius = 60f;    // терраса городка «Гнилой Причал»
    public const float TownHeight = 2.0f;   // плато городка — чуть выше берега
    public static readonly Vector2 TownCenter = new Vector2(240f, -160f); // r≈288, юго-восток

    // Гора с пещерой босса (S/пещера 2026-10-03), северо-запад острова.
    // Константы читают Setup (свод/стены каньона) и параллельные скрипты —
    // имена и значения не менять. Центр на r≈238 от центра острова:
    // подножие 238+95=333 — ровно до начала полосы отмели (IslandRadius-90
    // = 330); маска бугра на кромке уже ~0.003, а берег — последний слой
    // HeightAt, так что пляж гора не ломает (проверено в комментарии слоя).
    public static readonly Vector2 MountainCenter = new Vector2(-150f, 185f);
    public const float MountainHeight = 30f;
    public const float MountainRadius = 95f;
    public const float CaveFloor = 5f;
    // направление входа в пещеру: от вершины к центру острова — вход с
    // юго-восточного склона; тот же вектор использует Setup для стен каньона
    public static readonly Vector2 CaveInDir = (Vector2.zero - MountainCenter).normalized;

    public const int ChunkCount = 8;     // сетка 8×8 = 64 чанка по 125 м

    // Озеро: котловина в рельефе + WaterSource ставится на его центр
    public static readonly Vector2 LakeCenter = new Vector2(55f, 55f);
    public const float LakeRadius = 12f;
    public const float LakeDepth = 3f;

    static readonly Vector2 off1 = new Vector2(13.7f, 7.1f);
    static readonly Vector2 off2 = new Vector2(91.3f, 44.9f);
    // Крупные октавы холмистости (усиление по фидбеку автора 2026-10-03):
    // гряды с периодом ~250 м (до +7 м) и средние бугры. Берег защищён
    // последним слоем HeightAt — пляж и дно перекрывают их как и раньше.
    static readonly Vector2 off3 = new Vector2(41.2f, 17.9f);
    static readonly Vector2 off4 = new Vector2(7.7f, 83.1f);

    // Точка в океане: за отмелью и ниже уровня воды (запас 0.35 — на кромку
    // прибоя). Зовут чужие скрипты — не менять сигнатуру.
    public static bool IsInOcean(Vector3 p)
        => new Vector2(p.x, p.z).magnitude > IslandRadius + 8f && p.y < SeaLevel + 0.35f;

    // --- зоны §9.4 (X5): ось прогресса «дальше от городка — опаснее/богаче» ---
    // Пороги по расстоянию от TownCenter (городок = цивилизация, от неё
    // читается обратная кривая сложности): раннюю игру давит голод у
    // безопасного кольца, позднюю — враги. Герметики и лут богаче во
    // внешних зонах — раскладку делают спавнеры/лут-таблицы, здесь только
    // геометрия зон. (Завод 2026-10-04 снесён — станет отдельной локацией
    // с системой переходов; зоны остаются прогрессией лута.)
    public const float Zone0Radius = 130f; // <130 — «Окраина», кольцо вокруг городка
    public const float Zone1Radius = 230f; // <230 — «Леса»
    public const float Zone2Radius = 320f; // <320 — «Гнилые поля», дальше — «Глухомань»

    // Зона точки 0..3 по удалённости от городка. Зовут из спавнеров и HUD
    // на горячих путях — без аллокаций, одна дистанция.
    public static int ZoneAt(Vector2 p)
    {
        float d = Vector2.Distance(p, TownCenter);
        if (d < Zone0Radius) return 0;
        if (d < Zone1Radius) return 1;
        if (d < Zone2Radius) return 2;
        return 3;
    }

    // Имя зоны для HUD/тостов; индексы согласованы с ZoneAt.
    public static string ZoneName(int zone)
    {
        switch (zone)
        {
            case 0: return "Окраина";
            case 1: return "Леса";
            case 2: return "Гнилые поля";
            default: return "Глухомань";
        }
    }

    public static float HeightAt(float x, float z)
    {
        float h = Mathf.PerlinNoise(x * Freq + off1.x, z * Freq + off1.y) * Amp
                + Mathf.PerlinNoise(x * Freq * 3f + off2.x, z * Freq * 3f + off2.y) * Amp * 0.25f
                // усиление холмистости по фидбеку автора (2026-10-03): две
                // крупные октавы — гряды ~250 м и средние бугры ~70 м.
                // Берег защищён последним слоем, плато/город перекроют холмы
                + Mathf.PerlinNoise(x * Freq * 0.25f + off3.x, z * Freq * 0.25f + off3.y) * Amp * 1.4f
                + Mathf.PerlinNoise(x * Freq * 0.7f + off4.x, z * Freq * 0.7f + off4.y) * Amp * 0.45f;

        // плато у спауна (центр): ровное место под базу
        float dSpawn = new Vector2(x, z).magnitude;
        h *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(8f, 30f, dSpawn));

        // котловина озера
        float dLake = Vector2.Distance(new Vector2(x, z), LakeCenter);
        float rim = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(LakeRadius * 0.5f, LakeRadius, dLake));
        h -= LakeDepth * (1f - rim);

        // гора с пещерой (S/пещера 2026-10-03): колокол 30 м радиусом 95 на
        // северо-западе. Слой ПОСЛЕ всех холмов и ДО городской террасы и
        // берега (порядок: гора → терраса → берег-последний), поэтому город
        // и пляж её перекрывают, как перекрывали холмы.
        float dMountain = Vector2.Distance(new Vector2(x, z), MountainCenter);
        float mtT = dMountain / MountainRadius;
        float mountainMask = 1f - Smooth01(mtT); // колокол: 1 в центре, 0 на подножии
        // вершина слегка приплюснута — не остриё: на t<0.15 маска прижата к 0.85
        if (mtT < 0.15f) mountainMask = 0.85f;
        h += MountainHeight * mountainMask;

        // вырез пещеры поверх бугра (тот же слой): зал — круг r=16 у вершины,
        // каньон-вход — полоса шириной 9 м (|перп| ≤ 4.5) вдоль луча от
        // вершины к центру острова, от 8 м до MountainRadius+6 от центра
        // горы. Heightmap потолок не умеет — свод ставится примитивом в
        // Setup, рельеф лишь вырезает вход и зал; пол слегка неровный.
        Vector2 fromMt = new Vector2(x, z) - MountainCenter;
        float alongIn = Vector2.Dot(fromMt, CaveInDir);
        float sideIn = Vector2.Dot(fromMt, new Vector2(-CaveInDir.y, CaveInDir.x));
        bool inHall = fromMt.sqrMagnitude <= 16f * 16f;
        bool inCanyon = alongIn >= 8f && alongIn <= MountainRadius + 6f
                        && Mathf.Abs(sideIn) <= 4.5f;
        if (inHall || inCanyon)
            h = Mathf.Min(h, CaveFloor + Mathf.PerlinNoise(x * 0.15f, z * 0.15f) * 0.6f);

        // городская терраса: внутри 0.6*TownRadius — ровно TownHeight,
        // к 1.35*TownRadius плавный спуск к естественному рельефу
        float dTown = Vector2.Distance(new Vector2(x, z), TownCenter);
        float townBlend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(TownRadius * 1.35f, TownRadius * 0.6f, dTown));
        h = Mathf.Lerp(h, TownHeight, townBlend);

        // берег и дно — ПОСЛЕДНИЙ слой, перекрывает всё предыдущее.
        // Полоса отмели IslandRadius-90 .. IslandRadius+30: первые 40% —
        // пляж, прижатый почти вровень с океаном (0.6 м), дальше уход
        // под воду до ровного дна -4.5.
        float r = new Vector2(x, z).magnitude;
        if (r >= IslandRadius + 30f)
            h = -4.5f; // ровное глубокое дно, без перлина: иначе меш за
                       // пределами воды торчал бы из майонеза островками
        else
        {
            float t = Mathf.InverseLerp(IslandRadius - 90f, IslandRadius + 30f, r);
            if (t <= 0.4f)
                h = Mathf.Lerp(h, 0.6f, Smooth01(t / 0.4f));
            else
                h = Mathf.Lerp(0.6f, -4.5f, Smooth01((t - 0.4f) / 0.6f));
        }
        return h;
    }

    // clamp+smoothstep по доле 0..1 — сглаживание полос берега
    static float Smooth01(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    // Строит один чанк сетки ChunkCount×ChunkCount (cx, cz ∈ [0, ChunkCount)).
    // Вершины в локальных координатах, центр чанка в (0,0) — GameObject
    // ставится в мир по формуле -Size/2 + (c + 0.5) * (Size/ChunkCount).
    // Меш передаётся снаружи — ассет чанка перестраивается на месте,
    // ссылки в сцене не рвутся. resPerChunk=84 даёт ячейку ~1.5 м.
    public static void FillChunk(Mesh mesh, int cx, int cz, int resPerChunk)
    {
        mesh.Clear();
        int side = resPerChunk + 1;
        var verts = new Vector3[side * side];
        var uvs = new Vector2[side * side];
        float chunk = Size / ChunkCount;
        float x0 = -Size / 2f + cx * chunk;
        float z0 = -Size / 2f + cz * chunk;
        float step = chunk / resPerChunk;

        for (int z = 0; z < side; z++)
        for (int x = 0; x < side; x++)
        {
            float wx = x0 + x * step;
            float wz = z0 + z * step;
            // локальные координаты: центр чанка в нуле
            verts[z * side + x] = new Vector3(wx - x0 - chunk * 0.5f, HeightAt(wx, wz),
                                              wz - z0 - chunk * 0.5f);
            uvs[z * side + x] = new Vector2((float)x / resPerChunk, (float)z / resPerChunk);
        }

        var tris = new int[resPerChunk * resPerChunk * 6];
        int t = 0;
        for (int z = 0; z < resPerChunk; z++)
        for (int x = 0; x < resPerChunk; x++)
        {
            int i0 = z * side + x, i1 = i0 + 1, i2 = i0 + side, i3 = i2 + 1;
            tris[t++] = i0; tris[t++] = i2; tris[t++] = i1;
            tris[t++] = i1; tris[t++] = i2; tris[t++] = i3;
        }

        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
    }
}
