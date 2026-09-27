class_name WaterSource
extends Interactable
## Родник: E — наполнить пустую флягу (полная фляга -> пустая при питье).

const FlaskFull: ItemData = preload("res://resources/items/flask.tres")
const FlaskEmpty: ItemData = preload("res://resources/items/flask_empty.tres")


func get_prompt() -> String:
	return "[E] Наполнить флягу"


func interact(player: Player) -> void:
	if player == null:
		return
	var inv := player.inventory
	if inv.has_item(FlaskEmpty):
		inv.remove_item(FlaskEmpty, 1)
		inv.add_item(FlaskFull, 1)
		AudioManager.play_pickup()
		SignalHub.notify.emit("Фляга наполнена")
	elif inv.has_item(FlaskFull):
		SignalHub.notify.emit("Фляга уже полная")
	else:
		SignalHub.notify.emit("Нужна пустая фляга")
