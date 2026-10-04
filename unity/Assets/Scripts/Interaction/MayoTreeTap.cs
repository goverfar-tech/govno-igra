using UnityEngine;

// X3 «Майонезные деревья» (§9.3): банка-подвеска на стволе, за ночь в неё
// стекает майонезный сок. Стабильный «чистый» источник еды — сок БЕЗ яда,
// на фоне сырой плоти (12 еды/30 яда) и лужи (+15 яда).
//
// ПОЧЕМУ НА БАНКЕ ЕСТЬ ТРИГГЕР (отступление от «без коллайдеров»):
// протокол взаимодействия проекта — Physics.Raycast →
// collider.GetComponentInParent<IInteractable>() (Player.UpdateInteractFocus),
// а на корне дерева ПЕРВЫМ IInteractable стоит ResourceNode
// («[E] Добыть: Древесина») — GetComponent всегда возвращает первый
// подходящий компонент. Без собственного коллайдера на банке тап был бы
// недостижим лучом в принципе. Поэтому на банке ровно ОДИН маленький
// ТРИГГЕР-бокс: луч взаимодействия его ловит, а движению и остальным
// системам он не мешает — зомби-LOS, спавн-проверки и проверка приседа
// идут с QueryTriggerInteraction.Ignore, CharacterController триггеры
// не останавливает. Рубке ствола не мешает: банка висит СБОКУ от капсулы
// дерева, прицел на ствол мимо банки по-прежнему даёт ResourceNode.
//
// КАК ЭТО СОБРАНО: компонент ставится интегратором на КОРЕНЬ дерева
// (туда же, где CapsuleCollider + ResourceNode у Setup). Сам строит в
// Start визуал банки (стакан+крышка из примитивов, верёвка от ствола) —
// ребёнком корня, так что банка умирает и переезжает вместе с деревом,
// ничего специального не нужно. GLB-модель дерева и её материалы не
// трогаем: ResourceNode перекрашивает их сам при исчерпании.
// ДОБЫЧА ДЕРЕВА НЕ УБИВАЕТ БАНКУ (решение интегратора, аудит
// 2026-10-04): узел исчерпан (GreyOut), а сок капает дальше — «дрова
// кончились, дерево живо». Иначе рубка древесины выжигала бы
// единственный чистый источник еды и наказывала за обычную игру.
//
// СЕЙВ: наполненность СОЗНАННО не сохраняется (SaveSystem про банки не
// знает) — после загрузки банки пустые и снова наполняются за ночь.
// Потеря несобранного сока при сейве экономику не ломает: за ночь банка
// всё равно доходит до полной.
[DisallowMultipleComponent]
public class MayoTreeTap : MonoBehaviour, IInteractable
{
    [Header("Предмет-сок (интегратор проставит ассет «juice» из Resources/Items)")]
    public ItemData juiceItem;

    [Header("Сторона банки: градусы вокруг вертикали (локально дереву)")]
    [Range(0f, 360f)] public float sideAngle = 0f;

    // --- Константы (все числа задачи — здесь) ---

    // Ночных секунд до полной банки. Ночь при сутках 420 с (nightStart
    // 0.75 / dayStart 0.25) длится 210 с — банка заполняется за 180 с
    // ночи, с запасом до рассвета.
    const float FillNightsSeconds = 180f;

    // Сколько сока за один сбор (2 × 12 еды = 24 — скромнее домашнего
    // майонеза 35, зато без яда и стабильный: банка полна каждую ночь).
    const int CollectAmount = 2;

    // Высота банки над землёй (задание: ~1.2 м).
    const float HangHeight = 1.2f;

    // Отступ от оси ствола. У дерева капсула r=0.3 (Setup) — банка висит
    // чуть в стороне, чтобы её было видно и прицел не спорил с рубкой.
    const float SideOffset = 0.45f;

    // Точка крепления верёвки на стволе: диаметрально «внутрь» от банки
    // (капсула дерева r=0.3) и выше — верёвка идёт от ствола вниз к банке.
    const float TrunkAttach = 0.3f;
    const float RopeRise = 0.75f;

    // Размеры визуала. Цилиндр-примитив: r=0.5, h=2 при scale 1 →
    // CupScaleY одновременно и «полувысота» стакана.
    const float CupScaleXZ = 0.13f;  // диаметр стакана ~13 см
    const float CupScaleY = 0.09f;   // высота стакана ~18 см
    const float CapScaleXZ = 0.15f;  // крышка чуть шире стакана
    const float CapScaleY = 0.035f;
    const float RopeWidth = 0.014f;  // лёгкая верёвка-линия

    // Прицельный триггер на банке (щедрее самого стакана — в цель легче).
    const float AimBoxXZ = 0.22f;
    const float AimBoxY = 0.32f;

    float fill;    // 0..1; рассвет накопленное НЕ сбрасывает, сверху кламп 1
    bool nightNow; // кэш флага из TimeOfDayChanged (событие приходит ~каждые 0.4 с)

    void OnEnable() => GameEvents.TimeOfDayChanged += OnTime;
    void OnDisable() => GameEvents.TimeOfDayChanged -= OnTime;

    void OnTime(float t, bool night) => nightNow = night;

    // Наполнение копим в Update по кэшу флага, а не в самом событии: так
    // скорость покадрово точная и не зависит от троттлинга события, а на
    // паузе (timeScale=0) Time.deltaTime=0 — банка «мёртвая», как и мир.
    void Update()
    {
        if (!nightNow || fill >= 1f) return;
        fill = Mathf.Min(1f, fill + Time.deltaTime / FillNightsSeconds);
    }

