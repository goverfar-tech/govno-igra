using System.Collections.Generic;
using UnityEngine;

// Майонезные приливы (X1, §9.4): каждые N ночей — усиленная волна зомби,
// которая ИДЁТ ПО СЛЕДУ игрока. Сила приливов растёт: +1 зомби за каждый
// прошедший прилив. Мягкий глобальный таймер меты: отсиживаться в
// безопасной зоне вечно не выйдет.
//
// Режиссёр самодостаточен: обязательных ссылок нет, всё лениво через
// FindFirstObjectByType/Resources — объект «MayoSurge» создаёт Setup
// рядом с ZombieSpawner. Подписка на GameEvents.TimeOfDayChanged такая
// же, как у ZombieSpawner: событие регулярное (~0.3 с), работаем только
// на фронтах ночи/заката.
//
// ПРИЛИВ ИДЁТ СВЕРХ обычного ночного спавна: в приливную ночь
// ZombieSpawner.SpawnZombie отрабатывает как всегда (6 + эскалация),
// приливный рой добавляется отдельно и в его список alive НЕ попадает
// (иначе cap спавнера задавил бы обычную ночь).
public class MayoSurge : MonoBehaviour
{
    // ---- Константы баланса (править здесь, не в логике) ----
    // Период приливов: приливная ночь = (номер ночи % 3 == 2) — третья,
    // шестая, девятая… Между приливами две «обычные» ночи (стартово 3).
    const int SurgeEveryNights = 3;
    // База волны: прилив всегда плотнее обычной ночи (та стартует с 6,
    // но размазана кольцом 22–32 м; прилив — одним роем).
    const int WaveBaseSize = 6;
    // Потолок волны — как у ночного спавнера (14): поздняя игра давит,
    // но не превращается в неиграбельную кашу.
    const int WaveSizeCap = 14;
    // Кольцо спавна волны: дальше ночного (22–32 м) — прилив «идёт из
    // тумана», у игрока есть время заметить рой и выбрать: бежать
    // (жирный след выдаст!) или запереться.
    const float RingMin = 50f;
    const float RingMax = 70f;
    // Сектор спавна вокруг направления на самый свежий след: рой заходит
    // СО СТОРОНЫ следа, а не кольцом со всех сторон.
    const float SectorHalfAngleDeg = 40f;
    // Точка волны не ближе 25 м к игроку. Кольцо 50–70 и так дальше,
    // проверка страхует на случай уменьшения RingMin.
    const float MinPlayerDist = 25f;
    // Попыток найти чистую точку на зомби — как TryFindSpawn у спавнера.
    const int SpawnAttempts = 16;
    // Точек следа в маршруте максимум: след живёт 20 с и капает по ~1.1 м,
    // реальных точек мало; потолок — защита от разрастания списка.
    const int RouteMaxPoints = 64;
    // Текст закатного предупреждения — единственный тост перед приливом.
    const string WarnText = "Майонез за городом вскипает… ночью прилив";

    bool wasNight;      // фронт ночи: событие регулярное — не спавнить тысячами
    bool warnedToday;   // «предупреждено за сегодня» — закатный тост один
    int nightsPassed;   // свой счётчик ночей, старт 0 (см. комментарий в OnTime)
    int surgeCount;     // сколько приливов уже прошло — сила волны растёт
    DayNight dayNight;  // ленивый поиск: окно заката берём по его настройкам
    ItemData dropItem;         // лениво: Resources/Items/meat
    PickupItem pickupPrefab;   // лениво: Inventory.pickupPrefab (как FatZombie)

    void OnEnable() => GameEvents.TimeOfDayChanged += OnTime;
    void OnDisable() => GameEvents.TimeOfDayChanged -= OnTime;

