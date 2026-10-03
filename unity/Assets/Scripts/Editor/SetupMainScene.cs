using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Один раз собирает и сохраняет сцену Assets/Scenes/Main.unity:
// круглый остров 64 чанками в океане протухшего майонеза, солнце с
// DayNight, линейный туман §9.6 (22–115 м, серо-жёлтая мгла), игрок
// (капсула + Player/Inventory/Stats, Head с камерой), DebugHud, деревья
// и камни как ResourceNode (базово 50/18 шт, масштабируются от
// TerrainGen.Size — см. блок рассеивания), городок «Гнилой Причал»
// с причалом, гора с пещерой босса FatZombie на северо-западе (вырез —
// в TerrainGen, свод/стены/арка — примитивами здесь), 4 курицы по
// квандрантам, силуэты-«сторожа» кольцом на суше и две пасхалки §9.7
// (Monument_Bucket, MayoMonolith) как чистый визуал. ItemData-ассеты
// (древесина/камень/плоть/фляги/яйцо/майонез; ягоды и их кусты убраны
// автором 2026-10-03, ассет berry.asset остаётся в Resources как
// балласт — удалить руками при желании).
// Запуск: верхнее меню → Survival → Setup Main Scene.
public static class SetupMainScene
{
    [MenuItem("Survival/Setup Main Scene")]
    public static void Run()
    {
        if (EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("Survival",
                "Сначала выйди из Play-режима, потом запускай Setup.", "Ок");
            return;
        }

        var missing = new System.Text.StringBuilder();

        // --- сцена ---
        // спрашиваем про несохранённые правки текущей сцены — NewScene(Single)
        // сотрёт её молча, отмена диалога = отмена всего Setup
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        // убрать дефолтную камеру — своя будет в Head игрока
        var oldCam = GameObject.Find("Main Camera");
        if (oldCam != null) Object.DestroyImmediate(oldCam);

        // --- туман §9.6 (вариант «встроенный линейный») ---
        // RenderSettings относится к АКТИВНОЙ сцене — применяется к НОВОЙ
        // (после NewScene) и обязан встать ДО SaveScene, иначе уйдёт в
        // предыдущий файл сцены. Линейный туман 22–115 м: обзор сжимается
        // до зловещего островка видимости, силуэты читаются (§9.6).
        // Цвет — серо-жёлтая пасмурность; ночью его затемнит DayNight.
        // Небо (Sky/SkyUnlit) туман игнорит сознательно — диски солнца/луны
        // видны сквозь мглу; MayoWaves — surface-шейдер, туман подхватит сам.
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogStartDistance = 22f;
        RenderSettings.fogEndDistance = 115f;
        RenderSettings.fogColor = new Color(0.62f, 0.58f, 0.46f);

        // --- террейн: остров строится 64 чанками вместо одного меша ---
        // Карта выросла до 1000×1000: единый меш с ячейкой ~1.5 м не влезает
        // в лимит вершин, поэтому сетка 8×8 по 125 м. Ассеты чанков кэшируются
        // так же, как раньше кэшировался TerrainMesh.asset.
        EnsureFolder("Assets/Terrain");
        var terrainRoot = new GameObject("Terrain");
        const int chunkRes = 84; // ~1.5 м на ячейку — капсула не «проваливается»
        float chunkSize = TerrainGen.Size / TerrainGen.ChunkCount;
        for (int cz = 0; cz < TerrainGen.ChunkCount; cz++)
        for (int cx = 0; cx < TerrainGen.ChunkCount; cx++)
        {
            string chunkPath = $"Assets/Terrain/Terrain_c{cx}_c{cz}.asset";
            var chunkMesh = AssetDatabase.LoadAssetAtPath<Mesh>(chunkPath);
            bool isNewChunk = chunkMesh == null;
            if (isNewChunk) chunkMesh = new Mesh { name = "Terrain_c" + cx + "_c" + cz };
            TerrainGen.FillChunk(chunkMesh, cx, cz, chunkRes);
            if (isNewChunk) AssetDatabase.CreateAsset(chunkMesh, chunkPath);
            else EditorUtility.SetDirty(chunkMesh);

            var chunkGo = new GameObject("Terrain_" + cx + "_" + cz);
            chunkGo.transform.SetParent(terrainRoot.transform, false);
            chunkGo.transform.position = new Vector3(
                -TerrainGen.Size / 2f + (cx + 0.5f) * chunkSize, 0f,
                -TerrainGen.Size / 2f + (cz + 0.5f) * chunkSize);
            chunkGo.AddComponent<MeshFilter>().sharedMesh = chunkMesh;
            chunkGo.AddComponent<MeshRenderer>().sharedMaterial =
                CreateMaterial("Ground", new Color(0.35f, 0.5f, 0.25f));
            chunkGo.AddComponent<MeshCollider>().sharedMesh = chunkMesh;
        }

        // --- океан протухшего майонеза: сетка с бегущими волнами ---
        // Квадрат 1414×1414 м (покрывает карту 1000 и все углы) вместо
        // цилиндра: вершины качает шейдер MayoWaves, разводы плывут по
        // MayoSwirl. Коллайдера по-прежнему нет — упасть в океан можно,
        // выбраться нельзя (смерть в Player.cs).
        EnsureFolder("Assets/Textures"); // MayoSwirl рядом с текстурами неба
        var seaRoot = new GameObject("MayoSea");
        var seaSurface = new GameObject("Surface");
        seaSurface.transform.SetParent(seaRoot.transform, false);
        // гребень волны до +0.38: поверхность утоплена, чтобы волны не
        // протыкали плато спауна (высота 0) и пляж
        seaSurface.transform.localPosition = new Vector3(0f, -0.45f, 0f);
        const int seaRes = 128; // 129² вершин, ячейка ~11 м
        string seaMeshPath = "Assets/Terrain/MayoSeaGrid.asset";
        var seaMesh = AssetDatabase.LoadAssetAtPath<Mesh>(seaMeshPath);
        if (seaMesh != null)
        {
            // кэш как у чанков Terrain: вершины пересобираем на месте,
            // ссылки в сцене не рвутся
            FillSeaGrid(seaMesh, seaRes);
            EditorUtility.SetDirty(seaMesh);
        }
        else
        {
            seaMesh = MayoSeaGrid(seaRes);
            AssetDatabase.CreateAsset(seaMesh, seaMeshPath);
        }
        seaSurface.AddComponent<MeshFilter>().sharedMesh = seaMesh;

        var seaMat = CreateMaterial("MayoSea", new Color(0.86f, 0.80f, 0.55f));
        var swirlTex = EnsureTexture("Assets/Textures/MayoSwirl.png", MayoSwirlPixels, repeat: true);
        var wavesShader = EnsureMayoWavesShader();
        if (wavesShader != null)
        {
            // CreateMaterial даёт Standard — подменяем шейдер на волновой,
            // чтобы автор крутил цвет через _Color
            seaMat.shader = wavesShader;
            seaMat.mainTexture = swirlTex;
            seaMat.SetColor("_Color", new Color(0.92f, 0.86f, 0.60f));
        }
        else
            Debug.LogError("[Survival] MayoWaves.shader не загрузился — океан остаётся на Standard, без волн.");
        seaSurface.AddComponent<MeshRenderer>().sharedMaterial = seaMat;

        // --- солнце + день/ночь ---
        // Ищем ЛЮБОЙ включённый directional-свет (имя «Directional Light»
        // не гарантировано — без него ночь молча не наступала бы никогда).
        Light sun = null;
        foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            if (l.type == LightType.Directional && l.enabled) { sun = l; break; }
        if (sun == null)
        {
            // солнца в сцене нет — создаём своё (вращение/яркость потом
            // каждый кадр выставляет DayNight, здесь лишь разумный старт)
            var sunGo = new GameObject("Sun");
            sunGo.transform.rotation = Quaternion.Euler(50f, 170f, 0f);
            sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.1f;
        }
        sun.shadows = LightShadows.Soft;
        var dayNight = sun.gameObject.AddComponent<DayNight>();
        dayNight.sun = sun;

        // --- небо: диски солнца/луны + слой облаков (S/красота 2026-10-03) ---
        // текстуры генерим кодом и сохраняем ассетами; шейдер без тумана,
        // чтобы диски не тонули, когда включим туман §9.6
        EnsureFolder("Assets/Textures");
        var skyShader = EnsureSkyShader();
        var sunTex = EnsureTexture("Assets/Textures/SunDisc.png", SunDiscPixels);
        var moonTex = EnsureTexture("Assets/Textures/MoonDisc.png", MoonDiscPixels);
        var cloudTex = EnsureTexture("Assets/Textures/Clouds.png", CloudPixels, repeat: true);

        var skyRig = new GameObject("SkyRig");

        var sunQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Object.DestroyImmediate(sunQuad.GetComponent<Collider>());
        sunQuad.name = "SunDisc";
        sunQuad.transform.SetParent(skyRig.transform, false);
        sunQuad.transform.localScale = Vector3.one * 55f;
        var sunMat = new Material(skyShader);
        sunMat.mainTexture = sunTex;
        sunQuad.GetComponent<MeshRenderer>().sharedMaterial = sunMat;

        var moonQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Object.DestroyImmediate(moonQuad.GetComponent<Collider>());
        moonQuad.name = "MoonDisc";
        moonQuad.transform.SetParent(skyRig.transform, false);
        moonQuad.transform.localScale = Vector3.one * 38f;
        var moonMat = new Material(skyShader);
        moonMat.mainTexture = moonTex;
        moonQuad.GetComponent<MeshRenderer>().sharedMaterial = moonMat;

        var cloudGo = GameObject.CreatePrimitive(PrimitiveType.Plane);
        Object.DestroyImmediate(cloudGo.GetComponent<Collider>());
        cloudGo.name = "CloudLayer";
        cloudGo.transform.SetParent(skyRig.transform, false);
        cloudGo.transform.localScale = new Vector3(70f, 1f, 70f); // Plane 10 -> 700 м
        cloudGo.transform.position = new Vector3(0f, 78f, 0f);
        var cloudMat = new Material(skyShader);
        cloudMat.mainTexture = cloudTex;
        cloudMat.mainTextureScale = new Vector2(7f, 7f);
        cloudGo.GetComponent<MeshRenderer>().sharedMaterial = cloudMat;

        dayNight.sunDisc = sunQuad.GetComponent<MeshRenderer>();
        dayNight.moonDisc = moonQuad.GetComponent<MeshRenderer>();
        dayNight.cloudLayer = cloudGo.GetComponent<MeshRenderer>();

        // --- игрок ---
        var playerGo = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        playerGo.name = "Player";
        playerGo.transform.position = new Vector3(0f, TerrainGen.HeightAt(0f, 0f) + 1.1f, 0f);

        var capsuleCol = playerGo.GetComponent<CapsuleCollider>();
        if (capsuleCol != null) Object.DestroyImmediate(capsuleCol);

        // Тело игрока — само майонезное ведро «курочка яба» (§9.1): меш
        // капсулы-примитива прячем, визуал — GLB-ведро ребёнком ниже.
        // Коллизии остаются на CharacterController, геймплей не меняется.
        var capsuleMesh = playerGo.GetComponent<MeshFilter>();
        if (capsuleMesh != null) Object.DestroyImmediate(capsuleMesh);
        var capsuleRend = playerGo.GetComponent<MeshRenderer>();
        if (capsuleRend != null) Object.DestroyImmediate(capsuleRend);

        var cc = playerGo.AddComponent<CharacterController>();
        cc.height = 1.8f;
        cc.radius = 0.35f;
        cc.center = new Vector3(0f, 0f, 0f);

        var player = playerGo.AddComponent<Player>(); // Inventory/Stats подтянутся сами
        player.standHeight = 1.8f;

        var head = new GameObject("Head");
        head.transform.SetParent(playerGo.transform, false);
        head.transform.localPosition = new Vector3(0f, 1.6f, 0f);
        var cam = head.AddComponent<Camera>();
        cam.tag = "MainCamera";
        cam.nearClipPlane = 0.05f; // иначе объекты «исчезают» вплотную к камере
        head.AddComponent<AudioListener>();
        player.head = head.transform;

        // --- тело-ведро «курочка яба» (§9.1) ---
        // Панель этикетки в GLB смотрит в -z, вперёд игрока +z -> разворот
        // на 180°. Рост ведра 1.2 м: обод на 1.3 м ниже камеры (1.6) и при
        // прямом взгляде в кадр не лезет, при взгляде вниз видно майонез.
        var bucketModel = LoadModel("mayo-bucket");
        if (bucketModel != null)
        {
            var bucket = (GameObject)PrefabUtility.InstantiatePrefab(bucketModel);
            bucket.name = "BucketBody";
            bucket.transform.SetParent(playerGo.transform, false);
            bucket.transform.localPosition = new Vector3(0f, -cc.height / 2f, 0f); // дно на земле
            bucket.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var rends = bucket.GetComponentsInChildren<Renderer>();
            if (rends.Length > 0)
            {
                var bb = rends[0].bounds;
                foreach (var r in rends) bb.Encapsulate(r.bounds);
                if (bb.size.y > 1e-6f) bucket.transform.localScale *= 1.2f / bb.size.y;
            }

            // Майонез внутри — «шкала здоровья», читается взглядом вниз
            // (§9.2). Диск без коллайдера, чтобы не путать LOS-лучи зомби
            // и лучи взаимодействия.
            var mayoBodyMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Mayo.mat");
            if (mayoBodyMat == null)
                mayoBodyMat = CreateMaterial("Mayo", new Color(0.93f, 0.88f, 0.62f));
            var mayoGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            var mayoCol = mayoGo.GetComponent<CapsuleCollider>();
            if (mayoCol != null) Object.DestroyImmediate(mayoCol);
            mayoGo.name = "MayoInside";
            mayoGo.transform.SetParent(playerGo.transform, false);
            mayoGo.transform.localPosition = new Vector3(0f, -cc.height / 2f + 0.9f, 0f);
            mayoGo.transform.localScale = new Vector3(1.0f, 0.05f, 1.0f); // r=0.5, толщина 0.1 м
            mayoGo.GetComponent<MeshRenderer>().sharedMaterial = mayoBodyMat;
        }
        else
            missing.AppendLine("player body: mayo-bucket.glb (ведро игрока)");

        // --- временный HUD ---
        var hud = new GameObject("DebugHud");
        var debugHud = hud.AddComponent<DebugHud>();
        debugHud.stats = playerGo.GetComponent<Stats>();
        debugHud.inventory = playerGo.GetComponent<Inventory>();

        // --- префабы построек (нужны предметам-ссылкам ниже) ---
        var campfirePrefab = CreateCampfirePrefab();
        var wallPrefab = CreateWallPrefab();

        // --- предметы (ScriptableObject-ассеты) ---
        // Полный набор из Godot-эталона (§5, баланс §10). SyncItem
        // ПЕРЕЗАПИСЫВАЕТ поля даже у существующих ассетов — правки
        // баланса сюда, потом перезапуск Setup.
        EnsureFolder("Assets/Items");
        var flaskEmpty = SyncItem("flask_empty", "Фляга (пустая)", 1, worldModel: "bottle");
        var flask = SyncItem("flask", "Фляга (сырой майонез)", 1, food: 20f, poison: 15f, consumeReturns: flaskEmpty);
        var wood = SyncItem("wood", "Древесина", 30, worldModel: "resource-wood");
        var stone = SyncItem("stone", "Камень", 30, worldModel: "resource-stone");
        var meat = SyncItem("meat", "Сырая плоть", 10, food: 12f, poison: 30f, worldModel: "meat");      // §9.3: мало + яд
        // плоть зомби в этом мире майонезная — белая (решение автора 2026-10-03)
        var meatMayo = MakeWhiteModel("meat", "meat-mayo");
        if (meatMayo != null) meat.worldModel = meatMayo;
        var cookedMeat = SyncItem("cooked_meat", "Котлета", 10, food: 45f, poison: 8f, worldModel: "meat-patty"); // готовка режет яд
        // Яйца больше НЕ еда (решение автора 2026-10-03): персонаж —
        // майонезное ведро, ест только майонез и его производные (котлеты).
        // food: 0 — сырое яйцо лишь ингредиент рецепта «Домашний майонез».
        var egg = SyncItem("egg", "Яйцо", 10, food: 0f, worldModel: "egg");
        var mayo = SyncItem("mayo", "Домашний майонез", 10, food: 35f, heal: 5f, worldModel: "jar");     // чистая еда §9.3
        var spear = SyncItem("spear", "Деревянное копьё", 1, isTool: true, toolDamage: 10f, worldModel: "tool-hoe");
        // worldModel у постройками — только для иконки и вида в руке:
        // в мире ставится placeablePrefab, как и раньше
        var campfire = SyncItem("campfire", "Костёр", 5, isPlaceable: true, placeablePrefab: campfirePrefab, worldModel: "campfire-pit");
        var wall = SyncItem("wall", "Деревянная стена", 10, isPlaceable: true, placeablePrefab: wallPrefab, worldModel: "resource-planks");

        // --- пикап-заготовка для дропа ---
        // (берём бревно как визуал дропа дерева)
        var logPrefab = LoadModel("tree-log");
        PickupItem pickupPrefabAsset = null;
        if (logPrefab != null)
        {
            EnsureFolder("Assets/Prefabs");
            var pickupGo = (GameObject)PrefabUtility.InstantiatePrefab(logPrefab);
            pickupGo.name = "Pickup";
            var col = pickupGo.AddComponent<SphereCollider>();
            col.isTrigger = true; // пикапы не блокируют движение
            col.radius = 0.5f;
            pickupPrefabAsset = pickupGo.AddComponent<PickupItem>();
            PrefabUtility.SaveAsPrefabAsset(pickupGo, "Assets/Prefabs/Pickup.prefab");
            Object.DestroyImmediate(pickupGo);
            pickupPrefabAsset = AssetDatabase.LoadAssetAtPath<PickupItem>("Assets/Prefabs/Pickup.prefab");
        }
        playerGo.GetComponent<Inventory>().pickupPrefab = pickupPrefabAsset; // для выброса по ПКМ
        if (pickupPrefabAsset == null)
            // без префаба тихо ломаются: выброс ПКМ и дроп мяса с зомби
            missing.AppendLine("pickup prefab (tree-log.glb)");

        // --- у спауна только случайная пустая фляга (тайник снесён,
        // экономика — крафт; аудит механик 2026-10) ---
        SpawnPickup(pickupPrefabAsset, flaskEmpty, 1, OnGround(1.5f, 2f, 0.3f));

        // --- мир M4: рассеивание по seed + озеро ---
        // Весь блок масштабируется от TerrainGen.Size: количества — от
        // площади (sizeScale², кольцо периметра — линейно), радиусы — от
        // размера карты. При Size=200 вылезают те же числа, что были
        // «магическими»: 50 деревьев, 18 камней, кольцо 14–90 м, периметр
        // 88–96 м. Клампы Mathf.Max не дают выродиться при уменьшении карты.
        var rng = new System.Random(1337);
        float sizeScale = TerrainGen.Size / 200f;
        // радиусы кольца рассеивания: 0.07*Size..0.45*Size (14..90 м при 200),
        // внешний прижат к IslandRadius-40 — лес не стоит на отмели и в воде
        float scatterMinR = 0.07f * TerrainGen.Size;
        float scatterMaxR = Mathf.Min(0.45f * TerrainGen.Size, TerrainGen.IslandRadius - 40f);

        // деревья (все добываемые; количество ∝ площади карты)
        int treeTotal = Mathf.Max(50, Mathf.RoundToInt(50 * sizeScale * sizeScale));
        int treesSkipped = 0;
        for (int i = 0; i < treeTotal; i++)
        {
            var t = SpawnModel(i % 3 == 0 ? "tree-tall" : "tree", RandomPos(rng, scatterMinR, scatterMaxR),
                "Tree" + i, 2.6f + (float)rng.NextDouble() * 0.8f);
            if (t == null) { treesSkipped++; continue; }
            var tc = t.AddComponent<CapsuleCollider>();
            tc.center = new Vector3(0f, 1.3f, 0f);
            tc.height = 2.6f;
            tc.radius = 0.3f;
            var tn = t.AddComponent<ResourceNode>();
            tn.yield = wood; tn.hitsLeft = 3; tn.pickupPrefab = pickupPrefabAsset;
        }
        if (treesSkipped > 0)
            missing.AppendLine($"scatter trees: пропущено {treesSkipped} из {treeTotal} (нет GLB-моделей)");

        // камни (количество ∝ площади карты)
        string[] rockModels = { "rock-a", "rock-b", "rock-c" };
        int rockTotal = Mathf.Max(18, Mathf.RoundToInt(18 * sizeScale * sizeScale));
        int rocksSkipped = 0;
        for (int i = 0; i < rockTotal; i++)
        {
            var r = SpawnModel(rockModels[i % 3], RandomPos(rng, scatterMinR, scatterMaxR),
                "Rock" + i, 1.5f + (float)rng.NextDouble() * 0.7f);
            if (r == null) { rocksSkipped++; continue; }
            var rc = r.AddComponent<BoxCollider>();
            rc.center = new Vector3(0f, 0.5f, 0f);
            rc.size = new Vector3(1.2f, 1f, 1.2f);
            var rn = r.AddComponent<ResourceNode>();
            rn.yield = stone; rn.hitsLeft = 3; rn.pickupPrefab = pickupPrefabAsset;
        }
        if (rocksSkipped > 0)
            missing.AppendLine($"scatter rocks: пропущено {rocksSkipped} из {rockTotal} (нет GLB-моделей)");

        // ягодных кустов больше нет (решение автора 2026-10-03:
        // белые точки-глитчи на всю карту, ягоды не нужны по мете §9.3)
        // → в missing-отчёте для них строк тоже не держим.

        // --- силуэты по периметру: край карты не должен быть пустым ---
        // Кольцо 0.34*Size..0.41*Size (340–410 м при 1000): суша и дюны
        // перед отмелью (IslandRadius=420). Старые 0.44–0.48*Size после
        // ввода океана оказались в майонезе — силуэты стояли бы по колено
        // в воде. Редкие крупные камни и мёртвые деревья. Это АТМОСФЕРА,
        // не забор: коллайдер ставим только большим камням, деревья-силуэты
        // — декор.
        string[] edgeRocks = { "rock-sand-a", "rock-sand-b", "rock-sand-c" };
        string[] edgeDeadTrees = { "tree-trunk", "tree-autumn-trunk", "tree-autumn-tall" };
        // кольцо, а не площадь → количество масштабируется линейно, не квадратично
        int edgeTotal = Mathf.Max(14, Mathf.RoundToInt(14 * sizeScale));
        float edgeMinR = 0.34f * TerrainGen.Size;
        float edgeMaxR = 0.41f * TerrainGen.Size;
        int edgeSkipped = 0;
        for (int i = 0; i < edgeTotal; i++)
        {
            // равномерный обход круга + джиттер, чтобы не «забор из столбов»
            float ang = i / (float)edgeTotal * Mathf.PI * 2f
                        + ((float)rng.NextDouble() - 0.5f) * 0.35f;
            float r = Mathf.Lerp(edgeMinR, edgeMaxR, (float)rng.NextDouble());
            float x = Mathf.Cos(ang) * r, z = Mathf.Sin(ang) * r;
            bool isRock = rng.Next(0, 2) == 0;
            string model = isRock
                ? edgeRocks[rng.Next(0, edgeRocks.Length)]
                : edgeDeadTrees[rng.Next(0, edgeDeadTrees.Length)];
            var s = SpawnModel(model, OnGround(x, z), "Edge" + i,
                isRock ? 3.5f + (float)rng.NextDouble() * 1.5f
                       : 3.0f + (float)rng.NextDouble() * 1.0f);
            if (s == null) { edgeSkipped++; continue; }
            // поворот по случайному курсу — иначе все «смотрят» в одну сторону
            s.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
            if (isRock)
            {
                var bc = s.AddComponent<BoxCollider>();
                bc.center = new Vector3(0f, 1f, 0f);
                bc.size = new Vector3(2.5f, 2f, 2.5f);
            }
        }
        if (edgeSkipped > 0)
            missing.AppendLine($"edge silhouettes: пропущено {edgeSkipped} из {edgeTotal} (нет GLB-моделей)");

        // --- пасхалки §9.7: чисто визуальные пропсы, без интерактива ---
        // (а) «Памятник ведру» — большое ведро в честь Прародителя Майонеза.
        //     Стоит в 60–180 м от спауна (озеро исключает RandomPos само).
        var monument = SpawnModel("bucket", RandomPos(rng, 60f, 180f), "Monument_Bucket", 6f);
        if (monument == null)
            missing.AppendLine("easter egg: Monument_Bucket (bucket.glb)");
        else
            monument.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);

