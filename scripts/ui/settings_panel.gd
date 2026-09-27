class_name SettingsPanel
extends Control
## Панель настроек: громкость, чувствительность мыши, FOV.
## «Назад» скрывает панель. Работает и в главном меню, и на паузе.

@onready var _volume: HSlider = %VolumeSlider
@onready var _sens: HSlider = %SensSlider
@onready var _fov: HSlider = %FovSlider


func _ready() -> void:
	visible = false
	_volume.min_value = -40.0
	_volume.max_value = 0.0
	_volume.step = 0.5
	_volume.value = Settings.volume_db
	_volume.value_changed.connect(func(v: float) -> void: Settings.set_volume_db(v))

	_sens.min_value = 0.001
	_sens.max_value = 0.008
	_sens.step = 0.0002
	_sens.value = Settings.mouse_sensitivity
	_sens.value_changed.connect(func(v: float) -> void: Settings.set_mouse_sensitivity(v))

	_fov.min_value = 60.0
	_fov.max_value = 110.0
	_fov.step = 1.0
	_fov.value = Settings.fov
	_fov.value_changed.connect(func(v: float) -> void: Settings.set_fov(v))

	%BackButton.pressed.connect(close)

	if not Settings.is_connected("changed", Callable(self, "_sync")):
		Settings.changed.connect(_sync)


func open() -> void:
	_sync()
	visible = true


func close() -> void:
	visible = false
	Settings.save()


func _sync() -> void:
	if not is_node_ready():
		return
	_volume.value = Settings.volume_db
	_sens.value = Settings.mouse_sensitivity
	_fov.value = Settings.fov
