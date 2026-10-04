using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Editor-билдер Завода (X5, §9.4) — структура финальной цели пути.
// Вызывается из SetupMainScene одной строкой:
//   FactoryBuilder.Build(rng, msg => missing.AppendLine(msg));
// Сам по геймплею ничего не спавнит: ни лут-контейнеров (их ставит
// интегратор), ни зомби/спавна; рельеф не трогает — плоская промплощадка
// уже вырезана в TerrainGen.HeightAt, все постройки стоят ровно на
// TerrainGen.FactoryHeight (локальный y=0 == верх плато).
//
// Состав (GLB из Assets/Models; масштабы — по фактическим габаритам
// мешей, снятым из accessor'ов GLB, а не на глаз):
//   - главный цех 24×14 м из structure-metal-wall (панель 0.535×0.5 при
//     scale 1; в городке брали scale 6), стены 4.5 м, дверной проём-
//     пролом ~2.7 м в стене, обращённой к городу (пропуск панелей —
//     коллайдера в проёме нет, как у старых городских домов);
//   - крыша structure-metal-roof — это НАКЛОННЫЙ гофролист (подъём 0.5
//     на длине ската 0.544, ширина 0.55): два склона, конёк вдоль
//     длинной оси, к городу — фронтон с проёмом. Без коллайдеров;
//   - внутри цеха ПУСТО: пресс-финал будет позже (§9.4);
//   - труба: ствол tree-trunk (0.2Ø×0.2608 — тот же trunkH, что в Setup)
//     вытянут до 14 м, сверху кольцо barrel-open; коллайдер на стволе;
//   - силосы: бочки barrel scale 3 стопкой (секция 1.03 м), горловина
//     bottle-large; коллайдеры на секциях;
//   - забор fence по кругу r=34 (внутри плоского плато), шаг ~3 м,
//     пролом-вход ~7 м со стороны города; коллайдеры на секциях;
//   - декор без коллайдеров (как декор городка в Setup): валуны,
//     ящик, знаки у пролома, лужица майонеза (Mayo.mat) перед входом.
// Все имена — с префиксом «Factory_», root «Factory» на промплощадке.
public static class FactoryBuilder
{
    // Габариты GLB (сняты из accessor'ов моделей; при обновлении ассетов
    // перепроверить — на них держатся масштабы ниже).
    const float WallW = 0.5354f;   // ширина панели structure-metal-wall
    const float WallH = 0.5f;      // высота панели structure-metal-wall
    const float RoofRun = 0.5442f; // проекция ската structure-metal-roof
    const float RoofRise = 0.5f;   // подъём ската structure-metal-roof
    const float RoofWidth = 0.55f; // ширина листа по коньку
    const float TrunkH = 0.2608f;  // высота ствола tree-trunk (= trunkH в Setup)
    const float BarrelH = 0.344f;  // высота бочки barrel/barrel-open
    const float FenceW = 0.5f;     // ширина секции fence

    public static void Build(System.Random rng, Action<string> missing)
    {
        Vector2 c = TerrainGen.FactoryCenter;

        // Root на верху плато; +Z развёрнут на город: фасад цеха с проёмом,
        // ворота в заборе и знаки смотрят навстречу пути (§9.4).
        var root = new GameObject("Factory");
        root.transform.position = new Vector3(c.x, TerrainGen.FactoryHeight, c.y);
        Vector3 toTown = new Vector3(TerrainGen.TownCenter.x - c.x, 0f,
                                     TerrainGen.TownCenter.y - c.y);
        root.transform.rotation = Quaternion.LookRotation(toTown.normalized);

        // прогрев GLB: LoadModel дёргается один раз на тип; недоступный тип
        // пишет одну строку в missing, зависимый блок пропускается целиком
        // (тот же принцип, что у прогрева городка в Setup)
        string[] pieces =
        {
            "structure-metal-wall", "structure-metal-roof", "barrel",
            "barrel-open", "bottle-large", "fence", "tree-trunk",
            "box-large", "signpost-single", "rock-a", "rock-b"
        };
        var prefab = new Dictionary<string, GameObject>();
        foreach (var p in pieces)
        {
            var loaded = LoadModel(p);
            if (loaded == null) missing("factory: " + p + ".glb");
            else prefab[p] = loaded;
        }

        BuildWorkshop(prefab, root);
        BuildChimney(prefab, root);
        BuildSilos(prefab, root);
        BuildFence(prefab, root);
        BuildYardDecor(prefab, root, rng);
        BuildMayoPuddle(root);
    }