        // (б) «Майолит» — маяк-знамение посреди протухшего майонеза.
        //     Не RandomPos: азимут жёстко противоположен городу, радиус
        //     IslandRadius+130 — стоит в океане, виден с берега как дурной
        //     знак над горизонтом.
        Vector2 monoDir = -TerrainGen.TownCenter.normalized; // от города прочь
        float monoR = TerrainGen.IslandRadius + 130f;
        var monolith = SpawnModel("bottle-large",
            new Vector3(monoDir.x * monoR, 2f, monoDir.y * monoR), "MayoMonolith", 16f);
        if (monolith == null)
            missing.AppendLine("easter egg: MayoMonolith (bottle-large.glb)");
        else
            monolith.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);


        // озеро: вода + наполнение фляги
        float lakeY = TerrainGen.HeightAt(TerrainGen.LakeCenter.x, TerrainGen.LakeCenter.y);
        var lake = new GameObject("MayoPuddle");
        lake.transform.position = new Vector3(TerrainGen.LakeCenter.x, lakeY + 1.2f, TerrainGen.LakeCenter.y);

        var water = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        water.name = "Surface";
        water.transform.SetParent(lake.transform, false);
        water.transform.localScale = new Vector3(TerrainGen.LakeRadius * 1.6f, 0.05f, TerrainGen.LakeRadius * 1.6f);
        // майонезная лужа (§9): если уже есть руками настроенный Mayo.mat —
        // берём ЕГО (автор мог подкрутить глянец/цвет), иначе — fallback
        // через CreateMaterial с базовым кремовым цветом
        var mayoMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Mayo.mat");
        if (mayoMat == null)
            mayoMat = CreateMaterial("Mayo", new Color(0.93f, 0.88f, 0.62f));
        water.GetComponent<MeshRenderer>().sharedMaterial = mayoMat;
        var waterCol = water.GetComponent<CapsuleCollider>();
        if (waterCol != null) Object.DestroyImmediate(waterCol);

        // зона взаимодействия: тонкий немасштабированный бокс на уровне воды
        // (луч не попадает в триггер, стартуя изнутри него, поэтому НЕ
        // trigger — «стоять на воде» у кромки выглядит как мелководье).
        // Вода — юнит-цилиндр с localScale = LakeRadius*1.6 → её диаметр
        // 1.6*R; бокс вписываем в круг с запасом: сторона ≈ 0.9 * диаметр,
        // чтобы зона визуально совпадала с кромкой воды.
        var zone = new GameObject("InteractZone");
        zone.transform.SetParent(lake.transform, false);
        var zoneCol = zone.AddComponent<BoxCollider>();
        float zoneSide = TerrainGen.LakeRadius * 1.6f * 0.9f; // 0.9 * диаметр воды
        zoneCol.size = new Vector3(zoneSide, 0.2f, zoneSide);
        var waterSource = zone.AddComponent<WaterSource>();
        waterSource.emptyFlask = flaskEmpty;
        waterSource.fullFlask = flask;

        // --- городок «Гнилой Причал» (S/город 2026-10-03) ---
        // Осмысленная планировка вместо случайной разрухи: главная улица
        // по оси «центр острова → причал», площадь с рынком в её начале,
        // шесть ходибельных домов по сторонам (последний у причала
        // полуразрушен), причал с перилами и лут в домах/на причале.
        // Терраса плоская: вся застройка лежит в радиусе 36 м от townC,
        // где HeightAt == TownHeight ровно.
        Vector2 townC = TerrainGen.TownCenter;
        float townR = townC.magnitude;
        Vector2 pierDir = townC.normalized;  // ось улицы и причала, наружу
        Vector2 perpDir = new Vector2(-pierDir.y, pierDir.x);

        // таблица лута контейнеров: фляга, дерево, камень, майонез, котлета
        var lootTable = new ItemData[] { flask, wood, stone, mayo, cookedMeat };

        // прогрев кусков городка: LoadModel синхронно реимпортирует GLB,
        // на 200+ кусков дешевле один проход. Тип без ассета пишет одну
        // строку в missing, и составные блоки с ним пропускаются целиком.
        string[] townPieces = {
            "structure-floor", "tree-trunk", "structure-metal-wall", "structure-metal-doorway",
            "structure-metal-roof", "bedroll", "signpost", "barrel", "barrel-open",
            "box-large", "box-large-open", "chest", "campfire-pit", "fence",
            // типы заброшенностей: раньше грузились мимо словаря и молча
            // не спавнились — теперь прогреваются вместе с городом
            "box-open", "signpost-single", "resource-planks"
        };
        var townPrefab = new Dictionary<string, GameObject>();
        foreach (var piece in townPieces)
        {
            var loaded = LoadModel(piece);
            if (loaded == null) missing.AppendLine("town: " + piece + ".glb");
            else townPrefab[piece] = loaded;
        }

        // --- главная улица: мощёная, две полосы тайлов по 2 м (ширина 4 м),
        // от площади (r = townR-26) до входа на причал (r = townR+8), шаг 2 м.
        // Тайл structure-floor 0.5 м -> масштаб 4; приплюснут по Y, как настил
        // (в GLB у секции полметра «ног»). Коллайдер по габаритам — ступенька
        // 0.16 м, CharacterController заходит.
        if (townPrefab.ContainsKey("structure-floor"))
        {
            const float secScale = 4f; // тайл 0.5 -> 2.0 м
            const float secY = 0.3f;   // толщина плиты ~0.16 м
            int secI = 0;
            for (float d = -26f; d <= 8f + 0.01f; d += 2f)
            {
                secI++;
                for (int lane = 0; lane < 2; lane++)
                {
                    Vector2 p = townC + pierDir * d + perpDir * (lane * 2f - 1f);
                    var sec = SpawnTownPiece(townPrefab, "structure-floor",
                        OnGround(p.x, p.y), "Street_" + secI + (lane == 0 ? "" : "_b"));
                    if (sec == null) continue;
                    sec.transform.GetChild(0).localScale = new Vector3(secScale, secY, secScale);
                    AddPieceCollider(sec);
                }
            }
        }

        // --- площадь в начале улицы (r = townR-26): знак, бочки горкой,
        // ящик, сундук, холодный очаг. SpawnTownPiece не вешает Campfire —
        // очаг декор, живой ставится игроком. Всё по бокам от полос улицы
        // (|перп| >= 2.4), чтобы не перекрывать мощёную дорогу.
        Vector2 sqC = townC - pierDir * 26f;
        Vector2 signPos = sqC + perpDir * 2.6f + pierDir * 1.5f;
        var signpost = SpawnTownPiece(townPrefab, "signpost",
            OnGround(signPos.x, signPos.y), "Town_Signpost", 6f);
        if (signpost != null)
            signpost.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
        string[] barrelRow = { "barrel", "barrel-open", "barrel" };
        for (int i = 0; i < 3; i++)
        {
            Vector2 bp = sqC - perpDir * 4.6f - pierDir * 1.0f
                       + perpDir * (i * 1.1f) + pierDir * ((i % 2) * 0.9f);
            var barrelGo = SpawnTownPiece(townPrefab, barrelRow[i],
                OnGround(bp.x, bp.y), "Town_Barrel_" + i, 2.5f);
            if (barrelGo != null)
                barrelGo.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
        }
        Vector2 boxPos = sqC + perpDir * 3.1f - pierDir * 2.2f;
        var boxGo = SpawnTownPiece(townPrefab, "box-large-open",
            OnGround(boxPos.x, boxPos.y), "Town_Box", 2.5f);
        if (boxGo != null)
            boxGo.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
        Vector2 chestPos = sqC + perpDir * 3.4f + pierDir * 0.6f;
        var chestGo = SpawnTownPiece(townPrefab, "chest",
            OnGround(chestPos.x, chestPos.y), "Town_Chest", 2.5f);
        if (chestGo != null)
            chestGo.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
        Vector2 pitPos = sqC - pierDir * 3.0f;
        SpawnTownPiece(townPrefab, "campfire-pit", OnGround(pitPos.x, pitPos.y), "Town_Firepit", 3.5f);

        // навес рынка: плита-«пол» на 4 столбах стволов, высота 2.4 м —
        // под ним свободно проходят. Декор без коллайдеров.
        if (townPrefab.ContainsKey("tree-trunk") && townPrefab.ContainsKey("structure-floor"))
        {
            Vector2 mc = sqC - perpDir * 4.0f + pierDir * 1.2f;
            const float canopyH = 2.4f;
            const float trunkH = 0.2608f; // высота ствола в GLB
            for (int i = 0; i < 4; i++)
            {
                float sx = i % 2 == 0 ? -1f : 1f;
                float sz = i < 2 ? -1f : 1f;
                Vector2 pp = mc + (perpDir * sx + pierDir * sz) * 1.1f;
                var post = SpawnTownPiece(townPrefab, "tree-trunk",
                    OnGround(pp.x, pp.y), "Town_Market_Post_" + i);
                if (post != null)
                    post.transform.GetChild(0).localScale =
                        new Vector3(1.4f, canopyH / trunkH, 1.4f);
            }
            var canopy = SpawnTownPiece(townPrefab, "structure-floor",
                new Vector3(mc.x, TerrainGen.TownHeight + canopyH, mc.y), "Town_Market_Canopy");
            if (canopy != null)
                canopy.transform.GetChild(0).localScale = new Vector3(6.4f, 0.4f, 6.4f);
        }

        // --- дома вдоль улицы: 6 штук по обе стороны, фасад в 5.5 м от оси,
        // шаг вдоль 10 м, косость фасада ±4°. Панель стены 0.535×0.5 м,
        // масштаб 6 -> 3.2×3.0; полотно в GLB лежит на ребре тайла (сдвиг
        // 0.2233 от пивота) — компенсируем в позиции корня, как в старых
        // лачугах. Дома ходибельные: фундамент-плита 4×4 со ступенькой
        // 0.16 м, коллайдеры на стенах и фундаменте, дверной проём открыт
        // (косяки + перемычка, размеры по замеру GLB). Дом 6 (последний,
        // у причала) полуразрушен: без крыши, задняя стена отвалилась под
        // 25-40°. В домах 1-2 перегородка и лежанка, в домах 1/3/5 —
        // сундук или ящик с лут-контейнером Loot_Town_N.
        if (townPrefab.ContainsKey("structure-metal-wall")
            && townPrefab.ContainsKey("structure-metal-doorway"))
        {
            const float wallScale = 6f;
            const float roomHalf = 1.6f;
            const float wallOff = roomHalf + 0.2233f * wallScale; // пивот панели
            const float foundY = 0.165f;    // верх фундамента (0.5487 * 0.3)
            float wallH = 0.5f * wallScale; // верх стен — сюда кладём крышу
            int lootI = 0;
            for (int i = 0; i < 6; i++)
            {
                int s = i < 3 ? 1 : -1; // сторона улицы
                float d = s > 0 ? -18f + i * 10f : -13f + (i - 3) * 10f;
                bool ruined = i == 5;
                bool twoRooms = i == 0 || i == 1;
                bool withLoot = i == 0 || i == 2 || i == 4;
                string hn = "Town_House_" + (i + 1);

                Vector2 hp = townC + pierDir * d + perpDir * (s * (roomHalf + 5.5f));
                var house = new GameObject(hn);
                house.transform.position = OnGround(hp.x, hp.y);
                // фасад к улице: +Z дома смотрит через дорогу, косость ±4°
                Quaternion face = Quaternion.LookRotation(
                    new Vector3(-s * perpDir.x, 0f, -s * perpDir.y));
                house.transform.rotation =
                    Quaternion.Euler(0f, ((float)rng.NextDouble() - 0.5f) * 8f, 0f) * face;

                // фундамент: 2×2 секции -> плита 4×4 м, чуть шире стен
                if (townPrefab.ContainsKey("structure-floor"))
                    for (int f = 0; f < 4; f++)
                    {
                        var fsec = SpawnTownPiece(townPrefab, "structure-floor",
                            Vector3.zero, hn + "_Found_" + f);
                        if (fsec == null) continue;
                        fsec.transform.SetParent(house.transform, false);
                        fsec.transform.localPosition =
                            new Vector3((f % 2) * 2f - 1f, 0f, (f / 2) * 2f - 1f);
                        fsec.transform.GetChild(0).localScale = new Vector3(4f, 0.3f, 4f);
                        AddPieceCollider(fsec); // по нему ходят
                    }

                // вход: дверная панель к улице, проём не запираем коллайдером
                var door = SpawnTownPiece(townPrefab, "structure-metal-doorway",
                    Vector3.zero, hn + "_Door", wallScale);
                if (door != null)
                {
                    door.transform.SetParent(house.transform, false);
                    door.transform.localPosition = new Vector3(0f, foundY, wallOff);
                    AddDoorwayColliders(door);
                }

                // глухие стены E/S/W
                string[] sideNames = { "WallE", "WallS", "WallW" };
                Vector3[] sidePos = {
                    new Vector3(wallOff, foundY, 0f), new Vector3(0f, foundY, -wallOff),
                    new Vector3(-wallOff, foundY, 0f) };
                float[] sideYaw = { 90f, 180f, 270f };
                for (int side = 0; side < 3; side++)
                {
                    var piece = SpawnTownPiece(townPrefab, "structure-metal-wall",
                        Vector3.zero, hn + "_" + sideNames[side], wallScale);
                    if (piece == null) continue;
                    piece.transform.SetParent(house.transform, false);
                    if (ruined && side == 1)
                    {
                        // задняя стена отвалилась: наклон 25-40° и отвал от дома
                        piece.transform.localRotation = Quaternion.Euler(0f,
                            sideYaw[side] + ((float)rng.NextDouble() - 0.5f) * 20f,
                            25f + (float)rng.NextDouble() * 15f);
                        piece.transform.localPosition = sidePos[side] * 1.4f;
                    }
                    else
                    {
                        piece.transform.localRotation = Quaternion.Euler(0f, sideYaw[side], 0f);
                        piece.transform.localPosition = sidePos[side];
                    }
                    AddPieceCollider(piece, 0.15f);
                }

                // крыша (у разрушенного дома нет)
                if (!ruined && townPrefab.ContainsKey("structure-metal-roof"))
                {
                    var roof = SpawnTownPiece(townPrefab, "structure-metal-roof",
                        Vector3.zero, hn + "_Roof");
                    if (roof != null)
                    {
                        roof.transform.SetParent(house.transform, false);
                        // 6 по пятну (3.3 м, со свесом), 2.2 по высоте конька
                        roof.transform.GetChild(0).localScale = new Vector3(6f, 2.2f, 6f);
                        roof.transform.localPosition = new Vector3(0f, foundY + wallH, 0f);
                    }
                }

                // перегородка: укороченная панель делит дом на 2 комнаты,
                // у стен зазор ~0.75 м — протиснуться можно
                if (twoRooms && townPrefab.ContainsKey("structure-metal-wall"))
                {
                    var part = SpawnTownPiece(townPrefab, "structure-metal-wall",
                        Vector3.zero, hn + "_Partition", wallScale);
                    if (part != null)
                    {
                        part.transform.SetParent(house.transform, false);
                        // полотно смещено в GLB на -0.2233 от пивота: корень
                        // ставим так, чтобы полотно легло на z=0 посреди дома
                        part.transform.localPosition = new Vector3(0f, foundY, 0.2233f * wallScale);
                        part.transform.GetChild(0).localScale = new Vector3(2.2f, 6f, 6f);
                        AddPieceCollider(part);
                    }
                    if (townPrefab.ContainsKey("bedroll"))
                    {
                        var bed = SpawnTownPiece(townPrefab, "bedroll",
                            Vector3.zero, hn + "_Bedroll", 3f);
                        if (bed != null)
                        {
                            bed.transform.SetParent(house.transform, false);
                            bed.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
                            bed.transform.localPosition = new Vector3(0f, foundY, -0.8f);
                        }
                    }
                }

                // лут: сундук/ящик внутри + невидимый контейнер Loot_Town_N
                if (withLoot)
                {
                    lootI++;
                    bool useChest = lootI != 2; // дом 3 — открытый ящик
                    string vis = useChest ? "chest" : "box-large-open";
                    var lootProp = SpawnTownPiece(townPrefab, vis,
                        Vector3.zero, hn + (useChest ? "_Chest" : "_Box"), 2.5f);
                    Vector3 propLocal = new Vector3(-0.8f, foundY, twoRooms ? 0.8f : -0.85f);
                    if (lootProp != null)
                    {
                        lootProp.transform.SetParent(house.transform, false);
                        lootProp.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
                        lootProp.transform.localPosition = propLocal;
                    }
                    SpawnLootPoint(house.transform.TransformPoint(propLocal + Vector3.up * 0.35f),
                        "Loot_Town_" + lootI, lootTable, 2);
                }
            }
        }

        // --- причал: 3 ряда настила (~5.2 м шириной) от кромки городка
        // (r = townR+10) наружу до IslandRadius+10, площадка разворота
        // +2 ряда на конце. Над водой держится уровня моря +0.4 (принцип
        // прежнего причала), над землёй стелется по грунту — иначе у входа
        // с мостовой ступенька 0.4 м, которую не пройти. Тайл 0.5 м ->
        // масштаб 3.5 (1.75 м), ряды вплотную. Коллайдеры на секциях:
        // причал — единственная сухая дорога к морю.
        if (townPrefab.ContainsKey("structure-floor"))
        {
            Vector2 perp = perpDir;
            const float pierUpSea = 0.4f;    // над водой: уровень моря +0.4
            const float pierUpLand = 0.135f; // над землёй: верх настила +0.3
            const float secScale = 3.5f;     // тайл 0.5 -> 1.75 м
            const float secY = 0.3f;         // толщина настила ~0.16 м
            float deckYOf(Vector2 p) => Mathf.Max(TerrainGen.HeightAt(p.x, p.y) + pierUpLand,
                                                  TerrainGen.SeaLevel + pierUpSea);
            int secI = 0;
            for (float d = townR + 10f; d <= TerrainGen.IslandRadius + 14f + 0.01f; d += 2f)
            {
                secI++;
                for (int row = 0; row < 3; row++)
                {
                    Vector2 p = townC + pierDir * d + perp * ((row - 1) * secScale * 0.5f);
                    var sec = SpawnTownPiece(townPrefab, "structure-floor",
                        new Vector3(p.x, deckYOf(p), p.y),
                        "Pier_Section_" + secI + (row == 1 ? "" : (row == 0 ? "_a" : "_b")));
                    if (sec == null) continue;
                    sec.transform.GetChild(0).localScale = new Vector3(secScale, secY, secScale);
                    AddPieceCollider(sec);
                }
            }

            // опоры: стволы от y=-1 до настила, по обоим краям каждые 4 м
            if (townPrefab.ContainsKey("tree-trunk"))
            {
                const float trunkH = 0.2608f; // высота ствола в GLB
                int postI = 0;
                for (float d = townR + 12f; d <= TerrainGen.IslandRadius + 12f; d += 4f)
                    for (int e = 0; e < 2; e++)
                    {
                        Vector2 p = townC + pierDir * d + perp * (e == 0 ? -2.5f : 2.5f);
                        var post = SpawnTownPiece(townPrefab, "tree-trunk",
                            new Vector3(p.x, -1f, p.y), "Pier_Post_" + (++postI));
                        if (post == null) continue;
                        post.transform.GetChild(0).localScale =
                            new Vector3(2.2f, (deckYOf(p) + 1f) / trunkH, 2.2f); // 0.44 м толщиной
                        AddPieceCollider(post);
                    }
            }

            // перила: fence-секции по обоим краям через секцию настила
            // (шаг 4 м), строго вертикально, высота ~1.2 м
            if (townPrefab.ContainsKey("fence"))
            {
                const float railScale = 2.4f; // секция 0.5 -> 1.2 м
                int railI = 0;
                for (float d = townR + 10f; d <= TerrainGen.IslandRadius + 12f; d += 4f)
                    for (int e = 0; e < 2; e++)
                    {
                        Vector2 p = townC + pierDir * d + perp * (e == 0 ? -2.5f : 2.5f);
                        var rail = SpawnTownPiece(townPrefab, "fence",
                            new Vector3(p.x, deckYOf(p) + 0.1f, p.y),
                            "Pier_Rail_" + (++railI), railScale);
                        if (rail == null) continue;
                        // полотно секции вдоль кромки: локальный X -> perp
                        rail.transform.rotation = Quaternion.Euler(0f,
                            Mathf.Atan2(-perp.y, perp.x) * Mathf.Rad2Deg, 0f);
                        AddPieceCollider(rail);
                    }
            }

            // конец причала: бочка и ящик с лутом на площадке разворота
            Vector2 endMid = townC + pierDir * (TerrainGen.IslandRadius + 13f);
            float endY = deckYOf(endMid);
            var pierBarrel = SpawnTownPiece(townPrefab, "barrel",
                new Vector3(endMid.x - perp.x * 1.2f, endY + secY * 0.5487f, endMid.y - perp.y * 1.2f),
                "Pier_Barrel", 2.5f);
            if (pierBarrel != null)
                pierBarrel.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
            var pierBox = SpawnTownPiece(townPrefab, "box-large-open",
                new Vector3(endMid.x + perp.x * 1.2f, endY + secY * 0.5487f, endMid.y + perp.y * 1.2f),
                "Pier_Box", 2.5f);
            if (pierBox != null)
                pierBox.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
            SpawnLootPoint(new Vector3(endMid.x, endY + 0.5f, endMid.y), "Loot_Pier_1", lootTable, 2);
        }

        // --- пещера и босс (S/пещера 2026-10-03) ---
        // Гора и вырез (зал r=16 + каньон-вход к центру острова) сделаны в
        // TerrainGen.HeightAt. Здесь — то, чего heightmap не умеет: свод
        // залы, стены коридора (гарантированный «коридор», даже если шум
        // сгладит склон), арка у устья и берлога. Материал один — мрачно-
        // синеватый камень, ночью в тумане гора читается чёрным силуэтом.
        var caveMat = CreateMaterial("CaveDark", new Color(0.09f, 0.09f, 0.11f));
        Vector2 mtC = TerrainGen.MountainCenter;
        Vector2 caveIn = TerrainGen.CaveInDir;               // луч входа: от вершины к центру острова
        Vector2 cavePerp = new Vector2(-caveIn.y, caveIn.x); // поперёк коридора

        // локальный помощник: проп пещеры, при отсутствии GLB — строка
        // в missing, как у остальных блоков
        GameObject CaveProp(string glb, Vector3 pos, string goName, float scale)
        {
            var go = SpawnModel(glb, pos, goName, scale);
            if (go == null) missing.AppendLine("cave: " + glb + ".glb");
            return go;
        }

        // свод залы: каменная плита над полом (CaveFloor+8.5, толщина 1 м,
        // радиус 17 — накрывает вырез залы r=16 с запасом). Коллайдер снят:
        // до потолка всё равно не допрыгнуть, лишний коллайдер только путает
        // рейкасты. Имя GO стабильное: на миникарту пещера не выносится
        // (мелко), но имена в сцене должны быть предсказуемыми.
        var caveCeiling = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        var caveCeilingCol = caveCeiling.GetComponent<CapsuleCollider>();
        if (caveCeilingCol != null) Object.DestroyImmediate(caveCeilingCol);
        caveCeiling.name = "Cave_Ceiling";
        caveCeiling.transform.position = new Vector3(mtC.x, TerrainGen.CaveFloor + 8.5f, mtC.y);
        caveCeiling.transform.localScale = new Vector3(34f, 0.5f, 34f);
        caveCeiling.GetComponent<MeshRenderer>().sharedMaterial = caveMat;

        // поверх свода — пара камней (декор, стоят на вершине свода,
        // OnGround-стиль по фиксированной высоте), чтобы вершина горы не
        // выглядела идеальной площадкой снаружи
        var summit0 = CaveProp("rock-b",
            new Vector3(mtC.x - 5f, TerrainGen.CaveFloor + 9f, mtC.y + 3f), "Cave_Summit_Rock_0", 5f);
        if (summit0 != null)
            summit0.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
        var summit1 = CaveProp("rock-c",
            new Vector3(mtC.x + 6f, TerrainGen.CaveFloor + 9f, mtC.y - 4f), "Cave_Summit_Rock_1", 4f);
        if (summit1 != null)
            summit1.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);

        // стены каньона: та же геометрия, что вырез в TerrainGen — вдоль
        // луча входа от 8 м до MountainRadius+6 от центра горы, шаг 4 м,
        // по 2 стены по бокам ±5.5 м от оси. Боксы 2.5×9×4 стоят от пола
        // (CaveFloor) до CaveFloor+9; коллайдеры не добавляем — у примитивов
        // свои, а рельеф вокруг и так высокий: боксы лишь гарантируют
        // «коридор», если шум сгладит склон.
        int wallI = 0;
        for (float d = 8f; d <= TerrainGen.MountainRadius + 6f + 0.01f; d += 4f)
        {
            wallI++;
            for (int side = 0; side < 2; side++)
            {
                float sgn = side == 0 ? -1f : 1f;
                Vector2 wp = mtC + caveIn * d + cavePerp * (sgn * 5.5f);
                var wallBox = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wallBox.name = "Cave_Wall_" + wallI + (side == 0 ? "_a" : "_b");
                wallBox.transform.position = new Vector3(wp.x, TerrainGen.CaveFloor + 4.5f, wp.y);
                // длинная ось бокса — вдоль коридора
                wallBox.transform.rotation =
                    Quaternion.LookRotation(new Vector3(caveIn.x, 0f, caveIn.y));
                wallBox.transform.localScale = new Vector3(2.5f, 9f, 4f);
                wallBox.GetComponent<MeshRenderer>().sharedMaterial = caveMat;
            }
        }

        // арка входа у устья каньона (на последней паре стен): камень слева,
        // камень справа, плоская плита сверху — приметные ворота берлоги
        Vector2 archC = mtC + caveIn * (TerrainGen.MountainRadius + 5f);
        var archL = CaveProp("rock-a",
            OnGround(archC.x + cavePerp.x * 6.2f, archC.y + cavePerp.y * 6.2f), "Cave_Arch_Left", 2.5f);
        if (archL != null)
            archL.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
        var archR = CaveProp("rock-b",
            OnGround(archC.x - cavePerp.x * 6.2f, archC.y - cavePerp.y * 6.2f), "Cave_Arch_Right", 2.5f);
        if (archR != null)
            archR.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
        CaveProp("rock-flat",
            new Vector3(archC.x, TerrainGen.CaveFloor + 9f, archC.y), "Cave_Arch_Top", 2.5f);

        // берлога внутри залы: валуны и куча «костей» (resource-stone)
        var boulder0 = CaveProp("rock-a", OnGround(mtC.x - 6f, mtC.y + 4f), "Cave_Boulder_0", 2.5f);
        if (boulder0 != null)
            boulder0.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
        var boulder1 = CaveProp("rock-c", OnGround(mtC.x + 5.5f, mtC.y + 5f), "Cave_Boulder_1", 2.2f);
        if (boulder1 != null)
            boulder1.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
        var boulder2 = CaveProp("rock-a", OnGround(mtC.x + 2.5f, mtC.y - 6.5f), "Cave_Boulder_2", 2f);
        if (boulder2 != null)
            boulder2.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
        Vector2[] boneOffs = {
            new Vector2(-2.6f, 2.4f), new Vector2(2.4f, 1.2f), new Vector2(0.7f, -3.1f)
        };
        for (int bone = 0; bone < boneOffs.Length; bone++)
        {
            var boneGo = CaveProp("resource-stone",
                OnGround(mtC.x + boneOffs[bone].x, mtC.y + boneOffs[bone].y),
                "Cave_Bone_" + bone, 1.3f + bone * 0.25f);
            if (boneGo != null)
                boneGo.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
        }

        // босс: сидит в центре залы; убит — не респавнится (состояние в
        // сейве: FatZombie.Restore/CaptureDead, сохранение ведёт параллельный
        // поток SaveSystem). Тело босс строит сам, как обычный Zombie.
        if (!FatZombie.Dead)
        {
            var bossGo = new GameObject("FatBoss");
            bossGo.transform.position = new Vector3(mtC.x, TerrainGen.CaveFloor + 0.2f, mtC.y);
            var bossCC = bossGo.AddComponent<CharacterController>();
            bossCC.height = 2.6f;
            bossCC.radius = 0.9f;
            bossCC.center = new Vector3(0f, 1.3f, 0f);
            var boss = bossGo.AddComponent<FatZombie>();
            boss.dropItem = meat; // с босса падает сырая плоть, как с зомби
            boss.pickupPrefab = pickupPrefabAsset;
        }

        // --- заброшенности по острову: обломки лагерей ---
        // Кольцо 0.25–0.40*Size. Точка бракуется, если в океане или ниже
        // уровня воды; до 24 попыток, не нашли сушу — руина пропускается.
        // Типы (box-open/signpost-single/resource-planks) прогреты вместе
        // с городком. В двух первых — целый ящик с лут-контейнером.
        int lootRuins = 0;
        int ruinsTotal = 6;
        for (int i = 0; i < ruinsTotal; i++)
        {
            Vector2 rp = Vector2.zero;
            bool found = false;
            for (int attempt = 0; attempt < 24; attempt++)
            {
                float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                float rr = Mathf.Lerp(0.25f * TerrainGen.Size, 0.40f * TerrainGen.Size,
                    (float)rng.NextDouble());
                float x = Mathf.Cos(ang) * rr, z = Mathf.Sin(ang) * rr;
                if (TerrainGen.IsInOcean(new Vector3(x, 1f, z))) continue;
                if (TerrainGen.HeightAt(x, z) < TerrainGen.SeaLevel + 0.5f) continue;
                rp = new Vector2(x, z);
                found = true;
                break;
            }
            if (!found) continue;

            var ruin = new GameObject("Abandoned_" + i);
            ruin.transform.position = OnGround(rp.x, rp.y);

            // в двух первых заброшенностях — целый ящик с лутом:
            // невидимый контейнер Loot_Ruins_N над визуалом
            if (lootRuins < 2 && townPrefab.ContainsKey("box-open"))
            {
                lootRuins++;
                var lootBox = SpawnTownPiece(townPrefab, "box-open",
                    OnGround(rp.x + 1.2f, rp.y - 0.8f), "Abandoned_" + i + "_LootBox", 2.5f);
                if (lootBox != null)
                    lootBox.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                SpawnLootPoint(OnGround(rp.x + 1.2f, rp.y - 0.8f, 0.5f),
                    "Loot_Ruins_" + lootRuins, lootTable, 2);
            }

            int props = 2 + rng.Next(0, 3); // 2-4 предмета
            for (int pi = 0; pi < props; pi++)
            {
                float ang2 = (float)rng.NextDouble() * Mathf.PI * 2f;
                float off = 1f + (float)rng.NextDouble() * 3f;
                float px = rp.x + Mathf.Cos(ang2) * off;
                float pz = rp.y + Mathf.Sin(ang2) * off;
                switch (rng.Next(0, 5))
                {
                    case 0: // перевёрнутый ящик: на боку или вверх дном
                        var box = SpawnTownPiece(townPrefab, "box-open",
                            OnGround(px, pz), "Abandoned_" + i + "_Box", 2.5f);
                        if (box != null)
                        {
                            box.transform.SetParent(ruin.transform, true);
                            box.transform.rotation = Quaternion.Euler(0f,
                                (float)rng.NextDouble() * 360f, 90f + (float)rng.NextDouble() * 70f);
                        }
                        break;
                    case 1:
                        var bar = SpawnTownPiece(townPrefab, "barrel-open",
                            OnGround(px, pz), "Abandoned_" + i + "_Barrel", 2.5f);
                        if (bar != null)
                        {
                            bar.transform.SetParent(ruin.transform, true);
                            bar.transform.rotation = Quaternion.Euler(0f,
                                (float)rng.NextDouble() * 360f, 0f);
                        }
                        break;
                    case 2:
                        var sign = SpawnTownPiece(townPrefab, "signpost-single",
                            OnGround(px, pz), "Abandoned_" + i + "_Sign", 4f);
                        if (sign != null) sign.transform.SetParent(ruin.transform, true);
                        break;
                    case 3: // пара обломков стены под углами
                        for (int wi = 0; wi < 2; wi++)
                        {
                            var deb = SpawnTownPiece(townPrefab, "structure-metal-wall",
                                OnGround(px + wi * 1.2f, pz + wi * 0.4f),
                                "Abandoned_" + i + "_Wall" + wi, 3f);
                            if (deb != null)
                            {
                                deb.transform.SetParent(ruin.transform, true);
                                deb.transform.rotation = Quaternion.Euler(0f,
                                    (float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 35f);
                            }
                        }
                        break;
                    default: // штабель досок
                        for (int pl = 0; pl < 2; pl++)
                        {
                            var plk = SpawnTownPiece(townPrefab, "resource-planks",
                                OnGround(px + pl * 0.4f, pz + pl * 0.2f),
                                "Abandoned_" + i + "_Planks" + pl, 3f);
                            if (plk != null)
                            {
                                plk.transform.SetParent(ruin.transform, true);
                                plk.transform.rotation = Quaternion.Euler(0f,
                                    (float)rng.NextDouble() * 360f, 0f);
                            }
                        }
                        break;
                }
            }
        }

        // --- зомби-спавнер (M5): 6 зомби каждую ночь кольцом вокруг игрока,
        // +1 за каждую ночь до 14 (автор просил больше зомби) ---
        var spawnerGo = new GameObject("ZombieSpawner");
        var spawner = spawnerGo.AddComponent<ZombieSpawner>();
        spawner.dropItem = meat; // с зомби падает сырая плоть
        spawner.pickupPrefab = pickupPrefabAsset;

        // --- куры (§9.3): четыре, по одной на квандрант карты — «в разных
        // частях карты» (S/баланс 2026-10-03; раньше была одна). Яйцо раз
        // в 3 мин на курицу (layInterval в Chicken), не больше 3 штук
        // вокруг — четыре курицы делают домашний майонез стабильным планом
        // на еду, а не одной лотереей в глуши. Точка: диагональ квандранта
        // ±35°, r 200–340 (rng), взаимная дистанция ≥120 м, до 16 попыток;
        // брак — океан, пляж/пол пещеры (HeightAt ≤ CaveFloor), лужа,
        // городок и вырез горы (тот же предикат, что в RandomPos).
        var chickenPts = new Vector2[4];
        int chickenCount = 0;
        for (int q = 0; q < 4; q++)
        {
            float sx = (q & 1) == 0 ? 1f : -1f;
            float sz = q < 2 ? 1f : -1f;
            float baseAng = Mathf.Atan2(sz, sx); // диагональ квандранта (±45°/±135°)
            for (int attempt = 0; attempt < 16; attempt++)
            {
                float ang = baseAng + ((float)rng.NextDouble() - 0.5f) * 70f * Mathf.Deg2Rad;
                float r = Mathf.Lerp(200f, 340f, (float)rng.NextDouble());
                float x = Mathf.Cos(ang) * r, z = Mathf.Sin(ang) * r;
                var p = new Vector2(x, z);
                if (TerrainGen.IsInOcean(new Vector3(x, 1f, z))) continue;
                if (TerrainGen.HeightAt(x, z) <= TerrainGen.CaveFloor) continue;
                if (Vector2.Distance(p, TerrainGen.LakeCenter) < TerrainGen.LakeRadius + 2f) continue;
                if (Vector2.Distance(p, TerrainGen.TownCenter) < TerrainGen.TownRadius + 2f) continue;
                if (TerrainGen.HeightAt(x, z) < TerrainGen.CaveFloor + 2f
                    && Vector2.Distance(p, TerrainGen.MountainCenter) < TerrainGen.MountainRadius + 6f)
                    continue; // вырез пещеры в горе
                bool tooClose = false;
                for (int prev = 0; prev < chickenCount; prev++)
                    if (Vector2.Distance(p, chickenPts[prev]) < 120f) { tooClose = true; break; }
                if (tooClose) continue;
                chickenPts[chickenCount++] = p;
                break;
            }
        }
        for (int i = 0; i < chickenCount; i++)
        {
            var chickenGo = new GameObject("Chicken_" + (i + 1));
            chickenGo.transform.position = OnGround(chickenPts[i].x, chickenPts[i].y);
            var chickenCC = chickenGo.AddComponent<CharacterController>();
            chickenCC.height = 1f; chickenCC.radius = 0.35f; chickenCC.center = new Vector3(0f, 0.5f, 0f);
            var chicken = chickenGo.AddComponent<Chicken>();
            chicken.eggItem = egg;
            chicken.pickupPrefab = pickupPrefabAsset;
        }
        if (chickenCount < 4)
            missing.AppendLine($"chickens: размещено {chickenCount} из 4 (не нашлось точек в квандрантах)");

        // --- рецепт домашнего майонеза (§9.3): яйца x2 ---
        SyncRecipe("mayo", mayo, 1, (egg, 2));

        // --- сохранить сцену и добавить в Build Settings ---
        EnsureFolder("Assets/Scenes");
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Main.unity");
        // Main должна быть первой в списке, прочие сцены билда не роняем
        var buildScenes = new List<EditorBuildSettingsScene>
            { new EditorBuildSettingsScene("Assets/Scenes/Main.unity", true) };
        foreach (var s in EditorBuildSettings.scenes)
            if (s.path != "Assets/Scenes/Main.unity")
                buildScenes.Add(s);
        EditorBuildSettings.scenes = buildScenes.ToArray();
        AssetDatabase.SaveAssets();

        // NewScene(Single) создала сцену без Hud — довешиваем его сразу,
        // чтобы не требовался отдельный запуск меню «Setup HUD»
        // (quiet: итоговый диалог — один, в конце этого Run)
        SetupHud.Run(quiet: true);

        // Иконки предметов тоже здесь: repair Campfire.prefab обновляет
        // placeablePrefab, иконки должны перепечься в тот же заход —
        // иначе забудется, и слот костра останется пустым навсегда
        IconBaker.Run(quiet: true);

        Debug.Log($"[Survival] Сцена Main собрана и сохранена. Жми Play!");
        string msg = missing.Length == 0
            ? "Сцена Main собрана!\nЖми Play и тестируй."
            : "Сцена собрана, но эти модели НЕ найдены:\n" + missing +
              "\nСкопируй мне текст красных/жёлтых строк из Console.";
        EditorUtility.DisplayDialog("Survival", msg, "Ок");
    }

    // Создаёт ItemData-ассет ИЛИ перезаписывает поля существующего —
    // повторный запуск Setup приводит баланс к значениям из кода.
    static ItemData SyncItem(string id, string displayName, int maxStack,
        float food = 0f, float water = 0f, float heal = 0f,
        ItemData consumeReturns = null, bool isTool = false, float toolDamage = 0f,
        bool isPlaceable = false, string worldModel = null, GameObject placeablePrefab = null,
        float poison = 0f)
    {
        // Предметы живут в Resources/Items — SaveSystem грузит их по id
        // рантаймом. Старые ассеты из Assets/Items переносим, не создавая дублей.
        EnsureFolder("Assets/Resources/Items");
        string path = $"Assets/Resources/Items/{id}.asset";
        string oldPath = $"Assets/Items/{id}.asset";
        if (AssetDatabase.LoadAssetAtPath<ItemData>(path) == null
            && AssetDatabase.LoadAssetAtPath<ItemData>(oldPath) != null)
        {
            // цель могла появиться между проверками (дубль-файл) —
            // MoveAsset тогда падает: вместо падения удаляем устаревший источник
            string moveErr = AssetDatabase.MoveAsset(oldPath, path);
            if (!string.IsNullOrEmpty(moveErr))
            {
                Debug.LogWarning($"[Survival] «{id}»: MoveAsset не удался ({moveErr}) — удаляю дубль-источник {oldPath}.");
                AssetDatabase.DeleteAsset(oldPath);
            }
        }
        var item = AssetDatabase.LoadAssetAtPath<ItemData>(path);
        bool created = item == null;
        if (created) item = ScriptableObject.CreateInstance<ItemData>();

        item.id = id;
        item.displayName = displayName;
        item.maxStack = maxStack;
        item.foodRestore = food;
        item.waterRestore = water;
        item.healAmount = heal;
        item.poisonAmount = poison;
        item.consumeReturns = consumeReturns;
        item.isTool = isTool;
        item.toolDamage = toolDamage;
        item.isPlaceable = isPlaceable;
        if (worldModel != null)
        {
            // модель перезаписываем ТОЛЬКО при успешной загрузке —
            // иначе сбой импорта GLB затирал бы worldModel в null
            var wm = LoadModel(worldModel);
            if (wm != null) item.worldModel = wm;
            else Debug.LogWarning($"[Survival] worldModel «{worldModel}» не загрузился — у «{id}» оставлено прежнее значение.");
        }
        if (placeablePrefab != null) item.placeablePrefab = placeablePrefab;

        if (created) AssetDatabase.CreateAsset(item, path);
        else EditorUtility.SetDirty(item);
        return item;
    }

    static Material CreateMaterial(string name, Color color)
    {
        EnsureFolder("Assets/Materials");
        string path = $"Assets/Materials/{name}.mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        var mat = new Material(Shader.Find("Standard")) { color = color };
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    // GLB грузим с обходными путями: сначала принудительный синхронный
    // реимпорт с перехватом ошибок, затем LoadAssetAtPath, любой GameObject
    // внутри ассета, в последнюю очередь — поиск по проекту.
    static GameObject LoadModel(string name)
    {
        string path = $"Assets/Models/{name}.glb";

        // перехватываем предупреждения/ошибки, которые может кинуть импортёр
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

        var all = AssetDatabase.LoadAllAssetsAtPath(path);
        var types = new System.Text.StringBuilder();
        foreach (var a in all) types.Append(a.GetType().Name).Append(", ");
        Debug.LogWarning($"[Survival] Модель не найдена: {path}. Ассетов внутри: {all.Length} ({types}). " +
                         $"Ошибки импорта: {(captured.Length == 0 ? "нет (молча пусто)" : captured.ToString())}");
        return null;
    }

    // Возвращает НЕмасштабированный корень-обёртку; модель — ребёнок
    // с localScale=scale (как в Godot: коллайдеры на корне не растут
    // вместе с моделью, иначе капсула дерева становится ~1 м толщиной).
    // Точка на рельефе (x, z) с подъёмом на up над поверхностью.
    static Vector3 OnGround(float x, float z, float up = 0f)
        => new Vector3(x, TerrainGen.HeightAt(x, z) + up, z);

    // Случайная точка кольца вокруг спауна, прочь от озера (seed → rng).
    static Vector3 RandomPos(System.Random rng, float minR, float maxR)
    {
        for (int tries = 0; tries < 32; tries++)
        {
            float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
            float r = Mathf.Lerp(minR, maxR, (float)rng.NextDouble());
            float x = Mathf.Cos(ang) * r, z = Mathf.Sin(ang) * r;
            if (Vector2.Distance(new Vector2(x, z), TerrainGen.LakeCenter) < TerrainGen.LakeRadius + 2f)
                continue;
            // случайная растительность/пропсы не растут в городке — иначе
            // дерево прорастало сквозь дом (фидбек плейтеста 2026-10-03)
            if (Vector2.Distance(new Vector2(x, z), TerrainGen.TownCenter) < TerrainGen.TownRadius + 2f)
                continue;
            // вырез пещеры в горе: низина внутри горы — это пол залы или
            // каньон-вход, деревьям/камням там не место (S/пещера 2026-10-03)
            if (TerrainGen.HeightAt(x, z) < TerrainGen.CaveFloor + 2f
                && Vector2.Distance(new Vector2(x, z), TerrainGen.MountainCenter)
                    < TerrainGen.MountainRadius + 6f)
                continue;
            return OnGround(x, z);
        }
        return OnGround(minR, 0f);
    }

    static GameObject SpawnModel(string name, Vector3 pos, string goName, float scale = 1f)
    {
        var prefab = LoadModel(name);
        if (prefab == null) return null;
        var root = new GameObject(goName);
        root.transform.position = pos;
        var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        model.transform.SetParent(root.transform, false);
        model.transform.localScale = Vector3.one * scale; // Kenney-модели мелкие, масштабы — как в Godot-версии
        return root;
    }

    // Кусок городка/руин из прогретого словаря (townPrefab в Run):
    // LoadModel дёргается один раз на тип, а не на каждый экземпляр.
    // null = GLB не нашёлся; строка в missing уже записана при прогреве,
    // здесь молча пропускаем.
    static GameObject SpawnTownPiece(Dictionary<string, GameObject> prefabs,
        string glb, Vector3 pos, string goName, float scale = 1f)
    {
        if (!prefabs.TryGetValue(glb, out var prefab)) return null;
        var root = new GameObject(goName);
        root.transform.position = pos;
        var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        model.transform.SetParent(root.transform, false);
        model.transform.localScale = Vector3.one * scale;
        return root;
    }

    // Коллайдер-бокс по реальным габаритам куска: локальные bounds мешей
    // проводим через TransformPoint/InverseTransformPoint — точный бокс
    // выходит при любом повороте корня (наклонённая стена руин), чего
    // не даёт честный renderer.bounds (мировой AABB вращённой панели
    // раздут). minThickness: самую тонкую горизонтальную ось не даём
    // сделать тоньше — листовая стенка иначе прощупывается насквозь.
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
            if (size.x <= size.z && size.x < minThickness) size.x = minThickness;
            else if (size.z < size.x && size.z < minThickness) size.z = minThickness;
        }
        col.size = size;
    }

    // Дверной проём: сплошной бокс по габаритам запер бы дверь. По замеру
    // GLB (панель x -0.2677..0.2677, глубина z -0.2677..-0.1788, проём
    // |x| < 0.14 до y = 0.4875) ставим два косяка и перемычку над дверью
    // (верх проёма 2.93 м при масштабе 6 — игрок проходит свободно).
    static void AddDoorwayColliders(GameObject door)
    {
        const float s = 6f;
        const float zC = -0.2233f * s; // центр полотна по глубине
        const float zS = 0.0889f * s;  // глубина панели
        foreach (float sx in new[] { -1f, 1f })
        {
            var jamb = door.AddComponent<BoxCollider>();
            jamb.center = new Vector3(sx * (0.14f + 0.2677f) * 0.5f * s, 0.25f * s, zC);
            jamb.size = new Vector3((0.2677f - 0.14f) * s, 0.5f * s, zS);
        }
        var head = door.AddComponent<BoxCollider>();
        head.center = new Vector3(0f, (0.4875f + 0.5175f) * 0.5f * s, zC);
        head.size = new Vector3(0.3126f * s, 0.03f * s, zS);
    }

    // Точка лута: невидимый контейнер LootContainer под обычным (НЕ
    // триггер) боксом — луч взаимодействия бьёт по коллайдеру, как у
    // WaterSource. Имя GO («Loot_Town_1», «Loot_Pier_1», «Loot_Ruins_2»…)
    // по которому сейв запоминает обысканность.
    static void SpawnLootPoint(Vector3 pos, string goName, ItemData[] loot, int rolls)
    {
        var go = new GameObject(goName);
        go.transform.position = pos;
        var col = go.AddComponent<BoxCollider>();
        col.size = new Vector3(0.6f, 0.6f, 0.6f);
        var lc = go.AddComponent<LootContainer>();
        lc.loot = loot;
        lc.rolls = rolls;
    }

    // Подбираемый предмет на земле (пока все выглядят бревном-заглушкой)
    static void SpawnPickup(PickupItem prefabComponent, ItemData item, int count, Vector3 pos)
    {
        if (prefabComponent == null || item == null) return;
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefabComponent.gameObject);
        go.transform.position = pos;
        go.name = "Pickup_" + item.id;
        var pk = go.GetComponent<PickupItem>();
        pk.item = item;
        pk.count = count;
    }

    // Постройка «Костёр»: модель + коллайдер + дрожащий свет.
    static GameObject CreateCampfirePrefab()
    {
        EnsureFolder("Assets/Prefabs/Placeables");
        string path = "Assets/Prefabs/Placeables/Campfire.prefab";
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null)
        {
            // Префаб мог быть создан раньше без модели (тихий сбой импорта
            // GLB) или до появления Campfire.cs — чиним оба случая,
            // иначе костёр невидим в мире и иконка пустая.
            bool dirty = false;
            var rootGo = PrefabUtility.LoadPrefabContents(path);
            if (rootGo.GetComponent<Campfire>() == null)
            {
                rootGo.AddComponent<Campfire>();
                dirty = true;
            }
            if (rootGo.GetComponentInChildren<MeshRenderer>() == null)
            {
                var mdl = LoadModel("campfire-pit");
                if (mdl != null)
                {
                    var inst = (GameObject)PrefabUtility.InstantiatePrefab(mdl);
                    inst.transform.SetParent(rootGo.transform, false);
                    inst.transform.localScale = Vector3.one * 3.5f;
                    dirty = true;
                }
                else Debug.LogWarning("[Survival] Campfire.prefab без модели, и campfire-pit.glb недоступен.");
            }
            if (dirty)
                existing = PrefabUtility.SaveAsPrefabAsset(rootGo, path);
            PrefabUtility.UnloadPrefabContents(rootGo);
            return existing;
        }

        var root = new GameObject("Campfire");
        var model = LoadModel("campfire-pit");
        if (model != null)
        {
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            inst.transform.SetParent(root.transform, false);
            inst.transform.localScale = Vector3.one * 3.5f;
        }
        var col = root.AddComponent<CapsuleCollider>();
        col.center = new Vector3(0f, 0.25f, 0f);
        col.radius = 0.7f;
        col.height = 0.5f;
        root.AddComponent<Campfire>(); // тепло + готовка (рецепты из Resources)

        var glow = new GameObject("Glow");
        glow.transform.SetParent(root.transform, false);
        glow.transform.localPosition = new Vector3(0f, 0.7f, 0f);
        var light = glow.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.6f, 0.25f);
        light.range = 9f;
        light.intensity = 2f;
        glow.AddComponent<FireLight>();

        return PrefabUtility.SaveAsPrefabAsset(root, path);
    }

    // Постройка «Стена»: короб 2.5×2.5×0.25 (стиль Godot-версии).
    static GameObject CreateWallPrefab()
    {
        EnsureFolder("Assets/Prefabs/Placeables");
        string path = "Assets/Prefabs/Placeables/Wall.prefab";
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null) return existing;

        var root = new GameObject("Wall");
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "Model";
        cube.transform.SetParent(root.transform, false);
        cube.transform.localPosition = new Vector3(0f, 1.25f, 0f);
        cube.transform.localScale = new Vector3(2.5f, 2.5f, 0.25f);
        cube.GetComponent<MeshRenderer>().sharedMaterial =
            CreateMaterial("Wood", new Color(0.55f, 0.4f, 0.22f));
        // коллайдер у примитива свой; дублировать на root не нужно

        return PrefabUtility.SaveAsPrefabAsset(root, path);
    }

    // Рецепт-ассет в Resources/Recipes (UI грузит LoadAll). Отдельно от
    // RecipeBaker'а потока В — не конфликтуем, id не пересекаются.
    static void SyncRecipe(string id, ItemData result, int resultCount, params (ItemData item, int n)[] inputs)
    {
        EnsureFolder("Assets/Resources/Recipes");
        string path = $"Assets/Resources/Recipes/{id}.asset";
        var recipe = AssetDatabase.LoadAssetAtPath<RecipeData>(path);
        bool created = recipe == null;
        if (created) recipe = ScriptableObject.CreateInstance<RecipeData>();
        recipe.result = result;
        recipe.resultCount = resultCount;
        var ings = new List<RecipeData.Ingredient>();
        foreach (var (item, n) in inputs)
            ings.Add(new RecipeData.Ingredient { item = item, count = n });
        recipe.inputs = ings.ToArray();
        if (created) AssetDatabase.CreateAsset(recipe, path);
        else EditorUtility.SetDirty(recipe);
    }

    // ---------- небо и майонез-модели (S/красота 2026-10-03) ----------

    const string SkyShaderSource = @"
