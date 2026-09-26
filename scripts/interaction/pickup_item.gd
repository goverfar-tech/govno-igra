class_name PickupItem
extends Interactable
## Подбираемый предмет на земле: по E кладётся в инвентарь игрока.

@export var item: ItemData
@export var count := 1


func _ready() -> void:
	# Цвет-заглушка вместо модели: из icon_color предмета
	if item:
		var mat := StandardMaterial3D.new()
		mat.albedo_color = item.icon_color
		$MeshInstance3D.set_surface_override_material(0, mat)


func get_prompt() -> String:
	var n := item.display_name if item else "Предмет"
	if count > 1:
		return "[E] Взять: %s ×%d" % [n, count]
	return "[E] Взять: %s" % n


func interact(player: Player) -> void:
	if item == null:
		push_error("PickupItem без ItemData: %s" % get_path())
		queue_free()
		return
	if player == null or player.inventory == null:
		return
	var leftover := player.inventory.add_item(item, count)
	if leftover <= 0:
		queue_free()
	else:
		count = leftover
		SignalHub.notify.emit("Инвентарь полон!")
