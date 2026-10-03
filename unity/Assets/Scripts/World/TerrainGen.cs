using UnityEngine;

// Процедурный рельеф по шуму Перлина (аналог terrain.gd из Godot-версии, M4).
// Одна функция HeightAt используется и для меша, и для расстановки объектов.
// Мир — круглый остров в океане протухшего майонеза: холмы в центре,
// к краю пляж-отмель почти вровень с водой, за ним ровное дно (S/мир 2026-10).
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

    public const int ChunkCount = 8;     // сетка 8×8 = 64 чанка по 125 м

    // Озеро: котловина в рельефе + WaterSource ставится на его центр
    public static readonly Vector2 LakeCenter = new Vector2(55f, 55f);
    public const float LakeRadius = 12f;
    public const float LakeDepth = 3f;

    static readonly Vector2 off1 = new Vector2(13.7f, 7.1f);
    static readonly Vector2 off2 = new Vector2(91.3f, 44.9f);

    // Точка в океане: за отмелью и ниже уровня воды (запас 0.35 — на кромку
    // прибоя). Зовут чужие скрипты — не менять сигнатуру.
    public static bool IsInOcean(Vector3 p)
        => new Vector2(p.x, p.z).magnitude > IslandRadius + 8f && p.y < SeaLevel + 0.35f;

    public static float HeightAt(float x, float z)
    {
        float h = Mathf.PerlinNoise(x * Freq + off1.x, z * Freq + off1.y) * Amp
                + Mathf.PerlinNoise(x * Freq * 3f + off2.x, z * Freq * 3f + off2.y) * Amp * 0.25f;

        // плато у спауна (центр): ровное место под базу
        float dSpawn = new Vector2(x, z).magnitude;
        h *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(8f, 30f, dSpawn));

        // котловина озера
        float dLake = Vector2.Distance(new Vector2(x, z), LakeCenter);
        float rim = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(LakeRadius * 0.5f, LakeRadius, dLake));
        h -= LakeDepth * (1f - rim);

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
