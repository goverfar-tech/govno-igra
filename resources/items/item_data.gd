class_name ItemData
extends Resource
## Описание предмета. Чтобы добавить предмет — создаём новый .tres,
## код трогать не нужно.

@export var id: StringName
@export var display_name := "Предмет"
## Вместо иконки пока цветной квадрат
@export var icon_color := Color(0.7, 0.7, 0.7)
@export var max_stack := 20
## Съедобное/питьевое: восполняет статы при использовании из инвентаря (шаг 6)
@export var is_edible := false
@export var nutrition := 0.0
@export var hydration := 0.0
## Что остаётся в инвентаре после использования (полная фляга -> пустая)
@export var consume_returns: ItemData
## Инструмент/оружие (замах ЛКМ из хотбара) и урон по животным
@export var is_tool := false
@export var tool_damage := 0.0
