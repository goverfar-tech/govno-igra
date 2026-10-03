using UnityEngine;

// Костёр (M5): готовка плоти + тепло в радиусе.
// Тепло: пока игрок в warmRadius, ночной множитель расхода (Stats) не
// действует — ночь у огня переживается. Механика «посидеть у костра».
// R5: готовка — процесс, а не мгновение: E списывает ингредиент и
// запускает таймер, готовое жаркое выдаётся через cookTime секунд;
// пока жарится, подсказка живёт: «Жарим… N%».
// Рецепты берутся из Resources в Awake — префаб не зависит от Setup.
public class Campfire : MonoBehaviour, IInteractable
{
    public float warmRadius = 4f;
    public float cookTime = 2.5f;   // сколько жарится одна котлета

    ItemData rawMeat, cookedMeat;
    ItemData pendingCook;           // что сейчас в огне
    int pendingCount;
    float cookTimer;                // >0 — идёт готовка
    Player player;
    float nextNag;                  // антиспам тоста «Уже жарится»
    float playerSearchRetry; // повторный поиск не чаще раза в секунду

    void Awake()
    {
        rawMeat = Resources.Load<ItemData>("Items/meat");
        cookedMeat = Resources.Load<ItemData>("Items/cooked_meat");
    }

    public string GetPrompt()
    {
        // живая подсказка: процент обновляется каждый кадр
        // (Player.UpdateInteractFocus пересылает при смене строки)
        if (cookTimer > 0f)
        {
            int pct = Mathf.Clamp(Mathf.RoundToInt((1f - cookTimer / cookTime) * 100f), 0, 99);
            return $"Жарим… {pct}%";
        }
        return player != null && rawMeat != null && player.Inventory.CountOf(rawMeat) > 0
            ? "[E] Пожарить плоть (у костра тепло)"
            : "Костёр греет (нужна сырая плоть для готовки)";
    }

    public void Interact(Player p)
    {
        if (p == null || rawMeat == null || cookedMeat == null) return;

        // уже жарим — второе E не ускоряет
        if (cookTimer > 0f)
        {
            if (Time.time >= nextNag)
            {
                nextNag = Time.time + 1f;
                GameEvents.RaiseNotify("Уже жарится");
            }
            return;
        }

        if (p.Inventory.CountOf(rawMeat) <= 0)
        {
            GameEvents.RaiseNotify("Нет сырой плоти");
            return;
        }

        // ингредиент списывается при СТАРТЕ готовки — дальше мясо в огне
        p.Inventory.RemoveItem(rawMeat, 1);
        pendingCook = cookedMeat;
        pendingCount = 1;
        cookTimer = cookTime;
        GameEvents.RaiseNotify("Жарим плоть…");
    }

    void Update()
    {
        // игрока кэшируем; при потере (рестарт сцены/смерть) ищем заново,
        // но не каждый кадр. Тепло применяется сразу в кадр находки.
        if (player == null)
        {
            if (Time.time < playerSearchRetry) return;
            playerSearchRetry = Time.time + 1f;
            player = FindFirstObjectByType<Player>();
            if (player == null) return; // сцена без игрока (меню)
        }

        // дожариваем (R5): по таймеру выдаём результат. Инвентарь успел
        // заполниться за 2.5 с? — жаркое падает под ноги, не пропадает
        if (cookTimer > 0f)
        {
            cookTimer -= Time.deltaTime;
            if (cookTimer <= 0f)
            {
                // мёртвому не скармливаем: жаркое падёт под ноги на месте
                // гибели (corpse run §9.5 подберёт), а не растворится в трупе
                int leftover = player.Stats.IsDead
                    ? pendingCount
                    : player.Inventory.Add(pendingCook, pendingCount);
                if (leftover > 0)
                {
                    player.Inventory.DropAtFeet(pendingCook, leftover);
                    if (!player.Stats.IsDead)
                        GameEvents.RaiseNotify("Инвентарь полон — уронил у костра");
                }
                GameEvents.RaiseNotify("Готово: " + pendingCook.displayName);
                pendingCook = null;
                pendingCount = 0;
            }
        }

        bool near = Vector3.Distance(transform.position, player.transform.position) < warmRadius;
        if (near)
        {
            player.Stats.lastWarmTime = Time.time;
            // костёр — точка возрождения (§9.5): последний, у которого грелись
            Player.lastCampfirePos = transform.position;
            // (тост про тепло убран: авторская правка 2026-10-03 —
            // огонь и ночной множитель и так читаются без надписи)
            Player.hasCampfireSpawn = true;
        }
    }
}