    // ---------- главный цех ----------

    // Цех 24×14: длинной стороной (фасадом 24 м с проёмом) к городу —
    // локальный +Z корня. Стены — панели с бокс-коллайдерами по габаритам;
    // проём — просто пропуск панелей, коллайдера в нём нет.
    static void BuildWorkshop(Dictionary<string, GameObject> prefab, GameObject root)
    {
        if (!prefab.ContainsKey("structure-metal-wall")) return; // без панелей цеха нет
        const float halfL = 12f;  // половина длины цеха (локальный X)
        const float halfW = 7f;   // половина ширины (локальный Z)
        const float wallH = 4.5f; // высота стен
        const float panelW = 2.9f;
        var wScale = new Vector3(panelW / WallW, wallH / WallH, 6f); // толщина ~0.53

        // фронт (z=+7): два сегмента с проёмом ~2.7 м по центру
        SpawnWallRun(prefab, root, "Wall_F", -halfL, -1.5f, halfW, 0f, wScale, 4);
        SpawnWallRun(prefab, root, "Wall_F", 1.5f, halfL, halfW, 0f, wScale, 4);
        // тыл сплошной; бока с заходом в углы внахлёст (±6.7 при стене на ±7)
        SpawnWallRun(prefab, root, "Wall_B", -halfL, halfL, -halfW, 0f, wScale, 9);
        SpawnWallRun(prefab, root, "Wall_L", -halfW + 0.3f, halfW - 0.3f, -halfL, 90f, wScale, 5);
        SpawnWallRun(prefab, root, "Wall_R", -halfW + 0.3f, halfW - 0.3f, halfL, 90f, wScale, 5);

        // крыша: structure-metal-roof — наклонный гофролист (подъём к
        // локальному +X). Два склона, конёк вдоль длинной оси (X), уклон
        // ~20°, свесы за стены. Коллайдеров нет — как крыши городка.
        if (prefab.ContainsKey("structure-metal-roof"))
        {
            const float run = 8.4f;  // горизонтальная проекция ската (7 + свес)
            const float rise = 2.9f; // подъём к коньку
            var rScale = new Vector3(run / RoofRun, rise / RoofRise, 2.6f / RoofWidth);
            // ряд панелей вдоль конька: ±13 при стенах ±12 — свес на фронтонах
            for (int k = 0; k < 10; k++)
            {
                float x = -11.7f + k * 2.6f;
                // правый склон: низ у z≈+8.4 (низкая кромка листа), конёк у z≈0
                Piece(prefab, root, "structure-metal-roof", $"Factory_Roof_R{k}",
                    new Vector3(x, wallH, 4.3f), 90f, rScale);
                // левый склон зеркально; у конька листы сходятся с зазором
                // ~0.1 м — снаружи не читается, z-fighting исключён
                Piece(prefab, root, "structure-metal-roof", $"Factory_Roof_L{k}",
                    new Vector3(x, wallH, -4.3f), -90f, rScale);
            }
        }
    }

    // Ряд стены: n панелей от from до to вдоль выбранной оси на поперечной
    // координате cross (yaw 0 — ряд вдоль X: фасад/тыл; yaw 90 — вдоль Z:
    // бока, cross — координата X стены). Панели внахлёст, чтобы между
    // коллайдерами не оставалось щелей-«небоскрёбов» для лучей.
    static void SpawnWallRun(Dictionary<string, GameObject> prefab, GameObject root,
        string baseName, float from, float to, float cross, float yawDeg,
        Vector3 scale, int n)
    {
        float step = (to - from) / n;
        for (int i = 0; i < n; i++)
        {
            float along = from + step * (i + 0.5f);
            Vector2 lp = yawDeg == 0f
                ? new Vector2(along, cross)
                : new Vector2(cross, along);
            Piece(prefab, root, "structure-metal-wall", $"Factory_{baseName}_{i}",
                new Vector3(lp.x, 0f, lp.y), yawDeg, scale,
                collider: true, minThickness: 0.25f);
        }
    }

