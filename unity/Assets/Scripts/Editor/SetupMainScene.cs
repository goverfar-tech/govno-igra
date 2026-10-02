using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Один раз собирает и сохраняет сцену Assets/Scenes/Main.unity:
// земля, солнце с DayNight, игрок (капсула + Player/Inventory/Stats,
// Head с камерой), DebugHud, дерево и камень как ResourceNode,
// ягодный пикап, ItemData-ассеты (wood/stone/berry).
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
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        // убрать дефолтную камеру — своя будет в Head игрока
        var oldCam = GameObject.Find("Main Camera");
        if (oldCam != null) Object.DestroyImmediate(oldCam);

        // --- террейн: рельеф по шуму вместо плоскости (M4) ---
        EnsureFolder("Assets/Terrain");
        string terrainPath = "Assets/Terrain/TerrainMesh.asset";
        var terrainMesh = AssetDatabase.LoadAssetAtPath<Mesh>(terrainPath);
        bool isNewMesh = terrainMesh == null;
        if (isNewMesh) terrainMesh = new Mesh { name = "Terrain" };
        TerrainGen.FillMesh(terrainMesh, 130); // ~1.5 м на ячейку — капсула не «проваливается»
        if (isNewMesh) AssetDatabase.CreateAsset(terrainMesh, terrainPath);
        else EditorUtility.SetDirty(terrainMesh);
        var ground = new GameObject("Terrain");
        ground.AddComponent<MeshFilter>().sharedMesh = terrainMesh;
        ground.AddComponent<MeshRenderer>().sharedMaterial =
            CreateMaterial("Ground", new Color(0.35f, 0.5f, 0.25f));
        ground.AddComponent<MeshCollider>().sharedMesh = terrainMesh;

        // --- солнце + день/ночь ---
        var sun = GameObject.Find("Directional Light")?.GetComponent<Light>();
        if (sun != null)
        {
            sun.shadows = LightShadows.Soft;
            var dayNight = sun.gameObject.AddComponent<DayNight>();
            dayNight.sun = sun;
        }

        // --- игрок ---
        var playerGo = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        playerGo.name = "Player";
        playerGo.transform.position = new Vector3(0f, TerrainGen.HeightAt(0f, 0f) + 1.1f, 0f);

        var capsuleCol = playerGo.GetComponent<CapsuleCollider>();
        if (capsuleCol != null) Object.DestroyImmediate(capsuleCol);

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
        var berry = SyncItem("berry", "Ягода", 20, food: 12f);
        var meat = SyncItem("meat", "Сырая плоть", 10, food: 12f, poison: 30f);      // §9.3: мало + яд
        SyncItem("cooked_meat", "Котлета", 10, food: 45f, poison: 8f);              // готовка режет яд
        var egg = SyncItem("egg", "Яйцо", 10, food: 8f);
        var mayo = SyncItem("mayo", "Домашний майонез", 10, food: 35f, heal: 5f);   // чистая еда §9.3
        var spear = SyncItem("spear", "Деревянное копьё", 1, isTool: true, toolDamage: 10f, worldModel: "tool-hoe");
        var campfire = SyncItem("campfire", "Костёр", 5, isPlaceable: true, placeablePrefab: campfirePrefab);
        var wall = SyncItem("wall", "Деревянная стена", 10, isPlaceable: true, placeablePrefab: wallPrefab);

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

        // --- у спауна только случайная пустая фляга (тайник снесён,
        // экономика — крафт; аудит механик 2026-10) ---
        SpawnPickup(pickupPrefabAsset, flaskEmpty, 1, OnGround(1.5f, 2f, 0.3f));

        // --- мир M4: рассеивание по seed + озеро ---
        var rng = new System.Random(1337);

        // деревья (30 шт, все добываемые)
        for (int i = 0; i < 30; i++)
        {
            var t = SpawnModel(i % 3 == 0 ? "tree-tall" : "tree", RandomPos(rng, 14f, 90f),
                "Tree" + i, 2.6f + (float)rng.NextDouble() * 0.8f);
            if (t == null) { if (i == 0) missing.AppendLine("scatter trees"); continue; }
            var tc = t.AddComponent<CapsuleCollider>();
            tc.center = new Vector3(0f, 1.3f, 0f);
            tc.height = 2.6f;
            tc.radius = 0.3f;
            var tn = t.AddComponent<ResourceNode>();
            tn.yield = wood; tn.hitsLeft = 3; tn.pickupPrefab = pickupPrefabAsset;
        }

        // камни (12 шт)
        string[] rockModels = { "rock-a", "rock-b", "rock-c" };
        for (int i = 0; i < 12; i++)
        {
            var r = SpawnModel(rockModels[i % 3], RandomPos(rng, 14f, 90f),
                "Rock" + i, 1.5f + (float)rng.NextDouble() * 0.7f);
            if (r == null) continue;
            var rc = r.AddComponent<BoxCollider>();
            rc.center = new Vector3(0f, 0.5f, 0f);
            rc.size = new Vector3(1.2f, 1f, 1.2f);
            var rn = r.AddComponent<ResourceNode>();
            rn.yield = stone; rn.hitsLeft = 3; rn.pickupPrefab = pickupPrefabAsset;
        }

        // ягодные кусты (10 шт, пикап ягоды на кусте)
        for (int i = 0; i < 10; i++)
        {
            var b = SpawnModel("grass-large", RandomPos(rng, 10f, 80f), "Bush" + i, 2f);
            if (b == null) continue;
            var bc = b.AddComponent<SphereCollider>();
            bc.isTrigger = true; // куст не блокирует движение
            bc.center = new Vector3(0f, 0.5f, 0f);
            bc.radius = 0.9f;
            var bp = b.AddComponent<PickupItem>();
            bp.item = berry;
            bp.count = 2;
        }

        // озеро: вода + наполнение фляги
        float lakeY = TerrainGen.HeightAt(TerrainGen.LakeCenter.x, TerrainGen.LakeCenter.y);
        var lake = new GameObject("MayoPuddle");
        lake.transform.position = new Vector3(TerrainGen.LakeCenter.x, lakeY + 1.2f, TerrainGen.LakeCenter.y);

        var water = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        water.name = "Surface";
        water.transform.SetParent(lake.transform, false);
        water.transform.localScale = new Vector3(TerrainGen.LakeRadius * 1.6f, 0.05f, TerrainGen.LakeRadius * 1.6f);
        water.GetComponent<MeshRenderer>().sharedMaterial =
            CreateMaterial("Mayo", new Color(0.93f, 0.88f, 0.62f)); // майонезная лужа, §9
        var waterCol = water.GetComponent<CapsuleCollider>();
        if (waterCol != null) Object.DestroyImmediate(waterCol);

        // зона взаимодействия: тонкий немасштабированный бокс на уровне воды
        // (луч не попадает в триггер, стартуя изнутри него, поэтому НЕ
        // trigger — «стоять на воде» у кромки выглядит как мелководье)
        var zone = new GameObject("InteractZone");
        zone.transform.SetParent(lake.transform, false);
        var zoneCol = zone.AddComponent<BoxCollider>();
        zoneCol.size = new Vector3(14f, 0.2f, 14f);
        var waterSource = zone.AddComponent<WaterSource>();
        waterSource.emptyFlask = flaskEmpty;
        waterSource.fullFlask = flask;




        // --- зомби-спавнер (M5): 3 зомби каждую ночь кольцом вокруг игрока ---
        var spawnerGo = new GameObject("ZombieSpawner");
        var spawner = spawnerGo.AddComponent<ZombieSpawner>();
        spawner.dropItem = meat; // с зомби падает сырая плоть
        spawner.pickupPrefab = pickupPrefabAsset;

        // --- курица (§9.3): одна, вдали от спауна, яйца → домашний майонез ---
        var chickenPos = RandomPos(rng, 60f, 85f);
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
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Main.unity", true) };
        AssetDatabase.SaveAssets();

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
            AssetDatabase.MoveAsset(oldPath, path);
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
        if (worldModel != null) item.worldModel = LoadModel(worldModel);
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
            // префаб мог быть создан до появления Campfire.cs — довешиваем
            if (existing.GetComponent<Campfire>() == null)
            {
                var rootGo = PrefabUtility.LoadPrefabContents(path);
                rootGo.AddComponent<Campfire>();
                existing = PrefabUtility.SaveAsPrefabAsset(rootGo, path);
                PrefabUtility.UnloadPrefabContents(rootGo);
            }
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

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        string leaf = System.IO.Path.GetFileName(path);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }
}
