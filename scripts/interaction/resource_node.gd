class_name ResourceNode
extends Interactable
## Добываемый источник (дерево, камень-жилка).
## Несколько ударов по E, каждый приносит ресурс; после последнего — исчезает.

@export var item: ItemData
@export var hits_required: int = 3
## Разрешает ли источник добычу инструментом (топор — дереву, не камню)
@export var tool_allowed := true
@export var yield_per_hit: int = 1

var _hits_done := 0


func get_prompt() -> String:
	var n := item.display_name if item else "Ресурс"
	return "[E] Добыть: %s (%d/%d)" % [n, _hits_done, hits_required]


func interact(player: Player) -> void:
	harvest(1, player)


## Добыча amount ударов за раз (инструмент ускоряет). Общая логика удара.
func harvest(amount: int, player: Player) -> void:
	if item == null:
		push_error("ResourceNode без ItemData: %s" % get_path())
		return
	if player == null or player.inventory == null:
		return
	var leftover := player.inventory.add_item(item, yield_per_hit * amount)
	if leftover >= yield_per_hit * amount:
		SignalHub.notify.emit("Инвентарь полон!")
		return
	_hits_done += amount
	AudioManager.play_hit(global_position)
	if _hits_done >= hits_required:
		queue_free()
