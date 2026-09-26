extends Node
## SignalHub — общая шина сигналов (AutoLoad).
## Сюда добавляем сигналы, по которым общаются системы, не зная друг о друге.

## HUD обновляет подсказку взаимодействия; пустая строка — скрыть.
signal prompt_changed(text: String)

## Всплывающее уведомление внизу экрана («Инвентарь полон» и т.п.).
signal notify(text: String)

## Содержимое инвентаря игрока изменилось (массив слотов-Dictionary).
signal inventory_changed(slots: Array)
