class_name Campfire
extends Interactable
## Костёр: E — пожарить сырое мясо (сырое -> жаркое).
## Также даёт тепло: рядом ночью расход голода/жажды не ускоряется.

const RawMeat: ItemData = preload("res://resources/items/meat.tres")
const CookedMeat: ItemData = preload("res://resources/items/cooked_meat.tres")

## Радиус тепла (игрок рядом — ночний множитель расхода не действует)
const WARM_RADIUS := 5.0


func _ready() -> void:
	add_to_group("campfire")


func get_prompt() -> String:
	var player := get_tree().get_first_node_in_group("player") as Player
	if player and player.inventory.has_item(RawMeat):
		return "[E] Пожарить мясо"
	return "Костёр (нужно сырое мясо)"


func interact(player: Player) -> void:
	if player == null:
		return
	if not player.inventory.has_item(RawMeat):
		SignalHub.notify.emit("Нечего жарить — нужно сырое мясо")
		return
	if player.inventory.add_item(CookedMeat, 1) > 0:
		SignalHub.notify.emit("Инвентарь полон!")
		return
	player.inventory.remove_item(RawMeat, 1)
	AudioManager.play_eat()
	SignalHub.notify.emit("Пожарено: Жаркое")