Shader ""Sky/SkyUnlit"" {
    Properties { _MainTex (""Tex"", 2D) = ""white"" {} _Color (""Tint"", Color) = (1,1,1,1) }
    SubShader {
        Tags { ""Queue""=""Transparent"" ""IgnoreProjector""=""True"" ""RenderType""=""Transparent"" ""ForceNoShadowCasting""=""True"" }
        ZWrite Off Cull Off
        Blend SrcAlpha OneMinusSrcAlpha
        Pass {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include ""UnityCG.cginc""
            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 frag (v2f_img i) : SV_Target { return tex2D(_MainTex, i.uv) * _Color; }
            ENDCG
        }
    }
    Fallback Off
}";

    // Шейдер неба храним ТЕКСТОМ исходника: прошлый вариант
    // (ShaderUtil.CreateShaderAsset + CreateAsset) записывал в .shader
    // сериализованный Shader-объект — в той же сессии он работал, но при
    // следующем переимпорте ShaderImporter парсил файл как исходник и
    // падал (parse error line 1): солнце/луна/облака становились
    // фиолетовыми после перезапуска редактора. Текст импортируется всегда.
    static Shader EnsureSkyShader()
    {
        const string path = "Assets/Shaders/SkyUnlit.shader";
        EnsureFolder("Assets/Shaders");
        if (!File.Exists(path)
            || !File.ReadAllText(path).TrimStart().StartsWith("Shader"))
            File.WriteAllText(path, SkyShaderSource);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
        if (shader == null || ShaderUtil.ShaderHasError(shader))
            Debug.LogError("[Survival] SkyUnlit.shader не скомпилировался — солнце/луна/облака будут фиолетовыми. Текст ошибки в Console выше.");
        return shader;
    }

    // Волновой шейдер океана (S/океан 2026-10-03). Храним ТЕКСТОМ
    // исходника по той же причине, что и SkyUnlit: сериализованный
    // Shader-объект в .shader ломает переимпорт (см. EnsureSkyShader).
    const string MayoWavesShaderSource = @"