    // ---------- труба и силосы ----------

    // Труба: ствол, вытянутый до 14 м (масштаб по факту TrunkH), стоит за
    // цехом справа отдельно от стен; сверху кольцо открытых бочек —
    // «оголовок». Коллайдер на стволе, кольцо — декор.
    static void BuildChimney(Dictionary<string, GameObject> prefab, GameObject root)
    {
        if (!prefab.ContainsKey("tree-trunk")) return;
        const float h = 14f;
        var pos = new Vector3(9.5f, 0f, -9.5f);
        Piece(prefab, root, "tree-trunk", "Factory_Chimney", pos, 0f,
            new Vector3(3f, h / TrunkH, 3f), collider: true); // Ø0.6 м, 14 м
        if (!prefab.ContainsKey("barrel-open")) return;
        for (int i = 0; i < 6; i++)
        {
            float a = i / 6f * Mathf.PI * 2f;
            Piece(prefab, root, "barrel-open", $"Factory_Chimney_Ring_{i}",
                pos + new Vector3(Mathf.Cos(a) * 0.55f, h - 0.05f, Mathf.Sin(a) * 0.55f),
                a * Mathf.Rad2Deg, new Vector3(2.6f, 2.2f, 2.6f));
        }
    }

    // Силосы слева от цеха: бочки scale 3 стопкой (секция 1.03 м), у второго
    // сверху горловина bottle-large. Коллайдер на каждой секции — цистерны
    // должны держать удар (и луч).
    static void BuildSilos(Dictionary<string, GameObject> prefab, GameObject root)
    {
        if (!prefab.ContainsKey("barrel")) return;
        const float s = 3f;
        float bh = BarrelH * s;
        var a = new Vector3(-16.5f, 0f, -3.2f);
        Piece(prefab, root, "barrel", "Factory_Silo_A_0", a, 0f, Vector3.one * s, true);
        Piece(prefab, root, "barrel", "Factory_Silo_A_1", a + Vector3.up * bh, 30f,
            Vector3.one * s, true);
        if (prefab.ContainsKey("barrel-open"))
            Piece(prefab, root, "barrel-open", "Factory_Silo_A_2", a + Vector3.up * bh * 2f,
                70f, Vector3.one * s, true);
        var b = new Vector3(-16.5f, 0f, 1.8f);
        Piece(prefab, root, "barrel", "Factory_Silo_B_0", b, 0f, Vector3.one * s, true);
        if (prefab.ContainsKey("barrel-open"))
            Piece(prefab, root, "barrel-open", "Factory_Silo_B_1", b + Vector3.up * bh,
                45f, Vector3.one * s, true);
        if (prefab.ContainsKey("bottle-large"))
            Piece(prefab, root, "bottle-large", "Factory_Silo_B_Neck",
                b + Vector3.up * bh * 2f, 0f, Vector3.one * 4.5f, true);
    }

    // ---------- забор и двор ----------

    // Забор по кругу r=34 (внутри плоского плато FactoryRadius=38): два
    // прогона от краёв пролома-входа (гейт ~7 м дугой к городу, локальный
    // +Z) до кормы, замыкающая секция на корме. Секции 3×2.07 м с
    // бокс-коллайдерами — забор честный, как перила причала.
    static void BuildFence(Dictionary<string, GameObject> prefab, GameObject root)
    {
        if (!prefab.ContainsKey("fence")) return;
        const float r = 34f;
        var scale = new Vector3(3f / FenceW, 4f, 4f);
        float gateHalfDeg = 3.5f / r * Mathf.Rad2Deg; // половина пролома по дуге
        float stepDeg = 3.0f / r * Mathf.Rad2Deg;     // шаг ~3 м по дуге
        int idx = 0;
        for (int side = 0; side < 2; side++)
        {
            float sgn = side == 0 ? 1f : -1f;
            int kMax = Mathf.FloorToInt((180f - gateHalfDeg) / stepDeg);
            for (int k = 0; k <= kMax; k++)
                SpawnFenceAt(prefab, root, idx++, 90f + sgn * (gateHalfDeg + k * stepDeg),
                    r, scale);
        }
        // два прогона сходятся у кормы с зазором ~2.4 м — закрываем секцией
        SpawnFenceAt(prefab, root, idx++, 270f, r, scale);
    }

