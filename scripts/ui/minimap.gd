class_name Minimap
extends Control
## Мини-карта: вид сверху на мир через отдельную камеру в SubViewport.
## Камера каждый кадр висит над игроком; игрок — красная точка в центре.

const VIEW_HEIGHT := 60.0

@onready var _camera: Camera3D = %MinimapCamera

var _player: Player


func _ready() -> void:
	_player = get_tree().get_first_node_in_group("player")


func _process(_delta: float) -> void:
	if _player == null or _camera == null:
		return
	# держим камеру строго над игроком, «север» карты = -Z мира
	var p := _player.global_position
	_camera.global_position = p + Vector3(0.0, VIEW_HEIGHT, 0.0)
	_camera.global_rotation = Vector3(-PI * 0.5, 0.0, 0.0)
