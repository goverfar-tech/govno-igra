extends Node
## SaveSystem (AutoLoad): один слот сохранения в user://save.json.
## Хранит: игрока (позиция/статы/инвентарь), время суток, состояние мира
## (вырубленные источники, убитые животные, обобранные кусты, подборы).

const SAVE_PATH := "user://save.json"

var _pending_load := false


func has_save() -> bool:
	return FileAccess.file_exists(SAVE_PATH)


func request_load() -> void:
	_pending_load = true


func consume_load_request() -> bool:
	var r := _pending_load
	_pending_load = false
	return r


func save_game() -> void:
	var scene := get_tree().current_scene
	if scene == null or scene.name != "Game":
		return
	var player: Player = get_tree().get_first_node_in_group("player")
	var world_gen: WorldGen = scene.get_node_or_null("WorldPrototype/WorldGen")
	var day_night: DayNight = get_tree().get_first_node_in_group("daynight")
	if player == null or world_gen == null:
		return

	var data := {
		"version": 1,
		"time": day_night.time if day_night else 0.3,
		"player": {
			"pos": [player.global_position.x, player.global_position.y, player.global_position.z],
			"yaw": player.rotation.y,
			"pitch": player.head.rotation.x,
			"health": player.stats.health,
			"hunger": player.stats.hunger,
			"thirst": player.stats.thirst,
		},
		"inventory": player.inventory.serialize(),
		"world": world_gen.get_state(),
	}
	var f := FileAccess.open(SAVE_PATH, FileAccess.WRITE)
	if f:
		f.store_string(JSON.stringify(data))
		f.close()
		SignalHub.notify.emit("Сохранено")


func apply_save() -> void:
	if not has_save():
		return
	var scene := get_tree().current_scene
	if scene == null or scene.name != "Game":
		return
	var f := FileAccess.open(SAVE_PATH, FileAccess.READ)
	var data: Variant = JSON.parse_string(f.get_as_text())
	f.close()
	if not (data is Dictionary):
		return

	var player: Player = get_tree().get_first_node_in_group("player")
	var world_gen: WorldGen = scene.get_node_or_null("WorldPrototype/WorldGen")
	var day_night: DayNight = get_tree().get_first_node_in_group("daynight")

	if day_night:
		day_night.time = clampf(data.get("time", 0.3), 0.0, 0.999)
	if player:
		var pd: Dictionary = data.get("player", {})
		var pos: Array = pd.get("pos", [0.0, 1.0, 0.0])
		player.global_position = Vector3(pos[0], pos[1], pos[2])
		player.rotation.y = pd.get("yaw", 0.0)
		player._pitch = pd.get("pitch", 0.0)
		player.head.rotation.x = player._pitch
		player.stats.set_all(
			pd.get("health", Stats.MAX_VALUE),
			pd.get("hunger", Stats.MAX_VALUE),
			pd.get("thirst", Stats.MAX_VALUE))
		player.inventory.deserialize(data.get("inventory", []))
	if world_gen:
		world_gen.apply_state(data.get("world", {}))
	SignalHub.notify.emit("Сохранение загружено")
