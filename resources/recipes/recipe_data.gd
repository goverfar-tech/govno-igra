class_name RecipeData
extends Resource
## Рецепт крафта: из набора предметов делает другой предмет.

@export var result: ItemData
@export var result_count := 1
## Стоимость: {&"wood": 2, &"stone": 1}
@export var cost: Dictionary = {}
