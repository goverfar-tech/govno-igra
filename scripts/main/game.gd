class_name Game
extends Node
## Корневая сцена игры: World + Player + HUD.
## Ставит игрока на точку спауна из мира.


func _ready() -> void:
	var world := $WorldPrototype
	var player := $Player
	var spawn := world.get_node_or_null("PlayerSpawn") as Node3D
	if spawn != null:
		player.global_transform = spawn.global_transform
	# все дети готовы — можно применять сохранение
	if SaveSystem.consume_load_request():
		SaveSystem.apply_save()