    static void SpawnFenceAt(Dictionary<string, GameObject> prefab, GameObject root,
        int idx, float deg, float r, Vector3 scale)
    {
        float rad = deg * Mathf.Deg2Rad;
        float cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);
        // полотно вдоль касательной к кругу: локальный X секции -> тангенс
        // (та же математика yaw, что у перил причала в Setup)
        float yaw = Mathf.Atan2(-cos, -sin) * Mathf.Rad2Deg;
        Piece(prefab, root, "fence", $"Factory_Fence_{idx}",
            new Vector3(cos * r, 0f, sin * r), yaw, scale,
            collider: true, minThickness: 0.12f);
    }

    // Декор двора: валуны, ящик, знаки по бокам пролома. Без коллайдеров —
    // как декор городка в Setup (бочки/ящики там тоже без коллайдеров).
    static void BuildYardDecor(Dictionary<string, GameObject> prefab, GameObject root,
        System.Random rng)
    {
        Decor(prefab, root, "rock-a", "Factory_Rock_A", new Vector3(18f, 0f, 12f), 4.5f, rng);
        Decor(prefab, root, "rock-b", "Factory_Rock_B", new Vector3(-21f, 0f, -13f), 3.8f, rng);
        Decor(prefab, root, "box-large", "Factory_Box", new Vector3(5.5f, 0f, 31f), 3.5f, rng);
        Decor(prefab, root, "signpost-single", "Factory_Sign_0", new Vector3(-3.2f, 0f, 35.6f), 5f, rng);
        Decor(prefab, root, "signpost-single", "Factory_Sign_1", new Vector3(3.4f, 0f, 35.2f), 5f, rng);
    }

    static void Decor(Dictionary<string, GameObject> prefab, GameObject root,
        string glb, string goName, Vector3 lp, float scale, System.Random rng)
    {
        var go = Piece(prefab, root, glb, goName, lp, 0f, Vector3.one * scale);
        if (go != null) // случайный курс, как у пропсов Setup — не «по линейке»
            go.transform.localRotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
    }

    // Лужица майонеза у входа: вытекла из того, что тащили в цех. Mayo.mat
    // если автор уже завёл его, иначе — тот же fallback, что у лужи озера
    // в Setup (общий файл, дубль не создаётся). Декор: коллайдер снят.
    static void BuildMayoPuddle(GameObject root)
    {
        var puddle = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        var col = puddle.GetComponent<CapsuleCollider>();
        if (col != null) UnityEngine.Object.DestroyImmediate(col);
        puddle.name = "Factory_MayoPuddle";
        puddle.transform.SetParent(root.transform, false);
        puddle.transform.localPosition = new Vector3(0f, 0.02f, 35.5f); // перед пролома-входом
        puddle.transform.localScale = new Vector3(7f, 0.02f, 4.5f);     // овал 7×4.5 м
        var mayoMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Mayo.mat");
        if (mayoMat == null) mayoMat = CreateMayoMaterial();
        puddle.GetComponent<MeshRenderer>().sharedMaterial = mayoMat;
    }

    // Кремовый майонез — копия пути CreateMaterial из Setup (тот метод
    // приватный, а ассет должен быть общий: Setup потом подхватит его же).
    static Material CreateMayoMaterial()
    {
        EnsureFolder("Assets/Materials");
        string path = "Assets/Materials/Mayo.mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;
        var mat = new Material(Shader.Find("Standard")) { color = new Color(0.93f, 0.88f, 0.62f) };
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    // ---------- инфраструктура (локальные копии паттернов Setup) ----------

    // Кусок Завода: root-обёртка в локальных координатах корня Factory +
    // инстанс GLB с масштабом. collider=true — бокс по габаритам рендереров
    // (математика AddPieceCollider из Setup: локальные bounds мешей через
    // TransformPoint/InverseTransformPoint — точный бокс при любом повороте).
    static GameObject Piece(Dictionary<string, GameObject> prefab, GameObject root,
        string glb, string goName, Vector3 lp, float yawDeg, Vector3 scale,
        bool collider = false, float minThickness = 0f)
    {
        if (!prefab.TryGetValue(glb, out var pf)) return null; // missing уже записан
        var go = new GameObject(goName);
        go.transform.SetParent(root.transform, false);
        go.transform.localPosition = lp;
        go.transform.localRotation = Quaternion.Euler(0f, yawDeg, 0f);
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(pf);
        inst.transform.SetParent(go.transform, false);
        inst.transform.localScale = scale;
        if (collider) AddPieceCollider(go, minThickness);
        return go;
    }

    // Бокс-коллайдер по реальным габаритам: углы мешевых bounds проводятся
    // через TransformPoint/InverseTransformPoint относительно корня куска —
    // размер не раздувается поворотом (копия AddPieceCollider из Setup).
    static void AddPieceCollider(GameObject piece, float minThickness = 0f)
    {
        var lo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var hi = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        bool any = false;
        foreach (var rend in piece.GetComponentsInChildren<Renderer>())
        {
            var mf = rend.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            var b = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var corner = rend.transform.TransformPoint(new Vector3(
                    (i & 1) == 0 ? b.min.x : b.max.x,
                    (i & 2) == 0 ? b.min.y : b.max.y,
                    (i & 4) == 0 ? b.min.z : b.max.z));
                var lp = piece.transform.InverseTransformPoint(corner);
                lo = Vector3.Min(lo, lp);
                hi = Vector3.Max(hi, lp);
                any = true;
            }
        }
        if (!any) return;
        var col = piece.AddComponent<BoxCollider>();
        col.center = (lo + hi) * 0.5f;
        var size = hi - lo;
        if (minThickness > 0f)
        {
            // самую тонкую горизонтальную ось не даём сделать тоньше —
            // листовая стенка иначе прощупывается насквозь
            if (size.x <= size.z && size.x < minThickness) size.x = minThickness;
            else if (size.z < size.x && size.z < minThickness) size.z = minThickness;
        }
        col.size = size;
    }

    // GLB-грузчик — копия LoadModel из SetupMainScene (там метод приватный):
    // синхронный реимпорт с перехватом ошибок, затем обходные пути загрузки,
    // в последнюю очередь — поиск по проекту. null = типа нет, блоки с ним
    // пропускаются (строку в missing пишет прогрев выше, здесь молча).
    static GameObject LoadModel(string name)
    {
        string path = $"Assets/Models/{name}.glb";

        var captured = new System.Text.StringBuilder();
        Application.LogCallback spy = (msg, stack, type) =>
        {
            if (type != LogType.Log) captured.Append(type).Append(": ").AppendLine(msg);
        };
        Application.logMessageReceived += spy;
        AssetDatabase.ImportAsset(path,
            ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        Application.logMessageReceived -= spy;

        var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (go != null) return go;

        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
            if (asset is GameObject g) return g;

        foreach (var guid in AssetDatabase.FindAssets($"t:GameObject {name}", new[] { "Assets/Models" }))
        {
            go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (go != null) return go;
        }

        Debug.LogWarning($"[Survival] Модель не найдена: {path} (Завод). Ошибки импорта: " +
                         $"{(captured.Length == 0 ? "нет" : captured.ToString())}");
        return null;
    }

    // Копия EnsureFolder из SetupMainScene: рекурсивное создание пути.
    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        string leaf = System.IO.Path.GetFileName(path);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }
}
