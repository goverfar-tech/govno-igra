class_name Interactable
extends StaticBody3D
## Базовый класс для всего, с чем можно взаимодействовать клавишей E.
## Наследники переопределяют get_prompt() и interact().


## Текст подсказки, который видит игрок, глядя на объект. Пустая строка = нет подсказки.
func get_prompt() -> String:
	return ""


## Вызывается, когда игрок нажал interact, глядя на объект.
func interact(_player: Player) -> void:
	pass
