class_name ResourceNode
extends Interactable
## Добываемый источник (дерево, камень-жилка).
## Несколько ударов по E, каждый приносит ресурс; после последнего — исчезает.

@export var item: ItemData
@export var hits_required: int = 3
@export var yield_per_hit: int = 1

var _hits_done := 0


func get_prompt() -> String:
	var n := item.display_name if item else "Ресурс"
	return "[E] Добыть: %s (%d/%d)" % [n, _hits_done, hits_required]


func interact(player: Player) -> void:
	if item == null:
		push_error("ResourceNode без ItemData: %s" % get_path())
		return
	if player == null or player.inventory == null:
		return
	var leftover := player.inventory.add_item(item, yield_per_hit)
	if leftover == yield_per_hit:
		# Ничего не влезло — удар не засчитываем
		SignalHub.notify.emit("Инвентарь полон!")
		return
	_hits_done += 1
	if _hits_done >= hits_required:
		queue_free()