Shader ""Mayo/MayoWaves"" {
    Properties { _MainTex (""Swirl"", 2D) = ""white"" {} _Color (""Tint"", Color) = (1,1,1,1) }
    SubShader {
        Tags { ""RenderType""=""Opaque"" }
        CGPROGRAM
        #pragma surface surf Lambert vertex:vert addshadow
        sampler2D _MainTex;
        fixed4 _Color;
        struct Input { float2 uv_MainTex; float2 slope; };
        void vert (inout appdata_full v, out Input o) {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;
            // две бегущие волны ~47 м и ~31 м, фаза от мира — вершин не жалко
            float p1 = wp.x * 0.134f + _Time.y * 1.7f;
            float p2 = wp.z * 0.203f + wp.x * 0.05f + _Time.y * 1.1f;
            v.vertex.y += sin(p1) * 0.22f + sin(p2) * 0.16f;
            // наклон поверхности = сумма производных — нормаль для света
            o.slope = 0.22f * 0.134f * cos(p1) + 0.16f * 0.05f * cos(p2);
            o.slope = float2(o.slope, 0.16f * 0.203f * cos(p2));
        }
        void surf (Input IN, inout SurfaceOutput o) {
            // разводы медленно плывут — поверхность живая даже в штиль
            float2 uv = IN.uv_MainTex + float2(_Time.x * 0.006f, _Time.x * 0.0035f);
            o.Albedo = tex2D(_MainTex, uv).rgb * _Color.rgb;
            o.Normal = normalize(float3(-IN.slope.x, 1.0f, -IN.slope.y));
            o.Alpha = 1.0;
        }
        ENDCG
    }
    Fallback ""Diffuse""
}";

    static Shader EnsureMayoWavesShader()
    {
        const string path = "Assets/Shaders/MayoWaves.shader";
        EnsureFolder("Assets/Shaders");
        if (!File.Exists(path)
            || !File.ReadAllText(path).TrimStart().StartsWith("Shader"))
            File.WriteAllText(path, MayoWavesShaderSource);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
        if (shader == null || ShaderUtil.ShaderHasError(shader))
            Debug.LogError("[Survival] MayoWaves.shader не скомпилировался — океан останется на Standard без волн. Текст ошибки в Console выше.");
        return shader;
    }

    // Сетка океана: квадрат 1414×1414 м с центром (0,0) — покрывает карту
    // 1000 и все углы (707 м от центра). Топология как у чанков
    // TerrainGen: ряды по Z, тот же порядок обхода треугольников.
    // UV 0..1 — по ним плывут разводы MayoSwirl.
    static void FillSeaGrid(Mesh mesh, int res)
    {
        mesh.Clear();
        int side = res + 1;
        var verts = new Vector3[side * side];
        var uvs = new Vector2[side * side];
        const float half = 707.1f; // половина квадрата
        for (int z = 0; z < side; z++)
        for (int x = 0; x < side; x++)
        {
            verts[z * side + x] = new Vector3(
                -half + 2f * half * x / res, 0f, -half + 2f * half * z / res);
            uvs[z * side + x] = new Vector2((float)x / res, (float)z / res);
        }
        var tris = new int[res * res * 6];
        int t = 0;
        for (int z = 0; z < res; z++)
        for (int x = 0; x < res; x++)
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

    // Свежий меш океана — для первого создания ассета MayoSeaGrid.asset.
    static Mesh MayoSeaGrid(int res)
    {
        var mesh = new Mesh { name = "MayoSeaGrid" };
        FillSeaGrid(mesh, res);
        return mesh;
    }

    static Texture2D EnsureTexture(string path, System.Func<Color32[]> painter, bool repeat = false)
    {
        if (!File.Exists(path))
        {
            var px = painter();
            int n = (int)Mathf.Sqrt(px.Length);
            var gen = new Texture2D(n, n, TextureFormat.RGBA32, false, false);
            gen.SetPixels32(px);
            gen.Apply();
            File.WriteAllBytes(path, gen.EncodeToPNG());
            Object.DestroyImmediate(gen);
        }
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti != null)
        {
            ti.mipmapEnabled = false;
            ti.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            ti.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // Солнце: тёплое ядро + мягкое свечение по краю.
    static Color32[] SunDiscPixels()
    {
        const int N = 256;
        var px = new Color32[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float r = Vector2.Distance(new Vector2(x, y), new Vector2(N, N) * 0.5f) / (N * 0.5f);
                float core = 1f - Smooth(0.30f, 0.36f, r);
                float glow = (1f - Smooth(0.36f, 1f, r)) * 0.35f;
                float a = Mathf.Max(core, glow);
                var c = new Color(1f, 0.94f, 0.74f) * Mathf.Lerp(0.85f, 1.05f, core);
                px[y * N + x] = Color32.Lerp(new Color32(0, 0, 0, 0), (Color32)c, a);
            }
        return px;
    }

    // Луна: холодный диск с парой тёмных пятен-морей.
    static Color32[] MoonDiscPixels()
    {
        const int N = 256;
        var px = new Color32[N * N];
        var craters = new[] {
            new Vector2(0.42f, 0.60f), new Vector2(0.60f, 0.38f), new Vector2(0.38f, 0.36f)
        };
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                var uv = new Vector2(x, y) / N;
                float r = Vector2.Distance(uv, Vector2.one * 0.5f) / 0.5f;
                float a = Mathf.Max(1f - Smooth(0.33f, 0.38f, r),
                                    (1f - Smooth(0.4f, 1f, r)) * 0.12f);
                var c = new Color(0.85f, 0.89f, 1f);
                foreach (var k in craters)
                    c *= Mathf.Lerp(1f, 0.82f, 1f - Smooth(0.03f, 0.07f, Vector2.Distance(uv, k)));
                px[y * N + x] = Color32.Lerp(new Color32(0, 0, 0, 0), (Color32)(c * 1f), a);
            }
        return px;
    }

    // Облака: тайловый value-noise, мягкая пороговая альфа.
    static Color32[] CloudPixels()
    {
        const int N = 512;
        var px = new Color32[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = (float)x / N, v = (float)y / N;
                float n = 0.55f * VNoise(u, v, 6) + 0.30f * VNoise(u, v, 12) + 0.15f * VNoise(u, v, 24);
                float a = Smooth(0.52f, 0.75f, n) * 0.85f;
                px[y * N + x] = new Color(1f, 1f, 1f, a);
            }
        return px;
    }

    // Майонезные разводы океана: непрозрачный (альфа 255) тайловый
    // value-noise — тёмные прожилки и светлые пятна на кремовой базе.
    static Color32[] MayoSwirlPixels()
    {
        const int N = 512;
        var px = new Color32[N * N];
        var baseC = new Color(0.93f, 0.88f, 0.62f);
        var dark = new Color(0.78f, 0.70f, 0.44f);
        var light = new Color(0.98f, 0.95f, 0.80f);
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = (float)x / N, v = (float)y / N;
                float n = 0.6f * VNoise(u, v, 5) + 0.4f * VNoise(u, v, 11);
                float m = 0.55f * VNoise(u + 0.33f, v + 0.71f, 8)
                        + 0.45f * VNoise(u + 0.13f, v + 0.47f, 17);
                var c = Color.Lerp(baseC, dark, Smooth(0.38f, 0.75f, n) * 0.8f);
                c = Color.Lerp(c, light, Smooth(0.5f, 0.85f, m) * 0.7f);
                px[y * N + x] = c;
            }
        return px;
    }

    static float VNoise(float u, float v, int cells)
    {
        float x = u * cells, y = v * cells;
        int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
        float fx = x - ix, fy = y - iy;
        fx = fx * fx * (3f - 2f * fx);
        fy = fy * fy * (3f - 2f * fy);
        float a = VHash(ix, iy, cells), b = VHash(ix + 1, iy, cells);
        float c = VHash(ix, iy + 1, cells), d = VHash(ix + 1, iy + 1, cells);
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
    }

    static float VHash(int x, int y, int cells)
    {
        // целочисленный хеш с заворотом — облака тайлятся без швов
        x = ((x % cells) + cells) % cells;
        y = ((y % cells) + cells) % cells;
        int h = x * 374761393 + y * 668265263;
        h = (h ^ (h >> 13)) * 1274126177;
        return ((h ^ (h >> 16)) & 0xffff) / 65535f;
    }

    static float Smooth(float a, float b, float x)
    {
        float t = Mathf.Clamp01((x - a) / (b - a));
        return t * t * (3f - 2f * t);
    }

    // Белая «майонезная» версия модели: клон GLB, все материалы —
    // матовый майонез. Сохраняем префабом — годится и для иконки,
    // и для вида в руке.
    static GameObject MakeWhiteModel(string glbName, string assetName)
    {
        var src = LoadModel(glbName);
        if (src == null) return null;
        // Обычный клон, НЕ PrefabUtility.InstantiatePrefab: инстанс GLB
        // сохраняется как ВАРИАНТ, а подменённый материал (не ассет)
        // сериализуется в модификации ссылкой {fileID: 0} — рендерер
        // оставался без материала, и мясо было фиолетовым.
        var go = Object.Instantiate(src);
        // Материал — ОТДЕЛЬНЫЙ АССЕТ: рантайм-материал при сохранении
        // префаба терялся (в m_Materials оставался {fileID: 0}), и слот
        // рендерил мадженту. Присваиваем массивом: у GLB-мешей несколько
        // субмешей, любой пустой слот даёт фиолетовый.
        var white = CreateMaterial("MeatMayo", new Color(0.96f, 0.96f, 0.90f));
        white.SetFloat("_Glossiness", 0.25f);
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            var slots = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < slots.Length; i++) slots[i] = white;
            r.sharedMaterials = slots;
        }
        EnsureFolder("Assets/Prefabs/Items");
        var path = $"Assets/Prefabs/Items/{assetName}.prefab";
        var asset = PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
        return asset;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        string leaf = System.IO.Path.GetFileName(path);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }
}
