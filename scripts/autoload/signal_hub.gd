extends Node
## SignalHub — общая шина сигналов (AutoLoad).
## Сюда добавляем сигналы, по которым общаются системы, не зная друг о друге.

## HUD обновляет подсказку взаимодействия; пустая строка — скрыть.
signal prompt_changed(text: String)

## Всплывающее уведомление внизу экрана («Инвентарь полон» и т.п.).
signal notify(text: String)

## Содержимое инвентаря игрока изменилось (массив слотов-Dictionary).
signal inventory_changed(slots: Array)

## Панель инвентаря открылась/закрылась.
signal inventory_open_changed(is_open: bool)

## Изменился активный слот хотбара (0..4).
signal selection_changed(index: int)

## Статы игрока изменились (значения 0..100).
signal stats_changed(health: float, hunger: float, thirst: float)

## Игрок погиб.
signal player_died