    // Материалы шарятся между всеми банками (паттерн тела зомби в
    // ZombieSpawner): GLB-материалы дерева не трогаем — ResourceNode
    // перерисовывает их сам при исчерпании.
    static Material mayoMat, capMat, ropeMat;

    void Start() => BuildJar();

    void BuildJar()
    {
        if (mayoMat == null)
        {
            mayoMat = Solid(new Color(0.93f, 0.88f, 0.62f)); // цвет майонеза
            capMat = Solid(new Color(0.78f, 0.70f, 0.52f));  // крышка темнее
            ropeMat = Solid(new Color(0.45f, 0.38f, 0.30f)); // верёвка
        }

        var dir = Quaternion.Euler(0f, sideAngle, 0f) * Vector3.forward;

        var jar = new GameObject("MayoJar");
        jar.transform.SetParent(transform, false);
        jar.transform.localPosition = dir * SideOffset + Vector3.up * HangHeight;

        // Прицельный триггер + посредник для луча (см. шапку файла):
        // единственный коллайдер на банке, триггерный.
        var aim = jar.AddComponent<BoxCollider>();
        aim.isTrigger = true;
        aim.size = new Vector3(AimBoxXZ, AimBoxY, AimBoxXZ);
        jar.AddComponent<MayoTreeTapTarget>().owner = this;

        // Стакан из цилиндра. Коллайдер примитива сносим — коллайдер на
        // банке ровно один, прицельный (иначе луч ловил бы «мимо»
        // посредника и уходил в ResourceNode на корне).
        var cup = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(cup.GetComponent<CapsuleCollider>());
        cup.transform.SetParent(jar.transform, false);
        cup.transform.localScale = new Vector3(CupScaleXZ, CupScaleY, CupScaleXZ);
        cup.GetComponent<MeshRenderer>().sharedMaterial = mayoMat;

        // Кубик-крышка сверху.
        var cap = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(cap.GetComponent<BoxCollider>());
        cap.transform.SetParent(jar.transform, false);
        cap.transform.localPosition = new Vector3(0f, CupScaleY + CapScaleY * 0.5f, 0f);
        cap.transform.localScale = new Vector3(CapScaleXZ, CapScaleY, CapScaleXZ);
        cap.GetComponent<MeshRenderer>().sharedMaterial = capMat;

        // Верёвка: тонкий вытянутый куб от точки на стволе (выше банки)
        // до верха крышки. Примитив смотрит вдоль Z — тянем масштаб по Z.
        var from = new Vector3(-dir.x * TrunkAttach, RopeRise, -dir.z * TrunkAttach);
        var to = new Vector3(0f, CupScaleY + CapScaleY, 0f); // верх крышки
        var rope = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(rope.GetComponent<BoxCollider>());
        rope.transform.SetParent(jar.transform, false);
        var delta = to - from;
        rope.transform.localPosition = (from + to) * 0.5f;
        rope.transform.localRotation = Quaternion.LookRotation(delta);
        rope.transform.localScale = new Vector3(RopeWidth, RopeWidth, delta.magnitude);
        rope.GetComponent<MeshRenderer>().sharedMaterial = ropeMat;

        // Известная косметика (не чинится из этого файла): при исчерпании
        // дерева ResourceNode.GreyOut перекрашивает ВСЕ рендереры-дети
        // корня, включая банку — на обобранном дереве банка тускнеет
        // вместе со стволом. По тону мира это даже честно; GreyOut
        // инстанцирует копии материалов, другие банки не затронет.
    }

    static Material Solid(Color c) => new Material(Shader.Find("Standard")) { color = c };

    // Промпт зовётся каждый кадр (Player.UpdateInteractFocus), поэтому
    // процент живой без всяких событий — паттерн тот же, что у костра.
    public string GetPrompt()
    {
        if (fill >= 1f) return "[E] Собрать майонезный сок";
        // FloorToInt, а не Round: при fill 99.6% нельзя врать «100%»,
        // пока Interact честно откажет.
        return $"[E] Подождите: банка наполнилась на {Mathf.FloorToInt(fill * 100f)}%";
    }

    public void Interact(Player player)
    {
        if (player == null) return;
        if (fill < 1f)
        {
            GameEvents.RaiseNotify("Ещё стекает…");
            return;
        }
        if (juiceItem == null)
        {
            // Интегратор не проставил ассет — наполнение не тратим,
            // но и не молчим (иначе «банка съела сбор»).
            GameEvents.RaiseNotify("Банка полна, но предмет «juice» не заведён");
            return;
        }

        int leftover = player.Inventory.Add(juiceItem, CollectAmount);
        if (leftover > 0)
        {
            // Add кладёт сколько влезло: откатываем влезший хвост, чтобы
            // сбор был атомарным — иначе «частично забрал, fill остался 1»
            // превращается в бесконечный сок.
            if (leftover < CollectAmount)
                player.Inventory.RemoveItem(juiceItem, CollectAmount - leftover);
            GameEvents.RaiseNotify("Карманы полны, сок не унести");
            return; // fill не сбрасываем — банка остаётся полной до успеха
        }
        fill = 0f; // снова пустая — за ночь наполнится заново
        GameEvents.RaiseNotify("Сок собран");
    }
}

// Посредник на банке: луч игрока находит IInteractable только через
// коллайдер, а на корне дерева первый IInteractable — ResourceNode.
// Без состояния: всё пересылает владельцу (см. шапку MayoTreeTap).
// Ставится MayoTreeTap'ом в BuildJar, вручную не вешается.
public class MayoTreeTapTarget : MonoBehaviour, IInteractable
{
    public MayoTreeTap owner;

    public string GetPrompt() => owner != null ? owner.GetPrompt() : null;
    public void Interact(Player player)
    {
        if (owner != null) owner.Interact(player);
    }
}
