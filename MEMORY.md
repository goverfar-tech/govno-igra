# ПАМЯТЬ ПРОЕКТА — survival (Godot 4.7)
> Этот файл — постоянная память проекта. Агент читает его в начале
> каждой сессии. Обновляется при смене решений и завершении этапов.
> Последнее обновление: 2026-09-26

## 1. Цель проекта
Одиночная 3D FPS survival-игра.
Ближайшая цель — вертикальный прототип: играбельный срез от спауна
игрока до базового выживания.

## 2. Стек
- Движок: Godot 4.7 (GL Compatibility renderer)
- Язык: GDScript, типизированный
- Физика: Jolt Physics
- Контроль версий: Git (коммит после каждой завершённой системы;
  игра должна запускаться после каждого коммита)

## 3. Архитектурные принципы
- Композиция узлов + сигналы; минимум жёстких связей.
- Ни один скрипт не лезет в чужие внутренние узлы — только публичные
  методы и сигналы.
- Данные отдельно от логики: предметы и рецепты — это Resource (.tres),
  чтобы добавление предмета не требовало правок кода.
- Всё, с чем можно взаимодействовать, реализует протокол Interactable:
  `get_prompt() -> String` и `interact(player)`.
- AutoLoad вводится по мере необходимости (сейчас запланирован только
  SignalHub; Game/SaveSystem — позже, когда появятся сохранения).
- Перед реализацией каждой крупной системы её архитектура сначала
  согласуется с автором проекта.

## 4. Текущая структура проекта
(обновляется по мере создания файлов)
```
res://
├── MEMORY.md          — этот файл
├── README.md
├── project.godot      — Jolt, GL Compatibility, input-карта, main-сцена
├── icon.svg
├── scenes/
│   ├── player/player.tscn        — игрок (CharacterBody3D + Head + Camera3D
│   │                             + InteractRay + Inventory)
│   ├── world/world_prototype.tscn — тестовая локация 60×60 (CSG blockout):
│   │     земля, холм-конус, 10 деревьев-декора, 3 камня, 2 дома с дверными
│   │     проёмами, невидимые стены Bounds, 3 PickupItem (камни, ягоды),
│   │     2 добываемых дерева + 1 камень-жилка, HUD
│   ├── interaction/pickup_item.tscn         — подбираемый предмет
│   ├── interaction/resource_node_tree.tscn  — добываемое дерево (3 удара = 6 дерева)
│   ├── interaction/resource_node_rock.tscn  — камень-жилка (3 удара = 6 камней)
│   ├── ui/hud.tscn    — CanvasLayer: прицел, подсказка, тосты, хотбар, панель инвентаря
│   ├── ui/hotbar.tscn — 5 слотов внизу экрана (белая рамка = активный)
│   ├── ui/inventory_panel.tscn — панель 20 слотов по Tab
│   └── tests/player_test.tscn    — песочница для изолированных тестов
├── resources/items/  — item_data.gd (class_name ItemData) +
│     stone.tres, wood.tres, berry.tres, flask.tres
├── scripts/
│   ├── autoload/signal_hub.gd    — шина сигналов (prompt_changed, notify,
│   │                             inventory_changed)
│   ├── player/player.gd          — FPS-контроллер (class_name Player)
│   ├── player/interact_ray.gd    — луч взаимодействия (class_name InteractRay)
│   ├── player/inventory.gd       — ядро инвентаря, 20 слотов (class_name Inventory)
│   ├── interaction/interactable.gd — базовый протокол (class_name Interactable)
│   ├── interaction/pickup_item.gd  — PickupItem (ItemData + count)
│   ├── interaction/resource_node.gd — ResourceNode (hits_required, yield_per_hit)
│   ├── ui/hud.gd    — HUD (class_name HUD)
│   └── ui/hotbar.gd — Hotbar (5 первых слотов)
```

Input-действия: move_forward/back/left/right (WASD, физ. коды —
работают на любой раскладке), sprint (Shift), jump (Space),
crouch (Ctrl), interact (E).
Main-сцена: scenes/world/world_prototype.tscn (до появления game.tscn).
Локация построена на CSG-примитивах (blockout): заменяется на реальные
модели без изменения логики.
Взаимодействие: RayCast3D (2.5 м) из камеры; объект под прицелом должен
быть наследником Interactable с методами get_prompt()/interact(player).
HUD сообщается с системами через SignalHub.prompt_changed.
Путь к редактору на этой машине: E:/godot/Godot_v4.7.2-stable_win64.exe
GitHub: https://github.com/goverfar-tech/govno-igra (private)

## 5. Целевая структура (по мере роста)
```
scenes/    main, player, world, interaction, ui
scripts/   player, interaction, world, ui, autoload
resources/ items (item_data.gd + .tres)
assets/    временные меши/иконки (placeholder)
docs/      дизайн-заметки
```

## 6. Порядок разработки
- Этап 0 — Вертикальный прототип (см. §7)
- Этап 1 — Осмысленный мир (освещение, окружение, звук)
- Этап 2 — Расширенный survival и AI существ/врагов
- Этап 3 — Крафт/строительство, сохранения, полировка

## 7. Вертикальный прототип — состав и порядок реализации
Каждая система согласуется отдельно, коммитится отдельно.
1. Игрок — FPS-контроллер: ходьба/бег/прыжок/присед, камера, headbob,
   захват мыши.
2. Тестовая локация — земля, препятствия/«дома», солнце, WorldEnvironment.
3. Взаимодействие — RayCast3D из камеры, протокол Interactable,
   HUD-подсказка.
4. Сбор ресурсов — ItemData(.tres), PickupItem, ResourceNode (дерево,
   камень-жилка).
5. Инвентарь — стакающиеся слоты, хотбар (1–5), панель по Tab,
   «инвентарь полон».
6. Базовый survival — HP/сытость/жажда, истощение, еда из инвентаря,
   экран смерти + рестарт.
7. Связка — game.tscn как main-сцена: World + Player + HUD + пауза(Esc).

## 8. Важные технические решения
- GL Compatibility: не использовать фичи без поддержки в этом рендере
  (SDFGI, SSR, объёмный туман — проверять перед применением).
- Docker НЕ используем: одиночная игра, портативный редактор, экспорт
  из редактора. Вернёмся к теме, если появится мультиплеер/сервер/CI.
- Ассеты первое время — placeholder-примитивы и цветные материалы.
- Предметы-ресурсы (.tres): чтобы добавить предмет, создаётся файл,
  а не правится код.

## 9. Текущий статус
- [x] Проект создан (Godot 4.7, Jolt, GL Compatibility, Git)
- [x] MEMORY.md согласован и зафиксирован
- [x] Прототип: (1) игрок — FPS-контроллер + песочница tests/player_test
- [x] Прототип: (2) локация — world_prototype (CSG blockout 60×60)
- [x] Прототип: (3) взаимодействие — InteractRay, HUD-подсказка, SignalHub
- [x] Прототип: (4) сбор ресурсов — ItemData-предметы, PickupItem,
      ResourceNode (дерево/жилка), ядро Inventory, хотбар
- [x] Прототип: (5) инвентарь — панель по Tab, выбор слота 1–5/колёсико,
      ПКМ выбрасывает пачку перед игроком
- [ ] Прототип: (6) survival-статы
- [ ] Прототип: (7) связка / main-сцена