    void OnTime(float t, bool night)
    {
        if (!night)
        {
            // Предупреждение на закате ПЕРЕД приливной ночью: вечерняя
            // четверть светового дня. У DayNight день — [dayStart, nightStart]
            // (дефолты 0.25/0.75), последняя его четверть — закат; значения
            // берём из компонента, а не константой, чтобы не разъехаться,
            // если Setup подкрутит сутки.
            if (dayNight == null) dayNight = FindFirstObjectByType<DayNight>();
            float dayFrom = dayNight != null ? dayNight.dayStart : 0.25f;
            float nightFrom = dayNight != null ? dayNight.nightStart : 0.75f;
            float duskFrom = nightFrom - (nightFrom - dayFrom) * 0.25f;
            if (t >= duskFrom && t < nightFrom && !warnedToday && IsNextNightSurge())
            {
                warnedToday = true;
                GameEvents.RaiseNotify(WarnText);
            }
            wasNight = false;
            return;
        }

        if (wasNight) return; // только фронт ночи
        wasNight = true;
        warnedToday = false;  // новый цикл суток — закатный тост снова доступен

        // Приливность ночи определяется ДО инкремента: счёт 0-based, ночи
        // с номером 2, 5, 8… — третья, шестая, девятая…
        bool surge = IsNextNightSurge();
        nightsPassed++;
        // После загрузки сейва счётчик сбрасывается в 0 — осознанно: номер
        // ночи в SaveData не пишется (его файл не наш), прилив по §9.4 —
        // мягкий глобальный таймер, потеря счёта при перезагрузке мир
        // не ломает: волны просто начнут отсчёт заново.
        if (!surge) return;

        var player = FindFirstObjectByType<Player>();
        if (player == null)
        {
            // Фронт не отработан (загрузка/смерть в этот кадр): ночей это
            // не съедает — surgeCount не растёт, следующая приливная ночь
            // придёт полной силой.
            return;
        }
        SpawnWave(player);
    }

    // Приливная ночь: (номер ночи % 3 == 2) — третья, шестая…
    bool IsNextNightSurge() => nightsPassed % SurgeEveryNights == SurgeEveryNights - 1;

    // Волна растёт с каждым приливом (+1), потолок как у ночного спавна.
    int WaveSize() => Mathf.Min(WaveBaseSize + surgeCount, WaveSizeCap);

    void SpawnWave(Player player)
    {
        // размер волны ДО инкремента силы: первая волна — ровно
        // WaveBaseSize (аудит 2026-10-04: инкремент в начале давал 7).

        // Маршрут роя: точки следа от старейшей к свежей + финал — где
        // игрок стоял в момент спавна. Следа нет (стоял/в океане) — рой
        // всё равно идёт маршрутом из одной финальной точки.
        var route = MayoTrail.RecentPoints(RouteMaxPoints);

        // Направление на самый СВЕЖИЙ след — рой заходит с той стороны,
        // куда игрок бежал. Следа нет — случайный сектор.
        float baseAngle = Random.value * 360f;
        if (route.Count > 0)
        {
            Vector3 freshest = route[route.Count - 1];
            Vector2 dir = new Vector2(freshest.x - player.transform.position.x,
                                      freshest.z - player.transform.position.z);
            if (dir.sqrMagnitude > 0.01f)
                baseAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        }
        // дубли не нужны: если последняя точка следа и есть позиция игрока
        if (route.Count == 0 ||
            (route[route.Count - 1] - player.transform.position).sqrMagnitude > 0.01f)
            route.Add(player.transform.position);

        int size = WaveSize();
        int spawned = 0;
        for (int i = 0; i < size; i++)
        {
            if (TryFindWavePoint(player.transform.position, baseAngle, out var pos))
            {
                SpawnSurgeZombie(pos, route);
                spawned++;
            }
            else
            {
                Debug.Log("[MayoSurge] чистой точки за " + SpawnAttempts +
                          " попыток не нашлось — приливный зомби пропущен");
            }
        }
        if (spawned > 0)
        {
            // сила растёт только у СОСТОЯВШЕГОСЯ прилива: полный провал
            // спавна (сектор в океане) не съедает эскалацию (аудит)
            surgeCount++;
            GameEvents.RaiseNotify("Прилив! Мертвецы идут по твоему следу…");
        }
    }

