class_name PickupItem
extends Interactable
## Подбираемый предмет. Пока заглушка: исчезает по E.
## На шаге «ресурсы» получит ItemData и будет класть предмет в инвентарь.

@export var display_name := "Камушек"


func get_prompt() -> String:
	return "[E] Взять: %s" % display_name


func interact(_player: Player) -> void:
	print("Подобрано: %s" % display_name)
	queue_free()
