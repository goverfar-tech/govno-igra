class_name BerryBush
extends Interactable
## Куст ягод: одно нажатие E — собрать ягоды; куст увядает и
## восстанавливается через regrow_seconds.

@export var item: ItemData
@export var yield_count := 2
@export var regrow_seconds := 60.0

var _ready_to_pick := true

@onready var _crown: MeshInstance3D = $CrownMesh
var _pale_mat: StandardMaterial3D


func _ready() -> void:
	_pale_mat = StandardMaterial3D.new()
	_pale_mat.albedo_color = Color(0.5, 0.55, 0.45)


func get_prompt() -> String:
	if item == null:
		return ""
	if not _ready_to_pick:
		return "Куст обобран…"
	return "[E] Собрать: %s ×%d" % [item.display_name, yield_count]


func interact(player: Player) -> void:
	if item == null or player == null:
		return
	if not _ready_to_pick:
		SignalHub.notify.emit("Ягоды ещё не созрели")
		return
	var leftover := player.inventory.add_item(item, yield_count)
	if leftover > 0:
		SignalHub.notify.emit("Инвентарь полон!")
		return
	_set_ready(false)
	AudioManager.play_pickup()
	get_tree().create_timer(regrow_seconds).timeout.connect(_regrow)


func _set_ready(value: bool) -> void:
	_ready_to_pick = value
	_crown.material_override = null if value else _pale_mat


func _regrow() -> void:
	_set_ready(true)