    // Точка волны: кольцо RingMin–RingMax в секторе ±SectorHalfAngleDeg
    // вокруг направления на свежий след. Запреты скопированы 1:1 с
    // ZombieSpawner.TryFindSpawn (остров/не отмель/не океан/не озеро/
    // пещера босса) — при изменении правил спавна держать синхронно —
    // плюс «не ближе 25 м к игроку».
    bool TryFindWavePoint(Vector3 pp, float baseAngle, out Vector3 pos)
    {
        for (int attempt = 0; attempt < SpawnAttempts; attempt++)
        {
            float ang = (baseAngle + Random.Range(-SectorHalfAngleDeg, SectorHalfAngleDeg))
                        * Mathf.Deg2Rad;
            float r = Random.Range(RingMin, RingMax);
            float x = pp.x + Mathf.Cos(ang) * r;
            float z = pp.z + Mathf.Sin(ang) * r;

            // за кромкой острова спавнить бессмысленно — зомби не плавает
            if (new Vector2(x, z).magnitude > TerrainGen.IslandRadius - 10f)
                continue;
            float h = TerrainGen.HeightAt(x, z);
            // не на отмели и не в океане: точка обязана быть сушей
            if (h < TerrainGen.SeaLevel + 0.3f)
                continue;
            if (TerrainGen.IsInOcean(new Vector3(x, h + 1f, z)))
                continue;
            // логово босса — заповедник FatZombie: правило то же, что у
            // ночного спавнера (репро 2026-10-04)
            if (h < TerrainGen.CaveFloor + 2f
                && Vector2.Distance(new Vector2(x, z), TerrainGen.MountainCenter)
                    < TerrainGen.MountainRadius + 6f)
                continue;
            // не ближе MinPlayerDist к игроку
            if (Vector2.Distance(new Vector2(x, z), new Vector2(pp.x, pp.z)) < MinPlayerDist)
                continue;
            // не в озере и не на отмели у воды
            if (Vector2.Distance(new Vector2(x, z), TerrainGen.LakeCenter)
                < TerrainGen.LakeRadius + 2f)
                continue;

            pos = new Vector3(x, h + 1f, z);
            // тело зомби не должно пересекать ствол/камень/бокс озера
            if (Physics.CheckCapsule(pos + Vector3.up * 0.5f, pos + Vector3.up * 1.6f,
                    0.35f, ~0, QueryTriggerInteraction.Ignore))
                continue;
            return true;
        }
        pos = default;
        return false;
    }

    // Спавн одного приливного зомби — 1:1 с ZombieSpawner.SpawnZombie
    // (CharacterController 1.7/0.4/центр 0.85 + BuildBody). Тело общее
    // (материалы static у BuildBody), дроп — как у FatZombie.DropLoot:
    // мясо из Resources, пикап у Inventory. В ZombieSpawner.alive НЕ
    // добавляем — cap спавнера считает только ОБЫЧНУЮ ночь, прилив сверх.
    void SpawnSurgeZombie(Vector3 pos, List<Vector3> route)
    {
        var root = new GameObject("Zombie");
        root.transform.position = pos;
        var cc = root.AddComponent<CharacterController>();
        cc.height = 1.7f;
        cc.radius = 0.4f;
        cc.center = new Vector3(0f, 0.85f, 0f);

        var zombie = root.AddComponent<Zombie>();
        if (dropItem == null) dropItem = Resources.Load<ItemData>("Items/meat");
        zombie.dropItem = dropItem;
        if (pickupPrefab == null)
        {
            var inv = FindFirstObjectByType<Inventory>();
            if (inv != null) pickupPrefab = inv.pickupPrefab;
        }
        zombie.pickupPrefab = pickupPrefab;
        zombie.SetWaveRoute(route); // рой идёт по следу игрока
        ZombieSpawner.BuildBody(root.transform);
    }
}
