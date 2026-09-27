class_name DayNight
extends Node3D
## Цикл день/ночь: вращает солнце и луну, тонирует небо и свет от времени.
## time: 0..1, где 0 — полночь, 0.25 — восход, 0.5 — полдень, 0.75 — закат.
## Шлёт SignalHub.time_of_day_changed(time) каждый кадр.

@export var day_length_sec := 300.0          # реальных секунд на игровые сутки
@export var start_time := 0.30               # спаунимся утром

@onready var sun: DirectionalLight3D = $Sun
@onready var moon: DirectionalLight3D = $Moon
@onready var world_env: WorldEnvironment = $WorldEnvironment

var time := 0.3

var _night_notified := false


func _ready() -> void:
	add_to_group("daynight")
	time = start_time
	_update_lights()


func _process(delta: float) -> void:
	time = fmod(time + delta / day_length_sec, 1.0)
	_update_lights()
	SignalHub.time_of_day_changed.emit(time)

	var is_night := time < 0.25 or time > 0.75
	if is_night and not _night_notified:
		_night_notified = true
		SignalHub.notify.emit("Наступила ночь…")
	elif not is_night:
		_night_notified = false


func _update_lights() -> void:
	# Высота светила: -1 (надир) .. +1 (зенит); 0 — горизонт
	var sun_elev := sin((time - 0.25) * TAU)
	sun.rotation.x = -(time - 0.25) * TAU
	moon.rotation.x = -(time - 0.75) * TAU
	var moon_elev := -sun_elev

	# Сумеречный фактор: сильнее ближе к горизонту
	var dusk := clampf(1.0 - absf(sun_elev) * 4.0, 0.0, 1.0)
	var day_f := clampf(sun_elev, 0.0, 1.0)

	sun.visible = sun_elev > -0.05
	sun.light_energy = day_f * 1.2
	sun.light_color = Color(1, 1, 1).lerp(Color(1.0, 0.55, 0.25), dusk)

	moon.visible = moon_elev > 0.0
	moon.light_energy = clampf(moon_elev, 0.0, 1.0) * 0.25
	moon.light_color = Color(0.55, 0.65, 0.9)

	var sky_mat := world_env.environment.sky.sky_material as ProceduralSkyMaterial
	if sky_mat:
		sky_mat.sky_top_color = Color(0.02, 0.03, 0.09).lerp(Color(0.32, 0.45, 0.62), day_f)
		sky_mat.sky_horizon_color = Color(0.05, 0.06, 0.12).lerp(Color(0.62, 0.66, 0.7), day_f).lerp(Color(1.0, 0.45, 0.2), dusk * day_f * 0.5)
		sky_mat.ground_bottom_color = Color(0.01, 0.01, 0.02).lerp(Color(0.22, 0.2, 0.17), day_f)
		sky_mat.ground_horizon_color = Color(0.05, 0.06, 0.12).lerp(Color(0.55, 0.55, 0.55), day_f)

	world_env.environment.ambient_light_energy = lerpf(0.2, 1.0, day_f)
	var env := world_env.environment
	env.fog_light_color = Color(0.03, 0.04, 0.07).lerp(Color(0.58, 0.62, 0.68), day_f).lerp(Color(0.9, 0.5, 0.25), dusk * day_f * 0.5)
