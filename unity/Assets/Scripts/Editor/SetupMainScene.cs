using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Один раз собирает и сохраняет сцену Assets/Scenes/Main.unity:
// круглый остров 64 чанками в океане протухшего майонеза, солнце с
// DayNight, игрок (капсула + Player/Inventory/Stats, Head с камерой),
// DebugHud, деревья и камни как ResourceNode (базово 50/18 шт,
// масштабируются от TerrainGen.Size — см. блок рассеивания), городок
// «Гнилой Причал» с причалом и заброшенностями, силуэты-«сторожа»
// кольцом на суше и две пасхалки §9.7 (Monument_Bucket, MayoMonolith)
// как чистый визуал. ItemData-ассеты (древесина/камень/плоть/
// фляги/яйцо/майонез; ягоды и их кусты убраны автором 2026-10-03,
// ассет berry.asset остаётся в Resources как балласт — удалить руками
// при желании).
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

        // --- океан протухшего майонеза: диск на уровне моря ---
        // Радиус 707.1 м покрывает квадрат 1000×1000 до самых углов. Никаких
        // невидимых стенок — упасть в океан можно, выплыть нельзя (смерть
        // в Player.cs). Коллайдер у поверхности убран, как у лужи.
        var seaRoot = new GameObject("MayoSea");
        var seaSurface = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        seaSurface.name = "Surface";
        seaSurface.transform.SetParent(seaRoot.transform, false);
        seaSurface.transform.localScale = new Vector3(1414.2f, 0.02f, 1414.2f);
        // верх диска на сантиметр НИЖЕ уровня моря: плато спауна стоит ровно
        // на 0 (SeaLevel), копланарные поверхности мерцали бы z-fighting'ом
        seaSurface.transform.localPosition = new Vector3(0f, -0.03f, 0f);
        var seaCol = seaSurface.GetComponent<CapsuleCollider>();
        if (seaCol != null) Object.DestroyImmediate(seaCol);
        // свой материал: лужа (Mayo.mat) и океан должны отличаться
        var seaMat = CreateMaterial("MayoSea", new Color(0.64f, 0.58f, 0.28f));
        seaMat.SetFloat("_Glossiness", 0.5f); // глянец мёртвой воды; ассет
                                              // перезаписывается Setup'ом — ок
        seaSurface.GetComponent<MeshRenderer>().sharedMaterial = seaMat;

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
        SyncItem("cooked_meat", "Котлета", 10, food: 45f, poison: 8f, worldModel: "meat-patty");         // готовка режет яд
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

        // --- городок «Гнилой Причал» (S/мир 2026-10) ---
        // Терраса под городом плоская (TerrainGen.TownHeight), всё ставится
        // OnGround без ручного подъёма. Чистый декор без интерактива:
        // лут-руины — отдельный этап.
        Vector2 townC = TerrainGen.TownCenter;
        float townR = townC.magnitude;
        Vector2 pierDir = townC.normalized; // от центра острова наружу — туда причал

        // прогрев кусков городка: LoadModel синхронно реимпортирует GLB,
        // на 60+ кусков дешевле один проход. Тип без ассета пишет одну
        // строку в missing, и составные блоки с ним пропускаются целиком.
        string[] townPieces = {
            "structure-floor", "tree-trunk", "structure-metal-wall", "structure-metal-doorway",
            "structure-metal-roof", "bedroll", "signpost", "barrel", "barrel-open",
            "box-large", "box-large-open", "chest", "campfire-pit",
            "fence", "fence-fortified", "fence-doorway", "tent", "tent-canvas", "tent-canvas-half"
        };
        var townPrefab = new Dictionary<string, GameObject>();
        foreach (var piece in townPieces)
        {
            var loaded = LoadModel(piece);
            if (loaded == null) missing.AppendLine("town: " + piece + ".glb");
            else townPrefab[piece] = loaded;
        }

        // причал: два ряда секций-настила шагом 2 м от кромки городка наружу,
        // до IslandRadius+10 — конец настила висит над открытой водой.
        // Настил НЕ следует дну: над водой держится на уровне моря +0.4.
        // Секции приплюснуты по Y: у structure-floor в GLB полметра «ног»,
        // настил должен быть доской.
        if (townPrefab.ContainsKey("structure-floor"))
        {
            Vector2 perp = new Vector2(-pierDir.y, pierDir.x);
            const float pierUp = 0.4f;     // над землёй/водой
            const float secScale = 3f;     // секция 0.75 -> 2.25 м
            const float secY = 0.3f;       // толщина настила ~0.16 м
            int secI = 0;
            for (float d = townR + 10f; d <= TerrainGen.IslandRadius + 10f + 0.01f; d += 2f)
            {
                secI++;
                Vector2 mid = townC + pierDir * d;
                for (int row = 0; row < 2; row++)
                {
                    Vector2 p = mid + perp * (row * 1.2f - 0.6f);
                    float deckY = Mathf.Max(TerrainGen.HeightAt(p.x, p.y) + pierUp,
                                            TerrainGen.SeaLevel + pierUp);
                    var sec = SpawnTownPiece(townPrefab, "structure-floor",
                        new Vector3(p.x, deckY, p.y),
                        "Pier_Section_" + secI + (row == 0 ? "" : "b"));
                    if (sec != null)
                        sec.transform.GetChild(0).localScale = new Vector3(secScale, secY, secScale);
                }
            }

            // опоры: стволы от y=-1 до настила, под каждую третью секцию
            if (townPrefab.ContainsKey("tree-trunk"))
            {
                const float trunkH = 0.261f; // высота ствола в GLB
                int postI = 0;
                for (float d = townR + 12f; d <= TerrainGen.IslandRadius + 10f; d += 6f)
                {
                    Vector2 p = townC + pierDir * d;
                    float deckY = Mathf.Max(TerrainGen.HeightAt(p.x, p.y) + pierUp,
                                            TerrainGen.SeaLevel + pierUp);
                    var post = SpawnTownPiece(townPrefab, "tree-trunk",
                        new Vector3(p.x, -1f, p.y), "Pier_Post_" + (++postI));
                    if (post != null)
                        post.transform.GetChild(0).localScale =
                            new Vector3(2.2f, (deckY + 1f) / trunkH, 2.2f); // 0.44 м толщиной
                }
            }
        }

        // лачуги: 4 стены-панели вокруг квадрата 3.2 м, вход (+Z, дверной
        // проём) смотрит на площадь. Стена GLB 0.535×0.5 → масштаб 6 даёт
        // 3.2×3.0 м. Панель в GLB лежит на РЕБРЕ тайла (сдвиг 0.224 от
        // центра), иначе стены съезжают внутрь квадрата.
        if (townPrefab.ContainsKey("structure-metal-wall")
            && townPrefab.ContainsKey("structure-metal-doorway"))
        {
            const float wallScale = 6f;
            const float half = 1.6f;
            const float edge = half + 0.224f * wallScale;
            float wallH = 0.5f * wallScale; // верх стен — сюда кладём крышу
            int shackTotal = 7;
            for (int i = 0; i < shackTotal; i++)
            {
                float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                float rr = Mathf.Lerp(12f, 28f, (float)rng.NextDouble());
                Vector2 sp = townC + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rr;
                var shack = new GameObject("Shack_" + i);
                shack.transform.position = OnGround(sp.x, sp.y);
                shack.transform.rotation = Quaternion.Euler(0f,
                    Mathf.Atan2(townC.x - sp.x, townC.y - sp.y) * Mathf.Rad2Deg
                    + ((float)rng.NextDouble() - 0.5f) * 40f, 0f);

                bool noRoof = i % 2 == 1;              // каждая вторая — без крыши
                bool fallenWall = i > 0 && i % 3 == 0; // у части отвалилась стена

                string[] sideNames = { "WallN", "WallE", "WallS", "WallW" };
                Vector3[] sidePos = {
                    new Vector3(0f, 0f, edge), new Vector3(edge, 0f, 0f),
                    new Vector3(0f, 0f, -edge), new Vector3(-edge, 0f, 0f)
                };
                float[] sideYaw = { 0f, 90f, 180f, 270f };
                for (int side = 0; side < 4; side++)
                {
                    var piece = SpawnTownPiece(townPrefab,
                        side == 0 ? "structure-metal-doorway" : "structure-metal-wall",
                        Vector3.zero, "Shack_" + i + "_" + sideNames[side], wallScale);
                    if (piece == null) continue;
                    piece.transform.SetParent(shack.transform, false);
                    if (fallenWall && side == 2)
                    {
                        // задняя стена повернута на 20-40° и отвалила от дома
                        piece.transform.localRotation = Quaternion.Euler(0f,
                            sideYaw[side] + 20f + (float)rng.NextDouble() * 20f,
                            (float)rng.NextDouble() * 15f);
                        piece.transform.localPosition = sidePos[side] * 1.45f;
                    }
                    else
                    {
                        piece.transform.localRotation = Quaternion.Euler(0f, sideYaw[side], 0f);
                        piece.transform.localPosition = sidePos[side];
                    }
                }

                if (!noRoof && townPrefab.ContainsKey("structure-metal-roof"))
                {
                    var roof = SpawnTownPiece(townPrefab, "structure-metal-roof",
                        Vector3.zero, "Shack_" + i + "_Roof");
                    if (roof != null)
                    {
                        roof.transform.SetParent(shack.transform, false);
                        // 6 по пятну (3.3 м, со свесом), 2.2 по высоте конька
                        roof.transform.GetChild(0).localScale = new Vector3(6f, 2.2f, 6f);
                        roof.transform.localPosition = new Vector3(0f, wallH, 0f);
                    }
                }

                if (townPrefab.ContainsKey("bedroll") && (i == 0 || i == 4))
                    SpawnTownPiece(townPrefab, "bedroll",
                        OnGround(sp.x, sp.y, 0.02f), "Shack_" + i + "_Bedroll", 3f);
            }
        }

        // площадь: знак, тара, сундук, холодный очаг. SpawnTownPiece не
        // вешает Campfire — очаг просто декор, живой ставится игроком.
        // Масштабы 2.5–3.5 вместо «1» из спеки: GLB-куски 0.2–0.5 юнита,
        // при масштабе 1 бочка вышла бы 34 см ростом.
        SpawnTownPiece(townPrefab, "signpost", OnGround(townC.x, townC.y), "Town_Signpost", 6f);
        string[] barrelRow = { "barrel", "barrel-open", "barrel" };
        for (int i = 0; i < 3; i++)
        {
            float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
            float rr = 2.5f + (float)rng.NextDouble() * 3.5f;
            Vector2 bp = townC + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rr;
            var barrelGo = SpawnTownPiece(townPrefab, barrelRow[i],
                OnGround(bp.x, bp.y), "Town_Barrel_" + i, 2.5f);
            if (barrelGo != null)
                barrelGo.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
        }
        Vector2 boxPos = townC + new Vector2(3.5f, 2f);
        var boxGo = SpawnTownPiece(townPrefab, rng.Next(0, 2) == 0 ? "box-large-open" : "box-large",
            OnGround(boxPos.x, boxPos.y), "Town_Box", 2.5f);
        if (boxGo != null)
            boxGo.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
        Vector2 chestPos = townC + new Vector2(-3f, -2.5f);
        var chestGo = SpawnTownPiece(townPrefab, "chest",
            OnGround(chestPos.x, chestPos.y), "Town_Chest", 2.5f);
        if (chestGo != null)
            chestGo.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
        Vector2 pitPos = townC + new Vector2(1.5f, -4f);
        SpawnTownPiece(townPrefab, "campfire-pit", OnGround(pitPos.x, pitPos.y), "Town_Firepit", 3.5f);

        // забор по кольцу 34 м: два пролёта выбито (второй — случайный),
        // у выхода на причал — дверной проём. Секции со случайным yaw:
        // забор брошен, никто его не поправлял.
        if (townPrefab.ContainsKey("fence") || townPrefab.ContainsKey("fence-fortified"))
        {
            const int fenceTotal = 12;
            const float fenceR = 34f;
            const float fenceScale = 4f; // секция 0.5 -> 2.0 м
            float pierAng = Mathf.Atan2(pierDir.y, pierDir.x);
            float gapAng = (float)rng.NextDouble() * Mathf.PI * 2f;
            for (int i = 0; i < fenceTotal; i++)
            {
                float ang = i / (float)fenceTotal * Mathf.PI * 2f;
                if (Mathf.Abs(Mathf.DeltaAngle(ang, pierAng)) < 0.24f) continue; // проём на причал
                if (Mathf.Abs(Mathf.DeltaAngle(ang, gapAng)) < 0.3f) continue;   // разрушенный пролёт
                Vector2 fp = townC + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * fenceR;
                var f = SpawnTownPiece(townPrefab, rng.Next(0, 2) == 0 ? "fence" : "fence-fortified",
                    OnGround(fp.x, fp.y), "Town_Fence_" + i, fenceScale);
                if (f != null)
                    f.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
            }
            Vector2 dp = townC + pierDir * fenceR;
            var fenceDoor = SpawnTownPiece(townPrefab, "fence-doorway",
                OnGround(dp.x, dp.y), "Town_Fence_Door", fenceScale);
            if (fenceDoor != null)
                fenceDoor.transform.rotation = Quaternion.Euler(0f,
                    Mathf.Atan2(pierDir.x, pierDir.y) * Mathf.Rad2Deg, 0f); // створка вдоль пути
        }

        // палатки-руины на краю городка: брошенный лагерь, полотнища
        // покосились (наклон Z ±10°)
        string[] tentRow = { "tent", "tent-canvas", "tent-canvas-half" };
        for (int i = 0; i < tentRow.Length; i++)
        {
            float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
            float rr = 29f + (float)rng.NextDouble() * 4f; // между лачугами и забором
            Vector2 tp = townC + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rr;
            var tent = SpawnTownPiece(townPrefab, tentRow[i],
                OnGround(tp.x, tp.y), "Town_Tent_" + i, 5.5f);
            if (tent != null)
                tent.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f,
                    ((float)rng.NextDouble() - 0.5f) * 20f);
        }

        // --- заброшенности по острову: обломки лагерей ---
        // Кольцо 0.25–0.40*Size. Точка бракуется, если в океане или ниже
        // уровня воды; до 24 попыток, не нашли сушу — руина пропускается.
        // Декор без интерактива (лут-руины — этап X).
        string[] ruinPieces = { "box-open", "signpost-single", "resource-planks" };
        foreach (var piece in ruinPieces)
            if (LoadModel(piece) == null) missing.AppendLine("ruins: " + piece + ".glb");
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

        // --- зомби-спавнер (M5): 3 зомби каждую ночь кольцом вокруг игрока ---
        var spawnerGo = new GameObject("ZombieSpawner");
        var spawner = spawnerGo.AddComponent<ZombieSpawner>();
        spawner.dropItem = meat; // с зомби падает сырая плоть
        spawner.pickupPrefab = pickupPrefabAsset;

        // --- курица (§9.3): одна, далеко от спауна, яйца → домашний
        // майонез. Остров вырос — прежние 60–85 м теперь почти центр,
        // 200–380 м — глушь у пляжа ---
        var chickenPos = RandomPos(rng, 200f, 380f);
        var chickenGo = new GameObject("Chicken");
        chickenGo.transform.position = chickenPos;
        var chickenCC = chickenGo.AddComponent<CharacterController>();
        chickenCC.height = 1f; chickenCC.radius = 0.35f; chickenCC.center = new Vector3(0f, 0.5f, 0f);
        var chicken = chickenGo.AddComponent<Chicken>();
        chicken.eggItem = egg;
        chicken.pickupPrefab = pickupPrefabAsset;

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
        SetupHud.Run();

        // Иконки предметов тоже здесь: repair Campfire.prefab обновляет
        // placeablePrefab, иконки должны перепечься в тот же заход —
        // иначе забудется, и слот костра останется пустым навсегда
        IconBaker.Run();

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
