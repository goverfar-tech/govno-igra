extends Node
## Settings (AutoLoad): громкость, чувствительность мыши, FOV.
## Хранится в user://settings.cfg; изменения шлём через changed.

signal changed

const PATH := "user://settings.cfg"

var volume_db := 0.0
var mouse_sensitivity := 0.003
var fov := 75.0


func _ready() -> void:
	_load()
	_apply_audio()


func save() -> void:
	var cfg := ConfigFile.new()
	cfg.set_value("audio", "volume_db", volume_db)
	cfg.set_value("input", "mouse_sensitivity", mouse_sensitivity)
	cfg.set_value("video", "fov", fov)
	cfg.save(PATH)


func set_volume_db(v: float) -> void:
	volume_db = v
	_apply_audio()
	changed.emit()


func set_mouse_sensitivity(v: float) -> void:
	mouse_sensitivity = v
	changed.emit()


func set_fov(v: float) -> void:
	fov = v
	changed.emit()


func _apply_audio() -> void:
	AudioServer.set_bus_volume_db(0, volume_db)


func _load() -> void:
	var cfg := ConfigFile.new()
	if cfg.load(PATH) != OK:
		return
	volume_db = cfg.get_value("audio", "volume_db", 0.0)
	mouse_sensitivity = cfg.get_value("input", "mouse_sensitivity", 0.003)
	fov = cfg.get_value("video", "fov", 75.0)
