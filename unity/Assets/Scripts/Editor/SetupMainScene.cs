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
        var missing = new System.Text.StringBuilder();

        // --- сцена ---
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        // убрать дефолтную камеру — своя будет в Head игрока
        var oldCam = GameObject.Find("Main Camera");
        if (oldCam != null) Object.DestroyImmediate(oldCam);

        // --- земля ---
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.position = Vector3.zero;
        ground.transform.localScale = new Vector3(20f, 1f, 20f); // 200×200 м
        var groundMat = CreateMaterial("Ground", new Color(0.35f, 0.5f, 0.25f));
        ground.GetComponent<MeshRenderer>().sharedMaterial = groundMat;

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
        playerGo.transform.position = new Vector3(0f, 1.1f, 0f);

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
        var flask = SyncItem("flask", "Фляга (полная)", 1, water: 40f, consumeReturns: flaskEmpty);
        var wood = SyncItem("wood", "Древесина", 30, worldModel: "resource-wood");
        var stone = SyncItem("stone", "Камень", 30, worldModel: "resource-stone");
        var berry = SyncItem("berry", "Ягода", 20, food: 12f);
        var meat = SyncItem("meat", "Сырое мясо", 10, food: 25f);
        SyncItem("cooked_meat", "Жаркое", 10, food: 45f);
        var axe = SyncItem("axe", "Каменный топор", 1, isTool: true, toolDamage: 8f, worldModel: "tool-axe");
        var pickaxe = SyncItem("pickaxe", "Кирка", 1, isTool: true, toolDamage: 5f, worldModel: "tool-pickaxe");
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
        player.Inventory.pickupPrefab = pickupPrefabAsset; // для выброса по ПКМ

        // --- стартовый тайник у спауна: фляга, инструменты, еда ---
        // (для M2-теста; при M4 всё это заменится находками в мире)
        SpawnPickup(pickupPrefabAsset, flask, 1, new Vector3(1.5f, 0.3f, 2f));
        SpawnPickup(pickupPrefabAsset, axe, 1, new Vector3(2.2f, 0.3f, 2.8f));
        SpawnPickup(pickupPrefabAsset, pickaxe, 1, new Vector3(2.9f, 0.3f, 3.6f));
        SpawnPickup(pickupPrefabAsset, spear, 1, new Vector3(3.6f, 0.3f, 4.4f));
        SpawnPickup(pickupPrefabAsset, meat, 2, new Vector3(0.8f, 0.3f, 3f));
        SpawnPickup(pickupPrefabAsset, campfire, 1, new Vector3(1.5f, 0.3f, 4f));
        SpawnPickup(pickupPrefabAsset, wall, 3, new Vector3(0.3f, 0.3f, 4.2f));

        // --- дерево (добыча дерева) ---
        var tree = SpawnModel("tree", new Vector3(4f, 0f, -3f), "Tree", 3.2f);
        if (tree == null) tree = SpawnModel("tree-tall", new Vector3(4f, 0f, -3f), "Tree", 3.2f);
        if (tree != null)
        {
            var col = tree.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0f, 1.3f, 0f); // как в Godot-версии
            col.height = 2.6f;
            col.radius = 0.3f;
            var node = tree.AddComponent<ResourceNode>();
            node.yield = wood;
            node.hitsLeft = 3;
            node.pickupPrefab = pickupPrefabAsset;
        }
        else missing.AppendLine("tree / tree-tall");

        // --- камень-жилка ---
        var rock = SpawnModel("resource-stone-large", new Vector3(-4f, 0f, 2f), "StoneNode", 1.8f);
        if (rock == null) rock = SpawnModel("rock-a", new Vector3(-4f, 0f, 2f), "StoneNode", 1.8f);
        if (rock != null)
        {
            var col = rock.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.5f, 0f);
            col.size = new Vector3(1.2f, 1f, 1.2f);
            var node = rock.AddComponent<ResourceNode>();
            node.yield = stone;
            node.hitsLeft = 3;
            node.pickupPrefab = pickupPrefabAsset;
        }
        else missing.AppendLine("resource-stone-large / rock-a");

        // --- декорации: деревья и камни кольцом, ВСЕ добываемые ---
        for (int i = 0; i < 8; i++)
        {
            float angle = i * Mathf.PI * 2f / 8f;
            var pos = new Vector3(Mathf.Cos(angle) * (10f + i), 0f, Mathf.Sin(angle) * (10f + i));
            var t = SpawnModel(i % 2 == 0 ? "tree" : "tree-tall", pos, "Tree" + i, 3f);
            if (t == null)
            {
                if (i == 0) missing.AppendLine("decor trees");
                continue;
            }
            var tc = t.AddComponent<CapsuleCollider>();
            tc.center = new Vector3(0f, 1.3f, 0f);
            tc.height = 2.6f;
            tc.radius = 0.3f;
            var tn = t.AddComponent<ResourceNode>();
            tn.yield = wood;
            tn.hitsLeft = 3;
            tn.pickupPrefab = pickupPrefabAsset;
        }
        for (int i = 0; i < 3; i++)
        {
            var r = SpawnModel(new[] { "rock-a", "rock-b", "rock-c" }[i], new Vector3(-8f + i * 3f, 0f, -8f), "Rock" + i, 1.8f);
            if (r == null) continue;
            var rc = r.AddComponent<BoxCollider>();
            rc.center = new Vector3(0f, 0.5f, 0f);
            rc.size = new Vector3(1.2f, 1f, 1.2f);
            var rn = r.AddComponent<ResourceNode>();
            rn.yield = stone;
            rn.hitsLeft = 3;
            rn.pickupPrefab = pickupPrefabAsset;
        }

        // --- куст ягод (пикап ягоды рядом) ---
        if (SpawnModel("grass-large", new Vector3(2f, 0f, 4f), "Bush", 2f) == null)
        {
            // модель травы не нашлась — заменяем зелёным кустом-примитивом
            var bush = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bush.name = "Bush";
            bush.transform.position = new Vector3(2f, 0.4f, 4f);
            bush.transform.localScale = Vector3.one * 0.8f;
            Object.DestroyImmediate(bush.GetComponent<SphereCollider>());
            bush.GetComponent<MeshRenderer>().sharedMaterial = CreateMaterial("Bush", new Color(0.2f, 0.45f, 0.15f));
        }
        var berryPos = new Vector3(2f, 0.5f, 4f);
        var berryGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        berryGo.name = "BerryPickup";
        berryGo.transform.position = berryPos;
        berryGo.transform.localScale = Vector3.one * 0.25f;
        var berryCol = berryGo.GetComponent<SphereCollider>();
        berryCol.isTrigger = true;
        var berryPickup = berryGo.AddComponent<PickupItem>();
        berryPickup.item = berry;
        berryPickup.count = 2;
        var berryMat = CreateMaterial("Berry", new Color(0.7f, 0.1f, 0.2f));
        berryGo.GetComponent<MeshRenderer>().sharedMaterial = berryMat;

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
        bool isPlaceable = false, string worldModel = null, GameObject placeablePrefab = null)
    {
        string path = $"Assets/Items/{id}.asset";
        var item = AssetDatabase.LoadAssetAtPath<ItemData>(path);
        bool created = item == null;
        if (created) item = ScriptableObject.CreateInstance<ItemData>();

        item.id = id;
        item.displayName = displayName;
        item.maxStack = maxStack;
        item.foodRestore = food;
        item.waterRestore = water;
        item.healAmount = heal;
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
        if (existing != null) return existing;

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

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        string leaf = System.IO.Path.GetFileName(path);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }
}
